// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;

namespace BuildXL.Utilities.Collections
{
    /// <summary>
    /// Helper/extension methods for collections
    /// </summary>
    public static partial class CollectionUtilities
    {
        /// <summary>
        /// Creates a new array of the given length or returns the cached empty array
        /// </summary>
        /// <typeparam name="T">The type of array elements.</typeparam>
        public static T[] NewOrEmptyArray<T>(int length)
        {
            return length == 0 ? EmptyArray<T>() : new T[length];
        }

        /// <summary>
        /// Returns a cached instance of an array of the specified type, without any allocations.
        /// </summary>
        /// <typeparam name="T">The type of array elements.</typeparam>
        /// <returns>A cached empty instance of the required type.</returns>
        public static T[] EmptyArray<T>()
        {
            return Empty.Array<T>.Instance;
        }

        /// <summary>
        /// Returns a cached instance of a sorted array of the specified type, without any allocations.
        /// </summary>
        /// <returns>A cached empty instance of the required type.</returns>
        public static SortedReadOnlyArray<TValue, TComparer> EmptySortedReadOnlyArray<TValue, TComparer>(TComparer comparer)
            where TComparer : class, IComparer<TValue>
        {
            return Empty.SortedArray<TValue, TComparer>.Instance(comparer);
        }

        /// <summary>
        /// Returns a cached instance of an empty readonly set of the specified type, without any allocations.
        /// </summary>
        /// <typeparam name="T">The type of set elements.</typeparam>
        /// <returns>A cached empty instance of the required type.</returns>
        public static IReadOnlySet<T> EmptySet<T>()
        {
            return Empty.Set<T>.Instance;
        }

        /// <summary>
        /// Returns a cached instance of an empty dictionary, without any allocations.
        /// </summary>
        public static IReadOnlyDictionary<TKey, TValue> EmptyDictionary<TKey, TValue>()
        {
            return Empty.Dictionary<TKey, TValue>.Instance;
        }

        /// <summary>
        /// Attempts to cast the collection to a read-only list or copies to a list if not castable
        /// </summary>
        public static IReadOnlyList<T> AsReadOnlyList<T>(this IEnumerable<T> items)
        {
            if (items is IReadOnlyList<T>)
            {
                return (IReadOnlyList<T>)items;
            }
            else
            {
                return items.ToList();
            }
        }

        /// <summary>
        /// Attempts to cast the collection to a read-only list or copies to a list if not castable
        /// </summary>
        public static IReadOnlyCollection<T> AsReadOnlyCollection<T>(this IEnumerable<T> items)
        {
            if (items is IReadOnlyCollection<T>)
            {
                return (IReadOnlyCollection<T>)items;
            }
            else
            {
                return items.ToList();
            }
        }

        /// <summary>
        /// Converts the collection to a read-only array.
        /// </summary>
        /// <typeparam name="T">The type of array elements.</typeparam>
        /// <param name="collection">the collection of elements</param>
        /// <returns>an read only array instance containing the elements in the collection.</returns>
        public static ReadOnlyArray<T> ToReadOnlyArray<T>(this IEnumerable<T> collection)
        {
            Contract.Requires(collection != null);
            Contract.Ensures(Contract.Result<ReadOnlyArray<T>>().IsValid);

            return ReadOnlyArray<T>.From(collection);
        }

        /// <summary>
        /// Converts the collection to a read-only set.
        /// </summary>
        /// <typeparam name="T">The type of set elements.</typeparam>
        /// <param name="collection">the collection of elements</param>
        /// <returns>a read only set instance containing the elements in the collection.</returns>
        public static ReadOnlyHashSet<T> ToReadOnlySet<T>(this IEnumerable<T> collection)
        {
            Contract.Requires(collection != null);
            Contract.Ensures(Contract.Result<IReadOnlySet<T>>() != null);

            return new ReadOnlyHashSet<T>(collection);
        }

        /// <summary>
        /// Converts the collection to an array. Uses a cache array instance for empty arrays.
        /// </summary>
        /// <typeparam name="T">The type of array elements.</typeparam>
        /// <param name="collection">the collection of elements</param>
        /// <returns>an array instance containing the elements in the collection.</returns>
        public static T[] AsArray<T>(this IReadOnlyCollection<T> collection)
        {
            Contract.Requires(collection != null);
            Contract.Ensures(Contract.Result<T[]>() != null);

            int count = collection.Count;
            return AsArray<T>(collection, count);
        }

