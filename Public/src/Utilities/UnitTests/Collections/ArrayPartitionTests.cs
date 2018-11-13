// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using BuildXL.Utilities.Collections;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Utilities
{
    /// <summary>
    /// Tests for <see cref="CollectionUtilities.Partition{TValue}" />
    /// </summary>
    public class ArrayPartitionTests
    {
        [Fact]
        public void PartitionEmpty()
        {
            int[] values = { };

            ArrayView<int> trueValues;
            ArrayView<int> falseValues;
            values.Partition(
                v =>
                {
                    XAssert.Fail("No values should be queried");
                    return false;
                },
                out trueValues,
                out falseValues);

            XAssert.AreEqual(0, trueValues.Length);
            XAssert.AreEqual(0, falseValues.Length);
        }

        [Fact]
        public void PartitionSingle()
        {
            int[] values = { 123 };

            VerifyCreateSinglePartition(values, true);
            VerifyCreateSinglePartition(values, false);
        }

        [Fact]
        public void PartitionMultipleToSinglePartitionOdd()
        {
            int[] values = { 1, 2, 3, 4, 5, 6, 7 };

            VerifyCreateSinglePartition(values, true);
            VerifyCreateSinglePartition(values, false);
        }

        [Fact]
        public void PartitionMultipleToSinglePartitionEven()
        {
            int[] values = { 1, 2, 3, 4, 5, 6, 7, 8 };

            VerifyCreateSinglePartition(values, true);
            VerifyCreateSinglePartition(values, false);
        }

        [Fact]
        public void PartitionEvenOddAlternating()
        {
            int[] values = { 1, 2, 3, 4, 5, 6, 7, 8 };

            VerifyCreateEvenOddPartition(values, evenPartition: true);
            VerifyCreateEvenOddPartition(values, evenPartition: false);
        }

        [Fact]
        public void PartitionEvenOddAlternating2()
        {
            int[] values = { 1, 2, 3, 4, 5, 6, 7 };

            VerifyCreateEvenOddPartition(values, evenPartition: true);
            VerifyCreateEvenOddPartition(values, evenPartition: false);
        }

        [Fact]
        public void PartitionEvenOddAlreadyPartitioned()
        {
            int[] values = { 1, 3, 5, 7, 2, 4, 6, 8 };

            VerifyCreateEvenOddPartition(values, evenPartition: true);
            VerifyCreateEvenOddPartition(values, evenPartition: false);
        }

        [Fact]
        public void PartitionEvenOddAlreadyPartitioned2()
        {
            int[] values = { 2, 4, 6, 8, 1, 3, 5, 7 };

            VerifyCreateEvenOddPartition(values, evenPartition: true);
            VerifyCreateEvenOddPartition(values, evenPartition: false);
        }

        private static void VerifyCreateSinglePartition(int[] values, bool partitionAs)
        {
            ArrayView<int> trueValues;
            ArrayView<int> falseValues;

            int[] partitioned = new int[values.Length];
            Array.Copy(values, partitioned, values.Length);

            partitioned.Partition(
                v => partitionAs,
                out trueValues,
                out falseValues);

            if (partitionAs)
            {
                XAssert.AreEqual(values.Length, trueValues.Length);
                XAssert.AreEqual(0, falseValues.Length);

                for (int i = 0; i < values.Length; i++)
                {
                    XAssert.AreEqual(values[i], trueValues[i]);
                }
            }
            else
            {
                XAssert.AreEqual(values.Length, falseValues.Length);
                XAssert.AreEqual(0, trueValues.Length);

                for (int i = 0; i < values.Length; i++)
                {
                    XAssert.AreEqual(values[i], falseValues[i]);
                }
            }
        }

        private static void VerifyCreateEvenOddPartition(int[] values, bool evenPartition)
        {
            ArrayView<int> trueValues;
            ArrayView<int> falseValues;

            int[] partitioned = new int[values.Length];
            Array.Copy(values, partitioned, values.Length);

            partitioned.Partition(
                v => (v % 2 == 0) ^ !evenPartition,
                out trueValues,
                out falseValues);

            var found = new List<int>();
            foreach (int trueValue in trueValues)
            {
                found.Add(trueValue);
                XAssert.IsTrue((trueValue % 2 == 0) == evenPartition);
            }

            foreach (int falseValue in falseValues)
            {
                found.Add(falseValue);
                XAssert.IsTrue((falseValue % 2 == 0) != evenPartition);
            }

            found.Sort();
            var expected = new List<int>(values);
            expected.Sort();

            XAssert.AreEqual(expected.Count, found.Count);
            for (int i = 0; i < expected.Count; i++)
            {
                XAssert.AreEqual(expected[i], found[i]);
            }
        }
    }
}
