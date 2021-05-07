// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
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
    public class PublishingCache<TInner> : StartupShutdownBase, IPublishingCache, IContentStore, IStreamStore, IRepairStore, ICopyRequestHandler, IPushFileHandler
        where TInner : ICache, IContentStore, IStreamStore, IRepairStore, ICopyRequestHandler, IPushFileHandler
    {
        private readonly TInner _local;
        private readonly IPublishingStore _remote;

        /// <inheritdoc />
        public Guid Id { get; }

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(PublishingCache<TInner>));

        /// <nodoc />
        public PublishingCache(TInner local, IPublishingStore remote, Guid id)
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
            => ((ICache)_local).CreateReadOnlySession(context, name, implicitPin);

        /// <inheritdoc />
        public virtual CreateSessionResult<ICacheSession> CreateSession(Context context, string name, ImplicitPin implicitPin)
            => ((ICache)_local).CreateSession(context, name, implicitPin);

        /// <inheritdoc />
        public CreateSessionResult<ICacheSession> CreatePublishingSession(Context context, string name, ImplicitPin implicitPin, PublishingCacheConfiguration? config, string pat)
        {
            if (config is null)
            {
                return ((ICache)_local).CreateSession(context, name, implicitPin);
            }

            var remoteSessionResult = _remote.CreateSession(context, $"{name}-publisher", config, pat);
            if (!remoteSessionResult.Succeeded)
            {
                return new CreateSessionResult<ICacheSession>(remoteSessionResult);
            }

            var localSessionResult = ((ICache)_local).CreateSession(context, $"{name}-local", implicitPin);
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
            => ((ICache)_local).GetStatsAsync(context);

        /// <inheritdoc />
        CreateSessionResult<IReadOnlyContentSession> IContentStore.CreateReadOnlySession(Context context, string name, ImplicitPin implicitPin)
            => ((IContentStore)_local).CreateReadOnlySession(context, name, implicitPin);

        /// <inheritdoc />
        CreateSessionResult<IContentSession> IContentStore.CreateSession(Context context, string name, ImplicitPin implicitPin)
            => ((IContentStore)_local).CreateSession(context, name, implicitPin);

        /// <inheritdoc />
        public Task<DeleteResult> DeleteAsync(Context context, ContentHash contentHash, DeleteContentOptions? deleteOptions)
            => _local.DeleteAsync(context, contentHash, deleteOptions);

        /// <inheritdoc />
        public void PostInitializationCompleted(Context context, BoolResult result) => _local.PostInitializationCompleted(context, result);

        /// <inheritdoc />
        public Task<OpenStreamResult> StreamContentAsync(Context context, ContentHash contentHash) => _local.StreamContentAsync(context, contentHash);

        /// <inheritdoc />
        public Task<Result<long>> RemoveFromTrackerAsync(Context context) => _local.RemoveFromTrackerAsync(context);

        /// <inheritdoc />
        public Task<BoolResult> HandleCopyFileRequestAsync(Context context, ContentHash hash, CancellationToken token)
            => _local.HandleCopyFileRequestAsync(context, hash, token);

        /// <inheritdoc />
        public Task<PutResult> HandlePushFileAsync(Context context, ContentHash hash, FileSource source, CancellationToken token)
            => _local.HandlePushFileAsync(context, hash, source, token);

        /// <inheritdoc />
        public bool CanAcceptContent(Context context, ContentHash hash, out RejectionReason rejectionReason)
            => _local.CanAcceptContent(context, hash, out rejectionReason);
    }
}
