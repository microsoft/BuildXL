// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Text.RegularExpressions;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.ChangeFeed;

namespace BuildXL.Cache.ContentStore.Interfaces.Auth
{
    /// <nodoc />
    public class ManagedIdentityAzureStorageCredentials : IAzureStorageCredentials
    {
        private static readonly Regex StorageAccountNameRegex = new("https://(?<accountName>[^\\.]+)\\.blob\\..*", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly ManagedIdentityCredential _credentials;
        private readonly Uri _blobUri;

        /// <nodoc />
        public ManagedIdentityAzureStorageCredentials(string managedIdentityClientId, Uri blobUri)
        {
            _credentials = new ManagedIdentityCredential(managedIdentityClientId);
            _blobUri = blobUri;
        }

        /// <inheritdoc />
        public string GetAccountName()
        {
            var match = StorageAccountNameRegex.Match(_blobUri.ToString());

            if (match.Success)
            {
                return match.Groups["accountName"].Value;
            }

            throw new InvalidOperationException($"The provided URI is malformed and the account name could not be retrieved.");
        }

        /// <inheritdoc />
        public BlobServiceClient CreateBlobServiceClient(BlobClientOptions? blobClientOptions = null)
            => new(_blobUri, _credentials, blobClientOptions ?? new(BlobClientOptions.ServiceVersion.V2021_02_12));

        /// <inheritdoc />
        public BlobContainerClient CreateContainerClient(string containerName, BlobClientOptions? blobClientOptions = null)
            => new(new Uri(_blobUri, containerName), _credentials, blobClientOptions ?? new(BlobClientOptions.ServiceVersion.V2021_02_12));

        /// <inheritdoc />
        public BlobChangeFeedClient CreateBlobChangeFeedClient(BlobClientOptions? blobClientOptions = null, BlobChangeFeedClientOptions? changeFeedClientOptions = null)
            => new BlobChangeFeedClient(_blobUri, _credentials, blobClientOptions ?? new(BlobClientOptions.ServiceVersion.V2021_02_12), changeFeedClientOptions ?? new());
    }
}
