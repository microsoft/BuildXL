// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using Microsoft.VisualStudio.Services.BlobStore.Common;
using Microsoft.VisualStudio.Services.BlobStore.WebApi;
using Microsoft.VisualStudio.Services.Content.Common;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using BlobIdentifier = BuildXL.Cache.ContentStore.Hashing.BlobIdentifier;
using ByteArrayPool = Microsoft.VisualStudio.Services.BlobStore.Common.ByteArrayPool;
using VstsBlobIdentifier = Microsoft.VisualStudio.Services.BlobStore.Common.BlobIdentifier;

namespace BuildXL.Cache.ContentStore.Vsts
{
    /// <summary>
    ///     IReadOnlyContentSession for BlobBuildXL.ContentStore.
    /// </summary>
    [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    public class BlobReadOnlyContentSession : IReadOnlyContentSession
    {
        /// <summary>
        ///     The only HashType recognizable by the server.
        /// </summary>
        protected const HashType RequiredHashType = HashType.Vso0;

        /// <summary>
        ///     Size for stream buffers to temp files.
        /// </summary>
        protected const int StreamBufferSize = 16384;

        private static readonly ByteArrayPool BufferPool =
            new ByteArrayPool(
                int.Parse(Environment.GetEnvironmentVariable($"{EnvironmentVariablePrefix}ReadSizeInBytes") ??
                          DefaultReadSizeInBytes.ToString()),
                maxToKeep: 4 * Environment.ProcessorCount);

        /// <summary>
        ///     Policy determining whether or not content should be automatically pinned on adds or gets.
        /// </summary>
        protected readonly ImplicitPin ImplicitPin;

        /// <summary>
        ///     How long to keep content after referencing it.
        /// </summary>
        protected readonly TimeSpan TimeToKeepContent;

        /// <summary>
        /// A tracer for tracking blob content calls.
        /// </summary>
        protected readonly BackingContentStoreTracer Tracer;

        /// <summary>
        ///     Staging ground for parallel upload/downloads.
        /// </summary>
        protected readonly DisposableDirectory TempDirectory;

        private readonly bool _downloadBlobsThroughBlobStore;

        /// <summary>
        ///     Backing BlobStore http client
        /// </summary>
        protected readonly IBlobStoreHttpClient BlobStoreHttpClient;

        // Error codes: https://msdn.microsoft.com/en-us/library/windows/desktop/ms681382(v=vs.85).aspx
        private const int ErrorFileExists = 80;

        private const string EnvironmentVariablePrefix = "VSO_DROP_";

        private const int DefaultMaxParallelSegmentDownloadsPerFile = 16;

        private const int DefaultMaxSegmentDownloadRetries = 3;

        private const int DefaultParallelDownloadSegmentSizeInBytes = 8 * 1024 * 1024;

        private const int DefaultReadSizeInBytes = 64 * 1024;

        private const int DefaultSegmentDownloadTimeoutInMinutes = 10;

        private readonly ParallelHttpDownload.Configuration _parallelSegmentDownloadConfig;

        /// <summary>
        /// Initializes a new instance of the <see cref="BlobReadOnlyContentSession"/> class.
        /// </summary>
        /// <param name="fileSystem">Filesystem used to read/write files.</param>
        /// <param name="name">Session name.</param>
        /// <param name="implicitPin">Policy determining whether or not content should be automatically pinned on adds or gets.</param>
        /// <param name="blobStoreHttpClient">Backing BlobStore http client.</param>
        /// <param name="timeToKeepContent">Minimum time-to-live for accessed content.</param>
        /// <param name="tracer">A tracer for tracking blob content session calls.</param>
        /// <param name="downloadBlobsThroughBlobStore">If true, gets blobs through BlobStore. If false, gets blobs from the Azure Uri.</param>
        public BlobReadOnlyContentSession(
            IAbsFileSystem fileSystem,
            string name,
            ImplicitPin implicitPin,
            IBlobStoreHttpClient blobStoreHttpClient,
            TimeSpan timeToKeepContent,
            BackingContentStoreTracer tracer,
            bool downloadBlobsThroughBlobStore)
        {
            Contract.Requires(fileSystem != null);
            Contract.Requires(name != null);
            Contract.Requires(blobStoreHttpClient != null);

            Name = name;
            ImplicitPin = implicitPin;
            BlobStoreHttpClient = blobStoreHttpClient;
            TimeToKeepContent = timeToKeepContent;
            Tracer = tracer;
            _downloadBlobsThroughBlobStore = downloadBlobsThroughBlobStore;
            _parallelSegmentDownloadConfig = new ParallelHttpDownload
                .Configuration(
                segmentDownloadTimeout: TimeSpan.FromMinutes(int.Parse(
                    Environment.GetEnvironmentVariable(EnvironmentVariablePrefix + "SegmentDownloadTimeoutInMinutes") ??
                    DefaultSegmentDownloadTimeoutInMinutes.ToString())),
                segmentSizeInBytes: int.Parse(
                    Environment.GetEnvironmentVariable(EnvironmentVariablePrefix + "ParallelDownloadSegmentSizeInBytes") ??
                    DefaultParallelDownloadSegmentSizeInBytes.ToString()),
                maxParallelSegmentDownloadsPerFile: int.Parse(
                    Environment.GetEnvironmentVariable(EnvironmentVariablePrefix + "MaxParallelSegmentDownloadsPerFile") ??
                    DefaultMaxParallelSegmentDownloadsPerFile.ToString()),
                maxSegmentDownloadRetries:
                    int.Parse(
                        Environment.GetEnvironmentVariable(EnvironmentVariablePrefix + "MaxSegmentDownloadRetries") ??
                        DefaultMaxSegmentDownloadRetries.ToString()));

            TempDirectory = new DisposableDirectory(fileSystem);
            BuildXL.Native.IO.FileUtilities.CreateDirectory(TempDirectory.Path.Path);
        }

        /// <inheritdoc />
        public string Name { get; }

        /// <inheritdoc />
        public Task<BoolResult> StartupAsync(Context context)
        {
            StartupStarted = true;
            StartupCompleted = true;
            return Task.FromResult(BoolResult.Success);
        }

        /// <inheritdoc />
        public bool StartupCompleted { get; private set; }

        /// <inheritdoc />
        public bool StartupStarted { get; private set; }

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Dispose native resources.
        /// </summary>
        [SuppressMessage("ReSharper", "UnusedParameter.Global")]
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                TempDirectory.Dispose();
            }
        }

