// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Represents an async version of <see cref="Lazy{T}"/> which allows starting the underlying task to without waiting on the result
    /// </summary>
    public sealed class AsyncLazy<T>
    {
        private Lazy<Task<T>> m_lazyTask;

        /// <nodoc />
        public AsyncLazy(Func<Task<T>> factory)
        {
            m_lazyTask = new Lazy<Task<T>>(() => Task.Run(factory));    
        }

        /// <nodoc />
        private AsyncLazy(T value)
        {
            m_lazyTask = new Lazy<Task<T>>(() => Task.FromResult(value));
        }

        /// <summary>
        /// Creates an async lazy from the result value
        /// </summary>
        public static AsyncLazy<T> FromResult(T value)
        {
            return new AsyncLazy<T>(value);
        }

        /// <summary>
        /// Gets the synchronous result of the completion of the async lazy
        /// </summary>
        public T Value => m_lazyTask.Value.GetAwaiter().GetResult();

        /// <summary>
        /// Starts the asynchronous operation without blocking the current thread of execution
        /// </summary>
        public void Start()
        {
            m_lazyTask.Value.Forget();
        }
    }
}
