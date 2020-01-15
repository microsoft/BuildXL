// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using FluentAssertions;
using Xunit;

namespace BuildXL.Cache.ContentStore.Distributed.Test.ContentLocation.NuCache
{
    public class BinManagerTests
    {
        private readonly IClock _clock = new TestSystemClock();

        [Theory]
        [InlineData(3, 0, 1)] // Started with 0 machines
        [InlineData(3, 2, 10)] // Started with small amount of machines
        [InlineData(3, 1000, 24)] // Started with large non-power of two and ends in power of two.
        [InlineData(3, 1024, 10)] // Started in power of two and adds some machines
        public void AddLocationsKeepsBinsBalanced(int locationsPerBin, int initialAmountOfLocations, int locationsToAdd)
        {
            var (manager, locations) = CreateAndValidate(locationsPerBin, initialAmountOfLocations);
            AddLocationsAndValidate(manager, locations, locationsToAdd);
        }

        [Theory]
        [InlineData(3, 1, 1)] // Started with 1 machines and remove it
        [InlineData(3, 3, 1)] // Started with small amount of machines and remove 1
        [InlineData(3, 4, 1)] // Started remove to end at limit
        [InlineData(3, 1024, 10)] // Started in power of two and removes some machines
        public void RemoveLocationsKeepsBinsBalanced(int locationsPerBin, int initialAmountOfLocations, int locationsToRemove)
        {
            var (manager, locations) = CreateAndValidate(locationsPerBin, initialAmountOfLocations);
            RemoveLocationsAndValidate(manager, locations, locationsToRemove);
        }

        [Theory]
        [InlineData(3, 1000, 3)] // Same amount of locations
        [InlineData(3, 1000, 4)] // We increased the amount of locations per bin
        [InlineData(4, 1000, 3)] // We decreased the amount of locations per bin
        [InlineData(3, 2, 3)] // There are less locations than the amount of locations per bin
        [InlineData(3, 0, 3)] // We have 0 locations
        public void BinManagerFromOther(int prevLocationsPerBin, int amountOfLocations, int currLocationsPerBin)
        {
            var (manager, locations) = CreateAndValidate(prevLocationsPerBin, amountOfLocations);
            var bytes = manager.Serialize();
            var deserializedManager = BinManager.CreateFromSerialized(bytes, currLocationsPerBin, _clock, TimeSpan.FromHours(1));
            ValidateBalanced(deserializedManager, locations);
        }

        [Fact]
        public void BinManagerGetNextBinReturnsAllValues()
        {
            HashSet<uint> results = new HashSet<uint>();
            for (uint i = 0; i < BinManager.NumberOfBins; i++)
            {
                var nextBin = BinManager.GetNextBin(i);
                bool added = results.Add(nextBin);
                added.Should().BeTrue();
            }
        }

        [Theory]
        [InlineData(3, 2, 10)] // Start with small amount of machines
        [InlineData(3, 1000, 10)] // Start with large amount of machines
        public void BinManagerSerializeDeserialize(int locationsPerBin, int initialAmountOfLocations, int locationsToAdd)
        {
            var (manager, locations) = CreateAndValidate(locationsPerBin, initialAmountOfLocations);
            AddLocationsAndValidate(manager, locations, locationsToAdd);

            var bytes = manager.Serialize();
            var deserializedManager = BinManager.CreateFromSerialized(bytes, locationsPerBin, _clock, TimeSpan.FromHours(1));

            VerifyMappingsAreEqual(manager, deserializedManager);
        }

        private (BinManager manager, List<MachineId> locations) CreateAndValidate(int locationsPerBin, int amountOfLocations)
        {
            var locations = Enumerable.Range(0, amountOfLocations).Select(num => new MachineId(num)).ToList();
            var manager = new BinManager(locationsPerBin, startLocations: locations, _clock, TimeSpan.FromHours(1));

            ValidateBalanced(manager, locations);

            return (manager, locations);
        }

        private void AddLocationsAndValidate(BinManager manager, List<MachineId> locations, int locationsToAdd)
        {
            var newLocations = Enumerable.Range(locations.Count, locationsToAdd).Select(num => new MachineId(num));

            foreach (var location in newLocations)
            {
                locations.Add(location);
                manager.AddLocation(location);

                ValidateBalanced(manager, locations);
            }
        }

        private void RemoveLocationsAndValidate(BinManager manager, List<MachineId> locations, int locationsToRemove)
        {
            for (var i = 0; i < locationsToRemove; i++)
            {
                var locationToRemove = locations[0];

                var binsWithMachineAssigned = manager.GetBins(force: true)
                    .Select((machines, bin) => (machines, bin))
                    .Where(t => t.machines.Contains(locationToRemove))
                    .Select(t => (uint)t.bin)
                    .ToHashSet();

                var chain = manager.EnumeratePreviousBins(0).Take(30).Select(b => (b, binsWithMachineAssigned.Contains(b))).ToList();

                locations.Remove(locationToRemove);
                manager.RemoveLocation(locationToRemove);

                ValidateBalanced(manager, locations);

                foreach (var binWithMachineAssigned in binsWithMachineAssigned)
                {
                    var assignedMachines = manager.GetDesignatedLocations(binWithMachineAssigned, includeExpired: false);
                    var assignedMachinesWithExpired = manager.GetDesignatedLocations(binWithMachineAssigned, includeExpired: true);

                    assignedMachines.Should().NotContain(locationToRemove);
                    assignedMachinesWithExpired.Should().Contain(locationToRemove);
                }
            }
        }

        private void ValidateBalanced(BinManager manager, List<MachineId> locations)
        {
            var expectedLocationsPerBin = locations.Count > manager.LocationsPerBin
                ? manager.LocationsPerBin
                : locations.Count;

            var expectedBinsPerLocation = locations.Count >= manager.LocationsPerBin
                ? BinManager.NumberOfBins / locations.Count * manager.LocationsPerBin
                : BinManager.NumberOfBins;

            var binMappings = manager.GetBins();
            var counts = new Dictionary<int, int>();
            foreach (var location in locations)
            {
                counts[location.Index] = 0;
            }

            foreach (var bin in binMappings)
            {
                bin.Length.Should().Be(expectedLocationsPerBin);

                foreach (var mapping in bin)
                {
                    counts[mapping.Index]++;
                }
            }

            foreach (var count in counts.Values)
            {
                count.Should().BeInRange((int)(expectedBinsPerLocation * .9), (int)(expectedBinsPerLocation  * 1.1));
            }
        }

        private void VerifyMappingsAreEqual(BinManager x, BinManager y)
        {
            var xBins = x.GetBins();
            var yBins = y.GetBins();

            var xExpired = x.GetExpiredAssignments();
            var yExpired = y.GetExpiredAssignments();

            xBins.Length.Should().Be(yBins.Length);
            for (var bin = 0; bin < xBins.Length; bin++)
            {
                xBins[bin].Length.Should().Be(yBins[bin].Length);
                for (var mapping = 0; mapping < xBins[bin].Length; mapping++)
                {
                    xBins[bin][mapping].Index.Should().Be(yBins[bin][mapping].Index);
                }
            }

            xExpired.Count.Should().Be(yExpired.Count);
            foreach (var bin in xExpired.Keys)
            {
                var xBinExpired = xExpired[bin];
                var yBinExpired = yExpired[bin];
                xBinExpired.Count.Should().Be(yBinExpired.Count);

                foreach (var machine in xBinExpired.Keys)
                {
                    xBinExpired[machine].Should().Be(yBinExpired[machine]);
                }
            }
        }
    }
}
