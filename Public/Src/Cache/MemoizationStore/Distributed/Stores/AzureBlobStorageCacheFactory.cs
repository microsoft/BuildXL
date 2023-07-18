// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
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
            string Namespace,
            int? RetentionPolicyInDays = 0)
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
            /// <remarks>
            /// Default is 12 hours. If the specified duration is hit, pins will stop eliding for that content (which is not incorrect, just slower). The default value
            /// takes into consideration that most builds will take under 12 hours.
            /// </remarks>
            public TimeSpan MetadataPinElisionDuration { get; init; } = TimeSpan.FromHours(12);
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

            // If the user specifed a retention policy time greater than 0, we use that.
            // Otherwise, we use null, which is equivalent to not setting it
            TimeSpan? retentionPolicyTimeSpan = configuration.RetentionPolicyInDays > 0
                ? TimeSpan.FromDays(configuration.RetentionPolicyInDays.Value)
                : null;

            var blobMetadataStore = new AzureBlobStorageMetadataStore(new BlobMetadataStoreConfiguration
            {
                Topology = topology,
                BlobFolderStorageConfiguration = new BlobFolderStorageConfiguration
                {
                    StorageInteractionTimeout = configuration.StorageInteractionTimeout,
                    RetryPolicy = BlobFolderStorageConfiguration.DefaultRetryPolicy,
                }
            });

            // The memoization database will make sure the associated content for a retrieved content
            // hash list is preventively pinned if it runs the risk of being evicted based on the configured retention policy
            // This means that the content store can elide pins for content that is mentioned in get content hash list operations
            var blobMemoizationDatabase = new MetadataStoreMemoizationDatabase(
                blobMetadataStore,
                new MetadataStoreMemoizationDatabaseConfiguration()
                {
                    RetentionPolicy = retentionPolicyTimeSpan,
                    DisablePreventivePinning = configuration.RetentionPolicyInDays is null
                });

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
                    MetadataPinElisionDuration: configuration.MetadataPinElisionDuration,
                    // TODO: remove when we implement preventive pin elision for GetLevelSelectorsAsync
                    DoNotElidePinsForGetLevelSelectors: true
                ));

            return cache;
        }
    }
}
