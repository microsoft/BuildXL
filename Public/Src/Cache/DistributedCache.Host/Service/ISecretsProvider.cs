// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;

namespace BuildXL.Cache.Host.Service
{
    /// <summary>
    /// Contains all the secrets returned by <see cref="ISecretsProvider.RetrieveSecretsAsync"/>
    /// </summary>
    public record RetrievedSecrets(IReadOnlyDictionary<string, Secret> Secrets);

    /// <summary>
    /// Used to provide secrets
    /// </summary>
    public interface ISecretsProvider
    {
        /// <summary>
        /// Retrieves secrets from key vault or some other provider
        /// </summary>
        Task<RetrievedSecrets> RetrieveSecretsAsync(List<RetrieveSecretsRequest> requests, CancellationToken token);
    }
}
