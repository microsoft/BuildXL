// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using StackExchange.Redis;

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
        /// The number of operations in a batch.
        /// </summary>
        int BatchSize { get; }

        /// <summary>
        /// Sets a bit specified at an bit offset for a specific key.
        /// </summary>
        Task StringSetBitAsync(string key, long offset, bool bitValue);

        /// <summary>
        /// Sets a bit specified at an bit offset for a specific key.
        /// Only sets the key to the value if the key exists.
        /// Removes key from Redis if bitmask is empty.
        /// </summary>
        /// <returns>
        /// -1 if the key doesn't exist. 0 otherwise.
        /// </returns>
        Task<RedisResult> SetBitIfExistAndRemoveIfEmptyBitMaskAsync(string key, long offset, bool bitValue);

        /// <summary>
        /// Increments the value at the specified key (zero if not present) and updates the time to live if the value
        /// is less than or equal to <paramref name="threshold" />.
        /// </summary>
        /// <returns>
        /// The amount which the value was changed, or zero if value was not changed
        /// </returns>
        Task<RedisIncrementResult> TryStringIncrementBumpExpiryIfBelowOrEqualValueAsync(string key, uint threshold, TimeSpan timeToLive, long requestedIncrement = 1);

        /// <summary>
        /// Sets range specified at an bit offset for a specific key.
        /// Once range is set, updates the expiry to at least the value specified
        /// </summary>
        Task<RedisResult> StringSetRangeAndBumpExpiryAsync(string key, long rangeOffset, byte[] range, DateTime newExpiryTimeUtc, DateTime currentTimeUtc);

        /// <summary>
        /// Get value associated with key and update expiry to at least the value specified.
        /// </summary>
        Task<RedisResult> StringGetAndUpdateExpiryAsync(string key, DateTime newExpiryTimeUtc, DateTime currentTime);

        /// <summary>
        /// Set expiry to at least the value specified.
        /// </summary>
        Task<RedisResult> SetExpiryAsync(string key, DateTime keepAliveTime, DateTime currentTime);

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
        /// Gets the expiration time for a specific key in Redis.
        /// </summary>
        Task<TimeSpan?> KeyTimeToLiveAsync(string key);

        /// <summary>
        /// Deletes given key
        /// </summary>
        Task<bool> KeyDeleteAsync(string key);

        /// <summary>
        /// Insert the specified value into a set.
        /// </summary>
        Task<bool> SetAddAsync(string key, RedisValue value);

        /// <summary>
        /// Insert the specified values as a set.
        /// </summary>
        Task<long> SetAddAsync(string key, RedisValue[] value);

        /// <summary>
        /// Returns the elements in the specified set for given key.
        /// </summary>
        Task<RedisValue[]> SetMembersAsync(string key);

        /// <summary>
        /// Removes value from set stored at key.
        /// </summary>
        Task<bool> SetRemoveAsync(RedisKey key, RedisValue value);

        /// <summary>
        /// Removes value from set stored at key.
        /// </summary>
        Task<long> SetRemoveAsync(RedisKey key, RedisValue[] values);

        /// <summary>
        /// Gets length of set stored at key.
        /// </summary>
        Task<long> SetLengthAsync(RedisKey key, CommandFlags commandFlags = CommandFlags.None);

        /// <summary>
        /// Compare exchange for metadata.
        /// </summary>
        Task<bool> CompareExchangeAsync(string weakFingerprintKey, RedisValue selectorFieldName, RedisValue tokenFieldName, string expectedToken, RedisValue contentHashList, string newReplacementToken);

        /// <summary>
        /// Unset the machineId bit if local and remote last-access times are in sync.
        /// </summary>
        /// <returns>-1 if key doesn't exist in content tracker or if the local and remote last-access times match. Otherwise, it returns the distributed last-access time.</returns>
        Task<RedisLastAccessTimeResult> TryTrimWithLastAccessTimeCheckAsync(
            string key,
            DateTime currentTime,
            DateTime localAccessTime,
            TimeSpan contentHashBumpTime,
            TimeSpan targetRange,
            long machineId,
            int minReplicaCountForSafeEviction,
            int minReplicaCountForImmediateEviction);

        /// <summary>
        /// Updates the expiry for the provided key if it exists.
        /// If it doesn't exist, location record is created with provided expiry.
        /// </summary>
        Task<RedisResult> TouchOrSetLocationRecordAsync(string key, byte[] sizeInBytes, long machineId, DateTime keepAliveTime, DateTime currentTime);

        /// <summary>
        /// Adds a checkpoint to the given <paramref name="checkpointsKey"/> with the associated data and returns the slot number where the checkpoint was stored.
        /// </summary>
        Task<int> AddCheckpointAsync(string checkpointsKey, RedisCheckpointInfo checkpointInfo, int maxSlotCount);

        /// <summary>
        /// Attempts to acquire a master role for a machine
        /// </summary>
        Task<RedisAcquireMasterRoleResult?> AcquireMasterRoleAsync(
            string masterRoleRegistryKey,
            string machineName,
            DateTime currentTime,
            TimeSpan leaseExpiryTime,
            int masterCount,
            bool release);

        /// <summary>
        /// Gets all checkpoints given the key of the checkpoints hash map in redis.
        /// </summary>
        Task<(RedisCheckpointInfo[] checkpoints, DateTime epochStartCursor)> GetCheckpointsInfoAsync(string checkpointsKey, DateTime currentTime);

        /// <summary>
        /// Executes the currently created batch operation and returns a task that completes when the batch is done.
        /// </summary>
        Task ExecuteBatchOperationAndGetCompletion(Context context, IDatabase database);

        /// <summary>
        /// Notifies any consumer of tasks in the batch that the results are available successfully.
        /// </summary>
        Task NotifyConsumersOfSuccess();

        /// <summary>
        /// Notifies any consumer of tasks in the batch failed with a particular exception.
        /// </summary>
        void NotifyConsumersOfFailure(Exception exception);
    }
}
