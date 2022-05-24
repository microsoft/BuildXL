// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
#if MICROSOFT_INTERNAL
using Microsoft.Caching.Redis;
#else
using StackExchange.Redis;
#endif

namespace BuildXL.Cache.ContentStore.Distributed.Redis
{
    /// <summary>
    /// The result of <see cref="IRedisBatch.ScanAsync"/> operation.
    /// </summary>
    internal class ScanResult
    {
        /// <summary>
        /// New cursor that can be used to resume scanning operation.
        /// </summary>
        public long Cursor { get; }

        /// <summary>
        /// Resulting keys.
        /// </summary>
        public string[] Keys { get; }

        /// <inheritdoc />
        public ScanResult(long cursor, string[] keys)
        {
            Contract.Requires(keys != null);
            Cursor = cursor;
            Keys = keys;
        }
    }

    /// <summary>
    /// The result of <see cref="IRedisBatch.GetOrCleanAsync"/> operation.
    /// </summary>
    internal class GetOrCleanResult
    {
        /// <summary>
        /// Key-value entries retrieved from redis.
        /// </summary>
        public (string key, RedisValue value)[] Entries { get; }

        /// <summary>
        /// Keys that were deleted by the operation.
        /// </summary>
        public string[] DeletedKeys { get; }

        /// <summary>
        /// Number of actually deleted keys.
        /// </summary>
        /// <remarks>
        /// This value is equals to <see cref="DeletedKeys"/>.Length when the whatIf is false.
        /// </remarks>
        public int ActualDeletedKeysCount { get; }

        /// <nodoc />
        public GetOrCleanResult((string key, RedisValue value)[] entries, string[] deletedKeys, int actualDeletedKeysCount)
        {
            Entries = entries;
            DeletedKeys = deletedKeys;
            ActualDeletedKeysCount = actualDeletedKeysCount;
        }
    }

    /// <summary>
    /// A batch operation for Redis
    /// </summary>
    internal interface IRedisBatch
    {
        /// <summary>
        /// Operation enumeration for performance tracking.
        /// </summary>
        RedisOperation Operation { get; }

        /// <summary>
        /// Gets a database name (the primary or the secondary) that performs the operations.
        /// </summary>
        string DatabaseName { get; }

        /// <summary>
        /// The number of operations in a batch.
        /// </summary>
        int BatchSize { get; }

        /// <summary>
        /// Sets range of bits specified at an bit offset for a specific key.
        /// </summary>
        Task<RedisValue> StringSetRangeAsync(string key, long offset, RedisValue value);

        /// <summary>
        /// Sets a specific key with the specified expiry time.
        /// </summary>
        Task<bool> StringSetAsync(RedisKey key, RedisValue value, TimeSpan expiry, When when = When.Always);


        /// <summary>
        /// Increments a specific key by the specified value. Returns the value after being incremented.
        /// </summary>
        Task<long> StringIncrementAsync(RedisKey key, long byValue);

        /// <summary>
        /// Updates the expiration time for a particular key in Redis.
        /// </summary>
        Task<bool> KeyExpireAsync(string key, DateTime newExpiryTimeUtc);

        /// <summary>
        /// Updates the time to live for a particular key in Redis.
        /// </summary>
        Task<bool> KeyExpireAsync(string key, TimeSpan timeToLive);

        /// <summary>
        /// Gets a string value at a specified key.
        /// </summary>
        Task<RedisValue> StringGetAsync(string key, CommandFlags commandFlags = CommandFlags.None);

        /// <summary>
        /// Iterates the set of keys in the database selected by <paramref name="shardKey"/>.
        /// </summary>
        Task<ScanResult> ScanAsync(RedisKey shardKey, RedisValue cursor, int entryCount);

        /// <summary>
        /// Gets values for given <paramref name="keys"/> and removes entries that have an empty machine id set.
        /// </summary>
        Task<GetOrCleanResult> GetOrCleanAsync(RedisKey shardKey, string[] keys, TimeSpan maximumEmptyLastAccessTime, bool whatIf);

        /// <summary>
        /// Deletes given key
        /// </summary>
        Task<bool> KeyDeleteAsync(string key);

        /// <summary>
        /// Insert the specified values as a set.
        /// </summary>
        Task<long> SetAddAsync(string key, RedisValue[] value);

        /// <summary>
        /// Returns the elements in the specified set for given key.
        /// </summary>
        Task<RedisValue[]> SetMembersAsync(string key);

        /// <summary>
        /// Compare exchange for metadata.
        /// </summary>
        Task<bool> CompareExchangeAsync(string weakFingerprintKey, RedisValue selectorFieldName, RedisValue tokenFieldName, string expectedToken, RedisValue contentHashList, string newReplacementToken);

        /// <summary>
        /// Executes the currently created batch operation and returns a task that completes when the batch is done.
        /// </summary>
        Task ExecuteBatchOperationAndGetCompletion(Context context, IDatabase database, CancellationToken token = default);

        /// <summary>
        /// Notifies any consumer of tasks in the batch that the results are available successfully.
        /// </summary>
        Task NotifyConsumersOfSuccess();

        /// <summary>
        /// Notifies any consumer of tasks in the batch failed with a particular exception.
        /// </summary>
        void NotifyConsumersOfFailure(Exception exception);

        /// <summary>
        /// Notifies any consumer of tasks in the batch should be cancelled.
        /// </summary>
        void NotifyConsumersOfCancellation();
    }
}
