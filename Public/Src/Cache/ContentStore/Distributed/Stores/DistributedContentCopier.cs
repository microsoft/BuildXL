// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Sessions;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
using static BuildXL.Cache.ContentStore.Distributed.Stores.DistributedContentStoreSettings;

namespace BuildXL.Cache.ContentStore.Distributed.Stores
{
    /// <summary>
    /// Handles copies from remote locations to a local store
    /// </summary>
    /// <typeparam name="T">The content locations being stored.</typeparam>
    public class DistributedContentCopier<T> : StartupShutdownSlimBase, IDistributedContentCopier
        where T : PathBase
    {
        // Gate to control the maximum number of simultaneously active active IO operations.
        private readonly SemaphoreSlim _ioGate;

        private readonly IReadOnlyList<TimeSpan> _retryIntervals;
        private readonly DisposableDirectory _tempFolderForCopies;
        private readonly IFileCopier<T> _remoteFileCopier;
        private readonly IFileExistenceChecker<T> _remoteFileExistenceChecker;
        private readonly IPathTransformer<T> _pathTransformer;
        private readonly IContentLocationStore _contentLocationStore;

        private readonly DistributedContentStoreSettings _settings;
        private readonly IAbsFileSystem _fileSystem;
        private readonly Dictionary<HashType, IContentHasher> _hashers;

        private readonly CounterCollection<DistributedContentCopierCounters> _counters = new CounterCollection<DistributedContentCopierCounters>();

        private readonly AbsolutePath _workingDirectory;

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(DistributedContentCopier<T>));

        /// <nodoc />
        public int CurrentIoGateCount => _ioGate.CurrentCount;

        /// <nodoc />
        public DistributedContentCopier(
            AbsolutePath workingDirectory,
            DistributedContentStoreSettings settings,
            IAbsFileSystem fileSystem,
            IFileCopier<T> fileCopier,
            IFileExistenceChecker<T> fileExistenceChecker,
            IPathTransformer<T> pathTransformer,
            IContentLocationStore contentLocationStore)
        {
            Contract.Requires(settings != null);
            Contract.Requires(settings.ParallelHashingFileSizeBoundary >= -1);

            _settings = settings;
            _tempFolderForCopies = new DisposableDirectory(fileSystem, workingDirectory / "Temp");
            _remoteFileCopier = fileCopier;
            _remoteFileExistenceChecker = fileExistenceChecker;
            _contentLocationStore = contentLocationStore;
            _pathTransformer = pathTransformer;
            _fileSystem = fileSystem;

            _workingDirectory = _tempFolderForCopies.Path;

            // TODO: Use hashers from IContentStoreInternal instead?
            _hashers = HashInfoLookup.CreateAll();

            _ioGate = new SemaphoreSlim(_settings.MaxConcurrentCopyOperations);
            _retryIntervals = settings.RetryIntervalForCopies;
        }

        /// <inheritdoc />
        protected override Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            if (_settings.CleanRandomFilesAtRoot)
            {
                foreach (var file in _fileSystem.EnumerateFiles(_workingDirectory.Parent, EnumerateOptions.None))
                {
                    if (IsRandomFile(file.FullPath))
                    {
                        Tracer.Debug(context, $"Deleting random file {file.FullPath} at root.");
                        _fileSystem.DeleteFile(file.FullPath);
                    }
                }
            }

