// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Utilities.Collections
{
    /// <summary>
    /// Concurrent array implementation
    /// </summary>
    /// <remarks>
    /// The use case of this arraylist is many-read, few writes and few elements.
    /// This list is not enumerable because it is optimized for specific index.
    /// </remarks>
    /// <typeparam name="T">The type of elements in the ArrayList.</typeparam>
    public sealed class ConcurrentArrayList<T>
        where T : class
    {
        private readonly int m_initialSize;
        private readonly bool m_allowResize;

        // I had tried to use ReaderWriterLockSlim but the fact that one is IDisposable is very painful.
        private readonly object m_lock = new object();
        private T[] m_array;

        /// <summary>
        /// Constructs a new ConcurrentArray
        /// </summary>
        public ConcurrentArrayList(int initialSize, bool allowResize)
        {
            Contract.Requires(initialSize >= 0);

            m_initialSize = initialSize;
            m_allowResize = allowResize;
            m_array = new T[initialSize];
        }

        /// <summary>
        /// Gets the value at the current index.
        /// </summary>
        /// <remarks>
        /// This function will return null if not found or out of range.
        /// </remarks>
        public T this[int index]
        {
            get
            {
                Contract.Requires(index >= 0);

                // Fetch local pointer to array in case it gets mutated by a set.
                var array = m_array;

                if (index >= array.Length)
                {
                    return null;
                }

                return array[index];
            }

            set
            {
                Contract.Requires(index >= 0);
                SetValueAtIndex(index, value);
            }
        }

        /// <summary>
        /// Gets or adds a value to the array.
        /// </summary>
        /// <remarks>
        /// If the array is re-sizable the add might update the array.
        /// It is also possible that createValue is called by multiple threads, so it better be idempotent.
        /// It is illegal for createValue to return null. Because null indicates non-existing value.
        /// </remarks>
        public T GetOrSet(int index, Func<T> createValue)
        {
            Contract.Requires(index >= 0);

            // Fetch local pointer to array in case it gets mutated by a set.
            var array = m_array;

            if (index < array.Length)
            {
                var value = array[index];
                if (value != null)
                {
                    return value;
                }
            }

            T createdValue = createValue();
            SetValueAtIndex(index, createdValue);
            return createdValue;
        }

        private void SetValueAtIndex(int index, T value)
        {
            // Have to use a lock here to avoid "Lost-Update" of a thread inserting a value in the array that fits and another resizing.
            lock (m_lock)
            {
                var array = m_array;
                var existingLength = array.Length;
                if (existingLength <= index)
                {
                    if (m_allowResize)
                    {
                        var newArray = new T[index + 1 + m_initialSize]; // pad with the initial size.
                        Array.Copy(array, newArray, existingLength);

                        m_array = newArray;
                    }
                    else
                    {
                        throw new ArgumentOutOfRangeException(nameof(index));
                    }
                }

                Contract.Assume(value != null, "Value ");
                m_array[index] = value;
            }
        }
    }
}
