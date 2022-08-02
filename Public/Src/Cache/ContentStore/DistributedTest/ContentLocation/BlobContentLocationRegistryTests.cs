// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Utilities;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
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
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.Host.Service;
using BuildXL.Cache.Logging;
using BuildXL.Engine.Cache.KeyValueStores;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tasks;
using ContentStoreTest.Distributed.Redis;
using ContentStoreTest.Test;
using FluentAssertions;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using RocksDbSharp;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using static BuildXL.Cache.ContentStore.Distributed.NuCache.BlobContentLocationRegistry;
using OperationContext = BuildXL.Cache.ContentStore.Tracing.Internal.OperationContext;

#nullable enable annotations

namespace BuildXL.Cache.ContentStore.Distributed.Test.ContentLocation
{
    [Collection("Redis-based tests")]
    [Trait("Category", "WindowsOSOnly")] // 'redis-server' executable no longer exists
#if !NETCOREAPP
    [TestClassIfSupported(TestRequirements.NotSupported)]
#endif
    public class BlobContentLocationRegistryTests : TestBase
    {
        private readonly static MachineLocation M1 = new MachineLocation("M1");
        private readonly LocalRedisFixture _fixture;

        private Tracer Tracer { get; } = new Tracer(nameof(BlobContentLocationRegistryTests));

        public BlobContentLocationRegistryTests(LocalRedisFixture fixture, ITestOutputHelper output)
            : base(KustoStructuredLogger.CreateLogger("kuslogs", "logs", output), output)
        {
            _fixture = fixture;
        }

        [Fact]
        public void TestCompat()
        {
            // Changing these values are a breaking change!! An epoch reset is needed if they are changed.
            MachineRecord.BlockIdLength.Should().Be(56);

            Unsafe.SizeOf<MachineContentEntry>().Should().Be(24);
        }

        [Fact]
        public void TestScheduler()
        {
            var cases = new List<SchedulerCheck>()
            {
                new ("19:55:02", "19:55:10", 0, "15m", "1m", "20:00:00"),
                new ("19:55:02", "19:55:10", 1, "15m", "1m", "20:01:00"),
                new ("19:55:02", "19:55:10", 2, "15m", "1m", "20:02:00"),
                new ("19:55:02", "19:55:10", 3, "15m", "1m", "20:03:00"),

                new ("19:45:00", "19:55:10", 0, "15m", "1m", "19:45:00"),
                new ("19:45:00", "19:55:10", 1, "15m", "1m", "19:46:00"),
                new ("19:45:00", "19:55:10", 2, "15m", "1m", "19:47:00"),
                new ("19:45:00", "19:55:10", 3, "15m", "1m", "19:48:00"),
            };

            var context = new OperationContext(new Context(Logger));

            foreach (var c in cases)
            {
                var clock = new MemoryClock();
                clock.UtcNow = c.CurrentTime;
                var configuration = new BlobContentLocationRegistryConfiguration()
                {
                    PartitionsUpdateInterval = c.UpdateInterval,
                    StageInterval = c.StageInterval
                };
                var scheduler = new BlobContentLocationRegistryScheduler(configuration, clock);

                var waitTask = scheduler.WaitForStartStageAsync(context, c.Stage, c.StartTime, BuildXL.Utilities.AsyncOut.Var<DateTime>(out var target));
                waitTask.Forget();

                target.Value.Should().Be(c.ExpectedTarget);
            }
        }

        private record SchedulerCheck(
            TestDateTime StartTime,
            TestDateTime CurrentTime,
            int Stage,
            TimeSpanSetting UpdateInterval,
            TimeSpanSetting StageInterval,
            TestDateTime ExpectedTarget);

        private record struct TestDateTime(string StringValue)
        {
            public DateTime Value { get; } = Parse(StringValue);

            private static DateTime Parse(string stringValue)
            {
                return DateTime.Parse($"2022-08-02T{stringValue}.0000000Z");
            }

            public static implicit operator TestDateTime(string value)
            {
                return new TestDateTime(value);
            }

            public static implicit operator DateTime(TestDateTime value)
            {
                return value.Value;
            }
        }

