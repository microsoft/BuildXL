// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities.Collections;
using JetBrains.Annotations;
using TypeScript.Net.Types;

namespace TypeScript.Net.Extensions
{
    /// <summary>
    /// Set of extension methods for array, <see cref="List{T}"/> and <see cref="Map{T}"/> that helps to migrate code from TypeScript to C#.
    /// </summary>
    public static class CollectionExtensions
    {
        /// <nodoc />
        public static T Pop<T>(this List<T> list)
        {
            if (list.Count == 0)
            {
                return default(T);
            }

            var result = list[list.Count - 1];
            list.RemoveAt(list.Count - 1);
            return result;
        }

        /// <nodoc />
        public static void SetLength<T>(this List<T> list, int length)
        {
            if (list.Count > length)
            {
                list.RemoveRange(length, list.Count - length);
            }
        }

        /// <nodoc />
        public static string Join<T>(this T[] array, string separator)
        {
            return string.Join(separator, array);
        }

        /// <nodoc />
        public static T[] Slice<T>(this T[] array, int index)
        {
            return array.Skip(index).ToArray();
        }

        /// <nodoc />
        public static T[] Slice<T>(this T[] array, int index, int length)
        {
            return array.Skip(index).Take(length).ToArray();
        }

        // TODO: This is currently untested. Verify correctness!

        /// <summary>
        /// Removes elements from an array, returning the deleted elements.
        /// </summary>
        /// <param name="array">Array to splice.</param>
        /// <param name="start">The zero-based location in the array from which to start removing elements.</param>
        public static List<T> Splice<T>(this List<T> array, int start)
        {
            var deletedElements = array.Skip(start).ToList();
            array.RemoveRange(start, array.Count - start);

            return deletedElements;
        }

        // TODO: This is currently untested. Verify correctness!

        /// <summary>
        /// Removes elements from an array and, if necessary, inserts new elements in their place, returning the deleted elements.
        /// </summary>
        /// <param name="array">Array to splice.</param>
        /// <param name="start">The zero-based location in the array from which to start removing elements.</param>
        /// <param name="deleteCount">The number of elements to remove.</param>
        /// <param name="item">Element to insert into the array in place of the deleted elements.</param>
        public static void Splice<T>(this List<T> array, int start, int deleteCount, T item = null) where T : class
        {
            if (deleteCount > 0)
            {
                array.RemoveRange(start, deleteCount);
            }

            if (item != null)
            {
                array.Insert(start, item);
            }
        }

        /// <nodoc />
        public static bool Contains<T>(this List<T> array, T value)
        {
            if (array != null)
            {
                return array.Contains(value);
            }

            return false;
        }

