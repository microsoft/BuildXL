// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;

namespace Tool.ExecutionLogSdk
{
    /// <summary>
    /// This class implements a concurrent hash set with synchronized versions of the Add, AddRange and Contains methods
    /// </summary>
    /// <typeparam name="TItem">The type of the elements stored in the hash set</typeparam>
    internal sealed class ConcurrentHashSet<TItem> : HashSet<TItem>, IReadOnlyCollection<TItem>
    {
        #region Private properties

        /// <summary>
        /// Lock object used for synchronization
        /// </summary>
        private readonly object m_lockObj = new object();
        #endregion

        #region Internal methods

        /// <summary>
        /// Adds a new item to the hash set. This method is thread safe.
        /// </summary>
        /// <param name="item">The new item to be added</param>
        /// <returns>true when the item has been added, false when the item was already in the collection</returns>
        internal new bool Add(TItem item)
        {
            lock (m_lockObj)
            {
                return base.Add(item);
            }
        }

        /// <summary>
        /// Adds a set of new items to the hash set. This method is thread safe.
        /// </summary>
        /// <param name="items">The new items to be added</param>
        internal void AddRange(IEnumerable<TItem> items)
        {
            // for simplicity, we will lock once and add all the items to the collection without releasing the lock after each item
            lock (m_lockObj)
            {
                foreach (var i in items)
                {
                    base.Add(i);
                }
            }
        }

        /// <summary>
        /// Checks if an item is in the collection or not. This method is thread safe.
        /// </summary>
        /// <param name="item">The item to be checked</param>
        /// <returns>true if the item is in the collection, false otherwise</returns>
        internal new bool Contains(TItem item)
        {
            lock (m_lockObj)
            {
                return base.Contains(item);
            }
        }
        #endregion
    }
}
