// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;

namespace TypeScript.Net
{
    /// <summary>
    /// Special version of the object pool that has no locking (or lock-free stuff) inside.
    /// </summary>
    /// <remarks>
    /// This pool should be used as a thread-local variable to avoid contention.
    /// Unlike regular pool this one has drastic performance impact on the
    /// <see cref="TypeScript.Net.Parsing.NodeWalker.ForEachChild{T}(TypeScript.Net.Types.INode,System.Func{TypeScript.Net.Types.INode,T})"/>
    /// implementation.
    /// The switch from the standard pool to this one saved 40% of end-to-end time of the checker for one core build.
    /// </remarks>
    public sealed class ThreadLocalObjectPool<T> where T : class
    {
        [DebuggerDisplay("{Value,nq}")]
        private struct Element
        {
            internal T Value;
        }

        // Storage for the pool objects. The first item is stored in a dedicated field because we
        // expect to be able to satisfy most requests from it.
        private T m_firstItem;
        private readonly Element[] m_items;

        private readonly Func<T> m_creator;

        /// <summary>
        /// Optional cleanup method that is called before putting cached object back to the pool.
        /// </summary>
        private readonly Action<T> m_cleanup;

        /// <summary>
        /// Creates a new object pool for a specific type of object.
        /// </summary>
        /// <param name="creator">A method to invoke in order to create object instances to insert into the pool.</param>
        /// <param name="cleanup">An optional method to invoke whenever an object is returned into the pool.</param>
        /// <remarks>
        /// The cleanup method is expected to return the object to a 'clean' state such that it can
        /// recycled into the pool and be handed out as a fresh instance. This method typically clears
        /// an object's state to make it look new for subsequent uses.
        /// </remarks>
        public ThreadLocalObjectPool(Func<T> creator, Action<T> cleanup)
            : this(creator, cleanup, Environment.ProcessorCount * 4)
        { }

        /// <summary>
        /// Creates a new object pool for a specific type of object.
        /// </summary>
        /// <param name="creator">A method to invoke in order to create object instances to insert into the pool.</param>
        /// <param name="cleanup">An optional method to invoke whenever an object is returned into the pool.</param>
        /// <param name="size">A size of the pool.</param>
        /// <remarks>
        /// The cleanup method is expected to return the object to a 'clean' state such that it can
        /// recycled into the pool and be handed out as a fresh instance. This method typically clears
        /// an object's state to make it look new for subsequent uses.
        /// </remarks>
        public ThreadLocalObjectPool(Func<T> creator, Action<T> cleanup, int size)
        {
            Contract.Requires(creator != null);
            Contract.Requires(size >= 1);

            m_creator = creator;
            m_cleanup = cleanup;
            m_items = new Element[size - 1];
        }

        private T CreateInstance()
        {
            return m_creator();
        }

        /// <summary>
        /// Gets an object instance from the pool.
        /// </summary>
        /// <returns>An object instance.</returns>
        /// <remarks>
        /// If the pool is empty, a new object instance is allocated. Otherwise, a previously used instance
        /// is returned.
        /// </remarks>
        public ThreadLocalPooledObjectWrapper<T> GetInstance()
        {
            // Examine the first element. If that fails, AllocateSlow will look at the remaining elements.
            T inst;
            if (m_firstItem != null)
            {
                inst = m_firstItem;
                m_firstItem = null;
            }
            else
            {
                inst = AllocateSlow();
            }

            return new ThreadLocalPooledObjectWrapper<T>(this, inst);
        }

        private T AllocateSlow()
        {
            var items = m_items;

            for (int i = 0; i < items.Length; i++)
            {
                T inst = items[i].Value;
                if (inst != null)
                {
                    items[i].Value = null;
                    return inst;
                }
            }

            return CreateInstance();
        }

        /// <summary>
        /// Returns objects to the pool.
        /// </summary>
        public void PutInstance(T obj)
        {
            m_cleanup?.Invoke(obj);

            var item = m_firstItem;
            if (item == null)
            {
                m_firstItem = obj;
            }
            else
            {
                FreeSlow(obj);
            }
        }

        private void FreeSlow(T obj)
        {
            var items = m_items;
            for (int i = 0; i < items.Length; i++)
            {
                var item = items[i].Value;
                if (item == null)
                {
                    items[i].Value = obj;
                    break;
                }
            }
        }
    }
}
