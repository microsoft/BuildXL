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
    internal sealed class Purger
    {
        private readonly DistributedEvictionSettings _distributedEvictionSettings;

        private readonly Context _context;
        private readonly QuotaKeeper _quotaKeeper;
        private readonly IReadOnlyList<ContentHashWithLastAccessTimeAndReplicaCount> _contentHashesWithInfo;
        private readonly PurgeResult _purgeResult;
        private readonly CancellationToken _token;

        /// <nodoc />
        public Purger(
            Context context,
            QuotaKeeper quotaKeeper,
            DistributedEvictionSettings distributedEvictionSettings,
            IReadOnlyList<ContentHashWithLastAccessTimeAndReplicaCount> contentHashesWithInfo,
            PurgeResult purgeResult,
            CancellationToken token)
        {
            _distributedEvictionSettings = distributedEvictionSettings;
            _context = context;
            _quotaKeeper = quotaKeeper;
            _contentHashesWithInfo = contentHashesWithInfo;
            _purgeResult = purgeResult;
            _token = token;
        }

        /// <nodoc />
        public async Task<PurgeResult> PurgeAsync()
        {
            var purgeSuccessResult = await PurgeCoreAsync();
            if (!purgeSuccessResult)
            {
                return new PurgeResult(purgeSuccessResult);
            }

            return _purgeResult;
        }

        private Task<BoolResult> PurgeCoreAsync()
        {
            if (_distributedEvictionSettings != null)
            {
                Contract.Assert(_distributedEvictionSettings.IsInitialized);

                if (_distributedEvictionSettings.DistributedStore?.CanComputeLru == true)
                {
                    return EvictDistributedWithDistributedStoreAsync();
                }

                // This case is possible only in non-lls mode. The method should be removed once non-lls code is gone.
                return EvictDistributedAsync();
            }
            else
            {
                return EvictLocalAsync();
            }
        }

        private bool StopPurging(out string stopReason, out IQuotaRule rule) => _quotaKeeper.StopPurging(out stopReason, out rule);

        private async Task<BoolResult> EvictLocalAsync()
        {
            foreach (var contentHashInfo in _contentHashesWithInfo)
            {
                if (StopPurging(out var stopReason, out var rule))
                {
                    _purgeResult.StopReason = stopReason;
                    break;
                }

                var evictionResult = await _quotaKeeper.EvictContentAsync(_context, contentHashInfo, rule.GetOnlyUnlinked());
                if (!evictionResult)
                {
                    return evictionResult;
                }

                _purgeResult.Merge(evictionResult);
            }

            return BoolResult.Success;
        }

        /// <summary>
        /// Evict hashes in LRU-ed order determined by remote last-access times.
        /// </summary>
        private async Task<BoolResult> EvictDistributedWithDistributedStoreAsync()
        {
            var evictedContent = new List<ContentHash>();
            var distributedStore = _distributedEvictionSettings.DistributedStore;

            foreach (var contentHashInfo in distributedStore.GetHashesInEvictionOrder(_context, _contentHashesWithInfo))
            {
                if (StopPurging(out var stopReason, out var rule))
                {
                    _purgeResult.StopReason = stopReason;
                    break;
                }

                var evictionResult = await _quotaKeeper.EvictContentAsync(_context, contentHashInfo, rule.GetOnlyUnlinked());
                if (!evictionResult)
                {
                    return evictionResult;
                }

                if (evictionResult.SuccessfullyEvictedHash)
                {
                    evictedContent.Add(contentHashInfo.ContentHash);
                }

                _purgeResult.Merge(evictionResult);
            }

            var unregisterResult = await distributedStore.UnregisterAsync(_context, evictedContent, _token);
            if (!unregisterResult)
            {
                return unregisterResult;
            }

            return BoolResult.Success;
        }

        /// <summary>
        /// Evict hashes in LRU-ed order determined by remote last-access times.
        /// </summary>
        private async Task<BoolResult> EvictDistributedAsync()
        {
            // Track hashes for final update.
            // Item1 marks whether content was removed from Redis because it was safe to evict.
            // Item2 is the last-access time as seen by the data center.
            var unpurgedHashes = new Dictionary<ContentHash, Tuple<bool, DateTime>>();
            var evictedHashes = new List<ContentHash>();

            // Purge all unpinned content where local last-access time is in sync with remote last-access time.
            var purgeInfo = await AttemptPurgeAsync(unpurgedHashes, evictedHashes);

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
                var unregisterResult = await _distributedEvictionSettings.DistributedStore.UnregisterAsync(_context, evictedHashes, _token);
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
        private async Task<(bool finishedPurging, BoolResult purgeResult)> AttemptPurgeAsync(
            Dictionary<ContentHash, Tuple<bool, DateTime>> unpurgedHashes,
            List<ContentHash> evictedHashes)
        {
            var finishedPurging = false;

            var trimOrGetLastAccessTimeAsync = _distributedEvictionSettings.TrimOrGetLastAccessTimeAsync;
            var batchSize = _distributedEvictionSettings.LocationStoreBatchSize;
            var replicaCreditInMinutes = _distributedEvictionSettings.ReplicaCreditInMinutes;

            var hashQueue = CreatePriorityQueue(_contentHashesWithInfo, replicaCreditInMinutes);

            while (!finishedPurging && hashQueue.Count > 0)
            {
                var purgeableHashesBatch = GetLruBatch(hashQueue, batchSize);
                var unpinnedHashes = GetUnpinnedHashesAndCompilePinnedSize(purgeableHashesBatch);
                if (!unpinnedHashes.Any())
                {
                    continue; // No hashes in this batch are able to evicted because they're all pinned
                }

                // If unpurgedHashes contains hash, it was checked once in the data center. We relax the replica restriction on retry
                var unpinnedHashesWithCheck =
                    unpinnedHashes.Select(hash => Tuple.Create(hash, !unpurgedHashes.ContainsKey(hash.ContentHash))).ToList();

                // Unregister hashes that can be safely evicted and get distributed last-access time for the rest
                var contentHashesInfoRemoteResult =
                    await trimOrGetLastAccessTimeAsync(_context, unpinnedHashesWithCheck, _token, UrgencyHint.High);

                if (!contentHashesInfoRemoteResult.Succeeded)
                {
                    return (finishedPurging: false, contentHashesInfoRemoteResult);
                }

                var purgeInfo = await ProcessHashesForEvictionAsync(
                    contentHashesInfoRemoteResult.Data,
                    unpurgedHashes,
                    hashQueue,
                    evictedHashes);

                if (purgeInfo.purgeResult != null)
                {
                    return purgeInfo; // Purging encountered an error.
                }

                finishedPurging = purgeInfo.finishedPurging;
            }

            return (finishedPurging, BoolResult.Success);
        }

        /// <summary>
        /// Get up to a page of records to delete, prioritizing the least-wanted files.
        /// </summary>
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

        private IList<ContentHashWithLastAccessTimeAndReplicaCount> GetUnpinnedHashesAndCompilePinnedSize(IEnumerable<ContentHashWithLastAccessTimeAndReplicaCount> purgeableHashesBatch)
        {
            long totalPinnedSize = 0; // Compile aggregate pinnedSize for the fail faster case
            var unpinnedHashes = new List<ContentHashWithLastAccessTimeAndReplicaCount>();

            foreach (var hashInfo in purgeableHashesBatch)
            {
                var pinnedSize = _distributedEvictionSettings.PinnedSizeChecker(_context, hashInfo.ContentHash);
                if (pinnedSize >= 0)
                {
                    totalPinnedSize += pinnedSize;
                }
                else
                {
                    unpinnedHashes.Add(hashInfo);
                }
            }

            _purgeResult.MergePinnedSize(totalPinnedSize);
            return unpinnedHashes;
        }

        private async Task<(bool finishedPurging, BoolResult purgeResult)> ProcessHashesForEvictionAsync(
            IList<ContentHashWithLastAccessTimeAndReplicaCount> contentHashesWithRemoteInfo,
            Dictionary<ContentHash, Tuple<bool, DateTime>> unpurgedHashes,
            PriorityQueue<ContentHashWithLastAccessTimeAndReplicaCount> hashQueue,
            List<ContentHash> evictedHashes)
        {
            bool finishedPurging = false;

            foreach (var contentHashWithRemoteInfo in contentHashesWithRemoteInfo)
            {
                if (StopPurging(out var stopReason, out var rule))
                {
                    _purgeResult.StopReason = stopReason;
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
                        var evictResult = await _quotaKeeper.EvictContentAsync(
                            _context,
                            contentHashWithRemoteInfo,
                            rule.GetOnlyUnlinked());

                        if (!evictResult)
                        {
                            return (finishedPurging: false, evictResult);
                        }

                        _purgeResult.Merge(evictResult);

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
                    unpurgedHashes[contentHashWithRemoteInfo.ContentHash] = Tuple.Create(
                        contentHashWithRemoteInfo.SafeToEvict,
                        contentHashWithRemoteInfo.LastAccessTime);
                }
                else
                {
                    // Don't track hash to update with remote last-access time because it was evicted
                    unpurgedHashes.Remove(contentHashWithRemoteInfo.ContentHash);
                    evictedHashes.Add(contentHashWithRemoteInfo.ContentHash);
                }
            }

            // Null is special value here that prevents the purge loop from exiting.
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
