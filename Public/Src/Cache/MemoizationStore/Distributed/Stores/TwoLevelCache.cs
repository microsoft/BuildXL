// Copyright (c) Microsoft Corporation. All Rights Reserved.

using System;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Tracing;
using CreateReadOnlySessionCall = BuildXL.Cache.MemoizationStore.Tracing.CreateReadOnlySessionCall;
using CreateSessionCall = BuildXL.Cache.MemoizationStore.Tracing.CreateSessionCall;

namespace BuildXL.Cache.MemoizationStore.Distributed.Stores
{
    /// <summary>
    /// "Vertical aggregator" that wraps a local cache (L1) and remote (L2 - datacenter and L3 - backing storage) cache
    /// under an <see cref="ICache"/> interface.
    /// </summary>
    public sealed class TwoLevelCache : ICache
    {
        private static readonly Guid CloudBuildGuid = new Guid("241a1e30-a77b-49d8-82cf-52b6b70eb10f");
        private readonly CacheTracer _tracer = new CacheTracer(nameof(TwoLevelCache));
        private readonly ICache _localCache;
        private readonly ICache _remoteCache;
        private readonly TwoLevelCacheConfiguration _config;

        /// <nodoc />
        [Obsolete("Please use constructor with TwoLevelCacheConfiguration object ")]
        public TwoLevelCache(
            ICache localCache,
            ICache remoteCache,
            bool remoteCacheIsReadOnly = true,
            bool alwaysUpdateFromRemote = false)
            : this(
                  localCache,
                  remoteCache,
                  new TwoLevelCacheConfiguration {
                      RemoteCacheIsReadOnly = remoteCacheIsReadOnly,
                      AlwaysUpdateFromRemote = alwaysUpdateFromRemote })
        {
        }

        /// <nodoc />
        public TwoLevelCache(ICache localCache, ICache remoteCache, TwoLevelCacheConfiguration config)
        {
            _localCache = localCache;
            _remoteCache = remoteCache;
            _config = config;
        }

        private async Task<(TResult Local, TResult Remote)> MultiLevel<TResult>(Func<ICache, Task<TResult>> func)
        {
            // Running the function inside Task.Run to make the operations fully asynchronous.
            Task<TResult> localCacheTask = Task.Run(() => func(_localCache));
            Task<TResult> remoteCacheTask = Task.Run(() => func(_remoteCache));

            await Task.WhenAll(localCacheTask, remoteCacheTask);

            return (await localCacheTask, await remoteCacheTask);
        }

        private async Task<(TResult Local, TResult Remote)> MultiLevel<TResult>(Func<ICache, TResult> func)
        {
            // Running the function inside Task.Run to make the operations fully asynchronous.
            Task<TResult> localCacheTask = Task.Run(() => func(_localCache));
            Task<TResult> remoteCacheTask = Task.Run(() => func(_remoteCache));

            await Task.WhenAll(localCacheTask, remoteCacheTask);

            return (await localCacheTask, await remoteCacheTask);
        }

        /// <inheritdoc />
        public Task<BoolResult> StartupAsync(Context context)
        {
            return StartupCall<CacheTracer>.RunAsync(
                _tracer,
                context,
                async () =>
                {
                    StartupStarted = true;

                    (BoolResult local, BoolResult remote) = await MultiLevel(store => store.StartupAsync(context));
                    BoolResult result = local & remote;

                    if (!result.Succeeded)
                    {
                        // One of the initialization failed.
                        // Need to shut down the stores that were properly initialized.
                        if (local.Succeeded)
                        {
                            await _localCache.ShutdownAsync(context).TraceIfFailure(context);
                        }

                        if (remote.Succeeded)
                        {
                            await _remoteCache.ShutdownAsync(context).TraceIfFailure(context);
                        }
                    }

                    StartupStarted = false;
                    StartupCompleted = true;

                    return result;
                });
        }

        /// <inheritdoc />
        public bool StartupCompleted { get; private set; }

        /// <inheritdoc />
        public bool StartupStarted { get; private set; }

        /// <inheritdoc />
        public void Dispose()
        {
            _localCache.Dispose();
            _remoteCache.Dispose();
        }

