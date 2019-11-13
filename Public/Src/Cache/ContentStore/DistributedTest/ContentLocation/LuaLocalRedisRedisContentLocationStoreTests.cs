// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using ContentStoreTest.Distributed.Redis;
using ContentStoreTest.Test;
using Xunit;
using Xunit.Abstractions;
using FluentAssertions;

[assembly: CollectionBehavior(MaxParallelThreads = 4, DisableTestParallelization = true)]

namespace ContentStoreTest.Distributed.ContentLocation
{
#pragma warning disable SA1118 // Parameter must not span multiple lines
    [Collection("Redis-based tests")]
    [Trait("Category", "LongRunningTest")]
    public class LuaLocalRedisRedisContentLocationStoreTests : LocalRedisRedisContentLocationStoreTests
    {
        /// <inheritdoc />
        public LuaLocalRedisRedisContentLocationStoreTests(LocalRedisFixture redis, ITestOutputHelper output)
            : base(redis, output)
        { }

        [Fact]
        public Task TestCompareExchange()
        {
            // Setup
            var context = new Context(TestGlobal.Logger);
            var mockRedisDatabase = GetRedisDatabase(_clock);

            const string weakFingerprintKey = "wfkey";
            const string selectorFieldName = "selector";
            const string tokenFieldName = "token";
            const string expectedToken = "expectedToken";
            const string newToken = "newToken";
            const string contentHashList = "contentHashList";

            // Round time so that comparison below will succeed
            var initialTime = _clock.UtcNow.ToUnixTime().ToDateTime();

            return TestAsync(
                context,
                mockRedisDatabase,
                mockRedisDatabase,
                async (store, storeFactory) =>
                {
                    var adapter = storeFactory.RedisDatabaseAdapter;
                    var database = storeFactory.RedisDatabase;

                    var checkpoints = Enumerable.Range(0, 20).Select(sequenceNumber => CreateRandomCheckpoint(sequenceNumber)).ToArray();

                    var operationContext = new OperationContext(context);
                    var exchanged = await adapter.ExecuteBatchAsync(operationContext, batch =>
                    {
                        return batch.CompareExchangeAsync(weakFingerprintKey, selectorFieldName, tokenFieldName, string.Empty, contentHashList, expectedToken);
                    }, RedisOperation.All);

                    exchanged.Should().BeTrue();

                    exchanged = await adapter.ExecuteBatchAsync(operationContext, batch =>
                    {
                        return batch.CompareExchangeAsync(weakFingerprintKey, selectorFieldName, tokenFieldName, expectedToken, contentHashList, newToken);
                    }, RedisOperation.All);

                    exchanged.Should().BeTrue();
                });
        }

        [Fact]
        public Task TestAddCheckpoint()
        {
            // Setup
            var context = new Context(TestGlobal.Logger);
            var mockRedisDatabase = GetRedisDatabase(_clock);

            const string checkpointsKey = "testChkpt";

            // Round time so that comparison below will succeed
            var initialTime = _clock.UtcNow.ToUnixTime().ToDateTime();

            return TestAsync(
                context,
                mockRedisDatabase,
                mockRedisDatabase,
                async (store, storeFactory) =>
                {
                    var adapter = storeFactory.RedisDatabaseAdapter;
                    var database = storeFactory.RedisDatabase;

                    var checkpoints = Enumerable.Range(0, 20).Select(sequenceNumber => CreateRandomCheckpoint(sequenceNumber)).ToArray();

                    var operationContext = new OperationContext(context);
                    await adapter.ExecuteBatchAsync(operationContext, batch =>
                    {
                        foreach (var checkpoint in checkpoints)
                        {
                            batch.AddCheckpointAsync(checkpointsKey, checkpoint, maxSlotCount: 10).FireAndForget(context);
                        }

                        return Task.FromResult(0);
                    }, RedisOperation.All);

                    var values = database.GetDbWithExpiry();

                    var (retrievedCheckpoints, startCursorTime) = await adapter.ExecuteBatchAsync(operationContext, batch =>
                    {
                        return batch.GetCheckpointsInfoAsync(checkpointsKey, initialTime);
                    }, RedisOperation.All);

                    // Higher sequence numbers should replace lower sequence numbers
                    Assert.Equal(
                        Enumerable.Range(10, 10).ToArray(),
                        retrievedCheckpoints.Select(c => (int)c.SequenceNumber).OrderBy(s => s).ToArray());

                    checkpoints = checkpoints.OrderBy(c => c.SequenceNumber).Where(c => c.SequenceNumber >= 10).ToArray();
                    retrievedCheckpoints = retrievedCheckpoints.OrderBy(c => c.SequenceNumber).ToArray();

                    Assert.Equal(checkpoints, retrievedCheckpoints, new RedisCheckpointInfoEqualityComparer());
                    Assert.Equal(initialTime, startCursorTime);
                });
        }

        private RedisCheckpointInfo CreateRandomCheckpoint(int sequenceNumber)
        {
            return new RedisCheckpointInfo(
                        Guid.NewGuid().ToString(),
                        sequenceNumber,
                        DateTime.UtcNow,
                        "machine:" + Guid.NewGuid().ToString());
        }

