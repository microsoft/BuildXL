// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache.CopyScheduling;
using BuildXL.Cache.ContentStore.Distributed.Sessions;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
using ContentStore.Grpc;
using static BuildXL.Cache.ContentStore.Distributed.Stores.DistributedContentStoreSettings;
using static BuildXL.Utilities.ConfigurationHelper;

#nullable enable
namespace BuildXL.Cache.ContentStore.Distributed.Stores
{
    /// <summary>
    /// Handles copies from remote locations to a local store
    /// </summary>
    public class DistributedContentCopier : StartupShutdownSlimBase
    {
        private readonly IReadOnlyList<TimeSpan> _retryIntervals;
        private readonly int _maxRetryCount;
        private readonly IRemoteFileCopier _remoteFileCopier;
        private readonly IContentCommunicationManager _copyRequester;
        private readonly IClock _clock;

        private readonly DistributedContentStoreSettings _settings;

        private readonly ICopyScheduler _copyScheduler;

        /// <inheritdoc />
        public IAbsFileSystem FileSystem { get; }

        private readonly CounterCollection<DistributedContentCopierCounters> _counters = new CounterCollection<DistributedContentCopierCounters>();

        protected override Tracer Tracer { get; } = new Tracer(nameof(DistributedContentCopier));

        /// <summary>
        /// Unfortunately, tests do not use this component very cleanly, so it may be started-up and shutdown multiple times
        /// </summary>
        public override bool AllowMultipleStartupAndShutdowns { get; } = true;

        /// <nodoc />
        public DistributedContentCopier(
            DistributedContentStoreSettings settings,
            IAbsFileSystem fileSystem,
            IRemoteFileCopier fileCopier,
            IContentCommunicationManager copyRequester,
            IClock clock,
            ILogger logger)
        {
            Contract.Requires(settings != null);
            Contract.Requires(settings.ParallelHashingFileSizeBoundary >= -1);

            _settings = settings;
            _remoteFileCopier = fileCopier;
            _copyRequester = copyRequester;
            FileSystem = fileSystem;
            _clock = clock;

            var context = new Context(logger);
            _copyScheduler = settings.CopyScheduler.Create(context);

            _retryIntervals = settings.RetryIntervalForCopies;
            _maxRetryCount = settings.MaxRetryCount;
        }

        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            if (_copyScheduler is IStartupShutdownSlim slimBase)
            {
                await slimBase.StartupAsync(context).ThrowIfFailure();
            }

            return BoolResult.Success;
        }

        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            if (_copyScheduler is IStartupShutdownSlim slimBase)
            {
                await slimBase.ShutdownAsync(context).ThrowIfFailure();
            }

