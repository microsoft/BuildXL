// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using BuildXL.Utilities.PackedTable;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.Tool.Analyzers
{
    /// <summary>
    /// Tests for Parallel sorting extension method for Memory[T]
    /// </summary>
    public class ParallelMemorySortExtensionsTests : XunitBuildXLTest
    {
        public ParallelMemorySortExtensionsTests(ITestOutputHelper output) : base(output)
        {
        }

        /// <summary>Confirm data is properly sorted</summary>
        public static void ConfirmSorted(int[] data)
        {
            for (int i = 1; i < data.Length; i++)
            {
                if (data[i - 1] > data[i])
                {
                    throw new Exception("Sorting failure");
                }
            }
        }

        /// <summary>Test sorting the input array in a few ways</summary>
        public static void TestSort(int[] data, int parallelism = -1)
        {
            int[] copy1 = (int[])data.Clone();
            ParallelMemorySortExtensions.ParallelSort<int>(copy1, (i, j) => i.CompareTo(j), minimumSubspanSize: 1, parallelism: parallelism);
            ConfirmSorted(copy1);

            int[] copy2 = (int[])data.Clone();
            ParallelMemorySortExtensions.ParallelSort<int>(copy2, (i, j) => i.CompareTo(j), minimumSubspanSize: 3, parallelism: parallelism);
            ConfirmSorted(copy2);
        }

        /// <summary>Test sorting already-sorted data</summary>
        [Fact]
        public void TestSortedSort()
        {
            TestSort(new int[] { 1 });
            TestSort(new int[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 });
        }

        /// <summary>Test sorting antisorted data</summary>
        [Fact]
        public void TestReverseSort()
        {
            TestSort(new int[] { 9, 8, 7, 6, 5, 4, 3, 2, 1 });
        }

        /// <summary>Test sorting interleaved data</summary>
        [Fact]
        public void TestInterleavedSort()
        {
            TestSort(new int[] { 1, 4, 7, 2, 5, 8, 3, 6, 9 });
        }

        /// <summary>Test sorting disordered data</summary>
        [Fact]
        public void TestRandomSort()
        {
            TestSort(new int[] { 8, 4, 3, 5, 6, 1, 9, 2, 7 });
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(7)]
        [InlineData(8)]
        [InlineData(16)]
        [InlineData(20)]
        public void TestSortParallelism(int parallelism)
        {
            TestSort(new int[] { 8, 4, 3, 5, 6, 1, 9, 2, 7 }, parallelism);
        }
    }
}
