// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using ContentStoreTest.Test;
using BuildXL.Cache.MemoizationStore.Interfaces.Stores;
using BuildXL.Cache.MemoizationStore.InterfacesTest.Sessions;
using ContentStoreTest.Distributed.Redis;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using Xunit;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using Xunit.Abstractions;
using BuildXL.Cache.MemoizationStore.Stores;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using System;
using BuildXL.Cache.ContentStore.UtilitiesCore;

namespace BuildXL.Cache.MemoizationStore.Test.Sessions
{
    [Trait("Category", "LongRunningTest")]
    [Collection("Redis-based tests")]
    [Trait("Category", "WindowsOSOnly")] // 'redis-server' executable no longer exists
    public class BlobMetadataStoreTests : MemoizationSessionTests
    {
        private readonly MemoryClock _clock = new MemoryClock();
        private readonly LocalRedisFixture _redis;
        private readonly ILogger _logger;

        private readonly List<AzuriteStorageProcess> _databasesToDispose = new();

        public BlobMetadataStoreTests(LocalRedisFixture redis, ITestOutputHelper helper)
            : base(() => new PassThroughFileSystem(TestGlobal.Logger), TestGlobal.Logger, helper)
        {
            _redis = redis;
            _logger = TestGlobal.Logger;
        }

        protected override IMemoizationStore CreateStore(DisposableDirectory testDirectory)
        {
            var shards = Enumerable.Range(0, 10).Select(shard => (BlobCacheStorageAccountName)new BlobCacheStorageShardingAccountName("0123456789", shard, "testing")).ToList();

            // Force it to use a non-sharding account
            shards.Add(new BlobCacheStorageNonShardingAccountName("devstoreaccount1"));

            var process = AzuriteStorageProcess.CreateAndStart(
                _redis,
                _logger,
                accounts: shards.Select(account => account.AccountName).ToList());
            _databasesToDispose.Add(process);

            var credentials = shards.Select(
                account =>
                {
                    var connectionString = process.ConnectionString.Replace("devstoreaccount1", account.AccountName);
                    var credentials = new AzureStorageCredentials(connectionString);
                    Contract.Assert(credentials.GetAccountName() == account.AccountName);
                    return (Account: account, Credentials: credentials);
                }).ToDictionary(kvp => kvp.Account, kvp => kvp.Credentials);

            var topology = new ShardedBlobCacheTopology(
                new ShardedBlobCacheTopology.Configuration(
                    new ShardingScheme(ShardingAlgorithm.JumpHash, credentials.Keys.ToList()),
                    SecretsProvider: new StaticBlobCacheSecretsProvider(credentials),
                    Universe: ThreadSafeRandom.LowercaseAlphanumeric(10),
                    Namespace: "default"));
            var config = new BlobMetadataStoreConfiguration
            {
                Topology = topology,
            };

            var store = new AzureBlobStorageMetadataStore(configuration: config);

            return new DatabaseMemoizationStore(database: new MetadataStoreMemoizationDatabase(store: store));
        }

        public override Task EnumerateStrongFingerprints(int strongFingerprintCount)
        {
            // Do nothing, since operation isn't supported in Redis.
            return Task.FromResult(0);
        }
        public override Task EnumerateStrongFingerprintsEmpty()
        {
            // Do nothing, since operation isn't supported in Redis.
            return Task.FromResult(0);
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
