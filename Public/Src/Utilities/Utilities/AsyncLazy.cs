// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
        private readonly Lazy<Task<T>> m_lazyTask;

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
        /// Gets the asynchronous result of the completion of the async lazy
        /// </summary>
        public Task<T> GetValueAsync()
        {
            return m_lazyTask.Value;
        }

        /// <summary>
        /// Whether the value has actually been created or not
        /// </summary>
        public bool IsValueCreated => m_lazyTask.IsValueCreated;

        /// <summary>
        /// Whether the task has completed
        /// </summary>
        public bool IsCompleted => m_lazyTask.IsValueCreated && m_lazyTask.Value.IsCompleted;

        /// <summary>
        /// Starts the asynchronous operation without blocking the current thread of execution
        /// </summary>
        public void Start()
        {
            m_lazyTask.Value.Forget();
        }
    }
}
