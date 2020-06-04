// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.Distributed.ContentLocation.NuCache
{
    public class MachineListTests
    {
        private readonly ITestClock _clock = new MemoryClock();
        private static readonly ILogger Logger = TestGlobal.Logger;
        private static readonly ContentHash TestHash = new ContentHash();

        [Fact]
        public void SortsUsingReputation()
        {
            var amountMachines = 10;
            var settings = new MachineList.Settings()
            {
                DeprioritizeMaster = false,
                PrioritizeDesignatedLocations = false,
                Randomize = false
            };

            var (machineList, clusterState, tracker, machines) = Setup(settings, amountMachines, designatedLocations: 3);

            foreach (var machine in machines)
            {
                tracker.ReportReputation(machine.Location, MachineReputation.Missing);
            }

            tracker.ReportReputation(machines[0].Location, MachineReputation.Bad);
            tracker.ReportReputation(machines[amountMachines - 1].Location, MachineReputation.Good);

            machineList[0].Should().Be(machines[amountMachines - 1].Location);
            machineList[amountMachines - 1].Should().Be(machines[0].Location);

            for (var i = 1; i < amountMachines - 1; i++)
            {
                machineList[i].Should().Be(machines[i].Location);
            }
        }

        [Fact]
        public void PrioritizeDesignatedLocations()
        {
            var amountMachines = 10;
            var designatedLocations = 3;
            var settings = new MachineList.Settings()
            {
                DeprioritizeMaster = false,
                PrioritizeDesignatedLocations = true,
                Randomize = false
            };

            var (machineList, clusterState, tracker, machines) = Setup(settings, amountMachines, designatedLocations);

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
            var settings = new MachineList.Settings()
            {
                DeprioritizeMaster = true,
                PrioritizeDesignatedLocations = false,
                Randomize = false
            };

            var (machineList, clusterState, tracker, machines) = Setup(settings, amountMachines, designatedLocations);

            // Master should be moved to the back.
            machineList[amountMachines - 1].Should().Be(machines[0].Location);
            for (var i = 0; i < amountMachines - 1; i++)
            {
                machineList[i].Should().Be(machines[i + 1].Location);
            }
        }

        [Fact]
        public void CombineRules()
        {
            System.Diagnostics.Debugger.Launch();
            var amountMachines = 10;
            var designatedLocations = 3;
            var settings = new MachineList.Settings()
            {
                DeprioritizeMaster = true,
                PrioritizeDesignatedLocations = true,
                Randomize = false
            };

            var (machineList, clusterState, tracker, machines) = Setup(settings, amountMachines, designatedLocations);

            // Bad reputation for half of the machines.
            for (var i = 1; i < amountMachines / 2; i++)
            {
                tracker.ReportReputation(machines[i].Location, MachineReputation.Bad);
            }

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
                var reputation = (int)tracker.GetReputation(machineList[i]);
                reputation.Should().BeGreaterOrEqualTo(lastReputation);
                lastReputation = reputation;
            }
        }

        [Fact]
        public void CombineRulesWithRandomization()
        {
            System.Diagnostics.Debugger.Launch();
            var amountMachines = 10;
            var designatedLocations = 3;
            var settings = new MachineList.Settings()
            {
                DeprioritizeMaster = true,
                PrioritizeDesignatedLocations = true,
                Randomize = true
            };

            var (machineList, clusterState, tracker, machines) = Setup(settings, amountMachines, designatedLocations);

            // Bad reputation for half of the machines.
            for (var i = 1; i < amountMachines / 2; i++)
            {
                tracker.ReportReputation(machines[i].Location, MachineReputation.Bad);
            }

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
                var reputation = (int)tracker.GetReputation(machineList[i]);
                reputation.Should().BeGreaterOrEqualTo(lastReputation);
                lastReputation = reputation;
            }
        }

        private (MachineList, ClusterState, MachineReputationTracker, MachineMapping[]) Setup(MachineList.Settings settings, int amountMachines, int designatedLocations)
        {
            var context = new Context(Logger);

            var machines = Enumerable.Range(1, amountMachines).Select(n => (ushort) n).ToArray();
            var machineIdSet = new ArrayMachineIdSet(machines);

            var machineMappings = machines.Select(m => new MachineMapping(new MachineLocation(m.ToString()), new MachineId(m))).ToArray();
            var clusterState = new ClusterState(primaryMachineId: default, machineMappings);
            foreach (var mapping in machineMappings)
            {
                clusterState.AddMachine(mapping.Id, mapping.Location);
            }
            clusterState.SetMasterMachine(machineMappings[0].Location);
            clusterState.InitializeBinManagerIfNeeded(locationsPerBin: designatedLocations, _clock, expiryTime: TimeSpan.FromSeconds(1));

            var tracker = new MachineReputationTracker(
                context,
                _clock,
                new MachineReputationTrackerConfiguration(),
                id => clusterState.TryResolve(id, out var location) ? location : throw new Exception("Failed to resolve ID."),
                clusterState);

            var list = new MachineList(machineIdSet, tracker, clusterState, TestHash, settings);

            return (list, clusterState, tracker, machineMappings);
        }
    }
}
