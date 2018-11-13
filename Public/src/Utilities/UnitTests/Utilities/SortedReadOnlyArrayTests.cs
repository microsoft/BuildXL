// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using BuildXL.Utilities.Collections;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using SortedReadOnlyIntArray = BuildXL.Utilities.Collections.SortedReadOnlyArray<int, System.Collections.Generic.IComparer<int>>;

namespace Test.BuildXL.Utilities
{
    public sealed class SortedReadOnlyArrayTests : XunitBuildXLTest
    {
        public SortedReadOnlyArrayTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void SimpleSort()
        {
            SortAndXAssertValid(1, 2, 3);
        }

        [Fact]
        public void ConversionIdentity()
        {
            ReadOnlyArray<int> original = ReadOnlyArray<int>.From(new int[] { 1, 2, 3 });
            XAssert.AreEqual(
                SortedReadOnlyIntArray.FromSortedArrayUnsafe(original, Comparer<int>.Default),
                SortedReadOnlyIntArray.FromSortedArrayUnsafe(original, Comparer<int>.Default));
        }

        [Fact]
        public void SortExistingArray()
        {
            var original = new int[] { 3, 1, 2 };
            VerifySort(SortedReadOnlyIntArray.CloneAndSort(ReadOnlyArray<int>.From(original), Comparer<int>.Default), original);
        }

        [Fact]
        public void BinarySearch()
        {
            SortedReadOnlyIntArray sorted = SortAndXAssertValid(1, 2, 10, 20, 30);
            XAssert.AreEqual(2, sorted.BinarySearch(10, 0, sorted.Length));
            XAssert.AreEqual(~3, sorted.BinarySearch(11, 0, sorted.Length));
            XAssert.IsTrue(~sorted.BinarySearch(100, 0, sorted.Length) >= sorted.Length);

            XAssert.AreEqual(2, sorted.BinarySearch(10, 1, sorted.Length - 1));
            XAssert.AreEqual(~3, sorted.BinarySearch(11, 1, sorted.Length - 1));
            XAssert.IsTrue(~sorted.BinarySearch(100, 1, sorted.Length - 1) >= sorted.Length);
        }

        [Fact]
        public void ExceptWithToEmpty()
        {
            SortedReadOnlyIntArray excepted = VerifyExceptWith(
                SortAndXAssertValid(1, 2, 3),
                SortAndXAssertValid(-1, 1, 5, 6),
                SortAndXAssertValid(2, 2, 2, 42),
                SortAndXAssertValid(3));

            XAssert.AreEqual(0, excepted.Length);
        }

        [Fact]
        public void ExceptWithDuplicates()
        {
            SortedReadOnlyIntArray excepted = VerifyExceptWith(
                SortAndXAssertValid(2, 2, 2, 3),
                SortAndXAssertValid(2));

            XAssert.AreEqual(1, excepted.Length);
            XAssert.AreEqual(3, excepted[0]);

            excepted = VerifyExceptWith(
                SortAndXAssertValid(1, 2, 2, 2),
                SortAndXAssertValid(2));

            XAssert.AreEqual(1, excepted.Length);
            XAssert.AreEqual(1, excepted[0]);
        }

        [Fact]
        public void ExceptWithNonOverlapping()
        {
            SortedReadOnlyIntArray excepted = VerifyExceptWith(
                SortAndXAssertValid(1, 2, 3),
                SortAndXAssertValid(-1, -2, -3),
                SortAndXAssertValid(10, 20, 30));

            XAssert.AreEqual(3, excepted.Length);
            XAssert.AreEqual(1, excepted[0]);
            XAssert.AreEqual(2, excepted[1]);
            XAssert.AreEqual(3, excepted[2]);
        }

        [Fact]
        public void ExceptWithStress()
        {
            const int NumExcepts = 4;
            var rng = new Random(unchecked((int)0xF00DCAFE));

            for (int n = 10; n < 1000; n *= 10)
            {
                SortedReadOnlyIntArray values = CreateRandomArray(n * 5, rng);

                var excepts = new SortedReadOnlyIntArray[NumExcepts];
                for (int i = 0; i < NumExcepts; i++)
                {
                    excepts[i] = CreateRandomArray(n, rng);
                }

                VerifyExceptWith(values, excepts);
            }
        }

        private static SortedReadOnlyIntArray CreateRandomArray(int size, Random rng)
        {
            var array = new int[size];
            for (int i = 0; i < size; i++)
            {
                array[i] = rng.Next(int.MaxValue);
            }

            return SortedReadOnlyIntArray.SortUnsafe(array, Comparer<int>.Default);
        }

        private static SortedReadOnlyIntArray VerifyExceptWith(SortedReadOnlyIntArray values, params SortedReadOnlyIntArray[] exceptArrays)
        {
            var exceptSet = new HashSet<int>();
            foreach (var array in exceptArrays)
            {
                foreach (int item in array)
                {
                    exceptSet.Add(item);
                }
            }

            var valuesSet = new HashSet<int>(values);
            valuesSet.ExceptWith(exceptSet);

            SortedReadOnlyIntArray expectedSortedExcepted = SortedReadOnlyIntArray.CloneAndSort(valuesSet, Comparer<int>.Default);
            SortedReadOnlyIntArray actualSortedExcepted = values.ExceptWith(exceptArrays);

            XAssert.AreEqual(expectedSortedExcepted.Length, actualSortedExcepted.Length);
            for (int i = 0; i < expectedSortedExcepted.Length; i++)
            {
                XAssert.AreEqual(expectedSortedExcepted[i], actualSortedExcepted[i]);
            }

            return actualSortedExcepted;
        }

        private static void VerifySort(SortedReadOnlyIntArray array, int[] original)
        {
            for (int i = 0; i < array.Length - 1; i++)
            {
                XAssert.IsTrue(array[i] <= array[i + 1]);
            }

            XAssert.AreEqual(original.Length, array.Length);
        }

        private static SortedReadOnlyIntArray SortAndXAssertValid(params int[] values)
        {
            SortedReadOnlyIntArray result = SortedReadOnlyIntArray.CloneAndSort(values, Comparer<int>.Default);
            VerifySort(result, values);
            return result;
        }
    }
}
