// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.FrontEnd.Core.Incrementality;
using Xunit;

namespace Test.DScript.Ast.Incrementality
{
    public class TestConcurrentBitArrayPersistence
    {
        private readonly PathTable m_pathTable;
        private readonly RoarerBitSetEqualityComparer m_comparer;

        public TestConcurrentBitArrayPersistence()
        {
            m_pathTable = new PathTable();
            m_comparer = new RoarerBitSetEqualityComparer(m_pathTable);
        }

        [Fact]
        public void SerializeEmptyVector()
        {
            // Arrange
            var bitArray = RoaringBitSet.FromBitArray(new ConcurrentBitArray(42));

            // Act
            var copy = Copy(bitArray, m_pathTable);

            // Assert
            Assert.Equal(bitArray, copy, m_comparer);
        }

        [Fact]
        public void SerializeLargeArrayWithRandomBitsSet()
        {
            // Arrange

            // Tests that relies on random data considered harmful, but in this particular case
            // it is safe.
            // Better way would be to use property-based tests instead.
            const int Length = 1024;
            var bitArray = Factories.CreateBitSetWithRandomContent(Length);

            // Act
            var copy = Copy(bitArray, m_pathTable);

            // Assert
            Assert.Equal(bitArray, copy, m_comparer);
        }

        private static RoaringBitSet Copy(RoaringBitSet bitArray, PathTable pathTable)
        {
            bitArray.MaterializeSet(pathTable);

            using (var ms = new MemoryStream())
            {
                BuildXLWriter writer = new BuildXLWriter(true, ms, true, true);
                FrontEndSnapshotSerializer.SerializeBitSet(bitArray, writer);

                ms.Position = 0;
                BuildXLReader reader = new BuildXLReader(true, ms, true);
                return FrontEndSnapshotSerializer.DeserializeBitVector(reader);
            }
        }
    }
}
