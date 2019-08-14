// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Distributed.Sessions;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Distributed;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using ContentStoreTest.Distributed.ContentLocation;
using ContentStoreTest.Distributed.Redis;
using ContentStoreTest.Test;
using Xunit;
using Xunit.Abstractions;

namespace ContentStoreTest.Distributed.Sessions
{
    [Trait("Category", "Integration")]
    [Trait("Category", "LongRunningTest")]
    [Collection("Redis-based tests")]
    public class RedisDistributedContentTests : DistributedContentTests
    {
        private readonly LocalRedisFixture _redis;
        protected const HashType ContentHashType = HashType.Vso0;
        protected const int ContentByteCount = 100;

        private readonly ConcurrentDictionary<Guid, LocalRedisProcessDatabase> _localDatabases = new ConcurrentDictionary<Guid, LocalRedisProcessDatabase>();
        private readonly ConcurrentDictionary<Guid, LocalRedisProcessDatabase> _localMachineDatabases = new ConcurrentDictionary<Guid, LocalRedisProcessDatabase>();

        private PinConfiguration PinConfiguration { get; set; }

        private Func<AbsolutePath, int?, RedisContentLocationStoreConfiguration> CreateContentLocationStoreConfiguration { get; set; }

        private Action<RedisContentLocationStoreConfiguration, int?> PostProcessConfiguration { get; set; } = (configuration, index) => { };

        private RedisContentLocationStoreConfiguration _configuration;

        /// <nodoc />
        public RedisDistributedContentTests(LocalRedisFixture redis, ITestOutputHelper output)
            : base(output)
        {
            _redis = redis;
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            foreach (var database in _localDatabases.Values.Concat(_localMachineDatabases.Values))
            {
                database.Dispose();
            }

            base.Dispose(disposing);
        }

        protected override IContentStore CreateStore(
            Context context,
            TestFileCopier fileCopier,
            ICopyRequester copyRequester,
            DisposableDirectory testDirectory,
            int index,
            bool enableDistributedEviction,
            int? replicaCreditInMinutes,
            bool enableRepairHandling,
            bool emptyFileHashShortcutEnabled,
            object additionalArgs)
        {
            var rootPath = testDirectory.Path / "Root";
            var tempPath = testDirectory.Path / "Temp";
            var configurationModel = new ConfigurationModel(Config);
            var pathTransformer = new TestPathTransformer();
            var localMachineData = pathTransformer.GetLocalMachineLocation(rootPath);

            if (!_localDatabases.TryGetValue(context.Id, out var localDatabase))
            {
                localDatabase = LocalRedisProcessDatabase.CreateAndStartEmpty(_redis, TestGlobal.Logger, SystemClock.Instance);
                _localDatabases.TryAdd(context.Id, localDatabase);
            }

            if (!_localMachineDatabases.TryGetValue(context.Id, out var localMachineDatabase))
            {
                localMachineDatabase = LocalRedisProcessDatabase.CreateAndStartEmpty(_redis, TestGlobal.Logger, SystemClock.Instance);
                _localMachineDatabases.TryAdd(context.Id, localMachineDatabase);
            }

            if (enableDistributedEviction && replicaCreditInMinutes == null)
            {
                // Apparently, replicaCreditInMinutes != null enables distributed eviction,
                // so make sure replicaCreditInMinutes is set when enableDistributedEviction is
                // true
                replicaCreditInMinutes = 0;
            }

            _configuration = CreateContentLocationStoreConfiguration?.Invoke(rootPath, index) ?? new RedisContentLocationStoreConfiguration();
            _configuration.BlobExpiryTimeMinutes = 10;
            PostProcessConfiguration(_configuration, index);

            var storeFactory = new MockRedisContentLocationStoreFactory(
                localDatabase,
                localMachineDatabase,
                rootPath,
                mockClock: TestClock,
                _configuration);

            var distributedContentStore = new DistributedContentStore<AbsolutePath>(
                localMachineData,
                (nagleBlock, distributedEvictionSettings, contentStoreSettings, trimBulkAsync) =>
                    new FileSystemContentStore(
                        FileSystem,
                        TestClock,
                        rootPath,
                        configurationModel,
                        nagleQueue: nagleBlock,
                        distributedEvictionSettings: distributedEvictionSettings,
                        settings: contentStoreSettings,
                        trimBulkAsync: trimBulkAsync),
                storeFactory,
                fileCopier,
                fileCopier,
                pathTransformer,
                copyRequester,
                ContentAvailabilityGuarantee,
                tempPath,
                FileSystem,
                RedisContentLocationStoreConstants.DefaultBatchSize,
                retryIntervalForCopies: DistributedContentSessionTests.DefaultRetryIntervalsForTest,
                replicaCreditInMinutes: replicaCreditInMinutes,
                pinConfiguration: PinConfiguration,
                clock: TestClock,
                enableRepairHandling: enableRepairHandling,
                contentStoreSettings: new ContentStoreSettings()
                {
                    CheckFiles = true,
                    UseEmptyFileHashShortcut = emptyFileHashShortcutEnabled,
                    UseLegacyQuotaKeeperImplementation = false,
                });