        /// <summary>
        /// Creates a sorted list from the enumerable.
        /// </summary>
        public static List<T> ToListSorted<T>(this IEnumerable<T> enumerable, IComparer<T> comparer = null)
        {
            var list = enumerable.ToList();
            list.Sort(comparer);
            return list;
        }

        /// <summary>
        /// Converts the collection to an array. Uses a cache array instance for empty arrays.
        /// </summary>
        /// <typeparam name="T">The type of collection elements.</typeparam>
        /// <typeparam name="TResult">The type of result array elements.</typeparam>
        /// <param name="list">the collection of elements</param>
        /// <param name="select">project of list element to result element</param>
        /// <returns>an array instance containing the elements in the collection.</returns>
        public static TResult[] SelectArray<T, TResult>(this IReadOnlyList<T> list, Func<T, TResult> select)
        {
            Contract.Requires(list != null);
            Contract.Ensures(Contract.Result<TResult[]>() != null);

            int count = list.Count;
            var array = NewOrEmptyArray<TResult>(count);
            for (int i = 0; i < count; i++)
            {
                array[i] = select(list[i]);
            }

            return array;
        }

        /// <summary>
        /// Converts the dictionary to an array. Uses a cache array instance for empty arrays.
        /// </summary>
        public static TResult[] SelectArray<TKey, TValue, TResult>(this Dictionary<TKey, TValue> map, Func<KeyValuePair<TKey, TValue>, TResult> select)
        {
            Contract.Requires(map != null);
            Contract.Ensures(Contract.Result<TResult[]>() != null);

            int count = map.Count;
            var array = NewOrEmptyArray<TResult>(count);
            int idx = 0;
            foreach (var kvp in map)
            {
                array[idx] = select(kvp);
                idx++;
            }

            return array;
        }

        /// <summary>
        /// Clones the existing dictionary with no enumerator allocations.
        /// </summary>
        public static Dictionary<TKey, TValue> ToDictionary<TKey, TValue>(this Dictionary<TKey, TValue> map)
        {
            var result = new Dictionary<TKey, TValue>(map.Count);

            foreach (KeyValuePair<TKey, TValue> pair in map)
            {
                result.Add(pair.Key, pair.Value);
            }

            return result;
        }

        /// <summary>
        /// Concatenates the collections into an array. Uses a cache array instance for empty arrays.
        /// </summary>
        /// <typeparam name="T">The type of array elements.</typeparam>
        /// <param name="collection1">the first collection of elements</param>
        /// <param name="collection2">the second collection of elements</param>
        /// <returns>an array instance containing the elements in the collections.</returns>
        public static T[] ConcatAsArray<T>(this IReadOnlyCollection<T> collection1, IReadOnlyCollection<T> collection2)
        {
            Contract.Requires(collection1 != null);
            Contract.Requires(collection2 != null);
            Contract.Ensures(Contract.Result<T[]>() != null);

            int count1 = collection1.Count;
            int count2 = collection2.Count;

            IEnumerable<T> enumerable;

            if (count1 == 0)
            {
                enumerable = collection2;
            }
            else if (count2 == 0)
            {
                enumerable = collection1;
            }
            else
            {
                enumerable = collection1.Concat(collection2);
            }

            return AsArray<T>(enumerable, count1 + count2);
        }

        /// <summary>
        /// Creates an array from the collection with the specified number of elements from the source collection.
        /// </summary>
        /// <typeparam name="T">The type of array elements.</typeparam>
        /// <param name="enumerable">the collection of elements</param>
        /// <param name="count">the number of elements in the result array</param>
        /// <returns>an array instance containing the elements in the collection.</returns>
        private static T[] AsArray<T>(IEnumerable<T> enumerable, int count)
        {
            Contract.Requires(enumerable != null);
            Contract.Requires(count >= 0);
            Contract.Ensures(Contract.Result<T[]>() != null);

            if (count == 0)
            {
                return EmptyArray<T>();
            }

            T[] array = new T[count];

            ICollection<T> collection = enumerable as ICollection<T>;
            if (collection != null)
            {
                collection.CopyTo(array, 0);
            }
            else
            {
                var enumerator = enumerable.GetEnumerator();
                for (int i = 0; i < array.Length && enumerator.MoveNext(); i++)
                {
                    array[i] = enumerator.Current;
                }
            }

            return array;
        }

