// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;

#nullable enable

namespace BuildXL.Cache.Host.Service.Internal
{
    /// <summary>
    /// Retrieves secrets used for the distributed cache.
    /// </summary>
    public class DistributedCacheSecretRetriever
    {
        private readonly DistributedContentSettings _distributedSettings;
        private readonly AzureBlobStorageLogPublicConfiguration? _loggingConfiguration;

        private readonly ILogger _logger;
        private readonly IDistributedCacheServiceHost _host;

        private readonly Lazy<Task<Result<RetrievedSecrets>>> _secrets;

        /// <nodoc />
        public DistributedCacheSecretRetriever(DistributedCacheServiceArguments arguments)
        {
            _distributedSettings = arguments.Configuration.DistributedContentSettings;
            _loggingConfiguration = arguments.LoggingSettings?.Configuration;
            _logger = arguments.Logger;
            _host = arguments.Host;

            _secrets = new Lazy<Task<Result<RetrievedSecrets>>>(TryGetSecretsAsync);
        }

        /// <summary>
        /// Retrieves the secrets. Results will be cached so secrets are only computed the first time this is called.
        /// </summary>
        public Task<Result<RetrievedSecrets>> TryRetrieveSecretsAsync() => _secrets.Value;

        private async Task<Result<RetrievedSecrets>> TryGetSecretsAsync()
        {
            var errorBuilder = new StringBuilder();

            var result = await impl();

            if (result is null)
            {
                return Result.FromErrorMessage<RetrievedSecrets>(errorBuilder.ToString());
            }

            return Result.Success(result);

            async Task<RetrievedSecrets?> impl()
            {
                _logger.Debug(
                    $"{nameof(_distributedSettings.EventHubSecretName)}: {_distributedSettings.EventHubSecretName}, " +
                    $"{nameof(_distributedSettings.AzureStorageSecretName)}: {_distributedSettings.AzureStorageSecretName}, " +
                    $"{nameof(_distributedSettings.GlobalCacheWriteAheadBlobSecretName)}: {_distributedSettings.GlobalCacheWriteAheadBlobSecretName}, " +
                    $"{nameof(_distributedSettings.ContentMetadataBlobSecretName)}: {_distributedSettings.ContentMetadataBlobSecretName}");

                // Create the credentials requests
                var retrieveSecretsRequests = new List<RetrieveSecretsRequest>();

                var storageSecretNames = GetAzureStorageSecretNames(errorBuilder);
                if (storageSecretNames == null)
                {
                    return null;
                }

                retrieveSecretsRequests.AddRange(
                    storageSecretNames
                        .Select(tpl => new RetrieveSecretsRequest(tpl.secretName, tpl.useSasTokens ? SecretKind.SasToken : SecretKind.PlainText)));

                void addOptionalSecret(string? name, SecretKind secretKind = SecretKind.PlainText)
                {
                    if (!string.IsNullOrEmpty(name))
                    {
                        retrieveSecretsRequests.Add(new RetrieveSecretsRequest(name, secretKind));
                    }
                }

                addOptionalSecret(_distributedSettings.EventHubSecretName);

                var azureBlobStorageCredentialsKind = _distributedSettings.AzureBlobStorageUseSasTokens ? SecretKind.SasToken : SecretKind.PlainText;
                addOptionalSecret(_distributedSettings.ContentMetadataBlobSecretName, azureBlobStorageCredentialsKind);
                addOptionalSecret(_distributedSettings.GlobalCacheWriteAheadBlobSecretName, azureBlobStorageCredentialsKind);

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
                    if (secrets.Secrets.TryGetValue(request.Name, out var secret))
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
                            throw new SecurityException($"The credentials produced by the host for secret named {request.Name} do not match the expected kind. Requested: '{request.Kind}'. Actual: '{secret.GetType()}'.");
                        }
                    }
                }

                return secrets;
            }
        }

        private static IRetryPolicy CreateSecretsRetrievalRetryPolicy(DistributedContentSettings settings)
        {
            return RetryPolicyFactory.GetExponentialPolicy(
                IsTransient,
                retryCount: settings.SecretsRetrievalRetryCount,
                minBackoff: TimeSpan.FromSeconds(settings.SecretsRetrievalMinBackoffSeconds),
                maxBackoff: TimeSpan.FromSeconds(settings.SecretsRetrievalMaxBackoffSeconds),
                deltaBackoff: TimeSpan.FromSeconds(settings.SecretsRetrievalDeltaBackoffSeconds));
        }

        private List<(string secretName, bool useSasTokens)>? GetAzureStorageSecretNames(StringBuilder errorBuilder)
        {
            bool useSasToken = _distributedSettings.AzureBlobStorageUseSasTokens;
            var secretNames = new List<(string secretName, bool useSasTokens)>();
            if (_distributedSettings.AzureStorageSecretName != null && !string.IsNullOrEmpty(_distributedSettings.AzureStorageSecretName))
            {
                secretNames.Add((_distributedSettings.AzureStorageSecretName, useSasToken));
            }

            if (_distributedSettings.AzureStorageSecretNames != null && !_distributedSettings.AzureStorageSecretNames.Any(string.IsNullOrEmpty))
            {
                secretNames.AddRange(_distributedSettings.AzureStorageSecretNames.Select(n => (n, useSasToken)));
            }

            if (_loggingConfiguration?.SecretName != null)
            {
                secretNames.Add((_loggingConfiguration.SecretName, _loggingConfiguration.UseSasTokens));
            }

            if (secretNames.Count > 0)
            {
                return secretNames;
            }

            errorBuilder.Append(
                $"Unable to configure Azure Storage. {nameof(DistributedContentSettings.AzureStorageSecretName)} or {nameof(DistributedContentSettings.AzureStorageSecretNames)} configuration options should be provided. ");
            return null;
        }

        private static bool IsTransient(Exception ex)
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
