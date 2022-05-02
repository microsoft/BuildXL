// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace ContentStoreTest.Distributed.ContentLocation.NuCache
{
    public class MachineListTests
    {
        private readonly ITestOutputHelper _output;
        private readonly ITestClock _clock = new MemoryClock();
        private static readonly ILogger Logger = TestGlobal.Logger;
        private static readonly ContentHash TestHash = new ContentHash();

        public MachineListTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void SortsUsingReputation(bool resolveEagerly)
        {
            var amountMachines = 10;
            var settings = new MachineList.Settings()
            {
                DeprioritizeMaster = false,
                PrioritizeDesignatedLocations = false,
                ResolveLocationsEagerly = resolveEagerly
            };

            var factory = new MachineListFactory(_clock, amountMachines, designatedLocations: 3);
            var (_, tracker, machines) = factory;

            foreach (var machine in factory.MachineMappings)
            {
                tracker.ReportReputation(machine.Location, MachineReputation.Missing);
            }

            tracker.ReportReputation(machines[0].Location, MachineReputation.Bad);
            tracker.ReportReputation(machines[amountMachines - 1].Location, MachineReputation.Good);

            // Because the machine list may be eager or lazy, we need to change the state before creating a list.
            var machineList = factory.Create(settings);
            machineList[0].Should().Be(machines[amountMachines - 1].Location);
            machineList[amountMachines - 1].Should().Be(machines[0].Location);

            for (var i = 1; i < amountMachines - 1; i++)
            {
                machineList[i].Should().Be(machines[i].Location);
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void PrioritizeDesignatedLocations(bool resolveEagerly)
        {
            var amountMachines = 10;
            var designatedLocations = 3;
            var settings = new MachineList.Settings()
            {
                DeprioritizeMaster = false,
                PrioritizeDesignatedLocations = true,
                ResolveLocationsEagerly = resolveEagerly
            };

            var factory = new MachineListFactory(_clock, amountMachines, designatedLocations);
            var (clusterState, _, _) = factory;

            var machineList = factory.Create(settings);

            for (var i = 0; i < designatedLocations; i++)
            {
                clusterState.TryResolveMachineId(machineList[i], out var id).Should().BeTrue();
                clusterState.IsDesignatedLocation(id, TestHash, includeExpired: false).Should().BeTrue();
            }

            for (var i = designatedLocations; i < amountMachines; i++)
            {
                clusterState.TryResolveMachineId(machineList[i], out var id).Should().BeTrue();
                clusterState.IsDesignatedLocation(id, TestHash, includeExpired: false).Should().BeFalse();
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void DeprioritizeMaster(bool resolveEagerly)
        {
            var amountMachines = 10;
            var designatedLocations = 3;
            var settings = new MachineList.Settings()
            {
                DeprioritizeMaster = true,
                PrioritizeDesignatedLocations = false,
                ResolveLocationsEagerly = resolveEagerly
            };

            var factory = new MachineListFactory(_clock, amountMachines, designatedLocations);
            var (_, _, machines) = factory;

            var machineList = factory.Create(settings);

            // Master should be moved to the back.
            machineList[amountMachines - 1].Should().Be(machines[0].Location);
            for (var i = 0; i < amountMachines - 1; i++)
            {
                machineList[i].Should().Be(machines[i + 1].Location);
            }
        }

        [Fact(Skip = "For manual testing only!")]
        public void PerformanceTest()
        {
            var amountMachines = 1_000;
            var designatedLocations = 3;
            var eagerSettings = new MachineList.Settings() { ResolveLocationsEagerly = true };
            var lazySettings = eagerSettings with { ResolveLocationsEagerly = false };

            var context = new Context(Logger);

            var machines = Enumerable.Range(1, amountMachines).Select(n => (ushort)n).ToArray();

            // Skipping the first 2 machine to fail machine resolution.
            var machineMappings = machines.Select(m => new MachineMapping(new MachineLocation(m.ToString()), new MachineId(m))).ToArray();
            var clusterState = new ClusterState(primaryMachineId: default, machineMappings);
            foreach (var mapping in machineMappings)
            {
                clusterState.AddMachine(mapping.Id, mapping.Location);
            }

            clusterState.SetMasterMachine(machineMappings[0].Location);
            clusterState.InitializeBinManagerIfNeeded(locationsPerBin: designatedLocations, _clock, expiryTime: TimeSpan.FromSeconds(1));

            var tracker = new MachineReputationTracker(context, _clock, clusterState);

            int machineIdSetCount = 1_000;
            int maxLocationCount = 30;
            int locationsToCheck = 3;
            var random = new Random(42);

            var machineIdSets = Enumerable.Range(1, machineIdSetCount)
                .Select(
                    n =>
                    {
                        var locationCount = random.Next(minValue: locationsToCheck, maxValue: maxLocationCount);
                        var machines = Enumerable.Range(1, locationCount).Select(n => (ushort)n).ToArray();
                        return new ArrayMachineIdSet(machines);
                    }).ToList();

            int warmupCount = 10;

            // Warming up the test
            run(warmupCount, eagerSettings);
            run(warmupCount, lazySettings);

            collectAndSleep(1_000);

            int perfRunCount = 5_000;

            var sw = Stopwatch.StartNew();
            run(perfRunCount, eagerSettings);
            var eagerDuration = sw.Elapsed;

            collectAndSleep(1_000);

            sw = Stopwatch.StartNew();
            run(perfRunCount, lazySettings);
            var lazyDuration = sw.Elapsed;

            _output.WriteLine($"Eager: {eagerDuration}, Lazy: {lazyDuration}");

            bool run(int iterationCount, MachineList.Settings settings)
            {
                bool result = false;
                for (int iteration = 0; iteration < iterationCount; iteration++)
                {
                    foreach (var machineIdSet in machineIdSets)
                    {
                        var list = MachineList.Create(context, machineIdSet, tracker, clusterState, TestHash, settings);
                        var l1 = list[0];
                        var l2 = list[1];
                        var l3 = list[2];

                        result = l1 == l2 && l2 == l3;
                    }
                }

                return result;
            }
            

            static void collectAndSleep(int delayMs)
            {
                Thread.Sleep(delayMs);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                Thread.Sleep(delayMs);
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void TestUnresolvedMachine(bool resolveEagerly)
        {
            var amountMachines = 10;
            var designatedLocations = 3;
            var settings = new MachineList.Settings()
            {
                ResolveLocationsEagerly = resolveEagerly
            };

            var factory = new MachineListFactory(_clock, amountMachines, designatedLocations);
            var (_, _, machines) = factory;

            // Adding 2 unknown locations
            var machineIds = machines.Select(m => m.Id).ToList();
            machineIds.Add(new MachineId(amountMachines + 1));
            machineIds.Add(new MachineId(amountMachines + 2));

            var list = factory.Create(settings, machineIds.ToArray());

            if (resolveEagerly)
            {
                list.Count.Should().Be(amountMachines);
            }
            else
            {
                list.Count.Should().Be(amountMachines + 2);
                // A non-eager version should fail with an exception
                Assert.Throws<InvalidOperationException>(() => list.ToList());
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void CombineRules(bool resolveEagerly)
        {
            var amountMachines = 10;
            var designatedLocations = 3;
            var settings = new MachineList.Settings()
            {
                DeprioritizeMaster = true,
                PrioritizeDesignatedLocations = true,
                ResolveLocationsEagerly = resolveEagerly
            };

            var factory = new MachineListFactory(_clock, amountMachines, designatedLocations);
            var (clusterState, tracker, machines) = factory;

            // Bad reputation for half of the machines.
            for (var i = 1; i < amountMachines / 2; i++)
            {
                tracker.ReportReputation(machines[i].Location, MachineReputation.Bad);
            }

            var machineList = factory.Create(settings);
            // Master should be moved to the back.
            machineList[amountMachines - 1].Should().Be(machines[0].Location);

            // Designated locations should be pulled to the front.
            for (var i = 0; i < designatedLocations; i++)
            {
                clusterState.TryResolveMachineId(machineList[i], out var id).Should().BeTrue();
                clusterState.IsDesignatedLocation(id, TestHash, includeExpired: false).Should().BeTrue();
            }
            for (var i = designatedLocations; i < amountMachines; i++)
            {
                clusterState.TryResolveMachineId(machineList[i], out var id).Should().BeTrue();
                clusterState.IsDesignatedLocation(id, TestHash, includeExpired: false).Should().BeFalse();
            }

            // Others should be ordered by reputation
            var lastReputation = -1;
            for (var i = designatedLocations; i < amountMachines - 1; i++)
            {
                var reputation = (int)tracker.GetReputationByMachineLocation(machineList[i]);
                reputation.Should().BeGreaterOrEqualTo(lastReputation);
                lastReputation = reputation;
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void CombineRulesWithRandomization(bool resolveEagerly)
        {
            var amountMachines = 10;
            var designatedLocations = 3;
            var settings = new MachineList.Settings()
            {
                DeprioritizeMaster = true,
                PrioritizeDesignatedLocations = true,
                //Randomize = true,
                ResolveLocationsEagerly = resolveEagerly
            };

            var factory = new MachineListFactory(_clock, amountMachines, designatedLocations);
            var (clusterState, tracker, machines) = factory;

            // Bad reputation for half of the machines.
            for (var i = 1; i < amountMachines / 2; i++)
            {
                tracker.ReportReputation(machines[i].Location, MachineReputation.Bad);
            }

            // Master should be moved to the back.
            var machineList = factory.Create(settings);
            machineList[amountMachines - 1].Should().Be(machines[0].Location);

            // Designated locations should be pulled to the front.
            for (var i = 0; i < designatedLocations; i++)
            {
                clusterState.TryResolveMachineId(machineList[i], out var id).Should().BeTrue();
                clusterState.IsDesignatedLocation(id, TestHash, includeExpired: false).Should().BeTrue();
            }
            for (var i = designatedLocations; i < amountMachines; i++)
            {
                clusterState.TryResolveMachineId(machineList[i], out var id).Should().BeTrue();
                clusterState.IsDesignatedLocation(id, TestHash, includeExpired: false).Should().BeFalse();
            }

            // Others should be ordered by reputation
            var lastReputation = -1;
            for (var i = designatedLocations; i < amountMachines - 1; i++)
            {
                var reputation = (int)tracker.GetReputationByMachineLocation(machineList[i]);
                reputation.Should().BeGreaterOrEqualTo(lastReputation);
                lastReputation = reputation;
            }
        }

        private class MachineListFactory
        {
            public MachineListFactory(ITestClock clock, int amountMachines, int designatedLocations)
            {
                Context = new Context(Logger);

                this.MachineIds = Enumerable.Range(1, amountMachines).Select(n => (ushort)n).ToArray();

                var machineMappings = MachineIds.Select(m => new MachineMapping(new MachineLocation(m.ToString()), new MachineId(m))).ToArray();
                var clusterState = new ClusterState(primaryMachineId: default, machineMappings);
                foreach (var mapping in machineMappings)
                {
                    clusterState.AddMachine(mapping.Id, mapping.Location);
                }

                clusterState.SetMasterMachine(machineMappings[0].Location);
                clusterState.InitializeBinManagerIfNeeded(locationsPerBin: designatedLocations, clock, expiryTime: TimeSpan.FromSeconds(1));

                ClusterState = clusterState;
                Tracker = new MachineReputationTracker(Context, clock, clusterState);
                MachineMappings = machineMappings;
            }

            public ClusterState ClusterState { get; }

            public MachineMapping[] MachineMappings { get; }

            public MachineReputationTracker Tracker { get; }

            public ushort[] MachineIds { get; }

            public Context Context { get; }

            public IReadOnlyList<MachineLocation> Create(MachineList.Settings settings, MachineId[] machineIds = null)
            {
                machineIds ??= MachineMappings.Select(m => m.Id).ToArray();
                var machineIdSet = new ArrayMachineIdSet(machineIds.Select(id => (ushort)id.Index));
                return  MachineList.Create(Context, machineIdSet, Tracker, ClusterState, TestHash, settings);
            }

            public void Deconstruct(out ClusterState clusterState, out MachineReputationTracker tracker, out MachineMapping[] mappings)
            {
                clusterState = ClusterState;
                tracker = Tracker;
                mappings = MachineMappings;
            }
        }
    }
}
