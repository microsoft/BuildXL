// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Text.RegularExpressions;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Sessions;
using BuildXL.Cache.MemoizationStore.Stores;

namespace BuildXL.Cache.MemoizationStore.Distributed.Stores
{
    /// <nodoc />
    public static class AzureBlobStorageCacheFactory
    {
        /// <nodoc />
        public record Configuration(
            ShardingScheme ShardingScheme,
            string Universe,
            string Namespace)
        {
            /// <summary>
            /// Time we're willing to wait until a client to the backing storage account is created. This encompasses
            /// both getting the secret and creating the storage client.
            /// </summary>
            public TimeSpan ClientCreationTimeout { get; init; } = TimeSpan.MaxValue;

            /// <summary>
            /// Maximum amount of time we're willing to wait for any operation against storage.
            /// </summary>
            public TimeSpan StorageInteractionTimeout { get; init; } = TimeSpan.FromMinutes(30);

            /// <summary>
            /// Amount of time that content is guaranteed to exist after a fingerprint that points to that piece of
            /// content has been obtained from GetContentHashList.
            /// </summary>
            public TimeSpan MetadataPinElisionDuration { get; init; } = TimeSpan.FromHours(1);
        }

        /// <nodoc />
        public static ICache Create(Configuration configuration, IBlobCacheSecretsProvider secretsProvider)
        {
            BlobCacheContainerName.CheckValidUniverseAndNamespace(configuration.Universe, configuration.Namespace);

            var topology = new ShardedBlobCacheTopology(
                new ShardedBlobCacheTopology.Configuration(
                    ShardingScheme: configuration.ShardingScheme,
                    SecretsProvider: secretsProvider,
                    Universe: configuration.Universe,
                    Namespace: configuration.Namespace));

            var blobMetadataStore = new AzureBlobStorageMetadataStore(new BlobMetadataStoreConfiguration
            {
                Topology = topology,
                BlobFolderStorageConfiguration = new BlobFolderStorageConfiguration
                {
                    StorageInteractionTimeout = configuration.StorageInteractionTimeout,
                    RetryPolicy = BlobFolderStorageConfiguration.DefaultRetryPolicy,
                }
            });

            var blobMemoizationDatabase = new MetadataStoreMemoizationDatabase(blobMetadataStore);
            var blobMemoizationStore = new DatabaseMemoizationStore(blobMemoizationDatabase) { OptimizeWrites = true };

            var blobContentStore = new AzureBlobStorageContentStore(
                new AzureBlobStorageContentStoreConfiguration()
                {
                    Topology = topology,
                    StorageInteractionTimeout = configuration.StorageInteractionTimeout,
                });

            var cache = new OneLevelCache(
                contentStoreFunc: () => blobContentStore,
                memoizationStoreFunc: () => blobMemoizationStore,
                configuration: new OneLevelCacheBaseConfiguration(
                    Id: Guid.NewGuid(),
                    PassContentToMemoization: false,
                    MetadataPinElisionDuration: configuration.MetadataPinElisionDuration
                ));

            return cache;
        }
    }
}