        /// <inheritdoc />
        public Task<BoolResult> ShutdownAsync(Context context)
        {
            ShutdownStarted = true;
            ShutdownCompleted = true;
            return Task.FromResult(BoolResult.Success);
        }

        /// <inheritdoc />
        public bool ShutdownCompleted { get; private set; }

        /// <inheritdoc />
        public bool ShutdownStarted { get; private set; }

        /// <inheritdoc />
        public async Task<PinResult> PinAsync(
            Context context, ContentHash contentHash, CancellationToken cts, UrgencyHint urgencyHint)
        {
            try
            {
                var bulkResults = await PinAsync(context, new[] { contentHash }, cts, urgencyHint);
                return await bulkResults.SingleAwaitIndexed();
            }
            catch (Exception e)
            {
                return new PinResult(e);
            }
        }

        /// <inheritdoc />
        public async Task<OpenStreamResult> OpenStreamAsync(
            Context context, ContentHash contentHash, CancellationToken cts, UrgencyHint urgencyHint)
        {
            if (contentHash.HashType != RequiredHashType)
            {
                return new OpenStreamResult(
                    $"BuildCache client requires HashType '{RequiredHashType}'. Cannot take HashType '{contentHash.HashType}'.");
            }

            string tempFile = null;
            try
            {
                if (ImplicitPin == ImplicitPin.PutAndGet)
                {
                    var pinResult = await PinAsync(context, contentHash, cts, urgencyHint).ConfigureAwait(false);
                    if (!pinResult.Succeeded)
                    {
                        if (pinResult.Code == PinResult.ResultCode.ContentNotFound)
                        {
                            return new OpenStreamResult(null);
                        }

                        return new OpenStreamResult(pinResult);
                    }
                }

                tempFile = TempDirectory.CreateRandomFileName().Path;
                var possibleLength =
                    await PlaceFileInternalAsync(context, contentHash, tempFile, FileMode.Create, cts).ConfigureAwait(false);

                if (possibleLength.HasValue)
                {
                    return new OpenStreamResult(new FileStream(
                        tempFile,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read | FileShare.Delete,
                        StreamBufferSize,
                        FileOptions.DeleteOnClose));
                }

                return new OpenStreamResult(null);
            }
            catch (Exception e)
            {
                return new OpenStreamResult(e);
            }
            finally
            {
                if (tempFile != null)
                {
                    try
                    {
                        File.Delete(tempFile);
                    }
                    catch (Exception e)
                    {
                        Tracer.Warning(context, $"Error deleting temporary file at {tempFile}: {e}");
                    }
                }
            }
        }

