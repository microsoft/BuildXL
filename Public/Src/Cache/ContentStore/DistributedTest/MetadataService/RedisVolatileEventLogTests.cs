// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using ContentStoreTest.Distributed.Redis;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.Cache.ContentStore.Distributed.Test.MetadataService
{
    [Collection("Redis-based tests")]
    public class RedisVolatileEventStorageTests : TestBase, IDisposable
    {
        private readonly LocalRedisFixture _redisFixture;

        public RedisVolatileEventStorageTests(LocalRedisFixture redis, ITestOutputHelper output)
            : base(TestGlobal.Logger, output)
        {
            Contract.RequiresNotNull(redis);
            _redisFixture = redis;
        }

        [Fact]
        public Task SimpleAppendTest()
        {
            return RunTest(async (context, log) =>
            {
                var cursor = new BlockReference()
                {
                    LogId = new CheckpointLogId(0),
                    LogBlockId = 0,
                };

                var one = "hello";
                var oneBytes = Encoding.ASCII.GetBytes(one);
                var two = "hellohello";
                var twoBytes = Encoding.ASCII.GetBytes(two);

                await log.AppendAsync(context, cursor, oneBytes).ThrowIfFailure();
                await log.AppendAsync(context, cursor, oneBytes).ThrowIfFailure();

                var result = await log.ReadAsync(context, cursor).ThrowIfFailureAsync();
                Contract.Assert(Enumerable.SequenceEqual(result.Value.ToArray(), twoBytes));
            });
        }

        [Fact]
        public Task EntriesExpireAfterExpiryTest()
        {
            var lifetime = TimeSpan.FromSeconds(1);
            return RunTest(async (context, log) =>
            {
                var cursor = new BlockReference()
                {
                    LogId = new CheckpointLogId(0),
                    LogBlockId = 0,
                };

                var one = "hello";
                var oneBytes = Encoding.ASCII.GetBytes(one);
                await log.AppendAsync(context, cursor, oneBytes).ShouldBeSuccess();

                await log.ReadAsync(context, cursor).SelectResult(r => r.Value.Value.Length.Should().NotBe(0));
                await Task.Delay(lifetime, context.Token);
                await log.ReadAsync(context, cursor).SelectResult(o => o.Value.HasValue.Should().BeFalse());
            }, configuration: new RedisVolatileEventStorageConfiguration() {
                MaximumKeyLifetime = lifetime,
            });
        }

        [Fact]
        public Task MultipleAppendAndGcTest()
        {
            return RunTest(async (context, log) =>
            {
                var cursor0 = new BlockReference()
                {
                    LogId = new CheckpointLogId(0),
                    LogBlockId = 0,
                };

                var one = "hello";
                var oneBytes = Encoding.ASCII.GetBytes(one);
                var two = "hellohello";
                var twoBytes = Encoding.ASCII.GetBytes(two);

                await log.AppendAsync(context, cursor0, oneBytes).ThrowIfFailure();
                await log.AppendAsync(context, cursor0, oneBytes).ThrowIfFailure();

                var cursor1 = new BlockReference()
                {
                    LogId = new CheckpointLogId(1),
                    LogBlockId = 0,
                };

                await log.AppendAsync(context, cursor1, oneBytes).ThrowIfFailure();
                await log.AppendAsync(context, cursor1, oneBytes).ThrowIfFailure();

                var result = await log.ReadAsync(context, cursor1).ThrowIfFailureAsync();
                Contract.Assert(Enumerable.SequenceEqual(result.Value.ToArray(), twoBytes));

                // Simulate that events from cursor1 have reached Azure Storage
                await log.GarbageCollectAsync(context, cursor1).ThrowIfFailureAsync();

                await log.ReadAsync(context, cursor0).SelectResult(o => o.Value.HasValue.Should().BeFalse());
                await log.ReadAsync(context, cursor1).ShouldBeSuccess();
            });
        }

        [Fact]
        public Task MultipleBlocksTest()
        {
            return RunTest(async (context, log) =>
            {
                var cursor0 = new BlockReference()
                {
                    LogId = new CheckpointLogId(0),
                    LogBlockId = 0,
                };

                var cursor1 = new BlockReference()
                {
                    LogId = new CheckpointLogId(0),
                    LogBlockId = 1,
                };

                var one = "hello";
                var oneBytes = Encoding.ASCII.GetBytes(one);

                await log.AppendAsync(context, cursor0, oneBytes).ThrowIfFailure();
                await log.AppendAsync(context, cursor1, oneBytes).ThrowIfFailure();

                var result = await log.ReadAsync(context, cursor0).ThrowIfFailureAsync();
                Contract.Assert(Enumerable.SequenceEqual(result.Value.ToArray(), oneBytes));

                var result2 = await log.ReadAsync(context, cursor1).ThrowIfFailureAsync();
                Contract.Assert(Enumerable.SequenceEqual(result2.Value.ToArray(), oneBytes));
            });
        }

        private async Task RunTest(Func<OperationContext, RedisWriteAheadEventStorage, Task> runTestAsync, RedisVolatileEventStorageConfiguration configuration = null)
        {
            var tracingContext = new Context(Logger);
            var operationContext = new OperationContext(tracingContext);

            using var database = LocalRedisProcessDatabase.CreateAndStartEmpty(_redisFixture, TestGlobal.Logger, SystemClock.Instance);

            var primaryFactory = await RedisDatabaseFactory.CreateAsync(
                operationContext,
                new LiteralConnectionStringProvider(database.ConnectionString),
                new RedisConnectionMultiplexerConfiguration() { LoggingSeverity = Severity.Error });
            var primaryDatabaseAdapter = new RedisDatabaseAdapter(primaryFactory, "keyspace");

            configuration ??= new RedisVolatileEventStorageConfiguration();
            var instance = new RedisWriteAheadEventStorage(configuration, primaryDatabaseAdapter);

            await instance.StartupAsync(operationContext).ThrowIfFailure();

            await runTestAsync(operationContext, instance);

            await instance.ShutdownAsync(operationContext).ThrowIfFailure();
        }

        public override void Dispose()
        {
            _redisFixture.Dispose();
        }
    }
}
