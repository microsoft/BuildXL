// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;

namespace BuildXL.Utilities.Collections
{
    /// <summary>
    /// Concurrent collection sorted by a given sortOrder with a bounded number of elements.
    /// </summary>
    public sealed class ConcurrentBoundedSortedCollection<TSort, TValue> : IEnumerable<KeyValuePair<TSort, TValue>> where TSort : IComparable
    {
        /// <summary>
        /// The maximum number of elements contained by this structure.
        /// </summary>
        public int Capacity { get; }

        /// <summary>
        /// The current lowest sorting value in the sorted list.
        /// </summary>
        private TSort m_currentMinimum;

        /// <summary>
        /// List containing the sorted values.
        /// </summary>
        private readonly SortedList<TSort, TValue> m_list;

        /// <summary>
        /// Lock to ensure validation does not read from old values.
        /// </summary>
        private readonly object m_lock = new object();

        /// <summary>
        /// Constructor with element limit <paramref name="capacity"/>.
        /// </summary>
        public ConcurrentBoundedSortedCollection(int capacity, IComparer<TSort> comparer = null)
        {
            Contract.Requires(capacity >= 0);

            Capacity = capacity;
            m_list = new SortedList<TSort, TValue>(Capacity + 1, comparer ?? Comparer<TSort>.Default);
        }

        /// <summary>
        /// Add a value <paramref name="value"/> with sorting value <paramref name="sort"/>
        /// if <paramref name="sort"/> is greater than the least
        /// removing the lowest value if the number of stored elements exceeds the capacity.
        /// </summary>
        /// <remarks>
        /// Worst case O(n) where n &lt;= capacity.
        /// </remarks>
        public bool TryAdd(TSort sort, TValue value)
        {
            // Skip the (hopefully) common case
            if (m_list.Count == 0 || m_list.Count < Capacity || sort.CompareTo(m_currentMinimum) > 0)
            {
                lock (m_lock)
                {
                    // Double check locking pattern
                    if (m_list.Count == 0 || m_list.Count < Capacity || sort.CompareTo(m_currentMinimum) > 0)
                    {
                        // Avoid overlap
                        if (m_list.ContainsKey(sort))
                        {
                            return false;
                        }

                        // Insert the new value
                        m_list.Add(sort, value);

                        // Trim excess
                        if (m_list.Count > Capacity)
                        {
                            m_list.RemoveAt(0);
                        }

                        if (m_list.Count != 0)
                        {
                            m_currentMinimum = m_list.ElementAt(0).Key;
                        }

                        return true;
                    }
                }
            }

            return false;
        }

        /// <inheritdoc />
        public IEnumerator<KeyValuePair<TSort, TValue>> GetEnumerator()
        {
            lock (m_lock)
            {
                return new List<KeyValuePair<TSort, TValue>>(m_list).GetEnumerator();
            }
        }

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
