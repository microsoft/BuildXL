// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;

namespace BuildXL.Utilities.Collections
{
    /// <summary>
    /// A thread-safe priority queue
    /// </summary>
    /// <remarks>
    /// Higher priorities are preferred. The lowest priority 0 gets handled in a particularly efficient way.
    /// </remarks>
    [SuppressMessage("Microsoft.Naming", "CA1711:IdentifiersShouldNotHaveIncorrectSuffix", Justification = "But it is a queue...")]
    public sealed class ConcurrentPriorityQueue<T>
    {
        // TODO: Consider a better implementation that doesn't have a global lock
        private readonly object m_syncRoot = new object();
        private readonly ConcurrentQueue<T> m_lowestPriorityItems = new ConcurrentQueue<T>();
        private SortedDictionary<int, Queue<T>> m_items;

        /// <summary>
        /// Enqueue a value
        /// </summary>
        public void Enqueue(int priority, T value)
        {
            Contract.Requires(priority >= 0);

            if (priority == 0)
            {
                m_lowestPriorityItems.Enqueue(value);
                return;
            }

            lock (m_syncRoot)
            {
                if (m_items == null)
                {
                    m_items = new SortedDictionary<int, Queue<T>>();
                }

                int inversePriority = int.MaxValue - priority;
                Queue<T> q;
                if (!m_items.TryGetValue(inversePriority, out q))
                {
                    m_items.Add(inversePriority, q = new Queue<T>());
                }

                q.Enqueue(value);
            }
        }

        /// <summary>
        /// Try to peek at the queue
        /// </summary>
        public bool TryPeek(out int priority, out T value)
        {
            if (Volatile.Read(ref m_items) != null)
            {
                lock (m_syncRoot)
                {
                    KeyValuePair<int, Queue<T>> kvp = m_items.FirstOrDefault();
                    Queue<T> q = kvp.Value;
                    if (q != null)
                    {
                        priority = int.MaxValue - kvp.Key;
                        Contract.Assert(priority > 0);
                        value = q.Peek();

                        return true;
                    }
                }
            }

            if (m_lowestPriorityItems.TryPeek(out value))
            {
                priority = 0;
                return true;
            }

            priority = 0;
            value = default(T);
            return false;
        }

        /// <summary>
        /// Try to dequeue a value
        /// </summary>
        public bool TryDequeue(out int priority, out T value)
        {
            if (Volatile.Read(ref m_items) != null)
            {
                lock (m_syncRoot)
                {
                    KeyValuePair<int, Queue<T>> kvp = m_items.FirstOrDefault();
                    Queue<T> q = kvp.Value;
                    if (q != null)
                    {
                        priority = int.MaxValue - kvp.Key;
                        Contract.Assert(priority > 0);
                        value = q.Dequeue();
                        if (q.Count == 0)
                        {
                            m_items.Remove(kvp.Key);
                        }

                        return true;
                    }
                }
            }

            if (m_lowestPriorityItems.TryDequeue(out value))
            {
                priority = 0;
                return true;
            }

            priority = 0;
            value = default(T);
            return false;
        }
    }
}
