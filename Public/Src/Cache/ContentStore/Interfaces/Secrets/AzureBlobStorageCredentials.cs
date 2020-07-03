// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Blob;

namespace BuildXL.Cache.ContentStore.Interfaces.Secrets
{
    /// <summary>
    /// Provides Azure Storage authentication options
    /// </summary>
    public class AzureBlobStorageCredentials
    {
        /// <nodoc />
        private string? ConnectionString { get; }

        /// <summary>
        /// <see cref="StorageCredentials"/> can be updated from the outside, so it is a way to in fact change the way
        /// we authenticate with Azure Blob Storage over time. Changes are accepted only within the same authentication
        /// mode.
        /// </summary>
        private StorageCredentials? StorageCredentials { get; }

        /// <nodoc />
        private string? AccountName { get; }

        /// <nodoc />
        private string? EndpointSuffix { get; }

        /// <nodoc />
        public AzureBlobStorageCredentials(PlainTextSecret secret) : this(secret.Secret)
        {

        }

        /// <nodoc />
        public AzureBlobStorageCredentials(UpdatingSasToken sasToken) 
            : this(CreateStorageCredentialsFromSasToken(sasToken), sasToken.Token.StorageAccount!)
        {

        }

        private static StorageCredentials CreateStorageCredentialsFromSasToken(UpdatingSasToken updatingSasToken)
        {
            var storageCredentials = new StorageCredentials(sasToken: updatingSasToken.Token.Token);
            updatingSasToken.TokenUpdated += (_, replacementSasToken) =>
            {
                storageCredentials.UpdateSASToken(replacementSasToken.Token);
            };

            return storageCredentials;
        }

        /// <summary>
        /// Creates a fixed credential; this is our default mode of authentication.
        /// </summary>
        public AzureBlobStorageCredentials(string connectionString)
        {
            Contract.RequiresNotNullOrEmpty(connectionString);
            ConnectionString = connectionString;
        }

        /// <summary>
        /// Uses Azure Blob's storage credentials. This allows us to use SAS tokens, and to update shared secrets
        /// without restarting the service.
        /// </summary>
        public AzureBlobStorageCredentials(StorageCredentials storageCredentials, string accountName, string? endpointSuffix = null)
        {
            // Unfortunately, even though you can't generate a storage credentials without an account name, it isn't
            // stored inside object unless a shared secret is being used. Hence, we are forced to keep it here.
            Contract.RequiresNotNull(storageCredentials);
            Contract.RequiresNotNullOrEmpty(accountName);
            StorageCredentials = storageCredentials;
            AccountName = accountName;
            EndpointSuffix = endpointSuffix;
        }

        /// <nodoc />
        private CloudStorageAccount CreateCloudStorageAccount()
        {
            if (!string.IsNullOrEmpty(ConnectionString))
            {
                return CloudStorageAccount.Parse(ConnectionString);
            }

            if (StorageCredentials != null)
            {
                return new CloudStorageAccount(StorageCredentials, AccountName, EndpointSuffix, useHttps: true);
            }

            throw new ArgumentException("Invalid credentials");
        }

        /// <nodoc />
        public CloudBlobClient CreateCloudBlobClient()
        {
            return CreateCloudStorageAccount().CreateCloudBlobClient();
        }
    }
}
