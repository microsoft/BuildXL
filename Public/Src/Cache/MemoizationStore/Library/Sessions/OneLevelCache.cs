// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Interfaces.Stores;
using BuildXL.Cache.MemoizationStore.Tracing;

namespace BuildXL.Cache.MemoizationStore.Sessions
{
    /// <summary>
    ///     A reference implementation of <see cref="ICache"/> that represents a single level of content and metadata.
    /// </summary>
    public class OneLevelCache : OneLevelCacheBase
    {
        private readonly Func<IContentStore> _contentStoreFunc;
        private readonly Func<IMemoizationStore> _memoizationStoreFunc;

        /// <inheritdoc />
        protected override CacheTracer CacheTracer { get; } = new CacheTracer(nameof(OneLevelCache));

        /// <summary>
        ///     Initializes a new instance of the <see cref="OneLevelCache" /> class, with an option to configure whether the content session will be passed to the memoization store
        ///     when creating a non-readonly session.
        /// </summary>
        public OneLevelCache(Func<IContentStore> contentStoreFunc, Func<IMemoizationStore> memoizationStoreFunc, Guid id, bool passContentToMemoization = true)
            : base(id, passContentToMemoization)
        {
            Contract.Requires(contentStoreFunc != null);
            Contract.Requires(memoizationStoreFunc != null);

            _contentStoreFunc = contentStoreFunc;
            _memoizationStoreFunc = memoizationStoreFunc;
        }

        /// <inheritdoc />
        protected override async Task<(BoolResult contentStoreResult, BoolResult memoizationStoreResult)> CreateAndStartStoresAsync(OperationContext context)
        {
            ContentStore = _contentStoreFunc();
            MemoizationStore = _memoizationStoreFunc();

            var preStartupResult = await PreStartupAsync(context).ThrowIfFailure();

            var contentStoreTask = Task.Run(() => ContentStore.StartupAsync(context));
            var memoizationStoreResult = await MemoizationStore.StartupAsync(context).ConfigureAwait(false);
            var contentStoreResult = await contentStoreTask.ConfigureAwait(false);

            return (contentStoreResult, memoizationStoreResult);
        }

        /// <summary>
        ///     Hook for subclass to process before startup.
        /// </summary>
        protected virtual Task<BoolResult> PreStartupAsync(Context context)
        {
            return Task.FromResult(BoolResult.Success);
        }
    }
}