            return BoolResult.Success;
        }

        public record CopyRequest(
            IDistributedContentCopierHost Host,
            ContentHashWithSizeAndLocations HashInfo,
            CopyReason Reason,
            Func<(CopyFileResult copyResult, AbsolutePath tempLocation, int attemptCount), Task<PutResult>> HandleCopyAsync,
            CopyCompression CopyCompression);

        /// <inheritdoc />
        public async Task<PutResult> TryCopyAndPutAsync(
            OperationContext context,
            CopyRequest request)
        {
            Contract.Requires(request.HashInfo.Locations != null);
            var hashInfo = request.HashInfo;
            var cts = context.Token;

            try
            {
                PutResult? putResult = null;
                var badContentLocations = new HashSet<MachineLocation>();
                var missingContentLocations = new HashSet<MachineLocation>();
                var lastFailureTimes = new DateTime[hashInfo.Locations.Count];
                int attemptCount = 0;
                int totalRetries = 0;
                TimeSpan waitDelay = TimeSpan.Zero;

                //DateTime defaults to 01/01/0001 when we initialize the array.
                //This forloop initializes each element to the current time relative to the passed clock instance
                //We use the time from a clock instance in case future tests try to simulate the progression of time.
                for (int index = 0; index < lastFailureTimes.Length; index++)
                {
                    lastFailureTimes[index] = _clock.UtcNow;
                }

                // _retryIntervals controls how many cycles we go through of copying from a list of locations
                // It also has the increasing wait times between cycles
                while (attemptCount < _retryIntervals.Count && (putResult == null || !putResult))
                {
                    bool retry;

                    // Limit the number of replicas that we will go though if this is one of the first n restricted attempts.
                    var maxReplicaCount = attemptCount < _settings.CopyAttemptsWithRestrictedReplicas
                        ? _settings.RestrictedCopyReplicaCount
                        : int.MaxValue;
                    maxReplicaCount = Math.Min(maxReplicaCount, hashInfo.Locations.Count);

                    (putResult, retry) = await WalkLocationsAndCopyAndPutAsync(
                        context,
                        request,
                        badContentLocations,
                        missingContentLocations,
                        lastFailureTimes,
                        attemptCount,
                        waitDelay,
                        maxReplicaCount,
                        totalRetries);

                    totalRetries += maxReplicaCount;
                    if (putResult || cts.IsCancellationRequested)
                    {
                        break;
                    }

                    Contract.AssertNotNull(hashInfo.Locations);
                    if (missingContentLocations.Count == hashInfo.Locations.Count)
                    {
                        Tracer.Warning(context, $"{AttemptTracePrefix(attemptCount)} All replicas {hashInfo.Locations.Count} are reported missing. Not retrying for hash {hashInfo.ContentHash.ToShortString()}.");
                        break;
                    }

                    if (!retry)
                    {
                        Tracer.Warning(context, $"{AttemptTracePrefix(attemptCount)} Cannot place {hashInfo.ContentHash.ToShortString()} due to error: {putResult.ErrorMessage}. Not retrying for hash {hashInfo.ContentHash.ToShortString()}.");
                        break;
                    }

                    attemptCount++;

                    if (attemptCount < _retryIntervals.Count)
                    {
                        long waitTicks = _retryIntervals[attemptCount].Ticks;

                        // Every location uses the same waitDelay per cycle
                        // Randomize the wait delay to `[0.5 * delay, 1.5 * delay)`
                        waitDelay = TimeSpan.FromTicks((long)((waitTicks / 2) + (waitTicks * ThreadSafeRandom.Generator.NextDouble())));

                        // Log with the original attempt count
                        // Trace time remaining under trying to copy the first location of the next attempt.
                        TimeSpan waitedTime = _clock.UtcNow - lastFailureTimes[0];
                        Tracer.Warning(context, $"{AttemptTracePrefix(attemptCount - 1)} All replicas {hashInfo.Locations.Count} failed. Retrying for hash {hashInfo.ContentHash.ToShortString()} in { (waitedTime < waitDelay ? (waitDelay - waitedTime).TotalMilliseconds : 0)}ms...");
                    }
                    else
                    {
                        break;
                    }
                }

                // now that retries are exhausted, combine the missing and bad locations.
                badContentLocations.UnionWith(missingContentLocations);

                Contract.Assert(putResult != null);

                if (!putResult.Succeeded)
                {
                    traceCopyFailed(context);
                }
                else
                {
                    Tracer.TrackMetric(context, "RemoteBytesCount", putResult.ContentSize);
                    _counters[DistributedContentCopierCounters.RemoteBytes].Add(putResult.ContentSize);
                    _counters[DistributedContentCopierCounters.RemoteFilesCopied].Increment();

                    CacheActivityTracker.Increment(CaSaaSActivityTrackingCounters.RemoteCopyFiles);
                }

                return putResult;
            }
            catch (Exception ex)
            {
                traceCopyFailed(context);

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

        /// <summary>
        /// Requests another machine to copy from the current machine.
        /// </summary>
        public Task<BoolResult> RequestCopyFileAsync(OperationContext context, ContentHashWithSize hash, MachineLocation targetLocation, bool isInsideRing, int attempt)
        {
            var options = GetCopyOptions(attempt);
            return PerformProactiveCopyAsync(
                context,
                innerContext => _copyRequester.RequestCopyFileAsync(innerContext, hash, targetLocation),
                hash,
                targetLocation,
                isInsideRing,
                CopyReason.None,
                ProactiveCopyLocationSource.None,
                attempt,
                result => CopyResultCode.Unknown);
        }

        private Task<TResult> PerformProactiveCopyAsync<TResult>(
            OperationContext context,
            Func<OperationContext, Task<TResult>> func,
            ContentHashWithSize hash,
            MachineLocation targetLocation,
            bool isInsideRing,
            CopyReason reason,
            ProactiveCopyLocationSource source,
            int attempt,
            Func<TResult, CopyResultCode> statusFunc) where TResult : ResultBase
        {
            CopySchedulingSummary? copySchedulingSummary = null;
            var ioGateTimedOut = true;

            return context.PerformOperationAsync(
                Tracer,
                operation: async () =>
                {
                    var schedulerResult = await _copyScheduler.ScheduleOutboundPushAsync(new OutboundPushCopy<TResult>(
                        Context: context,
                        Reason: reason,
                        Attempt: attempt,
                        LocationSource: source,
                        PerformOperationAsync: async (arguments) =>
                        {
                            copySchedulingSummary = arguments.Summary;
                            return await func(arguments.Context);
                        }));

                    if (!schedulerResult.Succeeded)
                    {
                        if (schedulerResult.Reason == SchedulerFailureCode.Timeout)
                        {
                            ioGateTimedOut = true;
                        }

                        throw new ResultPropagationException(schedulerResult);
                    }
                    else
                    {
                        ioGateTimedOut = false;

                        return schedulerResult.Value!;
                    }
                },
                traceOperationStarted: false,
                extraEndMessage: result =>
                {
                    var sizeString = $"Size=[{hash.Size}] ";
                    var headerResponseTimeString = result is PushFileResult pushResult && pushResult.HeaderResponseTime.HasValue ? $"HeaderResponseTime={pushResult.HeaderResponseTime} " : string.Empty;
                    var minBandwidthSpeedString = result is ICopyResult copyResult && copyResult.MinimumSpeedInMbPerSec.HasValue ? $"minBandwidthSpeed={copyResult.MinimumSpeedInMbPerSec.Value}MiB/s " : string.Empty;
                    return
                        $"Code={statusFunc(result)} " +
                        $"ContentHash={hash.Hash.ToShortString()} " +
                        $"TargetLocation=[{targetLocation}] " +
                        $"InsideRing={isInsideRing} " +
                        $"CopyReason={reason} " +
                        $"Attempt={attempt} " +
                        $"LocationSource={source} " +
                        (copySchedulingSummary is null ? string.Empty : $"{copySchedulingSummary} ") +
                        $"Scheduler.TimedOut={ioGateTimedOut} " +
                        sizeString +
                        headerResponseTimeString +
                        minBandwidthSpeedString;
                });
        }

        /// <summary>
        /// Pushes content to another machine.
        /// </summary>
        public Task<PushFileResult> PushFileAsync(
            OperationContext context,
            ContentHashWithSize hashWithSize,
            MachineLocation targetLocation,
            Stream stream,
            bool isInsideRing,
            CopyReason reason,
            ProactiveCopyLocationSource source,
            int attempt)
        {
            var options = GetCopyOptions(attempt);
            return PerformProactiveCopyAsync(
                context,
                innerContext => _copyRequester.PushFileAsync(innerContext, hashWithSize, stream, targetLocation, options),
                hashWithSize,
                targetLocation,
                isInsideRing,
                reason,
                source,
                attempt,
                result => result.Status);
        }

        private CopyOptions GetCopyOptions(int attempt, int currentRetryNumber = 0, CopyRequest? request = null)
        {
            // Using more optimal bandwidth configuration only for the first half of the attempts.
            // This is needed for the cases when the hash is popular and we never reach the second attempt.
            // In this case, we'll be using a more aggressive configuration for the first 16 (by default) locations.
            if (currentRetryNumber > (_maxRetryCount / 2) - 1)
            {
                // if the index is negative, ElementAtOrDefault returns the default value.
                attempt = -1;
            }

            var bandwidthConfig = _settings.BandwidthConfigurations?.ElementAtOrDefault(attempt);

            var copyOptions = new CopyOptions(bandwidthConfig);
            if (request != null)
            {
                copyOptions.CompressionHint = request.CopyCompression;
            }

            return copyOptions;
        }

        private PutResult CreateCanceledPutResult() => new ErrorResult("The operation was canceled").AsResult<PutResult>();
        private PutResult CreateMaxRetryPutResult() => new ErrorResult($"Maximum total retries of {_maxRetryCount} attempted").AsResult<PutResult>();

        /// <nodoc />
        private async Task<(PutResult result, bool retry)> WalkLocationsAndCopyAndPutAsync(
            OperationContext context,
            CopyRequest request,
            HashSet<MachineLocation> badContentLocations,
            HashSet<MachineLocation> missingContentLocations,
            DateTime[] lastFailureTimes,
            int attemptCount,
            TimeSpan waitDelay,
            int maxReplicaCount,
            int totalRetries)
        {
            Contract.Requires(request.HashInfo.Locations != null);
            var workingFolder = request.Host.WorkingFolder;
            var hashInfo = request.HashInfo;

            var cts = context.Token;

            // before each retry, clear the list of bad locations so we can retry them all.
            // this helps isolate transient network errors.
            badContentLocations.Clear();
            string? lastErrorMessage = null;

            for (int replicaIndex = 0; replicaIndex < maxReplicaCount; replicaIndex++)
            {
                var location = hashInfo.Locations[replicaIndex];

                // Currently every time we increment attemptCount's value, we go through every location in request.HashInfo and try to copy.
                // We add one because replicaIndex is indexed from zero.
                // If we reach over maximum retries, return an put result stating so, and no longer retry
                var totalRetryCount = totalRetries + replicaIndex;
                if (totalRetryCount >= _maxRetryCount)
                {
                    Tracer.Debug(
                            context,
                            $"{AttemptTracePrefix(attemptCount)} Reached maximum number of total retries of {_maxRetryCount}.");
                    return (result: CreateMaxRetryPutResult(), retry: false);
                }

                // if the file is explicitly reported missing by the remote, don't bother retrying.
                if (missingContentLocations.Contains(location))
                {
                    continue;
                }

                // If there is a wait time, determine how much longer we need to wait
                if (!waitDelay.Equals(TimeSpan.Zero))
                {
                    TimeSpan waitedTime = _clock.UtcNow - lastFailureTimes[replicaIndex];
                    if (waitedTime < waitDelay)
                    {
                        await Task.Delay(waitDelay - waitedTime, cts);
                    }
                }

                var sourcePath = new ContentLocation(location, hashInfo.ContentHash);

                var tempLocation = AbsolutePath.CreateRandomFileName(workingFolder);

                (PutResult result, bool retry) reportCancellationRequested()
                {
                    Tracer.Debug(
                        context,
                        $"{AttemptTracePrefix(attemptCount)}: Could not copy file with hash {hashInfo.ContentHash.ToShortString()} to temp path {tempLocation} because cancellation was requested.");
                    return (result: CreateCanceledPutResult(), retry: false);
                }

                try
                {
                    if (cts.IsCancellationRequested)
                    {
                        return reportCancellationRequested();
                    }

                    // Gate entrance to both the copy logic and the logging which surrounds it
                    CopyFileResult? copyFileResult = null;
                    try
                    {
                        var options = GetCopyOptions(attemptCount, totalRetryCount, request);
                        CopySchedulingSummary? copySchedulingSummary = null;
                        copyFileResult = await context.PerformOperationAsync(
                            Tracer,
                            async () =>
                            {
                                var schedulerResult = await _copyScheduler.ScheduleOutboundPullAsync(new OutboundPullCopy(
                                    Reason: request.Reason,
                                    Context: context.WithCancellationToken(cts),
                                    Attempt: attemptCount,
                                    PerformOperationAsync: async (arguments) =>
                                    {
                                        var result = await TaskUtilities.AwaitWithProgressReportingAsync(
                                                    task: CopyFileAsync(arguments.Context, sourcePath, tempLocation, hashInfo, arguments.Context.Token, options),
                                                    period: _settings.PeriodicCopyTracingInterval,
                                                    action: timeSpan => Tracer.Debug(context, $"{Tracer.Name}.RemoteCopyFile from[{location}]) via stream in progress {(int)timeSpan.TotalSeconds}s, TotalBytesCopied=[{options.TotalBytesCopied}]."),
                                                    reportImmediately: false,
                                                    reportAtEnd: false);

                                        copySchedulingSummary = arguments.Summary;
                                        return result;
                                    })
                                {
                                    ContentHash = hashInfo.ContentHash,
                                });

                                // The scheduler may have failed, in which case the error is thrown here. If the copy
                                // fails, we'll pass this success check and return the error below.
                                if (!schedulerResult.Succeeded)
                                {
                                    throw new ResultPropagationException(schedulerResult);
                                }
                                else
                                {
                                    return schedulerResult.Value!;
                                }
                            },
                            traceOperationStarted: false,
                            traceOperationFinished: true,
                            extraEndMessage: (result) =>
                                $"contentHash=[{hashInfo.ContentHash.ToShortString()}] " +
                                $"from=[{sourcePath.Machine}] " +
                                $"size=[{result.Size ?? hashInfo.Size}] " +
                                $"reason={request.Reason} " +
                                $"trusted={_settings.UseTrustedHash(result.Size ?? hashInfo.Size)} " +
                                $"attempt={attemptCount} replica={replicaIndex} " +
                                (result.TimeSpentHashing.HasValue ? $"timeSpentHashing={result.TimeSpentHashing.Value.TotalMilliseconds}ms " : string.Empty) +
                                (result.TimeSpentWritingToDisk.HasValue ? $"writeTime={result.TimeSpentWritingToDisk.Value.TotalMilliseconds}ms " : string.Empty) +
                                (copySchedulingSummary is null ? string.Empty : $"{copySchedulingSummary} ") +
                                $"BandwidthOptions=[{options.BandwidthConfiguration?.ToString() ?? "null"}] " +
                                (result.HeaderResponseTime.HasValue ? $"HeaderResponseTime={result.HeaderResponseTime} " : string.Empty) +
                                (result.MinimumSpeedInMbPerSec.HasValue ? $"minBandwidthSpeed={result.MinimumSpeedInMbPerSec.Value}MiB/s " : string.Empty),
                            caller: "RemoteCopyFile",
                            counter: _counters[DistributedContentCopierCounters.RemoteCopyFile]);

                        TrackCopyFileResultMetrics(context, copyFileResult, attemptCount, copySchedulingSummary);
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
                            case CopyResultCode.Success:
                                request.Host.ReportReputation(location, MachineReputation.Good);
                                break;
                            case CopyResultCode.FileNotFoundError:
                                lastErrorMessage = $"Could not copy file with hash {hashInfo.ContentHash.ToShortString()} from path {sourcePath} to path {tempLocation} due to an error with the sourcepath: {copyFileResult}";
                                missingContentLocations.Add(location);
                                request.Host.ReportReputation(location, MachineReputation.Missing);
                                break;
                            case CopyResultCode.ServerUnavailable:
                            case CopyResultCode.UnknownServerError:
                                lastErrorMessage = $"Could not copy file with hash {hashInfo.ContentHash.ToShortString()} from path {sourcePath} to path {tempLocation} due to an error with the sourcepath: {copyFileResult}";
                                request.Host.ReportReputation(location, MachineReputation.Bad);
                                badContentLocations.Add(location);
                                break;
                            case CopyResultCode.ConnectionTimeoutError:
                                lastErrorMessage = $"Could not copy file with hash {hashInfo.ContentHash.ToShortString()} from path {sourcePath} to path {tempLocation} due to a grpc connection timeout: {copyFileResult}";
                                request.Host.ReportReputation(location, MachineReputation.Timeout);
                                badContentLocations.Add(location);
                                break;
                            case CopyResultCode.TimeToFirstByteTimeoutError:
                                lastErrorMessage = $"Could not copy file with hash {hashInfo.ContentHash.ToShortString()} from path {sourcePath} to path {tempLocation} due to timeout receiving the first bytes: {copyFileResult}";
                                request.Host.ReportReputation(location, MachineReputation.Timeout);
                                badContentLocations.Add(location);
                                break;
                            case CopyResultCode.DestinationPathError:
                                lastErrorMessage = $"Could not copy file with hash {hashInfo.ContentHash.ToShortString()} from path {sourcePath} to temp path {tempLocation} due to an error with the destination path: {copyFileResult}";
                                Tracer.Warning(
                                    context,
                                    $"{AttemptTracePrefix(attemptCount)} {lastErrorMessage} Not trying another replica.");
                                return (result: new ErrorResult(copyFileResult).AsResult<PutResult>(), retry: true);
                            case CopyResultCode.CopyTimeoutError:
                                lastErrorMessage = $"Could not copy file with hash {hashInfo.ContentHash.ToShortString()} from path {sourcePath} to path {tempLocation} due to copy timeout: {copyFileResult}";
                                request.Host.ReportReputation(location, MachineReputation.Timeout);
                                break;
                            case CopyResultCode.CopyBandwidthTimeoutError:
                                lastErrorMessage = $"Could not copy file with hash {hashInfo.ContentHash.ToShortString()} from path {sourcePath} to path {tempLocation} due to insufficient bandwidth timeout: {copyFileResult}";
                                request.Host.ReportReputation(location, MachineReputation.Timeout);
                                break;
                            case CopyResultCode.InvalidHash:
                                lastErrorMessage = $"Could not copy file with hash {hashInfo.ContentHash.ToShortString()} from path {sourcePath} to path {tempLocation} due to invalid hash: {copyFileResult}";
                                break;
                            case CopyResultCode.Unknown:
                                lastErrorMessage = $"Could not copy file with hash {hashInfo.ContentHash.ToShortString()} from path {sourcePath} to temp path {tempLocation} due to an internal error: {copyFileResult}";
                                request.Host.ReportReputation(location, MachineReputation.Bad);
                                break;
                            default:
                                lastErrorMessage = $"File copier result code {copyFileResult.Code} is not recognized. Not trying another replica.";
                                return (result: new ErrorResult(copyFileResult, $"{AttemptTracePrefix(attemptCount)} {lastErrorMessage}").AsResult<PutResult>(), retry: true);
                        }

                        if (copyFileResult.Succeeded)
                        {
                            // The copy succeeded, but it is possible that the resulting size doesn't match an expected one.
                            if (hashInfo.Size != -1 && copyFileResult.Size != null && hashInfo.Size != copyFileResult.Size.Value)
                            {
                                lastErrorMessage =
                                    $"ContentHash {hashInfo.ContentHash.ToShortString()} at location {location} has content size {copyFileResult.Size.Value} mismatch from {hashInfo.Size}";
                                Tracer.Warning(
                                    context,
                                    $"{AttemptTracePrefix(attemptCount)} {lastErrorMessage} Trying another replica.");
                                // Not tracking the source as a machine with bad reputation, because it is possible that we provided the wrong size.

                                continue;
                            }

                            PutResult putResult = await request.HandleCopyAsync((copyFileResult, tempLocation, attemptCount));

                            if (putResult.Succeeded)
                            {
                                // The put succeeded, but this doesn't necessarily mean that we put the content we intended. Check the content hash
                                // to ensure it's what is expected. This should only go wrong for a small portion of non-trusted puts.
                                if (putResult.ContentHash != hashInfo.ContentHash)
                                {
                                    lastErrorMessage =
                                        $"ContentHash at location {location} has ContentHash {putResult.ContentHash.ToShortString()} mismatch from {hashInfo.ContentHash.ToShortString()}";
                                    // If PutFileAsync re-hashed the file, then it could have found a content hash which differs from the expected content hash.
                                    // If this happens, we should fail this copy and move to the next location.
                                    Tracer.Warning(
                                        context,
                                        $"{AttemptTracePrefix(attemptCount)} {lastErrorMessage} Trying another replica.");
                                    badContentLocations.Add(location);
                                    continue;
                                }

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
                                var errorMessage = $"Put file for content hash {hashInfo.ContentHash.ToShortString()} failed with error {putResult.ErrorMessage} ";
                                Tracer.Warning(
                                    context,
                                    $"{AttemptTracePrefix(attemptCount)} {errorMessage} diagnostics {putResult.Diagnostics}");
                                return (result: putResult, retry: false);
                            }
                        }
                    }

                    Tracer.Warning(context, $"{AttemptTracePrefix(attemptCount)} {lastErrorMessage} Trying another replica.");
                }
                finally
                {
                    lastFailureTimes[replicaIndex] = _clock.UtcNow;

                    FileSystem.DeleteFile(tempLocation);
                }
            }

            if (lastErrorMessage != null)
            {
                lastErrorMessage = ". " + lastErrorMessage;
            }

            return (new PutResult(request.HashInfo.ContentHash, $"Unable to copy file{lastErrorMessage}"), retry: true);
        }

        private void TrackCopyFileResultMetrics(Context context, CopyFileResult result, int attemptCount, CopySchedulingSummary? summary)
        {
            Tracer.TrackMetric(context, "CopyFile_Duration", result.DurationMs);
            Tracer.TrackMetric(context, "CopyFile_AttemptCount", attemptCount);

            if (summary != null)
            {
                Tracer.TrackMetric(context, "CopyFile_IOGateWaitTimeMs", (long)summary.QueueWait.TotalMilliseconds);
            }

            ApplyIfNotNull(result.Size, v => Tracer.TrackMetric(context, "CopyFile_Size", v));
            ApplyIfNotNull(result.TimeSpentHashing, v => Tracer.TrackMetric(context, "CopyFile_HashingTimeMs", (long)v.TotalMilliseconds));
            ApplyIfNotNull(result.TimeSpentWritingToDisk, v => Tracer.TrackMetric(context, "CopyFile_WriteToDiskTimeMs", (long)v.TotalMilliseconds));
            ApplyIfNotNull(result.HeaderResponseTime, v => Tracer.TrackMetric(context, "CopyFile_HeaderResponseTimeMs", (long)v.TotalMilliseconds));
        }

        private async Task<CopyFileResult> CopyFileAsync(
            Context context,
            ContentLocation location,
            AbsolutePath tempDestinationPath,
            ContentHashWithSizeAndLocations hashInfo,
            CancellationToken cts,
            CopyOptions options)
        {
            try
            {
                // If the file satisfy trusted hash file size boundary, then we hash during the copy (i.e. now) and won't hash when placing the file into the store.
                // Otherwise we don't hash it now and the store will hash the file during put.
                if (_settings.UseTrustedHash(hashInfo.Size))
                {
                    // If we know that the file is large, then hash concurrently from the start
                    bool hashEntireFileConcurrently = _settings.ParallelHashingFileSizeBoundary >= 0 && hashInfo.Size > _settings.ParallelHashingFileSizeBoundary;

                    int bufferSize = GetBufferSize(hashInfo);

                    // Since this is the only place where we hash the file during trusted copies, we attempt to get access to the bytes here,
                    //  to avoid an additional IO operation later. In case that the file is bigger than the ContentLocationStore permits or blobs
                    //  aren't supported, disposing the FileStream twice does not throw or cause issues.
                    using (Stream fileStream = await FileSystem.OpenSafeAsync(tempDestinationPath, FileAccess.Write, FileMode.Create, FileShare.Read | FileShare.Delete, FileOptions.SequentialScan, bufferSize))
                    {
                        if (hashInfo.Size >= 0)
                        {
                            // Setting file size makes the file system happy and is considered a best practice.
                            fileStream.SetLength(hashInfo.Size);
                        }

                        using (Stream possiblyRecordingStream = _settings.AreBlobsSupported && hashInfo.Size <= _settings.MaxBlobSize && hashInfo.Size >= 0 ? (Stream)RecordingStream.WriteRecordingStream(fileStream) : fileStream)
                        // Use hashInfo.Size since if it is -1 we will not have resized the stream and it will disable an optimization in dedup hashers which depends on file size.
                        using (HashingStream hashingStream = ContentHashers.Get(hashInfo.ContentHash.HashType).CreateWriteHashingStream(hashInfo.Size, possiblyRecordingStream, hashEntireFileConcurrently ? 1 : _settings.ParallelHashingFileSizeBoundary))
                        {
                            var copyFileResult = await _remoteFileCopier.CopyToAsync(
                                new OperationContext(context, cts), location, hashingStream,
                                options);
                            copyFileResult.TimeSpentHashing = hashingStream.TimeSpentHashing;
                            TrackTimeSpentWritingToDisk(copyFileResult, fileStream);

                            if (copyFileResult.Succeeded)
                            {
                                var foundHash = hashingStream.GetContentHash();
                                if (foundHash != hashInfo.ContentHash)
                                {
                                    return new CopyFileResult(CopyResultCode.InvalidHash, $"{nameof(CopyFileAsync)} unsuccessful with different hash. Found {foundHash.ToShortString()}, expected {hashInfo.ContentHash.ToShortString()}. Found size {copyFileResult.Size}, expected size {hashInfo.Size}." + (copyFileResult.MinimumSpeedInMbPerSec.HasValue ? $" minBandwidthSpeed={copyFileResult.MinimumSpeedInMbPerSec.Value}MiB/s " : string.Empty));
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
                }
                else
                {
                    return await CopyFileAsync(
                        new OperationContext(context, cts),
                        _remoteFileCopier,
                        location,
                        tempDestinationPath,
                        hashInfo.Size,
                        overwrite: true,
                        cancellationToken: cts,
                        options: options);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException || ex is InvalidOperationException || ex is IOException)
            {
                // Auth errors are considered errors with the destination.
                // Since FSServer now returns HTTP based exceptions for web, IO failures must be local file paths.
                return new CopyFileResult(CopyResultCode.DestinationPathError, ex, ex.ToString());
            }
            catch (Exception ex)
            {
                // any other exceptions are assumed to be bad remote files.
                return new CopyFileResult(CopyResultCode.UnknownServerError, ex, ex.ToString());
            }
        }

        private void TrackTimeSpentWritingToDisk(CopyFileResult copyFileResult, Stream fileStream)
        {
            if (fileStream is TrackingFileStream tfs)
            {
                copyFileResult.TimeSpentWritingToDisk = tfs.WriteDuration;

                _counters.AddToCounter(DistributedContentCopierCounters.WriteToDisk, tfs.WriteDuration);
            }
        }

        /// <summary>
        /// Override for testing.
        /// </summary>
        protected virtual async Task<CopyFileResult> CopyFileAsync(
            OperationContext context,
            IRemoteFileCopier copier,
            ContentLocation sourcePath,
            AbsolutePath destinationPath,
            long expectedContentSize,
            bool overwrite,
            CopyOptions options,
            CancellationToken cancellationToken)
        {
            const int DefaultBufferSize = 1024 * 80;

            if (!overwrite && FileSystem.FileExists(destinationPath))
            {
                return new CopyFileResult(
                        CopyResultCode.DestinationPathError,
                        $"Destination file {destinationPath} exists but overwrite not specified.");
            }

            var directoryPath = destinationPath.GetParent();
            if (!FileSystem.DirectoryExists(directoryPath))
            {
                FileSystem.CreateDirectory(directoryPath);
            }

            using var stream = await FileSystem.OpenSafeAsync(destinationPath, FileAccess.Write, FileMode.Create, FileShare.None, FileOptions.SequentialScan, DefaultBufferSize);
            var result = await copier.CopyToAsync(context, sourcePath, stream, options);

            TrackTimeSpentWritingToDisk(result, stream);

            return result;
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

            public IReadOnlyList<MachineLocation> Present { get; }

            public IReadOnlyList<MachineLocation> Absent { get; }

            public IReadOnlyList<MachineLocation> Unknown { get; }

            public VerifyResult(ContentHash hash, IReadOnlyList<MachineLocation> present, IReadOnlyList<MachineLocation> absent, IReadOnlyList<MachineLocation> unknown)
            {
                Hash = hash;
                Present = present;
                Absent = absent;
                Unknown = unknown;
            }
        }

        /// <nodoc />
        public CounterSet GetCounters() => _counters.ToCounterSet();

        private enum DistributedContentCopierCounters
        {
            /// <nodoc />
            [CounterType(CounterType.Stopwatch)]
            RemoteCopyFile,

            [CounterType(CounterType.Stopwatch)]
            WriteToDisk,

            /// <nodoc />
            RemoteBytes,

            /// <nodoc />
            RemoteFilesCopied,

            /// <nodoc />
            RemoteFilesFailedCopy
        }

        /// <summary>
        /// The content communication manager calls DeleteFileAsync on every machine that has the contentHash.
        /// This will create a GrpcContentClient for each machine that calls deleteAsync.
        /// We then aggregate the results returned from every machine, and return the highest level of error code.
        /// </summary>
        public async Task<DeleteResult> DeleteAsync(OperationContext context, ContentHash contentHash, long contentSize, IReadOnlyList<MachineLocation> machines, Dictionary<string, DeleteResult> deleteMapping)
        {
            var tasks = machines.Select(m => _copyRequester.DeleteFileAsync(context, contentHash, m)).ToList();
            var deleteResults = await Task.WhenAll(tasks);
            var size = contentSize;

            Contract.Assert(machines.Count == deleteResults.Length);
            for (var i = 0; i < machines.Count; i++)
            {
                size = Math.Max(size, deleteResults[i].ContentSize);
                // The mapping could already have a given path.
                deleteMapping.TryAdd(machines[i].Path, deleteResults[i]);
            }

            return new DistributedDeleteResult(contentHash, size, deleteMapping);
        }
    }
}
