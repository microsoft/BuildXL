// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#if MICROSOFT_INTERNAL
using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Blobs;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.MemoizationStore.Distributed.Stores;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Sessions;
using BuildXL.Cache.MemoizationStore.Vsts;
using ContentStoreTest.Distributed.Redis;
using ContentStoreTest.Test;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.Cache.MemoizationStore.Test.Sessions
{
    [Trait("Category", "LongRunningTest")]
    [Collection("Redis-based tests")]
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
            return new BuildCacheServiceConfiguration(
                           cacheServiceContentEndpoint: "contentEndpoint",
                           cacheServiceFingerprintEndpoint: "fingerprintEndpoint")
            {
                PublishAsynchronously = publishAsynchronously
            };
        }

        protected override ICache CreateCache(DisposableDirectory testDirectory)
        {
            var instance = AzuriteStorageProcess.CreateAndStartEmpty(_fixture, _logger);
            _databasesToDispose.Add(instance);

            var contentStore = CreateInnerCache(testDirectory);
            return new PublishingCacheWrapper<LocalCache>(
                cacheId: Guid.NewGuid(),
                localCache: contentStore,
                remotePublishingStore: CreatePublishingStore(new CacheToContentStore(contentStore)),
                configFactory: () => CreateConfiguration(publishAsynchronously: false),
                pat: instance.ConnectionString);
        }

        protected override IPublishingStore CreatePublishingStore(IContentStore contentStore)
        {
            return new AzureBlobStoragePublishingStore(contentStore);
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
