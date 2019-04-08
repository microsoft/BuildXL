// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading;
using BuildXL.Utilities.Threading;

namespace BuildXL.Utilities.Collections
{
    /// <summary>
    /// Provides a cache which maps a key to a specified value.
    /// </summary>
    /// <remarks>
    ///  This class is thread safe.
    ///
    ///  Values are always evicted if a new entry's hash causes it
    /// to be placed in the slot for the incumbent value. However, values are stored in two slots so the chances of eviction of
    /// commonly used values is somewhat reduced.
    /// </remarks>
    /// <typeparam name="TKey">the key type</typeparam>
    /// <typeparam name="TValue">the value type</typeparam>
    public sealed class ObjectCache<TKey, TValue>
    {
        private struct Entry
        {
            /// <summary>
            /// 0 means not present
            /// </summary>
            public int ModifiedHashCode;
            public TKey Key;
            public TValue Value;
        }

        private readonly Entry[] m_slots;
        private readonly ReadWriteLock[] m_locks;
        private readonly IEqualityComparer<TKey> m_comparer;

        private long m_hits;
        private long m_misses;

        /// <summary>
        /// Gets the current number of cache hits
        /// </summary>
        public long Hits => Volatile.Read(ref m_hits);

        /// <summary>
        /// Gets the current number of cache misses
        /// </summary>
        public long Misses => Volatile.Read(ref m_misses);

        /// <summary>
        /// Gets the number of slots in the cache
        /// </summary>
        public int Capacity => m_slots.Length;

        /// <summary>
        /// Constructs a new lossy cache
        /// </summary>
        /// <param name="capacity">the capacity determining the number of slots available in the cache. For best results, this should be a prime number.</param>
        /// <param name="comparer">the equality comparer for computing hash codes and equality of keys</param>
        public ObjectCache(int capacity, IEqualityComparer<TKey> comparer = null)
        {
            Contract.Requires(capacity > 0);

            m_slots = new Entry[capacity];
            var locks = new ReadWriteLock[HashCodeHelper.GetGreaterOrEqualPrime(Math.Min(Environment.ProcessorCount * 4, capacity))];
            for (int i = 0; i < locks.Length; i++)
            {
                locks[i] = ReadWriteLock.Create();
            }

            m_locks = locks;
            m_comparer = comparer ?? EqualityComparer<TKey>.Default;
        }

        /// <summary>
        /// Attempts to retrieve the value for the specified key from the cache
        /// </summary>
        /// <param name="key">the key</param>
        /// <param name="value">the value</param>
        /// <returns>true if the value for the key exists in the cache, otherwise false</returns>
        public bool TryGetValue(TKey key, out TValue value)
        {
            int modifiedHashCode;
            uint index;
            Entry entry;

            // Look in the primary slot for the value
            GetEntry(key, out index, out modifiedHashCode, out entry);
            if (entry.ModifiedHashCode == modifiedHashCode)
            {
                var entryKey = entry.Key;
                if (m_comparer.Equals(entryKey, key))
                {
                    Interlocked.Increment(ref m_hits);
                    value = entry.Value;
                    return true;
                }
            }

            // Try the backup slot
            modifiedHashCode = HashCodeHelper.Combine(modifiedHashCode, 17);
            GetEntry(ref modifiedHashCode, out index, out entry);
            if (entry.ModifiedHashCode == modifiedHashCode)
            {
                var entryKey = entry.Key;
                if (m_comparer.Equals(entryKey, key))
                {
                    Interlocked.Increment(ref m_hits);
                    value = entry.Value;
                    return true;
                }
            }

            Interlocked.Increment(ref m_misses);
            value = default(TValue);
            return false;
        }

        /// <summary>
        /// Clears the object cache.
        /// </summary>
        public void Clear()
        {
            for (int i = 0; i < m_slots.Length; i++)
            {
                m_slots[i] = new Entry();
            }
        }

        private void GetEntry(TKey key, out uint index, out int modifiedHashCode, out Entry entry)
        {
            Contract.Ensures(Contract.ValueAtReturn<int>(out modifiedHashCode) != 0);

            modifiedHashCode = m_comparer.GetHashCode(key);

            GetEntry(ref modifiedHashCode, out index, out entry);
        }

        private void GetEntry(ref int modifiedHashCode, out uint index, out Entry entry)
        {
            Contract.Ensures(Contract.ValueAtReturn<int>(out modifiedHashCode) != 0);

            // Zero is reserved hash code for unset entries
            if (modifiedHashCode == 0)
            {
                modifiedHashCode = int.MaxValue;
            }

            unchecked
            {
                index = (uint)modifiedHashCode % (uint)m_slots.Length;

                // Note: A global lock here gets a ton of contention (1.2% of all execution of BuildXL time is spent waiting here), so we use many locks
                using (m_locks[(uint)index % (uint)m_locks.Length].AcquireReadLock())
                {
                    entry = m_slots[index];
                }
            }
        }

        private void SetEntry(uint index, Entry entry)
        {
            using (var writeLock = m_locks[(uint)index % (uint)m_locks.Length].TryAcquireWriteLock())
            {
                // Only write if we acquire the write lock
                if (writeLock.IsValid)
                {
                    m_slots[index] = entry;
                }
            }
        }

        /// <summary>
        /// Adds an item to the cache
        /// </summary>
        /// <param name="key">the key</param>
        /// <param name="value">the value</param>
        /// <returns>true if the item was not found in the cache</returns>
        public bool AddItem(TKey key, TValue value)
        {
            int missCount = 0;
            int modifiedHashCode;
            uint index;
            Entry entry;

            // Place in the primary slot
            GetEntry(key, out index, out modifiedHashCode, out entry);
            if (entry.ModifiedHashCode != modifiedHashCode || !m_comparer.Equals(entry.Key, key))
            {
                entry = new Entry()
                {
                    ModifiedHashCode = modifiedHashCode,
                    Key = key,
                    Value = value,
                };

                SetEntry(index, entry);
                missCount++;
            }

            // Place in the backup slot as well
            modifiedHashCode = HashCodeHelper.Combine(modifiedHashCode, 17);
            GetEntry(ref modifiedHashCode, out index, out entry);
            if (entry.ModifiedHashCode != modifiedHashCode || !m_comparer.Equals(entry.Key, key))
            {
                entry = new Entry()
                {
                    ModifiedHashCode = modifiedHashCode,
                    Key = key,
                    Value = value,
                };

                SetEntry(index, entry);
                missCount++;
            }

            // value was missed on both slots so report not found by returning true
            return missCount == 2;
        }
    }
}
