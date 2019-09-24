// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Synchronization;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.MemoizationStore.Distributed.Metadata.Tracing;
using BuildXL.Cache.MemoizationStore.Distributed.Utils;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using StackExchange.Redis;

namespace BuildXL.Cache.MemoizationStore.Distributed.Metadata
{
    /// <summary>
    /// An implementation of <see cref="IMetadataCache"/> backed by Redis cache
    /// </summary>
    /// <remarks>
    /// <para>
    ///     There are two types of metadata cache entires saved in Redis:
    ///
    ///         WeakFingerprint     -> SET (Selectors)
    ///         StrongFingerprint   -> ContentHashList
    ///
    ///     where SET (Selectors) is a Redis set of Selectors
    /// </para>
    /// <para>
    ///     Serialization and deserialization for all keys and values is handled by <see cref="IRedisSerializer"/>
    ///     All keys are prefix with the given keySpace for namespacing the entries.
    /// </para>
    /// <para>
    ///     The selectors are saved as a set under a single weak fingerprint key so that the lookup is a point query.
    ///     Other schemas involve using Redis queries or partitions which are an order of magnitude slower due to
    ///     key scans and locks on multiple partitions.
    ///     Also expiration of cache entries is determined at the key level, so it's not possible to age out particular
    ///     members of a Redis set.
    ///     These factors influenced <see cref="IMetadataCache"/> to use current design instead of the aggregator model
    /// </para>
    /// </remarks>
    public class RedisMetadataCache : StartupShutdownBase, IMetadataCache
    {
        /// <summary>
        ///     Default time by which Redis key lifetime is extended on access.
        /// </summary>
        internal static readonly TimeSpan DefaultCacheKeyBumpTime = TimeSpan.FromMinutes(15);

        /// <summary>
        ///     Provider of a connection string for the redis instance.
        /// </summary>
        internal readonly IConnectionStringProvider ConnectionStringProvider;

        /// <summary>
        ///     Redis keyspace to operate under.
        /// </summary>
        internal readonly string Keyspace;

        /// <summary>
        ///     Time by which Redis key lifetime is extended on access.
        /// </summary>
        internal readonly TimeSpan CacheKeyBumpTime;

        private readonly Tracer _tracer = new Tracer(nameof(RedisMetadataCache));
        private readonly IRedisSerializer _redisSerializer;
        private readonly IMetadataCacheTracer _cacheTracer;
        private RedisDatabaseAdapter _dbAdapter;
        private RedisDatabaseAdapter _stringDatabaseAdapter;
        private BackgroundTaskTracker _taskTracker;

        /// <inheritdoc />
        protected override Tracer Tracer => _tracer;

        /// <summary>
        ///     Initializes a new instance of the <see cref="RedisMetadataCache"/> class
        /// </summary>
        public RedisMetadataCache(IConnectionStringProvider connectionStringProvider, IRedisSerializer redisSerializer, string keySpace, IMetadataCacheTracer cacheTracer, TimeSpan? cacheKeyBumpTime = null)
        {
            Contract.Requires(connectionStringProvider != null);
            Contract.Requires(redisSerializer != null);
            Contract.Requires(!string.IsNullOrWhiteSpace(keySpace));

            ConnectionStringProvider = connectionStringProvider;
            _redisSerializer = redisSerializer;
            Keyspace = keySpace;
            _cacheTracer = cacheTracer;
            CacheKeyBumpTime = cacheKeyBumpTime.GetValueOrDefault(DefaultCacheKeyBumpTime);
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            _taskTracker = new BackgroundTaskTracker(nameof(RedisMetadataCache), new Context(context));
            var redisDatabaseAdapter = new RedisDatabaseAdapter(await RedisDatabaseFactory.CreateAsync(context, ConnectionStringProvider), Keyspace);
            _dbAdapter = redisDatabaseAdapter;
            _stringDatabaseAdapter = redisDatabaseAdapter;
            return BoolResult.Success;
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            if (_taskTracker != null)
            {
                await _taskTracker.Synchronize();
                await _taskTracker.ShutdownAsync(context);
            }

            return BoolResult.Success;
        }

