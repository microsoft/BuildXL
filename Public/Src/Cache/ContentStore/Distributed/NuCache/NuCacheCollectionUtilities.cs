// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.UtilitiesCore;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    internal static class NuCacheCollectionUtilities
    {
        /// <summary>
        /// Randomly splits the list and interleaves the items from the partitions in the returned enumerable.
        /// </summary>
        public static IEnumerable<TItem> RandomSplitInterleave<TItem>(this IReadOnlyList<TItem> list)
        {
            // Get an offset in the middle section of the list exclude top and bottom fourth of list
            var offset = ThreadSafeRandom.Generator.Next(list.Count / 4, (list.Count * 3) / 4);
            int firstCursor = 0;
            int secondCursor = offset;

            while (firstCursor < offset || secondCursor < list.Count)
            {
                if (firstCursor < offset)
                {
                    yield return list[firstCursor];
                    firstCursor++;
                }

                if (secondCursor < list.Count)
                {
                    yield return list[secondCursor];
                    secondCursor++;
                }
            }
        }

        /// <summary>
        /// Splits the list into partition with max size of <paramref name="batchSize"/>.
        /// </summary>
        public static IEnumerable<IReadOnlyList<TItem>> Split<TItem>(this IReadOnlyList<TItem> list, int batchSize)
        {
            Contract.Requires(list != null);
            Contract.Requires(batchSize > 0);

            List<TItem> batch = new List<TItem>(batchSize);
            for (int i = 0; i < list.Count; i++)
            {
                if (batch.Count == batchSize)
                {
                    yield return batch;
                    batch = new List<TItem>(batchSize);
                }

                batch.Add(list[i]);
            }

            if (batch.Count != 0)
            {
                yield return batch;
            }
        }

        /// <summary>
        /// Returns a difference between <paramref name="leftItems"/> sequence and <paramref name="rightItems"/> sequence assuming that both sequences are sorted.
        /// </summary>
        public static IEnumerable<(T item, MergeMode mode)> DistinctDiffSorted<T, TComparable>(
            IEnumerable<T> leftItems,
            IEnumerable<T> rightItems,
            Func<T, TComparable> getComparable)
            where TComparable : IComparable<TComparable>
        {
            foreach (var mergeItem in DistinctMergeSorted(leftItems, rightItems, getComparable, getComparable))
            {
                if (mergeItem.mode == MergeMode.LeftOnly)
                {
                    yield return (mergeItem.left, mergeItem.mode);
                }
                else if (mergeItem.mode == MergeMode.RightOnly)
                {
                    yield return (mergeItem.right, mergeItem.mode);
                }
            }
        }

        /// <summary>
        /// Merges two sorted sequences.
        /// </summary>
        public static IEnumerable<(TLeft left, TRight right, MergeMode mode)> DistinctMergeSorted<TLeft, TRight, TComparable>(
            IEnumerable<TLeft> leftItems,
            IEnumerable<TRight> rightItems,
            Func<TLeft, TComparable> getLeftComparable,
            Func<TRight, TComparable> getRightComparable)
            where TComparable : IComparable<TComparable>
        {
            var leftEnumerator = leftItems.SortedUnique(getLeftComparable).GetEnumerator();
            var rightEnumerator = rightItems.SortedUnique(getRightComparable).GetEnumerator();

            return DistinctMergeSorted(leftEnumerator, rightEnumerator, getLeftComparable, getRightComparable);
        }

        private static IEnumerable<(TLeft left, TRight right, MergeMode mode)> DistinctMergeSorted<TLeft, TRight, TComparable>(
            IEnumerator<TLeft> leftEnumerator,
            IEnumerator<TRight> rightEnumerator,
            Func<TLeft, TComparable> getLeftComparable,
            Func<TRight, TComparable> getRightComparable)
            where TComparable : IComparable<TComparable>
        {
            bool hasCurrentLeft = MoveNext(leftEnumerator, out var leftCurrent);
            bool hasCurrentRight = MoveNext(rightEnumerator, out var rightCurrent);

            while (hasCurrentLeft || hasCurrentRight)
            {
                if (!hasCurrentRight)
                {
                    do
                    {
                        yield return (leftCurrent, default(TRight), MergeMode.LeftOnly);
                    }
                    while (MoveNext(leftEnumerator, out leftCurrent));

                    yield break;
                }
                else if (!hasCurrentLeft)
                {
                    do
                    {
                        yield return (default(TLeft), rightCurrent, MergeMode.RightOnly);
                    }
                    while (MoveNext(rightEnumerator, out rightCurrent));

                    yield break;
                }
                else // hasCurrent1 && hasCurrent2
                {
                    var comparison = getLeftComparable(leftCurrent).Compare(getRightComparable(rightCurrent));
                    if (comparison == CompareResult.Equal) // (leftCurrent == rightCurrent)
                    {
                        yield return (leftCurrent, rightCurrent, MergeMode.Both);
                        hasCurrentLeft = MoveNext(leftEnumerator, out leftCurrent);
                        hasCurrentRight = MoveNext(rightEnumerator, out rightCurrent);
                    }
                    else if (comparison == CompareResult.RightGreater) // (leftCurrent < rightCurrent)
                    {
                        yield return (leftCurrent, default(TRight), MergeMode.LeftOnly);
                        hasCurrentLeft = MoveNext(leftEnumerator, out leftCurrent);
                    }
                    else // comparison > 0 (leftCurrent > rightCurrent)
                    {
                        yield return (default(TLeft), rightCurrent, MergeMode.RightOnly);
                        hasCurrentRight = MoveNext(rightEnumerator, out rightCurrent);
                    }
                }
            }
        }

        public static IEnumerable<T> SortedUnique<T, TComparable>(this IEnumerable<T> items, Func<T, TComparable> getComparable)
            where TComparable : IComparable<TComparable>
        {
            TComparable lastItemComparable = default(TComparable);
            bool hasLastItem = false;
            foreach (var item in items)
            {
                var itemComparable = getComparable(item);
                if (!hasLastItem || lastItemComparable.Compare(itemComparable) != CompareResult.Equal)
                {
                    hasLastItem = true;
                    lastItemComparable = itemComparable;
                    yield return item;
                }
            }
        }

        private static bool MoveNextUntilDifferent<T, TOther>(
            IEnumerator<T> enumerator,
            TOther comparisonValue,
            out T current,
            Func<T, TOther, bool> equal)
        {
            while (MoveNext(enumerator, out current))
            {
                if (!equal(current, comparisonValue))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool MoveNext<T>(IEnumerator<T> enumerator1, out T current)
        {
            if (enumerator1.MoveNext())
            {
                current = enumerator1.Current;
                return true;
            }
            else
            {
                current = default;
                return false;
            }
        }

        public static CompareResult Compare<T>(this T left, T right)
            where T : IComparable<T>
        {
            var compareResult = left.CompareTo(right);
            if (compareResult == 0)
            {
                return CompareResult.Equal;
            }
            else if (compareResult < 0)
            {
                return CompareResult.RightGreater;
            }
            else
            {
                return CompareResult.LeftGreater;
            }
        }
    }

    /// <nodoc />
    internal enum CompareResult
    {
        /// <nodoc />
        LeftGreater,

        /// <nodoc />
        RightGreater,

        /// <nodoc />
        Equal
    }

    /// <nodoc />
    [Flags]
    internal enum MergeMode
    {
        /// <nodoc />
        LeftOnly = 1,

        /// <nodoc />
        RightOnly = 1 << 1,

        /// <nodoc />
        Both = LeftOnly | RightOnly
    }
}
