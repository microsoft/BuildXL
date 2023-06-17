// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#if NET6_0_OR_GREATER

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.ContentStore.Distributed.Ephemeral;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.NuCache.ClusterStateManagement;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Grpc;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Launcher.Server;
using BuildXL.Utilities.Core;
using ContentStoreTest.Extensions;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Test.Ephemeral
{
    public class DistributedContentTrackerTests
    {
        [Fact]
        public Task LeaderDoesntMakeWorkerAwareOfChanges()
        {
            return RunTestAsync(
                async (context, host) =>
                {
                    var ring = host.Rings[0];
                    var leader = host.Instances[ring.Leader];

                    var content = new ContentHashWithSize(ContentHash.Random(), 100);

                    await leader.DistributedContentTracker.ProcessLocalChangeAsync(context, ChangeStampOperation.Add, content);
                    leader.ContentTracker.GetSequenceNumber(content.Hash, leader.Id).Should().Be(new SequenceNumber(1), "Sequence number must increment when doing a mutation");

                    int unseen = 0;
                    foreach (var instance in host.Instances.Values)
                    {
                        var seen = instance.ContentTracker.GetSequenceNumber(content.Hash, leader.Id) == new SequenceNumber(1);
                        if (!seen)
                        {
                            unseen++;
                        }
                    }

                    unseen.Should().BeGreaterOrEqualTo(4, "The update should have been gossipped to 6 machines in total.");
                },
                // WARNING: because of the distributed hash table, we need to use enough workers that at least one of
                // them can be guaranteed not to know.
                instancesPerRing: 10);
        }

        [Fact]
        public Task WorkerMakesLeaderAwareOfChanges()
        {
            return RunTestAsync(
                async (context, host) =>
                {
                    var ring = host.Rings[0];
                    var leader = host.Instances[ring.Leader];
                    var worker = host.Instances[ring.Builders[1]];

                    var content = new ContentHashWithSize(ContentHash.Random(), 100);

                    await worker.DistributedContentTracker.ProcessLocalChangeAsync(context, ChangeStampOperation.Add, content);
                    worker.ContentTracker.GetSequenceNumber(content.Hash, worker.Id).Should().Be(new SequenceNumber(1));
                    leader.ContentTracker.GetSequenceNumber(content.Hash, worker.Id).Should().Be(new SequenceNumber(1));

                    var entry = await leader.ContentTracker.GetSingleLocationAsync(context, content.Hash).ThrowIfFailureAsync();
                    entry.Size.Should().Be(content.Size);
                    entry.Contains(worker.Id).Should().BeTrue("The worker added the content and should have notified the leader");

                    await worker.DistributedContentTracker.ProcessLocalChangeAsync(context, ChangeStampOperation.Delete, content);
                    worker.ContentTracker.GetSequenceNumber(content.Hash, worker.Id).Should().Be(new SequenceNumber(2));
                    leader.ContentTracker.GetSequenceNumber(content.Hash, worker.Id).Should().Be(new SequenceNumber(2));

                    entry = await leader.ContentTracker.GetSingleLocationAsync(context, content.Hash).ThrowIfFailureAsync();
                    entry.Size.Should().Be(content.Size);
                    entry.Tombstone(worker.Id).Should().BeTrue("The worker deleted the content and should have notified the leader");
                });
        }

        [Fact]
        public Task DistributedHashTableIsMadeAwareOfChanges()
        {
            return RunTestAsync(
                async (context, host) =>
                {
                    var r1 = host.Rings[0];
                    var r1l = host.Instances[r1.Leader];
                    var r1w = host.Instances[r1.Builders[1]];

                    var r2 = host.Rings[1];
                    var r2l = host.Instances[r2.Leader];
                    var r2w = host.Instances[r2.Builders[1]];

                    var content = new ContentHashWithSize(ContentHash.Random(), 100);

                    await r1w.DistributedContentTracker.ProcessLocalChangeAsync(context, ChangeStampOperation.Add, content);

                    var entry = await r2w.DistributedContentTracker.GetSingleLocationAsync(context, content.Hash).ThrowIfFailureAsync();
                    entry.Size.Should().Be(content.Size);
                    entry.Contains(r1w.Id).Should().BeTrue($"{nameof(r1w)} added a piece of content, so {nameof(r2w)} should be able to look it up via the DHT");
                },
                numRings: 2);
        }

        [Fact]
        public Task UpdatesArePropagatedFromLocalContentStore()
        {
            return RunTestAsync(
                async (context, host) =>
                {
                    var r1 = host.Rings[0];
                    var r1l = host.Instances[r1.Leader];
                    var r1w = host.Instances[r1.Builders[1]];

                    var r2 = host.Rings[1];
                    var r2l = host.Instances[r2.Leader];
                    var r2w = host.Instances[r2.Builders[1]];

                    var putResult = await r1w.ContentSession!.PutRandomAsync(context, HashType.Vso0, provideHash: true, size: 100, context.Token).ThrowIfFailureAsync();

                    var entry = await r2w.DistributedContentTracker.GetSingleLocationAsync(context, putResult.ContentHash).ThrowIfFailureAsync();
                    entry.Size.Should().Be(putResult.ContentSize);
                    entry.Contains(r1w.Id).Should().BeTrue($"{nameof(r1w)} added a piece of content, so {nameof(r2w)} should be able to look it up via the DHT");

                    var evictResult = await r1w.ContentStore.DeleteAsync(context, putResult.ContentHash, null);

                    entry = await r2w.DistributedContentTracker.GetSingleLocationAsync(context, putResult.ContentHash).ThrowIfFailureAsync();
                    entry.Size.Should().Be(putResult.ContentSize);
                    entry.Tombstone(r1w.Id).Should().BeTrue($"{nameof(r1w)} removed a piece of content, so {nameof(r2w)} should be able to see the tombstone");
                },
                numRings: 2);
        }

        private static async Task RunTestAsync(Func<OperationContext, TestHost, Task> runTest, int numRings = 1, int instancesPerRing = 2)
        {
            Contract.Requires(numRings > 0);
            Contract.Requires(instancesPerRing > 0);
            var tracingContext = new Context(TestGlobal.Logger);
            var context = new OperationContext(tracingContext);
            var host = await TestHost.CreateAsync(context, numRings, instancesPerRing);

            try
            {
                await host.StartupAsync(context).ThrowIfFailureAsync();

                // Ensure all machines advertise themselves as Open
                foreach (var machine in host.Instances.Values)
                {
                    await machine.ClusterStateManager.HeartbeatAsync(context, MachineState.Open).ThrowIfFailureAsync();
                }

                // Ensure all machines see that each other is Open
                foreach (var machine in host.Instances.Values)
                {
                    await machine.ClusterStateManager.HeartbeatAsync(context, MachineState.Unknown).ThrowIfFailureAsync();
                }

                await runTest(context, host);
            }
            finally
            {
                await host.ShutdownAsync(context).ThrowIfFailureAsync();
            }
        }

        private class TestInstance : StartupShutdownComponentBase
        {
            protected override Tracer Tracer { get; } = new(nameof(TestInstance));

            public ILocalContentTracker ContentTracker { get; }

            public DistributedContentTracker DistributedContentTracker { get; }

            public ClusterStateManager ClusterStateManager { get; }

            public MachineId Id => ClusterStateManager.ClusterState.PrimaryMachineId;

            public MachineLocation Location
            {
                get
                {
                    if (!ClusterStateManager.ClusterState.TryResolve(Id, out var location))
                    {
                        throw new InvalidOperationException($"Could not resolve location for {Id}");
                    }

                    return location;
                }
            }

            private readonly ProtobufNetGrpcServiceEndpoint<IGrpcContentTracker, GrpcContentTrackerService> _endpoint;

            private readonly int _port;

            public IContentStore ContentStore { get; }

            public IContentSession? ContentSession { get; private set; } = null;

            private readonly GrpcDotNetInitializer _initializer = new();

            public TestInstance(
                ILocalContentTracker contentTracker,
                DistributedContentTracker distributedContentTracker,
                ClusterStateManager clusterStateManager,
                ProtobufNetGrpcServiceEndpoint<IGrpcContentTracker, GrpcContentTrackerService> endpoint,
                int port,
                IContentStore contentStore)
            {
                ContentTracker = contentTracker;
                DistributedContentTracker = distributedContentTracker;
                ClusterStateManager = clusterStateManager;
                _endpoint = endpoint;
                _port = port;
                ContentStore = contentStore;

                LinkLifetime(ClusterStateManager);
                LinkLifetime(DistributedContentTracker);
                LinkLifetime(_endpoint);
                LinkLifetime(ContentStore);
            }

            protected override async Task<BoolResult> StartupComponentAsync(OperationContext context)
            {
                await _initializer.StartAsync(context, _port, GrpcDotNetServerOptions.Default, new[] { _endpoint }).ThrowIfFailureAsync();

                ContentSession = ContentStore.CreateSession(context, "TestSession", ImplicitPin.None).ThrowIfFailure().Session;

                await ContentSession!.StartupAsync(context).ThrowIfFailureAsync();

                return BoolResult.Success;
            }

            protected override async Task<BoolResult> ShutdownComponentAsync(OperationContext context)
            {
                await _initializer.StopAsync(context, _port).ThrowIfFailureAsync();

                await ContentSession!.ShutdownAsync(context).ThrowIfFailureAsync();

                return BoolResult.Success;
            }

            public Task MarkClosedAsync(OperationContext context)
            {
                return ClusterStateManager.HeartbeatAsync(context, MachineState.Closed).ThrowIfFailureAsync();
            }
        }

        private class TestHost : StartupShutdownComponentBase
        {
            protected override Tracer Tracer { get; } = new(nameof(TestHost));

            public IReadOnlyDictionary<MachineLocation, TestInstance> Instances { get; }

            public IReadOnlyList<BuildRing> Rings { get; }

            public DisposableDirectory TestDirectory { get; }

            private TestHost(IReadOnlyDictionary<MachineLocation, TestInstance> instances, IReadOnlyList<BuildRing> rings, DisposableDirectory testDirectory)
            {
                Instances = instances;
                Rings = rings;
                TestDirectory = testDirectory;

                foreach (var instance in instances.Values)
                {
                    LinkLifetime(instance);
                }
            }

            protected override Task<BoolResult> ShutdownComponentAsync(OperationContext context)
            {
                TestDirectory.Dispose();
                return BoolResult.SuccessTask;
            }

            public static async Task<TestHost> CreateAsync(OperationContext context, int numRings, int instancesPerRing)
            {
                var testDirectory = new DisposableDirectory(PassThroughFileSystem.Default);

                var clusterStateStorage = new InMemoryClusterStateStorage();

                var rings = new List<BuildRing>();
                var instances = new Dictionary<MachineLocation, TestInstance>();
                foreach (var _ringId in Enumerable.Range(0, numRings))
                {
                    var ringMachines = new List<MachineLocation>();
                    foreach (var machineNum in Enumerable.Range(0, instancesPerRing))
                    {
                        var port = PortExtensions.GetNextAvailablePort(context);
                        var machineName = MachineLocation.Create(Environment.MachineName, port);
                        ringMachines.Add(machineName);

                        instances[machineName] = await CreateTestInstanceAsync(
                            context,
                            port,
                            ringMachines: ringMachines,
                            role: machineNum == 0 ? Role.Master : Role.Worker,
                            location: machineName,
                            clusterStateStorage: clusterStateStorage,
                            rootPath: testDirectory.CreateRandomFileName());
                    }

                    var ring = new BuildRing(ringMachines);
                    rings.Add(ring);
                }

                return new TestHost(instances, rings, testDirectory);
            }

            private static Task<TestInstance> CreateTestInstanceAsync(
                OperationContext context,
                int port,
                List<MachineLocation> ringMachines,
                Role role,
                MachineLocation location,
                IClusterStateStorage clusterStateStorage,
                AbsolutePath rootPath)
            {
                var localContentTracker = new LocalContentTracker();
                var information = new RiggedMasterElectionMechanism(ringMachines[0], role);

                var clusterStateManagerConfiguration = new ClusterStateManager.Configuration
                {
                    PrimaryLocation = location,
                    UpdateInterval = TimeSpan.FromMilliseconds(100),
                };
                var distributedContentTrackerConfiguration = new DistributedContentTracker.Configuration();

                var clusterStateManager = new ClusterStateManager(clusterStateManagerConfiguration, clusterStateStorage, clock: SystemClock.Instance);

                var shardManager = new ClusterStateShardManager(clusterStateManager.ClusterState);
                var shardingScheme = new RendezvousConsistentHash<MachineId>(shardManager, id => HashCodeHelper.GetHashCode(id.Index));

                var grpcContentTrackerClientConfiguration = new GrpcContentTrackerClient.Configuration(TimeSpan.FromMinutes(1), RetryPolicyConfiguration.Exponential());

                var grpcConnectionPoolConfiguration = new ConnectionPoolConfiguration
                {
                    ConnectTimeout = TimeSpan.FromSeconds(1),
                    DefaultPort = GrpcConstants.DefaultGrpcPort,
                    UseGrpcDotNet = true,
                    GrpcDotNetOptions = new GrpcDotNetClientOptions(),
                };
                var connectionPool = new GrpcConnectionPool(grpcConnectionPoolConfiguration, context, clock: SystemClock.Instance);

                var clientAccessor = new GenericGrpcClientAccessor<IGrpcContentTracker, IContentTracker>(connectionPool, service => new GrpcContentTrackerClient(grpcContentTrackerClientConfiguration, new FixedClientAccessor<IGrpcContentTracker>(service)));

                var distributedContentTracker = new DistributedContentTracker(
                    distributedContentTrackerConfiguration,
                    clusterStateManager.ClusterState,
                    shardingScheme,
                    localContentTracker,
                    clientAccessor,
                    information,
                    clock: SystemClock.Instance);

                var service = new GrpcContentTrackerService(localContentTracker);
                var contentTrackerEndpoint = new ProtobufNetGrpcServiceEndpoint<IGrpcContentTracker, GrpcContentTrackerService>(nameof(GrpcContentTrackerService), service);

                var contentStoreConfiguration = ContentStoreConfiguration.CreateWithMaxSizeQuotaMB(1000);
                var configurationModel = new ConfigurationModel(contentStoreConfiguration, ConfigurationSelection.RequireAndUseInProcessConfiguration, MissingConfigurationFileOption.DoNotWrite);
                var contentStoreSettings = ContentStoreSettings.DefaultSettings;
                var contentStore = new FileSystemContentStore(PassThroughFileSystem.Default, clock: SystemClock.Instance, rootPath, configurationModel, distributedStore: null, settings: contentStoreSettings);
                contentStore.Store.Announcer = new FileSystemNotificationReceiver(distributedContentTracker);

                //var distributedContentCopierConfiguration = new DistributedContentCopier.Configuration()
                //                                            {

                //                                            };
                //var remoteFileCopier = new GrpcFileCopier(context, new GrpcFileCopierConfiguration()
                //                                                                 {

                //                                                                 });
                //var contentCopier = new DistributedContentCopier(
                //    distributedContentCopierConfiguration,
                //    PassThroughFileSystem.Default,
                //    remoteFileCopier,
                //    copyRequester: remoteFileCopier,
                //    SystemClock.Instance,
                //    context.TracingContext.Logger);

                // TODO: need a server that allows copying files.

                return Task.FromResult(new TestInstance(localContentTracker, distributedContentTracker, clusterStateManager, contentTrackerEndpoint, port, contentStore));
            }
        }
    }
}

#endif
