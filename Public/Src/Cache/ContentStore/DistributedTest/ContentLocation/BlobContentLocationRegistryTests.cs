// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;
using ContentStoreTest.Distributed.Redis;
using ContentStoreTest.Test;
using FluentAssertions;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using static BuildXL.Cache.ContentStore.Distributed.NuCache.BlobContentLocationRegistry;

#nullable enable annotations

namespace BuildXL.Cache.ContentStore.Distributed.Test.ContentLocation
{
    [Collection("Redis-based tests")]
    [Trait("Category", "WindowsOSOnly")] // 'redis-server' executable no longer exists
    public class BlobContentLocationRegistryTests : TestWithOutput
    {
        private readonly static MachineLocation M1 = new MachineLocation("M1");
        private readonly LocalRedisFixture _fixture;

        public BlobContentLocationRegistryTests(LocalRedisFixture fixture, ITestOutputHelper output)
            : base(output)
        {
            _fixture = fixture;
        }

        public void TestCompat()
        {
            // Changing this value is a breaking change!! An epoch reset is needed if this changed.
            MachineRecord.MaxBlockLength.Should().Be(56);
        }

        [Theory]
        [MemberData(nameof(TruthTable.GetTable), 2, MemberType = typeof(TruthTable))]
        public async Task TestBasicRegister(bool concurrent, bool expireMachine)
        {
            // Increase to find flaky test if needed
            int iterationCount = 1;
            for (int i = 0; i < iterationCount; i++)
            {
                await RunTest(async context =>
                {
                    var excludedMachine = context.Machines[1];
                    bool excludeHeartbeat(TestMachine machine)
                    {
                        return expireMachine && machine == excludedMachine;
                    }

                    bool excludeContentByLocation(MachineLocation machine)
                    {
                        return expireMachine && machine == excludedMachine.Location;
                    }

                    // Heartbeat all machine so all machines are aware of each other
                    await context.HeartbeatAllMachines();
                    await context.UpdateAllMachinePartitionsAsync(concurrent);

                    context.Arguments.Clock.UtcNow += TimeSpan.FromHours(5);

                    // Heartbeat all machine so all machines are aware of each other and the selected machine has expired
                    await context.HeartbeatAllMachines(excludeHeartbeat);

                    // Increment time and recompute now that all machines have registered content and expiry time 
                    await context.UpdateAllMachinePartitionsAsync(concurrent);

                    await CheckAllPartitionsAsync(context, excludeContentByLocation);
                },
                machineCount: concurrent ? 10 : 3);
            }
        }

        // Only include some partitions to keep test run times reasonable
        private static readonly byte[] IncludedPartitions = new byte[] { 0, 4, 5, 6, 25, 255 };
        //private static readonly byte[] IncludedPartitions = new byte[] { 4 };

        private static async Task CheckAllPartitionsAsync(TestContext context, Func<MachineLocation, bool> isExcluded)
        {
            context.Arguments.Context.TracingContext.Debug("--- Checking all partitions ---", nameof(BlobContentLocationRegistryTests));
            foreach (var partition in IncludedPartitions)
            {
                var listing = await context.PrimaryMachine.Registry.ComputeSortedPartitionContentAsync(context, partition);
                listing.EntrySpan.Length.Should().Be(listing.FullSpanForTests.Length, $"Partition={partition}");
                context.CheckListing(listing, partition);
                context.Arguments.Context.TracingContext.Debug($"Partition={partition} Entries={listing.EntrySpan.Length}", nameof(BlobContentLocationRegistryTests));
            }

            var expectedContent = context.ExpectedContent.Where(t => isExcluded?.Invoke(t.Machine) != true);

            // Using this instead for set equivalence since the built-in equivalence is very slow.
            context.ActualContent.Except(expectedContent).Should().BeEmpty("Found unexpected content. Actual content minus expected content should be empty");
            expectedContent.Except(context.ActualContent).Should().BeEmpty("Could not find expected content. Expected content minus actual content should be empty");
        }

