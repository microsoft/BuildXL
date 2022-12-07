// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.InterfacesTest;
using BuildXL.Utilities;
using BuildXL.Utilities.Serialization;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace ContentStoreTest.Distributed.ContentLocation.NuCache
{
    internal static class MachineIdExtensions
    {
        public static MachineId AsMachineId(this int index) => MachineId.FromIndex(index);

        public static T NotNull<T>(this T instance,
#if NET5_0_OR_GREATER
            [CallerArgumentExpression("instance")]
#endif
            string expression = "")
        {
            Contract.Assert(instance is not null, expression);

            return instance;
        }
    }

    public class SortedLocationChangeMachineIdSetTests : LocationChangeMachineIdSetTests
    {
        public SortedLocationChangeMachineIdSetTests(ITestOutputHelper output)
            : base(output)
        {
        }

        protected override MachineIdSet EmptyInstance => MachineIdSet.SortedEmptyChangeSet;

        [Fact]
        public void SortedLocationChangeMachineIdSetSortsOnSerialize()
        {
            var set1 = (SortedLocationChangeMachineIdSet)MachineIdSet.SortedEmptyChangeSet
                .SetExistence(2.AsMachineId(), false)
                .SetExistence(1.AsMachineId(), true)
                .SetExistence(5.AsMachineId(), false)
                .SetExistence(3.AsMachineId(), true);

            // Serialization should sort the input list.
            SortedLocationChangeMachineIdSet clonedSet = set1.CloneWithSpan();

            clonedSet.LocationStates.Should()
                .BeEquivalentTo(set1.LocationStates.Sort(LocationChangeMachineIdSet.LocationChangeMachineIdComparer.Instance));
        }
    }

    public class LocationChangeMachineIdSetTests : TestWithOutput
    {
        public LocationChangeMachineIdSetTests(ITestOutputHelper output)
            : base(output)
        {
        }

        protected virtual MachineIdSet EmptyInstance => LocationChangeMachineIdSet.EmptyInstance;

        [Fact]
        public void MachineIdIndexShouldBePresent()
        {
            MachineIdSet set1 = EmptyInstance;

            set1 = set1.Add(1.AsMachineId());
            set1.GetMachineIdIndex(1.AsMachineId()).Should().Be(0);
            set1.GetMachineIdIndex(2.AsMachineId()).Should().Be(-1);
        }

        [Fact]
        public void SetExistenceWithAddAndRemove()
        {
            MachineIdSet set1 = EmptyInstance;

            set1 = set1
                .SetExistence(1.AsMachineId(), true)
                .SetExistence(2.AsMachineId(), false);

            set1.Count.Should().Be(1, "Removal is should not affect the Count");

            var setAsLocationChangeSet = (LocationChangeMachineIdSet)set1;
            Output.WriteLine("Set: " + string.Join(", ", setAsLocationChangeSet.LocationStates.Select(s => s.ToString())));
            setAsLocationChangeSet.LocationStates.Count().Should().Be(2);

            // Both machine ids should be present in the set.
            set1.Contains(1.AsMachineId()).Should().BeTrue();
            set1.Contains(2.AsMachineId()).Should().BeFalse();

            set1[1.AsMachineId()].Should().BeTrue();
            set1[2.AsMachineId()].Should().BeFalse();
        }

        [Fact]
        public void MergeWithEmptySet()
        {
            var set1 = EmptyInstance
                .SetExistence(2.AsMachineId(), false)
                .SetExistence(1.AsMachineId(), true)
                .SetExistence(5.AsMachineId(), false)
                .SetExistence(3.AsMachineId(), true);

            var locationStatesCount = 4;
            set1.Count.Should().Be(2);

            set1[1.AsMachineId()].Should().BeTrue();
            set1[3.AsMachineId()].Should().BeTrue();

            (set1 as LocationChangeMachineIdSet).NotNull().LocationStates.Count().Should().Be(locationStatesCount);

            // set1 should be the same as before
            set1 = set1.MergeTest(MachineIdSet.Empty);

            // set2 should be the same as set1,
            // because now merges should be respected regardless of the type on the left.
            var set2 = MachineIdSet.Empty.MergeTest(set1);

            (set1 as LocationChangeMachineIdSet).NotNull().LocationStates.Count().Should().Be(locationStatesCount);
            (set2 as LocationChangeMachineIdSet).NotNull().LocationStates.Count().Should().Be(locationStatesCount);

            // Both machine ids should be present in the set.
            set1.Contains(1.AsMachineId()).Should().BeTrue();
            set2.Contains(1.AsMachineId()).Should().BeTrue();

            set2.Contains(3.AsMachineId()).Should().BeTrue();

            set1.Contains(2.AsMachineId()).Should().BeFalse();
            set2.Contains(2.AsMachineId()).Should().BeFalse();

            set1[1.AsMachineId()].Should().BeTrue();
            set2[1.AsMachineId()].Should().BeTrue();

            set1[2.AsMachineId()].Should().BeFalse();
            set2[2.AsMachineId()].Should().BeFalse();
        }

        [Fact]
        public void AdditiveMerge()
        {
            MachineIdSet set1 = EmptyInstance;

            set1 = set1
                .SetExistence(2.AsMachineId(), false)
                .SetExistence(1.AsMachineId(), true);

            // Adding/removing different locations
            var set2 = EmptyInstance
                .SetExistence(3.AsMachineId(), true)
                .SetExistence(4.AsMachineId(), false);

            var merged = set1.MergeTest(set2);
            var setAsLocationChangeSet = (merged as LocationChangeMachineIdSet).NotNull();
            Output.WriteLine("Set: " + string.Join(", ", setAsLocationChangeSet.LocationStates.Select(s => s.ToString())));

            merged.Count.Should().Be(2, "Removal should not affect the Count");

            setAsLocationChangeSet.LocationStates.Count().Should().Be(4);

            // Both machine ids should be present in the set.
            merged.Contains(1.AsMachineId()).Should().BeTrue();
            merged.Contains(2.AsMachineId()).Should().BeFalse();

            merged.Contains(3.AsMachineId()).Should().BeTrue();
            merged.Contains(4.AsMachineId()).Should().BeFalse();
        }

        [Fact]
        public void MergeWithAdd()
        {
            MachineIdSet set1 = EmptyInstance;

            set1 = set1
                .SetExistence(1.AsMachineId(), true)
                .SetExistence(2.AsMachineId(), false);

            // Adding/removing the same locations
            var set2 = EmptyInstance.SetExistence(2.AsMachineId(), true);

            var merged = set1.MergeTest(set2);

            var setAsLocationChangeSet = (LocationChangeMachineIdSet)merged;
            Output.WriteLine("Set: " + string.Join(", ", setAsLocationChangeSet.LocationStates.Select(s => s.ToString())));

            merged.Count.Should().Be(2);
            setAsLocationChangeSet.LocationStates.Count().Should().Be(2);

            merged.Contains(1.AsMachineId()).Should().BeTrue();
        }

        [Fact]
        public void MergeWithRemove()
        {
            MachineIdSet set1 = EmptyInstance;

            set1 = set1
                .SetExistence(1.AsMachineId(), true)
                .SetExistence(2.AsMachineId(), true)
                .SetExistence(3.AsMachineId(), true);

            // Adding/removing the same locations
            var set2 = EmptyInstance.SetExistence(1.AsMachineId(), false)
                .SetExistence(2.AsMachineId(), false)
                .SetExistence(3.AsMachineId(), false);

            var merged = set1.MergeTest(set2);

            var setAsLocationChangeSet = (LocationChangeMachineIdSet)merged;
            Output.WriteLine("Set: " + string.Join(", ", setAsLocationChangeSet.LocationStates.Select(s => s.ToString())));

            merged.Count.Should().Be(0);
            setAsLocationChangeSet.LocationStates.Count().Should().Be(3);

            merged.Contains(1.AsMachineId()).Should().BeFalse();
        }

        [Fact]
        public void OverlappingMerge()
        {
            MachineIdSet set1 = EmptyInstance;

            set1 = set1
                .SetExistence(1.AsMachineId(), true)
                .SetExistence(2.AsMachineId(), false);

            // Adding/removing the same locations
            var set2 = EmptyInstance
                .SetExistence(1.AsMachineId(), false)
                .SetExistence(2.AsMachineId(), true);

            var merged = set1.MergeTest(set2);

            var setAsLocationChangeSet = (LocationChangeMachineIdSet)merged;
            Output.WriteLine("Set: " + string.Join(", ", setAsLocationChangeSet.LocationStates.Select(s => s.ToString())));

            merged.Count.Should().Be(1, "Removal should not affect the Count");
            setAsLocationChangeSet.LocationStates.Count().Should().Be(2);

            // Both machine ids should be present in the set.
            merged.Contains(1.AsMachineId()).Should().BeFalse();
            merged.Contains(2.AsMachineId()).Should().BeTrue();
        }
        
        [Fact]
        public void TestSerializationWithAdds()
        {
            var originalMachineIdSet = MachineSet(1, 5, 7, 10, 1001, 2232);
            var deserializedMachineIdSet = Copy(originalMachineIdSet);

            // Temporary remove.
            // deserializedMachineIdSet.GetType().Should().Be(originalMachineIdSet.GetType());

            var left = originalMachineIdSet.LocationStates.ToArray();
            var right = ((LocationChangeMachineIdSet)deserializedMachineIdSet).LocationStates.ToArray();
            left.Should().BeEquivalentTo(right);
        }

        [Fact]
        public void TestSerializationWithMixed()
        {
            var originalMachineIdSet = MachineSet(
                LocationChange.CreateAdd(1.AsMachineId()),
                LocationChange.CreateRemove(2.AsMachineId()),
                LocationChange.CreateAdd(5.AsMachineId()),
                LocationChange.CreateRemove(7.AsMachineId()),
                LocationChange.CreateAdd(12.AsMachineId()),
                LocationChange.CreateRemove(3.AsMachineId())
                );
            var deserializedMachineIdSet = Copy(originalMachineIdSet);

            // Assert.Equal(originalMachineIdSet.GetType(), deserializedMachineIdSet.GetType());

            var original = originalMachineIdSet.LocationStates.ToArray();
            var deserialized = ((LocationChangeMachineIdSet)deserializedMachineIdSet).LocationStates.ToArray();
            deserialized.Should().BeEquivalentTo(original);
        }

        private static MachineIdSet Copy(MachineIdSet source)
        {
            using (var memoryStream = new MemoryStream())
            {
                using (var writer = BuildXLWriter.Create(memoryStream, leaveOpen: true))
                {
                    source.Serialize(writer);
                }

                memoryStream.Position = 0;
                MachineIdSet readFromBinaryReader;
                using (var reader = BuildXLReader.Create(memoryStream))
                {
                    readFromBinaryReader = MachineIdSet.Deserialize(reader);
                }

                var data = memoryStream.ToArray().AsSpan().AsReader();
                MachineIdSet readFromSpan = MachineIdSet.Deserialize(ref data);
                Assert.Equal(readFromBinaryReader, readFromSpan);

                return readFromSpan;
            }
        }

        private LocationChangeMachineIdSet MachineSet(params int[] machineIdIndices)
        {
            return MachineSet(machineIdIndices.Select(idx => LocationChange.CreateAdd(MachineId.FromIndex(idx))).ToArray());
        }

        private LocationChangeMachineIdSet MachineSet(params LocationChange[] locationChanges)
        {
            var adds = MachineIdCollection.Create(locationChanges.Where(lc => lc.IsAdd).Select(lc => lc.AsMachineId()).ToArray());
            var removals = MachineIdCollection.Create(locationChanges.Where(lc => lc.IsRemove).Select(lc => lc.AsMachineId()).ToArray());

            // TODO: once possible (moved to .net core only) convert to use polymorphic return types for 'SetExistence'.
            return (LocationChangeMachineIdSet)EmptyInstance.SetExistence(adds, exists: true).SetExistence(removals, exists: false);
        }
    }
}
