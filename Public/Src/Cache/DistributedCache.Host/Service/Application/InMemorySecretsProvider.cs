// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

#nullable enable

namespace BuildXL.Cache.Host.Service
{
    /// <summary>
    /// The <see cref="ISecretsProvider"/> implementation that reads secrets from dictionary in memory.
    /// </summary>
    public sealed class InMemorySecretsProvider : InMemorySecretsProviderBase, ISecretsProvider
    {
        private readonly Dictionary<string, string> _secrets;

        /// <nodoc />
        public InMemorySecretsProvider(IReadOnlyDictionary<string, string> secrets)
        {
            _secrets = secrets.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        /// <inheritdoc />
        protected override string? GetSecretStoreValue(string key)
        {
            if (_secrets.TryGetValue(key, out var value))
            {
                return value;
            }

            return null;
        }

        /// <inheritdoc />
        public Task<RetrievedSecrets> RetrieveSecretsAsync(List<RetrieveSecretsRequest> requests, CancellationToken token) =>
            RetrieveSecretsCoreAsync(requests);
    }
}