        private async Task RunTest(
            Func<TestContext, Task> runTest,
            int machineCount = 3,
            double machineContentFraction = 0.5,
            int totalUniqueContent = 10000)
        {
            var tracingContext = new Context(TestGlobal.Logger);
            var context = new OperationContext(tracingContext);

            var clock = new MemoryClock();
            using var storage = AzuriteStorageProcess.CreateAndStartEmpty(_fixture, TestGlobal.Logger);
            var arguments = new TestContextArguments(context, storage.ConnectionString, clock);
            var testContext = new TestContext(arguments);

            await testContext.StartupAsync(context).ThrowIfFailureAsync();

            ThreadSafeRandom.SetSeed(-1);

            var hashes = Enumerable.Range(0, totalUniqueContent).Select(i => ContentHash.Random(
                (HashType)((i % (int)HashType.Vso0) + 1))).ToArray();

            var perMachineContent = totalUniqueContent * machineContentFraction;

            for (int i = 0; i < machineCount; i++)
            {
                await testContext.CreateAndStartAsync();

                ThreadSafeRandom.SetSeed(i);

                var machine = testContext.Machines[i];
                for (int j = 0; j < perMachineContent; j++)
                {
                    var accessTime = clock.UtcNow - TimeSpan.FromMinutes(ThreadSafeRandom.Uniform(0, short.MaxValue));
                    var hashIndex = ThreadSafeRandom.Generator.Next(0, hashes.Length);
                    var hash = hashes[hashIndex];

                    var maxSizeOffset = hashIndex % 40;
                    var maxSizeMask = (1L << maxSizeOffset) - 1;
                    var size = MemoryMarshal.Read<long>(MemoryMarshal.AsBytes(stackalloc[] { hash.ToFixedBytes() })) & maxSizeMask;

                    if (IncludedPartitions.Contains(hash[0]))
                    {
                        testContext.ExpectedContent.Add(((ShortHash)hash, machine.Location, size));
                    }

                    machine.Store.Add(
                        hashes[hashIndex],
                        accessTime,
                        size);
                }
            }

            await runTest(testContext);
            await testContext.ShutdownAsync(context).ThrowIfFailureAsync();
        }

        private class MockContentStore : TestObserver, ILocalContentStore
        {
            private ConcurrentDictionary<ShortHash, ContentInfo> InfoMap { get; } = new();
            private int[] PartitionCounts { get; } = new int[256];

            public bool Contains(ContentHash hash)
            {
                return InfoMap.ContainsKey(hash);
            }

            public bool Contains(ShortHash hash)
            {
                return InfoMap.ContainsKey(hash);
            }

            public Task<IEnumerable<ContentInfo>> GetContentInfoAsync(CancellationToken token)
            {
                return Task.FromResult(InfoMap.Values.ToList().AsEnumerable());
            }

            public bool TryGetContentInfo(ContentHash hash, out ContentInfo info)
            {
                return InfoMap.TryGetValue(hash, out info);
            }

            public bool TryGetContentInfo(ShortHash hash, out ContentInfo info)
            {
                return InfoMap.TryGetValue(hash, out info);
            }

            public void UpdateLastAccessTimeIfNewer(ContentHash hash, DateTime newLastAccessTime)
            {
            }

            public void Add(ContentHash hash, DateTime accessTime, long size)
            {
                if (InfoMap.TryAdd(hash, new ContentInfo(hash, size, accessTime)))
                {
                    var partition = MemoryMarshal.Read<byte>(MemoryMarshal.AsBytes(stackalloc[] { hash.ToFixedBytes() }));
                    PartitionCounts[partition]++;
                }
            }

            internal override void OnPutBlock(byte partitionId, ContentListing partition)
            {
                if (PartitionCounts[partitionId] != partition.EntrySpan.Length)
                {

                }
            }
        }

        private record TestContextArguments(OperationContext Context, string ConnectionString, MemoryClock Clock);

        private record TestMachine(
            int Index,
            MockContentStore Store,
            ClusterStateManager ClusterStateManager,
            BlobContentLocationRegistry Registry,
            MachineLocation Location) : IKeyedItem<MachineLocation>
        {
            public MachineLocation GetKey()
            {
                return Location;
            }
        }

        private class TestContext : StartupShutdownComponentBase
        {
            protected override Tracer Tracer { get; } = new Tracer(nameof(TestContext));

            public KeyedList<MachineLocation, TestMachine> Machines { get; } = new();

            public TestMachine PrimaryMachine => Machines[0];

            public HashSet<(ShortHash Hash, MachineLocation Machine, long Size)> ExpectedContent = new();
            public HashSet<(ShortHash Hash, MachineLocation Machine, long Size)> ActualContent = new();

            public TestContextArguments Arguments { get; }

            public MemoryClock Clock => Arguments.Clock;

            public TestContext(TestContextArguments data)
            {
                Arguments = data;
            }

