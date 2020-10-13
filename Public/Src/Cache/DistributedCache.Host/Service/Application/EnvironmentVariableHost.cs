// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.Host.Service;
using Microsoft.WindowsAzure.Storage;

// ReSharper disable once UnusedMember.Global
namespace BuildXL.Cache.Host.Service
{
    /// <summary>
    /// Host where secrets are derived from environment variables
    /// </summary>
    public class EnvironmentVariableHost : IDistributedCacheServiceHost
    {
        public CancellationTokenSource TeardownCancellationTokenSource { get; } = new CancellationTokenSource();

        public virtual void RequestTeardown(string reason)
        {
            TeardownCancellationTokenSource.Cancel();
        }

        public string GetSecretStoreValue(string key)
        {
            return Environment.GetEnvironmentVariable(key);
        }

        public virtual void OnStartedService()
        {
        }

        public virtual Task OnStartingServiceAsync()
        {
            return Task.CompletedTask;
        }

        public virtual void OnTeardownCompleted()
        {
        }

        public Task<Dictionary<string, Secret>> RetrieveSecretsAsync(List<RetrieveSecretsRequest> requests, CancellationToken token)
        {
            var secrets = new Dictionary<string, Secret>();

            foreach (var request in requests)
            {
                Secret secret = null;

                var secretValue = GetSecretStoreValue(request.Name);
                if (string.IsNullOrEmpty(secretValue))
                {
                    // Environment variables are null by default. Skip if that's the case
                    continue;
                }

                switch (request.Kind)
                {
                    case SecretKind.PlainText:
                        // In this case, the value is expected to be an entire connection string
                        secret = new PlainTextSecret(secretValue);
                        break;
                    case SecretKind.SasToken:
                        secret = CreateSasTokenSecret(request, secretValue);
                        break;
                    default:
                        throw new NotSupportedException($"It is expected that all supported credential kinds be handled when creating a DistributedService. {request.Kind} is unhandled.");
                }

                Contract.Requires(secret != null);
                secrets[request.Name] = secret;
            }

            return Task.FromResult(secrets);
        }

        private Secret CreateSasTokenSecret(RetrieveSecretsRequest request, string secretValue)
        {
            var resourceTypeVariableName = $"{request.Name}_ResourceType";
            var resourceType = GetSecretStoreValue(resourceTypeVariableName);
            if (string.IsNullOrEmpty(resourceType))
            {
                throw new ArgumentNullException($"Missing environment variable {resourceTypeVariableName} that stores the resource type for secret {request.Name}");
            }

            switch (resourceType.ToLowerInvariant())
            {
                case "storagekey":
                    return CreateAzureStorageSasTokenSecret(secretValue);
                default:
                    throw new NotSupportedException($"Unknown resource type {resourceType} for secret named {request.Name}. Check environment variable {resourceTypeVariableName} has a valid value.");
            }
        }

        internal static Secret CreateAzureStorageSasTokenSecret(string secretValue)
        {
            // In this case, the environment variable is expected to hold an Azure Storage connection string
            var cloudStorageAccount = CloudStorageAccount.Parse(secretValue);

            // Create a godlike SAS token for the account, so that we don't need to reimplement the Central Secrets Service.
            var sasToken = cloudStorageAccount.GetSharedAccessSignature(new SharedAccessAccountPolicy
            {
                SharedAccessExpiryTime = null,
                Permissions = SharedAccessAccountPermissions.Add | SharedAccessAccountPermissions.Create | SharedAccessAccountPermissions.Delete | SharedAccessAccountPermissions.List | SharedAccessAccountPermissions.ProcessMessages | SharedAccessAccountPermissions.Read | SharedAccessAccountPermissions.Update | SharedAccessAccountPermissions.Write,
                Services = SharedAccessAccountServices.Blob,
                ResourceTypes = SharedAccessAccountResourceTypes.Object | SharedAccessAccountResourceTypes.Container | SharedAccessAccountResourceTypes.Service,
                Protocols = SharedAccessProtocol.HttpsOnly,
                IPAddressOrRange = null,
            });

            var internalSasToken = new SasToken()
            {
                Token = sasToken,
                StorageAccount = cloudStorageAccount.Credentials.AccountName,
            };
            return new UpdatingSasToken(internalSasToken);
        }
    }
}
