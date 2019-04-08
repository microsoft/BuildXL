// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading;

namespace BuildXL.Utilities.Threading
{
    /// <summary>
    /// A simple and slim reader-writer lock which only occupies the space for an object and an integer.
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public readonly struct ReadWriteLock
    {
        /// <summary>
        /// An invalid read write lock.
        /// </summary>
        public static readonly ReadWriteLock Invalid = default(ReadWriteLock);

        /// <summary>
        /// The lock used to get read and write access to the value
        /// </summary>
        private readonly Locker m_locker;

        /// <summary>
        /// Indicates if the lock is valid
        /// </summary>
        [Pure]
        public bool IsValid => m_locker != null;

        private ReadWriteLock(Locker locker)
        {
            m_locker = locker;
        }

        /// <summary>
        /// Creates a reader writer lock
        /// </summary>
        public static ReadWriteLock Create()
        {
            return new ReadWriteLock(new Locker());
        }

        /// <summary>
        /// Acquires a read lock that can be disposed to release the lock
        /// </summary>
        [Pure]
        public ReadLock AcquireReadLock()
        {
            EnterReadLock();
            return new ReadLock(this);
        }

        /// <summary>
        /// Acquires a write lock that can be disposed to release the lock
        /// </summary>
        /// <remarks>
        /// This optionally allows read locks to be acquired during writes while still preventing concurrent writes. Reads exclusion
        /// can be triggered later by calling <see cref="ExcludeReads()"/>
        /// </remarks>
        /// <param name="allowReads">indicates whether the write lock allows reads to continue during write</param>
        [Pure]
        public WriteLock AcquireWriteLock(bool allowReads = false)
        {
            EnterWriteLock(allowReads);
            return new WriteLock(this);
        }

        /// <summary>
        /// Attempts to acquires a write lock that can be disposed to release the lock
        /// </summary>
        /// <remarks>
        /// This optionally allows read locks to be acquired during writes while still preventing concurrent writes. Reads exclusion
        /// can be triggered later by calling <see cref="ExcludeReads()"/>
        /// </remarks>
        /// <param name="allowReads">indicates whether the write lock allows reads to continue during write</param>
        /// <returns>A valid write lock if the write lock was acquired. Otherwise, invalid write lock.</returns>
        [Pure]
        public WriteLock TryAcquireWriteLock(bool allowReads = false)
        {
            bool acquired = TryEnterWriteLock(allowReads);
            return new WriteLock(acquired ? this : Invalid);
        }

        /// <summary>
        /// Indicates if the lock has exclusive write access in which reader
        /// are excluded
        /// </summary>
        [Pure]
        public bool HasExclusiveAccess => IsValid && m_locker.HasExclusiveAccess;

        /// <summary>
        /// This method may be called while holding a read-allowing write lock to prevent acquisition
        /// of new read locks and wait on current outstanding read locks to be released.
        /// </summary>
        public void ExcludeReads()
        {
            m_locker.ExcludeReads();
        }

        /// <summary>
        /// Acquires a read lock
        /// </summary>
        public void EnterReadLock()
        {
            m_locker.EnterReadLock();
        }

        /// <summary>
        /// Releases a read lock.
        /// WARNING: Does not verify that same thread entered read lock.
        /// </summary>
        public void ExitReadLock()
        {
            m_locker.ExitReadLock();
        }

        /// <summary>
        /// Acquires a write lock
        /// </summary>
        /// <remarks>
        /// This optionally allows read locks to be acquired during writes while still preventing concurrent writes. Reads exclusion
        /// can be triggered later by calling  <see cref="ExcludeReads()"/>
        /// </remarks>
        /// <param name="allowReads">indicates whether the write lock allows reads to continue during write</param>
        public void EnterWriteLock(bool allowReads = false)
        {
            m_locker.EnterWriteLock(allowReads);
        }

        /// <summary>
        /// Attempts to acquire a write lock
        /// </summary>
        /// <remarks>
        /// This optionally allows read locks to be acquired during writes while still preventing concurrent writes. Reads exclusion
        /// can be triggered later by calling  <see cref="ExcludeReads()"/>
        /// </remarks>
        /// <param name="allowReads">indicates whether the write lock allows reads to continue during write</param>
        /// <returns>Returns true if the write lock was acquired. Otherwise, false.</returns>
        public bool TryEnterWriteLock(bool allowReads = false)
        {
            return m_locker.TryEnterWriteLock(allowReads);
        }

        /// <summary>
        /// Releases a write lock.
        /// </summary>
        public void ExitWriteLock()
        {
            m_locker.ExitWriteLock();
        }

        /// <summary>
        /// Actual implementation lock for reader writer lock. This class is private since it does Monitor.Enter(this) which is generally
        /// dangerous so we wrap it so its impossible for external users to get access the Locker object.
        ///
        /// General principle is that writes acquire a mutex (ala Monitor.Enter) and set
        /// a write flag bit which prevents further reads because all readers will wait on the monitor in the event
        /// that the write flag is set. Readers increment a reader counter
        /// which pending writer spin waits to reach zero.
        /// </summary>
        private sealed class Locker
        {
            private const int WRITE_FLAG = 1 << 31;

            private int m_readerCountAndWriteFlag;

            public void EnterReadLock()
            {
                while (true)
                {
                    int readerCountAndWriteFlag = Interlocked.Increment(ref m_readerCountAndWriteFlag);
                    if (readerCountAndWriteFlag < 0)
                    {
                        // Write flag is set
                        // 1. Decrement the reader count so the write can proceed
                        // 2. Wait on the write lock, but don't hold it for the duration of the read operation
                        Interlocked.Decrement(ref m_readerCountAndWriteFlag);

                        SpinWait spinWait = default(SpinWait);
                        while (Volatile.Read(ref m_readerCountAndWriteFlag) < 0)
                        {
                            // Write flag is set.
                            // Spin wait for it to be unset so the read can proceed
                            spinWait.SpinOnce();
                        }
                    }
                    else
                    {
                        break;
                    }
                }
            }

            public void ExitReadLock()
            {
                Interlocked.Decrement(ref m_readerCountAndWriteFlag);
            }

            public void EnterWriteLock(bool allowReads)
            {
                Monitor.Enter(this);

                if (!allowReads)
                {
                    ExcludeReads();
                }
            }

            public bool TryEnterWriteLock(bool allowReads)
            {
                bool enteredWriteLock = Monitor.TryEnter(this);

                if (enteredWriteLock && !allowReads)
                {
                    ExcludeReads();
                }

                return enteredWriteLock;
            }

            public bool HasExclusiveAccess => Monitor.IsEntered(this) && Volatile.Read(ref m_readerCountAndWriteFlag) == WRITE_FLAG;

            public void ExcludeReads()
            {
                int readerCountAndWriteFlag = Volatile.Read(ref m_readerCountAndWriteFlag);
                if (readerCountAndWriteFlag < 0)
                {
                    // Write flag is set so must have entered lock recursively
                    throw new LockRecursionException();
                }

                // Set the top bit
                Interlocked.Add(ref m_readerCountAndWriteFlag, WRITE_FLAG);

                SpinWait spinWait = default(SpinWait);
                while (Volatile.Read(ref m_readerCountAndWriteFlag) != WRITE_FLAG)
                {
                    spinWait.SpinOnce();
                }
            }

            public void ExitWriteLock()
            {
                int readerCountAndWriteFlag = Volatile.Read(ref m_readerCountAndWriteFlag);
                if (readerCountAndWriteFlag < 0)
                {
                    // The top bit is set, so this will unset the top bit
                    Interlocked.Add(ref m_readerCountAndWriteFlag, WRITE_FLAG);
                }

                Monitor.Exit(this);
            }
        }
    }

