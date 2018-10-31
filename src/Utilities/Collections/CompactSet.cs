// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;

namespace BuildXL.Utilities.Collections
{
    /// <summary>
    /// A set with a compact representation
    /// </summary>
    /// <remarks>
    /// The basic size of this struct is one pointer.
    /// The empty set has no further overhead.
    /// The set of one element has the overhead of a boxed value.
    /// Sets with 1 to 4 elements have the overhead of an array (of that many elements).
    /// Greater sets are backed by a full HashSet (using the default equality comparer).
    /// </remarks>
    [SuppressMessage("Microsoft.Naming", "CA1710:NameShouldEndInCollection", Justification = "But it is a set...")]
    [SuppressMessage("Microsoft.Performance", "CA1815:ShouldOverrideEquals")]
    public readonly struct CompactSet<T> : IEnumerable<T>
        where T : IEquatable<T>
    {
        /// <summary>
        /// We represent up to four elements with an array.
        /// </summary>
        /// <remarks>
        /// Consider fine-tuning this number.
        /// Up to this number, lookups, adding/removing have linear complexity.
        /// This is practically irrelevant due to the small bound.
        /// However, in practice many sets are small, and thus we can reduce an order of magnitude in memory consumption by avoiding HashSet.
        /// </remarks>
        private const int MaxArrayLength = 4;
        private readonly object m_value;

        private CompactSet(object value)
        {
            Contract.Requires(value == null || value is T[] || value is HashSet<T> || value is T);
            m_value = value;
        }

        /// <summary>
        /// Creates a CompactSet from an existing array
        /// </summary>
        public static CompactSet<T> FromArray(T[] array)
        {
            Contract.Requires(array != null);

            if (array.Length == 0)
            {
                return new CompactSet<T>(null);
            }
            else if (array.Length == 1)
            {
                return new CompactSet<T>(array[0]);
            }
            else if (array.Length <= MaxArrayLength)
            {
                return new CompactSet<T>(array);
            }
            else
            {
                return new CompactSet<T>(new HashSet<T>(array));
            }
        }

        /// <summary>
        /// Adds an element.
        /// </summary>
        /// <remarks>
        /// After this method returns, the previous <code>CompactSet</code> value must not be used again. Use only the returned
        /// value.
        /// </remarks>
        public CompactSet<T> Add(T value)
        {
            if (m_value == null)
            {
                return new CompactSet<T>(value);
            }

            if (m_value is T[] array)
            {
                foreach (T x in array)
                {
                    if (x.Equals(value))
                    {
                        return this;
                    }
                }

                if (array.Length < MaxArrayLength)
                {
                    var newArray = new T[array.Length + 1];
                    Array.Copy(array, newArray, array.Length);
                    newArray[array.Length] = value;
                    return new CompactSet<T>(newArray);
                }

                return new CompactSet<T>(new HashSet<T>(array) { value });
            }

            if (m_value is HashSet<T> set)
            {
                set.Add(value);
                return this;
            }

            var other = (T)m_value;
            if (value.Equals(other))
            {
                return this;
            }

            return new CompactSet<T>(new T[] { other, value });
        }

        /// <summary>
        /// Removes an element.
        /// </summary>
        /// <remarks>
        /// After this method returns, the previous <code>CompactSet</code> value must not be used again. Use only the returned
        /// value.
        /// </remarks>
        public CompactSet<T> Remove(T value)
        {
            if (m_value == null)
            {
                return this;
            }

            if (m_value is T[] array)
            {
                for (int i = 0; i < array.Length; i++)
                {
                    if (array[i].Equals(value))
                    {
                        if (array.Length == 2)
                        {
                            return new CompactSet<T>(array[1 - i]);
                        }

                        var newArray = new T[array.Length - 1];
                        Array.Copy(array, 0, newArray, 0, i);
                        Array.Copy(array, i + 1, newArray, i, newArray.Length - i);
                        return new CompactSet<T>(newArray);
                    }
                }

                return this;
            }

            if (m_value is HashSet<T> set)
            {
                set.Remove(value);
                if (set.Count <= MaxArrayLength)
                {
                    return new CompactSet<T>(set.ToArray());
                }

                return this;
            }

            if (value.Equals((T)m_value))
            {
                return default(CompactSet<T>);
            }

            return this;
        }

        /// <summary>
        /// Count of elements in this set.
        /// </summary>
        public int Count
        {
            get
            {
                if (m_value == null)
                {
                    return 0;
                }

                if (m_value is T[] array)
                {
                    return array.Length;
                }

                if (m_value is HashSet<T> set)
                {
                    return set.Count;
                }

                return 1;
            }
        }

        /// <summary>
        /// Checks whether an value is contained in this set.
        /// </summary>
        public bool Contains(T value)
        {
            if (m_value == null)
            {
                return false;
            }

            if (m_value is T[] array)
            {
                foreach (T x in array)
                {
                    if (x.Equals(value))
                    {
                        return true;
                    }
                }

                return false;
            }

            if (m_value is HashSet<T> set)
            {
                return set.Contains(value);
            }

            var other = (T)m_value;
            return other.Equals(value);
        }

        /// <summary>
        /// Enumerator
        /// </summary>
        public struct Enumerator : IEnumerator<T>
        {
            private readonly object m_value;
            private int m_index;
            private HashSet<T>.Enumerator m_hashSetEnumerator;

            internal Enumerator(object value)
            {
                m_value = value;
                m_index = -1;
                var set = m_value as HashSet<T>;
                m_hashSetEnumerator = set != null ? set.GetEnumerator() : default(HashSet<T>.Enumerator);
            }

            /// <inheritdoc />
            public T Current
            {
                get
                {
                    if (m_value is T[] array)
                    {
                        return array[m_index];
                    }

                    if (m_value is HashSet<T> set)
                    {
                        return m_hashSetEnumerator.Current;
                    }

                    return (T)m_value;
                }
            }

            /// <inheritdoc />
            public void Dispose()
            {
                m_hashSetEnumerator.Dispose();
            }

            object IEnumerator.Current => Current;

            /// <inheritdoc />
            public bool MoveNext()
            {
                m_index++;

                if (m_value == null)
                {
                    return false;
                }

                if (m_value is T[] array)
                {
                    return m_index < array.Length;
                }

                if (m_value is HashSet<T> set)
                {
                    return m_hashSetEnumerator.MoveNext();
                }

                return m_index == 0;
            }

            /// <inheritdoc />
            public void Reset()
            {
                m_index = -1;
                if (m_value is HashSet<T> set)
                {
                    m_hashSetEnumerator = set.GetEnumerator();
                }
            }
        }

        /// <summary>
        /// Gets an enumerator for this set.
        /// </summary>
        public Enumerator GetEnumerator()
        {
            return new Enumerator(m_value);
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            if (m_value is IEnumerable<T> enumerable)
            {
                return enumerable.GetEnumerator();
            }

            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