        /// <inheritdoc />
        protected override void DisposeCore() => _taskTracker?.Dispose();

        /// <inheritdoc />
        public Task<Result<Selector[]>> GetOrAddSelectorsAsync(Context context, Fingerprint weakFingerprint, Func<Fingerprint, Task<Result<Selector[]>>> getFunc)
        {
            return GetOrAddSelectorsAsync(context, weakFingerprint, getFunc, CancellationToken.None);
        }

        /// <inheritdoc />
        public async Task<GetContentHashListResult> GetOrAddContentHashListAsync(Context context, StrongFingerprint strongFingerprint, Func<StrongFingerprint, Task<GetContentHashListResult>> getFuncAsync)
        {
            try
            {
                var cacheKey = _redisSerializer.ToRedisKey(strongFingerprint);
                RedisValue cacheResult;

                Stopwatch stopwatch = Stopwatch.StartNew();
                try
                {
                    _cacheTracer.GetContentHashListStart(context);
                    cacheResult = await StringGetWithExpiryBumpAsync(context, cacheKey);
                }
                finally
                {
                    _cacheTracer.GetContentHashListStop(context, stopwatch.Elapsed);
                }

                if (!cacheResult.HasValue)
                {
                    _cacheTracer.RecordContentHashListFetchedFromBackingStore(context, strongFingerprint);
                    GetContentHashListResult getResult = await getFuncAsync(strongFingerprint);

                    if (getResult.Succeeded)
                    {
                        var cacheValue = _redisSerializer.ToRedisValue(getResult.ContentHashListWithDeterminism);

                        stopwatch = Stopwatch.StartNew();
                        try
                        {
                            _cacheTracer.AddContentHashListStart(context);
                            bool result = await StringSetWithExpiryBumpAsync(context, cacheKey, cacheValue);
                            context.Logger.Debug($"Added redis cache entry for {strongFingerprint}: {getResult}. Result: {result}");
                        }
                        finally
                        {
                            _cacheTracer.AddContentHashListStop(context, stopwatch.Elapsed);
                        }
                    }

                    return getResult;
                }
                else
                {
                    _cacheTracer.RecordGetContentHashListFetchedDistributed(context, strongFingerprint);
                }

                return new GetContentHashListResult(_redisSerializer.AsContentHashList(cacheResult));
            }
            catch (Exception ex)
            {
                return new GetContentHashListResult(ex);
            }
        }

        /// <inheritdoc />
        public async Task<BoolResult> DeleteFingerprintAsync(Context context, StrongFingerprint strongFingerprint)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                _cacheTracer.InvalidateCacheEntryStart(context, strongFingerprint);
                var batch = _dbAdapter.CreateBatchOperation(RedisOperation.DeleteFingerprint);

                // Tasks
                var deleteStrongFingerprint = batch.KeyDeleteAsync(_redisSerializer.ToRedisKey(strongFingerprint)).FireAndForgetAndReturnTask(context);
                var deleteWeakFingerprint = batch.KeyDeleteAsync(_redisSerializer.ToRedisKey(strongFingerprint.WeakFingerprint)).FireAndForgetAndReturnTask(context);

                await _dbAdapter.ExecuteBatchOperationAsync(context, batch, CancellationToken.None).IgnoreFailure();

                var strongDeleted = await deleteStrongFingerprint;
                var weakDeleted = await deleteWeakFingerprint;

