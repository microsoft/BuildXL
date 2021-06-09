// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;

#nullable enable

namespace BuildXL.Cache.ContentStore.Interfaces.Synchronization
{
    /// <nodoc />
    public static class ReaderWriterLockSlimExtensions
    {
        /// <nodoc />
        public static bool TryAcquireReadLock(this ReaderWriterLockSlim @lock, out ReadLockExiter exiter)
        {
            if (@lock.TryEnterReadLock(0))
            {
                exiter = new ReadLockExiter(@lock);
                return true;
            }
            else
            {
                exiter = default;
                return false;
            }
        }

        /// <nodoc />
        public static ReadLockExiter AcquireReadLock(this ReaderWriterLockSlim @lock)
        {
            @lock.EnterReadLock();
            return new ReadLockExiter(@lock);
        }

        /// <nodoc />
        public readonly struct ReadLockExiter : IDisposable
        {
            private readonly ReaderWriterLockSlim _lock;

            /// <nodoc />
            internal ReadLockExiter(ReaderWriterLockSlim @lock)
            {
                _lock = @lock;
            }

            /// <inheritdoc />
            public void Dispose()
            {
                _lock.ExitReadLock();
            }
        }

        /// <nodoc />
        public static UpgradeableReadLockExiter AcquireUpgradeableRead(this ReaderWriterLockSlim @lock)
        {
            @lock.EnterUpgradeableReadLock();
            return new UpgradeableReadLockExiter(@lock);
        }

        /// <nodoc />
        public readonly struct UpgradeableReadLockExiter : IDisposable
        {
            private readonly ReaderWriterLockSlim _lock;

            /// <nodoc />
            internal UpgradeableReadLockExiter(ReaderWriterLockSlim @lock)
            {
                _lock = @lock;
            }

            /// <inheritdoc />
            public void Dispose()
            {
                if (_lock.IsWriteLockHeld)
                {
                    _lock.ExitWriteLock();
                }

                _lock.ExitUpgradeableReadLock();
            }

            /// <nodoc />
            public void EnterWrite()
            {
                _lock.EnterWriteLock();
            }
        }

        /// <nodoc />
        public static WriteLockExiter AcquireWriteLock(this ReaderWriterLockSlim @lock)
        {
            @lock.EnterWriteLock();
            return new WriteLockExiter(@lock);
        }

        /// <nodoc />
        public readonly struct WriteLockExiter : IDisposable
        {
            private readonly ReaderWriterLockSlim _lock;

            /// <nodoc />
            internal WriteLockExiter(ReaderWriterLockSlim @lock)
            {
                _lock = @lock;
            }

            /// <inheritdoc />
            public void Dispose()
            {
                _lock.ExitWriteLock();
            }
        }
    }
}
