// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Utilities.Tasks;
#if MICROSOFT_INTERNAL
using Microsoft.Caching.Redis;
#else
using StackExchange.Redis;
#endif

namespace BuildXL.Cache.ContentStore.Distributed.Redis
{
    /// <inheritdoc />
    internal class RedisBatch : IRedisBatch
    {
        /// <summary>
        /// The Epoch for Unix time.
        /// </summary>
        internal static readonly DateTime UnixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        internal const string EpochStartCursorFieldName = "EpochStartCursor";

        /// <summary>
        /// -- ({ { key, value, lastAccessTime }[], deletedKeys, actualDeletedKeysCount }) GetOrClean(string[] keys, long maximumEmptyLastAccessTime, bool whatif)
        /// </summary>
        private static readonly string GetOrClean = GetEmbeddedResourceFile("BuildXL.Cache.ContentStore.Distributed.Redis.Scripts.GetOrClean.lua");

        /// <summary>
        /// -- ({ nextCursor, key[] }) Scan(string cursor, int entryCount)
        /// </summary>
        private static readonly string Scan = GetEmbeddedResourceFile("BuildXL.Cache.ContentStore.Distributed.Redis.Scripts.Scan.lua");

        /// <summary>
        /// -- bool CompareExchange(string: weakFingerprintKey, byte[]: selectorFieldName, byte[] tokenFieldName, string expectedToken, byte[] contentHashList, string newReplacementToken)
        /// </summary>
        private static readonly string CompareExchange = GetEmbeddedResourceFile("BuildXL.Cache.ContentStore.Distributed.Redis.Scripts.CompareExchange.lua");

        /// <summary>
        /// An individual redis operation in a batch and the associated result.
        /// </summary>
        private interface IRedisOperationAndResult
        {
            /// <summary>
            /// Adds a task batch to a redis batch for execution.
            /// </summary>
            Task AddTaskToBatch(IBatch batch);

            /// <summary>
            /// Updates the final result from the redis batch onto the consumers' task.
            /// </summary>
            Task SetFinalResult();

            /// <summary>
            /// Updates the final exception from the redis batch to the consumers' task.
            /// </summary>
            void SetFailure(Exception exception);

            /// <summary>
            /// Updates the final result from the redis batch on the consumers' task.
            /// </summary>
            void SetCancelled();
        }

        /// <inheritdoc />
        private class RedisOperationAndResult<T> : IRedisOperationAndResult
        {
            private readonly Func<IBatch, Task<T>> _redisBatchFunction;

            private Task<T> BatchExecutionTask { get; set; }

            public readonly TaskSourceSlim<T> FinalTaskResult = TaskSourceSlim.Create<T>();

            public RedisOperationAndResult(Func<IBatch, Task<T>> batchFunction)
            {
                _redisBatchFunction = batchFunction;
            }

            public Task AddTaskToBatch(IBatch batch)
            {
                BatchExecutionTask = _redisBatchFunction(batch);
                return BatchExecutionTask;
            }

            public async Task SetFinalResult()
            {
                var taskResult = await BatchExecutionTask;
                // "Yielding" execution to another thread to avoid deadlocks.
                // SetResult causes a task's continuation to run in the same thread that can cause issues
                // because a continuation of BatchExecutionTask could be a synchronous continuation as well.
                await Task.Yield();
                FinalTaskResult.TrySetResult(taskResult);
            }

            public void SetCancelled()
            {
                FinalTaskResult.TrySetCanceled();
            }

            public void SetFailure(Exception exception)
            {
                FinalTaskResult.TrySetException(exception);
            }
        }

        private readonly List<IRedisOperationAndResult> _redisOperations = new List<IRedisOperationAndResult>();

        public string DatabaseName { get; }

        /// <nodoc />
        public RedisBatch(RedisOperation operation, string keySpace, string databaseName) => (Operation, KeySpace, DatabaseName) = (operation, keySpace, databaseName);