        /// <summary>
        /// Attempts to add the value to the dictionary under the given key.
        /// </summary>
        /// <typeparam name="TKey">the key type</typeparam>
        /// <typeparam name="TValue">the element type</typeparam>
        /// <param name="dictionary">the dictionary</param>
        /// <param name="key">the key</param>
        /// <param name="value">the value</param>
        public static void Add<TKey, TValue>(this ConcurrentDictionary<TKey, TValue> dictionary, TKey key, TValue value)
        {
            Contract.Requires(dictionary != null);
            ((IDictionary<TKey, TValue>)dictionary).Add(key, value);
        }

        /// <summary>
        /// Gets or adds the value to the dictionary
        /// </summary>
        /// <typeparam name="TKey">the key type</typeparam>
        /// <typeparam name="TValue">the element type</typeparam>
        /// <param name="dictionary">the dictionary</param>
        /// <param name="key">the key</param>
        /// <param name="addValueFactory">function which produces the element to add</param>
        /// <returns>the value retrieved or added to the dictionary</returns>
        public static TValue GetOrAdd<TKey, TValue>(this IDictionary<TKey, TValue> dictionary, TKey key, Func<TKey, TValue> addValueFactory)
        {
            Contract.Requires(dictionary != null);
            TValue result;
            if (!dictionary.TryGetValue(key, out result))
            {
                result = addValueFactory(key);
                dictionary[key] = result;
            }

            return result;
        }

        /// <summary>
        /// Gets the value from the read-only dictionary or returns the default value if the specified key is not found
        /// </summary>
        /// <typeparam name="TKey">the type of the key</typeparam>
        /// <typeparam name="TValue">the type of the value</typeparam>
        /// <param name="dictionary">the read-only dictionary</param>
        /// <param name="key">the key of the value to find</param>
        /// <param name="defaultValue">the default value if the key is not present</param>
        /// <returns>the value matching the key in the read-only dictionary, or the default value for TValue if no key is found</returns>
        public static TValue GetOrDefault<TKey, TValue>(this IReadOnlyDictionary<TKey, TValue> dictionary, TKey key, TValue defaultValue = default(TValue))
        {
            Contract.Requires(dictionary != null);
            TValue value;
            if (!dictionary.TryGetValue(key, out value))
            {
                value = defaultValue;
            }

            return value;
        }

        /// <summary>
        /// Creates a project list (see <see cref="SelectList{T, TResult}(IReadOnlyList{T}, Func{T, TResult})"/>) over the list using <paramref name="selector"/>
        /// </summary>
        public static IReadOnlyList<TResult> SelectList<T, TResult>(this IReadOnlyList<T> list, Func<T, TResult> selector)
        {
            return new SelectList<T, TResult>(list, selector);
        }

        /// <summary>
        /// Creates a projection list (see <see cref="SelectList{T, TResult}(IReadOnlyList{T}, Func{T, int, TResult})"/>) over the list using <paramref name="selector"/>
        /// </summary>
        public static IReadOnlyList<TResult> SelectList<T, TResult>(this IReadOnlyList<T> list, Func<T, int, TResult> selector)
        {
            return new SelectList<T, TResult>(list, selector);
        }

