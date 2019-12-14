// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using BuildXL.Utilities.Collections;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Utilities.Collections
{
    public class MinMaxHeapTests : XunitBuildXLTest
    {
        public MinMaxHeapTests(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void EmptyHeapOperations()
        {
            var heap = new MinMaxHeap<double>(1, Comparer<double>.Default);
            Assert.True(heap.Count == 0);

            Assert.ThrowsAny<Exception>(() =>
            {
                var x = heap.Minimum;
            });

            Assert.ThrowsAny<Exception>(() =>
            {
                var x = heap.Maximum;
            });

            Assert.ThrowsAny<Exception>(() =>
            {
                var x = heap.PopMinimum();
            });

            Assert.ThrowsAny<Exception>(() =>
            {
                var x = heap.PopMaximum();
            });

            Assert.True(heap.Count == 0);
        }

        [Fact]
        public void SingleItemHeapOperations()
        {
            var heap = new MinMaxHeap<double>(1, Comparer<double>.Default);
            Assert.Equal(heap.Count, 0);

            heap.Push(Math.PI);
            Assert.Equal(heap.Count, 1);
            Assert.Equal(heap.Minimum, Math.PI);
            Assert.Equal(heap.Maximum, Math.PI);
            Assert.Equal(heap.PopMinimum(), Math.PI);
            Assert.Equal(heap.Count, 0);

            heap.Push(Math.E);
            Assert.Equal(heap.Count, 1);
            Assert.Equal(heap.PopMaximum(), Math.E);
            Assert.Equal(heap.Count, 0);
        }

        [Fact]
        public void HeapCapacity()
        {
            var heap = new MinMaxHeap<double>(1, Comparer<double>.Default);
            Assert.True(heap.Capacity == 1);
            Assert.True(heap.Count == 0);

            heap.Push(Math.PI);
            Assert.True(heap.Capacity == 1);
            Assert.True(heap.Count == 1);
            Assert.ThrowsAny<Exception>(() =>
            {
                heap.Push(Math.E);
            });
            Assert.True(heap.Capacity == 1);
            Assert.True(heap.Count == 1);
        }

        [Fact]
        public void PopCorrectness()
        {
            // Generate random values
            var rng = new Random(1);
            var values = new double[20];
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = -Math.Log(rng.NextDouble());
            }

            // Push them on the heap
            var heap = new MinMaxHeap<double>(values.Length, Comparer<double>.Default);
            foreach (var value in values)
            {
                heap.Push(value);
            }

            // Sort them, so we know what order they should come out in
            Array.Sort(values);

            // Pop elements and make sure they come out in that order
            var minIndex = 0;
            var maxIndex = values.Length - 1;
            for (var i = 0; i < values.Length; i++)
            {
                Assert.Equal(heap.Count, values.Length - i);
                Assert.Equal(heap.Minimum, values[minIndex]);
                Assert.Equal(heap.Maximum, values[maxIndex]);

                // Randomly pop either minimum or maximum
                if (rng.NextDouble() < 0.5)
                {
                    Assert.Equal(heap.PopMinimum(), values[minIndex]);
                    minIndex++;
                }
                else
                {
                    Assert.Equal(heap.PopMaximum(), values[maxIndex]);
                    maxIndex--;
                }
            }
        }

        [Fact]
        public void PushOrderIrrelevant()
        {
            double[] values = { 1.0, 2.0, 3.0, 4.0, 5.0 };

            // Insert values in different orders, make sure
            // resuts are same

            var rng = new Random(1);
            for (var i = 0; i < 10; i++)
            {
                // Shuffle values
                for (var j = 0; j < values.Length; j++)
                {
                    var k = rng.Next(j, values.Length);
                    var t = values[j];
                    values[j] = values[k];
                    values[k] = t;
                }

                // Push in shuffled order
                var heap = new MinMaxHeap<double>(values.Length, Comparer<double>.Default);
                foreach (var value in values)
                {
                    heap.Push(value);
                }

                // Pop order should always be same
                Assert.Equal(heap.Count, 5);
                Assert.Equal(heap.PopMinimum(), 1.0);
                Assert.Equal(heap.PopMaximum(), 5.0);
                Assert.Equal(heap.PopMaximum(), 4.0);
                Assert.Equal(heap.PopMinimum(), 2.0);
                Assert.Equal(heap.PopMinimum(), 3.0);
                Assert.Equal(heap.Count, 0);
            }
        }

        [Fact]
        public void ClearHeap()
        {
            var heap = new MinMaxHeap<double>(2, Comparer<double>.Default);
            Assert.Equal(heap.Count, 0);

            heap.Push(Math.E);
            Assert.Equal(heap.Count, 1);

            heap.Clear();
            Assert.Equal(heap.Count, 0);

            heap.Push(Math.PI);
            Assert.Equal(heap.Minimum, Math.PI);
        }
    }
}
