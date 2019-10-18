// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace BuildXL.Cache.ContentStore.Extensions
{
    /// <summary>
    ///     Extension methods for Enumerable types.
    /// </summary>
    public static class EnumerableExtensions
    {
        private static readonly ExecutionDataflowBlockOptions AllProcessors = new ExecutionDataflowBlockOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount
        };

        /// <summary>
        ///     Attempt to remove an item from the ConcurrentDictionary.
        /// </summary>
        /// <remarks>
        ///     http://blogs.msdn.com/b/pfxteam/archive/2011/04/02/10149222.aspx
        /// </remarks>
        public static bool TryRemoveSpecific<TKey, TValue>(this ConcurrentDictionary<TKey, TValue> dictionary, TKey key, TValue value)
        {
            if (dictionary == null)
            {
                throw new ArgumentNullException(nameof(dictionary));
            }

            return ((ICollection<KeyValuePair<TKey, TValue>>)dictionary).Remove(new KeyValuePair<TKey, TValue>(key, value));
        }

        /// <summary>
        ///     Break an item list into chunks/pages.
        /// </summary>
        public static IEnumerable<List<T>> GetPages<T>(this IEnumerable<T> allItems, int pageSize)
        {
            List<T> page = null;

            foreach (T item in allItems)
            {
                if (page == null)
                {
                    page = new List<T>(pageSize);
                }

                page.Add(item);

                if (page.Count >= pageSize)
                {
                    yield return page;
                    page = null;
                }
            }

            if (page != null)
            {
                yield return page;
            }
        }

        /// <summary>
        ///     Break an item list into chunks/pages.
        /// </summary>
        public static IEnumerable<IList<T>> GetPages<TKey, T>(this IGrouping<TKey, T> allItems, int pageSize)
        {
            if (allItems is IList<T> lst && lst.Count <= pageSize)
            {
                yield return lst;
                yield break;
            }

            List<T> page = null;

            foreach (T item in allItems)
            {
                if (page == null)
                {
                    page = new List<T>(pageSize);
                }

                page.Add(item);

                if (page.Count >= pageSize)
                {
                    yield return page;
                    page = null;
                }
            }

            if (page != null)
            {
                yield return page;
            }
        }

        /// <summary>
        ///     Perform the function on all items in parallel.
        /// </summary>
        public static Task ParallelForEachAsync<T>(this IEnumerable<T> items, Func<T, Task> loopBody)
        {
            var block = new ActionBlock<T>(loopBody, AllProcessors);
            return block.PostAllAndComplete(items);
        }

        /// <summary>
        ///     Perform the function on all items in parallel.
        /// </summary>
        public static Task ParallelForEachAsync<T>(this IEnumerable<T> items, int maxDegreeOfParallelism, Func<T, Task> loopBody)
        {
            var block = new ActionBlock<T>(loopBody, new ExecutionDataflowBlockOptions {MaxDegreeOfParallelism = maxDegreeOfParallelism});
            return block.PostAllAndComplete(items);
        }

        /// <summary>
        ///     Perform the action on all items in parallel.
        /// </summary>
        public static Task ParallelForEachAsync<T>(this IEnumerable<T> items, Action<T> loopBody)
        {
            var block = new ActionBlock<T>(loopBody, AllProcessors);
            return block.PostAllAndComplete(items);
        }

        /// <summary>
        ///     Transform the items into a dictionary in parallel.
        /// </summary>
        public static Task<ConcurrentDictionary<TKey, TValue>> ParallelToConcurrentDictionaryAsync<TKey, TValue>(
            this IEnumerable<TKey> items, Func<TKey, TValue> getValue)
        {
            return items.ParallelToConcurrentDictionaryAsync(item => item, getValue);
        }

        /// <summary>
        ///     Transform the items into a dictionary in parallel.
        /// </summary>
        public static async Task<ConcurrentDictionary<TKey, TValue>> ParallelToConcurrentDictionaryAsync<T, TKey, TValue>(
            this IEnumerable<T> items, Func<T, TKey> getKey, Func<T, TValue> getValue)
        {
            var dict = new ConcurrentDictionary<TKey, TValue>();
            await items.ParallelAddToConcurrentDictionaryAsync(dict, getKey, getValue);
            return dict;
        }

        /// <summary>
        ///     Transform the items into dictionary entries and add them to the given dictionary.
        /// </summary>
        public static Task ParallelAddToConcurrentDictionaryAsync<T, TKey, TValue>(
            this IEnumerable<T> items, ConcurrentDictionary<TKey, TValue> dictionary, Func<T, TKey> getKey, Func<T, TValue> getValue)
        {
            var block = new ActionBlock<T>(item => { dictionary[getKey(item)] = getValue(item); }, AllProcessors);
            return block.PostAllAndComplete(items);
        }
    }
}
