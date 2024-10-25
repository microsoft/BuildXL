// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.BuildCacheResource.Model;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Synchronization;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.InterfacesTest.Utils;
using ContentStoreTest.Test;
using BuildXL.Cache.MemoizationStore.Sessions;
using BuildXL.Cache.MemoizationStore.Stores;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.InterfacesTest.Sessions;
using Xunit;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;
using BuildXL.Cache.ContentStore.Distributed.Test.Stores;
using BuildXL.Cache.ContentStore.Distributed.Test;
using ContentStoreTest.Distributed.Redis;
using System.Collections.Generic;
using System.Threading;
using Xunit.Abstractions;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.MemoizationStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.MemoizationStore.Distributed.Stores;

namespace BuildXL.Cache.MemoizationStore.Test.Sessions
{
    [Trait("Category", "LongRunningTest")]
    [Collection("Redis-based tests")]
    public class BlobCacheTests : CacheTests
    {
        private readonly MemoryClock _clock = new MemoryClock();
        private readonly LocalRedisFixture _redis;
        private readonly ILogger _logger;

        protected virtual bool OptimizeWrites => false;

        protected override bool ImplementsEnumerateStrongFingerprints => false;

        private readonly List<AzuriteStorageProcess> _databasesToDispose = new();

        protected virtual bool UseBuildCacheConfiguration => false;

        public BlobCacheTests(LocalRedisFixture redis, ITestOutputHelper helper)
            : base(() => new PassThroughFileSystem(TestGlobal.Logger), TestGlobal.Logger, helper)
        {
            _redis = redis;
            _logger = TestGlobal.Logger;
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

        protected override ICache CreateCache(DisposableDirectory testDirectory)
        {
            var shards = Enumerable.Range(0, 10).Select(shard => (BlobCacheStorageAccountName)new BlobCacheStorageShardingAccountName("0123456789", shard, "testing")).ToList();

            // Force it to use a non-sharding account
            shards.Add(new BlobCacheStorageNonShardingAccountName("devstoreaccount1"));

            var process = AzuriteStorageProcess.CreateAndStart(
                _redis,
                _logger,
                accounts: shards.Select(account => account.AccountName).ToList());
            _databasesToDispose.Add(process);

            BuildCacheConfiguration buildCacheConfiguration;
            IBlobCacheContainerSecretsProvider secretsProvider;
            if (UseBuildCacheConfiguration)
            {
                buildCacheConfiguration = BuildCacheConfigurationSecretGenerator.GenerateConfigurationFrom(cacheName: "MyCache", process, shards);
                secretsProvider = new AzureBuildCacheSecretsProvider(buildCacheConfiguration);
                // Under the build cache scenario, the account names are created using the corresponding URIs directly. So let's keep that in sync and use those
                shards = buildCacheConfiguration.Shards.Select(shard => shard.GetAccountName()).ToList();
            }
            else
            {
                buildCacheConfiguration = null;
                secretsProvider = new ConnectionStringSecretsProvider(process, shards);
            }

            var context = new OperationContext(new Context(Logger), CancellationToken.None);

            var result = AzureBlobStorageCacheFactory.Create(
                context,
                new AzureBlobStorageCacheFactory.Configuration(
                    ShardingScheme: new ShardingScheme(ShardingAlgorithm.JumpHash, shards),
                    Universe: ThreadSafeRandom.LowercaseAlphanumeric(10),
                    Namespace: "default",
                    RetentionPolicyInDays: null,
                    IsReadOnly: true),
                secretsProvider);

            return result.Cache;
        }
    }
}