            distributedContentStore.DisposeContentStoreFactory = false;
            return distributedContentStore;
        }

        [Fact]
        public async Task PinCacheTests()
        {
            var startTime = TestClock.UtcNow;
            var pinCacheTimeToLive = TimeSpan.FromMinutes(30);

            PinConfiguration = new PinConfiguration()
            {
                // Low risk and high risk tolerance for machine or file loss to prevent pin better from kicking in
                MachineRisk = 0.0000001,
                FileRisk = 0.0000001,
                PinRisk = 0.9999,
                PinCacheReplicaCreditRetentionMinutes = (int)pinCacheTimeToLive.TotalMinutes,
                UsePinCache = true
            };

            ContentAvailabilityGuarantee = ReadOnlyDistributedContentSession<AbsolutePath>.ContentAvailabilityGuarantee.FileRecordsExist;

            await RunTestAsync(
                new Context(Logger),
                3,
                async context =>
                {
                    var sessions = context.Sessions;
                    var session0 = context.GetDistributedSession(0);

                    var redisStore0 = (RedisContentLocationStore) session0.ContentLocationStore;

                    // Insert random file in session 0
                    var putResult0 = await sessions[0].PutRandomAsync(context, ContentHashType, false, ContentByteCount, Token).ShouldBeSuccess();

                    // Pinning the file on another machine should succeed
                    await sessions[1].PinAsync(context, putResult0.ContentHash, Token).ShouldBeSuccess();

                    // Remove the location from backing content location store so that in the absence of pin caching the
                    // result of pin should be false.
                    var getBulkResult = await redisStore0.GetBulkAsync(context, new[] { putResult0.ContentHash }, Token, UrgencyHint.Nominal).ShouldBeSuccess();
                    Assert.True(getBulkResult.ContentHashesInfo[0].Locations.Count == 1);

                    await redisStore0.TrimBulkAsync(
                        context,
                        getBulkResult.ContentHashesInfo.Select(c => new ContentHashAndLocations(c.ContentHash)).ToList(),
                        Token,
                        UrgencyHint.Nominal).ShouldBeSuccess();

                    // Verify no locations for the content
                    var postTrimGetBulkResult = await redisStore0.GetBulkAsync(context, new[] { putResult0.ContentHash }, Token, UrgencyHint.Nominal).ShouldBeSuccess();
                    Assert.True((postTrimGetBulkResult.ContentHashesInfo[0].Locations?.Count ?? 0) == 0);

                    // Simulate calling pin within pin cache TTL
                    TestClock.UtcNow = startTime + TimeSpan.FromMinutes(pinCacheTimeToLive.TotalMinutes * .99);

                    // Now try to pin/pin bulk again (within pin cache TTL)
                    await sessions[1].PinAsync(context.Context, putResult0.ContentHash, Token).ShouldBeSuccess();

                    var pinBulkResult1withinTtl = await sessions[1].PinAsync(context.Context, new[] { putResult0.ContentHash }, Token);
                    Assert.True((await pinBulkResult1withinTtl.Single()).Item.Succeeded);

                    // Simulate calling pin within pin cache TTL
                    TestClock.UtcNow = startTime + TimeSpan.FromMinutes(pinCacheTimeToLive.TotalMinutes * 1.01);

                    var pinResult1afterTtl = await sessions[1].PinAsync(context.Context, putResult0.ContentHash, Token);
                    Assert.False(pinResult1afterTtl.Succeeded);

                    var pinBulkResult1afterTtl = await sessions[1].PinAsync(context.Context, new[] { putResult0.ContentHash }, Token);
                    Assert.False((await pinBulkResult1afterTtl.Single()).Item.Succeeded);
                });
        }

