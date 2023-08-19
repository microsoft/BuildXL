// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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

    }
}