        /// <inheritdoc />
        public RedisOperation Operation { get; }

        /// <nodoc />
        public string KeySpace { get; }

        /// <inheritdoc />
        public int BatchSize => _redisOperations.Count;

        /// <nodoc />
        public Task<T> AddOperation<T>(string key, Func<IBatch, Task<T>> operation)
        {
            var redisOperation = new RedisOperationAndResult<T>(batch => operation(batch));
            _redisOperations.Add(redisOperation);
            return redisOperation.FinalTaskResult.Task;
        }

        /// <nodoc />
        public void AddOperationAndTraceIfFailure<T>(Context context, string key, Func<IBatch, Task<T>> operation, [CallerMemberName]string operationName = null)
        {
            // Trace failure using 'Debug' severity to avoid pollution of warning traces.
            AddOperation(key, operation).FireAndForget(context, batch: this, operationName);
        }

        /// -- bool CompareExchange(string: weakFingerprintKey, byte[]: selectorFieldName, byte[] tokenFieldName, string expectedToken, byte[] contentHashList)
        /// <inheritdoc />
        public async Task<bool> CompareExchangeAsync(string weakFingerprintKey, RedisValue selectorFieldName, RedisValue tokenFieldName, string expectedToken, RedisValue contentHashList, string newReplacementToken)
        {
            var redisOperation =
                new RedisOperationAndResult<RedisResult>(
                    batch =>
                        batch.ScriptEvaluateAsync(
                            CompareExchange,
                            new RedisKey[] { weakFingerprintKey },
                            new RedisValue[]
                            {
                                selectorFieldName,
                                tokenFieldName,
                                expectedToken,
                                contentHashList,
                                newReplacementToken
                            }));
            _redisOperations.Add(redisOperation);
            bool result = (bool)await redisOperation.FinalTaskResult.Task;
            return result;
        }

        /// <inheritdoc />
        public Task<RedisValue> StringSetRangeAsync(string key, long offset, RedisValue value)
        {
            var redisOperation = new RedisOperationAndResult<RedisValue>(batch => batch.StringSetRangeAsync(key, offset, value));
            _redisOperations.Add(redisOperation);
            return redisOperation.FinalTaskResult.Task;
        }

        /// <inheritdoc />
        public Task<bool> StringSetAsync(RedisKey key, RedisValue value, TimeSpan expiry, When when = When.Always)
        {
            var redisOperation = new RedisOperationAndResult<bool>(batch => batch.StringSetAsync(key, value, expiry, when));
            _redisOperations.Add(redisOperation);
            return redisOperation.FinalTaskResult.Task;
        }

        /// <inheritdoc />
        public Task<long> StringIncrementAsync(RedisKey key, long byValue)
        {
            var redisOperation = new RedisOperationAndResult<long>(batch => batch.StringIncrementAsync(key, byValue));
            _redisOperations.Add(redisOperation);
            return redisOperation.FinalTaskResult.Task;
        }

        /// <inheritdoc />
        public Task<bool> KeyExpireAsync(string key, DateTime newExpiryTimeUtc)
        {
            var redisOperation = new RedisOperationAndResult<bool>(batch => batch.KeyExpireAsync(key, newExpiryTimeUtc));
            _redisOperations.Add(redisOperation);
            return redisOperation.FinalTaskResult.Task;
        }

        /// <inheritdoc />
        public Task<bool> KeyExpireAsync(string key, TimeSpan timeToLive)
        {
            var redisOperation = new RedisOperationAndResult<bool>(batch => batch.KeyExpireAsync(key, timeToLive));
            _redisOperations.Add(redisOperation);
            return redisOperation.FinalTaskResult.Task;
        }

        /// <inheritdoc />
        public Task<RedisValue> StringGetAsync(string key, CommandFlags commandFlags = CommandFlags.None)
        {
            var redisOperation = new RedisOperationAndResult<RedisValue>(batch => batch.StringGetAsync(key, commandFlags));
            _redisOperations.Add(redisOperation);
            return redisOperation.FinalTaskResult.Task;
        }

