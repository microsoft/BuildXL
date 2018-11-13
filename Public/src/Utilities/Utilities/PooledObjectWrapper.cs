// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Threading;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Object wrapper whose purpose it is to provide a Dispose method that returns the wrapped object to its pool.
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public readonly struct PooledObjectWrapper<T> : IDisposable
        where T : class
    {
#if DEBUG
        /// <summary>
        /// Helper class for DEBUG builds to make sure that we only dispose this wrapper once.
        /// </summary>
        /// <remarks>
        /// This needs to go on the heap with an identity, as the wrapper itself is a struct that can be copied around.
        /// </remarks>
        private class Identity
        {
            public ObjectPool<T> Pool;
            public int Disposed;
        }

        private readonly Identity m_identity;
#else
        private readonly ObjectPool<T> m_pool;
#endif

        internal PooledObjectWrapper(ObjectPool<T> pool, T instance)
        {
            Contract.Requires(pool != null);
            Contract.Requires(instance != null);

#if DEBUG
            m_identity = new Identity { Pool = pool };
#else
            m_pool = pool;
#endif
            Instance = instance;
        }

        /// <summary>
        /// Returns the object being wrapped to its pool.
        /// </summary>
        /// <remarks>
        /// Once this method has been called, the wrapped object should no longer be used.
        /// </remarks>
        public void Dispose()
        {
#if DEBUG
            var incremented = Interlocked.Increment(ref m_identity.Disposed);
            Contract.Assume(incremented == 1, "PooledObjectWrappers must be disposed only once!");
            m_identity.Pool.PutInstance(Instance);
#else
            m_pool.PutInstance(Instance);
#endif
        }

        /// <summary>
        /// Gets the object being wrapped.
        /// </summary>
        public T Instance { get; }
    }
}
