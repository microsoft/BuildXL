// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Runtime.InteropServices;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Utilities
{
    public sealed class SpanSortHelperTests
    {
        [Fact]
        public void TestAll()
        {
            // Test basic sort
            EnsureEquivalent(
                a => { Array.Sort(a); return a; },
                a => { SpanSortHelper.Sort(a.AsSpan()); return a; });

            // Test bucket sort w/ single-threaded
            EnsureEquivalent(
                a => { Array.Sort(a); return a; },
                a =>
                {
                    SpanSortHelper.BucketSort(
                        () => a.AsSpan(),
                        i => MemoryMarshal.Read<short>(MemoryMarshal.AsBytes(stackalloc[] { i }).Slice(2)) - (int)short.MinValue,
                        (int)ushort.MaxValue + 1);
                    return a;
                });

            // Test bucket sort w/ parallelism
            EnsureEquivalent(
                a => { Array.Sort(a); return a; },
                a =>
                {
                    SpanSortHelper.BucketSort(
                        () => a.AsSpan(),
                        i => MemoryMarshal.Read<short>(MemoryMarshal.AsBytes(stackalloc[] { i }).Slice(2)) - (int)short.MinValue,
                        (int)ushort.MaxValue + 1,
                        parallelism: 3);
                    return a;
                });

            // Test binary search existing element
            EnsureEquivalent(
                a => { Array.Sort(a); return Array.BinarySearch(a, a[25]); },
                a => { SpanSortHelper.Sort(a.AsSpan()); return SpanSortHelper.BinarySearch(a, a[25]); });

            // Test binary search non-existing element (average between two consecutive elements to get element which probably doesn't exist)
            EnsureEquivalent(
                a => { Array.Sort(a); return Array.BinarySearch(a, (int)(((long)a[94] + a[93]) / 2)); },
                a => { SpanSortHelper.Sort(a.AsSpan()); return SpanSortHelper.BinarySearch(a, (int)(((long)a[94] + a[93]) / 2)); });
        }

        private void EnsureEquivalent<TResult>(
            Func<int[], TResult> expected,
            Func<int[], TResult> actual)
            where TResult : IEquatable<TResult>
        {
            var r = new Random();
            var bytes = new byte[1024];
            r.NextBytes(bytes);

            var ints = MemoryMarshal.Cast<byte, int>(bytes);

            Assert.Equal(expected(ints.ToArray()), actual(ints.ToArray()));
        }

        private void EnsureEquivalent<TResult>(
            Func<int[], TResult[]> expected,
            Func<int[], TResult[]> actual)
        {
            var r = new Random();
            var bytes = new byte[1024];
            r.NextBytes(bytes);

            var ints = MemoryMarshal.Cast<byte, int>(bytes);

            Assert.Equal<TResult>(expected(ints.ToArray()), actual(ints.ToArray()));
        }
    }
}