        [Theory]
        [InlineData(8, 1501, 64)]
        [InlineData(4, 999, 256)]
        [InlineData(2, 999, 256)]
        [InlineData(8, 15001, 64)]
        [InlineData(8, 10, 64)]
        [InlineData(4, 0, 64)]
        // Parameters which are coerced to powers of two
        [InlineData(5, 43, 67)]
        [InlineData(1, 43, 67)]
        public void TestNodes(int factor, int maxMachineId, int partitionCount)
        {
            var partitions = PartitionId.GetPartitions(partitionCount);
            var nodeStages = InterleaveNode.ComputeNodes(factor, maxMachineId, partitions);

            var totalNodes = nodeStages.Sum(n => n.Length);
            var maxTotalNodes = Math.Max(partitions.Length, (maxMachineId + 1) / factor) * partitions.Length;
            totalNodes.Should().BeLessOrEqualTo(maxTotalNodes);

            nodeStages.Length.Should().BeGreaterThan(0);
            foreach (var stage in nodeStages)
            {
                BitArray[] machinePartitionFoundMaps = Enumerable.Range(0, maxMachineId + 1)
                    .Select(m => new BitArray(256))
                    .ToArray();

                foreach (var node in stage)
                {
                    for (int machineId = node.MachineStart; machineId <= Math.Min(maxMachineId, node.MachineEnd); machineId++)
                    {
                        var bitMap = machinePartitionFoundMaps[machineId];
                        for (int hashPrefix = node.PartitionId.StartValue; hashPrefix <= node.PartitionId.EndValue; hashPrefix++)
                        {
                            bitMap[hashPrefix].Should().BeFalse("Every node should handle disjoint set of machine/partition ranges");
                            bitMap[hashPrefix] = true;
                        }
                    }
                }

                for (int machine = 0; machine <= maxMachineId; machine++)
                {
                    for (int hashPrefix = 0; hashPrefix < 256; hashPrefix++)
                    {
                        machinePartitionFoundMaps[machine][hashPrefix].Should().BeTrue();
                    }
                }
            }

            foreach (var node in nodeStages.Last())
            {
                node.IsTerminal.Should().BeTrue();
                node.PartitionId.PartitionCount.Should().Be(partitions.Length);
                node.PartitionSpan.Length.Should().Be(1);
                node.PartitionSpan.Partitions.Length.Should().Be(1);
            }
        }

        [Theory]
        [MemberData(nameof(TruthTable.GetTable), 3, MemberType = typeof(TruthTable))]
        public async Task TestBasicRegister(bool concurrent, bool expireMachine, bool useMaxPartitionCount)
        {
            // Increase to find flaky test if needed
            int iterationCount = 1;
            for (int i = 0; i < iterationCount; i++)
            {
                await RunTest(async context =>
                {
                    var excludedMachine = context.Machines[1];
                    var firstHash = new ContentHash(HashType.MD5, Guid.Empty.ToByteArray());
                    excludedMachine.Store.Add(firstHash, DateTime.UtcNow, 42);

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

                    await CheckAllPartitionsAsync(context, isExcluded: null);

                    context.Arguments.Clock.UtcNow += TimeSpan.FromHours(5);

                    // Heartbeat all machine so all machines are aware of each other and the selected machine has expired
                    await context.HeartbeatAllMachines(excludeHeartbeat);

                    // Increment time and recompute now that all machines have registered content and expiry time 
                    Tracer.Debug(context, "--- Machine should be excluded ---");
                    await context.UpdateAllMachinePartitionsAsync(concurrent, expireMachine, excludeHeartbeat);

                    await CheckAllPartitionsAsync(context, excludeContentByLocation);
                },
                machineCount: concurrent ? 10 : 3,
                useMaxPartitionCount: useMaxPartitionCount);
            }
        }

        // Only include some partitions to keep test run times reasonable
        private static readonly ReadOnlyArray<PartitionId> MaxPartitionCountIncludedPartitions = new byte[] { 0, 4, 5, 6, 25, 255 }
            .Select(i => PartitionId.GetPartitions(256)[i])
            .ToArray();

        private static readonly ReadOnlyArray<PartitionId> FewPartitionCountIncludedPartitions = PartitionId.GetPartitions(4).ToArray();

