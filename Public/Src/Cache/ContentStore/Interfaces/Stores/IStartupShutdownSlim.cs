// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
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

        /// <summary>
        /// Creates a lexically scoped guard to shutdown a component
        /// </summary>
        public static async Task<IAsyncDisposable> StartupWithAutoShutdownAsync(this IStartupShutdownSlim component, Context context)
        {
            await component.StartupAsync(context).ThrowIfFailureAsync();

            return new ShutdownGuard(component, context);
        }

        private class ShutdownGuard : IAsyncDisposable
        {
            private readonly IStartupShutdownSlim _component;
            private readonly Context _context;

            public ShutdownGuard(IStartupShutdownSlim component, Context context)
            {
                _component = component;
                _context = context;
            }

#pragma warning disable AsyncFixer01 // Unnecessary async/await usage
            public async ValueTask DisposeAsync()
            {
                await _component.ShutdownAsync(_context).ThrowIfFailureAsync();
            }
#pragma warning restore AsyncFixer01 // Unnecessary async/await usage
        }
    }
}
