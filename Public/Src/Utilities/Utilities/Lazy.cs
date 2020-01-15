// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Static helper class that allows to create instances of <see cref="Lazy{T}"/> using static helper function with type inference.
    /// </summary>
    public static class Lazy
    {
        /// <summary>
        /// Creates instance of <see cref="Lazy{T}"/>.
        /// </summary>
        /// <remarks>
        /// This method simply calls constructor of <see cref="Lazy{T}"/>, but because it is a static method, it will infer generic argument automatically.
        /// </remarks>
        public static Lazy<T> Create<T>(Func<T> factory, LazyThreadSafetyMode mode = LazyThreadSafetyMode.ExecutionAndPublication)
        {
            Contract.Requires(factory != null);
            return new Lazy<T>(factory, mode);
        }

        /// <summary>
        /// Creates instance of <see cref="AsyncLazy{T}"/>.
        /// </summary>
        /// <remarks>
        /// This method simply calls constructor of <see cref="AsyncLazy{T}"/>, but because it is a static method, it will infer generic argument automatically.
        /// </remarks>
        public static AsyncLazy<T> CreateAsync<T>(Func<Task<T>> factory)
        {
            Contract.Requires(factory != null);
            return new AsyncLazy<T>(factory);
        }
    }
}
