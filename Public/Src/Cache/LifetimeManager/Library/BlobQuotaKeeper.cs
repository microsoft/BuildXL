// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.MemoizationStore.Stores;
using BuildXL.Utilities.Core.Tasks;
using BuildXL.Utilities.ParallelAlgorithms;

namespace BuildXL.Cache.BlobLifetimeManager.Library
{
    /// <summary>
    /// This class takes in a <see cref="RocksDbLifetimeDatabase"/> and starts freeing up space based on the LRU enumeration
    /// of fingerprints provided by the DB. When performing deletions, this class will make sure that both the database and the remote cache
    /// reflect the changes necessary
    /// </summary>
    public class BlobQuotaKeeper
    {
        private static readonly Tracer Tracer = new(nameof(BlobQuotaKeeper));

        private readonly RocksDbLifetimeDatabase.IAccessor _database;
        private readonly IBlobCacheTopology _topology;
        private readonly IClock _clock;
        private readonly TimeSpan _lastAccessTimeDeletionThreshold;

        public BlobQuotaKeeper(
            RocksDbLifetimeDatabase.IAccessor database,
            IBlobCacheTopology topology,
            TimeSpan lastAccessTimeDeletionThreshold,
            IClock clock)
        {
            _database = database;
            _topology = topology;
            _lastAccessTimeDeletionThreshold = lastAccessTimeDeletionThreshold;
            _clock = clock;
        }

