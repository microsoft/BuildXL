// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Interfaces.Stores;
using BuildXL.Cache.MemoizationStore.Tracing;

namespace BuildXL.Cache.MemoizationStore.Sessions
{
    /// <summary>
    ///     A reference implementation of <see cref="ICache"/> that represents a single level of content and metadata.
    /// </summary>
    public abstract class OneLevelCacheBase : StartupShutdownBase, ICache, IContentStore, IStreamStore, IRepairStore, ICopyRequestHandler, IPushFileHandler
    {
        /// <summary>
        ///     Exposes the ContentStore to subclasses. NOTE: Only available after calling <see cref="CreateAndStartStoresAsync(OperationContext)"/>
        /// </summary>
        protected IContentStore ContentStore { get; set; }

        /// <summary>
        ///     Exposes the MemoizationStore to subclasses. NOTE: Only available after calling <see cref="CreateAndStartStoresAsync(OperationContext)"/>
        /// </summary>
        protected IMemoizationStore MemoizationStore { get; set; }

        /// <inheritdoc />
        protected override Tracer Tracer => CacheTracer;

        /// <nodoc />
        protected abstract CacheTracer CacheTracer { get; }

        /// <summary>
        ///     Determines if the content session will be passed to the memoization store when constructing a non-readonly session.
        /// </summary>
        private readonly bool _passContentToMemoization = true;

        /// <nodoc />
        public OneLevelCacheBase(Guid id, bool passContentToMemoization)
        {
            _passContentToMemoization = passContentToMemoization;
            Id = id;
        }

        /// <inheritdoc />
        public Guid Id { get; }

        /// <summary>
        /// Creates and starts the content store and memoization store
        /// </summary>
        protected abstract Task<(BoolResult contentStoreResult, BoolResult memoizationStoreResult)> CreateAndStartStoresAsync(OperationContext context);

        /// <inheritdoc />
        protected override void DisposeCore()
        {
            MemoizationStore?.Dispose();
            ContentStore?.Dispose();
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            (var contentStoreResult, var memoizationStoreResult) = await CreateAndStartStoresAsync(context);

            Contract.Assert(ContentStore != null, "Content store must be initialized");
            Contract.Assert(MemoizationStore != null, "Memoization store must be initialized");
            Contract.Assert(!ReferenceEquals(ContentStore, MemoizationStore));

            BoolResult result;

            if (!contentStoreResult.Succeeded || !memoizationStoreResult.Succeeded)
            {
                var sb = new StringBuilder();

                if (contentStoreResult.Succeeded)
                {
                    var r = await ContentStore.ShutdownAsync(context).ConfigureAwait(false);
                    if (!r.Succeeded)
                    {
                        sb.Append($"Content store shutdown failed, error=[{r}]");
                    }
                }
                else
                {
                    sb.Append($"Content store startup failed, error=[{contentStoreResult}]");
                }

                if (memoizationStoreResult.Succeeded)
                {
                    var r = await MemoizationStore.ShutdownAsync(context).ConfigureAwait(false);
                    if (!r.Succeeded)
                    {
                        sb.Append(sb.Length > 0 ? ", " : string.Empty);
                        sb.Append($"Memoization store shutdown failed, error=[{memoizationStoreResult}]");
                    }
                }
                else
                {
                    sb.Append(sb.Length > 0 ? ", " : string.Empty);
                    sb.Append($"Memoization store startup failed, error=[{memoizationStoreResult}]");
                }

                result = new BoolResult(sb.ToString());
            }
            else
            {
                result = BoolResult.Success;
            }

            return result;
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            var statsResult = await StatsAsync(context).ConfigureAwait(false);

            var contentStoreTask = Task.Run(() => ContentStore.ShutdownAsync(context));
            var memoizationStoreResult = await MemoizationStore.ShutdownAsync(context).ConfigureAwait(false);
            var contentStoreResult = await contentStoreTask.ConfigureAwait(false);

            BoolResult result;
            if (contentStoreResult.Succeeded && memoizationStoreResult.Succeeded)
            {
                result = BoolResult.Success;
            }
            else
            {
                var sb = new StringBuilder();
                if (!contentStoreResult.Succeeded)
                {
                    sb.Append($"Content store shutdown failed, error=[{contentStoreResult}]");
                }

                if (!memoizationStoreResult.Succeeded)
                {
                    sb.Append(sb.Length > 0 ? ", " : string.Empty);
                    sb.Append($"Memoization store shutdown failed, error=[{memoizationStoreResult}]");
                }

                result = new BoolResult(sb.ToString());
            }

            if (statsResult.Succeeded)
            {
#if NET_FRAMEWORK
                LocalCacheStatsEventSource.Instance.Stats(statsResult.CounterSet);
#endif
                statsResult.CounterSet.LogOrderedNameValuePairs(s => Tracer.Debug(context, s));
            }

            return result;
        }

        /// <inheritdoc />
        public CreateSessionResult<IReadOnlyCacheSession> CreateReadOnlySession(Context context, string name, ImplicitPin implicitPin)
        {
            return Tracing.CreateReadOnlySessionCall.Run(CacheTracer, context, name, () =>
            {
                var createContentResult = ContentStore.CreateReadOnlySession(context, name, implicitPin);
                if (!createContentResult.Succeeded)
                {
                    return new CreateSessionResult<IReadOnlyCacheSession>(createContentResult, "Content session creation failed");
                }
                var contentReadOnlySession = createContentResult.Session;

                var createMemoizationResult = MemoizationStore.CreateReadOnlySession(context, name);
                if (!createMemoizationResult.Succeeded)
                {
                    return new CreateSessionResult<IReadOnlyCacheSession>(createMemoizationResult, "Memoization session creation failed");
                }
                var memoizationReadOnlySession = createMemoizationResult.Session;

                var session = new ReadOnlyOneLevelCacheSession(name, implicitPin, memoizationReadOnlySession, contentReadOnlySession);
                return new CreateSessionResult<IReadOnlyCacheSession>(session);
            });
        }

