// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Distributed.Blobs;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Sessions;
using BuildXL.Cache.MemoizationStore.Stores;

namespace BuildXL.Cache.MemoizationStoreAdapter
{
    public partial class BlobCacheFactory
    {
        private ICache CreateCache(Config cacheConfig)
        {
            var connectionString = Environment.GetEnvironmentVariable(cacheConfig.ConnectionStringEnvironmentVariableName);
            var config = new BlobMetadataStoreConfiguration()
            {
                Credentials = new ContentStore.Interfaces.Secrets.AzureBlobStorageCredentials(connectionString),
                ContainerName = cacheConfig.ContainerName
            };

            var store = new AzureBlobStorageMetadataStore(config);

            var memoizationStore = new DatabaseMemoizationStore(new MetadataStoreMemoizationDatabase(store))
            {
                OptimizeWrites = true
            };

            var localFileSystemContentStore = new FileSystemContentStore(
                fileSystem: PassThroughFileSystem.Default,
                clock: SystemClock.Instance,
                rootPath: new AbsolutePath(cacheConfig.LocalCachePath),
                configurationModel: new ConfigurationModel(
                    inProcessConfiguration: new ContentStoreConfiguration(maxSizeQuota: new MaxSizeQuota("10GB")),
                    selection: ConfigurationSelection.RequireAndUseInProcessConfiguration),
                distributedStore: null,
                settings: null,
                coldStorage: null);

            // TODO: we should propagate settings here
            var contentStore = new AzureBlobStorageContentStore(new AzureBlobStorageContentStoreConfiguration()
            {
                Credentials = new ContentStore.Interfaces.Secrets.AzureBlobStorageCredentials(connectionString),
                ContainerName = cacheConfig.ContainerName
            }, () => localFileSystemContentStore);

            var innerCache = new OneLevelCache(
                contentStoreFunc: () => contentStore,
                memoizationStoreFunc: () => memoizationStore,
                id: Guid.NewGuid(),
                passContentToMemoization: false);

            return innerCache;
        }
    }
}