        /// <inheritdoc />
        public async Task<PlaceFileResult> PlaceFileAsync(
            Context context,
            ContentHash contentHash,
            AbsolutePath path,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            CancellationToken cts,
            UrgencyHint urgencyHint)
        {
            try
            {
                if (replacementMode != FileReplacementMode.ReplaceExisting && File.Exists(path.Path))
                {
                    return new PlaceFileResult(PlaceFileResult.ResultCode.NotPlacedAlreadyExists);
                }

                if (ImplicitPin == ImplicitPin.PutAndGet)
                {
                    var pinResult = await PinAsync(context, contentHash, cts, urgencyHint).ConfigureAwait(false);
                    if (!pinResult.Succeeded)
                    {
                        return pinResult.Code == PinResult.ResultCode.ContentNotFound
                            ? new PlaceFileResult(PlaceFileResult.ResultCode.NotPlacedContentNotFound)
                            : new PlaceFileResult(pinResult);
                    }
                }

                var fileMode = replacementMode == FileReplacementMode.ReplaceExisting
                    ? FileMode.Create
                    : FileMode.CreateNew;
                var possibleLength =
                    await PlaceFileInternalAsync(context, contentHash, path.Path, fileMode, cts).ConfigureAwait(false);
                return possibleLength.HasValue
                    ? new PlaceFileResult(PlaceFileResult.ResultCode.PlacedWithCopy, possibleLength.Value)
                    : new PlaceFileResult(PlaceFileResult.ResultCode.NotPlacedContentNotFound);
            }
            catch (IOException e) when (IsErrorFileExists(e))
            {
                return new PlaceFileResult(PlaceFileResult.ResultCode.NotPlacedAlreadyExists);
            }
            catch (Exception e)
            {
                return new PlaceFileResult(e);
            }
        }

        /// <inheritdoc />
        [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
        public async Task<IEnumerable<Task<Indexed<PinResult>>>> PinAsync(Context context, IReadOnlyList<ContentHash> contentHashes, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                Tracer.PinBulkStart(context, contentHashes);
                DateTime endDateTime = DateTime.UtcNow + TimeToKeepContent;

                return await Workflows.RunWithFallback(
                    contentHashes,
                    hashes => CheckInMemoryCaches(hashes, endDateTime),
                    hashes => UpdateBlobStoreAsync(context, hashes, endDateTime, cts),
                    result => result.Succeeded);
            }
            catch (Exception ex)
            {
                context.Warning($"Exception when querying pins against the VSTS services {ex}");
                return contentHashes.Select((_, index) => Task.FromResult(new PinResult(ex).WithIndex(index)));
            }
            finally
            {
                Tracer.PinBulkStop(context, sw.Elapsed);
            }
        }

        /// <inheritdoc />
        public Task<IEnumerable<Task<Indexed<PlaceFileResult>>>> PlaceFileAsync(Context context, IReadOnlyList<ContentHashWithPath> hashesWithPaths, FileAccessMode accessMode, FileReplacementMode replacementMode, FileRealizationMode realizationMode, CancellationToken cts, UrgencyHint urgencyHint = UrgencyHint.Nominal)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Converts a ContentStore blob id to an artifact BlobId
        /// </summary>
        protected static VstsBlobIdentifier ToVstsBlobIdentifier(BlobIdentifier blobIdentifier)
        {
            return new VstsBlobIdentifier(blobIdentifier.Bytes);
        }