            public Task CreateAndStartAsync()
            {
                var configuration = new BlobContentLocationRegistryConfiguration()
                {
                    Credentials = new AzureBlobStorageCredentials(Arguments.ConnectionString),
                    PerPartitionDelayInterval = TimeSpan.Zero,
                    PartitionsUpdateInterval = TimeSpan.FromMinutes(5),
                    UpdateInBackground = false
                };

                var location = GetRandomLocation();

                var store = new MockContentStore();

                var clusterStateManager = new ClusterStateManager(
                    new LocalLocationStoreConfiguration()
                    {
                        PrimaryMachineLocation = location,
                        DistributedContentConsumerOnly = false
                    },
                    new BlobClusterStateStorage(
                        new BlobClusterStateStorageConfiguration()
                        {
                            Credentials = configuration.Credentials,
                        },
                        Clock),
                    Clock);

                var registry = new BlobContentLocationRegistry(
                    configuration,
                    clusterStateManager,
                    store,
                    location,
                    Arguments.Clock);

                var machine = new TestMachine(Machines.Count, store, clusterStateManager, registry, location);
                registry.Observer = store;

                Machines.Add(machine);

                return registry.StartupAsync(Arguments.Context).ThrowIfFailureAsync();
            }

            public async Task HeartbeatAllMachines(Func<TestMachine, bool> excludeMachine = null)
            {
                // Perform two iterations so machines pick up heartbeats from other machines
                for (int i = 0; i < 2; i++)
                {
                    foreach (var machine in Machines)
                    {
                        if (excludeMachine?.Invoke(machine) == true)
                        {
                            continue;
                        }

                        // Heartbeat machine so it knows about other machines
                        await machine.ClusterStateManager.HeartbeatAsync(this, MachineState.Open).ShouldBeSuccess();

                    }
                }
            }

            public async Task UpdateAllMachinePartitionsAsync(bool parallel = true, Func<TestMachine, bool> excludeMachine = null)
            {
                var tasks = new List<Task>();

                // Run twice since
                foreach (var machine in Machines)
                {
                    if (excludeMachine?.Invoke(machine) == true)
                    {
                        continue;
                    }

                    var task = UpdateMachinePartitionsAsync(machine);
                    tasks.Add(task);

                    if (!parallel)
                    {
                        await task.IgnoreFailure();
                    }
                }

                await Task.WhenAll(tasks);
            }

            public Task<BoolResult> UpdateMachinePartitionsAsync(TestMachine machine)
            {
                return machine.Registry.UpdatePartitionsAsync(this,
                    excludePartition: partition => !IncludedPartitions.Contains(partition))
                    .ThrowIfFailureAsync(s => s);
            }

            private MachineLocation GetRandomLocation()
            {
                string name = $"TestMachine{Machines.Count}";

                if ((Machines.Count % 2) == 1)
                {
                    return new MachineLocation($"grpc://{name}:{Machines.Count}/");
                }
                else
                {
                    return new MachineLocation($@"\\{name}\D$\cache\");
                }
            }

            protected override async Task<BoolResult> ShutdownComponentAsync(OperationContext context)
            {
                var result = BoolResult.Success;
                foreach (var machine in Machines)
                {
                    result &= await machine.Registry.ShutdownAsync(context);
                }

                return result;
            }

            internal void CheckListing(ContentListing listing, byte partitionId)
            {
                var clusterState = PrimaryMachine.ClusterStateManager.ClusterState;
                var entries = listing.EntrySpan.ToArray();
                for (int i = 0; i < entries.Length; i++)
                {
                    var entry = entries[i];

                    entry.PartitionId.Should().Be(partitionId);
                    entry.Hash[0].Should().Be(partitionId);

                    clusterState.TryResolve(entry.Location.AsMachineId(), out var machineLocation).Should().BeTrue();
                    ActualContent.Add((entry.Hash, machineLocation, entry.Size.Value)).Should().BeTrue();
                    var store = Machines[machineLocation].Store;
                    store.TryGetContentInfo(entry.Hash, out var info).Should().BeTrue();
                    entry.Size.Should().Be(info.Size);
                    entry.AccessTime.Should().Be(info.LastAccessTimeUtc);
                }
            }

            public static implicit operator OperationContext(TestContext context)
            {
                return context.Arguments.Context;
            }

            public static implicit operator Context(TestContext context)
            {
                return context.Arguments.Context;
            }
        }
    }
}
