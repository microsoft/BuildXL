// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.MemoizationStore.Distributed.Metadata;
using BuildXL.Cache.MemoizationStore.Distributed.Sessions;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Interfaces.Stores;
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

        private DistributedContentStore<AbsolutePath> _distributedContentStore;

        /// <nodoc />
        public DistributedOneLevelCache(IContentStore contentStore, DistributedContentStore<AbsolutePath> distributedContentStore, Guid id, bool passContentToMemoization = true)
            : base(id, passContentToMemoization)
        {
            ContentStore = contentStore;
            _distributedContentStore = distributedContentStore;
        }

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

                var redisStore = (RedisGlobalStore)localLocationStore.GlobalStore;

                MemoizationStore = new DatabaseMemoizationStore(new DistributedMemoizationDatabase(
                    localLocationStore,
                    new RedisMemoizationDatabase(redisStore.RedisDatabase, localLocationStore.Clock, localLocationStore.Configuration.LocationEntryExpiry)));

                return await MemoizationStore.StartupAsync(context);
            });
        }
    }
}