        /// <nodoc />
        public static bool Contains<T>([CanBeNull] this IReadOnlyList<T> list, T item)
        {
            if (list == null)
            {
                return false;
            }

            if (item == null)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i] == null)
                    {
                        return true;
                    }
                }

                return false;
            }

            EqualityComparer<T> c = EqualityComparer<T>.Default;
            for (int i = 0; i < list.Count; i++)
            {
                if (c.Equals(list[i], item))
                {
                    return true;
                }
            }

            return false;
        }

        /// <nodoc />
        public static int IndexOf<T>(IReadOnlyList<T> array, T value, IEqualityComparer<T> comparer)
        {
            if (array != null)
            {
                for (var i = 0; i < array.Count; i++)
                {
                    if (comparer.Equals(array[i], value))
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        /// <nodoc />
        public static int IndexOf<T>(NodeArray<T> array, T value, IEqualityComparer<T> comparer)
        {
            if (array != null)
            {
                for (var i = 0; i < array.Length; i++)
                {
                    if (comparer.Equals(array[i], value))
                    {
                        return i;
                    }
                }
            }

            return -1;
        }

        /// <nodoc />
        public static int IndexOf<T>(this IReadOnlyList<T> array, T value) where T : INode
        {
            return IndexOf(array, value, NodeComparer<T>.Instance);
        }

        /// <nodoc />
        public static int IndexOf<T>(NodeArray<T> array, T value) where T : INode
        {
            return IndexOf(array, value, NodeComparer<T>.Instance);
        }

        // TODO: Verify correctness/equivalence

        /// <nodoc />
        public static List<T> Filter<T>(this IReadOnlyList<T> array, Func<T, bool> f)
        {
            List<T> result = null;

            if (array != null)
            {
                result = new List<T>(array.Count);
                foreach (var item in array.AsStructEnumerable())
                {
                    if (f(item))
                    {
                        result.Add(item);
                    }
                }
            }

            return result;
        }

        /// <nodoc />
        public static List<TItem> Filter<TItem, TState>(this IReadOnlyList<TItem> array, TState state, Func<TItem, TState, bool> f)
        {
            List<TItem> result = null;

            if (array != null)
            {
                result = new List<TItem>(array.Count / 2);
                foreach (var item in array.AsStructEnumerable())
                {
                    if (f(item, state))
                    {
                        result.Add(item);
                    }
                }
            }

            return result;
        }

        /// <nodoc />
        public static List<TResult> Map<T, TResult>(NodeArray<T> array, Func<T, TResult> f)
        {
            List<TResult> result = null;

            if (array != null)
            {
                result = new List<TResult>(array.Count);
                foreach (var v in array)
                {
                    result.Add(f(v));
                }
            }

            return result;
        }

        /// <nodoc />
        public static List<TValue> Map<TKey, TValue>(this IReadOnlyList<TKey> array, Func<TKey, TValue> f)
        {
            if (array == null)
            {
                return null;
            }

            var result = new List<TValue>(array.Count);
            for (int i = 0; i < array.Count; i++)
            {
                result.Add(f(array[i]));
            }

            return result;
        }

        /// <nodoc />
        public static List<TValue> Map<TKey, TValue, TState>(this IReadOnlyList<TKey> array, TState state, Func<TKey, TState, TValue> f)
        {
            if (array == null)
            {
                return null;
            }

            var result = new List<TValue>(array.Count);
            for (int i = 0; i < array.Count; i++)
            {
                result.Add(f(array[i], state));
            }

            return result;
        }

        /// <nodoc />
        public static T[] Concatenate<T>(this T[] left, T[] right)
        {
            if (right == null || right.Length == 0)
            {
                return left;
            }

            if (left == null || left.Length == 0)
            {
                return right;
            }

            return left.Concat(right).ToArray();
        }

        /// <nodoc />
        public static List<T> Concatenate<T>(this IEnumerable<T> left, IEnumerable<T> right)
        {
            if (left == null && right == null)
            {
                return null;
            }

            if (right == null)
            {
                return left.ToList();
            }

            if (left == null)
            {
                return right.ToList();
            }

            return left.Concat(right).ToList();
        }

        // TODO: Verify correctness/equivalence. Ported from core.ts

        /// <nodoc />
        public static bool RangeEquals<T>(this IReadOnlyList<T> array1, IReadOnlyList<T> array2, int pos, int end, IEqualityComparer<T> comparer)
        {
            while (pos < end)
            {
                if (!comparer.Equals(array1[pos], array2[pos]))
                {
                    return false;
                }

                pos++;
            }

            return true;
        }

        /// <nodoc />
        public static bool RangeEquals<T>(this IReadOnlyList<T> array1, IReadOnlyList<T> array2, int pos, int end) where T : INode
        {
            return RangeEquals(array1, array2, pos, end, NodeComparer<T>.Instance);
        }

        // TODO: Verify correctness/equivalence. Ported from core.ts

        /// <nodoc />
        public static T LastOrUndefined<T>(this IEnumerable<T> array)
        {
            return array.LastOrDefault();
        }

        /// <nodoc />
        public static void CopyMap<T>(IDictionary<string, T> source, IDictionary<string, T> target)
        {
            // The following code still allocates an iterator, but it never shows at the profiler.
            foreach (var p in source.Keys)
            {
                target[p] = source[p];
            }
        }

        /// <nodoc />
        public static IReadOnlyList<T> Concatenate<T>(this IReadOnlyList<T> @this, T element)
        {
            if (@this == null && element == null)
            {
                return CollectionUtilities.EmptyArray<T>();
            }

            if (element == null)
            {
                return @this;
            }

            if (@this == null)
            {
                // Consider switching to a special struct-like array with one element.
                return new List<T> { element };
            }

            var result = new List<T>(@this.Count + 1);
            for (int i = 0; i < @this.Count; i++)
            {
                result.Add(@this[i]);
            }

            result.Add(element);
            return result;
        }

        /// <summary>
        /// Allocation free LINQ-like 'Where' function.
        /// </summary>
        public static ReadOnlyListWhereEnumerable<T> Where<T>([NotNull]this IReadOnlyList<T> @this, [NotNull]Func<T, bool> predicate)
        {
            return new ReadOnlyListWhereEnumerable<T>(@this, predicate);
        }

        /// <summary>
        /// More performant version of a ToList method for <see cref="ReadOnlyListWhereEnumerable{T}"/>.
        /// </summary>
        public static IReadOnlyList<T> ToList<T>(this ReadOnlyListWhereEnumerable<T> @this)
        {
            var list = new List<T>();

            foreach (var e in @this)
            {
                list.Add(e);
            }

            return list;
        }

        /// <summary>
        /// Allocation-free enumerable for a <see cref="IReadOnlyList{T}"/>.
        /// </summary>
        public readonly struct ReadOnlyListWhereEnumerable<T>
        {
            private readonly IReadOnlyList<T> m_array;
            private readonly Func<T, bool> m_predicate;

            /// <nodoc/>
            public ReadOnlyListWhereEnumerable([NotNull]IReadOnlyList<T> array, Func<T, bool> predicate)
            {
                m_array = array;
                m_predicate = predicate;
            }

            /// <nodoc/>
            public ReadOnlyListWhereEnumerator<T> GetEnumerator()
            {
                return new ReadOnlyListWhereEnumerator<T>(m_array, m_predicate);
            }
        }

        /// <summary>
        /// Allocation-free enumerator for a <see cref="IReadOnlyList{T}"/>.
        /// </summary>
        public struct ReadOnlyListWhereEnumerator<T>
        {
            private readonly IReadOnlyList<T> m_array;
            private readonly Func<T, bool> m_predicate;

            private int m_index;

            /// <nodoc/>
            public ReadOnlyListWhereEnumerator([NotNull]IReadOnlyList<T> array, Func<T, bool> predicate)
            {
                m_array = array;
                m_index = -1;
                m_predicate = predicate;
            }

            /// <nodoc/>
            public T Current => m_array[m_index];

            /// <nodoc/>
            public bool MoveNext()
            {
                // Need to find an element that matches a predicate.
                for (int i = m_index; i < m_array.Count; i++)
                {
                    if (i + 1 == m_array.Count)
                    {
                        return false;
                    }

                    m_index++;
                    if (m_predicate(Current))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// Converts sequence to dictionary, but accepts duplicate keys. First will win.
        /// </summary>
        public static Dictionary<TKey, TValue> ToDictionarySafe<T, TKey, TValue>(this IEnumerable<T> source, Func<T, TKey> keySelector,
            Func<T, TValue> valueSelector)
        {
            Contract.Requires(source != null);
            Contract.Requires(keySelector != null);
            Contract.Requires(valueSelector != null);

            Dictionary<TKey, TValue> result = new Dictionary<TKey, TValue>();

            foreach (var element in source)
            {
                var key = keySelector(element);
                var value = valueSelector(element);

                if (!result.ContainsKey(key))
                {
                    result.Add(key, value);
                }
            }

            return result;
        }

        /// <summary>
        /// Returns whether a <paramref name="source"/> is null or empty.
        /// </summary>
        public static bool IsNullOrEmpty<T>(this IEnumerable<T> source)
        {
            return source == null || !source.Any();
        }

        /// <summary>
        /// Returns whether a <paramref name="source"/> is null or empty.
        /// </summary>
        public static bool IsNullOrEmpty<T>(this IReadOnlyList<T> source)
        {
            return source == null || source.Count == 0;
        }
    }
}
