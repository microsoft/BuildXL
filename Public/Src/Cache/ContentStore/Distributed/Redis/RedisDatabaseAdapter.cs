// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Utilities.Tracing;
using Microsoft.Practices.TransientFaultHandling;
using StackExchange.Redis;

namespace BuildXL.Cache.ContentStore.Distributed.Redis
{
    /// <summary>
    /// Enumeration for tracking performance of redis operations.
    /// </summary>
    public enum RedisOperation
    {
        /// <summary>
        /// Cumulative counter for tracking overall number and duration of operations via redis.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        All,

        /// <nodoc />
        Retry,

        /// <summary>
        /// Cumulative counter for tracking overall number and duration of batch operations via redis.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        Batch,

        /// <nodoc />
        BatchSize,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        StringGet,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        GetBulkGlobal,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        RegisterLocalSetNonExistentHashEntries,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        RegisterLocalSetHashEntries,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        StartupGetOrAddLocalMachine,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        UpdateClusterState,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        MirrorClusterState,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        InvalidateLocalMachine,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        StringIncrement,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        StringSet,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        SetRemove,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        GetExpiry,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        CheckExists,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        TouchBulk,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        TrimBulk,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        TrimOrGetLastAccessTime,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        GetContentLocationMap,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        CheckMasterForLocations,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        UpdateBulk,
        
        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        TrimBulkRemote,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        GetCheckpoint,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        UploadCheckpoint,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        UpdateRole,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        DeleteFingerprint,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        RunQueryWithExpiryBump,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        ScanEntriesWithLastAccessTime,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        HashGetKeys,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        HashGetValue,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        HashSetValue,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        HashDeleteAndRestore,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        CompareExchange,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        GetContentHashList,
    }

    /// <nodoc />
    internal class RedisDatabaseAdapter
    {
        /// <summary>
        /// The keyspace used to prefix keys in the database
        /// </summary>
        public string KeySpace { get; }

        private readonly RedisDatabaseFactory _databaseFactory;
        private readonly RetryPolicy _redisRetryStrategy;

        // In some cases, Redis.StackExchange library may fail to reset the connection to Azure Redis Cache.
        // To work around this problem we reset the connection multiplexer if all the calls are failing with RedisConnectionException.
        private readonly int _redisConnectionErrorLimit;
        private int _connectionErrorCount;
        private readonly object _resetConnectionsLock = new object();

        /// <summary>
        /// Initializes a new instance of the <see cref="RedisDatabaseAdapter"/> class.
        /// </summary>
        public RedisDatabaseAdapter(RedisDatabaseFactory databaseFactory, string keySpace, int redisConnectionErrorLimit = int.MaxValue, int? retryCount = null)
        {
            _databaseFactory = databaseFactory;
            KeySpace = keySpace;
            _redisConnectionErrorLimit = redisConnectionErrorLimit;
            if (retryCount != null)
            {
                _redisRetryStrategy = new RetryPolicy(new RedisRetryPolicy(OnRedisException), retryCount.Value);
            }
            else
            {
                _redisRetryStrategy = new RetryPolicy(new RedisRetryPolicy(OnRedisException), RetryStrategy.DefaultExponential);
            }
        }

        /// <summary>
        /// Counters for tracking redis-related operations.
        /// </summary>
        public CounterCollection<RedisOperation> Counters { get; } = new CounterCollection<RedisOperation>();

        /// <summary>
        /// Creates a retryable Redis operation.
        /// </summary>
        /// <returns>A batch that can be used to enqueue batch operations.</returns>
        public IRedisBatch CreateBatchOperation(RedisOperation operation)
        {
            return new RedisBatch(operation, KeySpace);
        }

        /// <nodoc />
        public RedisBatch CreateBatch(RedisOperation operation)
        {
            return new RedisBatch(operation, KeySpace);
        }

        /// <summary>
        /// Enumerates all endpoints with a sample keys for each of them.
        /// </summary>
        public List<(string serverId, string serverSampleKey)> GetServerKeys(string serverId = null)
        {
            return GetServers(serverId)
                .Select(tpl => ((serverId: tpl.serverId, serverSampleKey: (string)tpl.server.Keys(pageSize: 1).FirstOrDefault())))
                .Where(t => t.serverId != null)
                .ToList();
        }

        private async Task<RedisInfo> GetRedisInfoAsync(IServer server)
        {
            return new RedisInfo(await server.InfoAsync());
        }

        /// <summary>
        /// Obtains statistics from Redis.
        /// </summary>
        public async Task<List<(string serverId, RedisInfo info)>> GetInfoAsync(string serverId = null)
        {
            var servers = GetServers(serverId);

            var tasks = servers.Select(s => (serverId: s.serverId, infoTask: GetRedisInfoAsync(s.server))).ToList();

            await Task.WhenAll(tasks.Select(t => t.infoTask));

            var result = tasks
                .Select(tpl => (serverId: tpl.serverId, info: tpl.infoTask.GetAwaiter().GetResult()))
                .Where(t => t.info != null).ToList();
            return result;
        }

