// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tasks;
using StackExchange.Redis;

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
        /// SetRangeBumpExpiryScript(string key, long sizeOffset, byte[] size, long keepAliveTime, long currentTime)
        /// </summary>
        private const string SetRangeBumpExpiryScript = @"
redis.call(""SETRANGE"", KEYS[1], ARGV[1], ARGV[2]);
local t = tonumber(ARGV[4]);
local currentExpiry = redis.call(""TTL"", KEYS[1]);
local currentExpiryTime = t + currentExpiry;
if (currentExpiry < 0 or currentExpiryTime < tonumber(ARGV[3])) then
    redis.call(""EXPIREAT"", KEYS[1], ARGV[3]);
end";

        /// <summary>
        /// GetAndUpdateExpiry(string key, long keepAliveTime, long currentTime)
        /// </summary>
        private const string GetAndUpdateExpiry = @"
local getValue = redis.call(""GET"", KEYS[1]);
if (getValue ~= nil) then 
    local t = tonumber(ARGV[2]);
    local currentExpiry = redis.call(""TTL"", KEYS[1]);
    local currentExpiryTime = t + currentExpiry;
    if (currentExpiry < 0 or currentExpiryTime < tonumber(ARGV[1])) then
         redis.call(""EXPIREAT"", KEYS[1], ARGV[1]);
    end
end
return getValue;
";

        /// <summary>
        /// SetExpiry(string key, long keepAliveTime, long currentTime)
        /// </summary>
        private const string SetExpiryScript = @"
local currentExpiry = redis.call(""TTL"", KEYS[1]);
local t = tonumber(ARGV[2]);
local currentExpiryTime = t + currentExpiry;
if (currentExpiry < 0 or currentExpiryTime < tonumber(ARGV[1])) then
    return redis.call(""EXPIREAT"", KEYS[1], ARGV[1]);
end
return 0;
";

        /// <summary>
        /// TouchOrSetLocationRecordsScript(string key, byte[] size, long machineIdWithSizeOffset, long keepAliveTime, long currentTime)
        /// Returns 0 if the key exists, 1 if the key was added.
        /// </summary>
        internal static readonly string TouchOrSetLocationRecordScript = @"
local size = ARGV[1];
local machineIdWithSizeOffset = ARGV[2];
local keepAliveTime = ARGV[3];
local currentTime = ARGV[4];

-- Get current expiry. TTL call returns -2 if key doesn't exist and -1 if expiry is unset
local currentExpiry = redis.call(""TTL"", KEYS[1]);

-- Calculate suggested expiry
local t = tonumber(currentTime);
local currentExpiryTime = t + currentExpiry;

-- If key doesn't exist, re-add the content location record.
-- Location record follows this structure: [size][location bitmask]
if (currentExpiry == -2) then
    redis.call(""SETRANGE"", KEYS[1], 0, size);
    redis.call(""SETBIT"", KEYS[1], machineIdWithSizeOffset, 1);
    redis.call(""EXPIREAT"", KEYS[1], keepAliveTime);
    return 1;
end

-- If current expiry is either unset or older than suggested expiry, update it
if (currentExpiry == -1 or currentExpiryTime < tonumber(keepAliveTime)) then
    redis.call(""EXPIREAT"", KEYS[1], keepAliveTime);
end

return 0;
";

        /// <summary>
        /// Returns an array with content's eviction information: { safeToEvict, remoteLastAccessTime, replicaCount }.
        /// safeToEvict is -1 when true, 1 when content does not fulfill requirements (matching last-access time and replica count if flag is set).
        /// </summary>
        private static readonly string TryTrimWithLastAccessTimeCheck = @"
