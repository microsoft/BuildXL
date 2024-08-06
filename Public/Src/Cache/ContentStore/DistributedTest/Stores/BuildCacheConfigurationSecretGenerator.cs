// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using BuildXL.Cache.BuildCacheResource.Model;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.ContentStore.UtilitiesCore;
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
            var shards = accounts.Select(account =>
            {
                var connectionString = process.ConnectionString.Replace("devstoreaccount1", account.AccountName);
                var serviceClient = new BlobServiceClient(connectionString);
                var shard = GenerateShard(serviceClient);
                return shard;
            }).ToList();

            return new BuildCacheConfiguration() { Name = cacheName, RetentionDays = null, Shards = shards.ToList() };
        }

        private static BuildCacheShard GenerateShard(BlobServiceClient serviceClient)
        {
            Contract.Assert(serviceClient.CanGenerateAccountSasUri);

            // We use randomized container names to ensure nothing's hard-coded
            var contentName = ThreadSafeRandom.LowercaseAlphanumeric(10);
            var metadataName = ThreadSafeRandom.LowercaseAlphanumeric(10);
            var checkpointName = ThreadSafeRandom.LowercaseAlphanumeric(10);
            Contract.Assert(contentName != metadataName && contentName != checkpointName && metadataName != checkpointName);

            return new BuildCacheShard()
            {
                StorageUrl = serviceClient.Uri,
                Containers = new List<BuildCacheContainer> {
                GenerateContainer(serviceClient, contentName, BuildCacheContainerType.Content),
                GenerateContainer(serviceClient, metadataName, BuildCacheContainerType.Metadata),
                GenerateContainer(serviceClient, checkpointName, BuildCacheContainerType.Checkpoint)}
            };
        }

        private static BuildCacheContainer GenerateContainer(BlobServiceClient serviceClient, string containerName, BuildCacheContainerType type)
        {
            var containerClient = serviceClient.GetBlobContainerClient(containerName);
            Contract.Assert(containerClient.CanGenerateSasUri);

            // In the context of using the build cache, containers are already created by the provisioning scripts.
            // There is a CreateIfNotExistsAsync API, but it doesn't work in practice against the Azure
            // Storage emulator.
            var response = containerClient.CreateIfNotExists(PublicAccessType.None);
            Contract.Assert(!response.GetRawResponse().IsError);

            Uri sasUri = containerClient.GenerateSasUri(
                BlobContainerSasPermissions.Read | BlobContainerSasPermissions.Write | BlobContainerSasPermissions.List,
                DateTimeOffset.UtcNow.AddDays(1));

            return new BuildCacheContainer() { Name = containerName, Signature = sasUri.Query, Type = type };
        }
    }
}
