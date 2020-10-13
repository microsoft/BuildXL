// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.Host.Service
{
    /// <summary>
    /// Host used for providing callbacks and external functionality to a distributed cache service
    /// </summary>
    public interface IDistributedCacheServiceHost : ISecretsProvider
    {
        /// <summary>
        /// Notifies host immediately before host is started and returns a task that completes when the service is ready to start
        /// (for instance, the current service may wait for another service instance to stop).
        /// </summary>
        Task OnStartingServiceAsync();

        /// <summary>
        /// Notifies host when service teardown (shutdown) is complete
        /// </summary>
        void OnTeardownCompleted();

        /// <summary>
        /// Notifies the host when the service is successfully started
        /// </summary>
        void OnStartedService();

        /// <summary>
        /// Request a graceful shutdown of a current service instance.
        /// </summary>
        void RequestTeardown(string reason);
    }

    public interface IDistributedCacheServiceHostInternal : IDistributedCacheServiceHost
    {
        /// <summary>
        /// Notifies host immediately before host is started and returns a task that completes when the service is ready to start
        /// (for instance, the current service may wait for another service instance to stop).
        /// </summary>
        Task OnStartingServiceAsync(OperationContext context);

        /// <summary>
        /// Notifies host immediately before host is started and returns a task that completes when the service is ready to start
        /// (for instance, the current service may wait for another service instance to stop).
        /// </summary>
        Task OnStoppingServiceAsync(OperationContext context);
    }
}