        private async Task<IEnumerable<Task<Indexed<PinResult>>>> UpdateBlobStoreAsync(Context context, IReadOnlyList<ContentHash> contentHashes, DateTime endDateTime, CancellationToken cts)
        {
            // Convert missing content hashes to blob Ids
            var blobIds = contentHashes.Select(c => ToVstsBlobIdentifier(c.ToBlobIdentifier())).ToList();

            // Call TryReference on the blob ids
            var references = blobIds.Distinct().ToDictionary(
                blobIdentifier => blobIdentifier,
                _ => (IEnumerable<BlobReference>)new[] { new BlobReference(endDateTime) });

            // TODO: In groups of 1000 (bug 1365340)
            var referenceResults = await ArtifactHttpClientErrorDetectionStrategy.ExecuteWithTimeoutAsync(
                context,
                "UpdateBlobStore",
                innerCts => BlobStoreHttpClient.TryReferenceAsync(references, cancellationToken: innerCts),
                cts).ConfigureAwait(false);

            Tracer.RecordPinSatisfiedFromRemote();

            // There's 1-1 mapping between given content hashes and blob ids
            var remoteResults = blobIds
                .Select((blobId, i) =>
                {
                    PinResult pinResult = referenceResults.ContainsKey(blobId)
                        ? PinResult.ContentNotFound
                        : PinResult.Success;
                    if (pinResult.Succeeded)
                        {
                            BackingContentStoreExpiryCache.Instance.AddExpiry(contentHashes[i], endDateTime);
                        }

                        return pinResult.WithIndex(i);
                    });

            return remoteResults.AsTasks();
        }

        private Task<IEnumerable<Task<Indexed<PinResult>>>> CheckInMemoryCaches(IReadOnlyList<ContentHash> contentHashes, DateTime endDateTime)
        {
            return Task.FromResult(
                        contentHashes
                            .Select(c => CheckPinInMemory(c, endDateTime))
                            .AsIndexedTasks());
        }

        private PinResult CheckPinInMemory(ContentHash contentHash, DateTime endDateTime)
        {
            DateTime expiryTime;

            // TODO: allow cached expiry time to be within some bump threshold (e.g. allow expiryTime = 6 days & endDateTime = 7 days) (bug 1365340)
            if (BackingContentStoreExpiryCache.Instance.TryGetExpiry(contentHash, out expiryTime) &&
                expiryTime > endDateTime)
            {
                Tracer.RecordPinSatisfiedInMemory();
                return PinResult.Success;
            }

            PreauthenticatedUri authenticatedUri;
            if (DownloadUriCache.Instance.TryGetDownloadUri(contentHash, out authenticatedUri) &&
                authenticatedUri.ExpiryTime > endDateTime)
            {
                Tracer.RecordPinSatisfiedInMemory();
                return PinResult.Success;
            }

            return PinResult.ContentNotFound;
        }

        private Task<long?> PlaceFileInternalAsync(
            Context context, ContentHash contentHash, string path, FileMode fileMode, CancellationToken cts)
        {
            return AsyncHttpRetryHelper<long?>.InvokeAsync(
                async () =>
                {
                    Stream httpStream = null;
                    try
                    {
                        httpStream = await GetStreamInternalAsync(context, contentHash, null, cts).ConfigureAwait(false);
                        if (httpStream == null)
                        {
                            return null;
                        }

                        try
                        {
                            var success = DownloadUriCache.Instance.TryGetDownloadUri(contentHash, out PreauthenticatedUri preauthUri);
                            var uri = success ? preauthUri.NotNullUri : new Uri("http://empty.com");

                            Directory.CreateDirectory(Directory.GetParent(path).FullName);

                            // TODO: Investigate using ManagedParallelBlobDownloader instead (bug 1365340)
                            await ParallelHttpDownload.Download(
                                _parallelSegmentDownloadConfig,
                                uri,
                                httpStream,
                                null,
                                path,
                                fileMode,
                                cts,
                                (destinationPath, offset, endOffset) =>
                                    Tracer.Debug(context, $"Download {destinationPath} [{offset}, {endOffset}) start."),
                                (destinationPath, offset, endOffset) =>
                                    Tracer.Debug(context, $"Download {destinationPath} [{offset}, {endOffset}) end."),
                                (destinationPath, offset, endOffset, message) =>
                                    Tracer.Debug(context, $"Download {destinationPath} [{offset}, {endOffset}) failed. (message: {message})"),
                                async (offset, token) =>
                                {
                                    var offsetStream = await GetStreamInternalAsync(
                                        context,
                                        contentHash,
                                        _parallelSegmentDownloadConfig.SegmentSizeInBytes,
                                        cts).ConfigureAwait(false);
                                    offsetStream.Position = offset;
                                    return offsetStream;
                                },
                                () => BufferPool.Get()).ConfigureAwait(false);
                        }
                        catch (Exception e) when (fileMode == FileMode.CreateNew && !IsErrorFileExists(e))
                        {
                            try
                            {
                                // Need to delete here so that a partial download doesn't run afoul of FileReplacementMode.FailIfExists upon retry
                                // Don't do this if the error itself was that the file already existed
                                File.Delete(path);
                            }
                            catch (Exception ex)
                            {
                                Tracer.Warning(context, $"Error deleting file at {path}: {ex}");
                            }

                            throw;
                        }

                        return httpStream.Length;
                    }
                    catch (StorageException storageEx) when (storageEx.InnerException is WebException)
                    {
                        var webEx = (WebException)storageEx.InnerException;
                        if (((HttpWebResponse)webEx.Response).StatusCode == HttpStatusCode.NotFound)
                        {
                            return null;
                        }

                        throw;
                    }
                    finally
                    {
                        httpStream?.Dispose();
                    }
                },
                maxRetries: 5,
                tracer: new AppTraceSourceContextAdapter(context, "BlobReadOnlyContentSession", SourceLevels.All),
                canRetryDelegate: exception =>
                {
                    // HACK HACK: This is an additional layer of retries specifically to catch the SSL exceptions seen in DM.
                    // Newer versions of Artifact packages have this retry automatically, but the packages deployed with the M119.0 release do not.
                    // Once the next release is completed, these retries can be removed.
                    if (exception is HttpRequestException && exception.InnerException is WebException)
                    {
                        return true;
                    }

                    while (exception != null)
                    {
                        if (exception is TimeoutException)
                        {
                            return true;
                        }
                        exception = exception.InnerException;
                    }
                    return false;
                },
                cancellationToken: cts,
                continueOnCapturedContext: false,
                context: context.Id.ToString());
        }

