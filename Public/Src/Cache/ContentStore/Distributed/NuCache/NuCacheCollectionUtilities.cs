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


        /// <nodoc />
        /// <remarks>
        /// If <paramref name="poolSize"/> = <paramref name="pageSize"/>, the algorithm fills up the entire pool and
        /// evicts a fraction of it in every iteration.
        /// </remarks>
        public static IEnumerable<T> ApproximateSort<T>(this IEnumerable<T> original, Comparer<T> comparer, Func<List<T>, IEnumerable<T>> query, int poolSize, int pageSize, float removalFraction)
        {
            Contract.Requires(poolSize > 0);
            Contract.Requires(pageSize > 0 && pageSize <= poolSize);
            Contract.Requires(removalFraction > 0 && removalFraction <= 1);

            // The pool holds up to `poolSize` candidates sorted by the comparer
            var pool = new PriorityQueue<T>(poolSize, comparer);

            var source = original.GetEnumerator();
            var sourceHasItems = true;

            while (true)
            {
                if (sourceHasItems && pool.Count < poolSize)
                {
                    // In this branch, we fill the queue up to maximum capacity by querying in batches of size at most
                    // `pageSize`.
                    int batchSize = Math.Min(poolSize - pool.Count, pageSize);

                    var batch = new List<T>(batchSize);
                    while (batch.Count < batch.Capacity && source.MoveNext())
                    {
                        batch.Add(source.Current);
                    }

                    if (batch.Count > 0)
                    {
                        foreach (var candidate in query(batch))
                        {
                            pool.Push(candidate);
                        }
                    }
                    else
                    {
                        // No more items in the original IEnumerable. Stop trying to queue more items.
                        sourceHasItems = false;
                    }
                }

                if (pool.Count == 0)
                {
                    Contract.Assert(!sourceHasItems);
                    yield break;
                }

                int minimumYieldSize;
                if (sourceHasItems)
                {
                    minimumYieldSize = (int)Math.Floor(removalFraction * pool.Count);
                    // To guarantee termination, we always yield at least one element
                    minimumYieldSize = Math.Max(minimumYieldSize, 1);
                }
                else
                {
                    // If the enumerator has no elements left, we know that the queue has the best order possible,
                    // so we yield everything.
                    minimumYieldSize = pool.Count;
                }

                for (var i = 0; i < minimumYieldSize; ++i)
                {
                    yield return pool.Top;
                    pool.Pop();
                }
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
