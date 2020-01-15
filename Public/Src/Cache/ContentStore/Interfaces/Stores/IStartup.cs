// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
