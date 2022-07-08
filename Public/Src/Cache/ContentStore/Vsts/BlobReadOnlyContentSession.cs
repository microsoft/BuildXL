// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Utilities.Tracing;
using Microsoft.VisualStudio.Services.BlobStore.Common;
using Microsoft.VisualStudio.Services.BlobStore.WebApi;
using Microsoft.VisualStudio.Services.Content.Common;
using Microsoft.VisualStudio.Services.WebApi;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using ByteArrayPool = Microsoft.VisualStudio.Services.BlobStore.Common.ByteArrayPool;
using OperationContext = BuildXL.Cache.ContentStore.Tracing.Internal.OperationContext;

#nullable enable

namespace BuildXL.Cache.ContentStore.Vsts
{
    /// <summary>
    ///     IReadOnlyContentSession for BlobBuildXL.ContentStore.
    /// </summary>
    [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    public class BlobReadOnlyContentSession : ContentSessionBase, IReadOnlyBackingContentSession
    {
        private enum Counters
        {
            /// <summary>
            /// Download URI had to be obtained from calling a remote VSTS service.
            /// </summary>
            VstsDownloadUriFetchedFromRemote,

            /// <summary>
            /// DownloadUri was fetched from the in-memory cache.
            /// </summary>
            VstsDownloadUriFetchedInMemory
        }

        /// <inheritdoc />
        public BackingContentStoreExpiryCache ExpiryCache { get; } = new BackingContentStoreExpiryCache();

        /// <inheritdoc />
        public DownloadUriCache UriCache { get; } = new DownloadUriCache();

        private readonly CounterCollection<BackingContentStore.SessionCounters> _counters;
        private readonly CounterCollection<Counters> _blobCounters;

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

        /// <nodoc />
        protected readonly BackingContentStoreConfiguration Configuration;

        /// <summary>
        ///     How long to keep content after referencing it.
        /// </summary>
        protected TimeSpan TimeToKeepContent => Configuration.TimeToKeepContent;

        /// <summary>
        /// A tracer for tracking blob content calls.
        /// </summary>
        protected override Tracer Tracer { get; } = new Tracer(nameof(BlobContentSession));

        /// <summary>
        ///     Staging ground for parallel upload/downloads.
        /// </summary>
        protected readonly DisposableDirectory TempDirectory;

        /// <summary>
        ///     Backing BlobStore http client
        /// </summary>
        protected readonly IBlobStoreHttpClient BlobStoreHttpClient;

        // Error codes: https://msdn.microsoft.com/en-us/library/windows/desktop/ms681382(v=vs.85).aspx
        private const int ErrorFileExists = 80;

        private const string EnvironmentVariablePrefix = "VSO_DROP_";

        private const int DefaultReadSizeInBytes = 64 * 1024;

        private readonly ParallelHttpDownload.DownloadConfiguration _parallelSegmentDownloadConfig;

        /// <summary>
        /// Reused http client for http downloads
        /// </summary>
        private readonly HttpClient _httpClient = new HttpClient();

        /// <summary>
        /// Initializes a new instance of the <see cref="BlobReadOnlyContentSession"/> class.
        /// </summary>
        /// <param name="configuration">Configuration.</param>
        /// <param name="name">Session name.</param>
        /// <param name="blobStoreHttpClient">Backing BlobStore http client.</param>
        /// <param name="counterTracker">Parent counters to track the session.</param>
        public BlobReadOnlyContentSession(
            BackingContentStoreConfiguration configuration,
            string name,
            IBlobStoreHttpClient blobStoreHttpClient,
            CounterTracker? counterTracker = null)
            : base(name, counterTracker)
        {
            Contract.Requires(configuration != null);
            Contract.Requires(configuration.FileSystem != null);
            Contract.Requires(name != null);
            Contract.Requires(blobStoreHttpClient != null);

            Configuration = configuration;

            BlobStoreHttpClient = blobStoreHttpClient;
            _parallelSegmentDownloadConfig = ParallelHttpDownload.DownloadConfiguration.ReadFromEnvironment(EnvironmentVariablePrefix);

            TempDirectory = new DisposableDirectory(configuration.FileSystem);

            _counters = CounterTracker.CreateCounterCollection<BackingContentStore.SessionCounters>(counterTracker);
            _blobCounters = CounterTracker.CreateCounterCollection<Counters>(counterTracker);
        }

        /// <inheritdoc />
        protected override void DisposeCore() => TempDirectory.Dispose();

        /// <inheritdoc />
        protected override async Task<PinResult> PinCoreAsync(
            OperationContext context, ContentHash contentHash, UrgencyHint urgencyHint, Counter retryCounter)
        {
            try
            {
                var bulkResults = await PinAsync(context, new[] { contentHash }, context.Token, urgencyHint);
                return await bulkResults.SingleAwaitIndexed();
            }
            catch (Exception e)
            {
                return new PinResult(e);
            }
        }

        /// <inheritdoc />
        protected override async Task<OpenStreamResult> OpenStreamCoreAsync(
            OperationContext context, ContentHash contentHash, UrgencyHint urgencyHint, Counter retryCounter)
        {
            if (contentHash.HashType != RequiredHashType)
            {
                return new OpenStreamResult(
                    $"BuildCache client requires HashType '{RequiredHashType}'. Cannot take HashType '{contentHash.HashType}'.");
            }

            string? tempFile = null;
            try
            {
                tempFile = TempDirectory.CreateRandomFileName().Path;
                var possibleLength =
                    await PlaceFileInternalAsync(context, contentHash, tempFile, FileMode.Create).ConfigureAwait(false);

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
        protected override async Task<PlaceFileResult> PlaceFileCoreAsync(
            OperationContext context,
            ContentHash contentHash,
            AbsolutePath path,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            try
            {
                if (replacementMode != FileReplacementMode.ReplaceExisting && File.Exists(path.Path))
                {
                    return PlaceFileResult.AlreadyExists;
                }

                var fileMode = replacementMode == FileReplacementMode.ReplaceExisting
                    ? FileMode.Create
                    : FileMode.CreateNew;
                var possibleLength =
                    await PlaceFileInternalAsync(context, contentHash, path.Path, fileMode).ConfigureAwait(false);
                return possibleLength.HasValue
                    ? PlaceFileResult.CreateSuccess(PlaceFileResult.ResultCode.PlacedWithCopy, possibleLength.Value, source: PlaceFileResult.Source.BackingStore)
                    : PlaceFileResult.ContentNotFound;
            }
            catch (IOException e) when (IsErrorFileExists(e))
            {
                return PlaceFileResult.AlreadyExists;
            }
            catch (Exception e)
            {
                return new PlaceFileResult(e);
            }
        }

        /// <inheritdoc />
        [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
        protected override Task<IEnumerable<Task<Indexed<PinResult>>>> PinCoreAsync(OperationContext context, IReadOnlyList<ContentHash> contentHashes, UrgencyHint urgencyHint, Counter retryCounter, Counter fileCounter)
        {
            var endDateTime = DateTime.UtcNow + TimeToKeepContent;
            return PinCoreImplAsync(context, contentHashes, endDateTime);
        }

        private async Task<IEnumerable<Task<Indexed<PinResult>>>> PinCoreImplAsync(OperationContext context, IReadOnlyList<ContentHash> contentHashes, DateTime keepUntil)
        {
            try
            {
                return await Workflows.RunWithFallback(
                    contentHashes,
                    hashes => CheckInMemoryCaches(hashes, keepUntil),
                    hashes => UpdateBlobStoreAsync(context, hashes, keepUntil),
                    result => result.Succeeded);
            }
            catch (Exception ex)
            {
                Tracer.Warning(context, $"Exception when querying pins against the VSTS services {ex}");
                return contentHashes.Select((_, index) => Task.FromResult(new PinResult(ex).WithIndex(index)));
            }
        }

        /// <inheritdoc />
        protected override Task<IEnumerable<Task<Indexed<PlaceFileResult>>>> PlaceFileCoreAsync(OperationContext context, IReadOnlyList<ContentHashWithPath> hashesWithPaths, FileAccessMode accessMode, FileReplacementMode replacementMode, FileRealizationMode realizationMode, UrgencyHint urgencyHint, Counter retryCounter)
            => throw new NotImplementedException();

        private async Task<IEnumerable<Task<Indexed<PinResult>>>> UpdateBlobStoreAsync(OperationContext context, IReadOnlyList<ContentHash> contentHashes, DateTime endDateTime)
        {
            // Convert missing content hashes to blob Ids
            var blobIds = new BlobIdentifier[contentHashes.Count];
            for (int i = 0; i < contentHashes.Count; i++)
            {
                blobIds[i] = contentHashes[i].ToBlobIdentifier();
            }

            // Call TryReference on the blob ids
            Dictionary<BlobIdentifier, IEnumerable<BlobReference>> references = new(blobIds.Length);
            var blobReferences = new[] { new BlobReference(endDateTime) };
            foreach (var blobId in blobIds)
            {
                references[blobId] = blobReferences;
            }

            // TODO: In groups of 1000 (bug 1365340)
            var referenceResults = await ArtifactHttpClientErrorDetectionStrategy.ExecuteWithTimeoutAsync(
                context,
                "UpdateBlobStore",
                innerCts => BlobStoreHttpClient.TryReferenceAsync(references, cancellationToken: innerCts),
                context.Token).ConfigureAwait(false);

            // There's 1-1 mapping between given content hashes and blob ids
            var remoteResults = blobIds
                .Select((blobId, i) =>
                {
                    var pinResult = referenceResults.ContainsKey(blobId)
                        ? PinResult.ContentNotFound
                        : PinResult.Success;
                    if (pinResult.Succeeded)
                    {
                        ExpiryCache.AddExpiry(contentHashes[i], endDateTime);
                        _counters[BackingContentStore.SessionCounters.PinSatisfiedFromRemote].Increment();
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
            // TODO: allow cached expiry time to be within some bump threshold (e.g. allow expiryTime = 6 days & endDateTime = 7 days) (bug 1365340)
            if (ExpiryCache.TryGetExpiry(contentHash, out var expiryTime) &&
                expiryTime > endDateTime)
            {
                _counters[BackingContentStore.SessionCounters.PinSatisfiedInMemory].Increment();
                return PinResult.Success;
            }

            // if (DownloadUriCache.TryGetDownloadUri(contentHash, out var authenticatedUri) &&
            //     authenticatedUri.ExpiryTime > endDateTime)
            // {
            //     _counters[BackingContentStore.SessionCounters.PinSatisfiedInMemory].Increment();
            //     return PinResult.Success;
            // }

            return PinResult.ContentNotFound;
        }

        private Task<long?> PlaceFileInternalAsync(
            OperationContext context, ContentHash contentHash, string path, FileMode fileMode)
        {
#if PLATFORM_WIN
            if (Configuration.DownloadBlobsUsingHttpClient)
            {
                return DownloadUsingHttpDownloaderAsync(context, contentHash, path);
            }
#endif

            return DownloadUsingAzureBlobsAsync(context, contentHash, path, fileMode);
        }

        private Task<long?> DownloadUsingAzureBlobsAsync(
            OperationContext context, ContentHash contentHash, string path, FileMode fileMode)
        {

            return AsyncHttpRetryHelper<long?>.InvokeAsync(
                async () =>
                {
                    StreamWithRange? httpStream = null;
                    Uri? uri = null;
                    try
                    {
                        httpStream = await GetStreamInternalAsync(context, contentHash, 0, null).ConfigureAwait(false);
                        if (httpStream == null)
                        {
                            return null;
                        }

                        try
                        {
                            var success = UriCache.TryGetDownloadUri(contentHash, out var preauthUri);
                            uri = success ? preauthUri.NotNullUri : new Uri("http://empty.com");

                            Directory.CreateDirectory(Directory.GetParent(path)!.FullName);

                            // TODO: Investigate using ManagedParallelBlobDownloader instead (bug 1365340)
                            await ParallelHttpDownload.Download(
                                _parallelSegmentDownloadConfig,
                                uri,
                                httpStream.Value,
                                destinationPath: path,
                                mode: fileMode,
                                cancellationToken: context.Token,
                                logSegmentStart: (destinationPath, offset, endOffset) => { },
                                logSegmentStop: (destinationPath, offset, endOffset) => { },
                                logSegmentFailed: (destinationPath, offset, endOffset, message) =>
                                    Tracer.Debug(context, $"Download {destinationPath} [{offset}, {endOffset}) failed. (message: {message})"),
                                segmentFactory: async (offset, length, token) =>
                                {
                                    var offsetStream = await GetStreamInternalAsync(
                                        context,
                                        contentHash,
                                        offset,
                                        (int?)_parallelSegmentDownloadConfig.SegmentSizeInBytes).ConfigureAwait(false);

                                    return offsetStream.Value;
                                }).ConfigureAwait(false);
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

                            TraceException(context, contentHash, uri, e);
                            throw;
                        }

                        return httpStream.Value.Range.WholeLength;
                    }
                    catch (StorageException storageEx) when (storageEx.InnerException is WebException webEx)
                    {
                        if (((HttpWebResponse?)webEx.Response)?.StatusCode == HttpStatusCode.NotFound)
                        {
                            return null;
                        }

                        TraceException(context, contentHash, uri, storageEx);
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
                cancellationToken: context.Token,
                continueOnCapturedContext: false,
                context: context.TracingContext.TraceId);
        }

#if PLATFORM_WIN
        private async Task<long?> DownloadUsingHttpDownloaderAsync(OperationContext context, ContentHash contentHash, string path)
        {
            var downloader = new ManagedParallelBlobDownloader(
                _parallelSegmentDownloadConfig,
                new AppTraceSourceContextAdapter(context, Tracer.Name, SourceLevels.All),
                VssClientHttpRequestSettings.Default.SessionId,
                _httpClient);
            var uri = await GetUriAsync(context, contentHash);
            if (uri == null)
            {
                return null;
            }

            DownloadResult result = await downloader.DownloadAsync(path, uri.ToString(), knownSize: null, cancellationToken: context.Token);

            if (result.HttpStatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }
            else if (result.HttpStatusCode != HttpStatusCode.OK)
            {
                throw new ResultPropagationException(new ErrorResult($"Error in DownloadAsync({uri}) => [{path}]: HttpStatusCode={result.HttpStatusCode}. ErrorCode={result.ErrorCode}"));
            }

            return result.BytesDownloaded;
        }
#endif

        private bool IsErrorFileExists(Exception e) => (Marshal.GetHRForException(e) & ((1 << 16) - 1)) == ErrorFileExists;

        private async Task<Uri?> GetUriAsync(OperationContext context, ContentHash contentHash)
        {
            if (!UriCache.TryGetDownloadUri(contentHash, out var uri))
            {
                _blobCounters[Counters.VstsDownloadUriFetchedFromRemote].Increment();
                var blobId = contentHash.ToBlobIdentifier();

                var mappings = await ArtifactHttpClientErrorDetectionStrategy.ExecuteWithTimeoutAsync(
                    context,
                    "GetStreamInternal",
                    innerCts => BlobStoreHttpClient.GetDownloadUrisAsync(
                        new[] { blobId },
                        EdgeCache.NotAllowed,
                        cancellationToken: innerCts),
                    context.Token).ConfigureAwait(false);

                if (mappings == null || !mappings.TryGetValue(blobId, out uri))
                {
                    return null;
                }

                UriCache.AddDownloadUri(contentHash, uri);
            }
            else
            {
                _blobCounters[Counters.VstsDownloadUriFetchedInMemory].Increment();
            }

            return uri.NotNullUri;
        }

        private async Task<StreamWithRange?> GetStreamInternalAsync(OperationContext context, ContentHash contentHash, long offset, int? overrideStreamMinimumReadSizeInBytes)
        {
            Uri? azureBlobUri = default;
            try
            {
                azureBlobUri = await GetUriAsync(context, contentHash);
                if (azureBlobUri == null)
                {
                    return null;
                }

                return await GetStreamThroughAzureBlobsAsync(
                    azureBlobUri,
                    offset,
                    overrideStreamMinimumReadSizeInBytes,
                    _parallelSegmentDownloadConfig.SegmentDownloadTimeout,
                    context.Token).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                TraceException(context, contentHash, azureBlobUri, e);
                throw;
            }
        }

        private void TraceException(OperationContext context, ContentHash hash, Uri? azureBlobUri, Exception e, [CallerMemberName] string? operation = null)
        {
            string errorMessage = $"{operation} failed. ContentHash=[{hash.ToShortString()}], BaseAddress=[{BlobStoreHttpClient.BaseAddress}], BlobUri=[{getBlobUri(azureBlobUri)}]";

            // Explicitly trace all the failures here to simplify errors analysis.
            Tracer.Debug(context, $"{errorMessage}. Error=[{e}]");

            static string getBlobUri(Uri? uri)
            {
                if (uri == null)
                {
                    return "null";
                }

                // The uri can represent a sas token, so we need to exclude the query part of it to avoid printing security sensitive information.
                // Getting Uri.ToString() instead of UriBuilder.ToString(), because the builder will add a port in the output string
                // even if it was not presented in the original string.
                return new UriBuilder(uri) { Query = string.Empty }.Uri.ToString();
            }
        }

        private async Task<StreamWithRange> GetStreamThroughAzureBlobsAsync(Uri azureUri, long offset, int? overrideStreamMinimumReadSizeInBytes, TimeSpan? requestTimeout, CancellationToken cancellationToken)
        {
            var blob = new CloudBlockBlob(azureUri);
            if (overrideStreamMinimumReadSizeInBytes.HasValue)
            {
                blob.StreamMinimumReadSizeInBytes = overrideStreamMinimumReadSizeInBytes.Value;
            }

            var stream = await blob.OpenReadAsync(
                null,
                new BlobRequestOptions()
                {
                    MaximumExecutionTime = requestTimeout,

                    // See also:
                    // ParallelOperationThreadCount
                    // RetryPolicy
                    // ServerTimeout
                },
                null, cancellationToken).ConfigureAwait(false);

            stream.Position = offset;

            var range = new ContentRangeHeaderValue(offset, stream.Length - 1, stream.Length);

            return new StreamWithRange(stream, new StreamRange(range));
        }

        /// <inheritdoc />
        protected override CounterSet GetCounters()
        {
            return base.GetCounters()
                .Merge(_counters.ToCounterSet())
                .Merge(_blobCounters.ToCounterSet());

        }

        /// <inheritdoc />
        public async Task<IEnumerable<Task<PinResult>>> PinAsync(OperationContext context, IReadOnlyList<ContentHash> contentHashes, DateTime keepUntil)
        {
            var results = await context.PerformNonResultOperationAsync(
                Tracer,
                () => PinCoreImplAsync(context, contentHashes, keepUntil),
                counter: BaseCounters[ContentSessionBaseCounters.PinBulk],
                traceOperationStarted: TraceOperationStarted,
                traceErrorsOnly: TraceErrorsOnly,
                extraEndMessage: results =>
                {
                    var resultString = string.Join(",", results.Select(task =>
                    {
                        // Since all bulk operations are constructed with Task.FromResult, it is safe to just access the result;
                        var result = task.Result;
                        return $"{contentHashes[result.Index].ToShortString()}:{result.Item}";
                    }));

                    return $"Count={contentHashes.Count}, KeepUntil=[{keepUntil}] Hashes=[{resultString}]";
                });

            return results.Select(task =>
            {
                var indexedResult = task.Result;
                return Task.FromResult(indexedResult.Item);
            });
        }
    }
}
