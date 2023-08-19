// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Text.RegularExpressions;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.ChangeFeed;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Blob;

#nullable enable

namespace BuildXL.Cache.ContentStore.Interfaces.Auth
{
    /// <summary>
    /// Provides Azure Storage authentication options based on a <see cref="Secret"/> (Connection string or SAS Token)
    /// </summary>
    public class SecretBasedAzureStorageCredentials : IAzureStorageCredentials
    {
        /// <summary>
        /// Credentials for local storage emulator.
        ///
        /// Used for tests only.
        /// </summary>
        public static SecretBasedAzureStorageCredentials StorageEmulator = new SecretBasedAzureStorageCredentials(connectionString: "UseDevelopmentStorage=true");

        private readonly Secret _secret;

        /// <summary>
        /// Creates a fixed credential; this is our default mode of authentication.
        /// </summary>
        /// <remarks>
        /// This is just a convenience method that's actually equivalent to using a <see cref="PlainTextSecret"/>
        /// </remarks>
        public SecretBasedAzureStorageCredentials(string connectionString)
            : this(new PlainTextSecret(secret: connectionString))
        {
        }

        /// <nodoc />
        public SecretBasedAzureStorageCredentials(Secret secret)
        {
            _secret = secret;
        }

        private static readonly Regex s_storageAccountNameRegex = new Regex(".*;AccountName=(?<accountName>[^;]+);.*", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex s_storageUrlRegex = new Regex("https?://(?<accountName>.+)\\.blob\\..*", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <nodoc />
        public string GetAccountName()
        {
            switch (_secret)
            {
                case PlainTextSecret plainTextSecret:
                    var match = s_storageAccountNameRegex.Match(plainTextSecret.Secret);
                    if (match.Success)
                    {
                        return match.Groups["accountName"].Value;
                    }

                    match = s_storageUrlRegex.Match(plainTextSecret.Secret);
                    if (match.Success)
                    {
                        return match.Groups["accountName"].Value;
                    }

                    throw new InvalidOperationException($"The provided secret is malformed and the account name could not be retrieved.");
                case UpdatingSasToken updatingSasToken:
                    return updatingSasToken.Token.StorageAccount;
                default:
                    throw new NotImplementedException($"Unknown secret type `{_secret.GetType()}`");
            }
        }

        #region Blob V11 API

        /// <nodoc />
        public CloudStorageAccount CreateCloudStorageAccount()
        {
            return _secret switch
            {
                PlainTextSecret plainText => CloudStorageAccount.Parse(plainText.Secret),
                UpdatingSasToken updatingSasToken => new CloudStorageAccount(
                    CreateV11StorageCredentialsFromUpdatingSasToken(updatingSasToken),
                    accountName: updatingSasToken.Token.StorageAccount,
                    endpointSuffix: null,
                    useHttps: true),
                _ => throw new NotImplementedException($"Unknown secret type `{_secret.GetType()}`")
            };
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
            // currently support any higher version than this, and we won't upgrade it because its build process is
            // weird as hell and they don't just provide binaries.
            blobClientOptions ??= new BlobClientOptions(BlobClientOptions.ServiceVersion.V2021_02_12);

            return _secret switch
            {
                PlainTextSecret plainText => new BlobServiceClient(connectionString: plainText.Secret, blobClientOptions),
                UpdatingSasToken sasToken => new BlobServiceClient(
                    serviceUri: new Uri($"https://{sasToken.Token.StorageAccount}.blob.core.windows.net/"),
                    credential: CreateV12StorageCredentialsFromSasToken(sasToken),
                    blobClientOptions),
                _ => throw new NotImplementedException($"Unknown secret type `{_secret.GetType()}`")
            };
        }

        /// <nodoc />
        public BlobChangeFeedClient CreateBlobChangeFeedClient(BlobClientOptions? blobClientOptions = null, BlobChangeFeedClientOptions? changeFeedClientOptions = null)
        {
            // We default to this specific version because tests run against the Azurite emulator. The emulator doesn't
            // currently support any higher version than this, and we won't upgrade it because it's build process is
            // weird as hell and they don't just provide binaries.
            blobClientOptions ??= new BlobClientOptions(BlobClientOptions.ServiceVersion.V2021_02_12);

            changeFeedClientOptions ??= new BlobChangeFeedClientOptions();

            return _secret switch
            {
                PlainTextSecret plainText => new BlobChangeFeedClient(connectionString: plainText.Secret, blobClientOptions, changeFeedClientOptions),
                UpdatingSasToken sasToken => new BlobChangeFeedClient(
                    serviceUri: new Uri($"https://{sasToken.Token.StorageAccount}.blob.core.windows.net/"),
                    credential: CreateV12StorageCredentialsFromSasToken(sasToken),
                    blobClientOptions,
                    changeFeedClientOptions),
                _ => throw new NotImplementedException($"Unknown secret type `{_secret.GetType()}`")
            };
        }

        /// <nodoc />
        public BlobContainerClient CreateContainerClient(string containerName, BlobClientOptions? blobClientOptions = null)
        {
            // We default to this specific version because tests run against the Azurite emulator. The emulator doesn't
            // currently support any higher version than this, and we won't upgrade it because it's build process is
            // weird as hell and they don't just provide binaries.
            blobClientOptions ??= new BlobClientOptions(BlobClientOptions.ServiceVersion.V2021_02_12);

            return _secret switch
            {
                PlainTextSecret plainText => new BlobContainerClient(connectionString: plainText.Secret, containerName, blobClientOptions),
                UpdatingSasToken sasToken => new BlobContainerClient(
                    blobContainerUri: new Uri($"https://{sasToken.Token.StorageAccount}.blob.core.windows.net/{containerName}"),
                    CreateV12StorageCredentialsFromSasToken(sasToken),
                    blobClientOptions),
                _ => throw new NotImplementedException($"Unknown secret type `{_secret.GetType()}`")
            };
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
