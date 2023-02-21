// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using Azure;
using Azure.Storage.Blobs;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Blob;

#nullable enable

namespace BuildXL.Cache.ContentStore.Interfaces.Secrets
{
    /// <summary>
    /// Provides Azure Storage authentication options
    /// </summary>
    public class AzureBlobStorageCredentials
    {
        /// <summary>
        /// Credentials for local storage emulator.
        ///
        /// Used for tests only.
        /// </summary>
        public static AzureBlobStorageCredentials StorageEmulator = new AzureBlobStorageCredentials(connectionString: "UseDevelopmentStorage=true");

        private readonly Secret _secret;

        /// <summary>
        /// Creates a fixed credential; this is our default mode of authentication.
        /// </summary>
        /// <remarks>
        /// This is just a convenience method that's actually equivalent to using a <see cref="PlainTextSecret"/>
        /// </remarks>
        public AzureBlobStorageCredentials(string connectionString)
            : this(new PlainTextSecret(secret: connectionString))
        {
        }

        /// <nodoc />
        public AzureBlobStorageCredentials(Secret secret)
        {
            _secret = secret;
        }

        #region Blob V11 API

        /// <nodoc />
        public CloudStorageAccount CreateCloudStorageAccount()
        {
            if (_secret is PlainTextSecret plainText)
            {
                return CloudStorageAccount.Parse(plainText.Secret);
            }
            else if (_secret is UpdatingSasToken updatingSasToken)
            {
                return new CloudStorageAccount(
                    CreateV11StorageCredentialsFromUpdatingSasToken(updatingSasToken),
                    accountName: updatingSasToken.Token.StorageAccount,
                    endpointSuffix: null,
                    useHttps: true);
            }

            throw new NotImplementedException($"Unknown secret type `{_secret.GetType()}`");
        }

        /// <nodoc />
        public CloudBlobClient CreateCloudBlobClient()
        {
            return CreateCloudStorageAccount().CreateCloudBlobClient();
        }

        private static StorageCredentials CreateV11StorageCredentialsFromUpdatingSasToken(UpdatingSasToken updatingSasToken)
        {
            var storageCredentials = new StorageCredentials(sasToken: updatingSasToken.Token.Token);
            updatingSasToken.TokenUpdated += (_, replacementSasToken) =>
            {
                storageCredentials.UpdateSASToken(replacementSasToken.Token);
            };

            return storageCredentials;
        }

        #endregion

        #region Blob V12 API

        /// <nodoc />
        public BlobServiceClient CreateBlobServiceClient(BlobClientOptions? blobClientOptions = null)
        {
            // We default to this specific version because tests run against the Azurite emulator. The emulator doesn't
            // currently support any higher version than this, and we won't upgrade it because it's build process is
            // weird as hell and they don't just provide binaries.
            blobClientOptions ??= new BlobClientOptions(BlobClientOptions.ServiceVersion.V2021_02_12);

            if (_secret is PlainTextSecret plainText)
            {
                return new BlobServiceClient(connectionString: plainText.Secret, blobClientOptions);
            }
            else if (_secret is UpdatingSasToken sasToken)
            {
                Uri serviceUri = new Uri($"https://{sasToken.Token.StorageAccount}.blob.core.windows.net/");
                return new BlobServiceClient(
                    serviceUri,
                    CreateV12StorageCredentialsFromSasToken(sasToken),
                    blobClientOptions);
            }

            throw new NotImplementedException($"Unknown secret type `{_secret.GetType()}`");
        }

        private AzureSasCredential CreateV12StorageCredentialsFromSasToken(UpdatingSasToken updatingSasToken)
        {
            var credential = new AzureSasCredential(updatingSasToken.Token.Token);
            updatingSasToken.TokenUpdated += (_, replacementSasToken) =>
            {
                credential.Update(replacementSasToken.Token);
            };

            return credential;
        }

        #endregion
    }
}
