// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Distributed.Redis.Credentials;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using ContentStoreTest.Test;
using FluentAssertions;
using StackExchange.Redis;
using Xunit;
using Xunit.Abstractions;

namespace ContentStoreTest.Distributed.Redis
{
    public class ReplicatedRedisHashKeyTests : TestBase
    {
        private const string DefaultKeySpace = RedisContentLocationStoreFactory.DefaultKeySpace;

        private static readonly IDictionary<RedisKey, RedisValue> InitialTestData = new Dictionary<RedisKey, RedisValue>
        {
            { GetKey("first"), "one" },
            { GetKey("second"), "two" },
        };


        /// <inheritdoc />
        public ReplicatedRedisHashKeyTests(ITestOutputHelper output)
            : base(() => new PassThroughFileSystem(TestGlobal.Logger), TestGlobal.Logger, output)
        {
        }

        [Fact]
        public async Task UseReplicatedHashAsyncShouldNotCrashWhenBothRedisInstancesAreFailing()
        {
            // bug: https://dev.azure.com/mseng/1ES/_workitems/edit/1656837

            // It is important to throw non-redis error to not trigger retry strategy.
            var primaryDb = new FailureInjectingRedisDatabase(SystemClock.Instance, InitialTestData) {FailingQuery = 1, ThrowRedisException = false};

            var primaryConnection = MockRedisDatabaseFactory.CreateConnection(primaryDb, throwConnectionExceptionOnGet: true);
            var primaryAdapter = new RedisDatabaseAdapter(await RedisDatabaseFactory.CreateAsync(new EnvironmentConnectionStringProvider("TestConnectionString"), primaryConnection), DefaultKeySpace, retryCount: 1);

            var raidedDatabaseAdapter = new RaidedRedisDatabase(new Tracer("Test"), primaryAdapter, null);
            var context = new OperationContext(new Context(Logger));

            var replicatedRedisHashKey = new ReplicatedRedisHashKey("key", new MockReplicatedKeyHost(), new MemoryClock(), raidedDatabaseAdapter);
            var error = await replicatedRedisHashKey.UseReplicatedHashAsync(
                context,
                retryWindow: TimeSpan.FromMinutes(1),
                RedisOperation.All,
                (batch, key) =>
                {
                    return batch.StringGetAsync("first");
                }).ShouldBeError();
            // The operation should fail gracefully, not with a critical error like contract violation.
            error.IsCriticalFailure.Should().BeFalse();
        }

        private static RedisKey GetKey(RedisKey key)
        {
            return key.Prepend(DefaultKeySpace);
        }

        public class MockReplicatedKeyHost : ReplicatedRedisHashKey.IReplicatedKeyHost
        {
            /// <inheritdoc />
            public Tracer Tracer => new Tracer("tracer");

            /// <inheritdoc />
            public bool CanMirror => false;

            /// <inheritdoc />
            public TimeSpan MirrorInterval => default;
        }
    }
}