    /// <summary>
    /// A read lock which allows concurrent access to the underlying object
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public readonly struct ReadLock : IDisposable
    {
        /// <summary>
        /// An invalid lock
        /// </summary>
        public static readonly ReadLock Invalid = default(ReadLock);

        private readonly ReadWriteLock m_lock;

        /// <summary>
        /// Indicates if the lock is a properly initialized
        /// </summary>
        [Pure]
        public bool IsValid => m_lock.IsValid;

        /// <summary>
        /// Constructor
        /// </summary>
        public ReadLock(ReadWriteLock rwLock)
        {
            m_lock = rwLock;
        }

        /// <summary>
        /// Exits the read lock
        /// </summary>
        public void Dispose()
        {
            if (IsValid)
            {
                m_lock.ExitReadLock();
            }
        }
    }

    /// <summary>
    /// A write lock which ensures exclusive access to the underlying object
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public readonly struct WriteLock : IDisposable
    {
        /// <summary>
        /// An invalid lock
        /// </summary>
        public static readonly WriteLock Invalid = default(WriteLock);

        private readonly ReadWriteLock m_lock;

        /// <summary>
        /// Indicates if the lock is a properly initialized
        /// </summary>
        [Pure]
        public bool IsValid => m_lock.IsValid;

        /// <summary>
        /// Indicates if the lock has exclusive write access in which reader
        /// are excluded
        /// </summary>
        [Pure]
        public bool HasExclusiveAccess => IsValid && m_lock.HasExclusiveAccess;

        /// <summary>
        /// Constructor
        /// </summary>
        public WriteLock(ReadWriteLock rwLock)
        {
            m_lock = rwLock;
        }

        /// <summary>
        /// This method may be called while holding a read-allowing write lock to prevent acquisition
        /// of new read locks and wait on current outstanding read locks to be released.
        /// </summary>
        public void ExcludeReads()
        {
            if (IsValid)
            {
                m_lock.ExcludeReads();
            }
        }

        /// <summary>
        /// Exits the write lock
        /// </summary>
        public void Dispose()
        {
            if (IsValid)
            {
                m_lock.ExitWriteLock();
            }
        }
    }
}
