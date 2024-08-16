// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.ContentStore.Distributed.Ephemeral;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Auth;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.Logging;
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
using Xunit;
using Xunit.Abstractions;
using IContentResolver = BuildXL.Cache.ContentStore.Distributed.Ephemeral.IContentResolver;
// ReSharper disable ExplicitCallerInfoArgument

namespace BuildXL.Cache.ContentStore.Distributed.Test.Ephemeral;

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
            async (context, silentContext, host) =>
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

                r1l.LocalChangeProcessor.Counters[LocalChangeProcessor.Counter.ProcessLocalChangeCalls].Value.Should().Be(1);
                r1l.LocalChangeProcessor.Counters[LocalChangeProcessor.Counter.ProcessLocalAdd].Value.Should().Be(1);
                r1l.LocalChangeProcessor.Counters[LocalChangeProcessor.Counter.ProcessLocalDelete].Value.Should().Be(0);
            }, instancesPerRing: 3);
    }

    [Fact]
    public Task PersistentLocationIsTracked()
    {
        return RunTestAsync(
            async (context, silentContext, host) =>
            {
                var r1 = host.Ring(0);
                var r1l = host.Instance(r1.Leader);
                var r1w = host.Instance(r1.Builders[1]);

                var putResult = await r1w.Session!.PutRandomAsync(context, HashType.Vso0, provideHash: true, size: 100, context.Token)
                    .ThrowIfFailureAsync();
                putResult.ShouldBeSuccess();

                var locations = await r1l.ContentResolver.GetSingleLocationAsync(context, putResult.ContentHash).ThrowIfFailureAsync();
                bool hasPersistent = false;
                foreach (var location in locations.Existing())
                {
                    r1l.ClusterStateManager.ClusterState.QueryableClusterState.RecordsByMachineId.TryGetValue(location, out var record).Should().BeTrue();
                    hasPersistent |= record!.Persistent;
                }
                hasPersistent.Should().BeTrue();
            }, instancesPerRing: 2);
    }

    [Fact]
    public Task QueryElisionPreventsTraffic()
    {
        return RunTestAsync(
            async (context, silentContext, host) =>
            {
                var r1 = host.Ring(0);
                var r1l = host.Instance(r1.Leader);
                var r1w1 = host.Instance(r1.Builders[1]);
                var r1w2 = host.Instance(r1.Builders[2]);

                var putResult = await r1w1.Session!.PutRandomAsync(context, HashType.Vso0, provideHash: true, size: 100, context.Token)
                    .ThrowIfFailureAsync();
                putResult.ShouldBeSuccess();

                var e1 = await r1w2.ContentResolver.GetSingleLocationAsync(context, putResult.ContentHash).ThrowIfFailureAsync();
                e1.LastAuthoritativeUpdate.Should().BeAfter(DateTime.MinValue);

                // The last authoritative update shouldn't be updated, because we should have elided the query
                var e2 = await r1w2.ContentResolver.GetSingleLocationAsync(context, putResult.ContentHash).ThrowIfFailureAsync();
                e1.LastAuthoritativeUpdate.Should().Be(e2.LastAuthoritativeUpdate);
            },
            numRings: 1,
            instancesPerRing: 3,
            modifier: configuration =>
                      {
                          return configuration with
                          {
                              MaximumLeaderStaleness = TimeSpan.FromSeconds(5),
                              MaximumWorkerStaleness = TimeSpan.FromSeconds(5),
                          };
                      });
    }

    protected virtual IDisposable? CreateBackingContentStore(out IContentStore? backingContentStore)
    {
        backingContentStore = default;
        return null;
    }

    protected async Task RunTestAsync(Func<OperationContext, OperationContext, TestInstance, Task> runTest, int numRings = 1, int instancesPerRing = 2, TestInstance.ConfigurationModifier? modifier = null)
    {
        Contract.Requires(numRings > 0);
        Contract.Requires(instancesPerRing > 0);
        var tracingContext = new Context(TestGlobal.Logger);
        var context = new OperationContext(tracingContext);

        var accounts = Enumerable.Range(0, 10).Select(idx => new BlobCacheStorageShardingAccountName(RunId, idx, "test"))
            .Cast<BlobCacheStorageAccountName>().ToList();
        var (process, secretsProvider, buildCacheConfiguration) = AzureBlobStorageContentSessionTests.CreateTestTopology(_fixture, accounts, usePreauthenticatedUris: true);
        using var _ = process;

        var blobCacheConfiguration = new AzureBlobStorageCacheFactory.Configuration(
            ShardingScheme: new ShardingScheme(ShardingAlgorithm.JumpHash, accounts),
            Universe: RunId,
            Namespace: "test",
            RetentionPolicyInDays: null,
            IsReadOnly: false)
        {
            BuildCacheConfiguration = buildCacheConfiguration
        };

        using var _1 = CreateBackingContentStore(out var backingContentStore);

        var host = new TestInstance(
            blobCacheConfiguration,
            secretsProvider,
            universe: RunId,
            datacenterWide: TestMode == Mode.DatacenterWide,
            backingContentStore);

        var silentTracingContext = new Context(NullLogger.Instance);
        var silentContext = new OperationContext(silentTracingContext, context.Token);

        var setupContext = silentContext.CreateNested(nameof(RunTestAsync), caller: "SetupTestContext");
        var startupContext = silentContext.CreateNested(nameof(RunTestAsync), caller: "StartupTestContext");
        foreach (var ringNum in Enumerable.Range(0, numRings))
        {
            // TODO: parallelize
            await host.AddRingAsync(setupContext, ringNum.ToString(), instancesPerRing, modifier).ThrowIfFailureAsync();
        }

        try
        {
            await host.StartupAsync(startupContext).ThrowIfFailureAsync();

            // Ensure all machines see each other
            await host.HearbeatAsync(silentContext).ThrowIfFailureAsync();

            foreach (var entry in host.Instances.First().ClusterStateManager.ClusterState.QueryableClusterState.RecordsByMachineLocation.OrderBy(record => record.Value.Id.Index))
            {
                context.TracingContext.Info($"Machine {entry.Key} maps to {entry.Value}", component: "RunTestContext", operation: "LogMachineMapping");
            }

            var testContext = context.CreateNested(nameof(RunTestAsync), caller: "RunTestContext");
            await runTest(testContext, silentContext, host);
        }
        finally
        {
            var shutdownContext = silentContext.CreateNested(nameof(RunTestAsync), caller: "ShutdownTestContext");
            await host.ShutdownAsync(shutdownContext).ThrowIfFailureAsync();
        }
    }

    protected class TestNode : StartupShutdownComponentBase
    {
        protected override Tracer Tracer { get; } = new(nameof(TestNode));

        public MachineLocation Location { get; }

        public int Port { get; }

        public EphemeralHost Host { get; }

        public IFullCache Cache { get; }

        public OperationContext MachineContext { get; }

        public ILocalContentTracker ContentTracker => Host.LocalContentTracker;

        public IContentResolver ContentResolver => Host.ContentResolver;

        public LocalChangeProcessor LocalChangeProcessor => Host.LocalChangeProcessor;

        public ClusterStateManager ClusterStateManager => Host.ClusterStateManager;

        public MachineId Id => Host.ClusterStateManager.ClusterState.PrimaryMachineId;

        public IContentSession? Session { get; private set; }

        public TestNode(OperationContext machineContext, MachineLocation location, int port, EphemeralHost host, IFullCache cache)
        {
            Location = location;
            Port = port;
            Host = host;
            Cache = cache;
            MachineContext = machineContext;
            LinkLifetime(cache);
        }

        protected override Task<BoolResult> StartupComponentAsync(OperationContext context)
        {
            Session = ((ICache)Cache).CreateSession(context, "test", ImplicitPin.None).ThrowIfFailure().Session;
            return Session!.StartupAsync(context);
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

        private readonly AzureBlobStorageCacheFactory.Configuration _blobCacheConfiguration;
        private readonly IBlobCacheContainerSecretsProvider _secretsProvider;

        private readonly string _universe;
        private readonly bool _datacenterWide;
        private readonly IContentStore? _backingContentStore;

        public TestInstance(
            AzureBlobStorageCacheFactory.Configuration blobCacheConfiguration,
            IBlobCacheContainerSecretsProvider secretsProvider,
            string universe,
            bool datacenterWide,
            IContentStore? backingContentStore)
        {
            GrpcEnvironment.Initialize();
            var fileSystem = PassThroughFileSystem.Default;

            TestDirectory = new DisposableDirectory(fileSystem);
            _blobCacheConfiguration = blobCacheConfiguration;
            _secretsProvider = secretsProvider;
            _universe = universe;
            _datacenterWide = datacenterWide;
            _backingContentStore = backingContentStore;
        }

        protected override async Task<BoolResult> ShutdownComponentAsync(OperationContext context)
        {
            var result = BoolResult.Success;
            while (_rings.Count > 0)
            {
                // TODO: prallelize
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
            var tasks = _instances.Values.Select(
                async instance =>
                {
                    await Task.Yield();
                    using var boundContext = instance.Instance.MachineContext.WithCancellationToken(context.Token);
                    return await instance.Instance.ClusterStateManager.HeartbeatAsync(
                        boundContext,
                        state);
                });
            var results = await TaskUtilities.SafeWhenAll(tasks);
            return results.And();
        }

        public async Task<Result<BuildRing>> AddRingAsync(OperationContext context, string id, int numInstances, ConfigurationModifier? modifier = null)
        {
            // TODO: operation and cleanup

            var leader = MachineLocation.Invalid;
            var instances = new List<TestNode>(numInstances);
            foreach (var machineNum in Enumerable.Range(0, numInstances))
            {
                var port = PortExtensions.GetNextAvailablePort(context);
                var location = MachineLocation.Create(Environment.MachineName, port);

                var machineContext = context.CreateNested(nameof(AddRingAsync), caller: $"CreateMachineContext({machineNum})");
                Tracer.Warning(machineContext, $"This context was created for {machineNum} -> {location}");

                if (!leader.IsValid)
                {
                    leader = location;
                }
                Contract.Assert(leader.IsValid);

                // TODO: parallelize
                var (host, cache) = await CreateSingleInstanceAsync(machineContext, location, leader, modifier);
                instances.Add(new TestNode(machineContext, location, port, host, cache));
            }

            var ring = new BuildRing(id, instances.Select(instance => instance.Location).ToList());

            var tasks = instances.Select(instance => instance.StartupAsync(instance.MachineContext));
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

            // Start by removing non-leader machines first. This is crucial for the build-wide cache because the leader
            // hosts
            var builders = ring.Builders.ToList();
            builders.Reverse();

            foreach (var instance in builders)
            {
                // TODO: parallelize
                result &= await RemoveNodeAsync(context, instance);
            }

            return result;
        }

        public async Task<BoolResult> RemoveNodeAsync(OperationContext context, MachineLocation location)
        {
            // TODO: operation and cleanup
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
                Tracer.Warning(instance.MachineContext, $"Removed machine location {location} which was the last machine in ring {ring.Id}");
                _rings.Remove(ring.Id);
            }
            else if (wasLeader)
            {
                // We will allow this, because it's useful for testing.
                Tracer.Error(instance.MachineContext, $"Removed machine location {location} which was the leader of ring {ring.Id}");
            }


            using var boundContext = instance.MachineContext.WithCancellationToken(context.Token);
            return await instance.ShutdownAsync(boundContext);
        }

        private async Task<List<TestNode>> CreateTestRingAsync(
            OperationContext context,
            int numInstances,
            ConfigurationModifier? modifier)
        {
            MachineLocation? leader = null;
            var instances = new List<TestNode>(numInstances);
            foreach (var machineNum in Enumerable.Range(0, numInstances))
            {
                var port = PortExtensions.GetNextAvailablePort(context);
                var location = MachineLocation.Create(Environment.MachineName, port);
                if (leader is null)
                {
                    leader = location;
                }

                // TODO: parallelize

                var (host, cache) = await CreateSingleInstanceAsync(context, location, leader.Value, modifier);
                instances.Add(new TestNode(context, location, port, host, cache));
            }

            return instances;
        }

        public delegate EphemeralCacheFactory.Configuration ConfigurationModifier(EphemeralCacheFactory.Configuration configuration);

        private async Task<(EphemeralHost Host, IFullCache Cache)> CreateSingleInstanceAsync(
            OperationContext context,
            MachineLocation location,
            MachineLocation leader,
            ConfigurationModifier? modifier)
        {
            var persistentResult = AzureBlobStorageCacheFactory.Create(context, _blobCacheConfiguration, _secretsProvider);
            var persistentCache = persistentResult.Cache;
            Contract.Assert(persistentCache != null);

            EphemeralCacheFactory.Configuration? factoryConfiguration;
            if (_datacenterWide)
            {
                factoryConfiguration = new EphemeralCacheFactory.DatacenterWideCacheConfiguration
                {
                    RootPath = TestDirectory.CreateRandomFileName(),
                    Location = location,
                    Leader = leader,
                    MaxCacheSizeMb = 1024,
                    Universe = _universe,
                };
            }
            else
            {
                factoryConfiguration = new EphemeralCacheFactory.BuildWideCacheConfiguration()
                {
                    RootPath = TestDirectory.CreateRandomFileName(),
                    Location = location,
                    Leader = leader,
                    MaxCacheSizeMb = 1024,
                };
            }

            factoryConfiguration = factoryConfiguration with
            {
                // When running in production, these operations happen in very few milliseconds because the code
                // paths are warmed up. When running in tests, we'll often have 1 or 2 of these, so we'll always
                // fall into the worst-case times. If we use tight timeouts, we'll cause tests to fail.
                ConnectionTimeout = TimeSpan.FromSeconds(30),
                GetLocationsTimeout = TimeSpan.FromSeconds(1),
                UpdateLocationsTimeout = TimeSpan.FromSeconds(1),
                // These features are used in production but not in tests (by default). The reason is that we want to
                // ensure consistency in tests, and these features allow query elision and therefore allow stale data
                // to be returned in a given window of time.
                MaximumLeaderStaleness = TimeSpan.Zero,
                MaximumWorkerStaleness = TimeSpan.Zero,
                // Inline change processing to ensure that all changes are processed before we return from methods.
                TestInlineChangeProcessing = true,
            };

            var modified = modifier?.Invoke(factoryConfiguration);
            Contract.Assert(modified != null || modifier == null);
            if (modified is not null)
            {
                factoryConfiguration = modified;
            }

            var result = await EphemeralCacheFactory.CreateAsync(
                context,
                factoryConfiguration,
                persistentResult,
                backingLocalContentStore: _backingContentStore);

            return (result.Host, result.Cache);
        }
    }
}
