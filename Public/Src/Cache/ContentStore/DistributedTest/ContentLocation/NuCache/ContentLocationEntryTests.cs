// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Buffers;
using System.Linq;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Serialization;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using static ContentStoreTest.Distributed.ContentLocation.NuCache.ContentLocationEntryTestHelpers;

namespace ContentStoreTest.Distributed.ContentLocation.NuCache
{
    public class ContentLocationEntryTests
    {
        private readonly ITestOutputHelper _testOutputHelper;

        public ContentLocationEntryTests(ITestOutputHelper testOutputHelper)
        {
            _testOutputHelper = testOutputHelper;
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(5000)]
        public void PooledSerializationDeserializationTest(int locationCount)
        {
            var entry = CreateEntry(CreateMachineIdSet(Enumerable.Range(1, locationCount).Select(l => l.AsMachineId())));
            var copy = entry.CloneWithSpan(usePooledSerialization: true);

            copy.Should().Be(entry, $"Cloning with span should give equivalent {nameof(ContentLocationEntry)}");
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(10)]
        [InlineData(100)]
        [InlineData(5000)]
        public void SerializationDeserializationTest(int locationCount)
        {
            SerializationDeserializationTestCore(locationCount);
        }

        [Fact]
        public void SerializationDeserializationTest42()
        {
            int locationCount = 42;

            SerializationDeserializationTestCore(locationCount);
        }

        public void SerializationDeserializationTestCore(int locationCount)
        {
            var entry = CreateEntry(CreateMachineIdSet(Enumerable.Range(1, locationCount).Select(l => l.AsMachineId())));
            var copy = entry.CloneWithSpan();

            copy.Should().Be(entry, $"Cloning with span should give equivalent {nameof(ContentLocationEntry)}");
        }