        private List<(IServer server, string serverId)> GetServers(string serverId = null)
        {
            return _databaseFactory.GetEndPoints()
                .Select(ep => _databaseFactory.GetServer(ep))
                .Where(server => !server.IsSlave)
                .Where(server => serverId == null || GetServerId(server) == serverId)
                .Select(server => (server: server, serverId: GetServerId(server)))
                .Where(tpl => tpl.serverId != null)
                .ToList();
        }

        private static string GetServerId(IServer server)
        {
            if (server?.ClusterConfiguration == null)
            {
                return null;
            }

            return server.ClusterConfiguration[server.EndPoint]?.NodeId;
        }

        /// <summary>
        /// Given a batch with a set of operations, executes the set and awaits the results of the batch being available.
        /// </summary>
        /// <param name="context">Context.</param>
        /// <param name="batch">The batch to execute.</param>
        /// <param name="cancellationToken">A cancellation token for the task</param>
        /// <returns>A task that completes when all items in the batch are done.</returns>
        public async Task<BoolResult> ExecuteBatchOperationAsync(Context context, IRedisBatch batch, CancellationToken cancellationToken)
        {
            using (Counters[RedisOperation.All].Start())
            using (Counters[RedisOperation.Batch].Start())
            using (Counters[batch.Operation].Start())
            {
                Counters[RedisOperation.BatchSize].Add(batch.BatchSize);

                try
                {
                    await _redisRetryStrategy.ExecuteAsync(
                        async () =>
                        {
                            var database = await GetDatabaseAsync(context);
                            await batch.ExecuteBatchOperationAndGetCompletion(context, database);
                        },
                        cancellationToken);
                    await batch.NotifyConsumersOfSuccess();

                    RedisOperationSucceeded();
                    return BoolResult.Success;
                }
                catch (Exception ex)
                {
                    batch.NotifyConsumersOfFailure(ex);
                    HandleRedisExceptionAndResetMultiplexerIfNeeded(context, ex);
                    return new BoolResult(ex);
                }
            }
        }

        private void RedisOperationSucceeded()
        {
            // If the operation is successful, then reset the error count to 0.
            Interlocked.Exchange(ref _connectionErrorCount, 0);
        }

