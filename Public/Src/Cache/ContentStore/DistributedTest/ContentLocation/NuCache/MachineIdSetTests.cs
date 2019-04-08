// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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

                    set.Count.Should().Be(1, $"Set byte length={length}, Id(bit offset)={machineId}");
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