local redisExpiry = redis.call(""TTL"", KEYS[1]);
if (redisExpiry > 0) then
    local currentTime = tonumber(ARGV[1]);
    local localAccessTime = tonumber(ARGV[2]);
    local contentHashBumpTime = tonumber(ARGV[3]);
    local targetRange = tonumber(ARGV[4]);
    local machineId = tonumber(ARGV[5]);
    local minReplicaCountToSafeEvict = tonumber(ARGV[6]);
    local minReplicaCountToImmediateEvict = tonumber(ARGV[7]);

    local redisAccessTime = currentTime + redisExpiry - contentHashBumpTime;
    local locations = redis.call(""BITCOUNT"", KEYS[1], 8, -1);

    -- If content is untracked, able to evict immediately
    if (locations == 0) then
        redis.call(""DEL"", KEYS[1]);
        return { -1, redisAccessTime, locations };
    end

    if (locations >= minReplicaCountToImmediateEvict) then
        redis.call(""SETBIT"", KEYS[1], machineId, 0);
        return { -1, redisAccessTime, locations };
    end

    local lowerBound = redisAccessTime - targetRange;

    -- If touched in the datacenter after local last-access time, unable to safely evict.
    if (localAccessTime <= lowerBound) then
        return { 1, redisAccessTime, locations };
    end

    -- If minReplicaCount is set, unable to safely evict if content isn't sufficiently replicated.
    -- minReplicaCount is unset after the first pass, implying that content is aging out.
    if (minReplicaCountToSafeEvict > 0) then
        if (locations < minReplicaCountToSafeEvict) then
            return { 1, redisAccessTime, locations };
        end
    end

    -- If the last replica available is being considered for eviction, safe to remove the key.
    if (locations == 1) then
        redis.call(""DEL"", KEYS[1]);
    end

    -- Content is safe to evict and is unregistered.
    redis.call(""SETBIT"", KEYS[1], machineId, 0);
    return { -1, redisAccessTime, locations };
end
return { -1, -1, -1 };
";

        /// <summary>
        /// SetBitIfExistAndRemoveIfNoLocationsLuaScript(string key, bool setBit, long offset)
        /// If the key exists then the value will be set. If setting bit results in empty bitmask, key is removed.
        /// </summary>
        private static readonly string SetBitIfExistAndRemoveIfEmptyBitMaskLuaScript = @"
if (redis.call(""EXISTS"", KEYS[1]) == 1) then
    local setResult = redis.call(""SETBIT"", KEYS[1], ARGV[1], ARGV[2]);
    if (ARGV[2] ~= true) then
        -- Check set bits starting from index 8 (file size is 8 bytes, taking up indexes 0-7) to the end of bitmask
        local locations = redis.call(""BITCOUNT"", KEYS[1], 8, -1);
        if (locations == 0) then
            redis.call(""DEL"", KEYS[1]);
        end
    end
    return setResult;
else
    return -1;
end";

        /// <summary>
        /// IncrementBumpExpiryIfBelowOrEqualValue(string key, long comparisonValue, TimeSpan timeToLiveSeconds, long requestedIncrement)
        /// </summary>
        private static readonly string IncrementBumpExpiryIfBelowOrEqualValue = @"
local requestedIncrement = tonumber(ARGV[3]);
local comparisonValue = tonumber(ARGV[1]);
local retrievedValue = redis.call(""GET"", KEYS[1]);
local currentValue = 0;
local priorValue = 0;
if (tonumber(retrievedValue) ~= nil) then
    priorValue = tonumber(retrievedValue);
end

-- Constrain requestedIncrement to only modify value to be between range [0, comparisonValue]
if (requestedIncrement >= 0) then
    requestedIncrement = math.max(0, math.min(comparisonValue - priorValue, requestedIncrement));
else
    requestedIncrement = math.max(-priorValue, requestedIncrement);
end

if (requestedIncrement ~= 0) then
    currentValue = redis.call(""INCRBY"", KEYS[1], requestedIncrement);
    if (priorValue == 0) then
        redis.call(""EXPIRE"", KEYS[1], ARGV[2]);
    end
else
    currentValue = priorValue;
end