                return BoolResult.Success;
            }
            catch (Exception ex)
            {
                return new BoolResult(ex);
            }
            finally
            {
                _cacheTracer.InvalidateCacheEntryStop(context, stopwatch.Elapsed);
            }
        }

        private async Task<Result<Selector[]>> GetOrAddSelectorsAsync(
            Context context,
            Fingerprint weakFingerprint,
            Func<Fingerprint, Task<Result<Selector[]>>> getFunc,
            CancellationToken cancellationToken)
        {
            try
            {
                var cacheKey = _redisSerializer.ToRedisKey(weakFingerprint);

                RedisValue[] cacheSelectors;

                Stopwatch stopwatch = Stopwatch.StartNew();
                try
                {
                    _cacheTracer.GetDistributedSelectorsStart(context);
                    cacheSelectors = await GetSetMembersWithExpiryBumpAsync(context, cacheKey);
                }
                finally
                {
                    _cacheTracer.GetDistributedSelectorsStop(context, stopwatch.Elapsed);
                }

                if (cacheSelectors.Length == 0)
                {
                    _cacheTracer.RecordSelectorsFetchedFromBackingStore(context, weakFingerprint);
                    Result<Selector[]> selectorResults = await getFunc(weakFingerprint).ConfigureAwait(false);
                    if (!selectorResults || selectorResults.Value.Length == 0)
                    {
                        // Redis throws an error if set add is called without any values. Also, undefined keys are treated as empty sets.
                        // So currently skip trying to cache GetSelectors returning empty.
                        // If needed, we need to create a sentinel value to denote an known empty set v/s undefined.
                        return selectorResults;
                    }

                    {
                        var selectors = selectorResults.Value;
                        var cacheValues = _redisSerializer.ToRedisValues(selectors);

                        stopwatch = Stopwatch.StartNew();
                        try
                        {
                            _cacheTracer.AddSelectorsStart(context);
                            var addedCount = await AddSetMembersWithExpiryBumpAsync(context, cacheKey, cacheValues);
                            if (cacheValues.Length != addedCount)
                            {
                                _tracer.Warning(context, $"Expected to add {cacheValues.Length} members but actually added {addedCount}.");
                            }

                            _tracer.Debug(context, $"Added redis cache entry for {weakFingerprint}: {selectors.Length} selectors.");
                        }
                        finally
                        {
                            _cacheTracer.AddSelectorsStop(context, stopwatch.Elapsed);
                        }
                    }

                    return selectorResults;
                }
                else
                {
                    Selector[] result = _redisSerializer.AsSelectors(cacheSelectors).ToArray();
                    _cacheTracer.RecordSelectorsFetchedDistributed(context, weakFingerprint, result.Length);
                    return result;
                }
            }
            catch (Exception ex)
            {
                return Result.FromException<Selector[]>(ex);
            }
        }

        private Task<bool> StringSetWithExpiryBumpAsync(Context context, string cacheKey, RedisValue value)
        {
            return _stringDatabaseAdapter.StringSetAsync(context, cacheKey, value, CacheKeyBumpTime, When.Always, CancellationToken.None);
        }

        private Task<RedisValue> StringGetWithExpiryBumpAsync(Context context, string cacheKey)
        {
            return RunQueryWithExpiryBump(context, cacheKey, batch => batch.StringGetAsync(cacheKey));
        }

        private Task<RedisValue[]> GetSetMembersWithExpiryBumpAsync(Context context, string cacheKey)
        {
            return RunQueryWithExpiryBump(context, cacheKey, batch => batch.SetMembersAsync(cacheKey));
        }

        private Task<long> AddSetMembersWithExpiryBumpAsync(Context context, string cacheKey, RedisValue[] values)
        {
            return RunQueryWithExpiryBump(context, cacheKey, batch => batch.SetAddAsync(cacheKey, values));
        }

        /// <summary>
        /// Runs a given redis query and bumps the key expiration in batch mode
        /// </summary>
        private async Task<T> RunQueryWithExpiryBump<T>(Context context, string cacheKey, Func<IRedisBatch, Task<T>> redisQuery)
        {
            var batch = _dbAdapter.CreateBatchOperation(RedisOperation.RunQueryWithExpiryBump);

            var addTask = redisQuery(batch).FireAndForgetAndReturnTask(context);

            // Return value is not used, but task tracker ensures that no unhandled exception is thrown during garbage collection
            _taskTracker.Add(batch.KeyExpireAsync(cacheKey, DateTime.UtcNow.Add(CacheKeyBumpTime)));

            await _dbAdapter.ExecuteBatchOperationAsync(context, batch, CancellationToken.None).IgnoreFailure();

            return await addTask;
        }
    }
}