        private bool IsErrorFileExists(Exception e)
        {
            return (Marshal.GetHRForException(e) & ((1 << 16) - 1)) == ErrorFileExists;
        }

        private async Task<Stream> GetStreamInternalAsync(Context context, ContentHash contentHash, int? overrideStreamMinimumReadSizeInBytes, CancellationToken cts)
        {
            if (_downloadBlobsThroughBlobStore)
            {
                return await ArtifactHttpClientErrorDetectionStrategy.ExecuteWithTimeoutAsync(
                    context,
                    "GetStreamInternalThroughBlobStore",
                    innerCts => BlobStoreHttpClient.GetBlobAsync(ToVstsBlobIdentifier(contentHash.ToBlobIdentifier()), cancellationToken: innerCts),
                    cts).ConfigureAwait(false);
            }
            else
            {
                PreauthenticatedUri uri;
                if (!DownloadUriCache.Instance.TryGetDownloadUri(contentHash, out uri))
                {
                    Tracer.RecordDownloadUriFetchedFromRemote();
                    BlobIdentifier blobId = contentHash.ToBlobIdentifier();

                    IDictionary<VstsBlobIdentifier, PreauthenticatedUri> mappings = await ArtifactHttpClientErrorDetectionStrategy.ExecuteWithTimeoutAsync(
                        context,
                        "GetStreamInternal",
                        innerCts => BlobStoreHttpClient.GetDownloadUrisAsync(
                            new[] {ToVstsBlobIdentifier(blobId)},
                            EdgeCache.NotAllowed,
                            cancellationToken: innerCts),
                        cts).ConfigureAwait(false);

                    if (mappings == null || !mappings.TryGetValue(ToVstsBlobIdentifier(blobId), out uri))
                    {
                        return null;
                    }

                    DownloadUriCache.Instance.AddDownloadUri(contentHash, uri);
                }
                else
                {
                    Tracer.RecordDownloadUriFetchedFromCache();
                }

                return await GetStreamThroughAzureBlobs(
                    uri.NotNullUri,
                    overrideStreamMinimumReadSizeInBytes,
                    _parallelSegmentDownloadConfig.SegmentDownloadTimeout,
                    cts).ConfigureAwait(false);
            }
        }

        private Task<Stream> GetStreamThroughAzureBlobs(Uri azureUri, int? overrideStreamMinimumReadSizeInBytes, TimeSpan? requestTimeout, CancellationToken cancellationToken)
        {
            var blob = new CloudBlockBlob(azureUri);
            if (overrideStreamMinimumReadSizeInBytes.HasValue)
            {
                blob.StreamMinimumReadSizeInBytes = overrideStreamMinimumReadSizeInBytes.Value;
            }

            return blob.OpenReadAsync(
                null,
                new BlobRequestOptions()
                {
                    MaximumExecutionTime = requestTimeout,

                    // See also:
                    // ParallelOperationThreadCount
                    // RetryPolicy
                    // ServerTimeout
                },
                null, cancellationToken);
        }
    }
}