return { requestedIncrement, currentValue }";

        /// <summary>
        /// (int slotNumber) AddCheckpoint(string checkpointsKey, string checkpointId, long sequenceNumber, long checkpointCreationTime, string machineName, int maxSlotCount)
        /// </summary>
        private static readonly string AddCheckpoint = GetEmbeddedResourceFile("BuildXL.Cache.ContentStore.Distributed.Redis.Scripts.AddCheckpoint.lua");

        /// <summary>
        /// (int: machineId, string: replacedMachineName) AcquireSlot(string slotsKey, string machineName, long currentTime, long machineExpiryTime, int slotCount)
        /// </summary>
        private static readonly string AcquireSlot = GetEmbeddedResourceFile("BuildXL.Cache.ContentStore.Distributed.Redis.Scripts.AcquireSlot.lua");

        /// <summary>
        /// -- ({ { key, value, lastAccessTime }[], deletedKeys, actualDeletedKeysCount }) GetOrClean(string[] keys, long maximumEmptyLastAccessTime, bool whatif)
        /// </summary>
        private static readonly string GetOrClean = GetEmbeddedResourceFile("BuildXL.Cache.ContentStore.Distributed.Redis.Scripts.GetOrClean.lua");

        /// <summary>
        /// -- (int: machineId, bool: isAdded) GetOrAddMachine(string clusterStateKey, string machineLocation)
        /// </summary>
        private static readonly string GetOrAddMachine = GetEmbeddedResourceFile("BuildXL.Cache.ContentStore.Distributed.Redis.Scripts.GetOrAddMachine.lua");

        /// <summary>
        /// -- (MachineState: priorState, BitSet: inactiveMachineBitSet, BitSet: expiredBitSet) Heartbeat(string clusterStateKey, int machineId, MachineStatus declaredState, long currentTime, long recomputeExpiryInterval, long machineExpiryInterval)
        /// </summary>
        private static readonly string Heartbeat = GetEmbeddedResourceFile("BuildXL.Cache.ContentStore.Distributed.Redis.Scripts.Heartbeat.lua");

        /// <summary>
        /// -- (int: maxMachineId, HashEntry[]: unknownMachines) GetUnknownMachines(string clusterStateKey, int maxKnownMachineId)
        /// </summary>
        private static readonly string GetUnknownMachines = GetEmbeddedResourceFile("BuildXL.Cache.ContentStore.Distributed.Redis.Scripts.GetUnknownMachines.lua");

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
                FinalTaskResult.SetResult(taskResult);
            }

            public void SetFailure(Exception exception)
            {
                FinalTaskResult.SetException(exception);
            }
        }

        private readonly List<IRedisOperationAndResult> _redisOperations = new List<IRedisOperationAndResult>();

        /// <inheritdoc />
        public RedisBatch(RedisOperation operation, string keySpace) => (Operation, KeySpace) = (operation, keySpace);

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

        /// <inheritdoc />
        public async Task<(int machineId, bool isAdded)> GetOrAddMachineAsync(string clusterStateKey, string machineLocation, DateTime currentTime)
        {
            var redisOperation =
                new RedisOperationAndResult<RedisResult>(
                    batch =>
                        batch.ScriptEvaluateAsync(
                            GetOrAddMachine,
                            new RedisKey[] { clusterStateKey },
                            new RedisValue[]
                            {
                                machineLocation,
                                GetUnixTimeSecondsFromDateTime(currentTime)
                            }));
            _redisOperations.Add(redisOperation);
            var result = await redisOperation.FinalTaskResult.Task;
            var arrayResult = (RedisResult[])result;
            return (machineId: (int)arrayResult[0], isAdded: ((int)arrayResult[1]) != 0);
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
        public async Task<(int maxMachineId, Dictionary<MachineId, MachineLocation> unknownMachines)> GetUnknownMachinesAsync(
            string clusterStateKey,
            int maxKnownMachineId)
        {
            var redisOperation =
                new RedisOperationAndResult<RedisResult>(
                    batch =>
                        batch.ScriptEvaluateAsync(
                            GetUnknownMachines,
                            new RedisKey[] { clusterStateKey },
                            new RedisValue[]
                            {
                                maxKnownMachineId,
                            }));
            _redisOperations.Add(redisOperation);
            var result = await redisOperation.FinalTaskResult.Task;
            var arrayResult = (RedisResult[])result;
            var maxMachineId = (int)arrayResult[0];
            var unknownMachines = new Dictionary<MachineId, MachineLocation>();
            foreach (RedisResult[] entry in (RedisResult[])arrayResult[1])
            {
                int machineId = (int)entry[0];
                var locationData = (byte[])entry[1];
                unknownMachines.Add(new MachineId(machineId), new MachineLocation(locationData));
            }

            return (maxMachineId, unknownMachines);
        }

        /// <inheritdoc />
        public async Task<(MachineState priorState, BitMachineIdSet inactiveMachineIdSet)> HeartbeatAsync(
            string clusterStateKey,
            int machineId,
            MachineState declaredState,
            DateTime currentTime,
            TimeSpan recomputeExpiryInterval,
            TimeSpan machineExpiryInterval)
        {
            // -- (MachineState: priorState, BitSet: inactiveMachineBitSet)
            // Heartbeat(string clusterStateKey, int machineId, MachineStatus declaredState, long currentTime, long recomputeExpiryInterval, long machineExpiryInterval)
            var redisOperation =
                new RedisOperationAndResult<RedisResult>(
                    batch =>
                        batch.ScriptEvaluateAsync(
                            Heartbeat,
                            new RedisKey[] { clusterStateKey },
                            new RedisValue[]
                            {
                                machineId,
                                (int)declaredState,
                                GetUnixTimeSecondsFromDateTime(currentTime),
                                (long)recomputeExpiryInterval.TotalSeconds,
                                (long)machineExpiryInterval.TotalSeconds
                            }));

            _redisOperations.Add(redisOperation);
            var result = await redisOperation.FinalTaskResult.Task;
            var arrayResult = (RedisResult[])result;
            var priorState = (MachineState)(int)arrayResult[0];
            var inactiveMachinesData = (byte[])arrayResult[1] ?? CollectionUtilities.EmptyArray<byte>();
            var inactiveMachineIdSet = new BitMachineIdSet(inactiveMachinesData, 0);
            return (priorState, inactiveMachineIdSet);
        }

        /// <inheritdoc />
        public Task StringSetBitAsync(string key, long offset, bool bitValue)
        {
            var redisOperation = new RedisOperationAndResult<bool>(batch => batch.StringSetBitAsync(key, offset, bitValue));
            _redisOperations.Add(redisOperation);
            return redisOperation.FinalTaskResult.Task;
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
        public Task<TimeSpan?> KeyTimeToLiveAsync(string key)
        {
            var redisOperation = new RedisOperationAndResult<TimeSpan?>(batch => batch.KeyTimeToLiveAsync(key));
            _redisOperations.Add(redisOperation);
            return redisOperation.FinalTaskResult.Task;
        }

        /// <inheritdoc />
        public Task<bool> KeyDeleteAsync(string key)
        {
            var redisOperation = new RedisOperationAndResult<bool>(batch => batch.KeyDeleteAsync(key));
            _redisOperations.Add(redisOperation);
            return redisOperation.FinalTaskResult.Task;
        }

        /// <inheritdoc />
        public Task<bool> SetAddAsync(string key, RedisValue value)
        {
            var redisOperation = new RedisOperationAndResult<bool>(batch => batch.SetAddAsync(key, value));
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

        /// <inheritdoc />
        public Task<bool> SetRemoveAsync(RedisKey key, RedisValue value)
        {
            var redisOperation = new RedisOperationAndResult<bool>(batch => batch.SetRemoveAsync(key, value));
            _redisOperations.Add(redisOperation);
            return redisOperation.FinalTaskResult.Task;
        }

        /// <inheritdoc />
        public Task<long> SetRemoveAsync(RedisKey key, RedisValue[] values)
        {
            var redisOperation = new RedisOperationAndResult<long>(batch => batch.SetRemoveAsync(key, values));
            _redisOperations.Add(redisOperation);
            return redisOperation.FinalTaskResult.Task;
        }

        /// <inheritdoc />
        public Task<long> SetLengthAsync(RedisKey key, CommandFlags commandFlags = CommandFlags.None)
        {
            var redisOperation = new RedisOperationAndResult<long>(batch => batch.SetLengthAsync(key, commandFlags));
            _redisOperations.Add(redisOperation);
            return redisOperation.FinalTaskResult.Task;
        }

        /// <inheritdoc />
        public Task<RedisResult> SetBitIfExistAndRemoveIfEmptyBitMaskAsync(string key, long offset, bool bitValue)
        {
            var redisOperation =
                new RedisOperationAndResult<RedisResult>(
                    batch =>
                        batch.ScriptEvaluateAsync(SetBitIfExistAndRemoveIfEmptyBitMaskLuaScript, new RedisKey[] { key }, new RedisValue[] { offset, bitValue }));
            _redisOperations.Add(redisOperation);
            return redisOperation.FinalTaskResult.Task;
        }

        /// <inheritdoc />
        public async Task<RedisIncrementResult> TryStringIncrementBumpExpiryIfBelowOrEqualValueAsync(string key, uint comparisonValue, TimeSpan timeToLive, long incrementCount = 1)
        {
            var redisOperation =
                new RedisOperationAndResult<RedisResult>(
                    batch =>
                        batch.ScriptEvaluateAsync(IncrementBumpExpiryIfBelowOrEqualValue, new RedisKey[] { key }, new RedisValue[] { comparisonValue, (long)timeToLive.TotalSeconds, incrementCount }));
            _redisOperations.Add(redisOperation);
            var result = await redisOperation.FinalTaskResult.Task;
            long[] arrayResult = (long[])result;
            return new RedisIncrementResult(appliedIncrement: arrayResult[0], incrementedValue: arrayResult[1]);
        }

        /// <inheritdoc />
        public Task<RedisResult> StringSetRangeAndBumpExpiryAsync(
            string key,
            long rangeOffset,
            byte[] range,
            DateTime newExpiryTimeUtc,
            DateTime currentTimeUtc)
        {
            long expiryTime = GetUnixTimeSecondsFromDateTime(newExpiryTimeUtc);
            long unixCurrentTime = GetUnixTimeSecondsFromDateTime(currentTimeUtc);
            var redisOperation =
                new RedisOperationAndResult<RedisResult>(
                    batch =>
                        batch.ScriptEvaluateAsync(SetRangeBumpExpiryScript, new RedisKey[] { key }, new RedisValue[] { rangeOffset, range, expiryTime, unixCurrentTime }));
            _redisOperations.Add(redisOperation);
            return redisOperation.FinalTaskResult.Task;
        }

        /// <inheritdoc />
        public Task<RedisResult> StringGetAndUpdateExpiryAsync(string key, DateTime keepAliveTime, DateTime currentTime)
        {
            long unixExpiry = GetUnixTimeSecondsFromDateTime(keepAliveTime);
            long unixCurrentTime = GetUnixTimeSecondsFromDateTime(currentTime);
            var redisOperation =
                new RedisOperationAndResult<RedisResult>(
                    batch =>
                        batch.ScriptEvaluateAsync(GetAndUpdateExpiry, new RedisKey[] { key }, new RedisValue[] { unixExpiry, unixCurrentTime }));
            _redisOperations.Add(redisOperation);
            return redisOperation.FinalTaskResult.Task;
        }

        /// <inheritdoc />
        public Task<RedisResult> SetExpiryAsync(string key, DateTime keepAliveTime, DateTime currentTime)
        {
            long unixExpiry = GetUnixTimeSecondsFromDateTime(keepAliveTime);
            long unixCurrentTime = GetUnixTimeSecondsFromDateTime(currentTime);
            var redisOperation =
                new RedisOperationAndResult<RedisResult>(
                    batch =>
                        batch.ScriptEvaluateAsync(SetExpiryScript, new RedisKey[] { key }, new RedisValue[] { unixExpiry, unixCurrentTime }));
            _redisOperations.Add(redisOperation);
            return redisOperation.FinalTaskResult.Task;
        }

        /// <inheritdoc />
        public Task<RedisResult> TouchOrSetLocationRecordAsync(string key, byte[] sizeInBytes, long locationIdWithSizeOffset, DateTime keepAliveTime, DateTime currentTime)
        {
            long unixExpiry = GetUnixTimeSecondsFromDateTime(keepAliveTime);
            long unixCurrentTime = GetUnixTimeSecondsFromDateTime(currentTime);
            var redisOperation =
                new RedisOperationAndResult<RedisResult>(
                    batch =>
                        batch.ScriptEvaluateAsync(TouchOrSetLocationRecordScript, new RedisKey[] { key }, new RedisValue[] { sizeInBytes, locationIdWithSizeOffset, unixExpiry, unixCurrentTime }));
            _redisOperations.Add(redisOperation);
            return redisOperation.FinalTaskResult.Task;
        }

        /// <inheritdoc />
        public async Task<RedisLastAccessTimeResult> TryTrimWithLastAccessTimeCheckAsync(
            string key,
            DateTime currentTime,
            DateTime localAccessTime,
            TimeSpan contentHashBumpTime,
            TimeSpan targetRange,
            long machineId,
            int minReplicaCountForSafeEviction,
            int minReplicaCountForImmediateEviction)
        {
            long unixCurrentTime = GetUnixTimeSecondsFromDateTime(currentTime);
            long unixLocalAccessTime = GetUnixTimeSecondsFromDateTime(localAccessTime);
            long contentHashBumpTimeSeconds = (long)contentHashBumpTime.TotalSeconds;
            long targetRangeSeconds = (long)targetRange.TotalSeconds;
            var redisOperation =
                new RedisOperationAndResult<RedisResult>(
                    batch =>
                        batch.ScriptEvaluateAsync(
                            TryTrimWithLastAccessTimeCheck,
                            new RedisKey[] { key },
                            new RedisValue[] { unixCurrentTime, unixLocalAccessTime, contentHashBumpTimeSeconds, targetRangeSeconds, machineId, minReplicaCountForSafeEviction, minReplicaCountForImmediateEviction }));
            _redisOperations.Add(redisOperation);
            var result = await redisOperation.FinalTaskResult.Task;
            long[] arrayResult = (long[])result;
            bool safeToEvict = arrayResult[0] < 0;
            DateTime lastAccessTime = arrayResult[1] > 0 ? UnixEpoch.AddSeconds(arrayResult[1]) : DateTime.MinValue;
            return new RedisLastAccessTimeResult(safeToEvict, lastAccessTime, arrayResult[2]);
        }

        /// <inheritdoc />
        public async Task<int> AddCheckpointAsync(string checkpointsKey, RedisCheckpointInfo checkpointInfo, int maxSlotCount)
        {
            var redisOperation =
                new RedisOperationAndResult<RedisResult>(
                    batch =>
                        batch.ScriptEvaluateAsync(
                            AddCheckpoint,
                            new RedisKey[] { checkpointsKey },
                            new RedisValue[]
                            {
                                checkpointInfo.CheckpointId,
                                checkpointInfo.SequenceNumber,
                                checkpointInfo.CheckpointCreationTime.ToFileTimeUtc(),
                                checkpointInfo.MachineName,
                                maxSlotCount
                            }));
            _redisOperations.Add(redisOperation);
            var result = await redisOperation.FinalTaskResult.Task;
            return (int)result;
        }

        /// <inheritdoc />
        public async Task<RedisAcquireMasterRoleResult?> AcquireMasterRoleAsync(
            string masterRoleRegistryKey,
            string machineName,
            DateTime currentTime,
            TimeSpan leaseExpiryTime,
            int slotCount,
            bool release)
        {
            var redisOperation =
                new RedisOperationAndResult<RedisResult>(
                    batch =>
                        batch.ScriptEvaluateAsync(
                            AcquireSlot,
                            new RedisKey[] { masterRoleRegistryKey },
                            new RedisValue[]
                            {
                                machineName,
                                GetUnixTimeSecondsFromDateTime(currentTime),
                                GetUnixTimeSecondsFromDateTime(currentTime - leaseExpiryTime),
                                slotCount,
                                (int)(release ? SlotStatus.Released : SlotStatus.Acquired)
                            }));
            _redisOperations.Add(redisOperation);
            var result = await redisOperation.FinalTaskResult.Task;
            if (result.IsNull)
            {
                return null;
            }

            var results = (RedisResult[])result;
            if (results.Length < 4)
            {
                return null;
            }

            return new RedisAcquireMasterRoleResult((int)results[0], (string)results[1], GetDateTimeFromUnixTimeSeconds((long)results[2]), (SlotStatus)(long)results[3]);
        }

        /// <inheritdoc />
        public async Task<(RedisCheckpointInfo[] checkpoints, DateTime epochStartCursor)> GetCheckpointsInfoAsync(string checkpointsKey, DateTime currentTime)
        {
            var redisOperation =
                new RedisOperationAndResult<(HashEntry[] entries, RedisValue epochStartCursor)>(
                    async batch =>
                    {
                        var entriesTask = batch.HashGetAllAsync(checkpointsKey);
                        var setTask = batch.HashSetAsync(checkpointsKey, EpochStartCursorFieldName, GetUnixTimeSecondsFromDateTime(currentTime), When.NotExists);
                        var getTask = batch.HashGetAsync(checkpointsKey, EpochStartCursorFieldName);

                        await Task.WhenAll(entriesTask, setTask, getTask);
                        var entries = await entriesTask;
                        var epochStartCursor = await getTask;
                        return (entries, epochStartCursor);
                    });
            _redisOperations.Add(redisOperation);
            var result = await redisOperation.FinalTaskResult.Task;

            return (RedisCheckpointInfo.ParseCheckpoints(result.entries), GetDateTimeFromUnixTimeSeconds((long)result.epochStartCursor));
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
        public Task ExecuteBatchOperationAndGetCompletion(Context context, IDatabase database)
        {
            if (_redisOperations.Count == 0)
            {
                return Task.FromResult(0);
            }

            IBatch batch = database.CreateBatch();
            var taskToTrack = new List<Task>(_redisOperations.Count);
            foreach (IRedisOperationAndResult operation in _redisOperations)
            {
                var task = operation.AddTaskToBatch(batch);
                task.FireAndForget(context); // FireAndForget not inlined because we don't want to replace parent task with continuation
                taskToTrack.Add(task);
            }

            batch.Execute();
            return Task.WhenAll(taskToTrack);
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

        /// <summary>
        /// Helper to get the string content of a resource file from the current assembly.
        /// </summary>
        /// <remarks>This unfortunately cannot be in a shared location like 'AssemblyHelpers' because on .Net Core it ignores the assembly and always tries to extract the resources from the running assembly. Even though GetManifestResourceNames() does respect it.</remarks>
        public static string GetEmbeddedResourceFile(string resourceKey)
        {
            var callingAssembly = typeof(RedisBatch).GetTypeInfo().Assembly;
            var stream = callingAssembly.GetManifestResourceStream(resourceKey);
            if (stream == null)
            {
                Contract.Assert(false, $"Expected embedded resource key '{resourceKey}' not found in assembly {callingAssembly.FullName}. Valid resource names are: {string.Join(",", callingAssembly.GetManifestResourceNames())}");
                return null;
            }

            using (var sr = new StreamReader(stream))
            {
                return sr.ReadToEnd();
            }
        }
    }
}