            return base.StartupCoreAsync(context);
        }

        private bool IsRandomFile(AbsolutePath file)
        {
            var fileName = file.GetFileName();
            return fileName.StartsWith("random-", StringComparison.OrdinalIgnoreCase);
        }

        /// <inheritdoc />
        protected override Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            _tempFolderForCopies.Dispose();

            return base.ShutdownCoreAsync(context);
        }

        /// <inheritdoc />
        public async Task<PutResult> TryCopyAndPutAsync(
            OperationContext operationContext,
            ContentHashWithSizeAndLocations hashInfo,
            Func<(CopyFileResult copyResult, AbsolutePath tempLocation, int attemptCount), Task<PutResult>> handleCopyAsync,
            Action<IReadOnlyList<MachineLocation>> handleBadLocations = null)
        {
            var cts = operationContext.Token;

            try
            {
                PutResult putResult = null;
                var badContentLocations = new HashSet<MachineLocation>();
                var missingContentLocations = new HashSet<MachineLocation>();
                int attemptCount = 0;

                while (attemptCount < _retryIntervals.Count && (putResult == null || !putResult))
                {
                    bool retry;

                    (putResult, retry) = await WalkLocationsAndCopyAndPutAsync(
                        operationContext,
                        _workingDirectory,
                        hashInfo,
                        badContentLocations,
                        missingContentLocations,
                        attemptCount,
                        handleCopyAsync);

                    if (putResult || operationContext.Token.IsCancellationRequested)
                    {
                        break;
                    }

                    if (missingContentLocations.Count == hashInfo.Locations.Count)
                    {
                        Tracer.Warning(operationContext, $"{AttemptTracePrefix(attemptCount)} All replicas {hashInfo.Locations.Count} are reported missing. Not retrying for hash {hashInfo.ContentHash}.");
                        break;
                    }

                    if (!retry)
                    {
                        Tracer.Warning(operationContext, $"{AttemptTracePrefix(attemptCount)} Cannot place {hashInfo.ContentHash} due to error: {putResult.ErrorMessage}. Not retrying for hash {hashInfo.ContentHash}.");
                        break;
                    }

                    if (attemptCount == _retryIntervals.Count - 1)
                    {
                        // This is the last attempt, no need to wait any more.
                        break;
                    }

                    TimeSpan waitDelay = _retryIntervals[attemptCount];
                    Tracer.Warning(operationContext, $"{AttemptTracePrefix(attemptCount)} All replicas {hashInfo.Locations.Count} failed. Retrying for hash {hashInfo.ContentHash}...");

                    attemptCount++;

                    await Task.Delay(waitDelay, cts);
                }

                // now that retries are exhausted, combine the missing and bad locations.
                badContentLocations.UnionWith(missingContentLocations);

                if (badContentLocations.Any())
                {
                    // This will go away when LLS is the only content location store
                    handleBadLocations?.Invoke(badContentLocations.ToList());
                }

                if (!putResult.Succeeded)
                {
                    traceCopyFailed(operationContext);
                }
                else
                {
                    Tracer.TrackMetric(operationContext, "RemoteBytesCount", putResult.ContentSize);
                    _counters[DistributedContentCopierCounters.RemoteBytes].Add(putResult.ContentSize);
                    _counters[DistributedContentCopierCounters.RemoteFilesCopied].Increment();
                }

                return putResult;
            }
            catch (Exception ex)
            {
                traceCopyFailed(operationContext);

                if (cts.IsCancellationRequested)
                {
                    return CreateCanceledPutResult();
                }

                return new ErrorResult(ex).AsResult<PutResult>();
            }

            void traceCopyFailed(Context c)
            {
                Tracer.TrackMetric(c, "RemoteCopyFileFailed", 1);
                _counters[DistributedContentCopierCounters.RemoteFilesFailedCopy].Increment();
            }
        }

        private PutResult CreateCanceledPutResult() => new ErrorResult("The operation was canceled").AsResult<PutResult>();

        /// <nodoc />
        private async Task<(PutResult result, bool retry)> WalkLocationsAndCopyAndPutAsync(
            OperationContext context,
            AbsolutePath workingFolder,
            ContentHashWithSizeAndLocations hashInfo,
            HashSet<MachineLocation> badContentLocations,
            HashSet<MachineLocation> missingContentLocations,
            int attemptCount,
            Func<(CopyFileResult copyResult, AbsolutePath tempLocation, int attemptCount), Task<PutResult>> handleCopyAsync)
        {
            var cts = context.Token;

            // before each retry, clear the list of bad locations so we can retry them all.
            // this helps isolate transient network errors.
            badContentLocations.Clear();
            string lastErrorMessage = null;

            foreach (MachineLocation location in hashInfo.Locations)
            {
                // if the file is explicitly reported missing by the remote, don't bother retrying.
                if (missingContentLocations.Contains(location))
                {
                    continue;
                }

                var sourcePath = _pathTransformer.GeneratePath(hashInfo.ContentHash, location.Data);

                var tempLocation = AbsolutePath.CreateRandomFileName(workingFolder);

                (PutResult result, bool retry) reportCancellationRequested()
                {
                    Tracer.Debug(
                        context,
                        $"{AttemptTracePrefix(attemptCount)} Could not copy file with hash {hashInfo.ContentHash} to temp path {tempLocation} because cancellation was requested.");
                    return (result: CreateCanceledPutResult(), retry: false);
                }

                // Both Puts will attempt to Move the file into the cache. If the Put is successful, then the temporary file
                // does not need to be deleted. If anything else goes wrong, then the temporary file must be removed.
                bool deleteTempFile = true;

                try
                {
                    if (cts.IsCancellationRequested)
                    {
                        return reportCancellationRequested();
                    }

                    // Gate entrance to both the copy logic and the logging which surrounds it
                    CopyFileResult copyFileResult = null;
                    try
                    {
                        copyFileResult = await GatedIoOperationAsync(ts => context.PerformOperationAsync(
                            Tracer,
                            async () =>
                            {
                                return await TaskUtilities.AwaitWithProgressReporting(
                                    task: CopyFileAsync(context, sourcePath, tempLocation, hashInfo, cts),
                                    period: TimeSpan.FromMinutes(5),
                                    action: timeSpan => Tracer.Debug(context, $"{Tracer.Name}.RemoteCopyFile from[{location}]) via stream in progress {(int)timeSpan.TotalSeconds}s."),
                                    reportImmediately: false,
                                    reportAtEnd: false);
                            },
                            traceOperationStarted: false,
                            traceOperationFinished: true,
                            // _ioGate.CurrentCount returns the number of free slots, but we need to print the number of occupied slots instead.
                            extraEndMessage: (result) =>
                                $"contentHash=[{hashInfo.ContentHash}] " +
                                $"from=[{sourcePath}] " +
                                $"size=[{result.Size ?? hashInfo.Size}] " +
                                $"trusted={_settings.UseTrustedHash} " +
                                (result.TimeSpentHashing.HasValue ? $"timeSpentHashing={result.TimeSpentHashing.Value.TotalMilliseconds}ms " : string.Empty) +
                                $"IOGate.OccupiedCount={_settings.MaxConcurrentCopyOperations - _ioGate.CurrentCount} " +
                                $"IOGate.Wait={ts.TotalMilliseconds}ms.",
                            caller: "RemoteCopyFile",
                            counter: _counters[DistributedContentCopierCounters.RemoteCopyFile]), cts);

                        if (copyFileResult.TimeSpentHashing.HasValue)
                        {
                            Tracer.TrackMetric(context, "CopyHashingTimeMs", (long)copyFileResult.TimeSpentHashing.Value.TotalMilliseconds);
                        }
                    }
                    catch (Exception e) when (e is OperationCanceledException)
                    {
                        // Handles both OperationCanceledException and TaskCanceledException (TaskCanceledException derives from OperationCanceledException)
                        return reportCancellationRequested();
                    }

                    if (cts.IsCancellationRequested)
                    {
                        return reportCancellationRequested();
                    }

                    if (copyFileResult != null)
                    {
                        switch (copyFileResult.Code)
                        {
                            case CopyFileResult.ResultCode.Success:
                                _contentLocationStore.ReportReputation(location, MachineReputation.Good);
                                break;
                            case CopyFileResult.ResultCode.FileNotFoundError:
                                lastErrorMessage = $"Could not copy file with hash {hashInfo.ContentHash} from path {sourcePath} to path {tempLocation} due to an error with the sourcepath: {copyFileResult}";
                                Tracer.Warning(
                                    context,
                                    $"{AttemptTracePrefix(attemptCount)} {lastErrorMessage} Trying another replica.");
                                missingContentLocations.Add(location);
                                _contentLocationStore.ReportReputation(location, MachineReputation.Missing);
                                break;
                            case CopyFileResult.ResultCode.SourcePathError:
                                lastErrorMessage = $"Could not copy file with hash {hashInfo.ContentHash} from path {sourcePath} to path {tempLocation} due to an error with the sourcepath: {copyFileResult}";
                                Tracer.Warning(
                                    context,
                                    $"{AttemptTracePrefix(attemptCount)} {lastErrorMessage} Trying another replica.");
                                _contentLocationStore.ReportReputation(location, MachineReputation.Bad);
                                badContentLocations.Add(location);
                                break;
                            case CopyFileResult.ResultCode.DestinationPathError:
                                lastErrorMessage = $"Could not copy file with hash {hashInfo.ContentHash} from path {sourcePath} to temp path {tempLocation} due to an error with the destination path: {copyFileResult}";
                                Tracer.Warning(
                                    context,
                                    $"{AttemptTracePrefix(attemptCount)} {lastErrorMessage} Not trying another replica.");
                                return (result: new ErrorResult(copyFileResult).AsResult<PutResult>(), retry: true);
                            case CopyFileResult.ResultCode.CopyTimeoutError:
                                lastErrorMessage = $"Could not copy file with hash {hashInfo.ContentHash} from path {sourcePath} to path {tempLocation} due to copy timeout: {copyFileResult}";
                                Tracer.Warning(
                                    context,
                                    $"{AttemptTracePrefix(attemptCount)} {lastErrorMessage} Trying another replica.");
                                _contentLocationStore.ReportReputation(location, MachineReputation.Timeout);
                                break;
                            case CopyFileResult.ResultCode.CopyBandwidthTimeoutError:
                                lastErrorMessage = $"Could not copy file with hash {hashInfo.ContentHash} from path {sourcePath} to path {tempLocation} due to insufficient bandwidth timeout: {copyFileResult}";
                                Tracer.Warning(
                                    context,
                                    $"{AttemptTracePrefix(attemptCount)} {lastErrorMessage} Trying another replica.");
                                _contentLocationStore.ReportReputation(location, MachineReputation.Timeout);
                                break;
                            case CopyFileResult.ResultCode.InvalidHash:
                                lastErrorMessage = $"Could not copy file with hash {hashInfo.ContentHash} from path {sourcePath} to path {tempLocation} due to invalid hash: {copyFileResult}";
                                Tracer.Warning(
                                    context,
                                    $"{AttemptTracePrefix(attemptCount)} {lastErrorMessage} {copyFileResult}");
                                break;
                            case CopyFileResult.ResultCode.Unknown:
                                lastErrorMessage = $"Could not copy file with hash {hashInfo.ContentHash} from path {sourcePath} to temp path {tempLocation} due to an internal error: {copyFileResult}";
                                Tracer.Warning(
                                    context,
                                    $"{AttemptTracePrefix(attemptCount)} {lastErrorMessage} Not trying another replica.");
                                _contentLocationStore.ReportReputation(location, MachineReputation.Bad);
                                break;
                            default:
                                lastErrorMessage = $"File copier result code {copyFileResult.Code} is not recognized";
                                return (result: new ErrorResult(copyFileResult, $"{AttemptTracePrefix(attemptCount)} {lastErrorMessage}").AsResult<PutResult>(), retry: true);
                        }

                        if (copyFileResult.Succeeded)
                        {
                            // The copy succeeded, but it is possible that the resulting size doesn't match an expected one.
                            if (hashInfo.Size != -1 && copyFileResult.Size != null && hashInfo.Size != copyFileResult.Size.Value)
                            {
                                lastErrorMessage =
                                    $"Contenthash {hashInfo.ContentHash} at location {location} has content size {copyFileResult.Size.Value} mismatch from {hashInfo.Size}";
                                Tracer.Warning(
                                    context,
                                    $"{AttemptTracePrefix(attemptCount)} {lastErrorMessage} Trying another replica.");
                                // Not tracking the source as a machine with bad reputation, because it is possible that we provided the wrong size.

                                continue;
                            }

                            PutResult putResult = await handleCopyAsync((copyFileResult, tempLocation, attemptCount));

                            if (putResult.Succeeded)
                            {
                                // The put succeeded, but this doesn't necessarily mean that we put the content we intended. Check the content hash
                                // to ensure it's what is expected. This should only go wrong for a small portion of non-trusted puts.
                                if (putResult.ContentHash != hashInfo.ContentHash)
                                {
                                    lastErrorMessage =
                                        $"Contenthash at location {location} has contenthash {putResult.ContentHash} mismatch from {hashInfo.ContentHash}";
                                    // If PutFileAsync re-hashed the file, then it could have found a content hash which differs from the expected content hash.
                                    // If this happens, we should fail this copy and move to the next location.
                                    Tracer.Warning(
                                        context,
                                        $"{AttemptTracePrefix(attemptCount)} {lastErrorMessage}");
                                    badContentLocations.Add(location);
                                    continue;
                                }

                                // Don't delete the temporary file! It no longer exists after the Put moved it into the cache
                                deleteTempFile = false;

                                // Successful case
                                return (result: putResult, retry: false);
                            }
                            else if (putResult.IsCancelled)
                            {
                                return reportCancellationRequested();
                            }
                            else
                            {
                                // Nothing is known about the put's failure. Give up on all locations, do not retry.
                                // An example of a failure requiring this: Failed to reserve space for content
                                var errorMessage = $"Put file for content hash {hashInfo.ContentHash} failed with error {putResult.ErrorMessage} ";
                                Tracer.Warning(
                                    context,
                                    $"{AttemptTracePrefix(attemptCount)} {errorMessage} diagnostics {putResult.Diagnostics}");
                                return (result: putResult, retry: false);
                            }
                        }
                    }
                }
                finally
                {
                    if (deleteTempFile)
                    {
                        _fileSystem.DeleteFile(tempLocation);
                    }
                }
            }

            if (lastErrorMessage != null)
            {
                lastErrorMessage = ". " + lastErrorMessage;
            }

            return (new PutResult(hashInfo.ContentHash, $"Unable to copy file{lastErrorMessage}"), retry: true);
        }

        private async Task<CopyFileResult> CopyFileAsync(
            Context context,
            T location,
            AbsolutePath tempDestinationPath,
            ContentHashWithSizeAndLocations hashInfo,
            CancellationToken cts)
        {
            try
            {
                // Only use trusted hash for files greater than _trustedHashFileSizeBoundary. Over a few weeks of data collection, smaller files appear to copy and put faster using the untrusted variant.
                if (_settings.UseTrustedHash && hashInfo.Size >= _settings.TrustedHashFileSizeBoundary)
                {
                    // If we know that the file is large, then hash concurrently from the start
                    bool hashEntireFileConcurrently = _settings.ParallelHashingFileSizeBoundary >= 0 && hashInfo.Size > _settings.ParallelHashingFileSizeBoundary;

                    int bufferSize = GetBufferSize(hashInfo);

                    // Since this is the only place where we hash the file during trusted copies, we attempt to get access to the bytes here,
                    //  to avoid an additional IO operation later. In case that the file is bigger than the ContentLocationStore permits or blobs
                    //  aren't supported, disposing the FileStream twice does not throw or cause issues.
                    using (Stream fileStream = await _fileSystem.OpenAsync(tempDestinationPath, FileAccess.Write, FileMode.Create, FileShare.Read | FileShare.Delete, FileOptions.SequentialScan, bufferSize))
                    using (Stream possiblyRecordingStream = _contentLocationStore.AreBlobsSupported && hashInfo.Size <= _contentLocationStore.MaxBlobSize && hashInfo.Size >= 0 ? (Stream)new RecordingStream(fileStream, hashInfo.Size) : fileStream)
                    using (HashingStream hashingStream = _hashers[hashInfo.ContentHash.HashType].CreateWriteHashingStream(possiblyRecordingStream, hashEntireFileConcurrently ? 1 : _settings.ParallelHashingFileSizeBoundary))
                    {
                        var copyFileResult = await _remoteFileCopier.CopyToAsync(location, hashingStream, hashInfo.Size, cts);
                        copyFileResult.TimeSpentHashing = hashingStream.TimeSpentHashing;

                        if (copyFileResult.Succeeded)
                        {
                            var foundHash = hashingStream.GetContentHash();
                            if (foundHash != hashInfo.ContentHash)
                            {
                                return new CopyFileResult(CopyFileResult.ResultCode.InvalidHash, $"{nameof(CopyFileAsync)} unsuccessful with different hash. Found {foundHash}, expected {hashInfo.ContentHash}. Found size {hashingStream.Length}, expected size {hashInfo.Size}.");
                            }

                            // Expose the bytes that were copied, so that small files can be put into the ContentLocationStore even when trusted copy is done
                            if (possiblyRecordingStream is RecordingStream recordingStream)
                            {
                                copyFileResult.BytesFromTrustedCopy = recordingStream.RecordedBytes;
                            }

                            return copyFileResult;
                        }
                        else
                        {
                            // This result will be logged in the caller
                            return copyFileResult;
                        }
                    }
                }
                else
                {
                    return await _remoteFileCopier.CopyFileAsync(location, tempDestinationPath, hashInfo.Size, overwrite: true, cancellationToken: cts);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is IOException)
            {
                // Auth errors are considered errors with the destination.
                // Since FSServer now returns HTTP based exceptions for web, IO failures must be local file paths.
                return new CopyFileResult(CopyFileResult.ResultCode.DestinationPathError, ex, ex.ToString());
            }
            catch (Exception ex)
            {
                // any other exceptions are assumed to be bad remote files.
                return new CopyFileResult(CopyFileResult.ResultCode.SourcePathError, ex, ex.ToString());
            }
        }

        private static int GetBufferSize(ContentHashWithSizeAndLocations hashInfo)
        {
            // For "small" files we use "small buffer size"
            // For files in [small, large) range we use file.Size
            // For "large" files we use "large buffer size".

            var size = hashInfo.Size;
            if (size <= DefaultSmallBufferSize)
            {
                return DefaultSmallBufferSize;
            }
            else if (size >= DefaultLargeBufferSize)
            {
                return DefaultLargeBufferSize;
            }
            else
            {
                return (int)size;
            }
        }

        private string AttemptTracePrefix(int attemptCount)
        {
            return $"Attempt #{attemptCount}:";
        }

        // This class is used in this type to pass results out of the VerifyRemote method.
        internal class VerifyResult
        {
            public ContentHash Hash { get; set; }

            public IReadOnlyList<MachineLocation> Present { get; set; }

            public IReadOnlyList<MachineLocation> Absent { get; set; }

            public IReadOnlyList<MachineLocation> Unknown { get; set; }
        }

        // Given a content record set, check all the locations and determine, for each location, whether the file is actually
        // present, actually absent, or if its presence or absence cannot be determined in the alloted time.
        // The CheckFileExistsAsync method that is called in this implementation may be doing more complicated stuff (retries, queuing,
        // throttling, its own timeout) than we want or expect; we should dig into this.
        internal async Task<VerifyResult> VerifyAsync(Context context, ContentHashWithSizeAndLocations remote, CancellationToken cancel)
        {
            Contract.Requires(remote != null);

            Task<FileExistenceResult>[] verifications = new Task<FileExistenceResult>[remote.Locations.Count];
            using (var timeoutCancelSource = CancellationTokenSource.CreateLinkedTokenSource(cancel))
            {
                for (int i = 0; i < verifications.Length; i++)
                {
                    T location = _pathTransformer.GeneratePath(remote.ContentHash, remote.Locations[i].Data);
                    var verification = Task.Run(async () => await GatedCheckFileExistenceAsync(location, timeoutCancelSource.Token));
                    verifications[i] = verification;
                }

                // Spend up to the timeout doing as many verification as we can.
                timeoutCancelSource.CancelAfter(VerifyTimeout);

                // In order to await the end of the verifications and still not throw an exception if verification were canceled (or faulted internally), we
                // use the trick of awaiting a WhenAny, which never throws but instead always runs to completion when the argument tasks complete.
#pragma warning disable EPC13 // Suspiciously unobserved result.
                await Task.WhenAny(Task.WhenAll(verifications));
#pragma warning restore EPC13 // Suspiciously unobserved result.
            }

            // Read out the results of the file existence checks
            var present = new List<MachineLocation>();
            var absent = new List<MachineLocation>();
            var unknown = new List<MachineLocation>();
            for (int i = 0; i < verifications.Length; i++)
            {
                var location = remote.Locations[i];
                Task<FileExistenceResult> verification = verifications[i];
                Contract.Assert(verification.IsCompleted);
                if (verification.IsCanceled && !cancel.IsCancellationRequested)
                {
                    Tracer.Info(context, $"During verification, hash {remote.ContentHash} timed out for location {location}.");
                    unknown.Add(location);
                }
                else if (verification.IsFaulted)
                {
                    Tracer.Info(context, $"During verification, hash {remote.ContentHash} encountered the error {verification.Exception} while verifying location {location}.");
                    unknown.Add(location);
                }
                else
                {
                    FileExistenceResult result = await verification;
                    if (result.Code == FileExistenceResult.ResultCode.FileExists)
                    {
                        present.Add(location);
                    }
                    else if (result.Code == FileExistenceResult.ResultCode.FileNotFound)
                    {
                        Tracer.Info(context, $"During verification, hash {remote.ContentHash} was not found at location {location}.");
                        absent.Add(location);
                    }
                    else
                    {
                        unknown.Add(location);
                    }
                }
            }

            return new VerifyResult() { Hash = remote.ContentHash, Present = present, Absent = absent, Unknown = unknown };
        }

        /// <summary>
        /// This gated method attempts to limit the number of simultaneous off-machine file IO.
        /// It's not clear whether this is really a good idea, since usually IO thread management is best left to the scheduler.
        /// But if there is a truly enormous amount of external IO, it's not clear that they will all be scheduled in a way
        /// that will minimize timeouts, so we will try this gate.
        /// </summary>
        private Task<FileExistenceResult> GatedCheckFileExistenceAsync(T path, CancellationToken token)
        {
            return GatedIoOperationAsync(
                (_) => _remoteFileExistenceChecker.CheckFileExistsAsync(path, Timeout.InfiniteTimeSpan, token),
                token);
        }

        /// <summary>
        /// This gated method attempts to limit the number of simultaneous off-machine file IO.
        /// </summary>
        private async Task<TResult> GatedIoOperationAsync<TResult>(Func<TimeSpan, Task<TResult>> func, CancellationToken token)
        {
            var sw = Stopwatch.StartNew();
            await _ioGate.WaitAsync(token);

            try
            {
                return await func(sw.Elapsed);
            }
            finally
            {
                _ioGate.Release();
            }
        }

        /// <nodoc />
        public CounterSet GetCounters() => _counters.ToCounterSet();

        private enum DistributedContentCopierCounters
        {
            /// <nodoc />
            [CounterType(CounterType.Stopwatch)]
            RemoteCopyFile,

            /// <nodoc />
            RemoteBytes,

            /// <nodoc />
            RemoteFilesCopied,

            /// <nodoc />
            RemoteFilesFailedCopy
        }
    }
}
