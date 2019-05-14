// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

extern alias Async;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.MemoizationStore.Distributed.Metadata;
using BuildXL.Cache.MemoizationStore.Distributed.Sessions;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Tracing;

namespace BuildXL.Cache.MemoizationStore.Distributed.Stores
{
    /// <summary>
    /// Distributed cache
    /// </summary>
    /// <remarks>
    /// <para>
    ///     This is mainly a wrapper over any given <see cref="ICache"/> with the following characteristics:
    ///
    ///         * proxy content queries
    ///         * cache metadata queries
    ///
    ///     All sessions will use the same metadata cache i.e. be in the same cache universe.
    ///     Note: all queries to the inner ICache must go through this cache otherwise we risk the metadata cache diverging
    /// </para>
    /// <para>
    ///     The recommendation is to only use an authoratitive ICache as it's inner cache (e.g. VSTS BuildCache cache) though it
    ///     could work with other implementations as well.
    ///
    ///     There is no way to assert whether a cache is authoratitive in the current ICache interface though.
    /// </para>
    /// </remarks>
    public class DistributedCache : ICache
    {
        private readonly ILogger _logger;
        private readonly ICache _innerICache;
        private readonly IMetadataCache _metadataCache;
        private readonly DistributedCacheSessionTracer _tracer;
        private readonly ReadThroughMode _readThroughMode;

        private bool _disposed;

        /// <summary>
        ///     Initializes a new instance of the <see cref="DistributedCache" /> class.
        /// </summary>
        public DistributedCache(ILogger logger, ICache innerICache, IMetadataCache metadataCache, DistributedCacheSessionTracer cacheTracer, ReadThroughMode readThroughMode)
        {
            Contract.Requires(logger != null);
            Contract.Requires(innerICache != null);
            Contract.Requires(metadataCache != null);

            _logger = logger;
            _innerICache = innerICache;
            _metadataCache = metadataCache;

            _tracer = cacheTracer;
            _readThroughMode = readThroughMode;
        }

        /// <summary>
        /// Gets an Id
        /// </summary>
        public Guid Id => _innerICache.Id;

        /// <inheritdoc />
        public bool StartupCompleted { get; private set; }

        /// <inheritdoc />
        public bool StartupStarted { get; private set; }

        /// <inheritdoc />
        public bool ShutdownCompleted { get; private set; }

        /// <inheritdoc />
        public bool ShutdownStarted { get; private set; }

        /// <inheritdoc />
        public Task<BoolResult> StartupAsync(Context context)
        {
            StartupStarted = true;

            return StartupCall<MemoizationStoreTracer>.RunAsync(_tracer, context, async () =>
            {
                var innerCacheStarted = await _innerICache.StartupAsync(context);
                if (!innerCacheStarted)
                {
                    _logger.Error("StartUp call on inner cache failed.");
                    return new BoolResult(innerCacheStarted);
                }

                var metadataCacheStarted = await _metadataCache.StartupAsync(context);
                if (!metadataCacheStarted)
                {
                    _logger.Error("StartUp call on metadata cache failed. Shutting down inner cache.");
                    await _innerICache.ShutdownAsync(context).ThrowIfFailure();
                    return new BoolResult(metadataCacheStarted);
                }

                StartupCompleted = true;

                return BoolResult.Success;
            });
        }

        /// <inheritdoc />
        public Task<BoolResult> ShutdownAsync(Context context)
        {
            ShutdownStarted = true;
            return ShutdownCall<MemoizationStoreTracer>.RunAsync(_tracer, context, async () =>
            {
                GetStatsResult stats = GetStatsInternal();
                if (!stats)
                {
                    context.Debug($"Get stats failed with error {stats.ErrorMessage}; Diagnostics {stats.Diagnostics}");
                }
                else
                {
                    context.Debug("DistributedCache Stats:");
                    stats.CounterSet.LogOrderedNameValuePairs(s => _tracer.Debug(context, s));
                }

                var innerCacheShutdown = await _innerICache.ShutdownAsync(context);
                var metadataCacheShutdown = await _metadataCache.ShutdownAsync(context);

                if (!innerCacheShutdown)
                {
                    // TODO: should print errors as well.
                    _logger.Error("Shutdown call on inner cache failed.");
                    return new BoolResult(innerCacheShutdown);
                }

                if (!metadataCacheShutdown)
                {
                    _logger.Error("Shutdown call on metadata cache failed.");
                    return new BoolResult(metadataCacheShutdown);
                }

                ShutdownCompleted = true;

                return BoolResult.Success;
            });
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        ///     Protected implementation of Dispose pattern.
        /// </summary>
        private void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                _innerICache?.Dispose();
                _metadataCache?.Dispose();
            }

            _disposed = true;
        }

        /// <inheritdoc />
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        public CreateSessionResult<IReadOnlyCacheSession> CreateReadOnlySession(Context context, string name, ImplicitPin implicitPin)
        {
            var innerSession = _innerICache.CreateReadOnlySession(context, name, implicitPin);
            var session = new ReadOnlyDistributedCacheSession(_logger, name, innerSession.Session, _innerICache.Id, _metadataCache, _tracer, _readThroughMode);
            return new CreateSessionResult<IReadOnlyCacheSession>(session);
        }

        /// <inheritdoc />
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        public CreateSessionResult<ICacheSession> CreateSession(Context context, string name, ImplicitPin implicitPin)
        {
            var innerSession = _innerICache.CreateSession(context, name, implicitPin);
            var session = new DistributedCacheSession(_logger, name, innerSession.Session, _innerICache.Id, _metadataCache, _tracer, _readThroughMode);
            return new CreateSessionResult<ICacheSession>(session);
        }

        /// <inheritdoc />
        public async Task<GetStatsResult> GetStatsAsync(Context context)
        {
            try
            {
                GetStatsResult stats = await _innerICache.GetStatsAsync(context);
                GetStatsResult currentStats = GetStatsInternal();

                CounterSet counterSet = new CounterSet();
                if (stats.Succeeded)
                {
                    counterSet.Merge(stats.CounterSet, $"{_innerICache.GetType().Name}.");
                }

                if (currentStats.Succeeded)
                {
                    counterSet.Merge(currentStats.CounterSet, $"{nameof(DistributedCache)}.");
                }

                return new GetStatsResult(counterSet);
            }
            catch (Exception ex)
            {
                return new GetStatsResult(ex);
            }
        }

        [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
        private GetStatsResult GetStatsInternal()
        {
            try
            {
                return new GetStatsResult(_tracer.GetCounters());
            }
            catch (Exception ex)
            {
                return new GetStatsResult(ex);
            }
        }

        /// <inheritdoc />
        public Async::System.Collections.Generic.IAsyncEnumerable<StructResult<StrongFingerprint>> EnumerateStrongFingerprints(Context context)
        {
            return _innerICache.EnumerateStrongFingerprints(context);
        }
    }
}