        /// <summary>
        /// Performs fingerprint deletions based on LRU ordering for fingerprints as per the database. Once a fingerprint is deleted from the remote, we will
        /// decrease the reference coutn for all of its contents, and attempt to delete those contents which reach a reference count of zero.
        ///
        /// For content that already has a reference count of zero, we only perform deletions when a certain amount of time has passed since
        /// it was last accessed.
        /// </summary>
        public Task<Result<long>> EnsureUnderQuota(
            OperationContext context,
            long maxSize,
            bool dryRun,
            int contentDegreeOfParallelism,
            int fingerprintDegreeOfParallelism)
        {
            Contract.Requires(contentDegreeOfParallelism > 0);
            Contract.Requires(fingerprintDegreeOfParallelism > 0);

            return context.PerformOperationAsync<Result<long>>(
                Tracer,
                async () =>
                {
                    var enumerationResult = _database.GetLruOrderedContentHashLists(context);

                    var currentSize = enumerationResult.TotalSize;

                    Tracer.Info(context, $"Total L3 size is calculated to be {currentSize}.");

                    if (currentSize < maxSize)
                    {
                        return currentSize;
                    }

                    Tracer.Info(context, "Starting enumeration of zero-reference content for garbage collection.");
                    using var underQuotaCts = new CancellationTokenSource();
                    await ParallelAlgorithms.EnumerateAsync<(ContentHash hash, long length)>(
                        enumerationResult.ZeroReferenceBlobs,
                        contentDegreeOfParallelism,
                        async hashAndLength =>
                        {
                            var deleted = await TryDeleteContentAsync(context, hashAndLength.hash, dryRun, hashAndLength.length);
                            if (deleted)
                            {
                                Interlocked.Add(ref currentSize, -hashAndLength.length);
                            }

                            if (currentSize <= maxSize)
                            {
                                underQuotaCts.Cancel();
                            }
                        },
                        underQuotaCts.Token);

                    if (underQuotaCts.IsCancellationRequested)
                    {
                        return currentSize;
                    }

                var tryDeleteContentHashActionBlock = ActionBlockSlim.CreateWithAsyncAction<(ContentHash hash, TaskCompletionSource<object?> tcs, OperationContext context)>(
                    configuration: new ActionBlockSlimConfiguration(contentDegreeOfParallelism),
                    async (tpl) =>
                    {
                        var (contentHash, tcs, opContext) = tpl;

                        try
                        {
                            await Task.Yield();

                            var contentEntry = _database.GetContentEntry(contentHash);
                            if (contentEntry is null || contentEntry.ReferenceCount > 0)
                            {
                                return;
                            }

                            var refCount = contentEntry.ReferenceCount;
                            if (refCount < 0)
                            {
                                Tracer.Error(opContext, $"Found new reference count to be {refCount}. Negative values should never happen, which points towards " +
                                    $"premature deletion of the piece of content.");
                                return;
                            }

                            var deleted = await TryDeleteContentAsync(opContext, contentHash, dryRun, contentEntry.BlobSize);
                            if (deleted)
                            {
                                Interlocked.Add(ref currentSize, -contentEntry.BlobSize);
                            }
                        }
                        catch (Exception ex)
                        {
                            Tracer.Debug(opContext, ex, $"Error when decrementing reference count for hash {contentHash.ToShortHash()}");
                        }
                        finally
                        {
                            tcs.SetResult(null);
                        }
                    },
                    context.Token);

                    Tracer.Info(context, "Starting LRU enumeration for fingerprint garbage collection.");
                    await ParallelAlgorithms.EnumerateAsync(
                        enumerationResult.LruOrderedContentHashLists,
                        fingerprintDegreeOfParallelism,
                        async chl =>
                        {
                            var opContext = context.CreateNested(nameof(BlobQuotaKeeper), caller: "TryDeleteContentHashList");

                            try
                            {
                                // Here is where we attempt to delete a fingerprint, and decrease the reference count for all its contents.
                                // If the new reference count for a blob is 0, we also attempt to delete the content.

                                var fingerprint = AzureBlobStorageMetadataStore.ExtractStrongFingerprintFromPath(chl.BlobName);
                                var container = await _topology.GetContainerClientAsync(context, BlobCacheShardingKey.FromWeakFingerprint(fingerprint.WeakFingerprint));
                                var client = container.GetBlobClient(chl.BlobName);

                                if (!await TryDeleteContentHashListAsync(opContext, client, chl, dryRun))
                                {
                                    return;
                                }

                                Interlocked.Add(ref currentSize, -chl.BlobSize);

                                _database.DeleteContentHashList(chl.BlobName, chl.Hashes);

                                // At this point the CHL is deleted and ref count for all content is decremented. Check which content is safe to delete.
                                var tasks = new List<Task>();
                                foreach (var contentHash in chl.Hashes)
                                {
                                    // We don't care about the result of the operation. We just want something we can await.
                                    var tcs = new TaskCompletionSource<object?>();
                                    tryDeleteContentHashActionBlock.Post((contentHash, tcs, opContext));
                                    tasks.Add(tcs.Task);
                                }

                                await TaskUtilities.SafeWhenAll(tasks);

                                Tracer.Debug(opContext, $"Current size: {currentSize}, max size: {maxSize}");

                                if (currentSize <= maxSize)
                                {
                                    underQuotaCts.Cancel();
                                }
                            }
                            catch (Exception ex)
                            {
                                Tracer.Error(opContext, ex, $"Error when processing fingerprint blob {chl.BlobName}.");
                            }
                        },
                        underQuotaCts.Token);

                    return currentSize;
                },
                extraEndMessage: result => $"MaxSize=[{maxSize}], CurrentSize=[{(result.Succeeded ? result.Value : null)}]");
        }

        private async Task<bool> TryDeleteContentAsync(OperationContext context, ContentHash contentHash, bool dryRun, long contentSize)
        {
            var client = await _topology.GetBlobClientAsync(context, contentHash);

            // It's possible that a fingerprint is currently being created that references this piece of content. This means there's a race condition that we need to account for.
            // Because of this, the current design is that clients will update the last access time of a blob when they get a content cache hit when ulpoading the contents of a new strong fingerprint.
            // On the GC side of things, what this means is that we have to check that the content has not been accessed recently.
            var lastAccessTime = await GetLastAccessTimeAsync(context, client);
            if (lastAccessTime > _clock.UtcNow.Add(-_lastAccessTimeDeletionThreshold))
            {
                Tracer.Debug(context,
                    $"Skipping deletion of {contentHash.ToShortString()} because it has been accessed too recently to be deleted. LastAccessTime=[{lastAccessTime}]");

                return false;
            }

            if (dryRun)
            {
                Tracer.Debug(
                    context,
                    $"DRY RUN: DELETE ContentHash=[{contentHash.ToShortString()}], BlobSize=[{contentSize}], Shard=[{client.AccountName}]");
            }
            else
            {
                var result = await DeleteBlobFromStorageAsync(context, client, contentSize, lastAccessTime);
                if (!result.Succeeded)
                {
                    return false;
                }
            }

            _database.DeleteContent(contentHash);

            return true;
        }

