// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;

namespace TypeScript.Net
{
    /// <summary>
    /// Object wrapper whose purpose it is to provide a Dispose method that returns the wrapped object to its pool.
    /// </summary>
    public readonly struct ThreadLocalPooledObjectWrapper<T> : IDisposable
        where T : class
    {
        private readonly ThreadLocalObjectPool<T> m_pool;

        internal ThreadLocalPooledObjectWrapper(ThreadLocalObjectPool<T> pool, T instance)
        {
            Contract.Requires(pool != null);
            Contract.Requires(instance != null);

            m_pool = pool;
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
            m_pool.PutInstance(Instance);
        }

        /// <summary>
        /// Gets the object being wrapped.
        /// </summary>
        public T Instance { get; }
    }
}
