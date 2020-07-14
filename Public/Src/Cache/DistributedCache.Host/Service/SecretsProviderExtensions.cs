// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;

namespace BuildXL.Cache.Host.Service
{
    /// <summary>
    /// Extensions for retrieving secrets
    /// </summary>
    public static class SecretsProviderExtensions
    {
        /// <summary>
        /// Gets blob credentials from the given secrets provider
        /// </summary>
        public static async Task<AzureBlobStorageCredentials> GetBlobCredentialsAsync(
            this ISecretsProvider secretsProvider,
            string secretName,
            bool useSasTokens,
            CancellationToken token)
        {
            if (useSasTokens)
            {
                var secrets = await secretsProvider.RetrieveSecretsAsync(new List<RetrieveSecretsRequest>()
                {
                    new RetrieveSecretsRequest(secretName, SecretKind.SasToken)
                }, token);

                return new AzureBlobStorageCredentials((UpdatingSasToken)secrets[secretName]);
            }
            else
            {
                var secrets = await secretsProvider.RetrieveSecretsAsync(new List<RetrieveSecretsRequest>()
                {
                    new RetrieveSecretsRequest(secretName, SecretKind.PlainText)
                }, token);

                return new AzureBlobStorageCredentials((PlainTextSecret)secrets[secretName]);
            }
        }
    }
}
