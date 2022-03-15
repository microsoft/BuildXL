// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Sessions;
using BuildXL.Cache.MemoizationStore.Stores;
using BuildXL.Utilities;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

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

            var store = new BlobMetadataStore(config);

            var memoizationStore = new DatabaseMemoizationStore(new MetadataStoreMemoizationDatabase(store))
            {
                OptimizeWrites = true
            };

            var innerCache = new OneLevelCache(
                contentStoreFunc: () => new ReadOnlyEmptyContentStore(),
                memoizationStoreFunc: () => memoizationStore,
                id: Guid.NewGuid(),
                passContentToMemoization: false);

            return innerCache;
        }
    }
}
