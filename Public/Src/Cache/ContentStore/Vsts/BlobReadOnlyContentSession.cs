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
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Utilities.Tracing;
using Microsoft.VisualStudio.Services.BlobStore.Common;
using Microsoft.VisualStudio.Services.BlobStore.WebApi;
using Microsoft.VisualStudio.Services.Content.Common;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using BlobIdentifier = BuildXL.Cache.ContentStore.Hashing.BlobIdentifier;
using ByteArrayPool = Microsoft.VisualStudio.Services.BlobStore.Common.ByteArrayPool;
using OperationContext = BuildXL.Cache.ContentStore.Tracing.Internal.OperationContext;
using VstsBlobIdentifier = Microsoft.VisualStudio.Services.BlobStore.Common.BlobIdentifier;

namespace BuildXL.Cache.ContentStore.Vsts
{
    /// <summary>
    ///     IReadOnlyContentSession for BlobBuildXL.ContentStore.
    /// </summary>
    [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    public class BlobReadOnlyContentSession : ContentSessionBase
    {
        /// <nodoc />
        private enum BlobContentSessionCounters
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

        private CounterCollection<BackingContentStore.SessionCounters> _counters { get; } = new CounterCollection<BackingContentStore.SessionCounters>();
        private CounterCollection<BlobContentSessionCounters> _blobCounters { get; } = new CounterCollection<BlobContentSessionCounters>();

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
        protected override Tracer Tracer { get; } = new Tracer(nameof(BlobContentSession));

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
        /// <param name="downloadBlobsThroughBlobStore">If true, gets blobs through BlobStore. If false, gets blobs from the Azure Uri.</param>
        public BlobReadOnlyContentSession(
            IAbsFileSystem fileSystem,
            string name,
            ImplicitPin implicitPin,
            IBlobStoreHttpClient blobStoreHttpClient,
            TimeSpan timeToKeepContent,
            bool downloadBlobsThroughBlobStore)
            : base(name)
        {
            Contract.Requires(fileSystem != null);
            Contract.Requires(name != null);
            Contract.Requires(blobStoreHttpClient != null);

            ImplicitPin = implicitPin;
            BlobStoreHttpClient = blobStoreHttpClient;
            TimeToKeepContent = timeToKeepContent;
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

            string tempFile = null;
            try
            {
                if (ImplicitPin == ImplicitPin.PutAndGet)
                {
                    var pinResult = await PinAsync(context, contentHash, context.Token, urgencyHint).ConfigureAwait(false);
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
                    return new PlaceFileResult(PlaceFileResult.ResultCode.NotPlacedAlreadyExists);
                }

                if (ImplicitPin == ImplicitPin.PutAndGet)
                {
                    var pinResult = await PinAsync(context, contentHash, context.Token, urgencyHint).ConfigureAwait(false);
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
                    await PlaceFileInternalAsync(context, contentHash, path.Path, fileMode).ConfigureAwait(false);
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
        protected override async Task<IEnumerable<Task<Indexed<PinResult>>>> PinCoreAsync(OperationContext context, IReadOnlyList<ContentHash> contentHashes, UrgencyHint urgencyHint, Counter retryCounter, Counter fileCounter)
        {
            try
            {
                var endDateTime = DateTime.UtcNow + TimeToKeepContent;

                return await Workflows.RunWithFallback(
                    contentHashes,
                    hashes => CheckInMemoryCaches(hashes, endDateTime),
                    hashes => UpdateBlobStoreAsync(context, hashes, endDateTime),
                    result => result.Succeeded);
            }
            catch (Exception ex)
            {
                context.TracingContext.Warning($"Exception when querying pins against the VSTS services {ex}");
                return contentHashes.Select((_, index) => Task.FromResult(new PinResult(ex).WithIndex(index)));
            }
        }

        /// <inheritdoc />
        protected override Task<IEnumerable<Task<Indexed<PlaceFileResult>>>> PlaceFileCoreAsync(OperationContext context, IReadOnlyList<ContentHashWithPath> hashesWithPaths, FileAccessMode accessMode, FileReplacementMode replacementMode, FileRealizationMode realizationMode, UrgencyHint urgencyHint, Counter retryCounter)
            => throw new NotImplementedException();

        /// <summary>
        /// Converts a ContentStore blob id to an artifact BlobId
        /// </summary>
        protected static VstsBlobIdentifier ToVstsBlobIdentifier(BlobIdentifier blobIdentifier) => new VstsBlobIdentifier(blobIdentifier.Bytes);

        private async Task<IEnumerable<Task<Indexed<PinResult>>>> UpdateBlobStoreAsync(OperationContext context, IReadOnlyList<ContentHash> contentHashes, DateTime endDateTime)
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
                context.Token).ConfigureAwait(false);

            _counters[BackingContentStore.SessionCounters.PinSatisfiedFromRemote].Increment();

            // There's 1-1 mapping between given content hashes and blob ids
            var remoteResults = blobIds
                .Select((blobId, i) =>
                {
                    var pinResult = referenceResults.ContainsKey(blobId)
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

            // TODO: allow cached expiry time to be within some bump threshold (e.g. allow expiryTime = 6 days & endDateTime = 7 days) (bug 1365340)
            if (BackingContentStoreExpiryCache.Instance.TryGetExpiry(contentHash, out var expiryTime) &&
                expiryTime > endDateTime)
            {
                _counters[BackingContentStore.SessionCounters.PinSatisfiedInMemory].Increment();
                return PinResult.Success;
            }

            if (DownloadUriCache.Instance.TryGetDownloadUri(contentHash, out var authenticatedUri) &&
                authenticatedUri.ExpiryTime > endDateTime)
            {
                _counters[BackingContentStore.SessionCounters.PinSatisfiedInMemory].Increment();
                return PinResult.Success;
            }

            return PinResult.ContentNotFound;
        }

        private Task<long?> PlaceFileInternalAsync(
            OperationContext context, ContentHash contentHash, string path, FileMode fileMode)
        {
            return AsyncHttpRetryHelper<long?>.InvokeAsync(
                async () =>
                {
                    Stream httpStream = null;
                    try
                    {
                        httpStream = await GetStreamInternalAsync(context, contentHash, null).ConfigureAwait(false);
                        if (httpStream == null)
                        {
                            return null;
                        }

                        try
                        {
                            var success = DownloadUriCache.Instance.TryGetDownloadUri(contentHash, out var preauthUri);
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
                                context.Token,
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
                                        _parallelSegmentDownloadConfig.SegmentSizeInBytes).ConfigureAwait(false);
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
                cancellationToken: context.Token,
                continueOnCapturedContext: false,
                context: context.TracingContext.Id.ToString());
        }

        private bool IsErrorFileExists(Exception e) => (Marshal.GetHRForException(e) & ((1 << 16) - 1)) == ErrorFileExists;

        private async Task<Stream> GetStreamInternalAsync(OperationContext context, ContentHash contentHash, int? overrideStreamMinimumReadSizeInBytes)
        {
            if (_downloadBlobsThroughBlobStore)
            {
                return await ArtifactHttpClientErrorDetectionStrategy.ExecuteWithTimeoutAsync(
                    context,
                    "GetStreamInternalThroughBlobStore",
                    innerCts => BlobStoreHttpClient.GetBlobAsync(ToVstsBlobIdentifier(contentHash.ToBlobIdentifier()), cancellationToken: innerCts),
                    context.Token).ConfigureAwait(false);
            }
            else
            {
                if (!DownloadUriCache.Instance.TryGetDownloadUri(contentHash, out var uri))
                {
                    _blobCounters[BlobContentSessionCounters.VstsDownloadUriFetchedFromRemote].Increment();
                    var blobId = contentHash.ToBlobIdentifier();

                    var mappings = await ArtifactHttpClientErrorDetectionStrategy.ExecuteWithTimeoutAsync(
                        context,
                        "GetStreamInternal",
                        innerCts => BlobStoreHttpClient.GetDownloadUrisAsync(
                            new[] { ToVstsBlobIdentifier(blobId) },
                            EdgeCache.NotAllowed,
                            cancellationToken: innerCts),
                        context.Token).ConfigureAwait(false);

                    if (mappings == null || !mappings.TryGetValue(ToVstsBlobIdentifier(blobId), out uri))
                    {
                        return null;
                    }

                    DownloadUriCache.Instance.AddDownloadUri(contentHash, uri);
                }
                else
                {
                    _blobCounters[BlobContentSessionCounters.VstsDownloadUriFetchedInMemory].Increment();
                }

                return await GetStreamThroughAzureBlobs(
                    uri.NotNullUri,
                    overrideStreamMinimumReadSizeInBytes,
                    _parallelSegmentDownloadConfig.SegmentDownloadTimeout,
                    context.Token).ConfigureAwait(false);
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

        /// <inheritdoc />
        protected override CounterSet GetCounters()
        {
            return base.GetCounters()
                .Merge(_counters.ToCounterSet())
                .Merge(_blobCounters.ToCounterSet());
            
        }
    }
}
