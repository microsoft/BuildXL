// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#if MICROSOFT_INTERNAL
using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Sessions;
using BuildXL.Cache.MemoizationStore.Stores;
using BuildXL.Cache.MemoizationStore.Test.Sessions;
using ContentStoreTest.Distributed.Redis;
using ContentStoreTest.Test;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.Cache.MemoizationStore.Distributed.Test
{
    [Trait("Category", "LongRunningTest")]
    [Collection("Redis-based tests")]
    [Trait("Category", "WindowsOSOnly")] // 'redis-server' executable no longer exists
    public class AzureBlobStoragePublishingCacheTests : PublishingCacheTests
    {
        private readonly LocalRedisFixture _fixture;
        private readonly ILogger _logger;

        private readonly List<AzuriteStorageProcess> _databasesToDispose = new();

        public AzureBlobStoragePublishingCacheTests(LocalRedisFixture redis, ITestOutputHelper helper)
            : base(helper)
        {
            _fixture = redis;
            _logger = TestGlobal.Logger;
        }

        protected override PublishingCacheConfiguration CreateConfiguration(bool publishAsynchronously)
        {
            return new AzureBlobStoragePublishingCacheConfiguration()
            {
                PublishAsynchronously = publishAsynchronously,
                Configuration = new Stores.AzureBlobStorageCacheFactory.Configuration(
                    ShardingScheme: new ShardingScheme(ShardingAlgorithm.SingleShard, new() { BlobCacheStorageAccountName.Parse("devstoreaccount1") }),
                    Universe: "default",
                    Namespace: "default",
                    RetentionPolicyInDays: 0)
            };
        }

        protected override IPublishingStore CreatePublishingStore(IContentStore contentStore)
        {
            return new AzureBlobStoragePublishingStore(contentStore);
        }

        protected override ICache CreateCache(DisposableDirectory testDirectory)
        {
            var process = AzuriteStorageProcess.CreateAndStart(
                _fixture,
                _logger);
            _databasesToDispose.Add(process);

            var contentStore = CreateInnerCache(testDirectory);
            return new PublishingCacheWrapper<LocalCache>(
                cacheId: Guid.NewGuid(),
                localCache: contentStore,
                remotePublishingStore: new IPublishingStore[]
                {
                    CreatePublishingStore(new CacheToContentStore(contentStore)),
                },
                configFactory: () => CreateConfiguration(publishAsynchronously: false),
                pat: process.ConnectionString);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing)
            {
                foreach (var database in _databasesToDispose)
                {
                    database.Dispose();
                }
            }
        }
    }
}
#endif
