// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing;

namespace BuildXL.Cache.ContentStore.Utils
{
    /// <summary>
    /// An interface used for controlling the lifetime of the service.
    /// </summary>
    public interface ILifetimeManager
    {
        /// <summary>
        /// Request a graceful shutdown of a current service instance.
        /// </summary>
        void RequestTeardown(string reason);
    }

    /// <summary>
    /// A global entry point for gracefully shutting down a current service instance.
    /// </summary>
    public static class LifetimeManager
    {
        private static ILifetimeManager? _lifetimeManager;

        /// <summary>
        /// Notifies listener when teardown is requested
        /// </summary>
        public static event Action<(Context context, string reason)>? OnTeardownRequested;

        /// <summary>
        /// Sets a given <paramref name="lifetimeManager"/> as a global instance controlling service's lifetime.
        /// </summary>
        public static void SetLifetimeManager(ILifetimeManager lifetimeManager) => _lifetimeManager = lifetimeManager;

        /// <summary>
        /// Request a graceful shutdown of a current service instance.
        /// </summary>
        /// <remarks> The operation will succeed only if <see cref="SetLifetimeManager"/> method was called.</remarks>
        public static void RequestTeardown(Context context, string reason)
        {
            var lifetimeManager = _lifetimeManager;

            if (lifetimeManager == null)
            {
                context.Warning("Can't teardown the instance because lifetime manager is not available.", component: nameof(LifetimeManager));
            }
            else
            {
                LifetimeTracker.TeardownRequested(context, reason);
                lifetimeManager.RequestTeardown(reason);
            }

            OnTeardownRequested?.Invoke((context, reason));
        }
    }
}
