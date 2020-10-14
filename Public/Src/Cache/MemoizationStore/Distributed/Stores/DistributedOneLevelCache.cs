// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.MemoizationStore.Sessions;
using BuildXL.Cache.MemoizationStore.Stores;
using BuildXL.Cache.MemoizationStore.Tracing;

namespace BuildXL.Cache.MemoizationStore.Distributed.Stores
{
    /// <summary>
    /// Distributed OneLevelCache combining a content store and LocalLocationStore-backed DistributedMemoizationStore
    /// </summary>
    public class DistributedOneLevelCache : OneLevelCacheBase
    {
        /// <inheritdoc />
        protected override CacheTracer CacheTracer { get; } = new CacheTracer(nameof(DistributedOneLevelCache));

        private readonly DistributedContentStore _distributedContentStore;

        /// <nodoc />
        public DistributedOneLevelCache(IContentStore contentStore, DistributedContentStore distributedContentStore, Guid id, bool passContentToMemoization = true)
            : base(id, passContentToMemoization)
        {
            ContentStore = contentStore;
            _distributedContentStore = distributedContentStore;
        }

        /// <inheritdoc />
        protected override async Task<(BoolResult contentStoreResult, BoolResult memoizationStoreResult)> CreateAndStartStoresAsync(OperationContext context)
        {
            var contentStoreResult = await ContentStore.StartupAsync(context);
            if (!contentStoreResult)
            {
                return (contentStoreResult, new BoolResult("DistributedOneLevelCache memoization store requires successful content store startup"));
            }

            var memoizationStoreResult = await CreateDistributedMemoizationStoreAsync(context);

            return (contentStoreResult, memoizationStoreResult);
        }

        private Task<BoolResult> CreateDistributedMemoizationStoreAsync(OperationContext context)
        {
            return context.PerformOperationAsync(Tracer, async () =>
            {
                if (!_distributedContentStore.TryGetLocalLocationStore(out var localLocationStore))
                {
                    return new BoolResult("LocalLocationStore not available");
                }

                var memoizationStoreConfiguration = localLocationStore.Configuration as RedisMemoizationStoreConfiguration;
                if (memoizationStoreConfiguration == null)
                {
                    return new BoolResult($"LocalLocationStore.Configuration should be of type 'RedisMemoizationStoreConfiguration' but was {localLocationStore.Configuration.GetType()}");
                }

                var redisStore = (RedisGlobalStore)localLocationStore.GlobalStore;

                MemoizationStore = new DatabaseMemoizationStore(
                    new DistributedMemoizationDatabase(
                        localLocationStore,
                        new RedisMemoizationDatabase(
                            redisStore.RaidedRedis,
                            localLocationStore.EventStore.Clock,
                            memoizationStoreConfiguration.LocationEntryExpiry,
                            memoizationStoreConfiguration.MemoizationOperationTimeout,
                            memoizationStoreConfiguration.MemoizationSlowOperationCancellationTimeout)));

                return await MemoizationStore.StartupAsync(context);
            });
        }
    }
}
