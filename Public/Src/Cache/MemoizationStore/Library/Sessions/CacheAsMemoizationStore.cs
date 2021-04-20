// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Interfaces.Stores;

namespace BuildXL.Cache.MemoizationStore.Sessions
{
    /// <summary>
    /// Wraps an <see cref="ICache"/> instance as an <see cref="IMemoizationStore"/>
    /// </summary>
    public class CacheAsMemoizationStore : StartupShutdownBase, IMemoizationStore
    {
        private readonly ICache _cache;
        private readonly bool _startupInnerCache;

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(CacheAsMemoizationStore));

        /// <nodoc />
        public CacheAsMemoizationStore(ICache cache, bool startupInnerCache)
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
        public CreateSessionResult<IReadOnlyMemoizationSession> CreateReadOnlySession(Context context, string name)
        {
            return _cache.CreateReadOnlySession(context, name, ImplicitPin.None).Map<IReadOnlyMemoizationSession>(s => s);
        }

        /// <inheritdoc />
        public CreateSessionResult<IMemoizationSession> CreateSession(Context context, string name)
        {
            return _cache.CreateSession(context, name, ImplicitPin.None).Map<IMemoizationSession>(s => s);
        }

        /// <inheritdoc />
        public CreateSessionResult<IMemoizationSession> CreateSession(Context context, string name, IContentSession contentSession)
        {
            return _cache.CreateSession(context, name, ImplicitPin.None).Map<IMemoizationSession>(s => s);
        }

        /// <inheritdoc />
        public IAsyncEnumerable<StructResult<StrongFingerprint>> EnumerateStrongFingerprints(Context context)
        {
            return _cache.EnumerateStrongFingerprints(context);
        }

        /// <inheritdoc />
        public Task<GetStatsResult> GetStatsAsync(Context context)
        {
            return _cache.GetStatsAsync(context);
        }
    }
}
