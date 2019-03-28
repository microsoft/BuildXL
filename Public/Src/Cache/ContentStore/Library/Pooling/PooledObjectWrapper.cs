// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
#if DEBUG
using System.Threading;
#endif

namespace BuildXL.Cache.ContentStore.Pooling
{
    /// <summary>
    ///     Object wrapper whose purpose it is to provide a Dispose method that returns the wrapped object to its pool.
    /// </summary>
    /// <remarks>
    ///     Ported from Public\Src\Utilities\BuildXL.Utilities\PooledObjectWrapper.cs (commit
    ///     30904788516cbbaa0151e3b9a81fb820426a75f0).
    /// </remarks>
    [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public readonly struct PooledObjectWrapper<T> : IDisposable
        where T : class
    {
#if DEBUG
        /// <summary>
        ///     Helper class for DEBUG builds to make sure that we only dispose this wrapper once.
        /// </summary>
        /// <remarks>
        ///     This needs to go on the heap with an identity, as the wrapper itself is a struct that can be copied around.
        /// </remarks>
        private class Identity
        {
            public ObjectPool<T> Pool;
            public int Disposed;
        }

        private readonly Identity _identity;
#else
        private readonly ObjectPool<T> _pool;
#endif

        /// <summary>
        ///     Initializes a new instance of the <see cref="PooledObjectWrapper{T}"/> struct.
        /// </summary>
        internal PooledObjectWrapper(ObjectPool<T> pool, T instance)
        {
            Contract.Requires(pool != null);
            Contract.Requires(instance != null);

#if DEBUG
            _identity = new Identity {Pool = pool};
#else
            _pool = pool;
#endif
            Instance = instance;
        }

        /// <summary>
        ///     Returns the object being wrapped to its pool.
        /// </summary>
        /// <remarks>
        ///     Once this method has been called, the wrapped object should no longer be used.
        /// </remarks>
        public void Dispose()
        {
#if DEBUG
            var incremented = Interlocked.Increment(ref _identity.Disposed);
            Contract.Assume(incremented == 1, "PooledObjectWrappers must be disposed only once!");
            _identity.Pool.PutInstance(this);
#else
            _pool.PutInstance(this);
#endif
        }

        /// <summary>
        ///     Gets the object being wrapped.
        /// </summary>
        public T Instance { get; }
    }
}
