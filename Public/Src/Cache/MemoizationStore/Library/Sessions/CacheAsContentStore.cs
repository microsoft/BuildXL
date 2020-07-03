// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;

namespace BuildXL.Cache.MemoizationStore.Sessions
{
    /// <summary>
    /// Wraps an <see cref="ICache"/> instance as an <see cref="IContentStore"/>
    /// </summary>
    public class CacheAsContentStore : StartupShutdownBase, IContentStore
    {
        private readonly ICache _cache;
        private readonly bool _startupInnerCache;

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(CacheAsContentStore));

        /// <nodoc />
        public CacheAsContentStore(ICache cache, bool startupInnerCache)
        {
            _cache = cache;
            _startupInnerCache = startupInnerCache;
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            if (_startupInnerCache)
            {
                await _cache.StartupAsync(context).ThrowIfFailure();
            }

            return await base.StartupCoreAsync(context);
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            if (_startupInnerCache)
            {
                await _cache.ShutdownAsync(context).ThrowIfFailure();
            }

            return await base.ShutdownCoreAsync(context);
        }

        /// <inheritdoc />
        public Task<GetStatsResult> GetStatsAsync(Context context)
        {
            return _cache.GetStatsAsync(context);
        }

        /// <inheritdoc />
        public CreateSessionResult<IReadOnlyContentSession> CreateReadOnlySession(Context context, string name, ImplicitPin implicitPin)
        {
            return _cache.CreateReadOnlySession(context, name, implicitPin).Map<IReadOnlyContentSession>(s => s);
        }

        /// <inheritdoc />
        public CreateSessionResult<IContentSession> CreateSession(Context context, string name, ImplicitPin implicitPin)
        {
            return _cache.CreateSession(context, name, implicitPin).Map<IContentSession>(s => s);
        }

        /// <inheritdoc />
        public Task<DeleteResult> DeleteAsync(Context context, ContentHash contentHash, DeleteContentOptions deleteOptions)
        {
            if (_cache is IContentStore contentStore)
            {
                return contentStore.DeleteAsync(context, contentHash, deleteOptions);
            }

            return Task.FromResult(new DeleteResult(DeleteResult.ResultCode.Error, "Not implemented"));
        }

        /// <inheritdoc />
        public void PostInitializationCompleted(Context context, BoolResult result)
        {
            if (_cache is IContentStore contentStore)
            {
                contentStore.PostInitializationCompleted(context, result);
            }
        }
    }
}
