// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Diagnostics.ContractsLight;
using System.Threading;

namespace BuildXL.Cache.ContentStore.Pooling
{
    /// <summary>
    ///     Thread-safe pool of reusable objects.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Ported from Public\Src\Utilities\BuildXL.Utilities\ObjectPool.cs (commit
    ///         30904788516cbbaa0151e3b9a81fb820426a75f0).
    ///     </para>
    ///     <para>
    ///         Object pools are used to improve performance for objects which would otherwise need to be created and collected
    ///         at high rates.
    ///     </para>
    ///     <para>
    ///         Pools are particularly valuable for collections which suffer from bad allocation patterns
    ///         involving a lot of copying as a collection expands. For example, if code frequently needs to
    ///         use temporary lists, and the lists end up having to frequently expand their payload repeatedly during
    ///         their lifetime, that causes a lot of garbage and copying. With a pooled lists instead, the list will
    ///         expand a few times as it is used, and then will stabilize. The same list storage will be used over and
    ///         over again without ever needing to expand it again. Not only does this avoid garbage, it also helps
    ///         the cache.
    ///     </para>
    /// </remarks>
    public class ObjectPool<T> : IDisposable
        where T : class
    {
        private readonly ConcurrentBag<T> _bag = new ConcurrentBag<T>();
        private readonly Func<T> _creator;

        /// <summary>
        ///     Optional cleanup method that is called before putting cached object back to the pool.
        /// </summary>
        /// <remarks>
        ///     Using <see cref="Func&lt;T,T&gt;" /> instead of <see cref="Action&lt;T&gt;" /> allows a clients of the
        ///     <see cref="ObjectPool&lt;T&gt;" /> to disable pooling by returning new object in the cleanup method.
        ///     <example>
        ///         ObjectPool&lt;StringBuilder&gt; disabledPool = new ObjectPool&lt;StringBuilder&gt;(
        ///         creator: () => new StringBuilder(),
        ///         cleanup: sb => new StringBuilder());
        ///         ObjectPool&lt;StringBuilder&gt; regularPool = new ObjectPool&lt;StringBuilder&gt;(
        ///         creator: () => new StringBuilder(),
        ///         cleanup: sb => sb.Clear());
        ///     </example>
        /// </remarks>
        private readonly Func<T, T> _cleanup;

        private bool _disposed;
        private long _useCount;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ObjectPool{T}" /> class for a specific type of object.
        /// </summary>
        /// <param name="creator">A method to invoke in order to create object instances to insert into the pool.</param>
        /// <param name="cleanup">An optional method to invoke whenever an object is returned into the pool.</param>
        /// <remarks>
        ///     The cleanup method is expected to return the object to a 'clean' state such that it can
        ///     recycled into the pool and be handed out as a fresh instance. This method typically clears
        ///     an object's state to make it look new for subsequent uses.
        /// </remarks>
        public ObjectPool(Func<T> creator, Func<T, T> cleanup)
        {
            Contract.Requires(creator != null);

            _creator = creator;

            _cleanup = cleanup;
        }

        /// <summary>
        ///     Gets an object instance from the pool.
        /// </summary>
        /// <returns>An object instance.</returns>
        /// <remarks>
        ///     If the pool is empty, a new object instance is allocated. Otherwise, a previously used instance
        ///     is returned.
        /// </remarks>
        public PooledObjectWrapper<T> GetInstance()
        {
            T instance;

            if (!_bag.TryTake(out instance))
            {
                instance = _creator();
            }

            Interlocked.Increment(ref _useCount);

            return new PooledObjectWrapper<T>(this, instance);
        }

        /// <summary>
        ///     Cleans up a wrapped instance and puts it back into the pool
        /// </summary>
        protected internal void PutInstance(PooledObjectWrapper<T> wrapper)
        {
            T instance = _cleanup != null ? _cleanup(wrapper.Instance) : wrapper.Instance;

            _bag.Add(instance);
        }

        /// <summary>
        ///     Gets the number of objects that are currently available in the pool.
        /// </summary>
        public int ObjectsInPool => _bag.Count;

        /// <summary>
        ///     Gets the number of times an object has been obtained from this pool.
        /// </summary>
        public long UseCount => _useCount;

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Dispose(true);
            GC.SuppressFinalize(this);

            _disposed = true;
        }

        private void Dispose(bool disposing)
        {
            if (!disposing)
            {
                return;
            }

            T instance;
            while (_bag.TryTake(out instance))
            {
                var disposableInstance = instance as IDisposable;
                if (disposableInstance == null)
                {
                    break;
                }

                disposableInstance.Dispose();
            }
        }
    }
}
