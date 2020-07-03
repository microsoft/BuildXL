// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Utilities.Collections;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Utilities
{
    /// <summary>
    /// Tests for ConcurrentBoundedSortedList
    /// </summary>
    public class ConcurrentBoundedSortedListTests : XunitBuildXLTest
    {
        public ConcurrentBoundedSortedListTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void AddWithSortValueBelowDefault()
        {
            ConcurrentBoundedSortedCollection<int, int> cbsl = new ConcurrentBoundedSortedCollection<int, int>(10);

            cbsl.TryAdd(-30, 0);

            Assert.Equal(1, cbsl.Count());

            int value = cbsl.First().Value;
            Assert.Equal(0, value);
        }

        [Fact]
        public void AddToCapacity()
        {
            ConcurrentBoundedSortedCollection<long, int> cbsl = new ConcurrentBoundedSortedCollection<long, int>(4);
            cbsl.TryAdd(1, 1);
            cbsl.TryAdd(3, 3);
            cbsl.TryAdd(-1, -1);
            cbsl.TryAdd(0, 0);

            Assert.Equal(4, cbsl.Count());

            Assert.Equal(-1, cbsl.ElementAt(0).Value);
            Assert.Equal(0, cbsl.ElementAt(1).Value);
            Assert.Equal(1, cbsl.ElementAt(2).Value);
            Assert.Equal(3, cbsl.ElementAt(3).Value);
        }

        [Fact]
        public void AddBeyondCapacity()
        {
            ConcurrentBoundedSortedCollection<int, string> cbsl = new ConcurrentBoundedSortedCollection<int, string>(3);
            cbsl.TryAdd(1, "1");
            cbsl.TryAdd(5, "5");
            cbsl.TryAdd(3, "3");
            cbsl.TryAdd(2, "2"); // Should remove "1"
            cbsl.TryAdd(4, "4"); // Should remove "2"

            Assert.Equal(cbsl.Capacity, cbsl.Count());

            Assert.Equal("3", cbsl.ElementAt(0).Value);
            Assert.Equal("4", cbsl.ElementAt(1).Value);
            Assert.Equal("5", cbsl.ElementAt(2).Value);
        }

        [Theory]
        [InlineData(5)]
        [InlineData(10)]
        public void AddParallelManyElements(int capacity)
        {
            Random r = new Random();
            ConcurrentBoundedSortedCollection<int, int> cbsl = new ConcurrentBoundedSortedCollection<int, int>(capacity);

            // Build array of random ints
            int arraySize = 400;
            int[] valArray = new int[arraySize];
            for (int i = 0; i < arraySize; i++)
            {
                valArray[i] = r.Next();
            }

            Parallel.ForEach(valArray, i => cbsl.TryAdd(i, i));

            XAssert.ArrayEqual(valArray.OrderByDescending(i => i).Take(capacity).Reverse().ToArray(), cbsl.Select(kvp => kvp.Key).ToArray());
        }

        [Theory]
        [MemberData(nameof(BoundedExhaustiveTestData), 4)]
        public void BoundedExhaustiveTest(int[] arr, int capacity)
        {
            // expected: top 'capacity' elements in the sorted version of 'arr'
            var expectedResult = arr.OrderBy(i => i).Reverse().Take(capacity).Reverse().ToArray();

            var sl = new ConcurrentBoundedSortedCollection<int, int>(capacity);
            Parallel.ForEach(arr, e => sl.TryAdd(e, e));
            var slArray = sl.Select(kvp => kvp.Key).ToArray();
            XAssert.ArrayEqual(expectedResult, slArray);
        }

        public static IEnumerable<object[]> BoundedExhaustiveTestData(int arraySize)
        {
            var allPermutations = GenAllPermutations(Enumerable.Range(0, arraySize).Cast<object>().ToArray());
            var allCapacities = Enumerable.Range(1, arraySize + 1).Cast<object>();
            return CrossProduct(allPermutations.ToArray(), allCapacities.ToArray());
        }

        private static IEnumerable<T[]> GenAllPermutations<T>(T[] arr)
        {
            Contract.Requires(arr != null && arr.Length > 0);
            return arr.Length == 1
              ? new[] { arr }
              : Enumerable
                  .Range(0, arr.Length)
                  .SelectMany(i =>
                  {
                      var head = new[] { arr[i] };
                      var tail = arr.Take(i).Concat(arr.Skip(i + 1)).ToArray();
                      return GenAllPermutations(tail).Select(perm => head.Concat(perm).ToArray());
                  });
        }
    }
}
