// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

// This file is mostly a copy of ArraySortHelper<T> used by the BCL to sort arrays/spans.
// This is needed to allow some augmentations and to add support for sorting spans on full framework.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

#nullable disable
#nullable enable annotations

namespace BuildXL.Utilities.Collections
{
    /// <summary>
    /// Extension methods for sorting spans
    /// </summary>
    public static class SpanSortHelper
    {
        /// <nodoc/>
        public static int BinarySearch<T>(this ReadOnlySpan<T> array, T value, IComparer<T>? comparer = null)
        {
            comparer ??= Comparer<T>.Default;
            return SpanSortHelper<T>.InternalBinarySearch(array, 0, array.Length, value, comparer);
        }

        /// <nodoc/>
        public static int BinarySearch<T>(this ReadOnlySpan<T> array, int index, int length, T value, IComparer<T>? comparer = null)
        {
            comparer ??= Comparer<T>.Default;
            return SpanSortHelper<T>.InternalBinarySearch(array, index, length, value, comparer);
        }

        /// <nodoc/>
        public static void Sort<T>(this Span<T> keys, IComparer<T>? comparer = null)
        {
            comparer ??= Comparer<T>.Default;
            SpanSortHelper<T>.IntrospectiveSort(keys, comparer);
        }

        /// <nodoc/>
        public static void BucketSort<T>(this SpanSortHelper<T>.GetSpan getKeys, Func<T, int> getBucket, int bucketCount, IComparer<T>? comparer = null, int parallelism = 1)
        {
            comparer ??= Comparer<T>.Default;
            SpanSortHelper<T>.BucketSort(getKeys, getBucket, bucketCount, comparer, parallelism);
        }
    }

    /// <summary>
    /// Helper class for sorting spans
    /// </summary>
    public static class SpanSortHelper<T>
    {
        /// <summary>
        /// Retrieves a span instance
        /// </summary>
        public delegate Span<T> GetSpan();

        /// <summary>
        /// Sort a large span of items by first bucketizing then sorting the buckets
        /// </summary>
        internal static void BucketSort(GetSpan getKeys, Func<T, int> getBucket, int bucketCount, IComparer<T>? comparer, int parallelism = 1)
        {
            var keys = getKeys();
            var bucketCounts = new (int offset, int count)[bucketCount];
            foreach (var key in keys)
            {
                bucketCounts[getBucket(key)].count++;
            }

            (int offset, int end)[] buckets = bucketCounts;
            for (int i = 1; i < buckets.Length; i++)
            {
                var count = bucketCounts[i].count;
                var priorBucket = buckets[i - 1];
                buckets[i] = (priorBucket.end, priorBucket.end + count);
            }

            for (int bucketIndex = 0; bucketIndex < buckets.Length; bucketIndex++)
            {
                ref var bucket = ref buckets[bucketIndex];
                while (bucket.offset < bucket.end)
                {
                    ref var key = ref keys[bucket.offset];

                    while (true)
                    {
                        var targetBucketIndex = getBucket(key);
                        if (targetBucketIndex != bucketIndex)
                        {
                            var targetKeyIndex = buckets[targetBucketIndex].offset++;
                            ref var targetKey = ref keys[targetKeyIndex];
                            Swap(ref key, ref targetKey);
                        }
                        else
                        {
                            break;
                        }
                    }

                    bucket.offset++;
                }
            }

            if (parallelism > 1)
            {
                Parallel.For(0, buckets.Length, new ParallelOptions() { MaxDegreeOfParallelism = parallelism }, i =>
                {
                    var keys = getKeys();
                    var bucketEnd = buckets[i].end;
                    var bucketStart = i == 0 ? 0 : buckets[i - 1].end;
                    var bucketLength = bucketEnd - bucketStart;

                    IntrospectiveSort(keys.Slice(bucketStart, bucketLength), comparer);
                });
            }
            else
            {
                for (int i = 0; i < buckets.Length; i++)
                {
                    var bucketEnd = buckets[i].end;
                    var bucketStart = i == 0 ? 0 : buckets[i - 1].end;
                    var bucketLength = bucketEnd - bucketStart;

                    IntrospectiveSort(keys.Slice(bucketStart, bucketLength), comparer);
                }
            }
        }

        internal static void Swap(ref T a, ref T b)
        {
            var temp = a;
            a = b;
            b = temp;
        }

        // This is the threshold where Introspective sort switches to Insertion sort.
        // Empirically, 16 seems to speed up most cases without slowing down others, at least for integers.
        // Large value types may benefit from a smaller number.
        internal const int IntrosortSizeThreshold = 16;

        internal static int InternalBinarySearch(ReadOnlySpan<T> array, int index, int length, T value, IComparer<T> comparer)
        {
            Debug.Assert(array != null, "Check the arguments in the caller!");
            Debug.Assert(index >= 0 && length >= 0 && (array.Length - index >= length), "Check the arguments in the caller!");

            int lo = index;
            int hi = index + length - 1;
            while (lo <= hi)
            {
                int i = lo + ((hi - lo) >> 1);
                int order = comparer.Compare(array[i], value);

                if (order == 0) return i;
                if (order < 0)
                {
                    lo = i + 1;
                }
                else
                {
                    hi = i - 1;
                }
            }

            return ~lo;
        }

