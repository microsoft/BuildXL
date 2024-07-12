// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using BuildXL.Cache.BuildCacheResource.Model;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using ContentStoreTest.Distributed.Redis;

namespace BuildXL.Cache.ContentStore.Distributed.Test.Stores
{
    /// <summary>
    /// Helper class that can generate a <see cref="BuildCacheConfiguration"/> with proper test secrets
    /// </summary>
    public static class BuildCacheConfigurationSecretGenerator
    {
        /// <summary>
        /// Generates a <see cref="BuildCacheConfiguration"/> for testing purposes. Blob accounts and containers are also created as part of the generation process.
        /// </summary>
        public static BuildCacheConfiguration GenerateConfigurationFrom(string cacheName, AzuriteStorageProcess process, IReadOnlyList<BlobCacheStorageAccountName> accounts)
        {
            var accountClients = accounts.ToDictionary(account => account, account =>
            {
                var connectionString = process.ConnectionString.Replace("devstoreaccount1", account.AccountName);
                return new BlobServiceClient(connectionString);
            });

            var shards = accountClients.Select(kvp => GenerateShard(kvp.Value, kvp.Key.AccountName));
            return  new BuildCacheConfiguration() { Name = cacheName, RetentionPolicyInDays = 5, Shards = shards.ToList() };
        }

        private static BuildCacheShard GenerateShard(BlobServiceClient blobClient, string accountName)
        {
            Contract.Assert(blobClient.CanGenerateAccountSasUri);

            return new BuildCacheShard() { StorageUri = blobClient.Uri, Containers = new List<BuildCacheContainer> {
                GenerateContainer(blobClient, "content", BuildCacheContainerType.Content),
                GenerateContainer(blobClient, "metadata", BuildCacheContainerType.Metadata),
                GenerateContainer(blobClient, "checkpoint", BuildCacheContainerType.Checkpoint)} };
        }

        private static BuildCacheContainer GenerateContainer(BlobServiceClient client, string name, BuildCacheContainerType type)
        {
            var containerClient = client.GetBlobContainerClient(name);
            Contract.Assert(containerClient.CanGenerateSasUri);

            // In the context of using the build cache, containers are already created by the provisioning scripts.
            // There is a CreateIfNotExistsAsync API, but it doesn't work in practice against the Azure
            // Storage emulator.
            var response = containerClient.Create(PublicAccessType.None);
            Contract.Assert(!response.GetRawResponse().IsError);

            Uri sasUri = containerClient.GenerateSasUri(
                BlobContainerSasPermissions.Read | BlobContainerSasPermissions.Write | BlobContainerSasPermissions.List,
                DateTimeOffset.UtcNow.AddDays(1));

            return new BuildCacheContainer() { Name = name, SasUrl = sasUri, Type = type };
        }
    }
}
