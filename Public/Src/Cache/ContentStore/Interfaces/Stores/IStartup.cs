// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

namespace BuildXL.Cache.ContentStore.Interfaces.Stores
{
    /// <summary>
    ///     Some implementations require initialization outside the constructor.
    /// </summary>
    public interface IStartup<T>
    {
        /// <summary>
        ///     Gets a value indicating whether check if the service has started.
        /// </summary>
        bool StartupCompleted { get; }

        /// <summary>
        ///     Gets a value indicating whether check if the service startup has begun.
        /// </summary>
        bool StartupStarted { get; }

        /// <summary>
        ///     Startup the service asynchronously.
        /// </summary>
        Task<T> StartupAsync(Context context);
    }
}
