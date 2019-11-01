// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;

namespace BuildXL.Cache.ContentStore.Interfaces.Extensions
{
    /// <summary>
    ///     Extension methods for Enumerable types.
    /// </summary>
    public static class EnumerableExtensions
    {
        /// <summary>
        ///     Write all elements to a HashSet.
        /// </summary>
        public static HashSet<T> ToHashSet<T>(this IEnumerable<T> items, IEqualityComparer<T> comparer = null)
        {
            comparer ??= EqualityComparer<T>.Default;
            return new HashSet<T>(items, comparer);
        }

        /// <summary>
        /// Gets the item with the max key given the optional key comparer.
        /// </summary>
        public static T MaxByOrDefault<T, TKey>(this IEnumerable<T> items, Func<T, TKey> keySelector, IComparer<TKey> keyComparer = null)
        {
            keyComparer ??= Comparer<TKey>.Default;
            T maxItem = default;
            TKey maxKey = default;
            bool isFirst = true;

            foreach (var item in items)
            {
                var currentKey = keySelector(item);
                if (isFirst || keyComparer.Compare(currentKey, maxKey) > 0)
                {
                    isFirst = false;
                    maxItem = item;
                    maxKey = currentKey;
                }
            }

            return maxItem;
        }

        /// <summary>
        /// Variation of IEnumerable.Select for <see cref="Indexed{T}"/> items such that it preserves index
        /// </summary>
        public static IEnumerable<Indexed<TResult>> SelectPreserveIndex<TSource, TResult>(this IEnumerable<Indexed<TSource>> items, Func<TSource, TResult> selector)
        {
            return items.Select(item => selector(item.Item).WithIndex(item.Index));
        }

        /// <summary>
        /// Variation of IEnumerable.Single such that it awaits and returns the results of the items
        /// </summary>
        public static async Task<TSource> SingleAwaitIndexed<TSource>(this IEnumerable<Task<Indexed<TSource>>> items)
        {
            var result = await items.Single();
            return result.Item;
        }

        /// <summary>
        /// Variation of IEnumerable.ToLookup such that it awaits all items before grouping them based on the keyFunc
        /// </summary>
        public static async Task<ILookup<TKey, TSource>> ToLookupAwait<TSource, TKey>(this IEnumerable<Task<TSource>> items, Func<TSource, TKey> keyFunc)
        {
            var results = new List<Tuple<TKey, TSource>>();
            foreach (var itemTask in items)
            {
                var item = await itemTask;
                results.Add(Tuple.Create(keyFunc(item), item));
            }

            return results.ToLookup(t => t.Item1, t => t.Item2);
        }

        /// <summary>
        /// Converts an <see cref="IEnumerable{T}"/> to <see cref="Indexed{T}"/> items
        /// </summary>
        public static IEnumerable<Indexed<T>> AsIndexed<T>(this IEnumerable<T> items)
        {
            return items.Select((item, i) => new Indexed<T>(item, i));
        }

        /// <summary>
        /// Converts an <see cref="IEnumerable{T}"/> to <see cref="Indexed{T}"/> items
        /// </summary>
        public static IEnumerable<(T value, int index)> WithIndices<T>(this IEnumerable<T> items)
        {
            return items.Select((item, i) => (item, i));
        }

        /// <summary>
        /// Converts an <see cref="IEnumerable{T}"/> to <see cref="Task{T}"/> items
        /// </summary>
        public static IEnumerable<Task<T>> AsTasks<T>(this IEnumerable<T> items)
        {
            return items.Select(item => Task.FromResult(item));
        }

        /// <summary>
        /// Converts an <see cref="IEnumerable{T}"/> to tasks returning <see cref="Indexed{T}"/> items
        /// </summary>
        public static IEnumerable<Task<Indexed<T>>> AsIndexedTasks<T>(this IEnumerable<T> items)
        {
            return items.Select((item, i) => Task.FromResult(new Indexed<T>(item, i)));
        }
    }
}
