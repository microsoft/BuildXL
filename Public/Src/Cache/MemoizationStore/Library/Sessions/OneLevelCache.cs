// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

extern alias Async;

using System;
using System.Diagnostics.ContractsLight;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
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

        /// <inheritdoc />
        public Task<PutResult> HandlePushFileAsync(Context context, ContentHash hash, AbsolutePath sourcePath, CancellationToken token)
        {
            if (ContentStore is IPushFileHandler handler)
            {
                return handler.HandlePushFileAsync(context, hash, sourcePath, token);
            }

            return Task.FromResult(new PutResult(new InvalidOperationException($"{nameof(ContentStore)} does not implement {nameof(IPushFileHandler)}"), hash));
        }

        /// <inheritdoc />
        public bool HasContentLocally(Context context, ContentHash hash)
        {
            if (ContentStore is IPushFileHandler handler)
            {
                return handler.HasContentLocally(context, hash);
            }

            return false;
        }
    }
}
