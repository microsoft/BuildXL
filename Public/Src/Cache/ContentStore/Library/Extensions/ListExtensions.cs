// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Cache.ContentStore.UtilitiesCore;

namespace BuildXL.Cache.ContentStore.Extensions
{
    /// <summary>
    ///     Extension methods for IList objects
    /// </summary>
    public static class ListExtensions
    {
        /// <summary>
        ///     Shuffles this IList
        /// </summary>
        /// <typeparam name="T">Type the IList holds</typeparam>
        /// <param name="list">This IList</param>
        public static void Shuffle<T>(this IList<T> list)
        {
            Contract.Requires(list != null);

            for (int i = list.Count; i > 1; i--)
            {
                int swapWith = ThreadSafeRandom.Generator.Next(i);
                T tmp = list[swapWith];
                list[swapWith] = list[i - 1];
                list[i - 1] = tmp;
            }
        }

        /// <summary>
        ///     Return a new merged list from the existing items and the given new items, favoring the newest items.
        /// </summary>
        /// <remarks>
        ///     Items at the from of the currentItems list are favored as they are considered more recent.
        /// </remarks>
        public static List<T> Merge<T>(this IList<T> currentItems, IList<T> newItems, int maxMergedItems, IEqualityComparer<T> comparer)
        {
            Contract.Requires(currentItems != null);
            Contract.Requires(newItems != null);
            Contract.Requires(maxMergedItems >= 0);
            Contract.Requires(comparer != null);

            // Build a set (no duplicates) of all current and new items.
            var mergedHashSet = new HashSet<T>(currentItems, comparer);
            mergedHashSet.UnionWith(newItems);

            // If the combined set is under the max, return it without further processing.
            if (mergedHashSet.Count <= maxMergedItems)
            {
                return mergedHashSet.ToList();
            }

            // Prepare a list to be built and returned.
            var mergedList = new List<T>(maxMergedItems);

            // Add new items.
            foreach (var address in newItems)
            {
                if (mergedList.Count >= maxMergedItems)
                {
                    break;
                }

                if (mergedHashSet.Contains(address))
                {
                    mergedList.Add(address);
                    mergedHashSet.Remove(address);
                }
            }

            // Add current items, favoring those at the front.
            foreach (T item in currentItems)
            {
                if (mergedList.Count >= maxMergedItems)
                {
                    break;
                }

                if (mergedHashSet.Contains(item))
                {
                    mergedList.Add(item);
                }
            }

            return mergedList;
        }

        /// <summary>
        ///     Perform an action on each non-null item in the list.
        /// </summary>
        public static void ForEachNull<T>(this IList<T> list, Action<int> action)
            where T : class
        {
            Contract.Requires(list != null);
            Contract.Requires(action != null);

            for (var i = 0; i < list.Count; i++)
            {
                if (list[i] == null)
                {
                    action(i);
                }
            }
        }

        /// <summary>
        ///     Perform an action on each non-null item in the list.
        /// </summary>
        public static void ForEachNotNull<T>(this IList<T> list, Action<int> action)
            where T : class
        {
            Contract.Requires(list != null);
            Contract.Requires(action != null);

            for (var i = 0; i < list.Count; i++)
            {
                if (list[i] != null)
                {
                    action(i);
                }
            }
        }

        /// <summary>
        ///     Checks if list is null or empty.
        /// </summary>
        public static bool NullOrEmpty<T>(this IList<T> list)
        {
            return list == null || !list.Any();
        }

        /// <summary>
        ///     Checks if list is null or empty.
        /// </summary>
        public static bool NullOrEmpty<T>(this IReadOnlyList<T> list)
        {
            return list == null || !list.Any();
        }

        /// <summary>
        ///     Get and removes item from list.
        /// </summary>
        public static T RemoveAndGetItem<T>(this IList<T> list, int indexToRemove)
        {
            Contract.Requires(list != null);

            if (indexToRemove < 0 || indexToRemove >= list.Count)
            {
                throw new InvalidOperationException();
            }

            var item = list[indexToRemove];
            list.RemoveAt(indexToRemove);
            return item;
        }
    }
}