        /// <inheritdoc />
        public Task<BoolResult> ShutdownAsync(Context context)
        {
            return ShutdownCall<CacheTracer>.RunAsync(
                _tracer,
                context,
                async () =>
                {
                    ShutdownStarted = true;

                    (BoolResult localResult, BoolResult remoteResult) = await MultiLevel(store => store.ShutdownAsync(context));

                    ShutdownStarted = false;
                    ShutdownCompleted = true;

                    return localResult & remoteResult;
                });
        }

        /// <inheritdoc />
        public bool ShutdownCompleted { get; private set; }

        /// <inheritdoc />
        public bool ShutdownStarted { get; private set; }

        /// <inheritdoc />
        public CreateSessionResult<IReadOnlyCacheSession> CreateReadOnlySession(
            Context context,
            string name,
            ImplicitPin implicitPin)
        {
            return CreateReadOnlySessionCall.Run(
                _tracer,
                context,
                name,
                () =>
                {
                    CreateSessionResult<ICacheSession> localSession = _localCache.CreateSession(context, name, implicitPin);
                    if (!localSession.Succeeded)
                    {
                        return new CreateSessionResult<IReadOnlyCacheSession>(localSession);
                    }

                    CreateSessionResult<ICacheSession> remoteSession = _remoteCache.CreateSession(context, name, implicitPin);
                    if (!remoteSession.Succeeded)
                    {
                        return new CreateSessionResult<IReadOnlyCacheSession>(remoteSession);
                    }

                    IReadOnlyCacheSession cacheSession = new TwoLevelCacheSession(
                        name,
                        localSession.Session,
                        remoteSession.Session,
                        _config);
                    return new CreateSessionResult<IReadOnlyCacheSession>(cacheSession);
                });
        }

        /// <inheritdoc />
        public CreateSessionResult<ICacheSession> CreateSession(Context context, string name, ImplicitPin implicitPin)
        {
            return CreateSessionCall.Run(
                _tracer,
                context,
                name,
                () => CreateSessionAsync(context, name, implicitPin).GetAwaiter().GetResult());
        }

        private async Task<CreateSessionResult<ICacheSession>> CreateSessionAsync(Context context, string name, ImplicitPin implicitPin)
        {
            (CreateSessionResult<ICacheSession> localResult, CreateSessionResult<ICacheSession> remoteResult) = await MultiLevel(cache => cache.CreateSession(context, name, implicitPin));

            if (localResult & remoteResult)
            {
                // Both succeeded.
                TwoLevelCacheSession cacheSession = new TwoLevelCacheSession(
                    name,
                    localResult.Session,
                    remoteResult.Session,
                    _config);
                return new CreateSessionResult<ICacheSession>(cacheSession);
            }

            // One of the operations has failed.
            if (localResult)
            {
                // creating a local session has failed
                await localResult.Session.ShutdownAsync(context).TraceIfFailure(context);
                return remoteResult;
            }
            else
            {
                // creating a remote session has failed
                await remoteResult.Session.ShutdownAsync(context).TraceIfFailure(context);
                return localResult;
            }
        }

        /// <inheritdoc />
        public async Task<GetStatsResult> GetStatsAsync(Context context)
        {
            (GetStatsResult localStats, GetStatsResult remoteStats) = await MultiLevel(cache => cache.GetStatsAsync(context));

            if (localStats.Succeeded && remoteStats.Succeeded)
            {
                // Using a key prefix to avoid an error caused by duplicated keys.
                localStats.CounterSet.Merge(remoteStats.CounterSet, "Remote");
                return new GetStatsResult(localStats.CounterSet);
            }

            // One of the operation has failed. Returning the failure back to the caller.
            return !localStats.Succeeded ? localStats : remoteStats;
        }

        /// <inheritdoc />
        public Guid Id => CloudBuildGuid;

        /// <inheritdoc />
        public System.Collections.Generic.IAsyncEnumerable<StructResult<StrongFingerprint>> EnumerateStrongFingerprints(Context context)
        {
            return _localCache.EnumerateStrongFingerprints(context).Concat(_remoteCache.EnumerateStrongFingerprints(context));
        }
    }
}
