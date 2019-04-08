// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Distributed.Redis.Credentials;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using ContentStoreTest.Test;
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

        private static RedisKey GetKey(RedisKey key)
        {
            return key.Prepend(DefaultKeySpace);
        }
    }
}