        /// <summary>
        /// This method "handles" redis connectivity errors and reset the connection multiplexer if there are no successful operations happening.
        /// </summary>
        private void HandleRedisExceptionAndResetMultiplexerIfNeeded(Context context, Exception exception)
        {
            if (IsRedisConnectionException(exception))
            {
                // Using double-checked locking approach to reset the connection multiplexer only once.
                // Checking for greater then or equals because another thread can increment _connectionErrorCount.
                if (Interlocked.Increment(ref _connectionErrorCount) >= _redisConnectionErrorLimit)
                {
                    lock (_resetConnectionsLock)
                    {
                        // The second read of _connectionErrorCount is a non-interlocked read, but it should be fine because it is happening under the lock.
                        if (_connectionErrorCount >= _redisConnectionErrorLimit)
                        {
                            // This means that there is no successful operations happening, and all the errors that we're seeing are redis connectivity issues.
                            // This is, effectively, a work-around for the issue in StackExchange.Redis library (https://github.com/StackExchange/StackExchange.Redis/issues/559).
                            context.Warning($"Reset redis connection due to connectivity issues. ConnectionErrorCount={_connectionErrorCount}, RedisConnectionErrorLimit={_redisConnectionErrorLimit}.");
                            Interlocked.Exchange(ref _connectionErrorCount, 0);
                            _databaseFactory.ResetConnectionMultiplexer();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// The call back that is called by the redis retry strategy.
        /// </summary>
        /// <remarks>
        /// There are two very similar methods: <see cref="OnRedisException"/> and <see cref="HandleRedisExceptionAndResetMultiplexerIfNeeded"/>. The first one (the current method) is called
        /// by the retry strategy to track and increment the failure count for all the retries not only for the final ones.
        /// The later is called manually after each failed operation and that method takes a context that allows us to trace and actually may reset the connection mutliplexer
        /// due to a number of redis connection exceptions.
        /// </remarks>
        private void OnRedisException(Exception exception)
        {
            // Lets be very pessimistic here: reset the connectivity errors every time when the operation succeed or any other exception
            // but redis connection exception occurs.
            if (IsRedisConnectionException(exception))
            {
                Interlocked.Increment(ref _connectionErrorCount);
            }

            // Don't reset the connection error count if another error occurs.
            // The counter is set to 0 for all the successful operations only.
        }

        private bool IsRedisConnectionException(Exception exception)
        {
            return exception is RedisConnectionException redisException &&
                   (redisException.Message.Contains("No connection available") ||
                    redisException.Message.Contains("UnableToResolvePhysicalConnection"));
        }

        /// <summary>
        /// Gets a redis value at a particular string key from Redis.
        /// </summary>
        /// <param name="context">the logging context</param>
        /// <param name="key">The key to fetch.</param>
        /// <param name="cancellationToken">A cancellation token</param>
        /// <param name="commandFlags">optional command flags to execute the command</param>
        public Task<RedisValue> StringGetAsync(Context context, string key, CancellationToken cancellationToken, CommandFlags commandFlags = CommandFlags.None)
            => PerformDatabaseOperationAsync(context, db => db.StringGetAsync(key, commandFlags), Counters[RedisOperation.StringGet], cancellationToken);

        /// <summary>
        /// Atomically increments an integer in redis and returns the incremented integer.
        /// </summary>
        /// <param name="context">the logging context</param>
        /// <param name="key">The key to fetch.</param>
        /// <param name="byValue">The value to increment by.</param>
        /// <param name="cancellationToken">A cancellation token</param>
        public Task<long> StringIncrementAsync(Context context, string key, CancellationToken cancellationToken, long byValue = 1)
            => PerformDatabaseOperationAsync(context, db => db.StringIncrementAsync(key, byValue), Counters[RedisOperation.StringIncrement], cancellationToken);

        /// <summary>
        /// Sets a string in redis to a particular value and returns whether or not the string was updated.
        /// </summary>
        public Task<bool> StringSetAsync(Context context, string stringKey, RedisValue redisValue, When when, CancellationToken cancellationToken)
            => PerformDatabaseOperationAsync(context, db => db.StringSetAsync(stringKey, redisValue, when: when), Counters[RedisOperation.StringSet], cancellationToken);

        /// <summary>
        /// Sets a string in redis to a particular value and returns whether or not the string was updated.
        /// </summary>
        public Task<bool> StringSetAsync(Context context, string stringKey, RedisValue redisValue, TimeSpan? expiry, When when, CancellationToken cancellationToken)
            => PerformDatabaseOperationAsync(context, db => db.StringSetAsync(stringKey, redisValue, expiry, when), Counters[RedisOperation.StringSet], cancellationToken);

        /// <summary>
        /// Get expiry of specified key.
        /// </summary>
        public Task<TimeSpan?> GetExpiryAsync(Context context, RedisKey stringKey, CancellationToken cancellationToken)
            => PerformDatabaseOperationAsync(context, db => db.KeyTimeToLiveAsync(stringKey), Counters[RedisOperation.GetExpiry], cancellationToken);

        /// <summary>
        /// Checks whether a key exists.
        /// </summary>
        public Task<bool> KeyExistsAsync(Context context, RedisKey key, CancellationToken token)
            => PerformDatabaseOperationAsync(context, db => db.KeyExistsAsync(key), Counters[RedisOperation.CheckExists], token);
        
        /// <summary>
        /// Gets all the field names associated with a key
        /// </summary>
        public Task<RedisValue[]> GetHashKeysAsync(Context context, RedisKey key, CancellationToken token)
            => PerformDatabaseOperationAsync(context, db => db.HashKeysAsync(key), Counters[RedisOperation.HashGetKeys], token);

        /// <summary>
        /// Gets the value associated to a key's field.
        /// </summary>
        public Task<RedisValue> GetHashValueAsync(Context context, RedisKey key, RedisValue hashField, CancellationToken token)
            => PerformDatabaseOperationAsync(context, db => db.HashGetAsync(key, hashField), Counters[RedisOperation.HashGetValue], token);

        /// <summary>
        /// Sets the value associated to a key's field.
        /// </summary>
        public Task<bool> SetHashValueAsync(Context context, RedisKey key, RedisValue hashField, RedisValue value, When when, CancellationToken token)
            => PerformDatabaseOperationAsync(context, db => db.HashSetAsync(key, hashField, value, when), Counters[RedisOperation.HashSetValue], token);

        private async Task<T> PerformDatabaseOperationAsync<T>(Context context, Func<IDatabase, Task<T>> operation, Counter stopwatch, CancellationToken token)
        {
            using (Counters[RedisOperation.All].Start())
            using (stopwatch.Start())
            {
                try
                {
                    var result = await _redisRetryStrategy.ExecuteAsync(
                        async () =>
                        {
                            var database = await GetDatabaseAsync(context);
                            return await operation(database);
                        }, token);

                    RedisOperationSucceeded();
                    return result;
                }
                catch (Exception e)
                {
                    HandleRedisExceptionAndResetMultiplexerIfNeeded(context, e);
                    throw;
                }
            }
        }

        private Task<IDatabase> GetDatabaseAsync(Context context)
        {
            return _databaseFactory.GetDatabaseWithKeyPrefix(context, KeySpace);
        }

        private class RedisRetryPolicy : ITransientErrorDetectionStrategy
        {
            private readonly Action<Exception> _exceptionObserver;

            /// <nodoc />
            public RedisRetryPolicy(Action<Exception> exceptionObserver = null)
            {
                _exceptionObserver = exceptionObserver;
            }

            /// <inheritdoc />
            public bool IsTransient(Exception ex)
            {
                _exceptionObserver?.Invoke(ex);

                // naively retry all redis server exceptions.

                if (ex is RedisException redisException)
                {
                    // If the error contains the following text, then the error is not transient.
                    return !(redisException.ToString().Contains("Error compiling script") || redisException.ToString().Contains("Error running script"));
                }

                return false;
            }
        }
    }
}
