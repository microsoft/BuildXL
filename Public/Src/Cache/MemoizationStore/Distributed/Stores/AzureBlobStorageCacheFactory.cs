// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Interfaces.Stores;
using BuildXL.Cache.MemoizationStore.Sessions;
using BuildXL.Cache.MemoizationStore.Stores;

namespace BuildXL.Cache.MemoizationStore.Distributed.Stores
{
    /// <nodoc />
    public static class AzureBlobStorageCacheFactory
    {
        /// <summary>
        /// Configuration for <see cref="AzureBlobStorageCacheFactory"/>
        /// </summary>
        /// <param name="ShardingScheme">Sharding scheme to use</param>
        /// <param name="Universe">Cache universe</param>
        /// <param name="Namespace">Cache namespace</param>
        /// <param name="RetentionPolicyInDays">
        /// Set to null to disable engine-side GC if you are using the BlobLifetimeManager to manage the size of the cache.
        /// If not null, if a content hash list is older than this retention period, the engine will manually pin all of its contents to ensure that content still exists.
        /// </param>
        public record Configuration(
            ShardingScheme ShardingScheme,
            string Universe,
            string Namespace,
            int? RetentionPolicyInDays)
        {
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
        public static IFullCache Create(Configuration configuration, IBlobCacheSecretsProvider secretsProvider)
        {
            BlobCacheContainerName.CheckValidUniverseAndNamespace(configuration.Universe, configuration.Namespace);

            IBlobCacheTopology topology = CreateTopology(configuration, secretsProvider);

            Contract.Assert((configuration.RetentionPolicyInDays ?? 1) > 0, $"{nameof(configuration.RetentionPolicyInDays)} must be null or greater than 0");

            TimeSpan? retentionPolicyTimeSpan = configuration.RetentionPolicyInDays is null
                ? null
                : TimeSpan.FromDays(configuration.RetentionPolicyInDays.Value);

            IMemoizationStore memoizationStore = CreateMemoizationStore(configuration, topology, retentionPolicyTimeSpan);
            IContentStore contentStore = CreateContentStore(configuration, topology);

            return CreateCache(configuration, contentStore, memoizationStore);
        }

        private static IFullCache CreateCache(Configuration configuration, IContentStore contentStore, IMemoizationStore memoizationStore)
        {
            return new OneLevelCache(
                            contentStoreFunc: () => contentStore,
                            memoizationStoreFunc: () => memoizationStore,
                            configuration: new OneLevelCacheBaseConfiguration(
                                Id: Guid.NewGuid(),
                                AutomaticallyOverwriteContentHashLists: false,
                                MetadataPinElisionDuration: configuration.MetadataPinElisionDuration,
                                // TODO: remove when we implement preventive pin elision for GetLevelSelectorsAsync
                                DoNotElidePinsForGetLevelSelectors: true
                            ));
        }

        private static IBlobCacheTopology CreateTopology(Configuration configuration, IBlobCacheSecretsProvider secretsProvider)
        {
            return new ShardedBlobCacheTopology(
                new ShardedBlobCacheTopology.Configuration(
                    ShardingScheme: configuration.ShardingScheme,
                    SecretsProvider: secretsProvider,
                    Universe: configuration.Universe,
                    Namespace: configuration.Namespace));
        }

        private static AzureBlobStorageContentStore CreateContentStore(Configuration configuration, IBlobCacheTopology topology)
        {
            return new AzureBlobStorageContentStore(
                    new AzureBlobStorageContentStoreConfiguration()
                    {
                        Topology = topology,
                        StorageInteractionTimeout = configuration.StorageInteractionTimeout,
                    });
        }

        private static DatabaseMemoizationStore CreateMemoizationStore(
            Configuration configuration,
            IBlobCacheTopology topology,
            TimeSpan? retentionPolicyTimeSpan)
        {
            var blobMetadataStore = new AzureBlobStorageMetadataStore(
                new BlobMetadataStoreConfiguration
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
                    DisablePreventivePinning = retentionPolicyTimeSpan is null
                });

            return new DatabaseMemoizationStore(blobMemoizationDatabase) { OptimizeWrites = true };
        }
    }
}
