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
using BuildXL.Cache.ContentStore.InterfacesTest;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.Utils;
using ContentStoreTest.Test;
using FluentAssertions;
using StackExchange.Redis;
using Xunit;
using Xunit.Abstractions;

namespace ContentStoreTest.Distributed.Redis
{
    public class RedisDatabaseAdapterTests : TestWithOutput
    {
        private const string DefaultKeySpace = ContentLocationStoreFactory.DefaultKeySpace;

        private static readonly IDictionary<RedisKey, RedisValue> InitialTestData = new Dictionary<RedisKey, RedisValue>
        {
            { GetKey("first"), "one" },
            { GetKey("second"), "two" },
        };

        public RedisDatabaseAdapterTests(ITestOutputHelper output)
            : base(output)
        {
        }

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
            await dbAdapter.ExecuteBatchOperationAsync(new Context(TestGlobal.Logger), redisBatch, default).ShouldBeSuccess();

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
            await dbAdapter.ExecuteBatchOperationAsync(new Context(TestGlobal.Logger), redisBatch, default).IgnoreFailure();

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

            var testDb = new FailureInjectingRedisDatabase(SystemClock.Instance, InitialTestData);

            int connectionCount = 0;

            bool failWithRedisConnectionError = false;
            // Setup Redis DB adapter
            Func<IConnectionMultiplexer> connectionMultiplexerFactory = () =>
            {
                connectionCount++;
                // Failing connection only when the local is true;
                return MockRedisDatabaseFactory.CreateConnection(
                    testDb,
                    testBatch: null,
                    throwConnectionExceptionOnGet: () => failWithRedisConnectionError);
            };

            var redisDatabaseFactory = await RedisDatabaseFactory.CreateAsync(connectionMultiplexerFactory, connectionMultiplexer => BoolResult.SuccessTask);
            var adapterConfiguration = new RedisDatabaseAdapterConfiguration(DefaultKeySpace,
                // If the operation fails we'll retry once and after that we should reset the connection multiplexer so the next operation should create a new one.
                redisConnectionErrorLimit: 2,
                retryCount: 1);
            var dbAdapter = new RedisDatabaseAdapter(redisDatabaseFactory, adapterConfiguration);

            connectionCount.Should().Be(1);

            // The first execution should fail with the connectivity issue.
            failWithRedisConnectionError = true;
            await ExecuteBatchAsync(dbAdapter).ShouldBeError();
            failWithRedisConnectionError = false;

            // The second execution should recreate the connection.
            await ExecuteBatchAsync(dbAdapter).ShouldBeSuccess();
            connectionCount.Should().Be(2);

            // The connection was recently recreated.
            // Introducing the connectivity issue again.
            failWithRedisConnectionError = true;
            await ExecuteBatchAsync(dbAdapter).ShouldBeError();
            // The previous call set the flag to reconnect, but the actual reconnect happening on the next call.
            connectionCount.Should().Be(2);

            await ExecuteBatchAsync(dbAdapter).ShouldBeError();
            connectionCount.Should().Be(3);

            await ExecuteBatchAsync(dbAdapter).ShouldBeError();
            connectionCount.Should().Be(4);
        }

        [Fact]
        public async Task DoNotReconnectTooFrequently()
        {
            var memoryClock = new MemoryClock();
            memoryClock.UtcNow = DateTime.UtcNow;

            // This test checks that if the client fails to connect to redis, it'll successfully reconnect to it.

            var testDb = new FailureInjectingRedisDatabase(SystemClock.Instance, InitialTestData);

            int connectionCount = 0;
            TimeSpan reconnectInterval = TimeSpan.FromSeconds(10);
            bool failWithRedisConnectionError = false;
            // Setup Redis DB adapter
            Func<IConnectionMultiplexer> connectionMultiplexerFactory = () =>
            {
                connectionCount++;
                // Failing connection only when the local is true;
                return MockRedisDatabaseFactory.CreateConnection(
                    testDb,
                    testBatch: null,
                    throwConnectionExceptionOnGet: () => failWithRedisConnectionError);
            };

            var redisDatabaseFactory = await RedisDatabaseFactory.CreateAsync(connectionMultiplexerFactory, connectionMultiplexer => BoolResult.SuccessTask);
            var adapterConfiguration = new RedisDatabaseAdapterConfiguration(DefaultKeySpace,
                // If the operation fails we'll retry once and after that we should reset the connection multiplexer so the next operation should create a new one.
                redisConnectionErrorLimit: 2,
                retryCount: 1,
                minReconnectInterval: reconnectInterval);
            var dbAdapter = new RedisDatabaseAdapter(redisDatabaseFactory, adapterConfiguration, memoryClock);

            connectionCount.Should().Be(1);

            // The first execution should fail with the connectivity issue.
            failWithRedisConnectionError = true;
            await ExecuteBatchAsync(dbAdapter).ShouldBeError();
            failWithRedisConnectionError = false;

            // The second execution should recreate the connection.
            await ExecuteBatchAsync(dbAdapter).ShouldBeSuccess();
            connectionCount.Should().Be(2);

            // The connection was recently recreated.
            // Introducing the connectivity issue first.
            failWithRedisConnectionError = true;
            await ExecuteBatchAsync(dbAdapter).ShouldBeError();
            // The next call should not trigger the reconnect
            await ExecuteBatchAsync(dbAdapter).ShouldBeError();
            connectionCount.Should().Be(2);

            // Moving the clock forward and the next call should cause a reconnect.
            memoryClock.UtcNow += reconnectInterval.Multiply(2);
            await ExecuteBatchAsync(dbAdapter).ShouldBeError();
            // The previous execution should set the flag to reconnect,
            // but the reconnect count is still the same.
            connectionCount.Should().Be(2);

            // And only during the next call the multiplexer is actually recreated
            await ExecuteBatchAsync(dbAdapter).ShouldBeError();
            connectionCount.Should().Be(3);
        }

        private static Task<BoolResult> ExecuteBatchAsync(RedisDatabaseAdapter dbAdapter) =>
            dbAdapter.ExecuteBatchOperationAsync(new Context(TestGlobal.Logger), dbAdapter.CreateBatchOperation(RedisOperation.All), default);

        private static RedisKey GetKey(RedisKey key)
        {
            return key.Prepend(DefaultKeySpace);
        }
    }
}