        [Fact]
        public void MergeWithRemovals()
        {
            var machineId = 42.AsMachineId();
            var machineId2 = 43.AsMachineId();

            // Using stable time to improve debuggability of this test.
            var leftEntryCreationTime = new DateTime(2022, 10, 1, hour: 1, minute: 2, second: 3).ToUniversalTime();
            var rightEntryCreationTime = new DateTime(2022, 10, 1, hour: 1, minute: 2, second: 4).ToUniversalTime();

            var leftEntryLastAccessTime = leftEntryCreationTime + TimeSpan.FromSeconds(1);
            var rightEntryLastAccessTime = rightEntryCreationTime + TimeSpan.FromSeconds(5);

            var left = CreateEntry(CreateChangeSet(exists: true, machineId), lastAccessTimeUtc: leftEntryLastAccessTime, leftEntryCreationTime);

            left.Locations.Contains(machineId).Should().BeTrue();

            var right = CreateEntry(CreateChangeSet(exists: true, machineId2), lastAccessTimeUtc: rightEntryLastAccessTime, rightEntryCreationTime);

            var merge = left.MergeTest(right);

            merge.Locations.Count.Should().Be(2);

            merge.Locations.Contains(machineId).Should().BeTrue();
            merge.Locations.Contains(machineId2).Should().BeTrue();

            var removalLastAccessTime = rightEntryLastAccessTime + TimeSpan.FromSeconds(3);
            var removal = CreateEntry(CreateChangeSet(exists: false, machineId2), lastAccessTimeUtc: removalLastAccessTime, rightEntryCreationTime);
            merge = merge.MergeTest(removal);

            merge.Locations.Count.Should().Be(1);

            merge.Locations.Contains(machineId).Should().BeTrue();
            merge.Locations.Contains(machineId2).Should().BeFalse();
            merge.CreationTimeUtc.Should().Be(leftEntryCreationTime);
            merge.LastAccessTimeUtc.Should().Be(removalLastAccessTime);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void TestAssociativity(bool useMergeableTouch)
        {
            // Making sure that the merge works even if the left hand side of the Merge call
            // is not a state change instance.

            // If the touch event is crated with an ArrayMachineIdSet instance, the merge won't work.
            var touch = CreateEntry(useMergeableTouch ? MachineIdSet.EmptyChangeSet : MachineIdSet.Empty);
            var remove = CreateEntry(CreateChangeSet(exists: false, 1.AsMachineId()));
            var add = CreateEntry(CreateChangeSet(exists: true, 1.AsMachineId()));

            // add + (remove + touch)
            var merged = add.MergeTest(remove.MergeTest(touch));
            Assert.Equal(0, merged.Locations.Count);

            // add + (touch + remove)
            var temp = touch.MergeTest(remove);
            merged = add.MergeTest(temp);
            Assert.Equal(0, merged.Locations.Count);

            // (add + remove) + touch
            merged = add.MergeTest(remove).MergeTest(touch);

            Assert.Equal(0, merged.Locations.Count);

            // This is not the right associativity, but still worth checking
            // (add + remove) + touch
            merged = add.MergeTest(touch).MergeTest(remove);

            Assert.Equal(0, merged.Locations.Count);
            
            // (add + (touch + add)) + remove
            merged = add.MergeTest(touch.MergeTest(add).MergeTest(remove));
            Assert.Equal(0, merged.Locations.Count);

            // add + ((touch + add) + remove)
            merged = add.MergeTest(touch.MergeTest(add).MergeTest(remove));
            Assert.Equal(0, merged.Locations.Count);
        }

        [Fact]
        public void TestAssociativityWithNonMergeableSet()
        {
            // Its important that all the merge operations are associative and
            // regardless of the combination of them we should never loose any state changes.

            // If the touch event is crated with an ArrayMachineIdSet instance, the merge won't work.
            var touch = CreateEntry(MachineIdSet.EmptyChangeSet);
            var remove = CreateEntry(CreateChangeSet(exists: false, 1.AsMachineId()));
            var add = CreateEntry(CreateChangeSet(exists: true, 1.AsMachineId()));

            // add + (remove + touch)
            var merged = add.MergeTest(remove.MergeTest(touch));
            Assert.Equal(0, merged.Locations.Count);

            // add + (touch + remove)
            var temp = touch.MergeTest(remove);
            merged = add.MergeTest(temp);
            Assert.Equal(0, merged.Locations.Count);

            // (add + remove) + touch
            merged = add.MergeTest(remove).MergeTest(touch);

            Assert.Equal(0, merged.Locations.Count);

            // This is not the right associativity, but still worth checking
            // (add + remove) + touch
            merged = add.MergeTest(touch).MergeTest(remove);

            Assert.Equal(0, merged.Locations.Count);
            
            // (add + (touch + add)) + remove
            merged = add.MergeTest(touch.MergeTest(add).MergeTest(remove));
            Assert.Equal(0, merged.Locations.Count);

            // add + ((touch + add) + remove)
            merged = add.MergeTest(touch.MergeTest(add).MergeTest(remove));
            Assert.Equal(0, merged.Locations.Count);
        }

        [Fact]
        public void MergeWithLastTouch()
        {
            var machineId = new MachineId(42);
            var machineId2 = new MachineId(43);

            var left = CreateEntry(CreateChangeSet(exists: true, machineId));
            var right = CreateEntry(CreateChangeSet(exists: true, machineId2));

            var merge = left.MergeTest(right);

            var removal = CreateEntry(CreateChangeSet(exists: false, machineId2));
            merge = merge.MergeTest(removal);

            var touch = CreateEntry(MachineIdSet.Empty);
            merge = merge.MergeTest(touch);

            merge.Locations.Contains(machineId).Should().BeTrue();
            merge.Locations.Contains(machineId2).Should().BeFalse();
        }

        internal static ContentLocationEntry CreateEntry(MachineIdSet machineIdSet, UnixTime? lastAccessTimeUtc = null, UnixTime? creationTimeUtc = null) => ContentLocationEntry.Create(
            machineIdSet,
            contentSize: 42,
            lastAccessTimeUtc: lastAccessTimeUtc ?? (DateTime.UtcNow - TimeSpan.FromMinutes(5)).ToUnixTime(),
            creationTimeUtc: creationTimeUtc ?? UnixTime.UtcNow);
    }
}
