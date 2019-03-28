// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

namespace BuildXL.Cache.ContentStore.Interfaces.Stores
{
    /// <summary>
    /// To avoid relying on synchronous IDisposable to block waiting for background threads
    /// to complete (which can deadlock in some cases), we make this explicit and require
    /// clients to shutdown services asynchronously instead of relying on synchronous Dispose calls.
    /// </summary>
    public interface IShutdownSlim<T> where T : ResultBase
    {
        /// <summary>
        /// Gets a value indicating whether the component has been shutdown.
        /// </summary>
        bool ShutdownCompleted { get; }

        /// <summary>
        /// Gets a value indicating whether the component shutdown has begun.
        /// </summary>
        bool ShutdownStarted { get; }

        /// <summary>
        /// Shutdown the component asynchronously.
        /// </summary>
        Task<T> ShutdownAsync(Context context);
    }
}
