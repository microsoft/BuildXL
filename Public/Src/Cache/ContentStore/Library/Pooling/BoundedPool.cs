// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Utils;

namespace BuildXL.Cache.ContentStore.Pooling
{
    /// <summary>
    /// Pool of objects/resources that is bounded to the number of instances added to it.
    /// </summary>
    public sealed class BoundedPool<T> : IDisposable
    {
        private readonly ConcurrentBag<T> _objects = new ConcurrentBag<T>();
        private readonly SemaphoreSlim _objectsAvailable = new SemaphoreSlim(0, int.MaxValue);

        /// <summary>
        /// Add an instance to the pool
        /// </summary>
        public void Add(T obj)
        {
            _objects.Add(obj);
            _objectsAvailable.Release();
        }

        /// <summary>
        /// Wait for an instance to become available and then get it.
        /// </summary>
        public async Task<IPoolToken> GetAsync()
        {
            await _objectsAvailable.WaitAsync();
            SemaphoreSlim semaphoreToBeReleased = _objectsAvailable;
            try
            {
                T obj;
                if (!_objects.TryTake(out obj))
                {
                    throw ContractUtilities.AssertFailure("Semaphore was available, but pool is empty.");
                }

                var token = new BoundedPoolToken(obj, this);
                semaphoreToBeReleased = null;
                return token;
            }
            finally
            {
                if (semaphoreToBeReleased != null)
                {
                    _objectsAvailable.Release();
                }
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            T obj;
            while (_objects.TryTake(out obj))
            {
                IDisposable disposable = obj as IDisposable;
                disposable?.Dispose();
            }

            _objectsAvailable.Dispose();
        }

        /// <summary>
        /// Token that contains the pooled object
        /// </summary>
        /// <remarks>Object is returned to the pool on Dispose</remarks>
        public interface IPoolToken : IDisposable
        {
            /// <summary>
            /// Gets object on loan from the pool
            /// </summary>
            T Value { get; }
        }

        /// <inheritdoc/>
        private struct BoundedPoolToken : IPoolToken
        {
            private readonly BoundedPool<T> _pool;
            private bool _disposed;

            /// <inheritdoc/>
            public T Value { get; }

            /// <summary>
            /// Initializes a new instance of the <see cref="BoundedPoolToken"/> struct.
            /// </summary>
            public BoundedPoolToken(T value, BoundedPool<T> pool)
            {
                _pool = pool;
                Value = value;
                _disposed = false;
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _pool.Add(Value);
            }
        }
    }
}