        private async Task CheckAllPartitionsAsync(TestContext context, Func<MachineLocation, bool> isExcluded)
        {
            var primary = context.PrimaryMachine;
            context.ActualContent.Clear();

            Tracer.Debug(context, "--- Checking all partitions ---");
            foreach (var partition in context.IncludedPartitions)
            {
                var listing = await primary.Registry.ComputeSortedPartitionContentAsync(context, partition);
                listing.EntrySpan.Length.Should().Be(listing.FullSpanForTests.Length, $"Partition={partition}");
                context.CheckListing(listing, partition);
                Tracer.Debug(context, $"Partition={partition} Entries={listing.EntrySpan.Length}");
            }

            var expectedContent = context.ExpectedContent.Where(t => isExcluded?.Invoke(t.Machine) != true).ToHashSet();

            // Using this instead for set equivalence since the built-in equivalence is very slow.
            context.ActualContent.Except(expectedContent).Should().BeEmpty("Found unexpected content. Actual content minus expected content should be empty");
            expectedContent.Except(context.ActualContent).Should().BeEmpty($"Could not find expected content. Expected content (Count={expectedContent.Count}) minus actual content (Count={context.ActualContent.Count}) should be empty");

            // Ensure database is updated by setting time after interval and triggering update
            context.Clock.UtcNow += TestContext.PartitionsUpdateInterval + TimeSpan.FromSeconds(10);

            Tracer.Debug(context, "--- Updating database ---");
            context.ResetSemaphore(0);
            await context.UpdateMachinePartitionsAsync(primary).ShouldBeSuccess();

            var clusterState = primary.ClusterStateManager.ClusterState;
            var actualDbContent = context.ExpectedContent.Take(0).ToHashSet();

            primary.Database.IterateSstMergeContentEntries(context, entry =>
            {
                entry.Location.IsAdd.Should().BeTrue();
                clusterState.TryResolve(entry.Location.AsMachineId(), out var location).Should().BeTrue();
                var actualEntry = (entry.Hash, location, entry.Size.Value);
                expectedContent.Should().Contain(actualEntry);
                actualDbContent.Add(actualEntry).Should().BeTrue();
            }).ShouldBeSuccess().Value.ReachedEnd.Should().BeTrue();

            expectedContent.Except(actualDbContent).Should().BeEmpty($"Could not find expected content in database. Expected content (Count={expectedContent.Count}) minus actual content (Count={actualDbContent.Count}) should be empty");
        }

        private async Task RunTest(
            Func<TestContext, Task> runTest,
            int machineCount = 3,
            double machineContentFraction = 0.5,
            int totalUniqueContent = 10000,
            bool useMaxPartitionCount = true)
        {
            var includedPartitions = useMaxPartitionCount
                ? MaxPartitionCountIncludedPartitions
                : FewPartitionCountIncludedPartitions;

            KustoStructuredLogger.SetTestName(Guid.NewGuid().ToString());
            var tracingContext = new Context(Logger);
            var context = new OperationContext(tracingContext);

            var clock = new MemoryClock();
            using var storage = AzuriteStorageProcess.CreateAndStartEmpty(_fixture, Logger);
            var arguments = new TestContextArguments(context, storage.ConnectionString, clock, TestRootDirectoryPath, includedPartitions);
            var testContext = new TestContext(arguments);

            await testContext.StartupAsync(context).ThrowIfFailureAsync();

            ThreadSafeRandom.SetSeed(-1);

            var hashes = Enumerable.Range(0, totalUniqueContent).Select(i => ContentHash.Random(
                (HashType)((i % (int)HashType.Vso0) + 1))).ToArray();

            var zerohash = new ShortHash("MD5:0000000000000000000000");
            var hasZero = hashes.Any(h => zerohash.Equals(h));

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

                    machine.Store.Add(
                        hashes[hashIndex],
                        accessTime,
                        size);
                }
            }

