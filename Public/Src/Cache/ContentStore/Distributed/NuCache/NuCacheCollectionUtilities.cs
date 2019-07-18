// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Utilities.Collections;

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

        public static IEnumerable<T> SkipOptimized<T>(this IReadOnlyList<T> original, int amountToSkip)
        {
            for (var i = amountToSkip; i < original.Count; i++)
            {
                yield return original[i];
            }
        }

        /// <summary>
        /// Takes an enumerable and takes n=pageSize elements from it. Processess the elements with a query, and inserts
        /// them into a priority queue. It will then continue yielding elements of the queue, until it has less than
        /// n elements, at which point it will take another n elements from the original enumerable and repeat the
        /// process, adding them to the same priority queue. This process repeats until the original enumerable has no
        /// more elements.
        /// </summary>
        public static IEnumerable<T> QueryAndOrderInPages<T>(this IEnumerable<T> original, int pageSize, Comparer<T> comparer, Func<List<T>, IEnumerable<T>> query)
        {
            var source = original.GetEnumerator();
            var queue = new PriorityQueue<T>(pageSize * 2, comparer);
            var sourceHasItems = true;

            while (true)
            {
                if (sourceHasItems && queue.Count < pageSize)
                {
                    var itemsToQuery = new List<T>(pageSize);
                    for (var i = 0; i < pageSize && source.MoveNext(); i++)
                    {
                        itemsToQuery.Add(source.Current);
                    }

                    if (itemsToQuery.Count > 0)
                    {
                        var results = query(itemsToQuery);

                        foreach (var result in results)
                        {
                            queue.Push(result);
                        }
                    }
                    else
                    {
                        // No more items in the original IEnumerable. Stop trying to queue more items.
                        sourceHasItems = false;
                    }
                }

                if (queue.Count == 0)
                {
                    yield break;
                }

                yield return queue.Top;
                queue.Pop();
            }
        }

        public static IEnumerable<T> MergeOrdered<T>(IEnumerable<T> items1, IEnumerable<T> items2, IComparer<T> comparer)
        {
            var enumerator1 = items1.GetEnumerator();
            var enumerator2 = items2.GetEnumerator();

            T current1 = default;
            T current2 = default;

            bool next1 = TryMoveNext(enumerator1, ref current1);
            bool next2 = TryMoveNext(enumerator2, ref current2);

            while (next1 || next2)
            {
                while (next1)
                {
                    if (!next2 || comparer.Compare(current1, current2) <= 0)
                    {
                        yield return current1;
                    }
                    else
                    {
                        break;
                    }

                    next1 = TryMoveNext(enumerator1, ref current1);
                }

                while (next2)
                {
                    if (!next1 || comparer.Compare(current1, current2) > 0)
                    {
                        yield return current2;
                    }
                    else
                    {
                        break;
                    }

                    next2 = TryMoveNext(enumerator2, ref current2);
                }
            }

            bool TryMoveNext(IEnumerator<T> enumerator, ref T current)
            {
                if (enumerator.MoveNext())
                {
                    current = enumerator.Current;
                    return true;
                }
                else
                {
                    return false;
                }
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
