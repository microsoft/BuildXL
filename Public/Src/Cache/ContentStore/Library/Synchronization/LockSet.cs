// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Cache.ContentStore.Synchronization
{
    /// <summary>
    /// This is a collection of per-key exclusive locks.
    /// Borrowed from the BuildXL code-base
    /// </summary>
    public sealed class LockSet<TKey> where TKey : IEquatable<TKey>
    {
        // ReSharper disable once StaticFieldInGenericType
        private static long _currentHandleId = 1;

        private readonly ConcurrentDictionary<TKey, LockHandle> _exclusiveLocks = new ConcurrentDictionary<TKey, LockHandle>();

        private long _totalLockWaitTimeTicks = 0;

        /// <summary>
        /// Total amount of time waiting to acquire locks for this lock set.
        /// </summary>
        public TimeSpan TotalLockWaitTime => TimeSpan.FromTicks(_totalLockWaitTimeTicks);

        /// <summary>
        /// Acquires an exclusive lock for the given key. <see cref="Release" /> must be called
        /// subsequently in a 'finally' block.
        /// </summary>
        public async Task<LockHandle> AcquireAsync(TKey key)
        {
            StopwatchSlim stopwatch = StopwatchSlim.Start();
            LockHandle thisHandle = new LockHandle(this, key);
            
            while (true)
            {
                LockHandle currentHandle = _exclusiveLocks.GetOrAdd(key, thisHandle);

                if (currentHandle != thisHandle)
                {
                    await currentHandle.TaskCompletionSource.Task;
                }
                else
                {
                    break;
                }
            }

            Interlocked.Add(ref _totalLockWaitTimeTicks, stopwatch.Elapsed.Ticks);
            return thisHandle.WithDuration(stopwatch.Elapsed);
        }

        /// <summary>
        /// Tries acquiring an exclusive lock for the given key if available.
        /// Returns a <see cref="LockHandle"/> or <code>null</code> if lock is already held by another thread.
        /// </summary>
        public LockHandle? TryAcquire(TKey key)
        {
            var thisHandle = new LockHandle(this, key);

            LockHandle currentHandle = _exclusiveLocks.GetOrAdd(key, thisHandle);
            if (currentHandle == thisHandle)
            {
                return thisHandle;
            }

            return null;
        }

        /// <summary>
        /// Releases an exclusive lock for the given key. One must release a lock after first await-ing an
        /// <see cref="AcquireAsync(TKey)" /> (by disposing the returned lock handle).
        /// </summary>
        private void Release(LockHandle handle)
        {
            // Release method may be called multiple times for the same LockHandle,
            // and the method should not be failing for the second call in a raw.
            if (_exclusiveLocks.TryRemoveSpecific(handle.Key, handle))
            {
                handle.TaskCompletionSource.SetResult(ValueUnit.Void);
            }
        }

        /// <summary>
        /// Acquire exclusive locks for a set of keys.
        /// </summary>
        public async Task<LockHandleSet> AcquireAsync(IEnumerable<TKey> keys)
        {
            Contract.Requires(keys != null);

            var sortedKeys = new List<TKey>(keys);
            sortedKeys.Sort();

            var handles = new List<LockHandle>(sortedKeys.Count);
            foreach (var key in sortedKeys)
            {
                handles.Add(await AcquireAsync(key));
            }

            return new LockHandleSet(this, handles);
        }

        /// <summary>
        /// Represents an acquired lock in the collection. Call <see cref="Dispose" />
        /// to release the acquired lock.
        /// </summary>
        public readonly struct LockHandle : IEquatable<LockHandle>, IDisposable
        {
            private readonly long _handleId;
            private readonly LockSet<TKey> _locks;

            /// <summary>
            /// The associated TaskCompletionSource.
            /// </summary>
            public readonly TaskSourceSlim<ValueUnit> TaskCompletionSource;

            /// <summary>
            /// Gets the associated Key.
            /// </summary>
            public TKey Key { get; }

            /// <summary>
            /// Optional duration of a lock acquisition.
            /// </summary>
            public TimeSpan? LockAcquisitionDuration { get; }

            /// <summary>
            /// Initializes a new instance of the <see cref="LockHandle" /> struct for the given collection/key.
            /// </summary>
            public LockHandle(LockSet<TKey> locks, TKey key)
            {
                Contract.Requires(locks != null);
                Contract.Requires(key != null);

                TaskCompletionSource = TaskSourceSlim.Create<ValueUnit>();
                _locks = locks;
                Key = key;
                _handleId = Interlocked.Increment(ref _currentHandleId);
                LockAcquisitionDuration = null;
            }

            private LockHandle(LockHandle lockHandle, TimeSpan lockAcquisitionDuration)
            {
                _locks = lockHandle._locks;
                TaskCompletionSource = lockHandle.TaskCompletionSource;
                Key = lockHandle.Key;
                _handleId = lockHandle._handleId;
                LockAcquisitionDuration = lockAcquisitionDuration;
            }

            /// <inheritdoc />
            public void Dispose()
            {
                // Release method should be robust in terms of calling it multiple times
                _locks.Release(this);
            }

            /// <inheritdoc />
            public bool Equals(LockHandle other)
            {
                return this == other;
            }

            /// <inheritdoc />
            public override bool Equals(object obj)
            {
                if (obj is LockHandle handle)
                {
                    return Equals(handle);
                }

                return false;
            }

            /// <inheritdoc />
            public override int GetHashCode()
            {
                return unchecked((int)_handleId);
            }

            /// <summary>
            /// Equality operator.
            /// </summary>
            public static bool operator ==(LockHandle left, LockHandle right)
            {
                return left._handleId == right._handleId;
            }

            /// <summary>
            /// Inequality operator.
            /// </summary>
            public static bool operator !=(LockHandle left, LockHandle right)
            {
                return !(left == right);
            }

            /// <summary>
            /// Clones the current instance and adds <paramref name="lockAcquisitionDuration"/> to it for diagnostic purposes.
            /// </summary>
            public LockHandle WithDuration(TimeSpan lockAcquisitionDuration)
            {
                return new LockHandle(this, lockAcquisitionDuration);
            }
        }

        /// <summary>
        /// Represents a set of acquired locks in the collection. Call <see cref="Dispose" />
        /// to release the acquired locks.
        /// </summary>
        public sealed class LockHandleSet : IDisposable
        {
            private readonly LockSet<TKey> _locks;
            private readonly IEnumerable<LockHandle> _handles;

            /// <summary>
            /// Initializes a new instance of the <see cref="LockHandleSet" /> class for the given set of keys.
            /// </summary>
            public LockHandleSet(LockSet<TKey> locks, IEnumerable<LockHandle> handles)
            {
                Contract.Requires(locks != null);
                Contract.Requires(handles != null);
                _locks = locks;
                _handles = handles;
            }

            /// <inheritdoc />
            public void Dispose()
            {
                foreach (var handle in _handles)
                {
                    _locks.Release(handle);
                }
            }
        }
    }
}
