// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
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

        [Fact]
        public void SortsUsingReputation()
        {
            var amountMachines = 10;
            var settings = new MachineLocationResolver.Settings()
            {
                PrioritizeDesignatedLocations = false,
            };

            var factory = new MachineListFactory(_clock, amountMachines, designatedLocations: 3);
            var (_, tracker, machines) = factory;

            foreach (var machine in factory.MachineMappings)
            {
                tracker.ReportReputation(machine.Location, MachineReputation.Missing);
            }

            tracker.ReportReputation(machines[0].Location, MachineReputation.Bad);
            tracker.ReportReputation(machines[amountMachines - 2].Location, MachineReputation.Good);

            // Because the machine list may be eager or lazy, we need to change the state before creating a list.
            var machineList = factory.Create(settings);
            machineList[0].Should().Be(machines[amountMachines - 2].Location);
            machineList[amountMachines - 1].Should().Be(machines[amountMachines - 1].Location);

            for (var i = 1; i < amountMachines - 2; i++)
            {
                machineList[i].Should().Be(machines[i].Location);
            }
        }

        [Fact]
        public void PrioritizeDesignatedLocations()
        {
            var amountMachines = 10;
            var designatedLocations = 3;
            var settings = new MachineLocationResolver.Settings()
            {
                PrioritizeDesignatedLocations = true,
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

        [Fact]
        public void DeprioritizeMaster()
        {
            var amountMachines = 10;
            var designatedLocations = 3;
            var settings = new MachineLocationResolver.Settings()
            {
                PrioritizeDesignatedLocations = false,
            };

            var factory = new MachineListFactory(_clock, amountMachines, designatedLocations, master: 0);
            var (_, _, machines) = factory;

            var machineList = factory.Create(settings);

            // Master should be moved to the back.
            machineList[amountMachines - 1].Should().Be(machines.First().Location);
            for (var i = 0; i < amountMachines - 1; i++)
            {
                machineList[i].Should().Be(machines[i + 1].Location);
            }
        }

        [Fact]
        public void TestUnresolvedMachine()
        {
            var amountMachines = 10;
            var designatedLocations = 3;
            var settings = new MachineLocationResolver.Settings();

            var factory = new MachineListFactory(_clock, amountMachines, designatedLocations);
            var (_, _, machines) = factory;

            // Adding 2 unknown locations
            var machineIds = machines.Select(m => m.Id).ToList();
            machineIds.Add(new MachineId(amountMachines + 1));
            machineIds.Add(new MachineId(amountMachines + 2));

            var list = factory.Create(settings, machineIds.ToArray());

            list.Count.Should().Be(amountMachines);
        }

        [Fact]
        public void CombineRules()
        {
            var amountMachines = 10;
            var designatedLocations = 3;
            var settings = new MachineLocationResolver.Settings()
            {
                PrioritizeDesignatedLocations = true,
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
            machineList[amountMachines - 1].Should().Be(machines.Last().Location);

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

        [Fact]
        public void CombineRulesWithRandomization()
        {
            var amountMachines = 10;
            var designatedLocations = 3;
            var settings = new MachineLocationResolver.Settings()
            {
                PrioritizeDesignatedLocations = true,
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
            machineList[amountMachines - 1].Should().Be(machines.Last().Location);

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

        private class MockMasterElectionMechanism : StartupShutdownSlimBase, IMasterElectionMechanism
        {
            protected override Tracer Tracer { get; } = new Tracer(nameof(MockMasterElectionMechanism));

            public MachineLocation Master { get; set; }

            public Role Role => throw new NotImplementedException();

            public Task<BuildXL.Cache.ContentStore.Interfaces.Results.Result<MasterElectionState>> GetRoleAsync(OperationContext context)
            {
                throw new NotImplementedException();
            }

            public Task<BuildXL.Cache.ContentStore.Interfaces.Results.Result<Role>> ReleaseRoleIfNecessaryAsync(OperationContext context)
            {
                throw new NotImplementedException();
            }
        }

        private class MachineListFactory
        {
            public MockMasterElectionMechanism MasterElectionMechanism { get; }

            public MachineListFactory(ITestClock clock, int amountMachines, int designatedLocations, int? master = null)
            {
                Context = new OperationContext(new Context(Logger));

                this.MachineIds = Enumerable.Range(1, amountMachines).Select(n => (ushort)n).ToArray();

                var machineMappings = MachineIds.Select(m => new MachineMapping(new MachineId(m), new MachineLocation(m.ToString()))).ToArray();
                var clusterState = new ClusterState(primaryMachineId: default, machineMappings);
                foreach (var mapping in machineMappings)
                {
                    clusterState.AddMachineForTest(Context, mapping.Id, mapping.Location);
                }
                clusterState.InitializeBinManagerIfNeeded(locationsPerBin: designatedLocations, clock, expiryTime: TimeSpan.FromSeconds(1));

                ClusterState = clusterState;
                Tracker = new MachineReputationTracker(Context, clock, clusterState);
                MachineMappings = machineMappings;
                MasterElectionMechanism = new MockMasterElectionMechanism()
                {
                    Master = master != null ? machineMappings[master.Value].Location : machineMappings.Last().Location,
                };
            }

            public ClusterState ClusterState { get; }

            public MachineMapping[] MachineMappings { get; }

            public MachineReputationTracker Tracker { get; }

            public ushort[] MachineIds { get; }

            public OperationContext Context { get; }

            public IReadOnlyList<MachineLocation> Create(MachineLocationResolver.Settings settings, MachineId[] machineIds = null)
            {
                machineIds ??= MachineMappings.Select(m => m.Id).ToArray();
                var machineIdSet = new ArrayMachineIdSet(machineIds.Select(id => (ushort)id.Index));
                return  MachineLocationResolver.Resolve(Context, machineIdSet, Tracker, ClusterState, TestHash, settings, MasterElectionMechanism);
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
