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

        /// <summary>
        /// Initializes a new instance of the <see cref="RedisDatabaseAdapter"/> class.
        /// </summary>
        public RedisDatabaseAdapter(RedisDatabaseFactory databaseFactory, string keySpace)
        {
            _databaseFactory = databaseFactory;
            KeySpace = keySpace;
            _redisRetryStrategy = new RetryPolicy(new RedisRetryPolicy(), RetryStrategy.DefaultExponential);
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

        private List<(IServer server, string serverId)> GetServers(string serverId)
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
                    return BoolResult.Success;
                }
                catch (Exception ex)
                {
                    batch.NotifyConsumersOfFailure(ex);
                    return new BoolResult(ex);
                }
            }
        }

        /// <summary>
        /// Gets a redis value at a particular string key from Redis.
        /// </summary>
        /// <param name="context">the logging context</param>
        /// <param name="key">The key to fetch.</param>
        /// <param name="cancellationToken">A cancellation token</param>
        /// <param name="commandFlags">optional command flags to execute the command</param>
        public Task<RedisValue> StringGetAsync(Context context, string key, CancellationToken cancellationToken, CommandFlags commandFlags = CommandFlags.None)
        {
            using (Counters[RedisOperation.All].Start())
            using (Counters[RedisOperation.StringGet].Start())
            {
                return _redisRetryStrategy.ExecuteAsync(
                    async () =>
                    {
                        var database = await GetDatabaseAsync(context);
                        return await database.StringGetAsync(key, commandFlags);
                    },
                    cancellationToken);
            }
        }

        /// <summary>
        /// Atomically increments an integer in redis and returns the incremented integer.
        /// </summary>
        /// <param name="context">the logging context</param>
        /// <param name="key">The key to fetch.</param>
        /// <param name="byValue">The value to increment by.</param>
        /// <param name="cancellationToken">A cancellation token</param>
        public Task<long> StringIncrementAsync(Context context, string key, CancellationToken cancellationToken, long byValue = 1)
        {
            using (Counters[RedisOperation.All].Start())
            using (Counters[RedisOperation.StringIncrement].Start())
            {
                return _redisRetryStrategy.ExecuteAsync(
                    async () =>
                    {
                        var database = await GetDatabaseAsync(context);
                        return await database.StringIncrementAsync(key, byValue);
                    }, cancellationToken);
            }
        }

        /// <summary>
        /// Sets a string in redis to a particular value and returns whether or not the string was updated.
        /// </summary>
        public Task<bool> StringSetAsync(Context context, string stringKey, RedisValue redisValue, When when, CancellationToken cancellationToken)
        {
            using (Counters[RedisOperation.All].Start())
            using (Counters[RedisOperation.StringSet].Start())
            {
                return _redisRetryStrategy.ExecuteAsync(
                    async () =>
                    {
                        var database = await GetDatabaseAsync(context);
                        return await database.StringSetAsync(stringKey, redisValue, when: when);
                    }, cancellationToken);
            }
        }

        /// <summary>
        /// Sets a string in redis to a particular value and returns whether or not the string was updated.
        /// </summary>
        public Task<bool> StringSetAsync(Context context, string stringKey, RedisValue redisValue, TimeSpan? expiry, When when, CancellationToken cancellationToken)
        {
            using (Counters[RedisOperation.All].Start())
            using (Counters[RedisOperation.StringSet].Start())
            {
                return _redisRetryStrategy.ExecuteAsync(
                    async () =>
                    {
                        var database = await GetDatabaseAsync(context);
                        return await database.StringSetAsync(stringKey, redisValue, expiry, when);
                    }, cancellationToken);
            }
        }

        /// <summary>
        /// Get expiry of specified key.
        /// </summary>
        public Task<TimeSpan?> GetExpiryAsync(Context context, RedisKey stringKey, CancellationToken cancellationToken)
        {
            using (Counters[RedisOperation.All].Start())
            using (Counters[RedisOperation.GetExpiry].Start())
            {
                return _redisRetryStrategy.ExecuteAsync(
                    async () =>
                    {
                        var database = await GetDatabaseAsync(context);
                        return await database.KeyTimeToLiveAsync(stringKey);
                    }, cancellationToken);
            }
        }

        /// <summary>
        /// Checks whether a key exists.
        /// </summary>
        public Task<bool> KeyExists(Context context, RedisKey key, CancellationToken token)
        {
            using (Counters[RedisOperation.All].Start())
            using (Counters[RedisOperation.CheckExists].Start())
            {
                return _redisRetryStrategy.ExecuteAsync(
                    async () =>
                    {
                        var database = await GetDatabaseAsync(context);
                        return await database.KeyExistsAsync(key);
                    }, token);
            }
        }

        private Task<IDatabase> GetDatabaseAsync(Context context)
        {
            return _databaseFactory.GetDatabaseWithKeyPrefix(context, KeySpace);
        }

        private class RedisRetryPolicy : ITransientErrorDetectionStrategy
        {
            /// <inheritdoc />
            public bool IsTransient(Exception ex)
            {
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
