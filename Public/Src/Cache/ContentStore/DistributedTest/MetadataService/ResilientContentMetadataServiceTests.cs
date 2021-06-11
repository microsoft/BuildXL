// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Utilities.Tracing;
using ContentStoreTest.Distributed.ContentLocation.NuCache;
using ContentStoreTest.Distributed.Redis;
using ContentStoreTest.Test;
using FluentAssertions;
using Microsoft.VisualBasic;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.Cache.ContentStore.Distributed.Test.MetadataService
{
    [Collection("Redis-based tests")]
    public class ResilientContentMetadataServiceTests : TestBase, IDisposable
    {
        protected Tracer Tracer { get; } = new Tracer(nameof(ResilientContentMetadataServiceTests));

        private readonly LocalRedisFixture _redisFixture;

        public ResilientContentMetadataServiceTests(LocalRedisFixture redis, ITestOutputHelper output)
            : base(TestGlobal.Logger, output)
        {
            Contract.RequiresNotNull(redis);
            _redisFixture = redis;
        }

        [Fact]
        public Task UndefinedRoleDoesNotAnswerRequestsTest()
        {
            return RunTest(async (context, service) =>
            {
                // There haven't been any heartbeats yet, so we shouldn't be replying to requests
                var machineId = new MachineId(0);
                var contentHash = ContentHash.Random();
                var registerResponse = await service.RegisterContentLocationsAsync(new RegisterContentLocationsRequest()
                {
                    MachineId = machineId,
                    Hashes = new[] { new ShortHashWithSize(contentHash, 10) },
                });
                registerResponse.ShouldRetry.Should().BeTrue();
            });
        }

        [Fact]
        public Task SimpleRegisterAndGetTest()
        {
            return RunTest(async (context, service) =>
            {
                // First heartbeat lets the service know its master, so it's willing to process requests
                await service.OnSuccessfulHeartbeatAsync(context, Role.Master);

                var machineId = new MachineId(0);
                var contentHash = ContentHash.Random();
                var registerResponse = await service.RegisterContentLocationsAsync(new RegisterContentLocationsRequest() {
                    MachineId = machineId,
                    Hashes = new[] { new ShortHashWithSize(contentHash, 10) },
                });

                var getResponse = await service.GetContentLocationsAsync(new GetContentLocationsRequest()
                {
                    Hashes = new ShortHash[] { contentHash },
                });
                getResponse.Succeeded.Should().BeTrue();
                getResponse.Entries.Count.Should().Be(1);
                getResponse.Entries[0].Locations.Contains(machineId).Should().BeTrue();
            });
        }

        private async Task RunTest(
            Func<OperationContext, ResilientContentMetadataService, Task> runTestAsync,
            bool persistentStorageFailure = false,
            bool volatileStorageFailure = false,
            IClock clock = null)
        {
            var tracingContext = new Context(Logger);
            var operationContext = new OperationContext(tracingContext);

            clock ??= SystemClock.Instance;

            var contentMetadataServiceConfiguration = new ContentMetadataServiceConfiguration()
            {
                Checkpoint = new CheckpointManagerConfiguration(TestRootDirectoryPath / "CheckpointManager"),
                EventStream = new ContentMetadataEventStreamConfiguration(),
                VolatileEventStorage = new RedisVolatileEventStorageConfiguration(),
            };

            using var database = LocalRedisProcessDatabase.CreateAndStartEmpty(_redisFixture, TestGlobal.Logger, SystemClock.Instance);
            var primaryFactory = await RedisDatabaseFactory.CreateAsync(
                operationContext,
                new LiteralConnectionStringProvider(database.ConnectionString),
                new RedisConnectionMultiplexerConfiguration() { LoggingSeverity = Severity.Error });
            var primaryDatabaseAdapter = new RedisDatabaseAdapter(primaryFactory, "keyspace");
            var redisVolatileEventStorage = new RedisWriteAheadEventStorage(contentMetadataServiceConfiguration.VolatileEventStorage, primaryDatabaseAdapter, clock);

            IWriteAheadEventStorage volatileEventStorage = new FailingVolatileEventStorage();
            if (!volatileStorageFailure)
            {
                volatileEventStorage = redisVolatileEventStorage;
            }

            IWriteBehindEventStorage persistentEventStorage = new FailingPersistentEventStorage();
            if (!persistentStorageFailure)
            {
                persistentEventStorage = new MockPersistentEventStorage();
            }

            var contentMetadataEventStream = new ContentMetadataEventStream(
                contentMetadataServiceConfiguration.EventStream,
                volatileEventStorage,
                persistentEventStorage);


            var rocksDbContentLocationDatabaseConfiguration = new RocksDbContentLocationDatabaseConfiguration(TestRootDirectoryPath / "ContentMetadataDatabase");
            var rocksDbContentMetadataStore = new RocksDbContentMetadataStore(clock, new RocksDbContentMetadataStoreConfiguration() {
                Database = rocksDbContentLocationDatabaseConfiguration,
            });

            var checkpointManager = new CheckpointManager(
                rocksDbContentMetadataStore.Database,
                redisVolatileEventStorage,
                new MockCentralStorage(),
                contentMetadataServiceConfiguration.Checkpoint,
                new CounterCollection<ContentLocationStoreCounters>());
            var resilientContentMetadataService = new ResilientContentMetadataService(
                contentMetadataServiceConfiguration,
                checkpointManager,
                rocksDbContentMetadataStore,
                contentMetadataEventStream,
                clock);

            await resilientContentMetadataService.StartupAsync(operationContext).ThrowIfFailure();
            await runTestAsync(operationContext, resilientContentMetadataService);
            await resilientContentMetadataService.ShutdownAsync(operationContext).ThrowIfFailure();
        }

        public override void Dispose()
        {
            _redisFixture.Dispose();
        }
    }
}
