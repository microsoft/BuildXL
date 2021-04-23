// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.MemoizationStore.Distributed.Sessions;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;

#nullable enable

namespace BuildXL.Cache.MemoizationStore.Distributed.Stores
{
    /// <summary>
    /// Cache which acts on a local cache but also publishes content hash lists to a remote cache.
    /// </summary>
    public class PublishingCache : StartupShutdownBase, IPublishingCache
    {
        private readonly ICache _local;
        private readonly IPublishingStore _remote;

        /// <inheritdoc />
        public Guid Id { get; }

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(PublishingCache));

        /// <nodoc />
        public PublishingCache(ICache local, IPublishingStore remote, Guid id)
        {
            _local = local;
            _remote = remote;
            Id = id;
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            await _local.StartupAsync(context).ThrowIfFailure();
            await _remote.StartupAsync(context).ThrowIfFailure();
            return BoolResult.Success;
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            await _local.ShutdownAsync(context).ThrowIfFailure();
            await _remote.ShutdownAsync(context).ThrowIfFailure();
            return BoolResult.Success;
        }

        /// <inheritdoc />
        protected override void DisposeCore()
        {
            _local.Dispose();
        }

        /// <inheritdoc />
        public virtual CreateSessionResult<IReadOnlyCacheSession> CreateReadOnlySession(Context context, string name, ImplicitPin implicitPin)
            => _local.CreateReadOnlySession(context, name, implicitPin);

        /// <inheritdoc />
        public virtual CreateSessionResult<ICacheSession> CreateSession(Context context, string name, ImplicitPin implicitPin)
            => _local.CreateSession(context, name, implicitPin);

        /// <inheritdoc />
        public CreateSessionResult<ICacheSession> CreatePublishingSession(Context context, string name, ImplicitPin implicitPin, PublishingCacheConfiguration? config, string pat)
        {
            if (config is null)
            {
                return _local.CreateSession(context, name, implicitPin);
            }

            var remoteSessionResult = _remote.CreateSession(context, config, pat);
            if (!remoteSessionResult.Succeeded)
            {
                return new CreateSessionResult<ICacheSession>(remoteSessionResult);
            }

            var localSessionResult = _local.CreateSession(context, $"{name}-local", implicitPin);
            if (!localSessionResult.Succeeded)
            {
                return localSessionResult;
            }

            var session = new PublishingCacheSession(name, localSessionResult.Session, remoteSessionResult.Value, config.PublishAsynchronously);
            return new CreateSessionResult<ICacheSession>(session);
        }

        /// <inheritdoc />
        public IAsyncEnumerable<StructResult<StrongFingerprint>> EnumerateStrongFingerprints(Context context)
            => _local.EnumerateStrongFingerprints(context);

        /// <inheritdoc />
        public Task<GetStatsResult> GetStatsAsync(Context context)
            => _local.GetStatsAsync(context);
    }
}
