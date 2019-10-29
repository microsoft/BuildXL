// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Utils;

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    /// Base implementation for maintaining content quota.
    /// </summary>
    public abstract class QuotaRule : IQuotaRule
    {
        /// <summary>
        /// Definition for content quota.
        /// </summary>
        protected ContentStoreQuota _quota;

        private readonly DistributedEvictionSettings _distributedEvictionSettings;
        private readonly EvictAsync _evictAsync;

        /// <inheritdoc />
        public bool OnlyUnlinked { get; }

        /// <inheritdoc />
        public ContentStoreQuota Quota => _quota;

        /// <summary>
        ///     Initializes a new instance of the <see cref="QuotaRule" /> class.
        /// </summary>
        protected QuotaRule(EvictAsync evictAsync, bool onlyUnlinked, DistributedEvictionSettings distributedEvictionSettings = null)
        {
            _distributedEvictionSettings = distributedEvictionSettings;
            _evictAsync = evictAsync;
            OnlyUnlinked = onlyUnlinked;
        }

        /// <summary>
        /// Checks if reserve size is inside limit.
        /// </summary>
        public abstract BoolResult IsInsideLimit(long limit, long reserveCount);

        /// <inheritdoc />
        public Task<PurgeResult> PurgeAsync(
            Context context,
            long reserveSize,
            IReadOnlyList<ContentHashWithLastAccessTimeAndReplicaCount> contentHashesWithInfo,
            CancellationToken token)
        {
            if (_distributedEvictionSettings != null)
            {
                Contract.Assert(_distributedEvictionSettings.IsInitialized);

                if (_distributedEvictionSettings.DistributedStore?.CanComputeLru == true)
                {
                    return EvictDistributedWithDistributedStoreAsync(context, contentHashesWithInfo, reserveSize, token);
                }

                return EvictDistributedAsync(context, contentHashesWithInfo, reserveSize, token);
            }
            else
            {
                return EvictLocalAsync(context, contentHashesWithInfo, reserveSize, token);
            }
        }

        /// <inheritdoc />
        public virtual BoolResult IsInsideHardLimit(long reserveSize = 0)
        {
            return IsInsideLimit(_quota.Hard, reserveSize);
        }

        /// <inheritdoc />
        public virtual BoolResult IsInsideSoftLimit(long reserveSize = 0)
        {
            return IsInsideLimit(_quota.Soft, reserveSize);
        }

        /// <inheritdoc />
        public virtual BoolResult IsInsideTargetLimit(long reserveSize = 0)
        {
            return IsInsideLimit(_quota.Target, reserveSize);
        }

        /// <inheritdoc />
        public virtual bool CanBeCalibrated => false;

        /// <inheritdoc />
        public virtual Task<CalibrateResult> CalibrateAsync()
        {
            return Task.FromResult(CalibrateResult.CannotCalibrate);
        }

        /// <inheritdoc />
        public virtual bool IsEnabled
        {
            get
            {
                // Always enabled.
                return true;
            }

            set
            {
                // Do nothing.
            }
        }

        private async Task<PurgeResult> EvictLocalAsync(
            Context context,
            IReadOnlyList<ContentHashWithLastAccessTimeAndReplicaCount> contentHashesWithInfo,
            long reserveSize,
            CancellationToken cts)
        {
            var result = new PurgeResult();

            foreach (var contentHashInfo in contentHashesWithInfo)
            {
                if (StopPurging(reserveSize, cts, out _))
                {
                    break;
                }

                var r = await _evictAsync(context, contentHashInfo, OnlyUnlinked);
                if (!r.Succeeded)
                {
                    var errorResult = new PurgeResult(r);
                    errorResult.Merge(result);
                    return errorResult;
                }

                result.Merge(r);
            }

            return result;
        }

        private bool StopPurging(long reserveSize, CancellationToken cts, out string stopReason)
        {
            if (cts.IsCancellationRequested)
            {
                stopReason = "cancellation requested";
                return true;
            }

            if (IsInsideTargetLimit(reserveSize).Succeeded)
            {
                stopReason = "inside target limit";
                return true;
            }

            stopReason = null;
            return false;
        }

        /// <summary>
        /// Evict hashes in LRU-ed order determined by remote last-access times.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="hashesToPurge">Hashes in LRU-ed order based on local last-access time.</param>
        /// <param name="reserveSize">Reserve size.</param>
        /// <param name="cts">Cancellation token source.</param>
        private async Task<PurgeResult> EvictDistributedWithDistributedStoreAsync(
            Context context,
            IReadOnlyList<ContentHashWithLastAccessTimeAndReplicaCount> hashesToPurge,
            long reserveSize,
            CancellationToken cts)
        {
            var result = new PurgeResult(reserveSize, hashesToPurge.Count, _quota.ToString());

            var evictedContent = new List<ContentHash>();
            var distributedStore = _distributedEvictionSettings.DistributedStore;

            foreach (var contentHashInfo in distributedStore.GetHashesInEvictionOrder(context, hashesToPurge))
            {
                if (StopPurging(reserveSize, cts, out var stopReason))
                {
                    result.StopReason = stopReason;
                    break;
                }

                if (contentHashInfo.Age < _distributedEvictionSettings.MinimumEvictionTime)
                {
                    // Print eviction size, reserveSize??
                    // Print number of files previously returned for eviction this cycle
                    // Print pool size, min/max ages and effective ages
                    // ReplicaCount (contentHashInfo.ReplicaCount), Size, and Effective Age (contentHashInfo.EffectiveAge)
                }

                var r = await _evictAsync(context, contentHashInfo, OnlyUnlinked);
                if (!r.Succeeded)
                {
                    var errorResult = new PurgeResult(r);
                    errorResult.Merge(result);
                    return errorResult;
                }

                if (r.SuccessfullyEvictedHash)
                {
                    evictedContent.Add(contentHashInfo.ContentHash);
                }

                result.Merge(r);
            }

            var unregisterResult = await distributedStore.UnregisterAsync(context, evictedContent, cts);
            if (!unregisterResult)
            {
                var errorResult = new PurgeResult(unregisterResult);
                errorResult.Merge(result);
                return errorResult;
            }

            return result;
        }

        /// <summary>
        /// Evict hashes in LRU-ed order determined by remote last-access times.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="hashesToPurge">Hashes in LRU-ed order based on local last-access time.</param>
        /// <param name="reserveSize">Reserve size.</param>
        /// <param name="cts">Cancellation token source.</param>
        private async Task<PurgeResult> EvictDistributedAsync(
            Context context,
            IReadOnlyList<ContentHashWithLastAccessTimeAndReplicaCount> hashesToPurge,
            long reserveSize,
            CancellationToken cts)
        {
            // Track hashes for final update.
            // Item1 marks whether content was removed from Redis because it was safe to evict.
            // Item2 is the last-access time as seen by the data center.
            var unpurgedHashes = new Dictionary<ContentHash, Tuple<bool, DateTime>>();
            var evictedHashes = new List<ContentHash>();

            // Purge all unpinned content where local last-access time is in sync with remote last-access time.
            var purgeInfo = await AttemptPurgeAsync(context, hashesToPurge, cts, reserveSize, unpurgedHashes, evictedHashes);

            if (!purgeInfo.finishedPurging)
            {
                return purgeInfo.purgeResult; // Purging encountered an error.
            }

            // Update unused hashes during eviction in the data center and locally.
            foreach (var contentHashWithAccessTime in unpurgedHashes)
            {
                var unregisteredFromRedis = contentHashWithAccessTime.Value.Item1;
                var remoteLastAccessTime = contentHashWithAccessTime.Value.Item2;

                if (unregisteredFromRedis)
                {
                    // Re-register hash in the content tracker for use
                    _distributedEvictionSettings.ReregisterHashQueue.Enqueue(contentHashWithAccessTime.Key);
                }

                // Update local last-access time with remote last-access time for future use.
                // Don't update when remote last-access time is DateTime.MinValue because that means the hash has aged out of the content tracker.
                if (remoteLastAccessTime != DateTime.MinValue)
                {
                    // Last-access time will only be updated if it is more recent than the locally saved one
                    await _distributedEvictionSettings
                        .UpdateContentWithLastAccessTimeAsync(contentHashWithAccessTime.Key, contentHashWithAccessTime.Value.Item2);
                }
            }

            if (_distributedEvictionSettings.DistributedStore != null)
            {
                var unregisterResult = await _distributedEvictionSettings.DistributedStore.UnregisterAsync(context, evictedHashes, cts);
                if (!unregisterResult)
                {
                    return new PurgeResult(unregisterResult);
                }
            }

            return purgeInfo.purgeResult;
        }

        /// <summary>
        /// Attempts to evict hashes in hashesToPurge until the reserveSize is met or all hashes with in-sync local and remote last-access times have been evicted.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="hashesToPurge">Hashes sorted in LRU-ed order.</param>
        /// <param name="cts">Cancellation token source.</param>
        /// <param name="reserveSize">Reserve size.</param>
        /// <param name="unpurgedHashes">Hashes that were checked in the data center but not evicted.</param>
        /// <param name="evictedHashes">list of evicted hashes</param>
        private async Task<(bool finishedPurging, PurgeResult purgeResult)> AttemptPurgeAsync(
            Context context,
            IReadOnlyList<ContentHashWithLastAccessTimeAndReplicaCount> hashesToPurge,
            CancellationToken cts,
            long reserveSize,
            Dictionary<ContentHash, Tuple<bool, DateTime>> unpurgedHashes,
            List<ContentHash> evictedHashes)
        {
            var finalResult = new PurgeResult();
            var finishedPurging = false;

            var trimOrGetLastAccessTimeAsync = _distributedEvictionSettings.TrimOrGetLastAccessTimeAsync;
            var batchSize = _distributedEvictionSettings.LocationStoreBatchSize;
            var pinAndSizeChecker = _distributedEvictionSettings.PinnedSizeChecker;
            var replicaCreditInMinutes = _distributedEvictionSettings.ReplicaCreditInMinutes;

            var hashQueue = CreatePriorityQueue(hashesToPurge, replicaCreditInMinutes);

            while (!finishedPurging && hashQueue.Count > 0)
            {
                var contentHashesWithInfo = GetLruBatch(hashQueue, batchSize);
                var unpinnedHashes = GetUnpinnedHashesAndCompilePinnedSize(context, contentHashesWithInfo, pinAndSizeChecker, finalResult);
                if (!unpinnedHashes.Any())
                {
                    continue; // No hashes in this batch are able to evicted because they're all pinned
                }

                // If unpurgedHashes contains hash, it was checked once in the data center. We relax the replica restriction on retry
                var unpinnedHashesWithCheck = unpinnedHashes.Select(hash => Tuple.Create(hash, !unpurgedHashes.ContainsKey(hash.ContentHash))).ToList();

                // Unregister hashes that can be safely evicted and get distributed last-access time for the rest
                var contentHashesInfoRemoteResult =
                    await trimOrGetLastAccessTimeAsync(context, unpinnedHashesWithCheck, cts, UrgencyHint.High);

                if (!contentHashesInfoRemoteResult.Succeeded)
                {
                    var errorResult = new PurgeResult(contentHashesInfoRemoteResult);
                    errorResult.Merge(finalResult);
                    return (finishedPurging, errorResult);
                }

                var purgeInfo = await ProcessHashesForEvictionAsync(
                    contentHashesInfoRemoteResult.Data,
                    reserveSize,
                    cts,
                    context,
                    finalResult,
                    unpurgedHashes,
                    hashQueue,
                    evictedHashes);

                if (purgeInfo.purgeResult != null)
                {
                    return purgeInfo; // Purging encountered an error.
                }

                finishedPurging = purgeInfo.finishedPurging;
            }

            return (finishedPurging, finalResult);
        }

        // Get up to a page of records to delete, prioritizing the least-wanted files.
        private IEnumerable<ContentHashWithLastAccessTimeAndReplicaCount> GetLruBatch(
            PriorityQueue<ContentHashWithLastAccessTimeAndReplicaCount> hashQueue,
            int batchSize)
        {
            var candidates = new List<ContentHashWithLastAccessTimeAndReplicaCount>(batchSize);
            while (candidates.Count < batchSize && hashQueue.Count > 0)
            {
                var candidate = hashQueue.Top;
                hashQueue.Pop();
                candidates.Add(candidate);
            }

            return candidates;
        }

        private IList<ContentHashWithLastAccessTimeAndReplicaCount> GetUnpinnedHashesAndCompilePinnedSize(
            Context context,
            IEnumerable<ContentHashWithLastAccessTimeAndReplicaCount> hashesToPurge,
            PinnedSizeChecker pinnedSizeChecker,
            PurgeResult purgeResult)
        {
            long totalPinnedSize = 0; // Compile aggregate pinnedSize for the fail faster case
            var unpinnedHashes = new List<ContentHashWithLastAccessTimeAndReplicaCount>();

            foreach (var hashInfo in hashesToPurge)
            {
                var pinnedSize = pinnedSizeChecker(context, hashInfo.ContentHash);
                if (pinnedSize >= 0)
                {
                    totalPinnedSize += pinnedSize;
                }
                else
                {
                    unpinnedHashes.Add(hashInfo);
                }
            }

            purgeResult.MergePinnedSize(totalPinnedSize);
            return unpinnedHashes;
        }

        private async Task<(bool finishedPurging, PurgeResult purgeResult)> ProcessHashesForEvictionAsync(
            IList<ContentHashWithLastAccessTimeAndReplicaCount> contentHashesWithRemoteInfo,
            long reserveSize,
            CancellationToken cts,
            Context context,
            PurgeResult finalResult,
            Dictionary<ContentHash, Tuple<bool, DateTime>> unpurgedHashes,
            PriorityQueue<ContentHashWithLastAccessTimeAndReplicaCount> hashQueue,
            List<ContentHash> evictedHashes)
        {
            bool finishedPurging = false;

            foreach (var contentHashWithRemoteInfo in contentHashesWithRemoteInfo)
            {
                if (StopPurging(reserveSize, cts, out _))
                {
                    finishedPurging = true;
                }

                var trackHash = true;

                if (!finishedPurging)
                {
                    // If not done purging and locations is negative, safe to evict immediately because contentHash has either:
                    //      1) Aged out of content tracker
                    //      2) Has matching last-access time (implying that the hash's last-access time is in sync with the datacenter)
                    if (contentHashWithRemoteInfo.SafeToEvict)
                    {
                        var evictResult = await _evictAsync(
                            context,
                            contentHashWithRemoteInfo,
                            OnlyUnlinked);

                        if (!evictResult.Succeeded)
                        {
                            var errorResult = new PurgeResult(evictResult);
                            errorResult.Merge(finalResult);
                            return (finishedPurging, errorResult);
                        }

                        finalResult.Merge(evictResult);

                        // SLIGHT HACK: Only want to keep track of hashes that unsuccessfully evicted and were unpinned at eviction time.
                        // We can determine that it was unpinned by PinnedSize, which is pinned bytes encountered during eviction.
                        trackHash = !evictResult.SuccessfullyEvictedHash && evictResult.PinnedSize == 0;
                    }
                    else
                    {
                        hashQueue.Push(contentHashWithRemoteInfo);
                    }
                }

                if (trackHash)
                {
                    unpurgedHashes[contentHashWithRemoteInfo.ContentHash] = Tuple.Create(contentHashWithRemoteInfo.SafeToEvict, contentHashWithRemoteInfo.LastAccessTime);
                }
                else
                {
                    // Don't track hash to update with remote last-access time because it was evicted
                    unpurgedHashes.Remove(contentHashWithRemoteInfo.ContentHash);
                    evictedHashes.Add(contentHashWithRemoteInfo.ContentHash);
                }
            }

            return (finishedPurging, (PurgeResult)null);
        }

        private PriorityQueue<ContentHashWithLastAccessTimeAndReplicaCount> CreatePriorityQueue(
            IReadOnlyList<ContentHashWithLastAccessTimeAndReplicaCount> hashesToPurge,
            int replicaCreditInMinutes)
        {
            var hashQueue = new PriorityQueue<ContentHashWithLastAccessTimeAndReplicaCount>(
                hashesToPurge.Count,
                new ContentHashWithLastAccessTimeAndReplicaCount.ByLastAccessTime(replicaCreditInMinutes));

            foreach (var hashInfo in hashesToPurge)
            {
                hashQueue.Push(hashInfo);
            }

            return hashQueue;
        }
    }
}
