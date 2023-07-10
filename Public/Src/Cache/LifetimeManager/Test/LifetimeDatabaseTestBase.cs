// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.InterfacesTest;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Sessions;
using BuildXL.Cache.MemoizationStore.Stores;
using ContentStoreTest.Distributed.Redis;
using ContentStoreTest.Test;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.Cache.BlobLifetimeManager.Test
{
    [CollectionDefinition("Redis-based tests")]
    public class LocalRedisCollection : ICollectionFixture<LocalRedisFixture>
    {
        // This class has no code, and is never created. Its purpose is simply
        // to be the place to apply [CollectionDefinition] and all the
        // ICollectionFixture<> interfaces.
    }

    [Collection("Redis-based tests")]
    [Trait("Category", "WindowsOSOnly")] // 'redis-server' executable no longer exists
    public class LifetimeDatabaseTestBase : TestWithOutput
    {
        private readonly string _runId = ThreadSafeRandom.LowercaseAlphanumeric(10);
        private readonly LocalRedisFixture _fixture;
        public LifetimeDatabaseTestBase(LocalRedisFixture redis, ITestOutputHelper output)
            : base(output) => _fixture = redis;

        protected async Task RunTest(OperationContext context, Func<IBlobCacheTopology, ICacheSession, Task> run)
        {
            var shards = Enumerable.Range(0, 10).Select(shard => (BlobCacheStorageAccountName)new BlobCacheStorageShardingAccountName("0123456789", shard, "testing")).ToList();

            using var process = AzuriteStorageProcess.CreateAndStart(
                _fixture,
                TestGlobal.Logger,
                accounts: shards.Select(account => account.AccountName).ToList());

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
                    Universe: _runId,
                    Namespace: "default"));

            var blobMetadataStore = new AzureBlobStorageMetadataStore(new BlobMetadataStoreConfiguration
            {
                Topology = topology,
                BlobFolderStorageConfiguration = new BlobFolderStorageConfiguration
                {
                    RetryPolicy = new RetryPolicyConfiguration
                    {
                        MaximumRetryCount = 0,
                    },
                    StorageInteractionTimeout = TimeSpan.FromSeconds(1)
                }
            });

            var blobMemoizationDatabase = new MetadataStoreMemoizationDatabase(
                blobMetadataStore,
                new MetadataStoreMemoizationDatabaseConfiguration
                {
                    // For the purpose of the lifetime manager, no content pins should ever be required as there is no blob expiry set up in the L3.
                    DisablePreventivePinningForTests = true,
                });
            var blobMemoizationStore = new DatabaseMemoizationStore(blobMemoizationDatabase) { OptimizeWrites = true };

            var blobContentStore = new AzureBlobStorageContentStore(
                new AzureBlobStorageContentStoreConfiguration()
                {
                    Topology = topology,
                    StorageInteractionTimeout = TimeSpan.FromSeconds(1),
                });

            var cache = new OneLevelCache(
                contentStoreFunc: () => blobContentStore,
                memoizationStoreFunc: () => blobMemoizationStore,
                configuration: new OneLevelCacheBaseConfiguration(
                    Id: Guid.NewGuid(),
                    PassContentToMemoization: false,
                    MetadataPinElisionDuration: TimeSpan.FromHours(1)
                ));

            await cache.StartupAsync(context).ThrowIfFailure();

            var session = cache.CreateSession(context, "session", implicitPin: ImplicitPin.None).ThrowIfFailure().Session;

            await session!.StartupAsync(context).ThrowIfFailure();

            await run(topology, session);
        }
    }
}
