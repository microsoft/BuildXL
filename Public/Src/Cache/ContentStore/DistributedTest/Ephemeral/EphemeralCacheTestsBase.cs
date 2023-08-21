// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.ContentStore.Distributed.Ephemeral;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.NuCache.ClusterStateManagement;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Auth;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.MemoizationStore.Distributed.Stores;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Core.Tasks;
using ContentStoreTest.Distributed.Redis;
using ContentStoreTest.Extensions;
using ContentStoreTest.Test;
using FluentAssertions;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.Cache.ContentStore.Distributed.Test.Ephemeral;

// These tests are currently for Cloudbuild-specific scenarios, so they are disabled on macOS for now.
[TestClassIfSupported(requiresWindowsOrLinuxOperatingSystem: true)]
public class BuildWideEphemeralCacheTests : EphemeralCacheTestsBase
{
    protected override Mode TestMode => Mode.BuildWide;

    public BuildWideEphemeralCacheTests(LocalRedisFixture fixture, ITestOutputHelper output)
        : base(fixture, output)
    {
    }

    [Fact]
    public Task MachinesFallbackToStorageWhenCopyingOutsideTheRing()
    {
        return RunTestAsync(
            async (context, host) =>
            {
                var r1 = host.Ring(0);
                var r1l = host.Instance(r1.Leader);
                var r1w = host.Instance(r1.Builders[1]);

                var r2 = host.Ring(1);
                var r2l = host.Instance(r2.Leader);
                var r2w = host.Instance(r2.Builders[1]);

                var putResult = await r1w.Session!.PutRandomAsync(context, HashType.Vso0, provideHash: true, size: 100, context.Token)
                    .ThrowIfFailureAsync();
                putResult.ShouldBeSuccess();

                var placeResult = await r2w.Session!.PlaceFileAsync(
                    context,
                    putResult.ContentHash,
                    host.TestDirectory.CreateRandomFileName(),
                    FileAccessMode.ReadOnly,
                    FileReplacementMode.ReplaceExisting,
                    FileRealizationMode.Any,
                    context.Token);
                placeResult.ShouldBeSuccess();
                placeResult.MaterializationSource.Should().Be(PlaceFileResult.Source.BackingStore);
            }, numRings: 2, instancesPerRing: 2);
    }


}

[Collection("Redis-based tests")]
public abstract class EphemeralCacheTestsBase : TestWithOutput
{
    private readonly LocalRedisFixture _fixture;

    protected readonly string RunId = ThreadSafeRandom.LowercaseAlphanumeric(10);

    public enum Mode
    {
        DatacenterWide,
        BuildWide,
    }

    protected abstract Mode TestMode { get; }

    protected EphemeralCacheTestsBase(LocalRedisFixture fixture, ITestOutputHelper output)
        : base(output)
    {
        _fixture = fixture;
    }

    [Fact]
    public Task MachineCanCopyFromOtherMachinesInsideRing()
    {
        return RunTestAsync(
            async (context, host) =>
            {
                var r1 = host.Ring(0);
                var r1l = host.Instance(r1.Leader);
                var r1w = host.Instance(r1.Builders[1]);
                var r1w2 = host.Instance(r1.Builders[2]);

                var putResult = await r1w.Session!.PutRandomAsync(context, HashType.Vso0, provideHash: true, size: 100, context.Token)
                    .ThrowIfFailureAsync();
                putResult.ShouldBeSuccess();

                var placeResult = await r1l.Session!.PlaceFileAsync(
                    context,
                    putResult.ContentHash,
                    host.TestDirectory.CreateRandomFileName(),
                    FileAccessMode.ReadOnly,
                    FileReplacementMode.ReplaceExisting,
                    FileRealizationMode.Any,
                    context.Token);
                placeResult.ShouldBeSuccess();
                placeResult.MaterializationSource.Should().Be(PlaceFileResult.Source.DatacenterCache);

                placeResult = await r1w2.Session!.PlaceFileAsync(
                   context,
                   putResult.ContentHash,
                   host.TestDirectory.CreateRandomFileName(),
                   FileAccessMode.ReadOnly,
                   FileReplacementMode.ReplaceExisting,
                   FileRealizationMode.Any,
                   context.Token);
                placeResult.ShouldBeSuccess();
                placeResult.MaterializationSource.Should().Be(PlaceFileResult.Source.DatacenterCache);
            }, instancesPerRing: 3);
    }