            await runTest(testContext);
            await testContext.ShutdownAsync(context).ThrowIfFailureAsync();
        }

        private record MockContentStore(TestContext Context, MachineLocation Location) : ILocalContentStore
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
                ShortHash shortHash = hash;
                var partition = shortHash[0];
                if (InfoMap.TryAdd(hash, new ContentInfo(hash, size, accessTime)))
                {
                    PartitionCounts[partition]++;
                    if (Context.IncludedPartitionIndices.Contains(hash[0]))
                    {
                        Context.ExpectedContent.Add((shortHash, Location, size));
                    }
                }
            }
        }

        private record TestContextArguments(OperationContext Context,
                                            string ConnectionString,
                                            MemoryClock Clock,
                                            AbsolutePath Path,
                                            ReadOnlyArray<PartitionId> IncludedPartitions);

        private record TestMachine(
            int Index,
            RocksDbContentMetadataDatabase Database,
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

        private class TestContext : StartupShutdownComponentBase, IBlobContentLocationRegistryScheduler
        {
            protected override Tracer Tracer { get; } = new Tracer(nameof(TestContext));

            public KeyedList<MachineLocation, TestMachine> Machines { get; } = new();

            public static readonly TimeSpan PartitionsUpdateInterval = TimeSpan.FromMinutes(5);

            public TestMachine PrimaryMachine => Machines[0];

            public ReadOnlyArray<PartitionId> IncludedPartitions => Arguments.IncludedPartitions;
            public byte[] IncludedPartitionIndices => Arguments.IncludedPartitions
                .SelectMany(p => Enumerable.Range(p.StartValue, (p.EndValue - p.StartValue) + 1))
                .Select(i => (byte)i)
                .ToArray();

            public HashSet<(ShortHash Hash, MachineLocation Machine, long Size)> ExpectedContent = new();
            public HashSet<(ShortHash Hash, MachineLocation Machine, long Size)> ActualContent = new();

            public TestContextArguments Arguments { get; }

            private SemaphoreSlim _stageSemaphore;
            private int _slots;
            private int _maxSlots;

            public MemoryClock Clock => Arguments.Clock;

            public DisposableDirectory Directory { get; }

            public Guid TestUniqueId = Guid.NewGuid();

            public TestContext(TestContextArguments data)
            {
                Arguments = data;

                Directory = new DisposableDirectory(PassThroughFileSystem.Default, data.Path);
            }

            public void ResetSemaphore(int count)
            {
                _slots = count;
                _maxSlots = count;
                if (count != 0)
                {
                    _stageSemaphore = new SemaphoreSlim(0, count);
                }
            }

            public Task WaitForStartStageAsync(OperationContext context, int stage, DateTime start)
            {
                if (_maxSlots == 0)
                {
                    return Task.CompletedTask;
                }

                var semaphore = _stageSemaphore;
                var waitTask = semaphore.WaitAsync(context.Token);
                var remainingSlots = Interlocked.Decrement(ref _slots);
                remainingSlots.Should().BeGreaterOrEqualTo(0);
                if (remainingSlots == 0)
                {
                    Tracer.Debug(context, $"--- Releasing stage: {stage} ---");
                    ResetSemaphore(_maxSlots);
                    semaphore.Release(_maxSlots);
                }

                return waitTask;
            }

            protected override void DisposeCore()
            {
                Directory.Dispose();
            }

            public Task CreateAndStartAsync()
            {
                var index = Machines.Count;
                var configuration = new BlobContentLocationRegistryConfiguration()
                {
                    FolderName = TestUniqueId.ToString(),
                    Credentials = new AzureBlobStorageCredentials(Arguments.ConnectionString),
                    PartitionsUpdateInterval = PartitionsUpdateInterval,
                    UpdateInBackground = false,
                    PartitionCount = IncludedPartitions[0].PartitionCount,
                    UpdateDatabase = index == 0
                };

                var location = GetRandomLocation();

                var store = new MockContentStore(this, location);

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

                var database = new RocksDbContentMetadataDatabase(
                    Clock,
                    new RocksDbContentMetadataDatabaseConfiguration(Directory.Path / Machines.Count.ToString())
                    {
                        UseMergeOperators = true
                    });

                var registry = new BlobContentLocationRegistry(
                    configuration,
                    clusterStateManager,
                    location,
                    database,
                    store,
                    Clock);

                registry.Scheduler = this;

                var machine = new TestMachine(index, database, store, clusterStateManager, registry, location);

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

            public async Task UpdateAllMachinePartitionsAsync(bool parallel = true, bool isMachineExcluded = false, Func<TestMachine, bool> excludeMachine = null)
            {
                // Set the semaphore to progress when all machines are waiting for stage to progress
                // NOTE: This is only valid when running in parallel
                ResetSemaphore(parallel
                    ? Machines.Count - (isMachineExcluded ? 1 : 0)
                    : 0);

                // Two iterations required when running synchronously because
                // of bug in Azurite where latest partition is not picked.
                for (int i = 0; i < 2; i++)
                {
                    var tasks = new List<Task>();

                    foreach (var machine in Machines)
                    {
                        if (excludeMachine?.Invoke(machine) == true)
                        {
                            continue;
                        }

                        await machine.Registry.NodeCompletionTracker.ClearCompletedAsync();

                        var task = UpdateMachinePartitionsAsync(machine);
                        tasks.Add(task);

                        if (!parallel)
                        {
                            await task.IgnoreFailure();
                        }
                    }

                    await Task.WhenAll(tasks);
                }
            }

            public async Task<BoolResult> UpdateMachinePartitionsAsync(TestMachine machine)
            {
                await PrimaryMachine.Registry.NodeCompletionTracker.ClearCompletedAsync();

                // Ensure the primary machine is updating its database
                PrimaryMachine.Registry.SetDatabaseUpdateLeaseExpiry(Clock.UtcNow + PartitionsUpdateInterval + TimeSpan.FromSeconds(1));

                return await machine.Registry.UpdatePartitionsAsync(this,
                    excludePartition: partition => !IncludedPartitions.Any(p => p == partition || p.Intersects(partition)))
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

            internal void CheckListing(ContentListing listing, PartitionId partitionId)
            {
                var clusterState = PrimaryMachine.ClusterStateManager.ClusterState;
                var entries = listing.EntrySpan.ToArray();
                for (int i = 0; i < entries.Length; i++)
                {
                    var entry = entries[i];

                    entry.PartitionId.Should().Be(entry.Hash[0]);
                    partitionId.Contains(entry.PartitionId).Should().BeTrue();

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
