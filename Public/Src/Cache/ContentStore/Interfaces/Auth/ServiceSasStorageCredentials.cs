// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Text.RegularExpressions;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.ChangeFeed;

namespace BuildXL.Cache.ContentStore.Interfaces.Auth
{
    /// <summary>
    /// Provides Azure Storage authentication options based on a preauthenticated URI (URI with SAS token)
    /// </summary>
    public class ServiceSasStorageCredentials : IAzureStorageCredentials
    {
        private readonly Uri _preauthenticatedUri;

        /// <nodoc />
        public ServiceSasStorageCredentials(Uri preauthenticatedUri)
        {
            _preauthenticatedUri = preauthenticatedUri;
        }

        /// <nodoc />
        public BlobChangeFeedClient CreateBlobChangeFeedClient(BlobClientOptions? blobClientOptions = null, BlobChangeFeedClientOptions? changeFeedClientOptions = null)
        {
            blobClientOptions = BlobClientOptionsFactory.CreateOrOverride(blobClientOptions);
            changeFeedClientOptions ??= new BlobChangeFeedClientOptions();
            return new BlobChangeFeedClient(_preauthenticatedUri, blobClientOptions, changeFeedClientOptions);
        }

        /// <nodoc />
        public BlobServiceClient CreateBlobServiceClient(BlobClientOptions? blobClientOptions = null)
        {
            blobClientOptions = BlobClientOptionsFactory.CreateOrOverride(blobClientOptions);
            return new BlobServiceClient(_preauthenticatedUri, blobClientOptions);
        }

        /// <nodoc />
        public BlobContainerClient CreateContainerClient(string containerName, BlobClientOptions? blobClientOptions = null)
        {
            var serviceClient = CreateBlobServiceClient(blobClientOptions);
            return serviceClient.GetBlobContainerClient(containerName);
        }

        /// <nodoc />
        public string GetAccountName()
        {
            var serviceClient = CreateBlobServiceClient();
            return serviceClient.AccountName;
        }

        /// <nodoc />
        public AzureSasCredential GetContainerSasCredential(string containerName)
        {
            // We are authenticated at the service level, that SAS portion of the original URI can be (and is in fact) used to authenticate
            // both container and blob operations.
            var blobUriBuilder = new BlobUriBuilder(_preauthenticatedUri);
            if (blobUriBuilder.Sas == null)
            {
                throw new InvalidOperationException("The preauthenticated URI does not contain a SAS token.");
            }

            return new AzureSasCredential(blobUriBuilder.Sas.ToString());
        }
    }
}
