// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
using StackExchange.Redis;

#nullable enable

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
        SetLocalMachineState,

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
        private readonly RedisDatabaseAdapterConfiguration _configuration;
        private readonly Tracer _tracer = new Tracer(nameof(RedisDatabaseAdapter));

        /// <summary>
        /// The keyspace used to prefix keys in the database
        /// </summary>
        public string KeySpace => _configuration.KeySpace;

        private readonly RedisDatabaseFactory _databaseFactory;
        private readonly IRetryPolicy _redisRetryStrategy;

        // In some cases, Redis.StackExchange library may fail to reset the connection to Azure Redis Cache.
        // To work around this problem we reset the connection multiplexer if all the calls are failing with RedisConnectionException.
        private int _connectionErrorCount;
        private readonly IClock _clock;
        private DateTime _lastRedisReconnectDateTime = DateTime.MinValue;
        private readonly object _resetConnectionsLock = new object();
        private int _reconnectionCount;

        /// <nodoc />
        public RedisDatabaseAdapter(RedisDatabaseFactory databaseFactory, RedisDatabaseAdapterConfiguration configuration, IClock? clock = null)
        {
            _configuration = configuration;
            _redisRetryStrategy = configuration.CreateRetryPolicy(OnRedisException);
            _databaseFactory = databaseFactory;
            _clock = clock ?? SystemClock.Instance;
        }

        /// <nodoc />
        public RedisDatabaseAdapter(RedisDatabaseFactory databaseFactory, string keySpace, IClock? clock = null)
            : this(databaseFactory, new RedisDatabaseAdapterConfiguration(keySpace), clock)
        {
        }

        /// <summary>
        /// Counters for tracking redis-related operations.
        /// </summary>
        public CounterCollection<RedisOperation> Counters { get; } = new CounterCollection<RedisOperation>();

        /// <nodoc />
        public string DatabaseName => _configuration.DatabaseName;

        /// <summary>
        /// Creates a retryable Redis operation.
        /// </summary>
        /// <returns>A batch that can be used to enqueue batch operations.</returns>
        public IRedisBatch CreateBatchOperation(RedisOperation operation)
        {
            return new RedisBatch(operation, KeySpace, _configuration.DatabaseName);
        }

        /// <nodoc />
        public RedisBatch CreateBatch(RedisOperation operation)
        {
            return new RedisBatch(operation, KeySpace, _configuration.DatabaseName);
        }

        /// <summary>
        /// Enumerates all endpoints with a sample keys for each of them.
        /// </summary>
        public List<(string serverId, string serverSampleKey)> GetServerKeys(string? serverId = null)
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
        public async Task<List<(string serverId, RedisInfo info)>> GetInfoAsync(string? serverId = null)
        {
            var servers = GetServers(serverId);

            var tasks = servers.Select(s => (serverId: s.serverId, infoTask: GetRedisInfoAsync(s.server))).ToList();

            await Task.WhenAll(tasks.Select(t => t.infoTask));

            var result = tasks
                .Select(tpl => (serverId: tpl.serverId, info: tpl.infoTask.GetAwaiter().GetResult()))
                .Where(t => t.info != null).ToList();
            return result;
        }

        private List<(IServer server, string serverId)> GetServers(string? serverId = null)
        {
            return _databaseFactory.GetEndPoints()
                .Select(ep => _databaseFactory.GetServer(ep))
                .Where(server => !server.IsSlave)
                .Where(server => serverId == null || GetServerId(server) == serverId)
                .Select(server => (server: server, serverId: GetServerId(server)!))
                .Where(tpl => tpl.serverId != null)
                .ToList();
        }

        private static string? GetServerId(IServer server)
        {
            return server?.ClusterConfiguration?[server.EndPoint]?.NodeId;
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
            var operationContext = new OperationContext(context, cancellationToken);

            using var batchCounter = Counters[RedisOperation.Batch].Start();
            using var operationCounter = Counters[batch.Operation].Start();
            Counters[RedisOperation.BatchSize].Add(batch.BatchSize);

            var result = await PerformRedisOperationAsync(
                operationContext,
                operation: async (nestedContext, database) =>
                           {
                               await batch.ExecuteBatchOperationAndGetCompletion(nestedContext, database, nestedContext.Token);
                               return Unit.Void;
                           },
                onSuccess: () => batch.NotifyConsumersOfSuccess(),
                onFailure: ex => batch.NotifyConsumersOfFailure(ex),
                onCancel: () => batch.NotifyConsumersOfCancellation(),
                extraEndMessage: $"Operation={batch.Operation}, ",
                operationName: batch.Operation.ToString()
            );

            return result.Succeeded ? BoolResult.Success : new BoolResult(result);
        }

        private async Task<Result<TResult>> PerformRedisOperationAsync<TResult>(
            OperationContext context,
            Func<OperationContext, IDatabase, Task<TResult>> operation,
            string operationName,
            Func<Task>? onSuccess = null,
            Action<Exception>? onFailure = null,
            Action? onCancel = null,
            string? extraEndMessage = null)
        {
            // The cancellation logic in this method is quite complicated.
            // We have following "forces" that can cancel the operation:
            // 1. A token provided to this method is triggered.
            //    (if the current operation is no longer needed because we got the result from another redis instance already).
            // 2. Operation exceeds a timeout
            // 3. A multiplexer is closed and we need to retry with a newly created connection multiplexer.

            bool operationIsCanceled = false;

            // Cancellation token can be changed in this method so we need another local to avoid re-assigning an argument.
            CancellationToken token;

            var result = await context.PerformOperationWithTimeoutAsync(
                _tracer,
                async (withTimeoutContext) =>
                {
                    string getCancellationReason(bool multiplexerIsClosed)
                    {
                        bool externalTokenIsCancelled = context.Token.IsCancellationRequested;
                        bool timeoutTokenIsCancelled = withTimeoutContext.Token.IsCancellationRequested;

                        Contract.Assert(externalTokenIsCancelled || timeoutTokenIsCancelled || multiplexerIsClosed);

                        operationIsCanceled = true;

                        // Its possible to have more than one token to be triggered, in this case we'll report based on the check order.
                        // Have to put '!' at the end of each return statement due to this bug: https://github.com/dotnet/roslyn/issues/42396
                        // Should be removed once moved to a newer C# compiler version.
                        if (externalTokenIsCancelled) { return "a given cancellation token is cancelled"!; }

                        if (timeoutTokenIsCancelled) { return $"Operation timed out after {_configuration.OperationTimeout}"!; }

                        if (multiplexerIsClosed) { return "the multiplexer is closed"!; }

                        return "The operation is not cancelled"!;
                    }

                    // Now the token is a combination of "external token" and "timeout token"
                    token = withTimeoutContext.Token;

                    using (Counters[RedisOperation.All].Start())
                    {
                        try
                        {
                            // Need to register the cancellation here and not inside the ExecuteAsync callback,
                            // because the cancellation can happen before the execution of the given callback.
                            // And we still need to cancel the batch operations to finish all the tasks associated with them.
                            using (token.Register(() => { cancelTheBatch(getCancellationReason(multiplexerIsClosed: false)); }))
                            {
                                var r = await _redisRetryStrategy.ExecuteAsync(
                                    withTimeoutContext,
                                    async () =>
                                    {
                                        var (database, databaseClosedCancellationToken) = await GetDatabaseAsync(withTimeoutContext);
                                        CancellationTokenSource? linkedCts = null;
                                        if (_configuration.CancelBatchWhenMultiplexerIsClosed)
                                        {
                                            // The database may be closed during a redis call.
                                            // Linking two tokens together and cancelling the batch if one of the cancellations was requested.

                                            // We want to make sure the following: the task returned by this call and the tasks for each and individual
                                            // operation within a batch are cancelled.
                                            // To do that, we need to "Notify" all the batches about the cancellation inside the Register callback
                                            // and ExecuteBatchOperationAndGetCompletion should respect the cancellation token and throw an exception
                                            // if the token is set.
                                            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(databaseClosedCancellationToken, withTimeoutContext.Token);
                                            linkedCts.Token.Register(
                                                () =>
                                                {
                                                    cancelTheBatch(getCancellationReason(multiplexerIsClosed: databaseClosedCancellationToken.IsCancellationRequested));
                                                });

                                            // Now the token is a combination of "external token", "timeout token" and "database is closed token"
                                            token = linkedCts.Token;

                                            // It is fine that the second cancellation token is not passed to retry strategy.
                                            // Retry strategy only retries on redis exceptions and all the rest, like TaskCanceledException or OperationCanceledException
                                            // are be ignored.
                                        }

                                        // We need to dispose the token source to unlink it from the tokens the source was created from.
                                        // This is important, because the database cancellation token can live a long time
                                        // referencing a lot of token sources created here.
                                        using (linkedCts)
                                        {
                                            return await operation(new OperationContext(withTimeoutContext, token), database);
                                        }
                                    },
                                    token,
                                    databaseName: DatabaseName);

                                if (onSuccess != null)
                                {
                                    await onSuccess();
                                }

                                return new Result<TResult>(r, isNullAllowed: true);
                            }
                        }
                        catch (TaskCanceledException e)
                        {
                            // Don't have to cancel batch here, because we track the cancellation already and call 'cancelBatch' if needed
                            return new Result<TResult>(e) { IsCancelled = true };
                        }
                        catch (OperationCanceledException e)
                        {
                            // The same applies to OperationCanceledException as for TaskCanceledException
                            return new Result<TResult>(e) { IsCancelled = true };
                        }
                        catch (Exception ex)
                        {
                            onFailure?.Invoke(ex);
                            return new Result<TResult>(ex) { IsCancelled = operationIsCanceled };
                        }
                    }
                },
                // Tracing errors all the time. They're not happening too frequently and its useful to know about all of them.
                traceErrorsOnly: true,
                traceOperationStarted: false,
                extraEndMessage: r => $"{extraEndMessage}Database={_configuration.DatabaseName}, ConnectionErrors={_connectionErrorCount}, IsCancelled={operationIsCanceled}",
                timeout: _configuration.OperationTimeout);

            HandleOperationResult(context, result);
            return result;

            void cancelTheBatch(string reason)
            {
                _tracer.Debug(context, $"Cancelling {operationName} against {DatabaseName} because {reason}.");
                onCancel?.Invoke();
            }
        }

        private async Task<T> PerformDatabaseOperationAsync<T>(Context context, Func<IDatabase, Task<T>> operation, Counter stopwatch, CancellationToken token, [CallerMemberName] string? operationName = null)
        {
            var operationContext = new OperationContext(context, token);

            var result = await PerformRedisOperationAsync(
                operationContext,
                operation: (nestedContext, database) => operation(database),
                operationName: operationName!);
            return result.ThrowIfFailure();
        }

        private void HandleOperationResult(Context context, BoolResult result)
        {
            if (result)
            {
                RedisOperationSucceeded(context);
            }
            else if (result.Exception != null)
            {
                HandleRedisExceptionAndResetMultiplexerIfNeeded(context, result.Exception);
            }
        }

        private void RedisOperationSucceeded(Context context)
        {
            // If the operation is successful, then reset the error and reconnection count to 0.
            var previousConnectionErrorCount = Interlocked.Exchange(ref _connectionErrorCount, 0);
            var previousReconnectionCount = Interlocked.Exchange(ref _reconnectionCount, 0);
            if (previousConnectionErrorCount != 0)
            {
                // It means that the service just reconnected to a redis instance.
                _tracer.Info(context, $"Successfully reconnected to {DatabaseName}. Previous ConnectionErrorCount={previousConnectionErrorCount}, previous ReconnectionCount={previousReconnectionCount}");
            }
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
                if (Interlocked.Increment(ref _connectionErrorCount) >= _configuration.RedisConnectionErrorLimit)
                {
                    lock (_resetConnectionsLock)
                    {
                        // The second read of _connectionErrorCount is a non-interlocked read, but it should be fine because it is happening under the lock.
                        var timeSinceLastReconnect = _clock.UtcNow.Subtract(_lastRedisReconnectDateTime);

                        if (_connectionErrorCount >= _configuration.RedisConnectionErrorLimit && timeSinceLastReconnect >= _configuration.MinReconnectInterval)
                        {
                            // This means that there is no successful operations happening, and all the errors that we're seeing are redis connectivity issues.
                            // This is, effectively, a work-around for the issue in StackExchange.Redis library (https://github.com/StackExchange/StackExchange.Redis/issues/559).
                            _tracer.Warning(context, $"Reset redis connection to {DatabaseName} due to connectivity issues. ConnectionErrorCount={_connectionErrorCount}, RedisConnectionErrorLimit={_configuration.RedisConnectionErrorLimit}, ReconnectCount={_reconnectionCount}, LastReconnectDateTimeUtc={_lastRedisReconnectDateTime}.");

                            _databaseFactory.ResetConnectionMultiplexer();

                            Interlocked.Exchange(ref _connectionErrorCount, 0);
                            _lastRedisReconnectDateTime = _clock.UtcNow;
                            Interlocked.Increment(ref _reconnectionCount);

                            // In some cases the service can't connect to redis and the only option is to shut down the service.
                            if (_reconnectionCount >= _configuration.RedisReconnectionLimitBeforeServiceRestart)
                            {
                                LifetimeManager.RequestTeardown(context, $"Requesting teardown because redis reconnection limit of {_configuration.RedisReconnectionLimitBeforeServiceRestart} is reached for {DatabaseName}.");
                            }
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
            return exception is RedisConnectionException;
        }

        /// <summary>
        /// Returns true if a given exception of type <see cref="RedisCommandException"/> and the error is transient.
        /// </summary>
        public static bool IsTransientRedisCommandException(Exception exception)
        {
            if (exception is RedisCommandException rce && rce.Message.Contains("Command cannot be issued to a slave"))
            {
                return true;
            }

            // Other RedisCommandException may indicate the issues in the code.

            return false;
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

        private Task<(IDatabase database, CancellationToken databaseLifetimeToken)> GetDatabaseAsync(Context context)
        {
            return _databaseFactory.GetDatabaseWithKeyPrefix(context, KeySpace);
        }

        internal class RedisRetryPolicy
        {
            private readonly Action<Exception>? _exceptionObserver;
            private readonly bool _treatObjectDisposedExceptionAsTransient;

            /// <nodoc />
            public RedisRetryPolicy(Action<Exception>? exceptionObserver, bool treatObjectDisposedExceptionAsTransient)
                => (_exceptionObserver, _treatObjectDisposedExceptionAsTransient) = (exceptionObserver, treatObjectDisposedExceptionAsTransient);

            /// <inheritdoc />
            public bool IsTransient(Exception ex)
            {
                _exceptionObserver?.Invoke(ex);

                // naively retry all redis server exceptions.

                if (ex is RedisException redisException)
                {
                    // UnableToConnect may or may not be caused by the instance not existing any more. It can also be
                    // caused by transient connectivity issues (for example, due to Redis failover operations), in
                    // which case we want to keep retrying.

                    // If the error contains the following text, then the error is not transient.
                    return !(redisException.ToString().Contains("Error compiling script") || redisException.ToString().Contains("Error running script"));
                }

                // Handle RedisTimeoutException separately because it doesn't derive from RedisException.
                if (ex is RedisTimeoutException)
                {
                    return true;
                }

                if (IsTransientRedisCommandException(ex))
                {
                    return true;
                }

                if (ex is ObjectDisposedException && _treatObjectDisposedExceptionAsTransient)
                {
                    // The multiplexer can be closed during the call causing the call to fail with ObjectDisposeException.
                    // This is a transient issue, because the new multiplexer is created and the next call
                    // may succeed.
                    return true;
                }

                return false;
            }
        }
    }
}
