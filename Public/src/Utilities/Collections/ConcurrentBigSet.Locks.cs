// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading;
using BuildXL.Utilities.Threading;

namespace BuildXL.Utilities.Collections
{
    public sealed partial class ConcurrentBigSet<TItem>
    {
        /// <summary>
        /// Helper class for managing locks and corresponding write counts for a ConcurrentBigSet
        /// </summary>
        private sealed class Locks
        {
            // A set of reader-writer locks, each guarding a section of the table
            private readonly ReadWriteLock[] m_locks;

            /// <summary>
            /// An array indicating the number of writes for a particular lock. This can be used to determine if
            /// work needs to be retried between acquisition of a read lock and a subsequent read lock or write lock.
            /// </summary>
            private readonly int[] m_lockWriteCounts;

            public Locks(int concurrencyLevel)
            {
                m_locks = new ReadWriteLock[concurrencyLevel];
                m_lockWriteCounts = new int[concurrencyLevel];

                for (int i = 0; i < m_locks.Length; i++)
                {
                    m_locks[i] = ReadWriteLock.Create();
                }
            }

            /// <summary>
            /// Gets the number of locks.
            /// </summary>
            public int Length => m_locks.Length;

            /// <summary>
            /// Acquires the write lock at the given index. Optionally allowing concurrent reads while holding the write lock.
            /// </summary>
            public WriteLock AcquireWriteLock(int lockNo, bool allowReads)
            {
                int priorLockWriteCount;
                return AcquireWriteLock(lockNo, out priorLockWriteCount, allowReads);
            }

            /// <summary>
            /// Acquires the write lock at the given index. Optionally allowing concurrent reads while holding the write lock. Also,
            /// increments the write count for the lock and returns the prior write count
            /// </summary>
            public WriteLock AcquireWriteLock(int lockNo, out int priorLockWriteCount, bool allowReads)
            {
                var writeLock = m_locks[lockNo].AcquireWriteLock(allowReads);
                priorLockWriteCount = Interlocked.Increment(ref m_lockWriteCounts[lockNo]) - 1;
                return writeLock;
            }

            /// <summary>
            /// Acquires a read lock at the given index and returns the write count for the lock.
            /// </summary>
            public ReadLock AcquireReadLock(int lockNo, out int lockWriteCount)
            {
                lockWriteCount = Volatile.Read(ref m_lockWriteCounts[lockNo]);
                return m_locks[lockNo].AcquireReadLock();
            }
        }
    }
}
