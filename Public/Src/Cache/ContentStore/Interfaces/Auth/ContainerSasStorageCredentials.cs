// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.ChangeFeed;

namespace BuildXL.Cache.ContentStore.Interfaces.Auth
{
    /// <summary>
    /// Provides Azure Storage authentication options based on a preauthenticated URI (URI with SAS token)
    /// </summary>
    public class ContainerSasStorageCredentials : IAzureStorageCredentials
    {
        private readonly Uri _accountUri;
        private readonly AzureSasCredential _azureSasCredential;

        /// <summary>
        /// The name of the container reference by the credentials.
        /// </summary>
        public string ContainerName { get; }

        /// <nodoc />
        public ContainerSasStorageCredentials(Uri accountUri, string containerName, AzureSasCredential azureSasCredential)
        {
            _accountUri = accountUri;
            ContainerName = containerName;
            _azureSasCredential = azureSasCredential;
        }

        /// <nodoc />
        public BlobChangeFeedClient CreateBlobChangeFeedClient(BlobClientOptions? blobClientOptions = null, BlobChangeFeedClientOptions? changeFeedClientOptions = null)
        {
            throw new NotImplementedException("This operation is unsupported when using container-level credentials.");
        }

        /// <nodoc />
        public BlobServiceClient CreateBlobServiceClient(BlobClientOptions? blobClientOptions = null)
        {
            throw new NotImplementedException("This operation is unsupported when using container-level credentials.");
        }

        /// <nodoc />
        public BlobContainerClient CreateContainerClient(string containerName, BlobClientOptions? blobClientOptions = null)
        {
            if (containerName != ContainerName)
            {
                throw new ArgumentException($"The provided container name ({containerName}) does not match the container name for which the credentials were created ({ContainerName}).");
            }

            blobClientOptions = BlobClientOptionsFactory.CreateOrOverride(blobClientOptions);

            Uri blobContainerUri;
            if (_accountUri.LocalPath == "/")
            {
                // This happens in the normal use-case, where the storage URI is something like
                // https://account.blob.core.windows.net/, we just append the container name at the end.
                blobContainerUri = new Uri(_accountUri, $"{containerName}");
            }
            else
            {
                // This happens when using the Azure Storage Emulator, where the storage URI looks like
                // http://localhost:2134/accountName, we need to append the container name after the account name.
                blobContainerUri = new Uri(_accountUri, $"{_accountUri.LocalPath}/{containerName}");
            }

            return new BlobContainerClient(blobContainerUri, _azureSasCredential, blobClientOptions);
        }

        /// <nodoc />
        public string GetAccountName()
        {
            // We're doing this because we want to rely on Storage's SDK logic for parsing out the storage account name.
            var serviceClient = CreateContainerClient("dummy");
            return serviceClient.AccountName;
        }
    }
}
