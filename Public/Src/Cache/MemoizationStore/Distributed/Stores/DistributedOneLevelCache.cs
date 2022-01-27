// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Services;
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

        private readonly ContentLocationStoreServices _contentLocationStoreServices;

        /// <nodoc />
        public DistributedOneLevelCache(IContentStore contentStore, ContentLocationStoreServices contentLocationStoreServices, Guid id, bool passContentToMemoization = true)
            : base(id, passContentToMemoization)
        {
            ContentStore = contentStore;
            _contentLocationStoreServices = contentLocationStoreServices;
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
            return context.PerformOperationAsync(base.Tracer, async () =>
            {
                MemoizationDatabase getGlobalMemoizationDatabase()
                {
                    if (_contentLocationStoreServices.Configuration.UseMemoizationContentMetadataStore)
                    {
                        return new MetadataStoreMemoizationDatabase(_contentLocationStoreServices.GlobalCacheStore.Instance);
                    }
                    else
                    {
                        var redisStore = _contentLocationStoreServices.RedisGlobalStore.Instance;
                        return new RedisMemoizationDatabase(redisStore.RaidedRedis, _contentLocationStoreServices.Configuration.Memoization);
                    }
                }

                MemoizationStore = new DatabaseMemoizationStore(
                    new DistributedMemoizationDatabase(
                        _contentLocationStoreServices.LocalLocationStore.Instance,
                        getGlobalMemoizationDatabase()));

                return await MemoizationStore.StartupAsync(context);
            });
        }
    }
}
