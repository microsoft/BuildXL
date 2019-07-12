// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed;

namespace BuildXL.Cache.Host.Service
{
    /// <summary>
    /// Type to signal the host which kind of secret is expected to be returned
    /// </summary>
    public struct RetrieveSecretsRequest
    {
        public string Name { get; }

        public CredentialsKind Kind { get; }

        public RetrieveSecretsRequest(string name, CredentialsKind kind)
        {
            Contract.Requires(!string.IsNullOrEmpty(name));
            Name = name;
            Kind = kind;
        }
    }

    /// <summary>
    /// Host used for providing callbacks and external functionality to a distributed cache service
    /// </summary>
    public interface IDistributedCacheServiceHost
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
        /// Notifies the host when the service is sucessfully started
        /// </summary>
        void OnStartedService();

        /// <summary>
        /// Gets a value from the hosting environment's secret store
        /// </summary>
        string GetSecretStoreValue(string key);

        /// <summary>
        /// Retrieves secrets from key vault
        /// </summary>
        Task<Dictionary<string, Credentials>> RetrieveKeyVaultSecretsAsync(List<RetrieveSecretsRequest> requests, CancellationToken token);
    }
}
