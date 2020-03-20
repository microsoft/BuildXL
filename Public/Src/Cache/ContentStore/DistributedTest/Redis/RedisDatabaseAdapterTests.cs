// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Distributed.Redis.Credentials;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using ContentStoreTest.Test;
using FluentAssertions;
using StackExchange.Redis;
using Xunit;

namespace ContentStoreTest.Distributed.Redis
{
    public class RedisDatabaseAdapterTests
    {
        private const string DefaultKeySpace = RedisContentLocationStoreFactory.DefaultKeySpace;

        private static readonly IDictionary<RedisKey, RedisValue> InitialTestData = new Dictionary<RedisKey, RedisValue>
        {
            { GetKey("first"), "one" },
            { GetKey("second"), "two" },
        };

        [Fact]
        public async Task ExecuteBatchOperationRetriesOnRedisExceptions()
        {
            // Setup test DB configured to fail 2nd query with Redis Exception
            var testDb = new FailureInjectingRedisDatabase(SystemClock.Instance, InitialTestData)
            {
                FailingQuery = 2
            };

            // Setup Redis DB adapter
            var testConn = MockRedisDatabaseFactory.CreateConnection(testDb);
            var dbAdapter = new RedisDatabaseAdapter(await RedisDatabaseFactory.CreateAsync(new EnvironmentConnectionStringProvider("TestConnectionString"), testConn), DefaultKeySpace);

            // Create a batch query
            var redisBatch = dbAdapter.CreateBatchOperation(RedisOperation.All);
            var first = redisBatch.StringGetAsync("first");
            var second = redisBatch.StringGetAsync("second");

            // Execute the batch
            await dbAdapter.ExecuteBatchOperationAsync(new Context(TestGlobal.Logger), redisBatch, default(CancellationToken)).ShouldBeSuccess();

            // Adapter is expected to retry the entire batch if single call fails
            Assert.True(testDb.BatchCalled);
            Assert.Equal(4, testDb.Calls);
            Assert.Null(first.Exception);
            Assert.Null(second.Exception);
            Assert.Equal("one", await first);
            Assert.Equal("two", await second);
        }

        [Fact]
        public async Task ExecuteBatchOperationNoRetryOnRandomExceptions()
        {
            // Setup test DB configured to fail 2nd query with normal Exception
            var testDb = new FailureInjectingRedisDatabase(SystemClock.Instance, InitialTestData)
            {
                FailingQuery = 2,
                ThrowRedisException = false,
            };

            // Setup Redis DB adapter
            var testConn = MockRedisDatabaseFactory.CreateConnection(testDb);
            var dbAdapter = new RedisDatabaseAdapter(await RedisDatabaseFactory.CreateAsync(new EnvironmentConnectionStringProvider("TestConnectionString"), testConn), DefaultKeySpace);

            // Create a batch query
            var redisBatch = dbAdapter.CreateBatchOperation(RedisOperation.All);
            var first = redisBatch.StringGetAsync("first");
            var second = redisBatch.StringGetAsync("second");

            // Execute the batch
            await dbAdapter.ExecuteBatchOperationAsync(new Context(TestGlobal.Logger), redisBatch, default(CancellationToken)).IgnoreFailure();

            // Adapter does not retry in case random exception is thrown
            Assert.True(testDb.BatchCalled);
            Assert.Equal(2, testDb.Calls);
            Assert.NotNull(first.Exception);
            Assert.NotNull(second.Exception);
        }

        [Fact]
        public async Task TheClientReconnectsWhenTheNumberOfConnectionIssuesExceedsTheLimit()
        {
            // This test checks that if the client fails to connect to redis, it'll successfully reconnect to it.

            // Setup test DB configured to fail 2nd query with normal Exception
            var testDb = new FailureInjectingRedisDatabase(SystemClock.Instance, InitialTestData)
            {
                // No queries will fail, instead GetDatabase will throw with RedisConnectionException.
                FailingQuery = -1,
            };

            int numberOfFactoryCalls = 0;

            // Setup Redis DB adapter
            Func<IConnectionMultiplexer> connectionMultiplexerFactory = () =>
            {
                numberOfFactoryCalls++;
                // Failing connection error only from the first instance.
                return MockRedisDatabaseFactory.CreateConnection(testDb, testBatch: null, throwConnectionExceptionOnGet: numberOfFactoryCalls == 1);
            };

            var redisDatabaseFactory = await RedisDatabaseFactory.CreateAsync(connectionMultiplexerFactory, connectionMultiplexer => BoolResult.SuccessTask);
            var adapterConfiguration = new RedisDatabaseAdapterConfiguration(DefaultKeySpace,
                // If the operation fails we'll retry once and after that we should reset the connection multiplexer so the next operation should create a new one.
                redisConnectionErrorLimit: 2,
                retryCount: 1);
            var dbAdapter = new RedisDatabaseAdapter(redisDatabaseFactory, adapterConfiguration);

            // Create a batch query
            var redisBatch = dbAdapter.CreateBatchOperation(RedisOperation.All);

            // Execute the batch
            var result = await dbAdapter.ExecuteBatchOperationAsync(new Context(TestGlobal.Logger), redisBatch, default(CancellationToken));
            // The first execute batch should fail with the connectivity issue.
            result.ShouldBeError();
            numberOfFactoryCalls.Should().Be(1);

            var redisBatch2 = dbAdapter.CreateBatchOperation(RedisOperation.All);
            // Then we should recreate the connection and the second one should be successful.
            await dbAdapter.ExecuteBatchOperationAsync(new Context(TestGlobal.Logger), redisBatch2, default(CancellationToken)).ShouldBeSuccess();

            numberOfFactoryCalls.Should().Be(2);
        }

        private static RedisKey GetKey(RedisKey key)
        {
            return key.Prepend(DefaultKeySpace);
        }
    }
}
