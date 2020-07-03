// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;

namespace BuildXL.Cache.ContentStore.Interfaces.Stores
{
    /// <summary>
    /// Aggregation of <see cref="IStartup{T}"/> and <see cref="IShutdownSlim{T}"/> interfaces.
    /// </summary>
    public interface IStartupShutdownSlim : IStartup<BoolResult>, IShutdownSlim<BoolResult>
    {
    }

    /// <summary>
    /// Set of extension methods for <see cref="IStartupShutdownSlim"/>.
    /// </summary>
    public static class ShutdownExtensions
    {
        /// <summary>
        /// Shuts down a given <paramref name="startupShutdown"/> instance only if the instance was started successfully.
        /// </summary>
        public static Task<BoolResult> ShutdownIfStartedAsync(this IStartupShutdown startupShutdown, Context context)
        {
            if (!startupShutdown.StartupCompleted)
            {
                return BoolResult.SuccessTask;
            }

            return startupShutdown.ShutdownAsync(context);
        }
    }
}
