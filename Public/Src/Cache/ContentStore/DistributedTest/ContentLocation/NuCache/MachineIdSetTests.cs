// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Linq;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Utilities;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.Distributed.ContentLocation.NuCache
{
    public class MachineIdSetTests
    {
        [Fact]
        public void MachineIdIndexShouldBePresent()
        {
            var set1 = MachineIdSet.Empty;

            set1 = set1.Add(MachineId.FromIndex(1));
            set1.GetMachineIdIndex(MachineId.FromIndex(1)).Should().Be(0);
            set1.GetMachineIdIndex(MachineId.FromIndex(2)).Should().Be(-1);
        }

        [Fact]
        public void BitMachineIdSet_SetExistenceForEmptyTests()
        {
            var set1 = MachineIdSet.Empty;

            set1 = set1.Add(MachineId.FromIndex(1));
            set1[1].Should().BeTrue();
        }

        [Fact]
        public void BitMachineIdSet_ExhaustiveTests()
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

        [Fact]
        public void MachineIdSets_Are_TheSame_ExhaustiveTests()
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

        [Fact]
        public void ArrayMachineIdSet_ExhaustiveTests()
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

        private static MachineIdSet Copy(MachineIdSet source)
        {
            using (var memoryStream = new MemoryStream())
            {
                using (var writer = BuildXLWriter.Create(memoryStream, leaveOpen: true))
                {
                    source.Serialize(writer);
                }

                memoryStream.Position = 0;

                using (var reader = BuildXLReader.Create(memoryStream))
                {
                    return MachineIdSet.Deserialize(reader);
                }
            }
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
