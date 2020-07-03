// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading;

namespace BuildXL.Utilities.Threading
{
    /// <summary>
    /// A simple and slim reader-writer lock which only occupies the space for an object and a long.
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
            Contract.RequiresNotNull(locker);
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
            return new WriteLock(this, allowReads);
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
            return new WriteLock(acquired ? this : Invalid, allowReads);
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
        public void ExitWriteLock(bool ensureExcludeReadsLockReleased = false)
        {
            m_locker.ExitWriteLock(ensureExcludeReadsLockReleased);
        }

        /// <summary>
        /// Actual implementation lock for reader writer lock. This class is private since it does Monitor.Enter(this) which is generally
        /// dangerous so we wrap it so its impossible for external users to get access the Locker object.
        ///
        /// General principle is that writes acquire a mutex (ala Monitor.Enter) and set
        /// a write flag bit which prevents further reads because all readers will wait on the monitor in the event
        /// that the write flag is set. Readers increment a reader counter
        /// which pending writer spin waits to reach zero.
        /// 
        /// We are intentionally using Interlocked.CompareExchange to read the value of m_readerCountAndWriteFlag (CompareExchange always
        /// returns the original value regardless whether the exchange happened or not)
        /// </summary>
        private sealed class Locker
        {
            private const long WRITE_FLAG = 1L << 48;
            private const long FIRST_OVERFLOW_BIT = 1L << 31;

            // 63...........48  47...........31  30..............0
            // write lock flag  overflow buffer  read lock counter
            private long m_readerCountAndWriteFlag;

            public bool HasExclusiveAccess => Monitor.IsEntered(this) && Interlocked.CompareExchange(ref m_readerCountAndWriteFlag, 0, 0) == WRITE_FLAG;

            public void EnterReadLock()
            {
                while (true)
                {
                    long readerCountAndWriteFlag = Interlocked.Increment(ref m_readerCountAndWriteFlag);
                    if (readerCountAndWriteFlag >= WRITE_FLAG)
                    {
                        // Write flag is set
                        // 1. Decrement the reader count so the write can proceed
                        // 2. Wait on the write lock, but don't hold it for the duration of the read operation
                        Interlocked.Decrement(ref m_readerCountAndWriteFlag);

                        SpinWait spinWait = default(SpinWait);
                        while (Interlocked.CompareExchange(ref m_readerCountAndWriteFlag, 0, 0) >= WRITE_FLAG)
                        {
                            // Write flag is set.
                            // Spin wait for it to be unset so the read can proceed
                            spinWait.SpinOnce();
                        }
                    }
                    else if (readerCountAndWriteFlag >= FIRST_OVERFLOW_BIT)
                    {
                        throw new OverflowException($"readerCountAndWriteFlag={readerCountAndWriteFlag}");
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

            public void ExcludeReads()
            {
                long readerCountAndWriteFlag = Interlocked.CompareExchange(ref m_readerCountAndWriteFlag, 0, 0);
                if (readerCountAndWriteFlag >= WRITE_FLAG)
                {
                    // Write flag is set so must have entered lock recursively
                    throw new LockRecursionException($"m_readerCountAndWriteFlag={readerCountAndWriteFlag}");
                }

                // Set the write flag
                Interlocked.Add(ref m_readerCountAndWriteFlag, WRITE_FLAG);

                // Wait until all previously acquired read locks are released.
                SpinWait spinWait = default(SpinWait);
                while (Interlocked.CompareExchange(ref m_readerCountAndWriteFlag, 0, 0) != WRITE_FLAG)
                {
                    spinWait.SpinOnce();
                }
            }

            public void ExitWriteLock(bool ensureExcludeReadsLockReleased)
            {
                long readerCountAndWriteFlag = Interlocked.CompareExchange(ref m_readerCountAndWriteFlag, 0, 0);
                // Check if the flag is set; and unset it if necessary.
                // The flag is set in ExcludeReads method, and we only step into that method if allowReads is false,
                // i.e., it is possible to acquire a write lock without setting the flag.
                if (readerCountAndWriteFlag >= WRITE_FLAG)
                {
                    Interlocked.Add(ref m_readerCountAndWriteFlag, -WRITE_FLAG);
                }
                else if (ensureExcludeReadsLockReleased)
                {
                    // If ensureExcludeReadLockReleased is true, we know that we are holding an exclude reads write lock that needs to be released.
                    // If we stepped into this branch, something is wrong with the m_readerCountAndWriteFlag variable, i.e., the flag is missing.
                    throw new InvalidOperationException($"Expected to unset the write lock flag, but it is already unset (m_readerCountAndWriteFlag={m_readerCountAndWriteFlag}).");
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
        private readonly bool m_allowReads;

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
        public WriteLock(ReadWriteLock rwLock, bool allowReads)
        {
            m_lock = rwLock;
            m_allowReads = allowReads;
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
                // If WriteLock is configured to exclude reads, ensure that we are unsetting the flag.
                // This check is not applicable to write locks that were upgraded from allow reads to exclude reads.
                m_lock.ExitWriteLock(ensureExcludeReadsLockReleased: !m_allowReads);
            }
        }
    }
}
