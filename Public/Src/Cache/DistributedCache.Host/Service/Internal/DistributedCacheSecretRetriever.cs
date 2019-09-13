// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.Host.Configuration;
using Microsoft.Practices.TransientFaultHandling;

namespace BuildXL.Cache.Host.Service.Internal
{
    /// <summary>
    /// Retrieves secrets used for the distributed cache.
    /// </summary>
    public class DistributedCacheSecretRetriever
    {
        private DistributedContentSettings _distributedSettings;
        private ILogger _logger;
        private IDistributedCacheServiceHost _host;

        private Lazy<Task<(Dictionary<string, Secret>, string)>> _secrets;

        /// <nodoc />
        public DistributedCacheSecretRetriever(DistributedCacheServiceArguments arguments)
        {
            _distributedSettings = arguments.Configuration.DistributedContentSettings;
            _logger = arguments.Logger;
            _host = arguments.Host;

            _secrets = new Lazy<Task<(Dictionary<string, Secret>, string)>>(TryGetSecretsAsync);
        }

        /// <summary>
        /// Retrieves the secrets. Results will be cached so secrets are only computed the first time this is called.
        /// </summary>
        public Task<(Dictionary<string, Secret>, string)> TryRetrieveSecretsAsync() => _secrets.Value;

        private async Task<(Dictionary<string, Secret>, string errors)> TryGetSecretsAsync()
        {
            var errorBuilder = new StringBuilder();

            var result = await Impl();

            return (result, errorBuilder.ToString());

            async Task<Dictionary<string, Secret>> Impl()
            {
                _logger.Debug(
                    $"{nameof(_distributedSettings.EventHubSecretName)}: {_distributedSettings.EventHubSecretName}, " +
                    $"{nameof(_distributedSettings.AzureStorageSecretName)}: {_distributedSettings.AzureStorageSecretName}, " +
                    $"{nameof(_distributedSettings.GlobalRedisSecretName)}: {_distributedSettings.GlobalRedisSecretName}, " +
                    $"{nameof(_distributedSettings.SecondaryGlobalRedisSecretName)}: {_distributedSettings.SecondaryGlobalRedisSecretName}.");

                bool invalidConfiguration = AppendIfNull(_distributedSettings.EventHubSecretName, $"{nameof(DistributedContentSettings)}.{nameof(DistributedContentSettings.EventHubSecretName)}");
                invalidConfiguration |= AppendIfNull(_distributedSettings.GlobalRedisSecretName, $"{nameof(DistributedContentSettings)}.{nameof(DistributedContentSettings.GlobalRedisSecretName)}");

                if (invalidConfiguration)
                {
                    return null;
                }

                // Create the credentials requests
                var retrieveSecretsRequests = new List<RetrieveSecretsRequest>();

                var storageSecretNames = GetAzureStorageSecretNames(errorBuilder);
                if (storageSecretNames == null)
                {
                    return null;
                }

                var azureBlobStorageCredentialsKind = _distributedSettings.AzureBlobStorageUseSasTokens ? SecretKind.SasToken : SecretKind.PlainText;
                retrieveSecretsRequests.AddRange(storageSecretNames.Select(secretName => new RetrieveSecretsRequest(secretName, azureBlobStorageCredentialsKind)));

                if (string.IsNullOrEmpty(_distributedSettings.EventHubSecretName) ||
                    string.IsNullOrEmpty(_distributedSettings.GlobalRedisSecretName))
                {
                    return null;
                }

                retrieveSecretsRequests.Add(new RetrieveSecretsRequest(_distributedSettings.EventHubSecretName, SecretKind.PlainText));

                retrieveSecretsRequests.Add(new RetrieveSecretsRequest(_distributedSettings.GlobalRedisSecretName, SecretKind.PlainText));
                if (!string.IsNullOrEmpty(_distributedSettings.SecondaryGlobalRedisSecretName))
                {
                    retrieveSecretsRequests.Add(new RetrieveSecretsRequest(_distributedSettings.SecondaryGlobalRedisSecretName, SecretKind.PlainText));
                }

                // Ask the host for credentials
                var retryPolicy = CreateSecretsRetrievalRetryPolicy(_distributedSettings);
                var secrets = await retryPolicy.ExecuteAsync(
                    async () => await _host.RetrieveSecretsAsync(retrieveSecretsRequests, CancellationToken.None),
                    CancellationToken.None);
                if (secrets == null)
                {
                    return null;
                }

                // Validate requests match as expected
                foreach (var request in retrieveSecretsRequests)
                {
                    if (secrets.TryGetValue(request.Name, out var secret))
                    {
                        bool typeMatch = true;
                        switch (request.Kind)
                        {
                            case SecretKind.PlainText:
                                typeMatch = secret is PlainTextSecret;
                                break;
                            case SecretKind.SasToken:
                                typeMatch = secret is UpdatingSasToken;
                                break;
                            default:
                                throw new NotSupportedException("The requested kind is missing support for secret request matching");
                        }

                        if (!typeMatch)
                        {
                            throw new SecurityException($"The credentials produced by the host for secret named {request.Name} do not match the expected kind");
                        }
                    }
                }

                return secrets;
            }

            bool AppendIfNull(object value, string propertyName)
            {
                if (value is null)
                {
                    errorBuilder.Append($"{propertyName} should be provided. ");
                    return true;
                }

                return false;
            }
        }

