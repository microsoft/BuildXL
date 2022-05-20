// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.Host.Service.Internal;
using Microsoft.WindowsAzure.Storage;
using OperationContext = BuildXL.Cache.ContentStore.Tracing.Internal.OperationContext;

namespace BuildXL.Cache.Host.Service
{
    /// <summary>
    /// Host where secrets are derived from environment variables.
    /// </summary>
    public class EnvironmentVariableHost : IDistributedCacheServiceHost
    {
        private readonly string _exposedSecretsFileName;
        private Result<RetrievedSecrets> _secrets;
        private readonly CrossProcessSecretsCommunicationKind _secretsCommunicationKind;
        private readonly Context _tracingContext;

        public CancellationTokenSource TeardownCancellationTokenSource { get; } = new CancellationTokenSource();

        public EnvironmentVariableHost(Context context, CrossProcessSecretsCommunicationKind secretsCommunicationKind = CrossProcessSecretsCommunicationKind.Environment, string exposedSecretsFileName = null)
        {
            _secretsCommunicationKind = secretsCommunicationKind;
            _tracingContext = context;
            _exposedSecretsFileName = exposedSecretsFileName;
        }

        /// <inheritdoc />
        public virtual void RequestTeardown(string reason)
        {
            TeardownCancellationTokenSource.Cancel();
        }

        private string GetSecretStoreValue(string key)
        {
            return Environment.GetEnvironmentVariable(key);
        }

        /// <inheritdoc />
        public virtual void OnStartedService()
        {
        }

        /// <inheritdoc />
        public virtual Task OnStartingServiceAsync()
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public virtual void OnTeardownCompleted()
        {
        }
        
        /// <inheritdoc />
        public Task<RetrievedSecrets> RetrieveSecretsAsync(List<RetrieveSecretsRequest> requests, CancellationToken token)
        {
            if (_secretsCommunicationKind == CrossProcessSecretsCommunicationKind.Environment)
            {
                // Default mode for the launcher
                return RetrieveSecretsCoreAsync(requests);
            }
            else if (_secretsCommunicationKind == CrossProcessSecretsCommunicationKind.EnvironmentSingleEntry)
            {
                var secretsResult = LazyInitializer.EnsureInitialized(ref _secrets, () => DeserializeFromEnvironmentVariable());

                secretsResult.ThrowIfFailure();
                return Task.FromResult(secretsResult.Value);
            }
            else if (_secretsCommunicationKind == CrossProcessSecretsCommunicationKind.MemoryMappedFile)
            {
                // 'ReadExposedSecrets' returns a disposable object, but the secrets obtained here are long-lived.
                RetrievedSecrets secrets = InterProcessSecretsCommunicator.ReadExposedSecrets(new OperationContext(_tracingContext), fileName: _exposedSecretsFileName);
                return Task.FromResult(secrets);
            }
            else
            {
                throw Contract.AssertFailure($"Unknown {nameof(CrossProcessSecretsCommunicationKind)}: {_secretsCommunicationKind}.");
            }
        }

        private static Result<RetrievedSecrets> DeserializeFromEnvironmentVariable()
        {
            var variableName = RetrievedSecretsSerializer.SerializedSecretsKeyName;
            var variable = Environment.GetEnvironmentVariable(variableName);
            if (string.IsNullOrEmpty(variable))
            {
                return Result.FromErrorMessage<RetrievedSecrets>($"Environment variable '{variableName}' is null or empty.");
            }

            return RetrievedSecretsSerializer.Deserialize(variable);
        }

        private Task<RetrievedSecrets> RetrieveSecretsCoreAsync(List<RetrieveSecretsRequest> requests)
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

            return Task.FromResult(new RetrievedSecrets(secrets));
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

            var internalSasToken = new SasToken(
                                       token: sasToken,
                                       storageAccount: cloudStorageAccount.Credentials.AccountName,
                                       resourcePath: null);
            return new UpdatingSasToken(internalSasToken);
        }
    }
}