        [Theory]
        [InlineData(MachineReputation.Bad)]
        [InlineData(MachineReputation.Missing)]
        [InlineData(MachineReputation.Timeout)]
        public async Task ReputationTrackerTests(MachineReputation badReputation)
        {
            await RunTestAsync(
                new Context(Logger),
                3,
                async context =>
                {
                    var sessions = context.Sessions;
                    var session0 = context.GetDistributedSession(0);

                    var redisStore0 = (RedisContentLocationStore) session0.ContentLocationStore;

                    string content = "MyContent";
                    // Inserting the content into session 0
                    var putResult0 = await sessions[0].PutContentAsync(context, content).ShouldBeSuccess();

                    // Inserting the content into sessions 1 and 2
                    await sessions[1].PutContentAsync(context, content).ShouldBeSuccess();
                    await sessions[2].PutContentAsync(context, content).ShouldBeSuccess();

                    var getBulkResult = await redisStore0.GetBulkAsync(context, new[] {putResult0.ContentHash}, Token, UrgencyHint.Nominal).ShouldBeSuccess();
                    Assert.Equal(3, getBulkResult.ContentHashesInfo[0].Locations.Count);

                    var firstLocation = getBulkResult.ContentHashesInfo[0].Locations[0];
                    var reputation = redisStore0.MachineReputationTracker.GetReputation(firstLocation);
                    Assert.Equal(MachineReputation.Good, reputation);

                    // Changing the reputation
                    redisStore0.MachineReputationTracker.ReportReputation(firstLocation, badReputation);
                    reputation = redisStore0.MachineReputationTracker.GetReputation(firstLocation);
                    Assert.Equal(badReputation, reputation);

                    getBulkResult = await redisStore0.GetBulkAsync(context, new[] { putResult0.ContentHash }, Token, UrgencyHint.Nominal).ShouldBeSuccess();
                    Assert.Equal(3, getBulkResult.ContentHashesInfo[0].Locations.Count);

                    // Location of the machine with bad reputation should be the last one in the list.
                    Assert.Equal(firstLocation, getBulkResult.ContentHashesInfo[0].Locations[2]);

                    // Causing reputation to expire
                    TestClock.UtcNow += TimeSpan.FromHours(1);

                    reputation = redisStore0.MachineReputationTracker.GetReputation(firstLocation);
                    Assert.Equal(MachineReputation.Good, reputation);
                });
        }

        [Fact]
        public Task BigBlobIsNotPutIntoRedis()
        {
            return RunTestAsync(
                new Context(Logger),
                1,
                async context =>
                {
                    var session = context.GetDistributedSession(0);
                    var redisStore = (RedisContentLocationStore)session.ContentLocationStore;

                    await session.PutRandomAsync(context, HashType.Vso0, false, _configuration.MaxBlobSize + 1, CancellationToken.None).ShouldBeSuccess();
                    var counters = redisStore.GetCounters(context).ToDictionaryIntegral();

                    Assert.Equal(0, counters["RedisContentLocationStore.BlobAdapter.PutBlob.Count"]);
                });
        }

        [Fact]
        public Task SmallPutStreamIsPutIntoRedis()
        {
            return RunTestAsync(
                new Context(Logger),
                2,
                async context =>
                {
                    var session0 = context.GetDistributedSession(0);
                    var redisStore0 = (RedisContentLocationStore)session0.ContentLocationStore;

                    var session1 = context.GetDistributedSession(1);
                    var redisStore1 = (RedisContentLocationStore)session1.ContentLocationStore;

                    var putResult = await session0.PutRandomAsync(context, HashType.Vso0, false, 10, CancellationToken.None).ShouldBeSuccess();
                    var counters0 = redisStore0.GetCounters(context).ToDictionaryIntegral();
                    Assert.Equal(1, counters0["RedisContentLocationStore.BlobAdapter.PutBlob.Count"]);

                    await session1.OpenStreamAsync(context, putResult.ContentHash, CancellationToken.None).ShouldBeSuccess();
                    var counters1 = redisStore1.GetCounters(context).ToDictionaryIntegral();
                    Assert.Equal(1, counters1["RedisContentLocationStore.BlobAdapter.GetBlob.Count"]);
                });
        }

        [Fact]
        public Task SmallPutFileIsPutIntoRedis()
        {
            return RunTestAsync(
                new Context(Logger),
                2,
                async context =>
                {
                    var session0 = context.GetDistributedSession(0);
                    var redisStore0 = (RedisContentLocationStore)session0.ContentLocationStore;

                    var session1 = context.GetDistributedSession(1);
                    var redisStore1 = (RedisContentLocationStore)session1.ContentLocationStore;

                    var putResult = await session0.PutRandomFileAsync(context, FileSystem, HashType.Vso0, false, 10, CancellationToken.None).ShouldBeSuccess();
                    var counters0 = redisStore0.GetCounters(context).ToDictionaryIntegral();
                    Assert.Equal(1, counters0["RedisContentLocationStore.BlobAdapter.PutBlob.Count"]);
                    
                    await session1.OpenStreamAsync(context, putResult.ContentHash, CancellationToken.None).ShouldBeSuccess();
                    var counters1 = redisStore1.GetCounters(context).ToDictionaryIntegral();
                    Assert.Equal(1, counters1["RedisContentLocationStore.BlobAdapter.GetBlob.Count"]);
                });
        }

