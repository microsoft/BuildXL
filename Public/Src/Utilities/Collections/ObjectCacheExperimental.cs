// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading;

namespace BuildXL.Utilities.Collections
{
    /// <inheritdoc/>
    public class ObjectCacheExperimental<TKey, TValue> : ObjectCache<TKey, TValue>
    {
        private readonly ReaderWriterLockSlim[] m_locks;

        /// <inheritdoc/>
        public ObjectCacheExperimental(int capacity, IEqualityComparer<TKey>? comparer = null)
            : base(capacity, comparer)
        {
            var locks = new ReaderWriterLockSlim[HashCodeHelper.GetGreaterOrEqualPrime(Math.Min(Environment.ProcessorCount * 4, capacity))];
            for (int i = 0; i < locks.Length; i++)
            {
                locks[i] = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
            }

            m_locks = locks;
        }

        /// <inheritdoc/>
        protected override void GetEntry(ref int modifiedHashCode, out uint index, out Entry entry)
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
                uint lockIndex = (uint)index % (uint)m_locks.Length;

                try
                {
                    m_locks[lockIndex].EnterReadLock();
                    entry = m_slots[index];
                }
                finally
                {
                    m_locks[lockIndex].ExitReadLock();
                }
            }
        }

        /// <inheritdoc/>
        protected override void SetEntry(uint index, Entry entry)
        {
            uint lockIndex = (uint)index % (uint)m_locks.Length;

            bool lockAcquired = false;
            try
            {
                // Try to get a write lock, but do not wait for the lock to become available.
                lockAcquired = m_locks[lockIndex].TryEnterWriteLock(TimeSpan.Zero);

                // Only write if we successfully acquire the write lock
                if (lockAcquired)
                {
                    m_slots[index] = entry;
                }
            }
            finally
            {
                if (lockAcquired)
                {
                    m_locks[lockIndex].ExitWriteLock();
                }
            }
        }
    }
}