        /// <summary>
        /// Modifies the given array in-place such that true-valued and false-valued items (determined by <paramref name="predicate" />) are contiguous rather than intermixed
        /// </summary>
        public static void Partition<TValue>(
            this TValue[] values,
            Func<TValue, bool> predicate,
            out ArrayView<TValue> trueValues,
            out ArrayView<TValue> falseValues)
        {
            Contract.Requires(values != null);
            Contract.Requires(predicate != null);

            if (values.Length == 0)
            {
                trueValues = ArrayView<TValue>.Empty;
                falseValues = ArrayView<TValue>.Empty;
                return;
            }

            int leftIndex = 0;
            int rightIndex = values.Length - 1;

            // left and right indices will eventually meet (immediate for a single element array).
            while (rightIndex != leftIndex)
            {
                // Advance the left cursor to the next out-of-place false-value.
                // (but don't go past right; consider an all-true array meaning that the right cursor initially also points to a true value).
                while (leftIndex < rightIndex && predicate(values[leftIndex]))
                {
                    leftIndex++;
                }

                // Similarly for the right cursor - find the next out-of-place true-value
                while (leftIndex < rightIndex && !predicate(values[rightIndex]))
                {
                    rightIndex--;
                }

                // Swap the out-of-place pair (each is no longer out of place).
                if (leftIndex != rightIndex)
                {
                    TValue temp = values[leftIndex];
                    values[leftIndex] = values[rightIndex];
                    values[rightIndex] = temp;

                    // We've established both loop conditions above:
                    //   leftIndex < rightIndex && predicate(values[leftIndex]) && !predicate(values[rightIndex])
                    // (due to the swap).
                    // So, we can advance one or both cursors to avoid re-evaluating their predicates on the next iteration.
                    leftIndex++;
                    if (leftIndex < rightIndex)
                    {
                        rightIndex--;
                    }

                    Contract.Assert(leftIndex <= rightIndex);
                }
            }

            Contract.Assert(
                leftIndex == rightIndex,
                "Loop terminates when left and right cursors meet; we then have a pivot which could go in either the true or false view.");
            int pivotIndex = leftIndex;

            // The true values are [0, pivot] or [0, pivot - 1].
            // Shift so that the true values are instead [0, pivot - 1]
            if (predicate(values[pivotIndex]))
            {
                pivotIndex++;
            }

            trueValues = new ArrayView<TValue>(values, 0, pivotIndex);
            falseValues = new ArrayView<TValue>(values, pivotIndex, values.Length - pivotIndex);
        }

        /// <summary>
        /// Gets or adds an item to a concurrent dictionary.
        /// </summary>
        /// <remarks>
        /// Unlike <see cref="ConcurrentDictionary{TKey,TValue}.GetOrAdd(TKey,System.Func{TKey,TValue})"/>, this extension allows to provide a state
        /// that allows to create a non-capturing factory function.
        /// </remarks>
        public static TValue GetOrAddWithState<TKey, TValue, TState>(
            this ConcurrentDictionary<TKey, TValue> dictionary,
            TState state,
            TKey key,
            Func<TState, TKey, TValue> valueFactory)
        {
            Contract.Requires(dictionary != null, "dictionary != null");

            TValue resultingValue;
            if (dictionary.TryGetValue(key, out resultingValue))
            {
                return resultingValue;
            }

            var value = valueFactory(state, key);
            if (dictionary.TryAdd(key, value))
            {
                return value;
            }

            bool result = dictionary.TryGetValue(key, out resultingValue);
            Contract.Assert(result);

            return resultingValue;
        }

        /// <summary>
        /// Expands the array to be larger or equal to the required length
        /// </summary>
        public static void GrowArrayIfNecessary<T>(ref T[] array, int requiredLength)
        {
            var newLength = array.Length;
            while (requiredLength > newLength)
            {
                newLength *= 2;
            }

            Array.Resize(ref array, newLength);
        }

        /// <summary>
        /// Returns allocation-free enumerable for <see cref="IReadOnlyList{T}"/>.
        /// </summary>
        /// <remarks>
        /// The argument <paramref name="this"/> can be null.
        /// </remarks>
        public static ReadOnlyListEnumerable<T> AsStructEnumerable<T>(this IReadOnlyList<T> @this)
        {
            return new ReadOnlyListEnumerable<T>(@this);
        }

        /// <summary>
        /// Allocation-free enumerable for a <see cref="IReadOnlyList{T}"/>.
        /// </summary>
        public readonly struct ReadOnlyListEnumerable<T>
        {
            private readonly IReadOnlyList<T> m_array;

            /// <nodoc/>
            public ReadOnlyListEnumerable(IReadOnlyList<T> array)
            {
                m_array = array;
            }

            /// <nodoc/>
            public ReadOnlyListEnumerator<T> GetEnumerator()
            {
                return new ReadOnlyListEnumerator<T>(m_array);
            }
        }

        /// <summary>
        /// Allocation-free enumerator for a <see cref="IReadOnlyList{T}"/>.
        /// </summary>
        public struct ReadOnlyListEnumerator<T>
        {
            private readonly IReadOnlyList<T> m_array;
            private int m_index;

            /// <nodoc/>
            public ReadOnlyListEnumerator(IReadOnlyList<T> array)
            {
                m_array = array;
                m_index = -1;
            }

            /// <nodoc/>
            public T Current => m_array[m_index];

            /// <nodoc/>
            public bool MoveNext()
            {
                if (m_index + 1 == (m_array?.Count ?? 0))
                {
                    return false;
                }

                m_index++;
                return true;
            }
        }
    }
}
