// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Azure.Storage.Sas;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Auth;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Core.Tasks;
using OperationContext = BuildXL.Cache.ContentStore.Tracing.Internal.OperationContext;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// An <see cref="CentralStorage"/> backed by Azure blob storage.
    /// </summary>
    public class BlobCentralStorage : CentralStreamStorage
    {
        private readonly (BlobContainerClient container, int shardId)[] _containers;
        private readonly BlobContainerClient _primaryContainer;
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

        /// <nodoc />
        public BlobCentralStorage(BlobCentralStoreConfiguration configuration)
        {
            _configuration = configuration;

            _containers = _configuration.Credentials.Select(
                (credentials, index) =>
                {
                    Contract.Requires(credentials != null);
                    return (credentials.CreateContainerClient(configuration.ContainerName), shardId: index);
                }).ToArray();

            _primaryContainer = _containers[0].container;

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
                var blob = container.GetBlockBlobClient(storageId);
                var exists = await blob.ExistsAsync(context.Token);

                if (exists.Value)
                {
                    var policy = blob.GenerateSasUri(BlobSasPermissions.Read, expiresOn: expiry);

                    await TouchShardBlobAsync(context, container, shardId, storageId).ThrowIfFailure();

                    return policy.AbsoluteUri;
                }

                Tracer.Debug(context, $@"Could not find '{_configuration.ContainerName}\{storageId}' from shard #{shardId}.");
            }

            return new ErrorResult($@"Could not find '{_configuration.ContainerName}\{storageId}'");
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> TryGetFileCoreAsync(OperationContext context, string blobName, AbsolutePath targetCheckpointFile, bool isImmutable)
        {
            BoolResult? attemptResult = null;
            foreach (var (container, shardId) in _containers)
            {
                using (var fileStream = _fileSystem.OpenForWrite(targetCheckpointFile, expectingLength: null, FileMode.Create, FileShare.Delete))
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

            Contract.Assert(attemptResult != null, $"BlobCentralStorage should have at least one container but has '{_containers.Length}'.");
            return attemptResult!;
        }

        private Task<BoolResult> TryGetShardFileAsync(
            OperationContext context,
            BlobContainerClient container,
            int shardId,
            Stream fileStream,
            string blobName,
            AbsolutePath targetCheckpointFile)
        {
            return context.PerformOperationWithTimeoutAsync(Tracer, async nestedContext =>
            {
                var blob = container.GetBlockBlobClient(blobName);
                var exists = await blob.ExistsAsync(nestedContext.Token);
                if (!exists.Value)
                {
                    // The blob may be missing, because we could've picked the new shard.
                    return new BoolResult($@"Recoverable error: Checkpoint blob '{_configuration.ContainerName}\{blobName}' does not exist in shard #{shardId}.");
                }

                _fileSystem.CreateDirectory(targetCheckpointFile.GetParent());

                Tracer.Debug(
                    nestedContext,
                    $@"Downloading blob '{_configuration.ContainerName}\{blobName}' to {targetCheckpointFile} from shard #{shardId}.");

                await blob.DownloadToAsync(
                    fileStream,
                    new BlobDownloadToOptions(),
                    cancellationToken: nestedContext.Token);

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
            if (e is RequestFailedException rfe && rfe.Status == (int)HttpStatusCode.ServiceUnavailable)
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

        private Task<BoolResult> TouchShardBlobAsync(OperationContext context, BlobContainerClient container, int shardId, string blobName)
        {
            return context.PerformOperationWithTimeoutAsync(
                Tracer,
                async nestedContext =>
                {
                    var blob = await GetBlockBlobClientAsync(container, shardId, blobName, nestedContext.Token);
                    var exists = await blob.ExistsAsync(nestedContext.Token);

                    if (exists.Value)
                    {
                        await TouchShardBlobCoreAsync(shardId, blobName, nestedContext, blob);
                    }
                    else
                    {
                        return new BoolResult(errorMessage: $@"Checkpoint blob '{_configuration.ContainerName}\{blobName}' does not exist in shard #{shardId}.");
                    }

                    return BoolResult.Success;
                },
                timeout: _configuration.OperationTimeout);
        }

        private Task TouchShardBlobCoreAsync(int shardId, string blobName, OperationContext nestedContext, BlockBlobClient blob)
        {
            var now = DateTime.UtcNow;
            Tracer.Debug(
                nestedContext,
                $@"Touching blob '{_configuration.ContainerName}\{blobName}' of with access time {now} for shard #{shardId}.");

            return blob.SetMetadataAsync(
                new Dictionary<string, string>() { { LastAccessedMetadataName, now.ToReadableString() } },
                cancellationToken: nestedContext.Token);
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

        private Task<BoolResult> UploadShardFileAsync(OperationContext context, BlobContainerClient container, int shardId, AbsolutePath file, string blobName, bool garbageCollect)
        {
            long fileSize = -1;
            return context.PerformOperationWithTimeoutAsync(Tracer, async nestedContext =>
            {
                fileSize = new System.IO.FileInfo(file.ToString()).Length;

                var blob = await GetBlockBlobClientAsync(container, shardId, blobName, nestedContext.Token);

                // WARNING: There is a TOCTOU issue here. If there are multiple concurrent writers to a single blob,
                // there's no way to tell which upload will win the race. Moreover, it is possible for a blob to be
                // overwritten.
                // Since BlobCentralStorage is meant to upload files with unique names only, we don't care about this
                // particular use-case: it should basically never happen, and when it does, it shouldn't matter because
                // the file should be the same.
                var exists = await blob.ExistsAsync(nestedContext.Token);
                if (!exists)
                {
                    using (var fileStream = await _fileSystem.OpenReadOnlyAsync(file, FileShare.Read | FileShare.Delete))
                    {
                        await blob.UploadAsync(
                            fileStream,
                            new BlobUploadOptions(),
                            cancellationToken: nestedContext.Token);
                    }
                }
                else
                {
                    await TouchShardBlobCoreAsync(shardId, blobName, nestedContext, blob);
                }

                if (garbageCollect && _configuration.EnableGarbageCollect)
                {
                    // Only GC every after retention time.
                    if (!_gcLastRunTime.IsRecent(SystemClock.Instance.UtcNow, _configuration.RetentionTime))
                    {
                        TriggerGarbageCollection(nestedContext, container, shardId);
                    }
                }

                return BoolResult.Success;
            },
            counter: Counters[CentralStorageCounters.UploadShardFile],
            traceOperationStarted: false,
            extraEndMessage: _ => $"ShardId=[{shardId}] BlobName=[{_configuration.ContainerName}/{blobName}] FilePath=[{file}] FileSize=[{fileSize}]",
            timeout: _configuration.OperationTimeout);
        }

        private async Task<BlockBlobClient> GetBlockBlobClientAsync(BlobContainerClient container, int shardId, string blobName, CancellationToken token)
        {
            // For newly created stamps container may be missing.
            await CreateContainerIfNeededAsync(container, shardId, token);

            return container.GetBlockBlobClient(blobName);
        }

        private async Task CreateContainerIfNeededAsync(BlobContainerClient container, int shardId, CancellationToken token)
        {
            if (!_containersCreated[shardId])
            {
                using (await _containerCreationGate.AcquireAsync(token))
                {
                    if (!_containersCreated[shardId])
                    {
                        await container.CreateIfNotExistsAsync(cancellationToken: token);
                        _containersCreated[shardId] = true;
                    }
                }
            }
        }

        internal void TriggerGarbageCollection(OperationContext context, BlobContainerClient container, int shardId)
        {
            context.PerformOperationAsync(Tracer, () =>
            {
                return _gcGate.DeduplicatedOperationAsync(
                    (_, _) => GarbageCollectCoreAsync(context, container, shardId),
                    (_, _) => BoolResult.SuccessTask,
                    token: context.Token);
            },
            traceOperationStarted: false).FireAndForget(context);
        }

        private async Task<BoolResult> GarbageCollectCoreAsync(OperationContext context, BlobContainerClient container, int shardId)
        {
            var expiredThreshold = DateTime.UtcNow - _configuration.RetentionTime;
            Tracer.Debug(context, $"Collecting blobs with last access time earlier than {expiredThreshold} for shard #{shardId}.");
            using (var timer = Counters[CentralStorageCounters.CollectStaleBlobs].Start())
            {
                int totalNumberOfBlobs = 0;
                int numberOfDeletedBlobs = 0;

                await foreach (var item in container.GetBlobsAsync(cancellationToken: context.Token))
                {
                    totalNumberOfBlobs++;

                    DateTime? lastAccessTime = null;
                    if (item.Metadata.TryGetValue(LastAccessedMetadataName, out var lastAccessTimeString))
                    {
                        lastAccessTime = DateTimeUtilities.FromReadableTimestamp(lastAccessTimeString);
                    }

                    lastAccessTime ??= item.Properties.LastAccessedOn?.UtcDateTime;
                    lastAccessTime ??= item.Properties.LastModified?.UtcDateTime;

                    if (lastAccessTime < expiredThreshold)
                    {
                        var blob = container.GetBlobClient(item.Name);
                        if (await blob.DeleteIfExistsAsync(cancellationToken: context.Token))
                        {
                            numberOfDeletedBlobs++;
                            Tracer.Debug(context, $@"Deleted blob '{_configuration.ContainerName}\{item.Name}' for shard #{shardId}. LastAccessTime={lastAccessTime}.");
                        }
                        else
                        {
                            Tracer.Debug(context, $@"Failed deleting blob '{_configuration.ContainerName}\{item.Name}' for shard #{shardId}. LastAccessTime={lastAccessTime}.");
                        }
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

        /// <inheritdoc />
        public bool IsTransient(Exception ex) => IsRecoverableStorageException(ex);

        /// <inheritdoc />
        protected override async Task<TResult> ReadCoreAsync<TResult>(OperationContext context, string storageId, Func<StreamWithLength, Task<TResult>> readStreamAsync)
        {
            var blob = await GetBlockBlobClientAsync(_containers[0].container, _containers[0].shardId, storageId, context.Token);
            try
            {
                using var stream = await blob.OpenReadAsync(new BlobOpenReadOptions(allowModifications: false), cancellationToken: context.Token);
                return await readStreamAsync(stream.WithLength(stream.Length));
            }
            catch (RequestFailedException ex)
            {
                return new ErrorResult(ex, $@"Failed to read '{_configuration.ContainerName}/{storageId}'").AsResult<TResult>();
            }
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StoreCoreAsync(OperationContext context, string storageId, Stream stream)
        {
            var blob = await GetBlockBlobClientAsync(_containers[0].container, _containers[0].shardId, storageId, context.Token);
            await blob.UploadAsync(stream, new BlobUploadOptions(), cancellationToken: context.Token);
            return BoolResult.Success;
        }
    }
}
