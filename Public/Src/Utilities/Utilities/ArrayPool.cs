// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Thread-safe object pool of reusable arrays.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Object pools are used to improve performance for objects which would otherwise need to be created and collected
    /// at high rates. When retrieving an array of a specified capacity in this pool, a new array is created if the first
    /// retrieved array's length is less that the required minimum capacity.
    /// </para>
    /// </remarks>
    public sealed class ArrayPool<T>
    {
        private readonly ObjectPool<T[]> m_pool;
        private long m_totalCapacity;
        private long m_expansionCount;

        /// <summary>
        /// Creates a new object pool for a specific type of object.
        /// </summary>
        /// <param name="capacity">The initial capacity of the created arrays.</param>
        public ArrayPool(int capacity)
        {
            Contract.Requires(capacity > 0);

            m_pool = new ObjectPool<T[]>(
                () => new T[capacity],
                array =>
                {
                    Interlocked.Add(ref m_totalCapacity, array.Length);
                    return array;
                });
        }

        /// <summary>
        /// Gets an object instance from the pool.
        /// </summary>
        /// <param name="minimumCapacity">the minimum capacity of the array</param>
        /// <returns>An array instance.</returns>
        /// <remarks>
        /// If the pool is empty, a new object instance is allocated. Otherwise, a previously used instance
        /// is returned.
        /// </remarks>
        public PooledObjectWrapper<T[]> GetInstance(int minimumCapacity)
        {
            var wrapper = m_pool.GetInstance();

            var instance = wrapper.Instance;
            Interlocked.Add(ref m_totalCapacity, -instance.Length);

            if (instance.Length < minimumCapacity)
            {
                instance = new T[minimumCapacity];
                Interlocked.Increment(ref m_expansionCount);
            }
            else
            {
                Array.Clear(instance, 0, minimumCapacity);
            }

            return new PooledObjectWrapper<T[]>(m_pool, instance);
        }

        /// <summary>
        /// Gets the number of objects that are currently available in the pool.
        /// </summary>
        public int ObjectsInPool => m_pool.ObjectsInPool;

        /// <summary>
        /// Gets the number of times an object has been obtained from this pool.
        /// </summary>
        public long UseCount => m_pool.UseCount;

        /// <summary>
        /// Gets the total capacity of all arrays in the pool
        /// </summary>
        public long TotalCapacity => m_totalCapacity;

        /// <summary>
        /// Gets the number of times arrays needed to be resized to larger arrays
        /// </summary>
        public long ExpansionCount => m_expansionCount;

        /// <summary>
        /// Gets the number of times a factory method was called.
        /// </summary>
        public long FactoryCalls => m_pool.FactoryCalls;
    }
}
