// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Vfs;
using BuildXL.Cache.MemoizationStore.Sessions;
using BuildXL.Cache.MemoizationStore.Tracing;

using ICache = BuildXL.Cache.MemoizationStore.Interfaces.Caches.ICache;

namespace BuildXL.Cache.MemoizationStoreAdapter
{
    /// <summary>
    /// Cache implementation which uses <see cref="VirtualizedContentStore"/> to virtualize content operations
    /// </summary>
    public class VirtualizedContentCache : OneLevelCacheBase
    {
        /// <inheritdoc />
        protected override CacheTracer CacheTracer { get; } = new CacheTracer(nameof(VirtualizedContentCache));

        private readonly ICache _cache;
        private readonly VfsCasConfiguration _configuration;

        /// <inheritdoc />
        protected override bool UseOnlyContentStats => true;

        /// <nodoc />
        public VirtualizedContentCache(ICache cache, VfsCasConfiguration configuration)
            : base(Guid.NewGuid(), passContentToMemoization: false)
        {
            _cache = cache;
            _configuration = configuration;
        }

        /// <inheritdoc />
        protected override async Task<(BoolResult contentStoreResult, BoolResult memoizationStoreResult)> CreateAndStartStoresAsync(OperationContext context)
        {
            // Explicitly startup/shutdown inner cache so content store and memoization store wrappers do not need to startup
            await _cache.StartupAsync(context).ThrowIfFailure();

            var innerContentStore = new CacheAsContentStore(_cache, startupInnerCache: false);
            ContentStore = new VirtualizedContentStore(innerContentStore, context.TracingContext.Logger, _configuration);

            var contentStoreResult = await ContentStore.StartupAsync(context);

            MemoizationStore = new CacheAsMemoizationStore(_cache, startupInnerCache: false);

            var memoizationStoreResult = await MemoizationStore.StartupAsync(context);

            return (contentStoreResult, memoizationStoreResult);
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            var success = await base.ShutdownCoreAsync(context);

            success &= await _cache.ShutdownAsync(context);

            return success;
        }
    }
}
