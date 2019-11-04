// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Diagnostics.ContractsLight;

// disable 'Missing XML comment for publicly visible type' warnings.
#pragma warning disable 1591
#pragma warning disable SA1600 // Elements must be documented
#pragma warning disable SA1402 // One file one class.
#pragma warning disable SA1649 // File name must match first type.

namespace BuildXL.Cache.ContentStore.Hashing
{
    public interface IPoolHandle<out T> : IDisposable
    {
        /// <summary>
        ///     Gets value.
        /// </summary>
        T Value { get; }

        /// <summary>
        ///     Asserts validity.
        /// </summary>
        void AssertValid();
    }

    public sealed class ByteArrayPool : Pool<byte[]>
    {
        private static readonly Action<byte[]> Reset = b =>
        {
#if DEBUG
            b[0] = 0xcc;
            b[b.Length - 1] = 0xcc;
#endif

        };

        private static byte[] CreateNew(int bufferSize)
        {
            var bytes = new byte[bufferSize];
            Reset(bytes);
            return bytes;
        }

        public ByteArrayPool(int bufferSize)
            : base(() => CreateNew(bufferSize), Reset)
        {
        }

        public override PoolHandle Get()
        {
            var bytes = base.Get();
#if DEBUG
            Contract.Assert(bytes.Value[0] == 0xcc);
            Contract.Assert(bytes.Value[bytes.Value.Length - 1] == 0xcc);
#endif
            return bytes;
        }
    }

    public class Pool<T> : IDisposable
    {
        private readonly Func<T> _factory;
        private readonly Action<T> _reset;
        private readonly int _maxReserveInstances;
        private readonly ConcurrentQueue<T> _queue = new ConcurrentQueue<T>();

        public Pool(Func<T> factory, Action<T> reset = null, int maxReserveInstances = -1)
        {
            _factory = factory;
            _reset = reset;
            _maxReserveInstances = maxReserveInstances;
        }

        public int Size => _queue.Count;

        public virtual PoolHandle Get()
        {
            if (!_queue.TryDequeue(out var item))
            {
                item = _factory();
            }

            return new PoolHandle(this, item);
        }

        private void Return(T item)
        {
            if ((_maxReserveInstances > 0) && (Size < _maxReserveInstances))
            {
                _reset?.Invoke(item);
                _queue.Enqueue(item);
            }
            else
            {
                // Still reset the item incase the reset logic has side effects other than cleanup for future reuse
                _reset?.Invoke(item);
            }
        }

        public void Dispose()
        {
            foreach (var item in _queue)
            {
                (item as IDisposable)?.Dispose();
            }
        }

        public struct PoolHandle : IPoolHandle<T>
        {
            private readonly Pool<T> _pool;
            private readonly T _value;
            private bool _disposed;

            public PoolHandle(Pool<T> pool, T value)
            {
                _pool = pool;
                _value = value;
                _disposed = false;
            }

            public T Value
            {
                get
                {
                    AssertValid();
                    return _value;
                }
            }

            public void AssertValid()
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(GetType().FullName);
                }
            }

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
