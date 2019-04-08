// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

namespace BuildXL.Cache.ContentStore.Interfaces.Stores
{
    /// <summary>
    ///     To avoid relying on synchronous IDisposable to block waiting for background threads
    ///     to complete (which can deadlock in some cases), we make this explicit and require
    ///     clients to shutdown services before Dispose.
    /// </summary>
    public interface IShutdown<T> : IDisposable
    {
        /// <summary>
        ///     Gets a value indicating whether check if the service has been shutdown.
        /// </summary>
        bool ShutdownCompleted { get; }

        /// <summary>
        ///     Gets a value indicating whether check if the service shutdown has begun.
        /// </summary>
        bool ShutdownStarted { get; }

        /// <summary>
        ///     Shutdown the service asynchronously.
        /// </summary>
        Task<T> ShutdownAsync(Context context);
    }
}
