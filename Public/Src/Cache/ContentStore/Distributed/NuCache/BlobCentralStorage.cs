// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Tasks;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using ExponentialRetry = Microsoft.WindowsAzure.Storage.RetryPolicies.ExponentialRetry;
using OperationContext = BuildXL.Cache.ContentStore.Tracing.Internal.OperationContext;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// An <see cref="CentralStorage"/> backed by Azure blob storage.
    /// </summary>
    public class BlobCentralStorage : CentralStorage
    {
        private readonly (CloudBlobContainer container, int shardId)[] _containers;
        private readonly bool[] _containersCreated;

        private readonly BlobCentralStoreConfiguration _configuration;
        private readonly PassThroughFileSystem _fileSystem = new PassThroughFileSystem();
        private readonly IRetryPolicy _blobStorageRetryStrategy;

        private DateTime _gcLastRunTime = DateTime.MinValue;
        private readonly SemaphoreSlim _gcGate = TaskUtilities.CreateMutex();
        private readonly SemaphoreSlim _containerCreationGate = TaskUtilities.CreateMutex();

        private const string LastAccessedMetadataName = "LastAccessed";

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(BlobCentralStorage));

        private static readonly BlobRequestOptions DefaultBlobStorageRequestOptions = new BlobRequestOptions()
        {
            // Compute and store the MD5 hash of the stored file, which lets us validate the contents upon download
            StoreBlobContentMD5 = true,
            // Ensure content validation is activated client-side
            DisableContentMD5Validation = false,
            RetryPolicy = new ExponentialRetry(),
        };

        /// <nodoc />
        public BlobCentralStorage(BlobCentralStoreConfiguration configuration)
        {
            _configuration = configuration;

            _containers = _configuration.Credentials.Select(
                (credentials, index) =>
                {
                    Contract.Requires(credentials != null);
                    var cloudBlobClient = credentials.CreateCloudBlobClient();
                    return (cloudBlobClient.GetContainerReference(configuration.ContainerName), shardId: index);
                }).ToArray();
            _containers.Shuffle();

            _containersCreated = new bool[_configuration.Credentials.Count];
            _blobStorageRetryStrategy = RetryPolicyFactory.GetExponentialPolicy(IsTransient);
        }

        /// <inheritdoc />
        protected override void DisposeCore()
        {
            _fileSystem.Dispose();
        }

        /// <inheritdoc />
        public override bool SupportsSasUrls => true;

        /// <inheritdoc />
        protected override async Task<Result<string>> TryGetSasUrlCore(OperationContext context, string storageId, DateTime expiry)
        {
            foreach (var (container, shardId) in _containers)
            {
                var blob = container.GetBlockBlobReference(storageId);
                var exists = await blob.ExistsAsync(null, null, context.Token);

                if (exists)
                {
                    var policy = new SharedAccessBlobPolicy()
                    {
                        Permissions = SharedAccessBlobPermissions.Read,
                        SharedAccessExpiryTime = expiry
                    };

                    var sasUrlQuery = blob.GetSharedAccessSignature(policy);

                    await TouchShardBlobAsync(context, container, shardId, storageId).ThrowIfFailure();

                    return blob.Uri.AbsoluteUri + sasUrlQuery;
                }
                else
                {
                    Tracer.Debug(context, $@"Could not find '{_configuration.ContainerName}\{storageId}' from shard #{shardId}.");
                }
            }

            return new ErrorResult($@"Could not find '{_configuration.ContainerName}\{storageId}'");
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> TryGetFileCoreAsync(OperationContext context, string blobName, AbsolutePath targetCheckpointFile, bool isImmutable)
        {
            BoolResult? attemptResult = null;
            foreach (var (container, shardId) in _containers)
            {
                using (var fileStream = await _fileSystem.OpenSafeAsync(targetCheckpointFile, FileAccess.Write, FileMode.Create, FileShare.Delete))
                {
                    try
                    {
                        attemptResult = await _blobStorageRetryStrategy.ExecuteAsync(
                            () => TryGetShardFileAsync(context, container, shardId, fileStream, blobName, targetCheckpointFile),
                            context.Token);

                        if (attemptResult)
                        {
                            break;
                        }
                    }
                    catch (Exception e)
                    {
                        bool isRecoverable = IsRecoverableStorageException(e);

                        attemptResult = new BoolResult(e);
                        Tracer.Debug(context, $@"Failed getting a blob '{_configuration.ContainerName}\{blobName}' from shard #{shardId}: {attemptResult}.");

                        if (!isRecoverable)
                        {
                            break;
                        }
                    }
                }
            }

            Contract.Check(attemptResult != null)?.Assert($"BlobCentralStorage should have at least one container but has '{_containers.Length}'.");
            return attemptResult!;
        }

        private Task<BoolResult> TryGetShardFileAsync(
            OperationContext context,
            CloudBlobContainer container,
            int shardId,
            Stream fileStream,
            string blobName,
            AbsolutePath targetCheckpointFile)
        {
            return context.PerformOperationWithTimeoutAsync(Tracer, async nestedContext =>
            {
                var blob = container.GetBlockBlobReference(blobName);
                var exists = await blob.ExistsAsync(null, null, nestedContext.Token);

                if (!exists)
                {
                    // The blob may be missing, because we could've picked the new shard.
                    return new BoolResult($@"Recoverable error: Checkpoint blob '{_configuration.ContainerName}\{blobName}' does not exist in shard #{shardId}.");
                }

                _fileSystem.CreateDirectory(targetCheckpointFile.GetParent());

                Tracer.Debug(
                    nestedContext,
                    $@"Downloading blob '{_configuration.ContainerName}\{blobName}' to {targetCheckpointFile} from shard #{shardId}.");

                await blob.DownloadToStreamAsync(fileStream, null, DefaultBlobStorageRequestOptions, null, nestedContext.Token);

                return BoolResult.Success;
            },
            timeout: _configuration.OperationTimeout,
            traceOperationStarted: false);
        }

        private static bool IsRecoverableStorageException(Exception e)
        {
            if (e is TimeoutException)
            {
                return true;
            }

            // Check for throttling errors from a storage.
            if (e is StorageException && e.Message.Contains("503") && e.ToString().Contains("ErrorCode:ServerBusy"))
            {
                return true;
            }

            return false;
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> TouchBlobCoreAsync(OperationContext context, AbsolutePath file, string blobName, bool isUploader, bool isImmutable)
        {
            if (!isUploader)
            {
                // Only the uploader should touch the file since we just need some indication that the file is still in use.
                // Touches from downloaders would be redundant
                return BoolResult.Success;
            }

            var results = await Task.WhenAll(_containers.Select(tpl => TouchShardBlobAsync(context, tpl.container, tpl.shardId, blobName)).ToList());

            // The operation fails when all the shards failed.
            return results.Aggregate(BoolResult.Success, (result, boolResult) => result | boolResult);
        }

        private Task<BoolResult> TouchShardBlobAsync(OperationContext context, CloudBlobContainer container, int shardId, string blobName)
        {
            return context.PerformOperationWithTimeoutAsync(
                Tracer,
                async nestedContext =>
                {
                    var blob = await GetBlockBlobReferenceAsync(container, shardId, blobName, nestedContext.Token);
                    var exists = await blob.ExistsAsync(null, null, nestedContext.Token);

                    if (exists)
                    {
                        var now = DateTime.UtcNow;
                        Tracer.Debug(
                            nestedContext,
                            $@"Touching blob '{_configuration.ContainerName}\{blobName}' of size {blob.Properties.Length} with access time {now} for shard #{shardId}.");
                        blob.Metadata[LastAccessedMetadataName] = now.ToReadableString();

                        await blob.SetMetadataAsync(null, null, null, nestedContext.Token);
                    }
                    else
                    {
                        return new BoolResult(errorMessage: $@"Checkpoint blob '{_configuration.ContainerName}\{blobName}' does not exist in shard #{shardId}.");
                    }

                    return BoolResult.Success;
                },
                timeout: _configuration.OperationTimeout);
        }

        /// <inheritdoc />
        protected override async Task<Result<string>> UploadFileCoreAsync(OperationContext context, AbsolutePath file, string blobName, bool garbageCollect)
        {
            // Uploading files into all shards in parallel.
            var results = await Task.WhenAll(_containers.Select(tpl => UploadShardFileAsync(context, tpl.container, tpl.shardId, file, blobName, garbageCollect)).ToList());

            if (results.All(r => r))
            {
                // All the operations succeeded.
                return Result.Success(blobName);
            }

            // Operation fails if at least one operation has failed.
            var failure = results.First(r => !r);
            return new Result<string>(failure);
        }

        private Task<BoolResult> UploadShardFileAsync(OperationContext context, CloudBlobContainer container, int shardId, AbsolutePath file, string blobName, bool garbageCollect)
        {
            return context.PerformOperationWithTimeoutAsync(Tracer, async nestedContext =>
            {
                var fileSize = new System.IO.FileInfo(file.ToString()).Length;

                Tracer.Debug(nestedContext, $@"Uploading blob '{_configuration.ContainerName}\{blobName}' of size {fileSize} from {file} into shard #{shardId}.");

                var blob = await GetBlockBlobReferenceAsync(container, shardId, blobName, nestedContext.Token);

                await blob.UploadFromFileAsync(file.ToString(), null, DefaultBlobStorageRequestOptions, null, nestedContext.Token);

                if (garbageCollect && _configuration.EnableGarbageCollect)
                {
                    // Only GC every after retention time.
                    if (!_gcLastRunTime.IsRecent(SystemClock.Instance.UtcNow, _configuration.RetentionTime))
                    {
                        TriggerGarbageCollection(context, container, shardId);
                    }
                }

                return BoolResult.Success;
            },
            counter: Counters[CentralStorageCounters.UploadShardFile],
            timeout: _configuration.OperationTimeout);
        }

        private async Task<CloudBlockBlob> GetBlockBlobReferenceAsync(CloudBlobContainer container, int shardId, string blobName, CancellationToken token)
        {
            // For newly created stamps container may be missing.
            await CreateContainerIfNeededAsync(container, shardId, token);

            return container.GetBlockBlobReference(blobName);
        }

        private async Task CreateContainerIfNeededAsync(CloudBlobContainer container, int shardId, CancellationToken token)
        {
            if (!_containersCreated[shardId])
            {
                using (await _containerCreationGate.AcquireAsync(token))
                {
                    if (!_containersCreated[shardId])
                    {
                        await container.CreateIfNotExistsAsync(
                        accessType: BlobContainerPublicAccessType.Off,
                        options: null,
                        operationContext: null,
                        cancellationToken: token);

                        _containersCreated[shardId] = true;
                    }
                }
            }
        }

        /// <nodoc />
        public struct DerivedCheckpointInformation
        {
            /// <nodoc />
            public string StorageId { get; }

            /// <nodoc />
            public DateTime? CreationTime { get; }

            /// <nodoc />
            public DateTime? LastAccessTime { get; }

            /// <nodoc />
            public DerivedCheckpointInformation(CloudBlockBlob blob)
            {
                StorageId = blob.Name;
                CreationTime = blob.Properties.Created?.DateTime;
                LastAccessTime = GetLastAccessedTime(blob);
            }
        }

        /// <nodoc />
        public async IAsyncEnumerable<DerivedCheckpointInformation> ListBlobsWithNameMatchingAsync(OperationContext context, Regex regex)
        {
            foreach (var (container, shardId) in _containers)
            {
                await foreach (var entry in listForContainer(container, shardId))
                {
                    yield return entry;
                }
            }

            async IAsyncEnumerable<DerivedCheckpointInformation> listForContainer(CloudBlobContainer container, int shardId)
            {
                BlobContinuationToken? continuation = null;
                while (!context.Token.IsCancellationRequested)
                {
                    var blobs = await container.ListBlobsSegmentedAsync(
                        prefix: null,
                        useFlatBlobListing: true,
                        blobListingDetails: BlobListingDetails.Metadata,
                        maxResults: null,
                        currentToken: continuation,
                        options: null,
                        operationContext: null);
                    continuation = blobs.ContinuationToken;

                    foreach (CloudBlockBlob blob in blobs.Results.OfType<CloudBlockBlob>())
                    {
                        if (regex.IsMatch(blob.Name))
                        {
                            yield return new DerivedCheckpointInformation(blob);
                        }
                    }

                    if (continuation == null)
                    {
                        break;
                    }
                }

                yield break;
            }
        }

        internal void TriggerGarbageCollection(OperationContext context, CloudBlobContainer container, int shardId)
        {
            context.PerformOperationAsync(Tracer, () =>
            {
                return _gcGate.DeduplicatedOperationAsync(
                    (timeWaiting, currentCount) => GarbageCollectCoreAsync(context, container, shardId),
                    (timeWaiting, currentCount) => BoolResult.SuccessTask,
                    token: context.Token);
            },
            traceOperationStarted: false).FireAndForget(context);
        }

        private async Task<BoolResult> GarbageCollectCoreAsync(OperationContext context, CloudBlobContainer container, int shardId)
        {
            var expiredThreshold = DateTime.UtcNow - _configuration.RetentionTime;
            Tracer.Debug(context, $"Collecting blobs with last access time earlier than {expiredThreshold} for shard #{shardId}.");
            using (var timer = Counters[CentralStorageCounters.CollectStaleBlobs].Start())
            {
                int totalNumberOfBlobs = 0;
                int numberOfDeletedBlobs = 0;

                BlobContinuationToken? continuationToken = null;
                while (true)
                {
                    var result = await container.ListBlobsSegmentedAsync(
                        prefix: null,
                        useFlatBlobListing: true,
                        blobListingDetails: BlobListingDetails.Metadata,
                        maxResults: null,
                        currentToken: continuationToken,
                        options: null,
                        operationContext: null);

                    continuationToken = result.ContinuationToken;

                    foreach (CloudBlockBlob block in result.Results.OfType<CloudBlockBlob>())
                    {
                        totalNumberOfBlobs++;
                        var lastAccessTime = GetLastAccessedTime(block);
                        if (lastAccessTime < expiredThreshold)
                        {
                            if (await block.DeleteIfExistsAsync())
                            {
                                numberOfDeletedBlobs++;
                                Tracer.Debug(context, $@"Deleted blob '{_configuration.ContainerName}\{block.Name}' for shard #{shardId}. LastAccessTime={lastAccessTime}.");
                            }
                            else
                            {
                                Tracer.Debug(context, $@"Failed deleting blob '{_configuration.ContainerName}\{block.Name}' for shard #{shardId}. LastAccessTime={lastAccessTime}.");
                            }
                        }
                    }

                    if (continuationToken == null || context.Token.IsCancellationRequested)
                    {
                        break;
                    }
                }

                if (numberOfDeletedBlobs != 0)
                {
                    Tracer.Info(context, $"Deleted {numberOfDeletedBlobs} blobs out of {totalNumberOfBlobs} in {timer.Elapsed.TotalMilliseconds}ms for shard #{shardId}.");
                }

                _gcLastRunTime = DateTime.UtcNow;
            }

            return BoolResult.Success;
        }

        private static DateTime? GetLastAccessedTime(CloudBlockBlob block)
        {
            if (block.Metadata.TryGetValue(LastAccessedMetadataName, out var lastAccessTimeString))
            {
                return DateTimeUtilities.FromReadableTimestamp(lastAccessTimeString);
            }

            // Fall back to last modified time
            return block.Properties.LastModified?.UtcDateTime;
        }

        /// <inheritdoc />
        public bool IsTransient(Exception ex) => IsRecoverableStorageException(ex);
    }
}