        /// <inheritdoc />
        public Task<HashEntry[]> HashGetAllAsync(string key, CommandFlags commandFlags = CommandFlags.None)
        {
            var redisOperation = new RedisOperationAndResult<HashEntry[]>(batch => batch.HashGetAllAsync(key, commandFlags));
            _redisOperations.Add(redisOperation);
            return redisOperation.FinalTaskResult.Task;
        }

        /// <inheritdoc />
        public async Task<ScanResult> ScanAsync(RedisKey shardKey, RedisValue cursor, int entryCount)
        {
            var redisOperation =
                new RedisOperationAndResult<RedisResult>(
                    batch =>
                        batch.ScriptEvaluateAsync(
                            Scan,
                            new RedisKey[] { RemoveKeySpacePrefix(shardKey) },
                            new RedisValue[] { cursor, entryCount},
                            flags: CommandFlags.DemandMaster));
            _redisOperations.Add(redisOperation);
            var result = await redisOperation.FinalTaskResult.Task;

            var arrayResult = (RedisResult[])result;
            var nextCursor = (long)arrayResult[0];
            var keys = (string[])(arrayResult[1]);

            return new ScanResult(nextCursor, keys);
        }

        /// <inheritdoc />
        public async Task<GetOrCleanResult> GetOrCleanAsync(
            RedisKey shardKey,
            string[] keys,
            TimeSpan maximumEmptyLastAccessTime,
            bool whatIf)
        {
            var redisOperation =
                new RedisOperationAndResult<RedisResult>(
                    batch =>
                        batch.ScriptEvaluateAsync(
                            GetOrClean,
                            new RedisKey[] { RemoveKeySpacePrefix(shardKey) },
                            new RedisValue[] { (int)maximumEmptyLastAccessTime.TotalSeconds, whatIf }.Concat(keys.Select(k => (RedisValue)k)).ToArray(),
                            flags: CommandFlags.DemandMaster));
            _redisOperations.Add(redisOperation);
            var result = await redisOperation.FinalTaskResult.Task;

            var arrayResult = (RedisResult[])result;
            var rawEntries = (RedisResult[])(arrayResult[0]);
            var deletedKeys = ((string[])arrayResult[1]).Select(key => RemoveKeySpacePrefix(key)).ToArray();
            var actualDeletedKeysCount = (int)arrayResult[2];

            (string key, RedisValue value)[] entries = rawEntries.Select(
                e =>
                {
                    var array = (RedisResult[])e;
                    var key = RemoveKeySpacePrefix((string)array[0]);
                    return (key: key, value: (RedisValue)array[1]);
                }).ToArray();

            return new GetOrCleanResult(entries, deletedKeys, actualDeletedKeysCount);
        }

        private string RemoveKeySpacePrefix(string originalKey)
        {
            return originalKey.StartsWith(KeySpace) ? originalKey.Substring(KeySpace.Length) : originalKey;
        }
        
        /// <inheritdoc />
        public Task<bool> KeyDeleteAsync(string key)
        {
            var redisOperation = new RedisOperationAndResult<bool>(batch => batch.KeyDeleteAsync(key));
            _redisOperations.Add(redisOperation);
            return redisOperation.FinalTaskResult.Task;
        }
        
        /// <inheritdoc />
        public Task<long> SetAddAsync(string key, RedisValue[] values)
        {
            var redisOperation = new RedisOperationAndResult<long>(batch => batch.SetAddAsync(key, values));
            _redisOperations.Add(redisOperation);
            return redisOperation.FinalTaskResult.Task;
        }

