// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.ChangeFeed;

namespace BuildXL.Cache.ContentStore.Interfaces.Auth
{
    /// <nodoc />
    public interface IAzureStorageCredentials
    {
        /// <nodoc />
        public string GetAccountName();

        /// <nodoc />
        public BlobServiceClient CreateBlobServiceClient(BlobClientOptions? blobClientOptions = null);

        /// <nodoc />
        public BlobContainerClient CreateContainerClient(string containerName, BlobClientOptions? blobClientOptions = null);

        /// <nodoc />
        public BlobChangeFeedClient CreateBlobChangeFeedClient(BlobClientOptions? blobClientOptions = null, BlobChangeFeedClientOptions? changeFeedClientOptions = null);

        /// <summary>
        /// If the implementation of <see cref="IAzureStorageCredentials"/> knows how to derive a SAS credential for a container, this method returns it.
        /// The returned credential has the same lifetime / scope as the underlying secret.
        /// If the implementation does not support this, it should throw an exception.
        /// It's expected that this method is not called unless the implementation supports it, as it is not part of the interface contract.
        /// </summary>
        public AzureSasCredential GetContainerSasCredential(string containerName);
    }
}