        [Fact]
        public Task TestIncrementWithExpiry()
        {
            // Setup
            var context = new Context(TestGlobal.Logger);
            var mockRedisDatabase = GetRedisDatabase(_clock);

            return TestAsync(
                context,
                mockRedisDatabase,
                mockRedisDatabase,
                async (store, storeFactory) =>
                {
                    await Task.Yield();
                    var adapter = storeFactory.RedisDatabaseAdapter;
                    var database = storeFactory.RedisDatabase;

                    // Case: Non existing key applies expiry and sets value to 1
                    await IncrementWithExpiryValidate(
                        context,
                        adapter,
                        database,
                        "key1",
                        requestedIncrement: 1,
                        comparisonValue: 1,
                        specifiedExpiry: TimeSpan.FromHours(1),
                        expectedIncrementedValue: 1,
                        expectedReturnValue: 1);

                    // Case: Increment existing key over threshold does not change expiry
                    await IncrementWithExpiryValidate(
                        context,
                        adapter,
                        database,
                        "key1",
                        requestedIncrement: 1,
                        comparisonValue: 1,
                        specifiedExpiry: TimeSpan.FromHours(2),
                        expectedDifferentExpiry: TimeSpan.FromHours(1),
                        expectedIncrementedValue: 1,
                        expectedReturnValue: 0);

                    // Case: Increment existing key within threshold changes expiry
                    await IncrementWithExpiryValidate(
                        context,
                        adapter,
                        database,
                        "key1",
                        requestedIncrement: 1,
                        comparisonValue: 3,
                        specifiedExpiry: TimeSpan.FromHours(2),
                        expectedDifferentExpiry: TimeSpan.FromHours(1),
                        expectedIncrementedValue: 2,
                        expectedReturnValue: 1);

                    // Case: Does not create for non-existing key with comparison value == 0
                    await IncrementWithExpiryValidate(
                        context,
                        adapter,
                        database,
                        "key2",
                        requestedIncrement: 1,
                        comparisonValue: 0,
                        specifiedExpiry: TimeSpan.FromHours(1),
                        expectedIncrementedValue: null,
                        expectedReturnValue: 0);

                    // Case: Does not create for non-existing key with decrement
                    await IncrementWithExpiryValidate(
                        context,
                        adapter,
                        database,
                        "key2",
                        requestedIncrement: -1,
                        comparisonValue: 1,
                        specifiedExpiry: TimeSpan.FromHours(1),
                        expectedIncrementedValue: null,
                        expectedReturnValue: 0);

                    // Case: Increment will not exceed comparison value but will increment partially
                    // if slots available
                    await IncrementWithExpiryValidate(
                        context,
                        adapter,
                        database,
                        "key1",
                        requestedIncrement: 10,
                        comparisonValue: 10,
                        specifiedExpiry: TimeSpan.FromHours(3),
                        expectedDifferentExpiry: TimeSpan.FromHours(1),
                        expectedIncrementedValue: 10,
                        expectedReturnValue: 8);

                    // Case: Decrement will not exceed comparison value but will decrement partially
                    // if slots available
                    await IncrementWithExpiryValidate(
                        context,
                        adapter,
                        database,
                        "key1",
                        requestedIncrement: -1,
                        comparisonValue: 10,
                        specifiedExpiry: TimeSpan.FromHours(4),
                        expectedDifferentExpiry: TimeSpan.FromHours(1),
                        expectedIncrementedValue: 9,
                        expectedReturnValue: -1);

                    // Case: Decrement will not exceed comparison value but will decrement partially
                    // if slots available
                    await IncrementWithExpiryValidate(
                        context,
                        adapter,
                        database,
                        "key1",
                        requestedIncrement: -100,
                        comparisonValue: 10,
                        specifiedExpiry: TimeSpan.FromHours(5),
                        expectedDifferentExpiry: TimeSpan.FromHours(1),
                        expectedIncrementedValue: 0,
                        expectedReturnValue: -9);

                    // Case: Increment from zero updates expiry
                    await IncrementWithExpiryValidate(
                        context,
                        adapter,
                        database,
                        "key1",
                        requestedIncrement: 9,
                        comparisonValue: 10,
                        specifiedExpiry: TimeSpan.FromHours(5),
                        expectedIncrementedValue: 9,
                        expectedReturnValue: 9);
                });
        }

        private static async Task IncrementWithExpiryValidate(
            Context context,
            RedisDatabaseAdapter adapter,
            ITestRedisDatabase database,
            string key,
            uint comparisonValue,
            TimeSpan specifiedExpiry,
            int requestedIncrement,
            long expectedReturnValue,
            long? expectedIncrementedValue,
            TimeSpan? expectedDifferentExpiry = null)
        {
            var batch = adapter.CreateBatchOperation(RedisOperation.All);
            var redisKey = GetKey(key);
            var incrementWithExpire = batch.TryStringIncrementBumpExpiryIfBelowOrEqualValueAsync(key, comparisonValue, timeToLive: specifiedExpiry, requestedIncrement: requestedIncrement);
            await adapter.ExecuteBatchOperationAsync(context, batch, CancellationToken.None).IgnoreFailure();

            var incrementedValue = await incrementWithExpire;
            Assert.Equal(expectedReturnValue, incrementedValue.AppliedIncrement);

            var keysWithExpiry = database.GetDbWithExpiry();
            if (expectedIncrementedValue == null)
            {
                Assert.False(keysWithExpiry.ContainsKey(redisKey));
                Assert.Equal(expectedReturnValue, incrementedValue.IncrementedValue);
                return;
            }

            Assert.True(keysWithExpiry.ContainsKey(redisKey));

            var expiry = keysWithExpiry[redisKey];

            if (expectedDifferentExpiry != null)
            {
                Assert.False(expiry.Equals(new MockRedisValueWithExpiry(expectedIncrementedValue, DateTime.UtcNow + specifiedExpiry)));
                Assert.True(expiry.Equals(new MockRedisValueWithExpiry(expectedIncrementedValue, DateTime.UtcNow + expectedDifferentExpiry.Value)));
            }
            else
            {
                Assert.True(expiry.Equals(new MockRedisValueWithExpiry(expectedIncrementedValue, DateTime.UtcNow + specifiedExpiry)));
            }
        }
    }
}
