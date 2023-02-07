// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Utilities.Serialization;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.Distributed.ContentLocation.NuCache
{
    public class MachineIdSetTests
    {
        [Theory]
        [InlineData(MachineIdSet.SetFormat.Array)]
        [InlineData(MachineIdSet.SetFormat.Bits)]
        [InlineData(MachineIdSet.SetFormat.LocationChange)]
        public void MachineIdIndexShouldBePresent(MachineIdSet.SetFormat format)
        {
            var set1 = CreateMachineIdSet(format);

            set1 = set1.Add(MachineId.FromIndex(1));
            set1.GetMachineIdIndex(MachineId.FromIndex(1)).Should().Be(0);
            set1.GetMachineIdIndex(MachineId.FromIndex(2)).Should().Be(-1);
        }

        [Theory]
        [InlineData(MachineIdSet.SetFormat.Array)]
        [InlineData(MachineIdSet.SetFormat.Bits)]
        [InlineData(MachineIdSet.SetFormat.LocationChange)]
        public void BitMachineIdSet_SetExistenceForEmptyTests(MachineIdSet.SetFormat format)
        {
            var set1 = MachineIdSet.Empty;

            set1 = set1.Add(MachineId.FromIndex(1));
            set1[1].Should().BeTrue();
        }

        [Theory]
        [InlineData(MachineIdSet.SetFormat.Array)]
        [InlineData(MachineIdSet.SetFormat.Bits)]
        [InlineData(MachineIdSet.SetFormat.LocationChange)]
        public void TestSetMachineId(MachineIdSet.SetFormat format)
        {
            var set1 = CreateMachineIdSet(format);
            set1 = set1.SetExistence(1.AsMachineId(), exists: true);
            set1 = set1.SetExistence(2.AsMachineId(), exists: true);
            set1 = set1.SetExistence(3.AsMachineId(), exists: true);

            var span = SerializeToSpan(set1);
            MachineIdSet.HasMachineId(span, 1).Should().BeTrue();
            MachineIdSet.HasMachineId(span, 2).Should().BeTrue();
            MachineIdSet.HasMachineId(span, 3).Should().BeTrue();

            set1 = set1.SetExistence(2.AsMachineId(), exists: false);
            span = SerializeToSpan(set1);

            MachineIdSet.HasMachineId(span, 2).Should().BeFalse();

            set1 = set1.SetExistence(MachineIdCollection.Create(Enumerable.Range(1, 100).Select(n => n.AsMachineId()).ToArray()), exists: true);
            set1 = set1.SetExistence(MachineIdCollection.Create(Enumerable.Range(101, 100).Select(n => n.AsMachineId()).ToArray()), exists: false);
            span = SerializeToSpan(set1);

            MachineIdSet.HasMachineId(span, 99).Should().BeTrue();
            MachineIdSet.HasMachineId(span, 105).Should().BeFalse();
        }

        private static MachineIdSet CreateMachineIdSet(MachineIdSet.SetFormat format)
        {
            return format switch
            {
                MachineIdSet.SetFormat.Bits => BitMachineIdSet.EmptyInstance,
                MachineIdSet.SetFormat.Array => ArrayMachineIdSet.EmptyInstance,
                MachineIdSet.SetFormat.LocationChange => LocationChangeMachineIdSet.EmptyInstance,
                _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
            };
        }

        [Theory]
        [InlineData(MachineIdSet.SetFormat.Array)]
        [InlineData(MachineIdSet.SetFormat.Bits)]
        [InlineData(MachineIdSet.SetFormat.LocationChange)]
        public void BitMachineIdSet_ExhaustiveTests(MachineIdSet.SetFormat format)
        {
            for (int length = 0; length < 15; length++)
            {
                for (int machineId = 0; machineId < length * 8; machineId++)
                {
                    MachineIdSet set = new BitMachineIdSet(new byte[length], 0);
                    set = set.Add(new MachineId(machineId));

                    var index = set.GetMachineIdIndex(new MachineId(machineId));
                    index.Should().NotBe(-1, $"Id={new MachineId(machineId)}, Ids in set=[{string.Join(", ", set.EnumerateMachineIds())}]");

                    // We recreating a set each time, so the index is always 0.
                    index.Should().Be(0);

                    (set.EnumerateMachineIds().ToList()[index]).Should().Be(new MachineId(machineId));

                    set.Count.Should().Be(1, $"Set byte length={length}, Id(bit offset)={machineId}");
                }
            }
        }

        [Theory]
        [InlineData(MachineIdSet.SetFormat.Array)]
        [InlineData(MachineIdSet.SetFormat.Bits)]
        [InlineData(MachineIdSet.SetFormat.LocationChange)]
        public void MachineIdSets_Are_TheSame_ExhaustiveTests(MachineIdSet.SetFormat format)
        {
            // This test makes sure that the main functionality provides the same results for both BitMachineIdSet and ArrayMachineIdSet
            for (int length = 0; length < 15; length++)
            {
                MachineIdSet set = new BitMachineIdSet(new byte[length], 0);

                for (int machineId = 0; machineId < length * 8; machineId++)
                {
                    set = set.Add(new MachineId(machineId));

                    var arrayIdSet = new ArrayMachineIdSet(set.EnumerateMachineIds().Select(i => (ushort)i.Index));
                    set.EnumerateMachineIds().Should().BeEquivalentTo(arrayIdSet.EnumerateMachineIds());

                    var indexFromArray = arrayIdSet.EnumerateMachineIds().ToList().IndexOf(new MachineId(machineId));
                    var index = set.GetMachineIdIndex(new MachineId(machineId));

                    index.Should().Be(indexFromArray);

                    var index2 = arrayIdSet.GetMachineIdIndex(new MachineId(machineId));
                    index2.Should().Be(index);
                }
            }
        }

        [Theory]
        [InlineData(MachineIdSet.SetFormat.Array)]
        [InlineData(MachineIdSet.SetFormat.Bits)]
        [InlineData(MachineIdSet.SetFormat.LocationChange)]
        public void MachineIdSet_ExhaustiveTests(MachineIdSet.SetFormat format)
        {
            for (int length = 0; length < 15; length++)
            {
                for (int machineId = 0; machineId < length * 8; machineId++)
                {
                    MachineIdSet set = new ArrayMachineIdSet(new ushort[0]);
                    set = set.Add(new MachineId(machineId));

                    set.GetMachineIdIndex(new MachineId(machineId)).Should().NotBe(-1);
                    set.Count.Should().Be(1, $"Set byte length={length}, Id(bit offset)={machineId}");
                }
            }
        }

        [Fact]
        public void TestSerializationFromArrayMachineIdSetToBitVersion()
        {
            var originalMachineIdSet = new ArrayMachineIdSet(Enumerable.Range(1, MachineIdSet.BitMachineIdSetThreshold + 1).Select(id => (ushort)id).ToArray());
            var deserializedMachineIdSet = Copy(originalMachineIdSet);

            Assert.NotEqual(originalMachineIdSet.GetType(), deserializedMachineIdSet.GetType());

            Assert.Equal(originalMachineIdSet.EnumerateMachineIds().OrderBy(x => x.Index), deserializedMachineIdSet.EnumerateMachineIds().OrderBy(x => x.Index));
        }

        [Fact]
        public void TestSerializationFromBitVersionToArrayMachineIdSet()
        {
            var originalMachineIdSet = MachineSet(1, 5, 7, 10, 1001, 2232);
            var deserializedMachineIdSet = Copy(originalMachineIdSet);

            Assert.NotEqual(originalMachineIdSet.GetType(), deserializedMachineIdSet.GetType());

            Assert.Equal(originalMachineIdSet.EnumerateMachineIds().OrderBy(x => x.Index), deserializedMachineIdSet.EnumerateMachineIds().OrderBy(x => x.Index));
        }

        internal static MachineIdSet Copy(MachineIdSet source)
        {
            var data = SerializeToSpan(source);
            var reader = data.AsReader();
            MachineIdSet readFromSpan = MachineIdSet.Deserialize(ref reader);
            if (source is SortedLocationChangeMachineIdSet sorted)
            {
                // Need to sort the source to guarantee equality in the next assert.
                source = new SortedLocationChangeMachineIdSet(
                    sorted.LocationStates.Sort(LocationChangeMachineIdSet.LocationChangeMachineIdComparer.Instance));
            }

            Assert.Equal(source, readFromSpan);

            return readFromSpan;
        }

        private static ReadOnlySpan<byte> SerializeToSpan(MachineIdSet source)
        {
            byte[] data = new byte[4 * 1024];
            var dataWriter = data.AsSpan().AsWriter();
            source.Serialize(ref dataWriter);
            return data.AsSpan(0, dataWriter.Position);
        }

        [Fact]
        public void SetExistenceTests()
        {
            var set1 = MachineSet(1, 2);
            set1[1].Should().BeTrue();
            set1[2].Should().BeTrue();

            set1 = set1.Remove(MachineId.FromIndex(1));
            set1[1].Should().BeFalse();

            set1 = set1.Add(MachineId.FromIndex(1));
            set1[1].Should().BeTrue();
        }

        private MachineIdSet CreateMachineIdSet(int startByteOffset, int length, params int[] setBits)
        {
            return new BitMachineIdSet(new byte[length], startByteOffset).Add(setBits.Select(b => MachineId.FromIndex(b)).ToArray());
        }

        private MachineIdSet MachineSet(params int[] setBits)
        {
            return CreateMachineIdSet(0, (setBits.Max() / 8) + 1, setBits);
        }
    }
}
