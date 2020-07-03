// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;

#nullable enable

namespace BuildXL.Cache.ContentStore.Stores
{
    internal sealed class Purger
    {
        private readonly Context _context;
        private readonly QuotaKeeper _quotaKeeper;
        private readonly IDistributedLocationStore? _distributedStore;
        private readonly IReadOnlyList<ContentHashWithLastAccessTimeAndReplicaCount> _contentHashesWithInfo;
        private readonly PurgeResult _purgeResult;
        private readonly CancellationToken _token;

        /// <nodoc />
        public Purger(
            Context context,
            QuotaKeeper quotaKeeper,
            IDistributedLocationStore? distributedStore,
            IReadOnlyList<ContentHashWithLastAccessTimeAndReplicaCount> contentHashesWithInfo,
            PurgeResult purgeResult,
            CancellationToken token)
        {
            _context = context;
            _quotaKeeper = quotaKeeper;
            _distributedStore = distributedStore;
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
            if (_distributedStore?.CanComputeLru == true)
            {
                return EvictDistributedWithDistributedStoreAsync(_distributedStore);
            }
            else
            {
                return EvictLocalAsync();
            }
        }

        private bool StopPurging([NotNullWhen(true)]out string? stopReason, [NotNullWhen(false)]out IQuotaRule? rule) => _quotaKeeper.StopPurging(out stopReason, out rule);

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
        private async Task<BoolResult> EvictDistributedWithDistributedStoreAsync(IDistributedLocationStore distributedStore)
        {
            var evictedContent = new List<ContentHash>();
            TimeSpan? minEffectiveAge = null;

            foreach (var contentHashInfo in distributedStore.GetHashesInEvictionOrder(_context, _contentHashesWithInfo))
            {
                if (StopPurging(out var stopReason, out var rule))
                {
                    _purgeResult.StopReason = stopReason;
                    break;
                }

                var contentHashWithLastAccessTimeAndReplicaCount = ToContentHashListWithLastAccessTimeAndReplicaCount(contentHashInfo);
                var evictionResult = await _quotaKeeper.EvictContentAsync(_context, contentHashWithLastAccessTimeAndReplicaCount, rule.GetOnlyUnlinked());
                if (!evictionResult)
                {
                    return evictionResult;
                }

                if (evictionResult.SuccessfullyEvictedHash)
                {
                    evictedContent.Add(contentHashInfo.ContentHash);
                    minEffectiveAge = minEffectiveAge < contentHashInfo.EffectiveAge ? minEffectiveAge : contentHashInfo.EffectiveAge;
                }

                _purgeResult.Merge(evictionResult);
            }

            var unregisterResult = await distributedStore.UnregisterAsync(_context, evictedContent, _token, minEffectiveAge);
            if (!unregisterResult)
            {
                return unregisterResult;
            }

            return BoolResult.Success;
        }

        private static ContentHashWithLastAccessTimeAndReplicaCount ToContentHashListWithLastAccessTimeAndReplicaCount(ContentEvictionInfo contentHashInfo) => new ContentHashWithLastAccessTimeAndReplicaCount(contentHashInfo.ContentHash, DateTime.UtcNow - contentHashInfo.Age, contentHashInfo.ReplicaCount, safeToEvict: true, effectiveLastAccessTime: DateTime.UtcNow - contentHashInfo.EffectiveAge);
    }
}
