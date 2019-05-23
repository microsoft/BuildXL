// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Utilities.Tasks;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using DateTimeUtilities = BuildXL.Cache.ContentStore.Utils.DateTimeUtilities;
using OperationContext = BuildXL.Cache.ContentStore.Tracing.Internal.OperationContext;
using static BuildXL.Cache.ContentStore.Utils.DateTimeUtilities;

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

        private DateTime _lastGcTime = DateTime.MinValue;

        private const string LastAccessedMetadataName = "LastAccessed";

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(BlobCentralStorage));

        /// <nodoc />
        public BlobCentralStorage(BlobCentralStoreConfiguration configuration)
        {
            Contract.Requires(configuration.ConnectionStrings.Count != 0);

            _configuration = configuration;

            _containers = _configuration.ConnectionStrings.Select(
                (connectionString, index) =>
                {
                    var storage = CloudStorageAccount.Parse(connectionString);
                    var blobClient = storage.CreateCloudBlobClient();
                    return (blobClient.GetContainerReference(configuration.ContainerName), shardId: index);
                }).ToArray();

            // Need to shuffle all the connection strings to reduce the traffic over the storage accounts.
            _containers.Shuffle();

            _containersCreated = new bool[_configuration.ConnectionStrings.Count];
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> TryGetFileCoreAsync(OperationContext context, string blobName, AbsolutePath targetCheckpointFile)
        {
            AttemptResult attemptResult = null;
            foreach (var (container, shardId) in _containers)
            {
                using (var fileStream = await _fileSystem.OpenSafeAsync(targetCheckpointFile, FileAccess.Write, FileMode.Create, FileShare.Delete))
                {
                    attemptResult = await TryGetShardFileAsync(context, container, shardId, fileStream, blobName, targetCheckpointFile);

                    if (!attemptResult && attemptResult.CanRetry)
                    {
                        Tracer.Debug(context, $@"Failed getting a blob '{_configuration.ContainerName}\{blobName}' from shard #{shardId}: {attemptResult}.");
                    }

                    if (attemptResult || !attemptResult.CanRetry)
                    {
                        // Break if the operation succeeded or can not be retried.
                        break;
                    }
                }
            }

            Contract.Assert(attemptResult != null, $"BlobCentralStorage should have at least one container but has '{_containers.Length}'.");
            return (BoolResult)attemptResult;
        }

        private async Task<AttemptResult> TryGetShardFileAsync(
            OperationContext context,
            CloudBlobContainer container,
            int shardId,
            Stream fileStream,
            string blobName,
            AbsolutePath targetCheckpointFile)
        {
            try
            {
                AttemptResult result = await TaskUtilities.WithTimeoutAsync(async token =>
                    {
                        var blob = container.GetBlockBlobReference(blobName);
                        var exists = await blob.ExistsAsync(null, null, token);

                        if (exists)
                        {
                            _fileSystem.CreateDirectory(targetCheckpointFile.Parent);

                            Tracer.Debug(context, $@"Downloading blob '{_configuration.ContainerName}\{blobName}' to {targetCheckpointFile} from shard #{shardId}.");

                            await blob.DownloadToStreamAsync(fileStream, null, null, null, token);
                        }
                        else
                        {
                            // The blob may be missing, because we could've picked the new shard.
                            return AttemptResult.RecoverableError(errorMessage: $@"Checkpoint blob '{_configuration.ContainerName}\{blobName}' does not exist in shard #{shardId}.");
                        }

                        return AttemptResult.SuccessResult;
                    },
                    _configuration.OperationTimeout,
                    context.Token);

                return result;
            }
            catch (Exception e)
            {
                bool isRecoverable = IsRecoverableStorageException(e);
                if (isRecoverable)
                {
                    // Non recoverable error would be traced differently as part of the operation result,
                    // but we need to trace recoverable errors as well for potential further analysis.
                    Tracer.Debug(context, $@"Downloading blob '{_configuration.ContainerName}\{blobName}' failed with recoverable exception: {e}.");
                }

                return AttemptResult.FromException(isRecoverable, e, context.Token);
            }
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
        protected override async Task<BoolResult> TouchBlobCoreAsync(OperationContext context, AbsolutePath file, string blobName, bool isUploader)
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
            return context.PerformOperationAsync(
                Tracer,
                () =>
                    TaskUtilities.WithTimeoutAsync(
                        async token =>
                        {
                            var blob = await GetBlockBlobReferenceAsync(container, shardId, blobName, token);
                            var exists = await blob.ExistsAsync(null, null, token);

                            if (exists)
                            {
                                var now = DateTime.UtcNow;
                                Tracer.Debug(
                                    context,
                                    $@"Touching blob '{_configuration.ContainerName}\{blobName}' of size {blob.Properties.Length} with access time {now} for shard #{shardId}.");
                                blob.Metadata[LastAccessedMetadataName] = now.ToReadableString();

                                await blob.SetMetadataAsync(null, null, null, token);
                            }
                            else
                            {
                                return new BoolResult(errorMessage: $@"Checkpoint blob '{_configuration.ContainerName}\{blobName}' does not exist in shard #{shardId}.");
                            }

                            return BoolResult.Success;
                        },
                        _configuration.OperationTimeout,
                        context.Token));
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
            return context.PerformOperationAsync(Tracer, () =>
            {
                return TaskUtilities.WithTimeoutAsync(async token =>
                {
                    var fileSize = new System.IO.FileInfo(file.ToString()).Length;

                    Tracer.Debug(context, $@"Uploading blob '{_configuration.ContainerName}\{blobName}' of size {fileSize} from {file} into shard #{shardId}.");

                    var blob = await GetBlockBlobReferenceAsync(container, shardId, blobName, token);

                    await blob.UploadFromFileAsync(file.ToString(), null, null, null, token);

                    if (garbageCollect)
                    {
                        // Only GC every after retention time. 
                        if (!_lastGcTime.IsRecent(DateTime.UtcNow, _configuration.RetentionTime))
                        {
                            await GarbageCollectAsync(context, container, shardId);
                            _lastGcTime = DateTime.UtcNow;
                        }
                    }

                    return BoolResult.Success;
                },
                _configuration.OperationTimeout,
                context.Token);
            },
            counter: Counters[CentralStorageCounters.UploadShardFile]);
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
                await container.CreateIfNotExistsAsync(
                    accessType: BlobContainerPublicAccessType.Off,
                    options: null,
                    operationContext: null,
                    cancellationToken: token);

                _containersCreated[shardId] = true;
            }
        }

        internal async Task GarbageCollectAsync(OperationContext context, CloudBlobContainer container, int shardId)
        {
            var expiredThreshold = DateTime.UtcNow - _configuration.RetentionTime;
            Tracer.Debug(context, $"Collecting blobs with last access time earlier than {expiredThreshold} for shard #{shardId}.");
            using (var timer = Counters[CentralStorageCounters.CollectStaleBlobs].Start())
            {
                int totalNumberOfBlobs = 0;
                int numberOfDeletedBlobs = 0;

                BlobContinuationToken continuationToken = null;
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
            }
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

        private class AttemptResult : BoolResult
        {
            /// <inheritdoc />
            private AttemptResult()
                : base(succeeded: true)
            {
            }

            /// <inheritdoc />
            private AttemptResult(ResultBase other, string message = null)
                : base(other, message)
            {
            }

            /// <inheritdoc />
            private AttemptResult(bool canRetry, string errorMessage, string diagnostics = null)
                : base(errorMessage, diagnostics)
            {
                CanRetry = canRetry;
            }

            /// <inheritdoc />
            private AttemptResult(bool canRetry, Exception exception, string message = null)
                : base(exception, message)
            {
                CanRetry = canRetry;
            }

            public bool CanRetry { get; }

            public static AttemptResult SuccessResult { get; } = new AttemptResult();
            public static AttemptResult FromResult(ResultBase other) => other.Succeeded ? SuccessResult : new AttemptResult(other);
            public static AttemptResult RecoverableError(string errorMessage) => new AttemptResult(canRetry: true, errorMessage: errorMessage);
            public static AttemptResult FromException(bool isRecoverable, Exception exception, CancellationToken contextToken) =>
                new AttemptResult(isRecoverable, exception)
                {
                    IsCancelled = contextToken.IsCancellationRequested && NonCriticalForCancellation(exception)
                };
        }
    }
}