    protected async Task RunTestAsync(Func<OperationContext, TestInstance, Task> runTest, int numRings = 1, int instancesPerRing = 2)
    {
        Contract.Requires(numRings > 0);
        Contract.Requires(instancesPerRing > 0);
        var tracingContext = new Context(TestGlobal.Logger);
        var context = new OperationContext(tracingContext);

        var accounts = Enumerable.Range(0, 10).Select(idx => new BlobCacheStorageShardingAccountName(RunId, idx, "test"))
            .Cast<BlobCacheStorageAccountName>().ToList();
        var (process, secretsProvider) = AzureBlobStorageContentSessionTests.CreateTestTopology(_fixture, accounts);
        using var _ = process;

        var blobCacheConfiguration = new AzureBlobStorageCacheFactory.Configuration(
            ShardingScheme: new ShardingScheme(ShardingAlgorithm.JumpHash, accounts),
            Universe: RunId,
            Namespace: "test",
            RetentionPolicyInDays: null);

        var ephemeralManagementStorageCredentials = TestMode == Mode.DatacenterWide ? new SecretBasedAzureStorageCredentials(process.ConnectionString) : null;
        var host = new TestInstance(
            blobCacheConfiguration,
            secretsProvider,
            ephemeralManagementStorageCredentials);
        foreach (var ringNum in Enumerable.Range(0, numRings))
        {
            await host.AddRingAsync(context, ringNum.ToString(), instancesPerRing).ThrowIfFailureAsync();
        }

        try
        {
            await host.StartupAsync(context).ThrowIfFailureAsync();

            // Ensure all machines advertise themselves as Open
            await host.HearbeatAsync(context, MachineState.Open).ThrowIfFailureAsync();

            // Ensure all machines see that each other is Open
            await host.HearbeatAsync(context).ThrowIfFailureAsync();

            await runTest(context, host);
        }
        finally
        {
            await host.ShutdownAsync(context).ThrowIfFailureAsync();
        }
    }

    protected class TestNode : StartupShutdownComponentBase
    {
        protected override Tracer Tracer { get; } = new(nameof(TestNode));

        public MachineLocation Location { get; }

        public int Port { get; }

        public EphemeralHost Host { get; }

        public IFullCache Cache { get; }

        public ILocalContentTracker ContentTracker => Host.LocalContentTracker;

        public IDistributedContentTracker DistributedContentTracker => Host.DistributedContentTracker;

        public ClusterStateManager ClusterStateManager => Host.ClusterStateManager;

        public MachineId Id => Host.ClusterStateManager.ClusterState.PrimaryMachineId;

        public IContentSession? Session { get; private set; }

        public TestNode(MachineLocation location, int port, EphemeralHost host, IFullCache cache)
        {
            Location = location;
            Port = port;
            Host = host;
            Cache = cache;
            LinkLifetime(cache);
        }

        protected override Task<BoolResult> StartupComponentAsync(OperationContext context)
        {
            Session = ((ICache)Cache).CreateSession(context, "test", ImplicitPin.None).ThrowIfFailure().Session;
            return BoolResult.SuccessTask;
        }

        protected override async Task<BoolResult> ShutdownComponentAsync(OperationContext context)
        {
            if (Session is not null)
            {
                await Session!.ShutdownAsync(context).ThrowIfFailureAsync();
                Session = null;
            }

            return BoolResult.Success;
        }
    }

    protected class TestInstance : StartupShutdownComponentBase
    {
        protected override Tracer Tracer { get; } = new(nameof(TestInstance));

        private readonly Dictionary<MachineLocation, (TestNode Instance, BuildRing Ring)> _instances = new();

        private readonly Dictionary<string, BuildRing> _rings = new();

        public DisposableDirectory TestDirectory { get; }

        private readonly IClusterStateStorage _clusterStateStorage;
        private readonly AzureBlobStorageCacheFactory.Configuration _blobCacheConfiguration;
        private readonly IBlobCacheSecretsProvider _secretsProvider;
        private readonly IAzureStorageCredentials? _ephemeralManagementStorageCredentials;

        public TestInstance(AzureBlobStorageCacheFactory.Configuration blobCacheConfiguration, IBlobCacheSecretsProvider secretsProvider, IAzureStorageCredentials? ephemeralManagementStorageCredentials)
        {
            GrpcEnvironment.Initialize();
            var fileSystem = PassThroughFileSystem.Default;

            TestDirectory = new DisposableDirectory(fileSystem);
            _clusterStateStorage = new InMemoryClusterStateStorage();
            _blobCacheConfiguration = blobCacheConfiguration;
            _secretsProvider = secretsProvider;
            _ephemeralManagementStorageCredentials = ephemeralManagementStorageCredentials;
        }

        protected override async Task<BoolResult> ShutdownComponentAsync(OperationContext context)
        {
            var result = BoolResult.Success;
            while (_rings.Count > 0)
            {
                result &= await RemoveRingAsync(context, _rings.First().Key);
            }

            TestDirectory.Dispose();
            return result;
        }

        public IEnumerable<BuildRing> Rings => _rings.Values;