        /// <inheritdoc />
        public Task<RedisValue[]> SetMembersAsync(string key)
        {
            var redisOperation = new RedisOperationAndResult<RedisValue[]>(batch => batch.SetMembersAsync(key));
            _redisOperations.Add(redisOperation);
            return redisOperation.FinalTaskResult.Task;
        }

        private long GetUnixTimeSecondsFromDateTime(DateTime preciseDateTime)
        {
            if (preciseDateTime == DateTime.MinValue)
            {
                return 0;
            }

            if (preciseDateTime.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentException("preciseDateTime must be UTC");
            }

            return (long)(preciseDateTime - UnixEpoch).TotalSeconds;
        }

        private DateTime GetDateTimeFromUnixTimeSeconds(long unixTimeSeconds)
        {
            if (unixTimeSeconds <= 0)
            {
                return DateTime.MinValue;
            }

            return UnixEpoch.AddSeconds(unixTimeSeconds);
        }

        /// <inheritdoc />
        public async Task ExecuteBatchOperationAndGetCompletion(Context context, IDatabase database, CancellationToken token)
        {
            if (_redisOperations.Count == 0)
            {
                return;
            }
            
            IBatch batch = database.CreateBatch();
            var taskToTrack = new List<Task>(_redisOperations.Count);
            foreach (IRedisOperationAndResult operation in _redisOperations)
            {
                var task = operation.AddTaskToBatch(batch);
                // FireAndForget not inlined because we don't want to replace parent task with continuation. Failure
                // severity is Unknown to stop the logging from happening. We still need to do FireAndForget in order
                // to avoid any ThrowOnUnobservedTaskException triggers.

                // The tracing is effectively disabled, because the failure is observed by the caller of this method.
                task.FireAndForget(context, failureSeverity: Severity.Diagnostic);
                taskToTrack.Add(task);
            }

            batch.Execute();

            // The following call with throw an exception if token triggers before the completion of the tasks.
            await TaskUtilities.WhenAllWithCancellationAsync(taskToTrack, token);
        }

        /// <inheritdoc />
        public Task NotifyConsumersOfSuccess()
        {
            return Task.WhenAll(_redisOperations.Select(operation => operation.SetFinalResult()));
        }

        /// <inheritdoc />
        public void NotifyConsumersOfFailure(Exception exception)
        {
            foreach (IRedisOperationAndResult operation in _redisOperations)
            {
                operation.SetFailure(exception);
            }
        }

        /// <inheritdoc />
        public void NotifyConsumersOfCancellation()
        {
            foreach (IRedisOperationAndResult operation in _redisOperations)
            {
                operation.SetCancelled();
            }
        }

        /// <summary>
        /// Helper to get the string content of a resource file from the current assembly.
        /// </summary>
        /// <remarks>This unfortunately cannot be in a shared location like 'AssemblyHelpers' because on .Net Core it ignores the assembly and always tries to extract the resources from the running assembly. Even though GetManifestResourceNames() does respect it.</remarks>
        public static string GetEmbeddedResourceFile(string resourceKey)
        {
            var callingAssembly = typeof(RedisBatch).GetTypeInfo().Assembly;
            var stream = callingAssembly.GetManifestResourceStream(resourceKey);
            Contract.Check(stream != null)?.Assert($"Expected embedded resource key '{resourceKey}' not found in assembly {callingAssembly.FullName}. Valid resource names are: {string.Join(",", callingAssembly.GetManifestResourceNames())}");

            using (var sr = new StreamReader(stream))
            {
                return sr.ReadToEnd();
            }
        }
    }

    /// <nodoc />
    internal static class RedisBatchTaskExtensions
    {
        public static void FireAndForget(this Task task, Context context, IRedisBatch batch, [CallerMemberName]string operation = null)
        {
            string extraMessage = string.IsNullOrEmpty(batch.DatabaseName) ? string.Empty : $"Database={batch.DatabaseName}";
            task.FireAndForget(context, operation, failureSeverity: Severity.Diagnostic, extraMessage: extraMessage);
        }
    }
}
