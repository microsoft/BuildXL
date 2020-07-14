// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;

namespace BuildXL.Cache.Host.Service
{
    /// <summary>
    /// Used to provide secrets
    /// </summary>
    public interface ISecretsProvider
    {
        /// <summary>
        /// Retrieves secrets from key vault
        /// </summary>
        Task<Dictionary<string, Secret>> RetrieveSecretsAsync(List<RetrieveSecretsRequest> requests, CancellationToken token);
    }
}