        public IEnumerable<TestNode> Instances => _instances.Values.Select(instance => instance.Instance);

        public BuildRing Ring(int id)
        {
            return Rings.ElementAt(id);
        }

        public BuildRing Ring(string id)
        {
            return _rings[id];
        }

        public TestNode Instance(MachineLocation location)
        {
            return _instances[location].Instance;
        }

        public async Task<BoolResult> HearbeatAsync(OperationContext context, MachineState state = MachineState.Unknown)
        {
            var tasks = _instances.Values.Select(instance => instance.Instance.ClusterStateManager.HeartbeatAsync(context, state));
            var results = await TaskUtilities.SafeWhenAll(tasks);
            return results.And();
        }

        public async Task<Result<BuildRing>> AddRingAsync(OperationContext context, string id, int numInstances)
        {
            var instances = await CreateTestRingAsync(context, numInstances);
            var ring = new BuildRing(id, instances.Select(instance => instance.Location).ToList());

            var tasks = instances.Select(instance => instance.StartupAsync(context));
            var results = await TaskUtilities.SafeWhenAll(tasks);
            var result = results.And();

            if (result.Succeeded)
            {
                _rings.Add(ring.Id, ring);
                _instances.AddRange(
                    instances.Select(
                        instance => new KeyValuePair<MachineLocation, (TestNode Instance, BuildRing Ring)>(
                            instance.Location,
                            (instance, ring))));

                return Result.Success(ring);
            }

            return new Result<BuildRing>(result);
        }

        public async Task<BoolResult> RemoveRingAsync(OperationContext context, string id)
        {
            if (!_rings.TryGetValue(id, out var ring))
            {
                return new BoolResult(errorMessage: $"Failed to find ring {id}");
            }

            _rings.Remove(id);

            var result = BoolResult.Success;
            foreach (var instance in ring.Builders.ToList())
            {
                result &= await RemoveNodeAsync(context, instance);
            }

            return result;
        }

        public async Task<BoolResult> RemoveNodeAsync(OperationContext context, MachineLocation location)
        {
            if (!_instances.TryGetValue(location, out var kvp))
            {
                return new BoolResult(errorMessage: $"Failed to find node {location}");
            }

            var (instance, ring) = kvp;

            var wasLeader = ring.Leader == location;
            ring.Remove(location);
            _instances.Remove(location);

            if (ring.Builders.Count == 0)
            {
                Tracer.Warning(context, $"Removed machine location {location} which was the last machine in ring {ring.Id}");
                _rings.Remove(ring.Id);
            }
            else if (wasLeader)
            {
                // We will allow this, because it's useful for testing.
                Tracer.Error(context, $"Removed machine location {location} which was the leader of ring {ring.Id}");
            }


            return await instance.ShutdownAsync(context);
        }

        private async Task<List<TestNode>> CreateTestRingAsync(
            OperationContext context,
            int numInstances)
        {
            var leader = MachineLocation.Invalid;
            var instances = new List<TestNode>(numInstances);
            foreach (var machineNum in Enumerable.Range(0, numInstances))
            {
                var port = PortExtensions.GetNextAvailablePort(context);
                var location = MachineLocation.Create(Environment.MachineName, port);
                if (!leader.IsValid)
                {
                    leader = location;
                }

                var (host, cache) = await CreateSingleInstanceAsync(context, location, leader);
                instances.Add(new TestNode(location, port, host, cache));
            }

            return instances;
        }

        private async Task<(EphemeralHost Host, IFullCache Cache)> CreateSingleInstanceAsync(OperationContext context, MachineLocation location, MachineLocation leader)
        {
            var persistentCache = AzureBlobStorageCacheFactory.Create(_blobCacheConfiguration, _secretsProvider) as IFullCache;
            Contract.Assert(persistentCache != null);

            EphemeralCacheFactory.Configuration? factoryConfiguration;
            if (_ephemeralManagementStorageCredentials is not null)
            {
                factoryConfiguration = new EphemeralCacheFactory.DatacenterWideCacheConfiguration
                {
                    RootPath = TestDirectory.CreateRandomFileName(),
                    Location = location,
                    Leader = leader,
                    StorageCredentials = _ephemeralManagementStorageCredentials,
                    MaxCacheSizeMb = 1024
                };
            }
            else
            {
                factoryConfiguration = new EphemeralCacheFactory.BuildWideCacheConfiguration()
                {
                    RootPath = TestDirectory.CreateRandomFileName(),
                    Location = location,
                    Leader = leader,
                    MaxCacheSizeMb = 1024
                };
            }


            var (host, cache) = await EphemeralCacheFactory.CreateInternalAsync(
                context,
                factoryConfiguration,
                persistentCache);

            return (host, cache);
        }
    }
}
