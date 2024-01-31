// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Auth;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Interfaces.Stores;
using BuildXL.Cache.MemoizationStore.InterfacesTest.Results;
using BuildXL.Cache.MemoizationStore.InterfacesTest.Sessions;
using BuildXL.Cache.MemoizationStore.Stores;
using ContentStoreTest.Distributed.Redis;
using ContentStoreTest.Test;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.Cache.MemoizationStore.Test.Sessions
{
    [Trait("Category", "LongRunningTest")]
    [Collection("Redis-based tests")]
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
            // Many tests don't upload content for a given content hash list, and a null retention policy will make the database try to preventively pin
            // nonexistent content
            var conf = new Host.Configuration.MetadataStoreMemoizationDatabaseConfiguration() { DisablePreventivePinning = true };
            var database = new MetadataStoreMemoizationDatabase(store: CreateAzureBlobStorageMetadataStore(), conf);

            return new DatabaseMemoizationStore(database: database);
        }

        private AzureBlobStorageMetadataStore CreateAzureBlobStorageMetadataStore()
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
                    IAzureStorageCredentials credentials = new SecretBasedAzureStorageCredentials(connectionString);
                    Contract.Assert(credentials.GetAccountName() == account.AccountName);
                    return (Account: account, Credentials: credentials);
                }).ToDictionary(kvp => kvp.Account, kvp => kvp.Credentials);

            var topology = new ShardedBlobCacheTopology(
                new ShardedBlobCacheTopology.Configuration(
                    new ShardingScheme(ShardingAlgorithm.JumpHash, credentials.Keys.ToList()),
                    SecretsProvider: new StaticBlobCacheSecretsProvider(credentials),
                    Universe: ThreadSafeRandom.LowercaseAlphanumeric(10),
                    Namespace: "default",
                    BlobRetryPolicy: new ShardedBlobCacheTopology.BlobRetryPolicy()));
            var config = new BlobMetadataStoreConfiguration
            {
                Topology = topology,
            };

            topology.EnsureContainersExistAsync(new OperationContext(new Context(Logger), CancellationToken.None)).GetAwaiter().GetResult().ShouldBeSuccess();

            return new AzureBlobStorageMetadataStore(configuration: config);
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

        protected async Task RunTestAsync(
          Context context, TimeSpan retentionPolicy, Func<DatabaseMemoizationStore, AzureBlobStorageMetadataStore, MetadataStoreMemoizationDatabase, ICacheSession, IContentSession, ContentStoreInternalTracer, FileSystemContentStore, Task> funcAsync)
        {
            var metadataStore = CreateAzureBlobStorageMetadataStore();
            var database = new MetadataStoreMemoizationDatabase(metadataStore,
                new Host.Configuration.MetadataStoreMemoizationDatabaseConfiguration() { RetentionPolicy = retentionPolicy });

            using var store = new DatabaseMemoizationStore(database: database);

            using var testDirectory = new DisposableDirectory(FileSystem);
            var configuration = ContentStoreConfiguration.CreateWithMaxSizeQuotaMB(1);
            var configurationModel = new ConfigurationModel(configuration);

            using (var contentStore = new FileSystemContentStore(
                FileSystem, SystemClock.Instance, testDirectory.Path, configurationModel))
            {
                try
                {
                    var startupContentStoreResult = await contentStore.StartupAsync(context);
                    startupContentStoreResult.ShouldBeSuccess();

                    var contentSessionResult = contentStore.CreateSession(context, Name, ImplicitPin.None);
                    contentSessionResult.ShouldBeSuccess();

                    var sessionResult = store.CreateSession(context, Name, contentSessionResult.Session, automaticallyOverwriteContentHashLists: true);
                    sessionResult.ShouldBeSuccess();

                    using (var cacheSession = new OneLevelCacheSession(parent: null, Name, ImplicitPin.None, sessionResult.Session, contentSessionResult.Session))
                    {
                        try
                        {
                            var r = await cacheSession.StartupAsync(context);
                            r.ShouldBeSuccess();

                            await funcAsync(store, metadataStore, database, cacheSession, contentSessionResult.Session, contentStore.Store.InternalTracer, contentStore);
                        }
                        finally
                        {
                            var r = await cacheSession.ShutdownAsync(context);
                            r.ShouldBeSuccess();
                        }
                    }
                }
                finally
                {
                    var shutdownContentStoreResult = await contentStore.ShutdownAsync(context);
                    shutdownContentStoreResult.ShouldBeSuccess();
                }
            }
        }

        [Fact]
        public Task TestContentHashListUploadTimeRoundtrip()
        {
            var context = new Context(Logger);
            var strongFingerprint = StrongFingerprint.Random();

            return RunTestAsync(context, retentionPolicy: TimeSpan.FromDays(1), async (
                DatabaseMemoizationStore _,
                AzureBlobStorageMetadataStore _,
                MetadataStoreMemoizationDatabase database,
                ICacheSession cacheSession,
                IContentSession _,
                ContentStoreInternalTracer _,
                FileSystemContentStore _) =>
            {
                var before = DateTime.UtcNow;
                var ctx = new OperationContext(context);

                // Store a new content hash list
                var putResult = await cacheSession.PutRandomAsync(
                    context, ContentHashType, false, RandomContentByteCount, Token);
                var contentHashList = new ContentHashList(new[] { putResult.ContentHash });
                var addResult = await database.CompareExchangeAsync(
                    ctx, strongFingerprint, string.Empty, new ContentHashListWithDeterminism(null, CacheDeterminism.None), new ContentHashListWithDeterminism(contentHashList, CacheDeterminism.None)).ShouldBeSuccess();

                // Now retrieve it
                var getResult = await database.GetContentHashListAsync(ctx, strongFingerprint, preferShared: true).ShouldBeSuccess();

                // The last upload time needs to be some date time greater than 'before'
                Assert.True(getResult.LastContentPinnedTime! > before);

                // Now retrieve it again
                var getResultAgain = await database.GetContentHashListAsync(ctx, strongFingerprint, preferShared: true).ShouldBeSuccess();

                // The upload time needs to be the same one that was assigned on add
                Assert.Equal(getResult.LastContentPinnedTime!, getResultAgain.LastContentPinnedTime);
            });
        }

        [Fact]
        public Task TestGetContentHashListTriggersPreventivePins()
        {
            var context = new Context(Logger);
            var strongFingerprint = StrongFingerprint.Random();

            var retentionPolicy = TimeSpan.FromDays(1);

            return RunTestAsync(context, retentionPolicy, async (
                DatabaseMemoizationStore store,
                AzureBlobStorageMetadataStore metadataStore,
                MetadataStoreMemoizationDatabase database,
                ICacheSession cacheSession,
                IContentSession contentSession,
                ContentStoreInternalTracer tracer,
                FileSystemContentStore _) =>
            {
                var before = DateTime.UtcNow;
                var ctx = new OperationContext(context);

                // Store a new content hash list
                var putResult = await cacheSession.PutRandomAsync(
                    context, ContentHashType, false, RandomContentByteCount, Token);
                var contentHashList = new ContentHashList(new[] { putResult.ContentHash });
                var addResult = await database.CompareExchangeAsync(
                    ctx, strongFingerprint, string.Empty, new ContentHashListWithDeterminism(null, CacheDeterminism.None), new ContentHashListWithDeterminism(contentHashList, CacheDeterminism.None)).ShouldBeSuccess();

                // Now retrieve it using the store
                await store.GetContentHashListAsync(ctx, strongFingerprint, Token, contentSession, automaticallyOverwriteContentHashLists: contentSession != null).ShouldBeSuccess();

                // This operation shouldn't have triggered any preventive pins, since the content is guaranteed by the eviction policy
                Assert.Equal(0, GetPinCount(tracer));

                // Retrieve the same content hash list using a lower level component so we can inspect the last upload time later
                var getResult = await database.GetContentHashListAsync(ctx, strongFingerprint, preferShared: true).ShouldBeSuccess();

                // Change the timestamp so it looks like it is due for being evicted
                await metadataStore.UpdateLastContentPinnedTimeForTestingAsync(ctx, strongFingerprint, DateTime.UtcNow - retentionPolicy).ShouldBeSuccess();

                // Now retrieve it again. This should have triggered a pin on the content and the content hash list last pin time updated
                await store.GetContentHashListAsync(ctx, strongFingerprint, Token, contentSession, automaticallyOverwriteContentHashLists: contentSession != null).ShouldBeSuccess();

                // The operation should have triggered a pin on the (single) content
                Assert.Equal(1, GetPinCount(tracer));

                // Retrieve the same content hash list using a lower level component so we can inspect the last upload time
                var getResultWithPreventivePin = await database.GetContentHashListAsync(ctx, strongFingerprint, preferShared: true).ShouldBeSuccess();

                // The last content pinned time should have been updated, and it should be greater than the one retrieved on the first call
                Assert.True(getResultWithPreventivePin.LastContentPinnedTime > getResult.LastContentPinnedTime);

                // Just being defensive: retrieve it a third time. Now no new pins should be triggered
                await store.GetContentHashListAsync(ctx, strongFingerprint, Token, contentSession, automaticallyOverwriteContentHashLists: contentSession != null).ShouldBeSuccess();
                Assert.Equal(1, GetPinCount(tracer));
            });
        }

        [Fact]
        public Task TestFaultyEvictionStateSelfRecovery()
        {
            var context = new Context(Logger);
            var strongFingerprint = StrongFingerprint.Random();

            var retentionPolicy = TimeSpan.FromDays(1);

            return RunTestAsync(context, retentionPolicy, async (
                DatabaseMemoizationStore store,
                AzureBlobStorageMetadataStore metadataStore,
                MetadataStoreMemoizationDatabase database,
                ICacheSession cacheSession,
                IContentSession contentSession,
                ContentStoreInternalTracer tracer,
                FileSystemContentStore fileSystemContentStore) =>
            {
                var before = DateTime.UtcNow;
                var ctx = new OperationContext(context);

                // Store a new content hash list
                var putResult = await cacheSession.PutStreamAsync(context, ContentStore.Hashing.HashType.Vso0, new MemoryStream(new byte[] { 1, 2, 3 }), Token);

                var contentHashList = new ContentHashList(new[] { putResult.ContentHash });
                var addResult = await database.CompareExchangeAsync(
                    ctx, strongFingerprint, string.Empty, new ContentHashListWithDeterminism(null, CacheDeterminism.None), new ContentHashListWithDeterminism(contentHashList, CacheDeterminism.None)).ShouldBeSuccess();

                // Now retrieve it using the store
                await store.GetContentHashListAsync(ctx, strongFingerprint, Token, contentSession, automaticallyOverwriteContentHashLists: contentSession != null).ShouldBeSuccess();

                // This operation shouldn't have triggered any preventive pins, since the content is guaranteed by the eviction policy
                Assert.Equal(0, GetPinCount(tracer));

                // Delete the content to introduce a faulty state wrt eviction
                await fileSystemContentStore.DeleteAsync(ctx, putResult.ContentHash).ShouldBeSuccess();

                // Try to place the file. This should fail and it should also trigger a notification on the memoization database so the corresponding fingerprint last pinned time is removed
                var placeFileResult = await cacheSession.PlaceFileAsync(ctx,
                    new List<ContentHashWithPath>() { new ContentHashWithPath(putResult.ContentHash, new AbsolutePath(Path.Combine(Path.GetTempPath(), GetRandomFileName()))) },
                    FileAccessMode.Write,
                    FileReplacementMode.ReplaceExisting,
                    FileRealizationMode.Copy,
                    Token);

                Assert.True(placeFileResult.First().Result.Item.Code == ContentStore.Interfaces.Results.PlaceFileResult.ResultCode.NotPlacedContentNotFound);

                // Retrieve the same content hash list using a lower level component so we can inspect the last upload time later
                var getResult = await database.GetContentHashListAsync(ctx, strongFingerprint, preferShared: true).ShouldBeSuccess();

                // The failed place operation should have caused that the last content pinned time is now cleared
                Assert.Equal(null, getResult.LastContentPinnedTime);

                // Put back the same content
                putResult = await cacheSession.PutStreamAsync(context, ContentStore.Hashing.HashType.Vso0, new MemoryStream(new byte[] { 1, 2, 3 }), Token);

                // Now retrieve it using the store
                await store.GetContentHashListAsync(ctx, strongFingerprint, Token, contentSession, automaticallyOverwriteContentHashLists: contentSession != null).ShouldBeSuccess();

                // This operation shoukd have triggered preventive pins, since no metadata was present
                Assert.Equal(1, GetPinCount(tracer));
            });
        }

        [Fact]
        public Task TestSelectorsAreMRUOrdered()
        {
            var context = new Context(Logger);

            var retentionPolicy = TimeSpan.FromDays(1);

            return RunTestAsync(context, retentionPolicy, async (
                DatabaseMemoizationStore store,
                AzureBlobStorageMetadataStore metadataStore,
                MetadataStoreMemoizationDatabase database,
                ICacheSession cacheSession,
                IContentSession contentSession,
                ContentStoreInternalTracer tracer,
                FileSystemContentStore fileSystemContentStore) =>
            {
                var ctx = new OperationContext(context);

                // Store a new content hash list
                var strongFingerprint = StrongFingerprint.Random();
                _ = await AddContentHashListWithStrongFingerprint(database, cacheSession, context, strongFingerprint, ctx);

                // Make sure there is a 1 second delay between pushes. The last modified time for blobs has a 1 second granularity.
                Thread.Sleep(1000);

                // Store another one with the same weak fingerprint
                var strongFingerprint2 = StrongFingerprint.Random(weakFingerprint: strongFingerprint.WeakFingerprint);
                _ = await AddContentHashListWithStrongFingerprint(database, cacheSession, context, strongFingerprint2, ctx);

                // We should get two selectors
                var result = await database.GetLevelSelectorsAsync(ctx, strongFingerprint.WeakFingerprint, 0).ShouldBeSuccess();
                var selectors = result.Value.Selectors;

                Assert.Equal(2, selectors.Count);

                // The second content hash list should be listed first (since it got added last)
                Assert.Equal(strongFingerprint2.Selector.ContentHash, selectors[0].ContentHash);
                Assert.Equal(strongFingerprint.Selector.ContentHash, selectors[1].ContentHash);
            });
        }

        private static async Task<ContentStore.Interfaces.Results.PutResult> AddContentHashListWithStrongFingerprint(MetadataStoreMemoizationDatabase database, ICacheSession cacheSession, Context context, StrongFingerprint strongFingerprint, OperationContext ctx)
        {
            var putResult = await cacheSession.PutStreamAsync(context, ContentStore.Hashing.HashType.Vso0, new MemoryStream(new byte[] { 1, 2, 3 }), Token);

            var contentHashList = new ContentHashList(new[] { putResult.ContentHash });
            var addResult = await database.CompareExchangeAsync(
                ctx, strongFingerprint, string.Empty, new ContentHashListWithDeterminism(null, CacheDeterminism.None), new ContentHashListWithDeterminism(contentHashList, CacheDeterminism.None)).ShouldBeSuccess();

            return putResult;
        }

        private static long GetPinCount(ContentStoreInternalTracer tracer)
        {
            var counterDict = tracer.GetCounters().ToDictionaryIntegral();

            return counterDict["PinBulkCallCount"] + counterDict["PinCallCount"];
        }

        private static BlobStorageClientAdapter GetBlobStorageClientAdapter(ContentStoreInternalTracer tracer)
        {
            return new BlobStorageClientAdapter(tracer, new BlobFolderStorageConfiguration());
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
