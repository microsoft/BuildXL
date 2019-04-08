// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;

namespace BuildXL.Utilities
{
    // TODO: this struct can leverage 'DontUseDefaultContstructorAttribute' from ErrorProne.NET because
    // this struct should never be used with default constructor!

    /// <summary>
    /// Lazy-like struct that wraps <see cref="Task"/>
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public struct CachedTask
    {
        private readonly object m_taskSyncRoot;
        private Task m_task;

        /// <nodoc/>
        private CachedTask(object taskSyncRoot)
            : this()
        {
            m_taskSyncRoot = taskSyncRoot;
        }

        /// <nodoc/>
        public static CachedTask Create()
        {
            return new CachedTask(taskSyncRoot: new object());
        }

        /// <summary>
        /// Returns already created task or creates new otherwise.
        /// </summary>
        public Task GetOrCreate(Func<Task> factory)
        {
            Contract.Requires(factory != null);

            // Can't use LazyInitializer.EnsureInitialized because it can call factory method
            // more than once! So using double-checked lock manually to ensure, that factory method
            // would be called at most once.
            if (m_task == null)
            {
                lock (m_taskSyncRoot)
                {
                    if (m_task == null)
                    {
                        m_task = factory();
                    }
                }
            }

            return m_task;
        }
    }

    /// <summary>
    /// Lazy-like struct that wraps <see cref="Task{TResult}"/>
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public struct CachedTask<T>
    {
        private readonly object m_taskSyncRoot;
        private volatile Task<T> m_task;

        /// <nodoc/>
        private CachedTask(object taskSyncRoot)
            : this()
        {
            m_taskSyncRoot = taskSyncRoot;
        }

        /// <nodoc/>
        public static CachedTask<T> Create()
        {
            return new CachedTask<T>(taskSyncRoot: new object());
        }

        /// <summary>
        /// Returns already created task or creates new otherwise.
        /// </summary>
        public Task<T> GetOrCreate(Func<Task<T>> factory)
        {
            Contract.Requires(factory != null);

            // Can't use LazyInitializer.EnsureInitialized because it can call factory method
            // more than once! So using double-checked lock manually to ensure, that factory method
            // would be called at most once.
            if (m_task == null)
            {
                lock (m_taskSyncRoot)
                {
                    if (m_task == null)
                    {
                        m_task = factory();
                    }
                }
            }

            return m_task;
        }

        /// <summary>
        /// Returns already created task or creates new otherwise.
        /// </summary>
        public Task<T> GetOrCreate<TData>(TData data, Func<TData, Task<T>> factory)
        {
            Contract.Requires(factory != null);

            // Can't use LazyInitializer.EnsureInitialized because it can call factory method
            // more than once! So using double-checked lock manually to ensure, that factory method
            // would be called at most once.
            if (m_task == null)
            {
                lock (m_taskSyncRoot)
                {
                    if (m_task == null)
                    {
                        m_task = factory(data);
                    }
                }
            }

            return m_task;
        }

        /// <summary>
        /// Resets the cache.
        /// </summary>
        public void Reset()
        {
            lock (m_taskSyncRoot)
            {
                m_task = null;
            }
        }
    }
}
