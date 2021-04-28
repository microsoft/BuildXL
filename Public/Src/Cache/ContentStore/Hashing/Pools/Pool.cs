// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Threading;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    /// A thread-safe pool of reusable objects.
    /// </summary>
    public class Pool<T> : IDisposable
    {
        // The number of times a creator was invoked.
        private long _factoryCalls;

        // Number of times an instance was obtained from the pool (the counter is incremented when regardless whether the was created or not).
        private long _useCount;

        private readonly Func<T> _factory;
        private readonly Action<T>? _reset;

        // Number of idle reserve instances to hold in the queue. -1 means unbounded
        private readonly int _maxReserveInstances;
        private readonly ConcurrentQueue<T> _queue = new ConcurrentQueue<T>();

        /// <summary>
        /// Initializes an object pool
        /// </summary>
        /// <param name="factory">Func to create a new object for the pool</param>
        /// <param name="reset">Action to reset the state of the object for future reuse</param>
        /// <param name="maxReserveInstances">Number of idle reserve instances to keep. No bound when unset</param>
        public Pool(Func<T> factory, Action<T>? reset = null, int maxReserveInstances = -1)
        {
            _factory = factory;
            _reset = reset;
            _maxReserveInstances = maxReserveInstances;
        }

        /// <summary>
        /// Gets the number of times an object has been obtained from this pool.
        /// </summary>
        public long UseCount => _useCount;

        /// <summary>
        /// Gets the number of times a factory method was called.
        /// </summary>
        public long FactoryCalls => _factoryCalls;

        /// <summary>
        /// Gets the number of objects that are currently available in the pool.
        /// </summary>
        public int Size => _queue.Count;

        /// <summary>
        /// Gets a disposable handle to an pooled object.
        /// </summary>
        /// <returns></returns>
        public virtual PoolHandle Get()
        {
            if (!_queue.TryDequeue(out var item))
            {
                Interlocked.Increment(ref _factoryCalls);
                item = _factory();
            }

            Interlocked.Increment(ref _useCount);

            return new PoolHandle(this, item);
        }

        private void Return(T item)
        {
            if ((_maxReserveInstances < 0) || (Size < _maxReserveInstances))
            {
                _reset?.Invoke(item);
                _queue.Enqueue(item);
            }
            else
            {
                // Still reset the item in case the reset logic has side effects other than cleanup for future reuse
                _reset?.Invoke(item);
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            foreach (var item in _queue)
            {
                (item as IDisposable)?.Dispose();
            }
        }

        /// <summary>
        /// A disposable handle that gets the instance back to the pool when <see cref="Dispose"/> method is called.
        /// </summary>
        public struct PoolHandle : IDisposable
        {
            private readonly Pool<T> _pool;
            private readonly T _value;
            private bool _disposed;

            /// <nodoc />
            public PoolHandle(Pool<T> pool, T value)
            {
                _pool = pool;
                _value = value;
                _disposed = false;
            }

            /// <nodoc />
            public T Value
            {
                get
                {
                    AssertValid();
                    return _value;
                }
            }

            private void AssertValid()
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(GetType().FullName);
                }
            }

            /// <inheritdoc />
            public void Dispose()
            {
                if (!_disposed)
                {
                    try
                    {
                        _pool.Return(_value);
                    }
                    catch (ObjectDisposedException)
                    {
                        // Nothing to return to...
                    }

                    _disposed = true;
                }
            }
        }
    }
}