        /// <inheritdoc />
        public CreateSessionResult<ICacheSession> CreateSession(Context context, string name, ImplicitPin implicitPin)
        {
            return Tracing.CreateSessionCall.Run(CacheTracer, context, name, () =>
            {
                var createContentResult = ContentStore.CreateSession(context, name, implicitPin);
                if (!createContentResult.Succeeded)
                {
                    return new CreateSessionResult<ICacheSession>(createContentResult, "Content session creation failed");
                }
                var contentSession = createContentResult.Session;

                var createMemoizationResult = _passContentToMemoization
                    ? MemoizationStore.CreateSession(context, name, contentSession)
                    : MemoizationStore.CreateSession(context, name);

                if (!createMemoizationResult.Succeeded)
                {
                    return new CreateSessionResult<ICacheSession>(createMemoizationResult, "Memoization session creation failed");
                }
                var memoizationSession = createMemoizationResult.Session;

                var session = new OneLevelCacheSession(name, implicitPin, memoizationSession, contentSession);
                return new CreateSessionResult<ICacheSession>(session);
            });
        }

        /// <inheritdoc />
        public Task<GetStatsResult> GetStatsAsync(Context context)
        {
            return GetStatsCall<Tracer>.RunAsync(Tracer, new OperationContext(context), () => StatsAsync(context));
        }

        private async Task<GetStatsResult> StatsAsync(Context context)
        {
            var counters = new CounterSet();

            var statsResult = await ContentStore.GetStatsAsync(context).ConfigureAwait(false);

            if (!statsResult.Succeeded)
            {
                return statsResult;
            }

            counters.Merge(statsResult.CounterSet);

            statsResult = await MemoizationStore.GetStatsAsync(context).ConfigureAwait(false);

            if (!statsResult.Succeeded)
            {
                return statsResult;
            }

            counters.Merge(statsResult.CounterSet);

            return new GetStatsResult(counters);
        }

        /// <inheritdoc />
        public Async::System.Collections.Generic.IAsyncEnumerable<StructResult<StrongFingerprint>> EnumerateStrongFingerprints(Context context)
        {
            return MemoizationStore.EnumerateStrongFingerprints(context);
        }

        /// <inheritdoc />
        public async Task<OpenStreamResult> StreamContentAsync(Context context, ContentHash contentHash)
        {
            if (ContentStore is IStreamStore innerStreamStore)
            {
                return await innerStreamStore.StreamContentAsync(context, contentHash);
            }

            return new OpenStreamResult($"{ContentStore} does not implement {nameof(IStreamStore)} in {nameof(OneLevelCache)}.");
        }

        /// <inheritdoc />
        public async Task<FileExistenceResult> CheckFileExistsAsync(Context context, ContentHash contentHash)
        {
            if (ContentStore is IStreamStore innerStreamStore)
            {
                return await innerStreamStore.CheckFileExistsAsync(context, contentHash);
            }

            return new FileExistenceResult(FileExistenceResult.ResultCode.Error, $"{ContentStore} does not implement {nameof(IStreamStore)} in {nameof(OneLevelCache)}.");
        }

        /// <inheritdoc />
        public async Task<StructResult<long>> RemoveFromTrackerAsync(Context context)
        {
            if (ContentStore is IRepairStore innerRepairStore)
            {
                return await innerRepairStore.RemoveFromTrackerAsync(context);
            }

            return new StructResult<long>($"{ContentStore} does not implement {nameof(IRepairStore)} in {nameof(OneLevelCache)}.");
        }

        /// <inheritdoc />
        public async Task<BoolResult> HandleCopyFileRequestAsync(Context context, ContentHash hash)
        {
            if (ContentStore is ICopyRequestHandler innerCopyStore)
            {
                return await innerCopyStore.HandleCopyFileRequestAsync(context, hash);
            }

            return new BoolResult($"{ContentStore} does not implement {nameof(ICopyRequestHandler)} in {nameof(OneLevelCache)}.");
        }

        CreateSessionResult<IReadOnlyContentSession> IContentStore.CreateReadOnlySession(Context context, string name, ImplicitPin implicitPin)
        {
            return CreateReadOnlySession(context, name, implicitPin).Map(session => (IReadOnlyContentSession)session);
        }

        CreateSessionResult<IContentSession> IContentStore.CreateSession(Context context, string name, ImplicitPin implicitPin)
        {
            return CreateSession(context, name, implicitPin).Map(session => (IContentSession)session);
        }

        /// <inheritdoc />
        public Task<DeleteResult> DeleteAsync(Context context, ContentHash contentHash, DeleteContentOptions deleteOptions = null)
        {
            return ContentStore.DeleteAsync(context, contentHash, deleteOptions);
        }

        /// <inheritdoc />
        public void PostInitializationCompleted(Context context, BoolResult result)
        {
            ContentStore.PostInitializationCompleted(context, result);
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