        private async Task<bool> TryDeleteContentHashListAsync(
            OperationContext context,
            BlobClient client,
            ContentHashList contentHashList,
            bool dryRun)
        {
            // Ideally instead of checking for last access time, we would do a conditional delete based on last access time. However,
            // that API doesn't exist. This leaves a race condition open where we might access the strong fingerprint in between the
            // last access time check and the deletion. However the timing is very precise and this wouldn't break the cache; we would
            // only be prematurely evicting a fingerprint.
            var currentLastAccessTime = await GetLastAccessTimeAsync(context, client);
            if (currentLastAccessTime is null)
            {
                // A null value means the blob does not exist. This must mean that the blob has already been deleted so it's safe to proceed as if we had
                // deleted it.
                return true;
            }

            if (currentLastAccessTime > contentHashList.LastAccessTime)
            {
                Tracer.Debug(context,
                    $"Current last access time for CHL '{contentHashList.BlobName}' is greater than the stored last access time. " +
                    $"Updating database and skipping deletion. " +
                    $"Current=[{currentLastAccessTime}], Stored=[{contentHashList.LastAccessTime}]");

                var updatedHashList = contentHashList with { LastAccessTime = currentLastAccessTime.Value };

                _database.UpdateContentHashListLastAccessTime(updatedHashList);

                return false;
            }

            if (currentLastAccessTime > _clock.UtcNow.Add(-_lastAccessTimeDeletionThreshold))
            {
                Tracer.Debug(context,
                    $"Skipping deletion of {contentHashList.BlobName} because it has been accessed too recently to be deleted. LastAccessTime=[{currentLastAccessTime}]");

                return false;
            }

            if (dryRun)
            {
                Tracer.Debug(
                    context,
                    $"DRY RUN: DELETE StrongFingerprint=[{contentHashList.BlobName}], LastAccessTime=[{contentHashList.LastAccessTime}], BlobSize=[{contentHashList.BlobSize}]");
            }
            else
            {
                var result = await DeleteBlobFromStorageAsync(context, client, contentHashList.BlobSize, currentLastAccessTime);
                return result.Succeeded;
            }

            return true;
        }

        internal static Task<Result<bool>> DeleteBlobFromStorageAsync(OperationContext context, BlobClient client, long size, DateTime? lastAccessTime)
        {
            return context.PerformOperationAsync<Result<bool>>(
                Tracer,
                async () =>
                {
                    try
                    {
                        var response = await client.DeleteAsync();
                        return response.IsError
                            ? new Result<bool>($"Failed to delete blob {client.Name}. Error ({response.Status}): {response.ReasonPhrase}")
                            : true;
                    }
                    catch (RequestFailedException e) when (e.ErrorCode == BlobErrorCode.BlobNotFound)
                    {
                        // Consider this a success, but trace for diagnostic purposes.
                        return false;
                    }
                },
                traceOperationStarted: false,
                extraEndMessage: result =>
                    $"BlobName=[{client.Name}], Size=[{size}], Shard=[{client.AccountName}], LastAccessTime=[{lastAccessTime}], BlobExisted=[{(result.Succeeded ? result.Value : null)}]");
        }

        private static async Task<DateTime?> GetLastAccessTimeAsync(OperationContext context, BlobClient client)
        {
            try
            {
                var response = await client.GetPropertiesAsync(cancellationToken: context.Token);
                return response.Value.LastAccessed.UtcDateTime;
            }
            catch (RequestFailedException e) when (e.ErrorCode == BlobErrorCode.BlobNotFound)
            {
                return null;
            }
        }
    }
}
