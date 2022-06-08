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
    /// This implementation wraps a "local" cache and provides asynchronous publishing capabilities for a "remote"
    /// cache.
    /// </summary>
    public class PublishingCache<TInner> : StartupShutdownBase,
        IPublishingCache,
        IContentStore, IStreamStore,
        IRepairStore, ICopyRequestHandler, IPushFileHandler,
        IComponentWrapper<TInner>
        where TInner : ICache, IContentStore, IStreamStore, IRepairStore, ICopyRequestHandler, IPushFileHandler
    {
        private readonly TInner _local;
        private readonly IReadOnlyList<IPublishingStore> _publishingStores;

        /// <inheritdoc />
        public Guid Id { get; }

        /// <inheritdoc />
        TInner IComponentWrapper<TInner>.Inner => _local;

        /// <inheritdoc />
        protected override Tracer Tracer { get; } = new Tracer(nameof(PublishingCache<TInner>));

        /// <nodoc />
        public PublishingCache(TInner local, IReadOnlyList<IPublishingStore> publishingStores, Guid id)
        {
            _local = local;
            _publishingStores = publishingStores;
            Id = id;
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            var result = await _local.StartupAsync(context);

            foreach (var publishingStore in _publishingStores)
            {
                result &= await publishingStore.StartupAsync(context);
            }

            return result;
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            var result = BoolResult.Success;
            foreach (var publishingStore in _publishingStores)
            {
                result &= await publishingStore.ShutdownAsync(context);
            }

            result &= await _local.ShutdownAsync(context);

            return result;
        }

        /// <inheritdoc />
        protected override void DisposeCore()
        {
            _local.Dispose();
        }

        /// <inheritdoc />
        public virtual CreateSessionResult<IReadOnlyCacheSession> CreateReadOnlySession(Context context, string name, ImplicitPin implicitPin)
        {
            return ((ICache)_local).CreateReadOnlySession(context, name, implicitPin);
        }

        /// <inheritdoc />
        public virtual CreateSessionResult<ICacheSession> CreateSession(Context context, string name, ImplicitPin implicitPin)
        {
            return ((ICache)_local).CreateSession(context, name, implicitPin);
        }

        /// <inheritdoc />
        public CreateSessionResult<ICacheSession> CreatePublishingSession(Context context, string name, ImplicitPin implicitPin, PublishingCacheConfiguration? config, string pat)
        {
            if (config is null)
            {
                return ((ICache)_local).CreateSession(context, name, implicitPin);
            }

            var remoteSessionResult = FetchMatchingStore(config)
                .CreateSession(context, $"{name}-publisher", config, pat);
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

        private IPublishingStore FetchMatchingStore(PublishingCacheConfiguration config)
        {
            foreach (var publishingStore in _publishingStores)
            {
                if (publishingStore.IsValidConfigurationType(config))
                {
                    return publishingStore;
                }
            }

            throw new NotImplementedException($"Could not find publishing cache backend for type `{config.GetType().FullName}`");
        }

        /// <inheritdoc />
        public IAsyncEnumerable<StructResult<StrongFingerprint>> EnumerateStrongFingerprints(Context context)
        {
            return _local.EnumerateStrongFingerprints(context);
        }

        /// <inheritdoc />
        public Task<GetStatsResult> GetStatsAsync(Context context)
        {
            return ((ICache)_local).GetStatsAsync(context);
        }

        /// <inheritdoc />
        CreateSessionResult<IReadOnlyContentSession> IContentStore.CreateReadOnlySession(Context context, string name, ImplicitPin implicitPin)
        {
            return ((IContentStore)_local).CreateReadOnlySession(context, name, implicitPin);
        }

        /// <inheritdoc />
        CreateSessionResult<IContentSession> IContentStore.CreateSession(Context context, string name, ImplicitPin implicitPin)
        {
            return ((IContentStore)_local).CreateSession(context, name, implicitPin);
        }

        /// <inheritdoc />
        public Task<DeleteResult> DeleteAsync(Context context, ContentHash contentHash, DeleteContentOptions? deleteOptions)
        {
            return _local.DeleteAsync(context, contentHash, deleteOptions);
        }

        /// <inheritdoc />
        public void PostInitializationCompleted(Context context, BoolResult result)
        {
            _local.PostInitializationCompleted(context, result);
        }

        /// <inheritdoc />
        public Task<OpenStreamResult> StreamContentAsync(Context context, ContentHash contentHash)
        {
            return _local.StreamContentAsync(context, contentHash);
        }

        /// <inheritdoc />
        public Task<BoolResult> RemoveFromTrackerAsync(Context context)
        {
            return _local.RemoveFromTrackerAsync(context);
        }

        /// <inheritdoc />
        public Task<BoolResult> HandleCopyFileRequestAsync(Context context, ContentHash hash, CancellationToken token)
        {
            return _local.HandleCopyFileRequestAsync(context, hash, token);
        }

        /// <inheritdoc />
        public Task<PutResult> HandlePushFileAsync(Context context, ContentHash hash, FileSource source, CancellationToken token)
        {
            return _local.HandlePushFileAsync(context, hash, source, token);
        }

        /// <inheritdoc />
        public bool CanAcceptContent(Context context, ContentHash hash, out RejectionReason rejectionReason)
        {
            return _local.CanAcceptContent(context, hash, out rejectionReason);
        }
    }
}