        private static void SwapIfGreater(Span<T> keys, IComparer<T> comparer, int i, int j)
        {
            Debug.Assert(i != j);

            if (comparer.Compare(keys[i], keys[j]) > 0)
            {
                T key = keys[i];
                keys[i] = keys[j];
                keys[j] = key;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Swap(Span<T> a, int i, int j)
        {
            Debug.Assert(i != j);

            T t = a[i];
            a[i] = a[j];
            a[j] = t;
        }

        internal static void IntrospectiveSort(Span<T> keys, IComparer<T> comparer)
        {
            Debug.Assert(comparer != null);

            if (keys.Length > 1)
            {
                IntroSort(keys, 2 * (Log2((uint)keys.Length) + 1), comparer);
            }
        }

        private static int Log2(uint value)
        {
#if NETCOREAPP
            return BitOperations.Log2(value);
#else
            return Bits.FindLowestBitSet(Bits.HighestBitSet(value));
#endif
        }

        private static void IntroSort(Span<T> keys, int depthLimit, IComparer<T> comparer)
        {
            Debug.Assert(keys.Length > 0);
            Debug.Assert(depthLimit >= 0);
            Debug.Assert(comparer != null);

            int partitionSize = keys.Length;
            while (partitionSize > 1)
            {
                if (partitionSize <= IntrosortSizeThreshold)
                {

                    if (partitionSize == 2)
                    {
                        SwapIfGreater(keys, comparer, 0, 1);
                        return;
                    }

                    if (partitionSize == 3)
                    {
                        SwapIfGreater(keys, comparer, 0, 1);
                        SwapIfGreater(keys, comparer, 0, 2);
                        SwapIfGreater(keys, comparer, 1, 2);
                        return;
                    }

                    InsertionSort(keys.Slice(0, partitionSize), comparer);
                    return;
                }

                if (depthLimit == 0)
                {
                    HeapSort(keys.Slice(0, partitionSize), comparer);
                    return;
                }
                depthLimit--;

                int p = PickPivotAndPartition(keys.Slice(0, partitionSize), comparer);

                // Note we've already partitioned around the pivot and do not have to move the pivot again.
                int start = p + 1;
                IntroSort(keys.Slice(start, partitionSize - start), depthLimit, comparer);
                partitionSize = p;
            }
        }

        private static int PickPivotAndPartition(Span<T> keys, IComparer<T> comparer)
        {
            Debug.Assert(keys.Length >= IntrosortSizeThreshold);
            Debug.Assert(comparer != null);

            int hi = keys.Length - 1;

            // Compute median-of-three.  But also partition them, since we've done the comparison.
            int middle = hi >> 1;

            // Sort lo, mid and hi appropriately, then pick mid as the pivot.
            SwapIfGreater(keys, comparer, 0, middle);  // swap the low with the mid point
            SwapIfGreater(keys, comparer, 0, hi);   // swap the low with the high
            SwapIfGreater(keys, comparer, middle, hi); // swap the middle with the high

            T pivot = keys[middle];
            Swap(keys, middle, hi - 1);
            int left = 0, right = hi - 1;  // We already partitioned lo and hi and put the pivot in hi - 1.  And we pre-increment & decrement below.

            while (left < right)
            {
                while (comparer.Compare(keys[++left], pivot) < 0) ;
                while (comparer.Compare(pivot, keys[--right]) < 0) ;

                if (left >= right)
                    break;

                Swap(keys, left, right);
            }

            // Put pivot in the right location.
            if (left != hi - 1)
            {
                Swap(keys, left, hi - 1);
            }
            return left;
        }

        private static void HeapSort(Span<T> keys, IComparer<T> comparer)
        {
            Debug.Assert(comparer != null);
            Debug.Assert(keys.Length > 0);

            int n = keys.Length;
            for (int i = n >> 1; i >= 1; i--)
            {
                DownHeap(keys, i, n, comparer);
            }

            for (int i = n; i > 1; i--)
            {
                Swap(keys, 0, i - 1);
                DownHeap(keys, 1, i - 1, comparer);
            }
        }

        private static void DownHeap(Span<T> keys, int i, int n, IComparer<T> comparer)
        {
            Debug.Assert(comparer != null);

            T d = keys[i - 1];
            while (i <= n >> 1)
            {
                int child = 2 * i;
                if (child < n && comparer.Compare(keys[child - 1], keys[child]) < 0)
                {
                    child++;
                }

                if (!(comparer.Compare(d, keys[child - 1]) < 0))
                    break;

                keys[i - 1] = keys[child - 1];
                i = child;
            }

            keys[i - 1] = d;
        }

        private static void InsertionSort(Span<T> keys, IComparer<T> comparer)
        {
            for (int i = 0; i < keys.Length - 1; i++)
            {
                T t = keys[i + 1];

                int j = i;
                while (j >= 0 && comparer.Compare(t, keys[j]) < 0)
                {
                    keys[j + 1] = keys[j];
                    j--;
                }

                keys[j + 1] = t;
            }
        }
    }
}