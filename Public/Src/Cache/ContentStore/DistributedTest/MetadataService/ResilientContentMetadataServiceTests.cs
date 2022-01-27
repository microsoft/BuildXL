// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
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
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Utilities.Tracing;
using ContentStoreTest.Distributed.ContentLocation.NuCache;
using ContentStoreTest.Distributed.Redis;
using ContentStoreTest.Test;
using FluentAssertions;
using Microsoft.VisualBasic;
using Xunit;
using Xunit.Abstractions;

#nullable enable

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
            return RunTest(async (context, service, iteration) =>
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
            return RunTest(async (context, service, iteration) =>
            {
                // First heartbeat lets the service know its master, so it's willing to process requests
                await service.OnRoleUpdatedAsync(context, Role.Master);

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

        [Fact]
        public Task SimpleBlobPutAndGetTest()
        {
            return RunTest(async (context, service, iteration) =>
            {
                // First heartbeat lets the service know its master, so it's willing to process requests
                await service.OnRoleUpdatedAsync(context, Role.Master);

                var machineId = new MachineId(0);
                var data = ThreadSafeRandom.GetBytes((int)100);
                var contentHash = HashInfoLookup.GetContentHasher(HashType.Vso0).GetContentHash(data);

                var putResponse = await service.PutBlobAsync(new PutBlobRequest()
                {
                    ContextId = Guid.NewGuid().ToString(),
                    ContentHash = contentHash,
                    Blob = data
                });

                putResponse.Succeeded.Should().BeTrue();

                var getResponse = await service.GetBlobAsync(new GetBlobRequest()
                {
                    ContextId = Guid.NewGuid().ToString(),
                    ContentHash = contentHash,
                });

                getResponse.Succeeded.Should().BeTrue();
                getResponse.Blob.Should().NotBeNull();
                getResponse.Blob.Should().BeEquivalentTo(data);
            });
        }

        [Fact]
        public Task CheckpointMaxAgeIsRespected()
        {
            var clock = new MemoryClock();
            return RunTest(async (context, service, iteration) =>
            {
                if (iteration == 0)
                {
                    // First heartbeat lets the service know its master. This also restores the latest checkpoint and clears old ones if needed
                    await service.OnRoleUpdatedAsync(context, Role.Master);

                    // Create a checkpoint and make sure it shows up in the registry
                    await service.CreateCheckpointAsync(context).ShouldBeSuccess();

                    var r = await service.CheckpointManager.CheckpointRegistry.GetCheckpointStateAsync(context);
                    r.Succeeded.Should().BeTrue();
                    r.Value!.CheckpointAvailable.Should().BeTrue();
                }
                else if (iteration == 1)
                {
                    clock.Increment(TimeSpan.FromHours(0.5));

                    // First heartbeat lets the service know its master. This also restores the latest checkpoint and clears old ones if needed
                    await service.OnRoleUpdatedAsync(context, Role.Master);

                    var r = await service.CheckpointManager.CheckpointRegistry.GetCheckpointStateAsync(context);
                    r.Succeeded.Should().BeTrue();
                    r.Value!.CheckpointAvailable.Should().BeTrue();
                }
                else
                {
                    clock.Increment(TimeSpan.FromHours(1));

                    // First heartbeat lets the service know its master. This also restores the latest checkpoint and clears old ones if needed
                    await service.OnRoleUpdatedAsync(context, Role.Master);

                    var r = await service.CheckpointManager.CheckpointRegistry.GetCheckpointStateAsync(context);
                    r.Succeeded.Should().BeTrue();
                    r.Value!.CheckpointAvailable.Should().BeFalse();
                }
            },
            clock: clock,
            iterations: 3,
            modifyConfig: configuration =>
            {
                configuration.CheckpointMaxAge = TimeSpan.FromHours(1);
            });
        }

        private async Task RunTest(
            Func<OperationContext, ResilientGlobalCacheService, int, Task> runTestAsync,
            bool persistentStorageFailure = false,
            bool volatileStorageFailure = false,
            IClock? clock = null,
            int iterations = 1,
            Action<GlobalCacheServiceConfiguration>? modifyConfig = null)
        {
            var tracingContext = new Context(Logger);
            var operationContext = new OperationContext(tracingContext);

            clock ??= SystemClock.Instance;

            var contentMetadataServiceConfiguration = new GlobalCacheServiceConfiguration()
            {
                Checkpoint = new CheckpointManagerConfiguration(TestRootDirectoryPath / "CheckpointManager"),
                EventStream = new ContentMetadataEventStreamConfiguration(),
            };
            modifyConfig?.Invoke(contentMetadataServiceConfiguration);

            using var database = LocalRedisProcessDatabase.CreateAndStartEmpty(_redisFixture, TestGlobal.Logger, SystemClock.Instance);
            var primaryFactory = await RedisDatabaseFactory.CreateAsync(
                operationContext,
                new LiteralConnectionStringProvider(database.ConnectionString),
                new RedisConnectionMultiplexerConfiguration() { LoggingSeverity = Severity.Error });
            var primaryDatabaseAdapter = new RedisDatabaseAdapter(primaryFactory, "keyspace");

            var centralStorage = new Dictionary<string, byte[]>();

            for (var iteration = 0; iteration < iterations; iteration++)
            {
                Tracer.Info(operationContext, $"Running iteration {iteration}");

                var redisVolatileEventStorage = new RedisWriteAheadEventStorage(new RedisVolatileEventStorageConfiguration(), primaryDatabaseAdapter, clock);

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

                var rocksdbContentMetadataDatabaseConfiguration = new RocksDbContentMetadataDatabaseConfiguration(TestRootDirectoryPath / "ContentMetadataDatabase");
                var rocksDbContentMetadataStore = new RocksDbContentMetadataStore(clock, new RocksDbContentMetadataStoreConfiguration()
                {
                    Database = rocksdbContentMetadataDatabaseConfiguration,
                });

                var storage = new MockCentralStorage(centralStorage);
                var checkpointManager = new CheckpointManager(
                    rocksDbContentMetadataStore.Database,
                    redisVolatileEventStorage,
                    storage,
                    contentMetadataServiceConfiguration.Checkpoint,
                    new CounterCollection<ContentLocationStoreCounters>());
                var resilientContentMetadataService = new ResilientGlobalCacheService(
                    contentMetadataServiceConfiguration,
                    checkpointManager,
                    rocksDbContentMetadataStore,
                    contentMetadataEventStream,
                    storage,
                    clock);

                await resilientContentMetadataService.StartupAsync(operationContext).ThrowIfFailure();
                await runTestAsync(operationContext, resilientContentMetadataService, iteration);
                await resilientContentMetadataService.ShutdownAsync(operationContext).ThrowIfFailure();
            }
        }

        public override void Dispose()
        {
            _redisFixture.Dispose();
        }
    }
}
