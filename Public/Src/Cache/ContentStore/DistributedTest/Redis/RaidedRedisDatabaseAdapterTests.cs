// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Distributed.Redis.Credentials;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using ContentStoreTest.Test;
using FluentAssertions;
using StackExchange.Redis;
using Xunit;
using Xunit.Abstractions;

namespace ContentStoreTest.Distributed.Redis
{
    public class RaidedRedisDatabaseAdapterTests : TestBase
    {
        private const string DefaultKeySpace = ContentLocationStoreFactory.DefaultKeySpace;

        private static readonly IDictionary<RedisKey, RedisValue> InitialTestData = new Dictionary<RedisKey, RedisValue>
        {
            { GetKey("first"), "one" },
            { GetKey("second"), "two" },
        };


        /// <inheritdoc />
        public RaidedRedisDatabaseAdapterTests(ITestOutputHelper output)
            : base(() => new PassThroughFileSystem(TestGlobal.Logger), TestGlobal.Logger, output)
        {
        }

        [Fact]
        public async Task TestRaidedRedisFailureRecovery()
        {
            // It is important to set ThrowRedisException to false, because redis exceptions are recoverable
            // and we don't want to run this test for too long because of exponential back-off recovery algorithm
            var primaryDb = new FailureInjectingRedisDatabase(SystemClock.Instance, InitialTestData) {FailingQuery = -1, ThrowRedisException = false};

            var secondaryDb = new FailureInjectingRedisDatabase(SystemClock.Instance, InitialTestData) {FailingQuery = -1, ThrowRedisException = false};

            // Setup Redis DB adapter
            var primaryConnection = MockRedisDatabaseFactory.CreateConnection(primaryDb);
            var primaryAdapter = new RedisDatabaseAdapter(await RedisDatabaseFactory.CreateAsync(new EnvironmentConnectionStringProvider("TestConnectionString"), primaryConnection), DefaultKeySpace);

            var secondaryConnection = MockRedisDatabaseFactory.CreateConnection(secondaryDb);
            var secondaryAdapter = new RedisDatabaseAdapter(await RedisDatabaseFactory.CreateAsync(new EnvironmentConnectionStringProvider("TestConnectionString"), secondaryConnection), DefaultKeySpace);
            var raidedDatabaseAdapter = new RaidedRedisDatabase(new Tracer("Test"), primaryAdapter, secondaryAdapter);
            var context = new OperationContext(new Context(Logger));

            var retryWindow = TimeSpan.FromSeconds(1);

            // Running for the first time, both operation should be successful.
            var r = await raidedDatabaseAdapter.ExecuteRaidedAsync(context, (adapter, token) => ExecuteAsync(context, adapter, token), retryWindow, concurrent: true);
            r.primary.ShouldBeSuccess();
            r.secondary.ShouldBeSuccess();

            secondaryDb.FailNextOperation();
            r = await raidedDatabaseAdapter.ExecuteRaidedAsync(context, (adapter, token) => ExecuteAsync(context, adapter, token), retryWindow, concurrent: true);
            r.primary.ShouldBeSuccess();
            // The second redis should fail when we'll try to use it the second time.
            r.secondary.ShouldBeError();

            primaryDb.FailNextOperation();
            r = await raidedDatabaseAdapter.ExecuteRaidedAsync(context, (adapter, token) => ExecuteAsync(context, adapter, token), retryWindow, concurrent: true);
            // Now all the instance should fail.
            r.primary.ShouldBeError();
            r.secondary.ShouldBeSuccess();

            primaryDb.FailNextOperation();
            secondaryDb.FailNextOperation();
            r = await raidedDatabaseAdapter.ExecuteRaidedAsync(context, (adapter, token) => ExecuteAsync(context, adapter, token), retryWindow, concurrent: true);
            // Now all the instance should fail.
            r.primary.ShouldBeError();
            r.secondary.ShouldBeError();
        }

        [Fact]
        public async Task SlowOperationTimesOut()
        {
            // Setup test DB configured to fail 2nd query with Redis Exception
            var primaryDb = new FailureInjectingRedisDatabase(SystemClock.Instance, InitialTestData) { FailingQuery = -1 };

            // The second redis will throw RedisException, because we want to use retry strategy here and see the cancellation happening.
            var secondaryDb = new FailureInjectingRedisDatabase(SystemClock.Instance, InitialTestData) { FailingQuery = -1, ThrowRedisException = true };

            // Setup Redis DB adapter
            var primaryConnection = MockRedisDatabaseFactory.CreateConnection(primaryDb);
            var primaryAdapter = new RedisDatabaseAdapter(await RedisDatabaseFactory.CreateAsync(new EnvironmentConnectionStringProvider("TestConnectionString"), primaryConnection), DefaultKeySpace);

            var secondaryConnection = MockRedisDatabaseFactory.CreateConnection(secondaryDb);
            var secondaryAdapter = new RedisDatabaseAdapter(await RedisDatabaseFactory.CreateAsync(new EnvironmentConnectionStringProvider("TestConnectionString"), secondaryConnection), DefaultKeySpace);
            var raidedDatabaseAdapter = new RaidedRedisDatabase(new Tracer("Test"), primaryAdapter, secondaryAdapter);
            var context = new OperationContext(new Context(Logger));

            var retryWindow = TimeSpan.FromSeconds(1);

            // All the operations in the secondary instance will fail all the time.
            secondaryDb.FailNextOperation(resetFailureAutomatically: false);

            var r = await raidedDatabaseAdapter.ExecuteRaidedAsync(
                context,
                (adapter, token) => ExecuteAsync(context, adapter, token),
                retryWindow,
                concurrent: true);
            r.primary.ShouldBeSuccess();
            // The secondary result is null is an indication that the operation was canceled.
            r.secondary.Should().BeNull();
        }

        private static Task<BoolResult> ExecuteAsync(OperationContext context, RedisDatabaseAdapter adapter, CancellationToken token)
        {
            var redisBatch = adapter.CreateBatchOperation(RedisOperation.All);
            var first = redisBatch.StringGetAsync("first");

            first.FireAndForget(context, redisBatch);

            // Execute the batch
            return adapter.ExecuteBatchOperationAsync(new Context(TestGlobal.Logger), redisBatch, token);
        }

        private static RedisKey GetKey(RedisKey key)
        {
            return key.Prepend(DefaultKeySpace);
        }
    }
}
