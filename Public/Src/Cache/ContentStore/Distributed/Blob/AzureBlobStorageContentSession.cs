// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Azure;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;
using OperationContext = BuildXL.Cache.ContentStore.Tracing.Internal.OperationContext;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Blobs
{
    public sealed class AzureBlobStorageContentSession : ContentSessionBase
    {
        /// <summary>
        /// Which strategy to use for bulk pinning
        /// </summary>
        /// <remarks>
        /// As of 2022/07/14, bulk strategies do not work when using the Storage Emulator, because the emulator doesn't
        /// have support for the API calls required.
        /// </remarks>
        public enum BulkPinStrategy
        {
            /// <summary>
            /// Bulk requests are split into individual pin requests, which are satisfied by individual existence
            /// checks.
            /// </summary>
            Individual,

            /// <summary>
            /// Use batch requests to always fail to delete a content blob.
            /// </summary>
            /// <remarks>
            /// 1. It is unsafe to delete a content blob if content backing guarantees are being assumed.
            ///
            /// 2. The client accessing this must have permissions to delete blobs.
            ///
            /// 3. If an adversary obtains the SAS token given to the client, the adversary is able to cause build
            ///    failures by deleting blobs.
            ///
            /// 4. It is not possible to introduce artifacts into the build by utilizing this technique.
            /// </remarks>
            BulkDelete,

            /// <summary>
            /// Use batch requests to change the blob access tiers to Hot on pin.
            /// </summary>
            /// <remarks>
            /// - This can only be used in non-premium storage accounts.
            ///
            /// - It's unknown which specific kind of SAS tokens are required to use this method.
            ///
            /// - This is mainly intended to be used in trusted environments (i.e., SAS token scoping is not needed)
            ///   with non-premium storage accounts.
            /// </remarks>
            BulkSetTier,
        }

        public record Configuration(
            string Name,
            ImplicitPin ImplicitPin,
            AzureBlobStorageContentStore Parent,
            TimeSpan StorageInteractionTimeout,
            BlobDownloadStrategyConfiguration BlobDownloadStrategyConfiguration,
            BulkPinStrategy BulkPinStrategy)
        {
        }

        protected override Tracer Tracer { get; } = new Tracer(nameof(AzureBlobStorageContentSession));

        private readonly Configuration _configuration;

        private readonly IAbsFileSystem _fileSystem = PassThroughFileSystem.Default;

        private readonly IClock _clock = SystemClock.Instance;

        private static readonly BlobRequestOptions DefaultBlobStorageRequestOptions = new BlobRequestOptions()
        {
            RetryPolicy = new Microsoft.WindowsAzure.Storage.RetryPolicies.ExponentialRetry(),
        };

        private readonly IBlobDownloadStrategy _downloadStrategy;

        public AzureBlobStorageContentSession(Configuration configuration)
            : base(configuration.Name)
        {
            _configuration = configuration;
            _downloadStrategy = BlobDownloadStrategyFactory.Create(configuration.BlobDownloadStrategyConfiguration);
        }

        #region IContentSession Implementation

        protected override Task<PinResult> PinCoreAsync(
            OperationContext context,
            ContentHash contentHash,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            if (contentHash.IsEmptyHash())
            {
                return Task.FromResult(new PinResult(PinResult.ResultCode.Success));
            }

            return PinRemoteAsync(context, contentHash);
        }

        protected override Task<IEnumerable<Task<Indexed<PinResult>>>> PinCoreAsync(
            OperationContext context,
            IReadOnlyList<ContentHash> contentHashes,
            UrgencyHint urgencyHint,
            Counter retryCounter,
            Counter fileCounter)
        {
            return _configuration.BulkPinStrategy switch
            {
                BulkPinStrategy.Individual => BulkIndividualPinAsync(context, contentHashes, urgencyHint, retryCounter),
                BulkPinStrategy.BulkDelete or BulkPinStrategy.BulkSetTier => BulkPinAsync(context, contentHashes, _configuration.BulkPinStrategy),
                _ => throw new NotImplementedException($"Unknown bulk pin strategy `{_configuration.BulkPinStrategy}`"),
            };
        }

        private async Task<IEnumerable<Task<Indexed<PinResult>>>> BulkPinAsync(
            OperationContext context,
            IReadOnlyList<ContentHash> contentHashes,
            BulkPinStrategy strategy)
        {
            var batchClient = _configuration.Parent.GetBlobBatchClient();

            // The Blob Batch API doesn't support more than 256 operations per batch, so we limit it here
            const int PageLimit = 255;
            var tasks = new Task<IEnumerable<PinResult>>[(contentHashes.Count / PageLimit) + 1];
            var i = 0;
            foreach (var contentHashSubset in contentHashes.GetPages(PageLimit))
            {
                tasks[i++] = BulkPinRemoteAsync(context, batchClient, contentHashSubset, strategy);
            }

            var results = await TaskUtilities.SafeWhenAll(tasks);
            return results.SelectMany((batchResults, index) =>
            {
                return batchResults.Select((pinResult, subindex) =>
                {
                    return Task.FromResult(pinResult.WithIndex(index * PageLimit + subindex));
                });
            });
        }

        private async Task<IEnumerable<PinResult>> BulkPinRemoteAsync(
            OperationContext context,
            BlobBatchClient batchClient,
            IReadOnlyList<ContentHash> contentHashes,
            BulkPinStrategy strategy)
        {
            var responses = new Response?[contentHashes.Count];

            var batch = batchClient.CreateBatch();
            foreach (var indexed in contentHashes.AsIndexed())
            {
                var blobClient = _configuration.Parent.GetBlobClient(indexed.Item);

                Response? response = null;
                if (!indexed.Item.IsEmptyHash())
                {
                    switch (strategy)
                    {
                        case BulkPinStrategy.BulkDelete:
                            response = batch.DeleteBlob(
                                blobClient.Uri,
                                Azure.Storage.Blobs.Models.DeleteSnapshotsOption.None,
                                new Azure.Storage.Blobs.Models.BlobRequestConditions()
                                {
                                    // This request condition will always fail to pass. This is on purpose.
                                    IfModifiedSince = _clock.UtcNow + TimeSpan.FromDays(7),
                                });
                            break;
                        case BulkPinStrategy.BulkSetTier:
                            response = batch.SetBlobAccessTier(
                                blobClient.Uri,
                                Azure.Storage.Blobs.Models.AccessTier.Hot,
                                Azure.Storage.Blobs.Models.RehydratePriority.Standard,
                                leaseAccessConditions: null);
                            break;
                        default:
                            throw new NotImplementedException($"Unknown bulk pin strategy `{strategy}`");
                    }
                }

                responses[indexed.Index] = response;
            }

            var batchResponse = await batchClient.SubmitBatchAsync(
                batch,
                throwOnAnyFailure: false,
                cancellationToken: context.Token);

            return contentHashes.Select((contentHash, index) =>
            {
                var response = responses[index];

                if (response is null)
                {
                    // Empty hash case, we didn't even send a request
                    return PinResult.Success;
                }
                else if (response.Status == 404)
                {
                    return PinResult.ContentNotFound;
                }
                else if (response.Status == 412 || (response.Status >= 200 && response.Status < 300))
                {
                    // Condition not met. This happens when doing the bulk delete pin.
                    return PinResult.Success;
                }
                else
                {
                    return new PinResult(errorMessage: $"Pin for content hash `{contentHash}` failed with error message: {response.ReasonPhrase}");
                }
            });
        }

        private async Task<IEnumerable<Task<Indexed<PinResult>>>> BulkIndividualPinAsync(
            OperationContext context,
            IReadOnlyList<ContentHash> contentHashes,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            var tasks = contentHashes.Select((contentHash, index) =>
            {
                return PinCoreAsync(
                    context,
                    contentHash,
                    urgencyHint,
                    retryCounter).WithIndexAsync(index);
            });

            await TaskUtilities.SafeWhenAll(tasks);
            return tasks;
        }

        protected override async Task<OpenStreamResult> OpenStreamCoreAsync(
            OperationContext context,
            ContentHash contentHash,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            if (contentHash.IsEmptyHash())
            {
                return new OpenStreamResult(new MemoryStream(Array.Empty<byte>()).WithLength(0));
            }

            var stream = await OpenRemoteStreamAsync(context, contentHash).ThrowIfFailureAsync();
            return new OpenStreamResult(stream);
        }

        private Task<Result<StreamWithLength?>> OpenRemoteStreamAsync(OperationContext context, ContentHash contentHash)
        {
            return context.PerformOperationWithTimeoutAsync(
                Tracer,
                async context =>
                {
                    var reference = GetCloudBlockBlobReference(contentHash);

                    Stream readStream;
                    try
                    {
                        readStream = await reference.OpenReadAsync(
                            accessCondition: null,
                            options: DefaultBlobStorageRequestOptions,
                            operationContext: null,
                            cancellationToken: context.Token);
                    }
                    catch (StorageException exception)
                    {
                        // See: https://docs.microsoft.com/en-us/rest/api/storageservices/blob-service-error-codes
                        // See: https://docs.microsoft.com/en-us/rest/api/storageservices/Specifying-Conditional-Headers-for-Blob-Service-Operations#Subheading3
                        if (exception.RequestInformation.HttpStatusCode == (int)HttpStatusCode.NotFound)
                        {
                            return Result.Success<StreamWithLength?>(null, isNullAllowed: true);
                        }
                        else
                        {
                            throw;
                        }
                    }

                    return Result.Success<StreamWithLength?>(readStream.WithLength(readStream.Length));
                },
                traceOperationStarted: false,
                timeout: _configuration.StorageInteractionTimeout,
                extraEndMessage: r =>
                {
                    long size = -1;
                    if (r.Succeeded && r.Value is not null)
                    {
                        size = r.Value.Value.Length;
                    }

                    return $"ContentHash=[{contentHash}] Size=[{size}]";
                });
        }

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
            if (replacementMode is FileReplacementMode.SkipIfExists or FileReplacementMode.FailIfExists && _fileSystem.FileExists(path))
            {
                return new PlaceFileResult(PlaceFileResult.ResultCode.NotPlacedAlreadyExists);
            }

            if (contentHash.IsEmptyHash())
            {
                _fileSystem.CreateEmptyFile(path);
                return new PlaceFileResult(PlaceFileResult.ResultCode.PlacedWithCopy, fileSize: 0);
            }

            var remoteDownloadResult = await PlaceRemoteFileAsync(context, contentHash, path, accessMode, replacementMode).ThrowIfFailureAsync();
            return new PlaceFileResult(remoteDownloadResult.ResultCode, remoteDownloadResult.FileSize ?? 0, source: PlaceFileResult.Source.BackingStore);
        }
        
        private Task<Result<RemoteDownloadResult>> PlaceRemoteFileAsync(OperationContext context, ContentHash contentHash, AbsolutePath path, FileAccessMode accessMode, FileReplacementMode replacementMode)
        {
            return context.PerformOperationWithTimeoutAsync(
                Tracer,
                async context =>
                {
                    var reference = GetCloudBlockBlobReference(contentHash);

                    var sasUrlQuery = reference.GetSharedAccessSignature(new SharedAccessBlobPolicy()
                    {
                        Permissions = SharedAccessBlobPermissions.Read,
                        SharedAccessExpiryTime = _clock.UtcNow + TimeSpan.FromDays(1),
                    });

                    var downloadUrl = reference.Uri.AbsoluteUri + sasUrlQuery;

                    var downloadRequest = new RemoteDownloadRequest()
                    {
                        ContentHash = contentHash,
                        AbsolutePath = path,
                        Reference = reference,
                        DownloadUrl = downloadUrl,
                    };

                    RemoteDownloadResult remoteDownloadResult;
                    try
                    {
                        remoteDownloadResult = await _downloadStrategy.DownloadAsync(context, downloadRequest);
                    }
                    catch
                    {
                        _fileSystem.DeleteFile(downloadRequest.AbsolutePath);
                        throw;
                    }

                    return Result.Success(remoteDownloadResult);
                },
                traceOperationStarted: false,
                timeout: _configuration.StorageInteractionTimeout,
                extraEndMessage: r =>
                {
                    var baseline = $"ContentHash=[{contentHash.ToShortString()}] Path=[{path}] AccessMode=[{accessMode}] ReplacementMode=[{replacementMode}]";
                    if (!r.Succeeded)
                    {
                        return baseline;
                    }

                    var d = r.Value;
                    return $"{baseline} {r.Value}";
                });
        }

        protected override async Task<IEnumerable<Task<Indexed<PlaceFileResult>>>> PlaceFileCoreAsync(
            OperationContext context,
            IReadOnlyList<ContentHashWithPath> hashesWithPaths,
            FileAccessMode accessMode,
            FileReplacementMode replacementMode,
            FileRealizationMode realizationMode,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            var tasks = hashesWithPaths.Select((contentHashWithPath, index) =>
            {
                return PlaceFileCoreAsync(
                    context,
                    contentHashWithPath.Hash,
                    contentHashWithPath.Path,
                    accessMode,
                    replacementMode,
                    realizationMode,
                    urgencyHint,
                    retryCounter).WithIndexAsync(index);
            });

            await TaskUtilities.SafeWhenAll(tasks);
            return tasks;
        }

        protected override async Task<PutResult> PutFileCoreAsync(
            OperationContext context,
            HashType hashType,
            AbsolutePath path,
            FileRealizationMode realizationMode,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            using var streamWithLength = _fileSystem.Open(
                path,
                FileAccess.Read,
                FileMode.Open,
                FileShare.Read,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await PutStreamCoreAsync(context, hashType, streamWithLength.Stream, urgencyHint, retryCounter);
        }

        protected override async Task<PutResult> PutFileCoreAsync(
            OperationContext context,
            ContentHash contentHash,
            AbsolutePath path,
            FileRealizationMode realizationMode,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            using var streamWithLength = _fileSystem.Open(
                path,
                FileAccess.Read,
                FileMode.Open,
                FileShare.Read,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await UploadFromStreamAsync(
                context,
                contentHash,
                streamWithLength.Stream,
                streamWithLength.Length);
        }

        protected override Task<PutResult> PutStreamCoreAsync(
            OperationContext context,
            ContentHash contentHash,
            Stream stream,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            // This is on purpose because we don't know what the stream implementation is here.
            long contentSize = -1;
            try
            {
                contentSize = stream.Length;
            }
            catch
#pragma warning disable ERP022 // Unobserved exception in a generic exception handler
            {
            }
#pragma warning restore ERP022 // Unobserved exception in a generic exception handler

            return UploadFromStreamAsync(context, contentHash, stream, contentSize);
        }

        protected override async Task<PutResult> PutStreamCoreAsync(
            OperationContext context,
            HashType hashType,
            Stream stream,
            UrgencyHint urgencyHint,
            Counter retryCounter)
        {
            // We can't hash while uploading because the name of the blob must be determined before uploading, so we
            // need to hash before.
            var position = stream.Position;
            var streamWithLength = stream.WithLength(stream.Length - position);
            var contentHash = await HashInfoLookup.GetContentHasher(hashType).GetContentHashAsync(streamWithLength);
            stream.Position = position;

            return await UploadFromStreamAsync(
                context,
                contentHash,
                streamWithLength.Stream,
                streamWithLength.Length);
        }

#endregion

        private Task<PinResult> PinRemoteAsync(
            OperationContext context,
            ContentHash contentHash)
        {
            return context.PerformOperationWithTimeoutAsync(
                Tracer,
                async context => {
                    var reference = GetCloudBlockBlobReference(contentHash);

                    bool exists = await reference.ExistsAsync(
                            options: DefaultBlobStorageRequestOptions,
                            operationContext: null,
                            cancellationToken: context.Token);

                    if (exists)
                    {
                        return new PinResult(
                            code: PinResult.ResultCode.Success,
                            lastAccessTime: reference.Properties.LastModified?.UtcDateTime,
                            contentSize: reference.Properties.Length);
                    }
                    else
                    {
                        return PinResult.ContentNotFound;
                    }
                },
                traceOperationStarted: false,
                extraEndMessage: _ => $"ContentHash=[{contentHash}]",
                timeout: _configuration.StorageInteractionTimeout);
        }

        private Task<PutResult> UploadFromStreamAsync(
            OperationContext context,
            ContentHash contentHash,
            Stream stream,
            long contentSize)
        {
            return context.PerformOperationWithTimeoutAsync(
                Tracer,
                async context =>
                {
                    var reference = GetCloudBlockBlobReference(contentHash);

                    // WARNING: remember implicit pin

                    // TODO: bandwidth checking?
                    // TODO: timeouts
                    bool contentAlreadyExistsInCache = false;
                    try
                    {
                        // TODO: setup parallel upload by using storage options (ParallelOperationThreadCount)
                        // TODO: setup cancellation time via storage options (MaximumExecutionTime / ServerTimeout)
                        // TODO: ideally here we'd also hash in the bg and cancel the op if it turns out the hash
                        // doesn't match as a protective measure against trusted puts with the wrong hash.
                        await reference.UploadFromStreamAsync(
                            stream,
                            accessCondition: AccessCondition.GenerateIfNotExistsCondition(),
                            options: DefaultBlobStorageRequestOptions,
                            operationContext: null,
                            cancellationToken: context.Token);
                    }
                    catch (StorageException exception)
                    {
                        // See: https://docs.microsoft.com/en-us/rest/api/storageservices/blob-service-error-codes
                        // See: https://docs.microsoft.com/en-us/rest/api/storageservices/Specifying-Conditional-Headers-for-Blob-Service-Operations#Subheading3
                        if (exception.RequestInformation.HttpStatusCode == (int)HttpStatusCode.PreconditionFailed
                            || exception.RequestInformation.ErrorCode == "BlobAlreadyExists")
                        {
                            contentAlreadyExistsInCache = true;
                        }
                        else
                        {
                            throw;
                        }
                    }

                    return new PutResult(contentHash, (int)contentSize, contentAlreadyExistsInCache);
                },
                traceOperationStarted: false,
                timeout: _configuration.StorageInteractionTimeout);
        }

        private CloudBlockBlob GetCloudBlockBlobReference(ContentHash contentHash)
        {
            return _configuration.Parent.GetCloudBlockBlobReference(contentHash);
        }
    }
}
