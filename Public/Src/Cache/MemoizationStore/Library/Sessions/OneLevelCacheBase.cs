// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
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

#nullable enable

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
        protected IContentStore? ContentStore { get; set; }

        /// <summary>
        ///     Exposes the MemoizationStore to subclasses. NOTE: Only available after calling <see cref="CreateAndStartStoresAsync(OperationContext)"/>
        /// </summary>
        protected IMemoizationStore? MemoizationStore { get; set; }

        /// <inheritdoc />
        protected override Tracer Tracer => CacheTracer;

        /// <nodoc />
        protected abstract CacheTracer CacheTracer { get; }

        /// <summary>
        ///     Determines if the content session will be passed to the memoization store when constructing a non-readonly session.
        /// </summary>
        private readonly bool _passContentToMemoization;

        /// <summary>
        /// Gets whether stats should be from only content store.
        /// This is to avoid aggregating equivalent stats when content and memoization
        /// are backed by the same instance.
        /// </summary>
        protected virtual bool UseOnlyContentStats => false;

        /// <nodoc />
        protected OneLevelCacheBase(Guid id, bool passContentToMemoization)
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
            var (contentStoreResult, memoizationStoreResult) = await CreateAndStartStoresAsync(context);

            if (contentStoreResult.Succeeded && memoizationStoreResult.Succeeded)
            {
                // The properties must be initialized but only when the startup succeeded.
                Contract.Assert(ContentStore != null, "Content store must be initialized");
                Contract.Assert(MemoizationStore != null, "Memoization store must be initialized");
                Contract.Assert(!ReferenceEquals(ContentStore, MemoizationStore), "ContentStore and MemoizationStore should not be the same.");

                return BoolResult.Success;
            }
            else
            {
                // One of the startup operations failed.
                var sb = new StringBuilder();

                AppendIfError(sb, "Content store startup", contentStoreResult);

                if (contentStoreResult)
                {
                    Contract.Assert(ContentStore != null, "Content store must be initialized");
                    var r = await ContentStore.ShutdownAsync(context).ConfigureAwait(false);
                    AppendIfError(sb, "Content store shutdown", r);
                }

                AppendIfError(sb, "Memoization store startup", memoizationStoreResult);
                if (memoizationStoreResult)
                {
                    Contract.Assert(MemoizationStore != null, "Memoization store must be initialized");
                    var r = await MemoizationStore.ShutdownAsync(context).ConfigureAwait(false);
                    AppendIfError(sb, "Memoization store shutdown", r);
                }

                return new BoolResult(sb.ToString());
            }
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            await LogStatsAsync(context);

            var contentStoreTask = IfNotNull(ContentStore, store => Task.Run(() => store.ShutdownIfStartedAsync(context)));

            // This code potentially may cause unobserved task exception, but this is possible only when the callback
            // provided into Task.Run fails (like with NRE). But this should not be happening (anymore at least).
            var memoizationStoreResult = await IfNotNull(MemoizationStore, store => store.ShutdownIfStartedAsync(context)).ConfigureAwait(false);
            var contentStoreResult = await contentStoreTask.ConfigureAwait(false);

            BoolResult result;
            if (contentStoreResult.Succeeded && memoizationStoreResult.Succeeded)
            {
                result = BoolResult.Success;
            }
            else
            {
                var sb = new StringBuilder();
                AppendIfError(sb, "Content store shutdown", contentStoreResult);
                AppendIfError(sb, "Memoization store shutdown", memoizationStoreResult);
                result = new BoolResult(sb.ToString());
            }

            return result;
        }

        private static void AppendIfError(StringBuilder sb, string operation, BoolResult result)
        {
            if (!result)
            {
                sb.Append(sb.Length > 0 ? ", " : string.Empty);
                sb.Append($"{operation} failed, error=[{result}]");
            }
        }

        private async Task LogStatsAsync(OperationContext context)
        {
            var statsResult = await GetStatsCoreAsync(context).ConfigureAwait(false);

            if (statsResult.Succeeded)
            {
#if NET_FRAMEWORK
                LocalCacheStatsEventSource.Instance.Stats(statsResult.CounterSet);
#endif
                Tracer.TraceStatisticsAtShutdown(context, statsResult.CounterSet!, prefix: "OneLevelCacheStats");
            }
        }

        /// <inheritdoc />
        public CreateSessionResult<IReadOnlyCacheSession> CreateReadOnlySession(Context context, string name, ImplicitPin implicitPin)
        {
            Contract.Requires(ContentStore != null);
            Contract.Requires(MemoizationStore != null);

            return Tracing.CreateReadOnlySessionCall.Run(CacheTracer, context, name, () =>
            {
                var createContentResult = ContentStore.CreateReadOnlySession(context, name, implicitPin);
                if (!createContentResult)
                {
                    return new CreateSessionResult<IReadOnlyCacheSession>(createContentResult, "Content session creation failed");
                }
                
                var contentReadOnlySession = createContentResult.Session;

                var createMemoizationResult = MemoizationStore.CreateReadOnlySession(context, name);
                if (!createMemoizationResult)
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
            Contract.Requires(ContentStore != null);
            Contract.Requires(MemoizationStore != null);

            return Tracing.CreateSessionCall.Run(CacheTracer, context, name, () =>
            {
                var createContentResult = ContentStore.CreateSession(context, name, implicitPin);
                if (!createContentResult)
                {
                    return new CreateSessionResult<ICacheSession>(createContentResult, "Content session creation failed");
                }

                var contentSession = createContentResult.Session;

                var createMemoizationResult = _passContentToMemoization
                    ? MemoizationStore.CreateSession(context, name, contentSession)
                    : MemoizationStore.CreateSession(context, name);

                if (!createMemoizationResult)
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
            return GetStatsCall<Tracer>.RunAsync(Tracer, new OperationContext(context), () => GetStatsCoreAsync(context));
        }

        private Task<BoolResult> IfNotNull<T>(T? source, Func<T, Task<BoolResult>> func)
            where T : class
        {
            if (source is null)
            {
                return BoolResult.SuccessTask;
            }

            return func(source);
        }

        private Task<GetStatsResult> GetStatsResultCoreAsync<T>(T? source, Func<T, Task<GetStatsResult>> getStats) where T : class
        {
            if (source is null)
            {
                return Task.FromResult(new GetStatsResult(new CounterSet()));
            }

            return getStats(source);
        }

        private async Task<GetStatsResult> GetStatsCoreAsync(Context context)
        {
            // Both ContentStore and MemoizationStore may be null if startup failed.
            // Using a helper function to avoid NRE.
            var statsResult = await GetStatsResultCoreAsync(ContentStore, store => store.GetStatsAsync(context)).ConfigureAwait(false);

            if (!statsResult || UseOnlyContentStats)
            {
                return statsResult;
            }

            var counters = new CounterSet();
            counters.Merge(statsResult.CounterSet);

            statsResult = await GetStatsResultCoreAsync(MemoizationStore, store => store.GetStatsAsync(context)).ConfigureAwait(false);

            if (!statsResult)
            {
                return statsResult;
            }

            counters.Merge(statsResult.CounterSet);

            return new GetStatsResult(counters);
        }

        /// <inheritdoc />
        public IAsyncEnumerable<StructResult<StrongFingerprint>> EnumerateStrongFingerprints(Context context)
        {
            Contract.Assert(MemoizationStore != null, "Memoization store must be initialized");
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
        public async Task<Result<long>> RemoveFromTrackerAsync(Context context)
        {
            if (ContentStore is IRepairStore innerRepairStore)
            {
                return await innerRepairStore.RemoveFromTrackerAsync(context);
            }

            return new Result<long>($"{ContentStore} does not implement {nameof(IRepairStore)} in {nameof(OneLevelCache)}.");
        }

        /// <inheritdoc />
        public async Task<BoolResult> HandleCopyFileRequestAsync(Context context, ContentHash hash, CancellationToken token)
        {
            if (ContentStore is ICopyRequestHandler innerCopyStore)
            {
                return await innerCopyStore.HandleCopyFileRequestAsync(context, hash, token);
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
        public Task<DeleteResult> DeleteAsync(Context context, ContentHash contentHash, DeleteContentOptions? deleteOptions = null)
        {
            return ContentStore!.DeleteAsync(context, contentHash, deleteOptions);
        }

        /// <inheritdoc />
        public void PostInitializationCompleted(Context context, BoolResult result)
        {
            Contract.Requires(ContentStore != null, "ContentStore must be initialized here.");
            ContentStore.PostInitializationCompleted(context, result);
        }

        /// <inheritdoc />
        public Task<PutResult> HandlePushFileAsync(Context context, ContentHash hash, FileSource source, CancellationToken token)
        {
            if (ContentStore is IPushFileHandler handler)
            {
                return handler.HandlePushFileAsync(context, hash, source, token);
            }

            return Task.FromResult(new PutResult(new InvalidOperationException($"{nameof(ContentStore)} does not implement {nameof(IPushFileHandler)}"), hash));
        }

        /// <inheritdoc />
        public bool CanAcceptContent(Context context, ContentHash hash, out RejectionReason rejectionReason)
        {
            if (ContentStore is IPushFileHandler handler)
            {
                return handler.CanAcceptContent(context, hash, out rejectionReason);
            }

            rejectionReason = RejectionReason.NotSupported;
            return false;
        }
    }
}
