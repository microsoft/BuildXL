// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
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
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Tasks;
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
        public async Task BatchIsCancelledOnReconnectForOtherOperation()
        {
            // The test checks that if the connection is lost and the new connection is established,
            // all the pending operations are cancelled.

            var testDb = new FailureInjectingRedisDatabase(SystemClock.Instance, InitialTestData);
            var context = new OperationContext(new Context(TestGlobal.Logger));

            int connectionCount = 0;

            bool failWithRedisConnectionErrorOnce = false;

            // Setup Redis DB adapter
            Func<IConnectionMultiplexer> connectionMultiplexerFactory = () =>
            {
                connectionCount++;
                // Failing connection only when the local is true;
                return MockRedisDatabaseFactory.CreateConnection(
                    testDb,
                    testBatch: null,
                    throwConnectionExceptionOnGet: () =>
                                                   {
                                                       var oldValue = failWithRedisConnectionErrorOnce;
                                                       failWithRedisConnectionErrorOnce = false;
                                                       return oldValue;
                                                   });
            };

            var redisDatabaseFactory = await RedisDatabaseFactory.CreateAsync(connectionMultiplexerFactory, connectionMultiplexer => BoolResult.SuccessTask);
            var adapterConfiguration = new RedisDatabaseAdapterConfiguration(DefaultKeySpace,
                // If the operation fails we'll retry once and after that we should reset the connection multiplexer so the next operation should create a new one.
                redisConnectionErrorLimit: 1,
                // No retries: should fail the operation immediately.
                retryCount: 0,
                cancelBatchWhenMultiplexerIsClosed: true);
            var dbAdapter = new RedisDatabaseAdapter(redisDatabaseFactory, adapterConfiguration);

            connectionCount.Should().Be(1);

            // Causing HashGetAllAsync operation to hang that will cause ExecuteGetCheckpointInfoAsync operation to "hang".
            var taskCompletionSource = new TaskCompletionSource<HashEntry[]>();
            testDb.HashGetAllAsyncTask = taskCompletionSource.Task;

            // Running two operations at the same time:
            // The first one should get stuck on task completion source's task
            // and the second one will fail with connectivity issue, will cause the restart of the multiplexer,
            // and will cancel all the existing operations 9including the first one).

            var task1 = ExecuteGetCheckpointInfoAsync(context, dbAdapter);
            failWithRedisConnectionErrorOnce = true;
            var task2 = ExecuteGetCheckpointInfoAsync(context, dbAdapter);

            Output.WriteLine("Waiting for the redis operations to finish.");
            await Task.WhenAll(task1, task2);

            var results = new[] {task1.Result, task2.Result};
            var errorCount = results.Count(r => !r.Succeeded && r.ErrorMessage?.Contains("RedisConnectionException") == true);
            errorCount.Should().Be(1, $"Should have 1 error with RedisConnectionException. Results: {string.Join(Environment.NewLine, results.Select(r => r.ToString()))}");

            var cancelledCount = results.Count(r => r.IsCancelled);
            cancelledCount.Should().Be(1, $"Should have 1 cancellation. Results: {string.Join(Environment.NewLine, results.Select(r => r.ToString()))}");
        }

        [Fact]
        public async Task BatchIsCanceledBeforeOperationStarts()
        {
            // The test checks that if the connection is lost and the new connection is established,
            // all the pending operations are cancelled.

            var testDb = new FailureInjectingRedisDatabase(SystemClock.Instance, InitialTestData);
            var cts = new CancellationTokenSource();
            var context = new OperationContext(new Context(TestGlobal.Logger), cts.Token);

            // Setup Redis DB adapter
            Func<IConnectionMultiplexer> connectionMultiplexerFactory = () =>
            {
                return MockRedisDatabaseFactory.CreateConnection(testDb, testBatch: null);
            };

            var redisDatabaseFactory = await RedisDatabaseFactory.CreateAsync(connectionMultiplexerFactory, connectionMultiplexer => BoolResult.SuccessTask);
            var adapterConfiguration = new RedisDatabaseAdapterConfiguration(DefaultKeySpace,
                // If the operation fails we'll retry once and after that we should reset the connection multiplexer so the next operation should create a new one.
                redisConnectionErrorLimit: 1,
                // No retries: should fail the operation immediately.
                retryCount: 0,
                cancelBatchWhenMultiplexerIsClosed: true);
            var dbAdapter = new RedisDatabaseAdapter(redisDatabaseFactory, adapterConfiguration);

            // Causing HashGetAllAsync operation to hang that will cause ExecuteGetCheckpointInfoAsync operation to "hang".
            var taskCompletionSource = new TaskCompletionSource<HashEntry[]>();
            testDb.HashGetAllAsyncTask = taskCompletionSource.Task;
            cts.Cancel();
            // Using timeout to avoid hangs in the tests if something is wrong with the logic.
            var result = await ExecuteGetCheckpointInfoAsync(context, dbAdapter).WithTimeoutAsync(TimeSpan.FromSeconds(1));
            result.IsCancelled.Should().BeTrue();
        }

        private static Task<Result<(RedisCheckpointInfo[] checkpoints, DateTime epochStartCursor)>> ExecuteGetCheckpointInfoAsync(OperationContext context, RedisDatabaseAdapter dbAdapter)
        {
            var tracer = new Tracer("Tracer");
            return dbAdapter.ExecuteBatchAsResultAsync(context, tracer, b => b.GetCheckpointsInfoAsync("Key", DateTime.UtcNow), RedisOperation.Batch);
        }

        [Fact]
        public async Task ExecuteBatchOperationRetriesOnObjectDisposedException()
        {
            // Setup test DB configured to fail 2nd query with Redis Exception
            var testDb = new FailureInjectingRedisDatabase(SystemClock.Instance, InitialTestData)
                         {
                             FailingQuery = 2,
                             ExceptionTypeToThrow = typeof(ObjectDisposedException),
                         };

            // Setup Redis DB adapter
            var testConn = MockRedisDatabaseFactory.CreateConnection(testDb);
            var dbAdapter = new RedisDatabaseAdapter(
                await RedisDatabaseFactory.CreateAsync(new EnvironmentConnectionStringProvider("TestConnectionString"), testConn),
                new RedisDatabaseAdapterConfiguration(DefaultKeySpace, treatObjectDisposedExceptionAsTransient: true));

            // Create a batch query 
            var redisBatch = dbAdapter.CreateBatchOperation(RedisOperation.All);
            var first = redisBatch.StringGetAsync("first");
            var second = redisBatch.StringGetAsync("second");

            // Execute the batch
            await dbAdapter.ExecuteBatchOperationAsync(new Context(TestGlobal.Logger), redisBatch, default).ShouldBeSuccess();

            // Adapter is expected to retry the entire batch if single call fails
            Assert.True(testDb.BatchCalled);
            Assert.Null(first.Exception);
            Assert.Null(second.Exception);
            Assert.Equal(4, testDb.Calls);
            Assert.Equal("one", await first);
            Assert.Equal("two", await second);
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

        private static Task<BoolResult> ExecuteBatchAsync(RedisDatabaseAdapter dbAdapter) => dbAdapter.ExecuteBatchOperationAsync(new Context(TestGlobal.Logger), dbAdapter.CreateBatchOperation(RedisOperation.All), default);

        private static RedisKey GetKey(RedisKey key)
        {
            return key.Prepend(DefaultKeySpace);
        }
    }
}