        [Fact]
        public Task SmallCopyIsPutIntoRedis()
        {
            PostProcessConfiguration = (configuration, i) =>
                                       {
                                           if (i == 0)
                                           {
                                               // Disable small files in Redis for session 0
                                               configuration.BlobExpiryTimeMinutes = 0;
                                           }
                                       };
            return RunTestAsync(
                new Context(Logger),
                2,
                async context =>
                {
                    var session0 = context.GetDistributedSession(0);
                    var redisStore0 = (RedisContentLocationStore)session0.ContentLocationStore;

                    // Put a random file when small files in Redis feature is disabled.
                    var putResult = await session0.PutRandomFileAsync(context, FileSystem, HashType.Vso0, false, 10, CancellationToken.None).ShouldBeSuccess();
                    var counters0 = redisStore0.GetCounters(context).ToDictionaryIntegral();
                    Assert.Equal(0, counters0["RedisContentLocationStore.BlobAdapter.PutBlob.Count"]);
                    var contentHash = putResult.ContentHash;

                    var session1 = context.GetDistributedSession(1);
                    var redisStore1 = (RedisContentLocationStore)session1.ContentLocationStore;

                    // Getting the file when small files in Redis feature is enabled.
                    // This should copy the file from another "machine" and place blob into redis.
                    await session1.OpenStreamAsync(context, contentHash, CancellationToken.None).ShouldBeSuccess();
                    var counters1 = redisStore1.GetCounters(context).ToDictionaryIntegral();
                    Assert.Equal(1, counters1["RedisContentLocationStore.BlobAdapter.GetBlob.Count"]);
                    Assert.Equal(1, counters1["RedisContentLocationStore.BlobAdapter.PutBlob.Count"]);
                });
        }

        [Fact]
        public Task RepeatedBlobIsSkipped()
        {
            return RunTestAsync(
                new Context(Logger),
                2,
                async context =>
                {
                    var sessions = context.Sessions;

                    var session0 = context.GetDistributedSession(0);
                    var redisStore0 = (RedisContentLocationStore)session0.ContentLocationStore;

                    var session1 = context.GetDistributedSession(1);
                    var redisStore1 = (RedisContentLocationStore)session1.ContentLocationStore;

                    var file = ThreadSafeRandom.GetBytes(10);
                    var fileString = Encoding.Default.GetString(file);

                    await session0.PutContentAsync(context, fileString).ShouldBeSuccess();
                    var counters0 = redisStore0.GetCounters(context).ToDictionaryIntegral();
                    Assert.Equal(1, counters0["RedisContentLocationStore.BlobAdapter.PutBlob.Count"]);

                    await session1.PutContentAsync(context, fileString).ShouldBeSuccess();
                    var counters1 = redisStore1.GetCounters(context).ToDictionaryIntegral();
                    Assert.Equal(1, counters1["RedisContentLocationStore.BlobAdapter.PutBlob.Count"]);
                    Assert.Equal(1, counters1["RedisContentLocationStore.BlobAdapter.SkippedBlobs.Count"]);
                });
        }

        [Fact]
        public Task SmallBlobsInRedisAfterCopy()
        {
            return RunTestAsync(
                new Context(Logger),
                2,
                async context =>
                {
                    var sessions = context.Sessions;

                    var session0 = context.GetDistributedSession(0);
                    var redisStore0 = (RedisContentLocationStore)session0.ContentLocationStore;

                    var session1 = context.GetDistributedSession(1);
                    var redisStore1 = (RedisContentLocationStore)session1.ContentLocationStore;

                    var putResult = await session0.PutRandomAsync(context, HashType.Vso0, false, 10, CancellationToken.None).ShouldBeSuccess();
                    var counters0 = redisStore0.GetCounters(context).ToDictionaryIntegral();
                    Assert.Equal(1, counters0["RedisContentLocationStore.BlobAdapter.PutBlob.Count"]);

                    // Simulate that the blob has expired.
                    var blobKey = RedisBlobAdapter.GetBlobKey(putResult.ContentHash);
                    var deleted = await _localDatabases[context.Context.Id].KeyDeleteAsync($"{RedisContentLocationStoreFactory.DefaultKeySpace}{blobKey}");
                    Assert.True(deleted, $"Could not delete {blobKey} because it does not exist.");

                    var openStreamResult = await session1.OpenStreamAsync(context, putResult.ContentHash, CancellationToken.None).ShouldBeSuccess();
                    var counters1 = redisStore1.GetCounters(context).ToDictionaryIntegral();
                    Assert.Equal(0, counters1["RedisContentLocationStore.BlobAdapter.DownloadedBlobs.Count"]);
                    Assert.Equal(1, counters1["RedisContentLocationStore.BlobAdapter.PutBlob.Count"]);
                });
        }
    }
}
