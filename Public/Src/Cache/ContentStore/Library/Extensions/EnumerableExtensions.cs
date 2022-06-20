// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

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
        public static bool TryRemoveSpecific<TKey, TValue>(this ConcurrentDictionary<TKey, TValue> dictionary, TKey key, TValue value) where TKey : notnull
        {
            return ((ICollection<KeyValuePair<TKey, TValue>>)dictionary).Remove(new KeyValuePair<TKey, TValue>(key, value));
        }

        /// <nodoc />
        public static IEnumerable<T> AppendItem<T>(this IEnumerable<T> sequence, T item)
        {
            // Add build id hash to hashes so build ring machines can be updated
            return sequence.Append(item);
        }

        /// <summary>
        ///     Break an item list into chunks/pages.
        /// </summary>
        public static IEnumerable<List<T>> GetPages<T>(this IEnumerable<T> allItems, int pageSize)
        {
            List<T>? page = null;

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

            List<T>? page = null;

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
            this IEnumerable<TKey> items, Func<TKey, TValue> getValue) where TKey : notnull
        {
            return items.ParallelToConcurrentDictionaryAsync(item => item, getValue);
        }

        /// <summary>
        ///     Transform the items into a dictionary in parallel.
        /// </summary>
        public static async Task<ConcurrentDictionary<TKey, TValue>> ParallelToConcurrentDictionaryAsync<T, TKey, TValue>(
            this IEnumerable<T> items, Func<T, TKey> getKey, Func<T, TValue> getValue) where TKey : notnull
        {
            var dict = new ConcurrentDictionary<TKey, TValue>();
            await items.ParallelAddToConcurrentDictionaryAsync(dict, getKey, getValue);
            return dict;
        }

        /// <summary>
        ///     Transform the items into dictionary entries and add them to the given dictionary.
        /// </summary>
        public static Task ParallelAddToConcurrentDictionaryAsync<T, TKey, TValue>(
            this IEnumerable<T> items, ConcurrentDictionary<TKey, TValue> dictionary, Func<T, TKey> getKey, Func<T, TValue> getValue) where TKey : notnull
        {
            var block = new ActionBlock<T>(item => { dictionary[getKey(item)] = getValue(item); }, AllProcessors);
            return block.PostAllAndComplete(items);
        }

        /// <summary>
        /// Pseudorandomly enumerates the range from [0, <paramref name="length"/>)
        /// </summary>
        public static IEnumerable<int> PseudoRandomEnumerateRange(int length)
        {
            var offset = ThreadSafeRandom.Generator.Next(0, length);
            var current = ThreadSafeRandom.Generator.Next(0, length);
            for (int i = 0; i < length; i++)
            {
                yield return (current + offset) % length;
                current = PseudoRandomNextIndex(current, length);
            }
        }

        /// <summary>
        /// Pseudorandomly enumerates the items in the list
        /// </summary>
        public static IEnumerable<T> PseudoRandomEnumerate<T>(this IReadOnlyList<T> list)
        {
            foreach (var index in PseudoRandomEnumerateRange(list.Count))
            {
                yield return list[index];
            }
        }

        /// <summary>
        /// Gets a unique, pseudorandom value between [0, length).
        ///
        /// for a collection of the  given bin based on the Linear congruential generator
        /// See https://en.wikipedia.org/wiki/Linear_congruential_generator
        /// 
        /// X_{n+1}= (a * X_{n} + c) mod m
        /// where m is the modulus
        /// where a is the multiplier
        /// where c is the increment
        /// 
        /// Values are chosen such that each bin has a unique next bin (i.e. no two bins have the same next bin).
        /// This implies that every bin is the backup of some other bin. The following properties ensure this:
        /// See wikipedia article section 4.
        /// 1. m and c are relatively prime,
        /// 2. a-1 is divisible by all prime factors of m,
        /// 3. a-1 is divisible by 4 if m is divisible by 4.
        /// </summary>
        public static int PseudoRandomNextIndex(int current, int length)
        {
            Contract.Requires(Utilities.Range.IsValid(current, length));
            uint m = EqualOrGreaterPowerOfTwo(length); // the modulus
            const uint A = 1664525; // the multiplier
            const uint C = 1013904223; // the increment

            uint x = (uint)current;
            do
            {
                x = unchecked(((A * x) + C) % m);
            }
            while (x >= length);

            return (int)x;
        }

        private static uint EqualOrGreaterPowerOfTwo(int length)
        {
            uint powerOfTwo = Bits.HighestBitSet((uint)length);
            return powerOfTwo == length ? powerOfTwo : powerOfTwo * 2;
        }
    }
}
