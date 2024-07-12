// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.ChangeFeed;

namespace BuildXL.Cache.ContentStore.Interfaces.Auth
{
    /// <summary>
    /// Provides Azure Storage authentication options based on a preauthenticated URI (URI with SAS token)
    /// </summary>
    public class ContainerSasStorageCredentials : IAzureStorageCredentials
    {
        private readonly Uri _preauthenticatedUri;

        /// <nodoc />
        public ContainerSasStorageCredentials(Uri preauthenticatedUri)
        {
            _preauthenticatedUri = preauthenticatedUri;
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
            blobClientOptions = BlobClientOptionsFactory.CreateOrOverride(blobClientOptions);
            return new BlobContainerClient(_preauthenticatedUri, blobClientOptions);
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