        private static RetryPolicy CreateSecretsRetrievalRetryPolicy(DistributedContentSettings settings)
        {
            return new RetryPolicy(
                new KeyVaultRetryPolicy(),
                new ExponentialBackoff(
                    name: "SecretsRetrievalBackoff",
                    retryCount: settings.SecretsRetrievalRetryCount,
                    minBackoff: TimeSpan.FromSeconds(settings.SecretsRetrievalMinBackoffSeconds),
                    maxBackoff: TimeSpan.FromSeconds(settings.SecretsRetrievalMaxBackoffSeconds),
                    deltaBackoff: TimeSpan.FromSeconds(settings.SecretsRetrievalDeltaBackoffSeconds),
                    firstFastRetry: false)); // All retries are subjects to the policy, even the first one
        }

        private List<string> GetAzureStorageSecretNames(StringBuilder errorBuilder)
        {
            var secretNames = new List<string>();
            if (_distributedSettings.AzureStorageSecretName != null && !string.IsNullOrEmpty(_distributedSettings.AzureStorageSecretName))
            {
                secretNames.Add(_distributedSettings.AzureStorageSecretName);
            }

            if (_distributedSettings.AzureStorageSecretNames != null && !_distributedSettings.AzureStorageSecretNames.Any(string.IsNullOrEmpty))
            {
                secretNames.AddRange(_distributedSettings.AzureStorageSecretNames);
            }

            if (secretNames.Count > 0)
            {
                return secretNames;
            }

            errorBuilder.Append(
                $"Unable to configure Azure Storage. {nameof(DistributedContentSettings.AzureStorageSecretName)} or {nameof(DistributedContentSettings.AzureStorageSecretNames)} configuration options should be provided. ");
            return null;
        }

        private sealed class KeyVaultRetryPolicy : ITransientErrorDetectionStrategy
        {
            /// <inheritdoc />
            public bool IsTransient(Exception ex)
            {
                var message = ex.Message;

                if (message.Contains("The remote name could not be resolved"))
                {
                    // In some cases, KeyVault provider may fail with HttpRequestException with an inner exception like 'The remote name could not be resolved: 'login.windows.net'.
                    // Theoretically, this should be handled by the host, but to make error handling simple and consistent (this method throws one exception type) the handling is happening here.
                    // This is a recoverable error.
                    return true;
                }

                if (message.Contains("429"))
                {
                    // This is a throttling response which is recoverable as well.
                    return true;
                }

                return false;
            }
        }
    }
}
