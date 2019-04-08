// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Runtime.CompilerServices;

namespace TypeScript.Net
{
    /// <summary>
    /// Specialized resizable array suitable for specific concurrent scenarios.
    /// </summary>
    /// <remarks>
    /// The checker needs to keep some auxiliary information about nodes and symbols.
    /// Originally this information was kept in a concurrent dictionary from NodeId to NodeLinks.
    /// But that solution was not very effective in terms of speed and memory.
    /// This special vector keeps data in an array and allows to 'get or add' new items for a given index.
    /// The checker uses this data structure as a map with index as a key.
    /// </remarks>
    public sealed class ConcurrentVector<T>
    {
        // Using default size that List<T> is use.

        /// <nodoc/>
        public const int DefaultSize = 4;
        private volatile T[] m_data;

        /// <nodoc />
        public ConcurrentVector(int size = DefaultSize)
        {
            Contract.Requires(size > 0);

            m_data = new T[size];
        }

        /// <summary>
        /// Returns an item for a give index or gets the new item via <paramref name="provider"/> and returns it.
        /// </summary>
        /// <remarks>
        /// Current method has two cases: if the element is already presented in the array, then we can just safely return it
        /// without any locks.
        /// If the element is not present in the array or the given index is outside the range of the current array,
        /// then the slow path is executed. In this case, the lock can be acquired to prevent the race.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T GetOrAdd(int index, Func<T> provider)
        {
            Contract.Assert(index >= 0);
            Contract.Assert(provider != null);

            var candidate = Get(index);
            if (candidate != null)
            {
                return candidate;
            }

            // We've missed. Need to add an item to the list.
            return GetOrAddSlow(index, provider);
        }

        /// <summary>
        /// Returns an item from the vector.
        /// </summary>
        /// <remarks>
        /// We know all ConcurrentVector's are write-once data structures.
        /// This allows us to read the data without any locks.
        /// If the data is present, it can't be removed.
        /// So the result, if presented, is correct.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Get(int index)
        {
            Contract.Assert(index >= 0);

            // Copy m_data into local to prevent races.
            var data = m_data;
            if (index < data.Length)
            {
                return data[index];
            }

            return default(T);
        }

        private T GetOrAddSlow(int index, Func<T> provider)
        {
            EnsureCapacity(index);

            // We didn't get the data by calling Get method
            // Need to acquire the lock to prevent race with EnsureCapacity
            // when current thread is dealing with the last element in the current vector
            lock (this)
            {
                // Using double-checked locking: maybe another thread already set the value.
                // (First check happened in Get method call)
                var candidate = m_data[index];
                if (candidate == null)
                {
                    candidate = provider();
                    Contract.Assert(candidate != null);
                    m_data[index] = candidate;
                }

                return candidate;
            }
        }

        private void EnsureCapacity(int index)
        {
            if (index >= m_data.Length)
            {
                lock (this)
                {
                    // Double check locking.
                    if (index >= m_data.Length)
                    {
                        var newSize = m_data.Length * 2;

                        // Make sure that the new size is sufficient for a requested index.
                        newSize = index < newSize ? newSize : index + 1;

                        T[] newItems = new T[newSize];
                        Array.Copy(m_data, 0, newItems, 0, m_data.Length);
                        m_data = newItems;
                    }
                }
            }
        }
    }
}
