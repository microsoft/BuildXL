// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.NuCache.EventStreaming;
using BuildXL.Cache.ContentStore.Distributed.Redis;
using BuildXL.Cache.ContentStore.Distributed.Sessions;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Tracing;
using ContentStoreTest.Distributed.ContentLocation;
using ContentStoreTest.Distributed.Redis;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

namespace ContentStoreTest.Distributed.Sessions
{
    [Trait("Category", "Integration")]
    [Trait("Category", "LongRunningTest")]
    [Collection("Redis-based tests")]
    public class LocalLocationStoreDistributedContentTests : DistributedContentTests
    {
        private readonly LocalRedisFixture _redis;
        protected const HashType ContentHashType = HashType.Vso0;
        protected const int ContentByteCount = 100;

        private readonly ConcurrentDictionary<(Guid, int), LocalRedisProcessDatabase> _localDatabases = new ConcurrentDictionary<(Guid, int), LocalRedisProcessDatabase>();

        private PinConfiguration PinConfiguration { get; set; }

        private readonly Dictionary<int, RedisContentLocationStoreConfiguration> _configurations
            = new Dictionary<int, RedisContentLocationStoreConfiguration>();

        private Func<AbsolutePath, int, RedisContentLocationStoreConfiguration> CreateContentLocationStoreConfiguration { get; set; }
        private LocalRedisProcessDatabase _primaryGlobalStoreDatabase;
        private LocalRedisProcessDatabase _secondaryGlobalStoreDatabase;

        /// <nodoc />
        public LocalLocationStoreDistributedContentTests(LocalRedisFixture redis, ITestOutputHelper output)
            : base(output)
        {
            _redis = redis;
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            foreach (var database in _localDatabases.Values)
            {
                database.Dispose();
            }

            base.Dispose(disposing);
        }

        protected override IContentStore CreateStore(
            Context context,
            TestFileCopier fileCopier,
            DisposableDirectory testDirectory,
            int index,
            bool enableDistributedEviction,
            int? replicaCreditInMinutes,
            bool enableRepairHandling,
            bool emptyFileHashShortcutEnabled)
        {
            var rootPath = testDirectory.Path / "Root";
            var tempPath = testDirectory.Path / "Temp";
            var configurationModel = new ConfigurationModel(Config);
            var pathTransformer = new TestPathTransformer();
            var localMachineData = pathTransformer.GetLocalMachineLocation(rootPath);

            int dbIndex = 0;
            var localDatabase = GetDatabase(context, ref dbIndex);
            var localMachineDatabase = GetDatabase(context, ref dbIndex);
            _primaryGlobalStoreDatabase = GetDatabase(context, ref dbIndex);

            if (enableDistributedEviction && replicaCreditInMinutes == null)
            {
                // Apparently, replicaCreditInMinutes != null enables distributed eviction,
                // so make sure replicaCreditInMinutes is set when enableDistributedEviction is
                // true
                replicaCreditInMinutes = 0;
            }

            if (CreateContentLocationStoreConfiguration == null)
            {
                // If not configured use the write both, read redis
                _readMode = ContentLocationMode.Redis;
                _writeMode = ContentLocationMode.Both;
                ConfigureRocksDbContentLocationBasedTest(configureInMemoryEventStore: true, configurePin: false);
            }

            var configuration = CreateContentLocationStoreConfiguration?.Invoke(rootPath, index) ?? new RedisContentLocationStoreConfiguration();

            configuration.RedisGlobalStoreConnectionString = _primaryGlobalStoreDatabase.ConnectionString;
            if (_enableSecondaryRedis)
            {
                _secondaryGlobalStoreDatabase = GetDatabase(context, ref dbIndex);
                configuration.RedisGlobalStoreSecondaryConnectionString = _secondaryGlobalStoreDatabase.ConnectionString;
            }

            configuration.DistributedCentralStore = new DistributedCentralStoreConfiguration(rootPath);

            _configurations[index] = configuration;
            var testPathTransformer = new TestPathTransformer();
            var storeFactory = new RedisContentLocationStoreFactory(
                new LiteralConnectionStringProvider(localDatabase.ConnectionString),
                new LiteralConnectionStringProvider(localMachineDatabase.ConnectionString),
                TestClock,
                contentHashBumpTime: TimeSpan.FromHours(1),
                keySpace: RedisContentLocationStoreFactory.DefaultKeySpace,
                localMachineLocation: testPathTransformer.GetLocalMachineLocation(rootPath),
                fileSystem: null,
                configuration: configuration);

            var distributedContentStore = new DistributedContentStore<AbsolutePath>(
                localMachineData,
                (nagleBlock, distributedEvictionSettings, contentStoreSettings, trimBulkAsync) =>
                {
                    return new FileSystemContentStore(
                        FileSystem,
                        TestClock,
                        rootPath,
                        configurationModel,
                        nagleQueue: nagleBlock,
                        distributedEvictionSettings: distributedEvictionSettings,
                        settings: contentStoreSettings,
                        trimBulkAsync: trimBulkAsync);
                },
                storeFactory,
                fileCopier,
                fileCopier,
                pathTransformer,
                ContentAvailabilityGuarantee,
                tempPath,
                FileSystem,
                retryIntervalForCopies: DistributedContentSessionTests.DefaultRetryIntervalsForTest,
                locationStoreBatchSize: 1,
                replicaCreditInMinutes: replicaCreditInMinutes,
                pinConfiguration: PinConfiguration,
                clock: TestClock,
                enableRepairHandling: enableRepairHandling,
                contentStoreSettings: new ContentStoreSettings()
                {
                    CheckFiles = true,
                    UseEmptyFileHashShortcut = emptyFileHashShortcutEnabled,
                    UseLegacyQuotaKeeperImplementation = false,
                }
                );

            distributedContentStore.DisposeContentStoreFactory = false;
            return distributedContentStore;
        }

        private LocalRedisProcessDatabase GetDatabase(Context context, ref int index)
        {
            index++;
            if (!_localDatabases.TryGetValue((context.Id, index), out var localDatabase))
            {
                localDatabase = LocalRedisProcessDatabase.CreateAndStartEmpty(_redis, TestGlobal.Logger, SystemClock.Instance);
                _localDatabases.TryAdd((context.Id, index), localDatabase);
            }

            return localDatabase;
        }

        [Fact]
        public async Task PinCacheTests()
        {
            var startTime = TestClock.UtcNow;
            TimeSpan pinCacheTimeToLive = TimeSpan.FromMinutes(30);

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

                    var redisStore0 = context.GetRedisStore(session0);

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
                        getBulkResult.ContentHashesInfo.Select(c => c.ContentHash).ToList(),
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

        [Fact]
        public async Task LocalLocationStoreRedundantReconcileTest()
        {
            ConfigureWithOneMaster();

            await RunTestAsync(
                new Context(Logger),
                1,
                async context =>
                {
                    var master = context.GetMaster();

                    master.LocalLocationStore.IsReconcileUpToDate().Should().BeFalse();

                    await master.LocalLocationStore.ReconcileAsync(context).ThrowIfFailure();

                    var result = await master.LocalLocationStore.ReconcileAsync(context).ThrowIfFailure();
                    result.Value.totalLocalContentCount.Should().Be(-1, "Amount of local content should be unknown because reconcile is skipped");

                    master.LocalLocationStore.IsReconcileUpToDate().Should().BeTrue();

                    TestClock.UtcNow += LocalLocationStoreConfiguration.DefaultLocationEntryExpiry.Multiply(0.5);

                    master.LocalLocationStore.IsReconcileUpToDate().Should().BeTrue();

                    TestClock.UtcNow += LocalLocationStoreConfiguration.DefaultLocationEntryExpiry.Multiply(0.5);

                    master.LocalLocationStore.IsReconcileUpToDate().Should().BeFalse();

                    master.LocalLocationStore.MarkReconciled();

                    master.LocalLocationStore.IsReconcileUpToDate().Should().BeTrue();

                    master.LocalLocationStore.MarkReconciled(reconciled: false);

                    master.LocalLocationStore.IsReconcileUpToDate().Should().BeFalse();
                });
        }

        [Fact]
        public async Task LocalLocationStoreDistributedEvictionTest()
        {
            // Use the same context in two sessions when checking for file existence
            var loggingContext = new Context(Logger);

            var contentHashes = new List<ContentHash>();

            int machineCount = 5;
            ConfigureWithOneMaster();

            // HACK: Existing purge code removes an extra file. Testing with this in mind.
            await RunTestAsync(
                loggingContext,
                machineCount,
                async context =>
                {

                    var session = context.Sessions[context.GetMasterIndex()];
                    var masterStore = context.GetMaster();

                    var defaultFileSize = (Config.MaxSizeQuota.Hard / 4) + 1;

                    // Insert random file #1 into session
                    var putResult = await session.PutRandomAsync(context, HashType.Vso0, false, defaultFileSize, Token).ShouldBeSuccess();
                    contentHashes.Add(putResult.ContentHash);

                    // Ensure first piece of content older than other content by at least the replica credit
                    TestClock.UtcNow += TimeSpan.FromMinutes(ReplicaCreditInMinutes);

                    // Put random large file #2 into session.
                    putResult = await session.PutRandomAsync(context, HashType.Vso0, false, defaultFileSize, Token).ShouldBeSuccess();
                    contentHashes.Add(putResult.ContentHash);

                    // Put random large file #3 into session.
                    putResult = await session.PutRandomAsync(context, HashType.Vso0, false, defaultFileSize, Token).ShouldBeSuccess();
                    contentHashes.Add(putResult.ContentHash);

                    // Add replicas on all workers
                    foreach (var workerId in context.EnumerateWorkersIndices())
                    {
                        var workerSession = context.Sessions[workerId];

                        // Open stream to ensure content is brought to machine
                        using (await workerSession.OpenStreamAsync(context, contentHashes[2], Token).ShouldBeSuccess().SelectResult(o => o.Stream))
                        {
                        }
                    }

                    var locationsResult = await masterStore.GetBulkAsync(
                        context,
                        contentHashes,
                        Token,
                        UrgencyHint.Nominal,
                        GetBulkOrigin.Local).ShouldBeSuccess();

                    // Random file #2 and 3 should not be found
                    locationsResult.ContentHashesInfo.Count.Should().Be(3);
                    locationsResult.ContentHashesInfo[0].Locations.Count.Should().Be(1);
                    locationsResult.ContentHashesInfo[1].Locations.Count.Should().Be(1);
                    locationsResult.ContentHashesInfo[2].Locations.Count.Should().Be(machineCount);

                    // Put random large file #4 into session that will evict file #2 and #3.
                    putResult = await session.PutRandomAsync(context, HashType.Vso0, false, defaultFileSize, Token).ShouldBeSuccess();
                    putResult = await session.PutRandomAsync(context, HashType.Vso0, false, defaultFileSize, Token).ShouldBeSuccess();
                    contentHashes.Add(putResult.ContentHash);

                    await context.SyncAsync(context.GetMasterIndex());

                    locationsResult = await masterStore.GetBulkAsync(
                        context,
                        contentHashes,
                        Token,
                        UrgencyHint.Nominal,
                        GetBulkOrigin.Local).ShouldBeSuccess();

                    // Random file #2 and 3 should not be found
                    locationsResult.ContentHashesInfo.Count.Should().Be(4);
                    locationsResult.ContentHashesInfo[0].Locations.Should().BeEmpty();
                    locationsResult.ContentHashesInfo[2].Locations.Count.Should().Be(machineCount - 1, "Master should have evicted newer content because effective age due to replicas was older than other content");
                    locationsResult.ContentHashesInfo[3].Locations.Should().NotBeEmpty();
                },
                implicitPin: ImplicitPin.None,
                enableDistributedEviction: true);
        }

        [Fact]
        public async Task RegisterLocalLocationToGlobalRedisTest()
        {
            ConfigureWithOneMaster();

            await RunTestAsync(
                new Context(Logger),
                3,
                async context =>
                {
                    var store0 = context.GetLocationStore(0);
                    var store1 = context.GetLocationStore(1);

                    var hash = ContentHash.Random();

                    // Add to store 0
                    await store0.RegisterLocalLocationAsync(context, new[] { new ContentHashWithSize(hash, 120) }, Token, UrgencyHint.Nominal).ShouldBeSuccess();

                    // Result should be available from store 1 as a global result
                    var globalResult = await store1.GetBulkAsync(context, new[] { hash }, Token, UrgencyHint.Nominal, GetBulkOrigin.Global).ShouldBeSuccess();
                    globalResult.ContentHashesInfo[0].Locations.Should().NotBeNullOrEmpty();

                    var redisStore0 = (RedisGlobalStore)store0.LocalLocationStore.GlobalStore;

                    int registerContentCount = 5;
                    int registerMachineCount = 300;
                    HashSet<int> ids = new HashSet<int>();
                    List<MachineLocation> locations = new List<MachineLocation>();
                    List<ContentHashWithSize> content = Enumerable.Range(0, 40).Select(i => RandomContentWithSize()).ToList();

                    content.Add(new ContentHashWithSize(ContentHash.Random(), -1));

                    var contentLocationIdLists = new ConcurrentDictionary<ContentHash, HashSet<int>>();

                    for (int i = 0; i < registerMachineCount; i++)
                    {
                        var location = new MachineLocation((TestRootDirectoryPath / "redis" / i.ToString()).ToString());
                        locations.Add(location);
                        var id = await redisStore0.RegisterMachineAsync(context, location);
                        ids.Should().NotContain(id);
                        ids.Add(id);

                        List<ContentHashWithSize> machineContent = Enumerable.Range(0, registerContentCount)
                            .Select(_ => content[ThreadSafeRandom.Generator.Next(content.Count)]).ToList();

                        await redisStore0.RegisterLocationByIdAsync(context, machineContent, id).ShouldBeSuccess();

                        foreach (var item in machineContent)
                        {
                            var locationIds = contentLocationIdLists.GetOrAdd(item.Hash, new HashSet<int>());
                            locationIds.Add(id);
                        }

                        var getBulkResult = await redisStore0.GetBulkAsync(context, machineContent.SelectList(c => c.Hash)).ShouldBeSuccess();
                        IReadOnlyList<ContentLocationEntry> entries = getBulkResult.Value;

                        entries.Count.Should().Be(machineContent.Count);
                        for (int j = 0; j < entries.Count; j++)
                        {
                            var entry = entries[j];
                            var hashAndSize = machineContent[j];
                            entry.ContentSize.Should().Be(hashAndSize.Size);
                            entry.Locations[id].Should().BeTrue();
                        }
                    }

                    foreach (var page in content.GetPages(10))
                    {
                        var globalGetBulkResult = await store1.GetBulkAsync(context, page.SelectList(c => c.Hash), Token, UrgencyHint.Nominal, GetBulkOrigin.Global).ShouldBeSuccess();

                        var redisGetBulkResult = await redisStore0.GetBulkAsync(context, page.SelectList(c => c.Hash)).ShouldBeSuccess();

                        var infos = globalGetBulkResult.ContentHashesInfo;
                        var entries = redisGetBulkResult.Value;

                        for (int i = 0; i < page.Count; i++)
                        {
                            ContentHashWithSizeAndLocations info = infos[i];
                            ContentLocationEntry entry = entries[i];

                            context.Context.Debug($"Hash: {info.ContentHash}, Size: {info.Size}, LocCount: {info.Locations?.Count}");

                            info.ContentHash.Should().Be(page[i].Hash);
                            info.Size.Should().Be(page[i].Size);
                            entry.ContentSize.Should().Be(page[i].Size);

                            if (contentLocationIdLists.ContainsKey(info.ContentHash))
                            {
                                var locationIdList = contentLocationIdLists[info.ContentHash];
                                entry.Locations.Should().BeEquivalentTo(locationIdList.Select(id => new MachineId(id)).ToList());
                                entry.Locations.Should().HaveSameCount(locationIdList);
                                info.Locations.Should().HaveSameCount(locationIdList);

                            }
                            else
                            {
                                info.Locations.Should().BeNullOrEmpty();
                            }
                        }
                    }
                });
        }

        private ContentHashWithSize RandomContentWithSize()
        {
            var maxValue = 1L << ThreadSafeRandom.Generator.Next(1, 63);
            var factor = ThreadSafeRandom.Generator.NextDouble();
            long size = (long)(factor * maxValue);

            return new ContentHashWithSize(ContentHash.Random(), size);
        }

        [Fact]
        public async Task LazyAddForHighlyReplicatedContentTest()
        {
            ConfigureWithOneMaster();

            await RunTestAsync(
                new Context(Logger),
                SafeToLazilyUpdateMachineCountThreshold + 1,
                async context =>
                {
                    var master = context.GetMaster();

                    var hash = ContentHash.Random();
                    var hashes = new[] { new ContentHashWithSize(hash, 120) };

                    foreach (var workerStore in context.EnumerateWorkers())
                    {
                        // Add to store
                        await workerStore.RegisterLocalLocationAsync(context, hashes, Token, UrgencyHint.Nominal).ShouldBeSuccess();
                        workerStore.LocalLocationStore.Counters[ContentLocationStoreCounters.LocationAddQueued].Value.Should().Be(0);
                        workerStore.LocalLocationStore.GlobalStore.Counters[GlobalStoreCounters.RegisterLocalLocation].Value.Should().Be(1);
                    }

                    await master.RegisterLocalLocationAsync(context, hashes, Token, UrgencyHint.Nominal).ShouldBeSuccess();

                    master.LocalLocationStore.Counters[ContentLocationStoreCounters.LocationAddQueued].Value.Should().Be(1,
                        "When number of replicas is over limit location adds should be set through event stream but not eagerly sent to redis");

                    master.LocalLocationStore.GlobalStore.Counters[GlobalStoreCounters.RegisterLocalLocation].Value.Should().Be(0);
                });
        }

        [Fact]
        public async Task TestGetLruPages()
        {
            _enableReconciliation = true;
            ConfigureWithOneMaster();

            await RunTestAsync(
                new Context(Logger),
                2,
                async context =>
                {
                    var master = context.GetMaster();

                    int count = 10000;
                    var hashes = Enumerable.Range(0, count).Select(i => (delay: count - i, hash: ContentHash.Random()))
                        .Select(
                            c => new ContentHashWithLastAccessTimeAndReplicaCount(
                                c.hash,
                                DateTime.Now + TimeSpan.FromSeconds(2 * c.delay)))
                        .ToList();

                    var pages = master.GetLruPages(context, hashes).ToList();

                    var lruHashes = pages.SelectMany(p => p).ToList();

                    var visitedHashes = new HashSet<ContentHash>();
                    // All the hashes should be unique
                    foreach (var hash in lruHashes)
                    {
                        visitedHashes.Add(hash.ContentHash).Should().BeTrue();
                    }

                    // GetLruPages returns not a fully ordered entries. Instead, it sporadically shufles some of them.
                    // This makes impossible to assert here that the result is fully sorted.

                    await Task.Yield();
                });
        }


        [Theory]
        [InlineData(100)]
        // [InlineData(ContentLocationEventStore.ReconcileContentCountEventThreshold)] // Flaky test
        public async Task ReconciliationTest(int removeCount)
        {
            _enableReconciliation = true;
            ConfigureWithOneMaster();

            await RunTestAsync(
                new Context(Logger),
                2,
                async context =>
                {
                    var master = context.GetMaster();
                    var worker = context.GetFirstWorker();
                    var workerId = worker.LocalLocationStore.LocalMachineId;

                    var workerSession = context.Sessions[context.GetFirstWorkerIndex()];

                    ThreadSafeRandom.SetSeed(1);

                    var addedHashes = new List<ContentHashWithSize>();
                    var retainedHashes = new List<ContentHashWithSize>();
                    var removedHashes = Enumerable.Range(0, removeCount).Select(i => new ContentHashWithSize(ContentHash.Random(), 120)).OrderBy(h => h.Hash).ToList();

                    for (int i = 0; i < 10; i++)
                    {
                        var putResult = await workerSession.PutRandomAsync(context, ContentHashType, false, ContentByteCount, Token).ShouldBeSuccess();
                        addedHashes.Add(new ContentHashWithSize(putResult.ContentHash, putResult.ContentSize));
                    }

                    for (int i = 0; i < 10; i++)
                    {
                        var putResult = await workerSession.PutRandomAsync(context, ContentHashType, false, ContentByteCount, Token).ShouldBeSuccess();
                        retainedHashes.Add(new ContentHashWithSize(putResult.ContentHash, putResult.ContentSize));
                    }

                    foreach (var removedHash in removedHashes)
                    {
                        // Add hashes to master db that are not present on the worker so during reconciliation remove events will be sent to master for these hashes
                        master.LocalLocationStore.Database.LocationAdded(context, removedHash.Hash, workerId, removedHash.Size);
                        HasLocation(master.LocalLocationStore.Database, context, removedHash.Hash, workerId, removedHash.Size).Should()
                            .BeTrue();
                    }

                    foreach (var addedHash in addedHashes)
                    {
                        // Remove hashes from master db that ARE present on the worker so during reconciliation add events will be sent to master for these hashes
                        master.LocalLocationStore.Database.LocationRemoved(context, addedHash.Hash, workerId);
                        HasLocation(master.LocalLocationStore.Database, context, addedHash.Hash, workerId, addedHash.Size).Should()
                            .BeFalse();
                    }

                    // Upload and restore checkpoints to trigger reconciliation
                    await UploadCheckpointOnMasterAndRestoreOnWorkers(context);

                    int removedIndex = 0;
                    foreach (var removedHash in removedHashes)
                    {
                        HasLocation(master.LocalLocationStore.Database, context, removedHash.Hash, workerId, removedHash.Size).Should()
                            .BeFalse($"Index={removedIndex}, Hash={removedHash}");
                        removedIndex++;
                    }

                    foreach (var addedHash in addedHashes.Concat(retainedHashes))
                    {
                        HasLocation(master.LocalLocationStore.Database, context, addedHash.Hash, workerId, addedHash.Size).Should()
                            .BeTrue(addedHash.ToString());
                    }
                });
        }

        private static bool HasLocation(ContentLocationDatabase db, OperationContext context, ContentHash hash, MachineId machine, long size)
        {
            if (!db.TryGetEntry(context, hash, out var entry))
            {
                return false;
            }

            entry.ContentSize.Should().Be(size);

            return entry.Locations[machine.Index];
        }

        [Fact]
        public async Task CopyFileWithCancellation()
        {
            ConfigureWithOneMaster();
            await RunTestAsync(new Context(Logger), 3, async context =>
            {
                var sessions = context.Sessions;

                // Insert random file in session 0
                var randomBytes1 = ThreadSafeRandom.GetBytes(0x40);
                var worker = context.GetFirstWorkerIndex();
                var putResult1 = await sessions[worker].PutStreamAsync(context, HashType.Vso0, new MemoryStream(randomBytes1), Token).ShouldBeSuccess();

                // Ensure both files are downloaded to session 2
                var cts = new CancellationTokenSource();
                cts.Cancel();
                var master = context.GetMasterIndex();
                OpenStreamResult openResult = await sessions[master].OpenStreamAsync(context, putResult1.ContentHash, cts.Token);
                openResult.ShouldBeCancelled();
            });
        }

        [Fact]
        public async Task SkipRedundantTouchAndAddTest()
        {
            ConfigureWithOneMaster();

            await RunTestAsync(
                new Context(Logger),
                3,
                async context =>
                {
                    var workerStore = context.GetFirstWorker();

                    var hash = ContentHash.Random();
                    var hashes = new[] { new ContentHashWithSize(hash, 120) };
                    // Add to store
                    await workerStore.RegisterLocalLocationAsync(context, hashes, Token, UrgencyHint.Nominal).ShouldBeSuccess();

                    // Redundant add should not be sent
                    await workerStore.RegisterLocalLocationAsync(context, hashes, Token, UrgencyHint.Nominal).ShouldBeSuccess();

                    workerStore.LocalLocationStore.EventStore.Counters[ContentLocationEventStoreCounters.PublishAddLocations].Value.Should().Be(1);
                    workerStore.LocalLocationStore.EventStore.Counters[ContentLocationEventStoreCounters.PublishTouchLocations].Value.Should().Be(0);

                    await workerStore.TouchBulkAsync(context, hashes, Token, UrgencyHint.Nominal).ShouldBeSuccess();

                    // Touch after register local should not touch the content since it will be viewed as recently touched
                    workerStore.LocalLocationStore.EventStore.Counters[ContentLocationEventStoreCounters.PublishTouchLocations].Value.Should().Be(0);

                    TestClock.UtcNow += TimeSpan.FromDays(1);

                    await workerStore.TouchBulkAsync(context, hashes, Token, UrgencyHint.Nominal).ShouldBeSuccess();

                    // Touch after touch frequency should touch the content again
                    workerStore.LocalLocationStore.EventStore.Counters[ContentLocationEventStoreCounters.PublishTouchLocations].Value.Should().Be(1);

                    // After time interval the redundant add should be sent again (this operates as a touch of sorts)
                    await workerStore.RegisterLocalLocationAsync(context, hashes, Token, UrgencyHint.Nominal).ShouldBeSuccess();
                    workerStore.LocalLocationStore.EventStore.Counters[ContentLocationEventStoreCounters.PublishAddLocations].Value.Should().Be(2);
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

                    var redisStore0 = context.GetRedisStore(session0);

                    string content = "MyContent";
                    // Inserting the content into session 0
                    var putResult0 = await sessions[0].PutContentAsync(context, content).ShouldBeSuccess();

                    // Inserting the content into sessions 1 and 2
                    await sessions[1].PutContentAsync(context, content).ShouldBeSuccess();
                    await sessions[2].PutContentAsync(context, content).ShouldBeSuccess();

                    var getBulkResult = await redisStore0.GetBulkAsync(context, new[] { putResult0.ContentHash }, Token, UrgencyHint.Nominal).ShouldBeSuccess();
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

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task MultiLevelContentLocationStoreDatabasePinTests(bool usePinBulk)
        {
            ConfigureWithOneMaster();
            int storeCount = 3;

            await RunTestAsync(
                new Context(Logger),
                storeCount,
                async context =>
                {
                    var sessions = context.Sessions;

                    var workerStore = context.GetFirstWorker();
                    var firstWorkerIndex = context.GetFirstWorkerIndex();

                    var masterStore = context.GetMaster();

                    // Insert random file in a worker session
                    var putResult0 = await sessions[firstWorkerIndex].PutRandomAsync(context, ContentHashType, false, ContentByteCount, Token).ShouldBeSuccess();

                    // Content SHOULD NOT be registered locally since it has not been queried
                    var localGetBulkResult1a = await workerStore.GetBulkAsync(context, new[] { putResult0.ContentHash }, Token, UrgencyHint.Nominal, GetBulkOrigin.Local).ShouldBeSuccess();
                    localGetBulkResult1a.ContentHashesInfo[0].Locations.Should().BeNullOrEmpty();

                    for (int sessionIndex = 0; sessionIndex < storeCount; sessionIndex++)
                    {
                        // Pin the content in the session which should succeed
                        await PinContentForSession(putResult0.ContentHash, sessionIndex).ShouldBeSuccess();
                    }

                    await workerStore.TrimBulkAsync(
                        context,
                        new[] { putResult0.ContentHash },
                        Token,
                        UrgencyHint.Nominal).ShouldBeSuccess();

                    // Verify no locations for the content on master local db after receiving trim event
                    var postTrimGetBulkResult = await masterStore.GetBulkAsync(context, new[] { putResult0.ContentHash }, Token, UrgencyHint.Nominal, GetBulkOrigin.Local).ShouldBeSuccess();
                    postTrimGetBulkResult.ContentHashesInfo[0].Locations.Should().BeNullOrEmpty();

                    async Task<PinResult> PinContentForSession(ContentHash hash, int sessionIndex)
                    {
                        if (usePinBulk)
                        {
                            var result = await sessions[sessionIndex].PinAsync(context, new[] { hash }, Token);
                            return (await result.First()).Item;
                        }

                        return await sessions[sessionIndex].PinAsync(context, hash, Token);
                    }
                });
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task MultiLevelContentLocationStoreDatabasePinFailOnEvictedContentTests(bool usePinBulk)
        {
            ConfigureWithOneMaster();
            int storeCount = 3;

            // Disable test pin better logic which currently succeeds if there is one replica registered. This will cause the pin
            // logic to fall back to verifying when the number of replicas is below 3
            PinConfiguration = null;

            await RunTestAsync(
                new Context(Logger),
                storeCount,
                async context =>
                {
                    var sessions = context.Sessions;

                    var workerStore = context.GetFirstWorker();
                    var masterStore = context.GetMaster();

                    var hash = ContentHash.Random();

                    // Add to worker store
                    await workerStore.RegisterLocalLocationAsync(context, new[] { new ContentHashWithSize(hash, 120) }, Token, UrgencyHint.Nominal).ShouldBeSuccess();

                    TestClock.UtcNow += TimeSpan.FromMinutes(2);
                    await masterStore.LocalLocationStore.HeartbeatAsync(context).ShouldBeSuccess();

                    for (int sessionIndex = 0; sessionIndex < storeCount; sessionIndex++)
                    {
                        // Heartbeat to ensure machine receives checkpoint
                        await context.GetLocationStore(sessionIndex).LocalLocationStore.HeartbeatAsync(context).ShouldBeSuccess();

                        // Pin the content in the session which should fail with content not found
                        await PinContentForSession(sessionIndex).ShouldBeContentNotFound();
                    }

                    async Task<PinResult> PinContentForSession(int sessionIndex)
                    {
                        if (usePinBulk)
                        {
                            var result = await sessions[sessionIndex].PinAsync(context, new[] { hash }, Token);
                            return (await result.First()).Item;
                        }

                        return await sessions[sessionIndex].PinAsync(context, hash, Token);
                    }
                });
        }

        [Fact]
        public async Task MultiLevelContentLocationStoreOpenStreamTests()
        {
            ConfigureWithOneMaster();

            await RunTestAsync(
                new Context(Logger),
                3,
                async context =>
                {
                    var sessions = context.Sessions;
                    var master = context.GetMaster();
                    var worker = context.GetFirstWorker();

                    var contentHash = await PutContentInSession0_PopulateSession1LocalDb_RemoveContentFromGlobalStore(sessions, context, worker, master);
                    var openStreamResult = await sessions[1].OpenStreamAsync(
                        context,
                        contentHash,
                        Token).ShouldBeSuccess();

#pragma warning disable AsyncFixer02
                    openStreamResult.Stream.Dispose();
#pragma warning restore AsyncFixer02
                });
        }

        [Fact]
        public async Task MultiLevelContentLocationStorePlaceFileTests()
        {
            ConfigureWithOneMaster();

            await RunTestAsync(
                new Context(Logger),
                3,
                async context =>
                {
                    var sessions = context.Sessions;
                    var master = context.GetMaster();
                    var worker = context.GetFirstWorker();

                    var contentHash = await PutContentInSession0_PopulateSession1LocalDb_RemoveContentFromGlobalStore(sessions, context, worker, master);
                    await sessions[1].PlaceFileAsync(
                        context,
                        contentHash,
                        context.Directories[0].Path / "randomfile",
                        FileAccessMode.Write,
                        FileReplacementMode.ReplaceExisting,
                        FileRealizationMode.Copy,
                        Token).ShouldBeSuccess();
                });
        }



        [Fact]
        public async Task MultiLevelContentLocationStorePlaceFileFallbackToGlobalTest()
        {
            ConfigureWithOneMaster();

            await RunTestAsync(
                new Context(Logger),
                3,
                async context =>
                {
                    var sessions = context.Sessions;
                    var store0 = context.GetLocationStore(context.GetMasterIndex());
                    var store1 = context.GetLocationStore(context.EnumerateWorkersIndices().ElementAt(0));
                    var store2 = context.GetLocationStore(context.EnumerateWorkersIndices().ElementAt(1));

                    var content = ThreadSafeRandom.GetBytes((int)ContentByteCount);
                    var hashInfo = HashInfoLookup.Find(ContentHashType);
                    var contentHash = hashInfo.CreateContentHasher().GetContentHash(content);

                    // Register missing location with store 1
                    await store1.RegisterLocalLocationAsync(
                        context,
                        new[] { new ContentHashWithSize(contentHash, content.Length) },
                        Token,
                        UrgencyHint.Nominal).ShouldBeSuccess();

                    // Heartbeat to distribute checkpoints
                    await UploadCheckpointOnMasterAndRestoreOnWorkers(context);

                    var localResult = await store2.GetBulkAsync(context, contentHash, GetBulkOrigin.Local).ShouldBeSuccess();
                    localResult.ContentHashesInfo[0].Locations.Count.Should().Be(1);

                    var globalResult = await store2.GetBulkAsync(context, contentHash, GetBulkOrigin.Global).ShouldBeSuccess();
                    globalResult.ContentHashesInfo[0].Locations.Count.Should().Be(1);

                    // Put content into session 0
                    var putResult0 = await sessions[0].PutStreamAsync(context, ContentHashType, new MemoryStream(content), Token).ShouldBeSuccess();

                    // State should be:
                    //  Local: Store1
                    //  Global: Store1, Store0
                    localResult = await store2.GetBulkAsync(context, contentHash, GetBulkOrigin.Local).ShouldBeSuccess();
                    localResult.ContentHashesInfo[0].Locations.Count.Should().Be(1);

                    globalResult = await store2.GetBulkAsync(context, contentHash, GetBulkOrigin.Global).ShouldBeSuccess();
                    globalResult.ContentHashesInfo[0].Locations.Count.Should().Be(2);

                    // Place on session 2
                    await sessions[2].PlaceFileAsync(
                        context,
                        contentHash,
                        context.Directories[0].Path / "randomfile",
                        FileAccessMode.Write,
                        FileReplacementMode.ReplaceExisting,
                        FileRealizationMode.Copy,
                        Token).ShouldBeSuccess();
                });
        }

        [Fact]
        public async Task LocalDatabaseReplicationWithLocalDiskCentralStoreTest()
        {
            ConfigureWithOneMaster();

            await RunTestAsync(
                new Context(Logger),
                3,
                async context =>
                {
                    var sessions = context.Sessions;

                    var worker = context.GetFirstWorker();
                    var master = context.GetMaster();

                    // Insert random file in session 0
                    var putResult0 = await sessions[0].PutRandomAsync(context, ContentHashType, false, ContentByteCount, Token).ShouldBeSuccess();

                    // Content should be available in session 0
                    var masterLocalResult = await master.GetBulkAsync(
                        context,
                        new[] { putResult0.ContentHash },
                        Token,
                        UrgencyHint.Nominal,
                        GetBulkOrigin.Local).ShouldBeSuccess();
                    masterLocalResult.ContentHashesInfo[0].Locations.Should().NotBeNullOrEmpty();

                    // Making sure that the data exists in the first session but not in the second
                    var workerLocalResult = await worker.GetBulkAsync(
                        context,
                        new[] { putResult0.ContentHash },
                        Token,
                        UrgencyHint.Nominal,
                        GetBulkOrigin.Local).ShouldBeSuccess();
                    workerLocalResult.ContentHashesInfo[0].Locations.Should().BeNullOrEmpty();

                    TestClock.UtcNow += TimeSpan.FromMinutes(20);

                    await UploadCheckpointOnMasterAndRestoreOnWorkers(context);

                    // Now the data should be in the second session.
                    var workerLocalResult1 = await worker.GetBulkAsync(
                        context,
                        new[] { putResult0.ContentHash },
                        Token,
                        UrgencyHint.Nominal,
                        GetBulkOrigin.Local).ShouldBeSuccess();
                    workerLocalResult1.ContentHashesInfo[0].Locations.Should().NotBeNullOrEmpty();

                    // Ensure content is pulled from peers since distributed central storage is enabled
                    worker.LocalLocationStore.DistributedCentralStorage.Counters[CentralStorageCounters.TryGetFileFromPeerSucceeded].Value.Should().BeGreaterThan(0);
                    worker.LocalLocationStore.DistributedCentralStorage.Counters[CentralStorageCounters.TryGetFileFromFallback].Value.Should().Be(0);
                });
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task LocalDatabaseReplicationWithMasterSelectionTest(bool useIncrementalCheckpointing)
        {
            var centralStoreConfiguration = new LocalDiskCentralStoreConfiguration(TestRootDirectoryPath / "centralstore", Guid.NewGuid().ToString());

            var masterLeaseExpiryTime = TimeSpan.FromMinutes(3);

            ConfigureRocksDbContentLocationBasedTest(
                configureInMemoryEventStore: true,
                (index, testRootDirectory, config) =>
                {
                    config.Checkpoint = new CheckpointConfiguration(testRootDirectory)
                    {
                        /* Set role to null to automatically choose role using master election */
                        Role = null,
                        UseIncrementalCheckpointing = useIncrementalCheckpointing,
                        CreateCheckpointInterval = TimeSpan.FromMinutes(1),
                        RestoreCheckpointInterval = TimeSpan.FromMinutes(1),
                        HeartbeatInterval = Timeout.InfiniteTimeSpan,
                        MasterLeaseExpiryTime = masterLeaseExpiryTime
                    };
                    config.CentralStore = centralStoreConfiguration;
                });

            await RunTestAsync(
                new Context(Logger),
                2,
                async context =>
                {
                    var sessions = context.Sessions;

                    var ls0 = context.GetLocationStore(0);
                    var ls1 = context.GetLocationStore(1);

                    var lls0 = context.GetLocalLocationStore(0);
                    var lls1 = context.GetLocalLocationStore(1);

                    // Machines must acquire role on startup
                    Assert.True(lls0.CurrentRole != null);
                    Assert.True(lls1.CurrentRole != null);

                    // One of the machines must acquire the master role
                    Assert.True(lls0.CurrentRole == Role.Master || lls1.CurrentRole == Role.Master);

                    // One of the machines should be a worker (i.e. only one master is allowed)
                    Assert.True(lls0.CurrentRole == Role.Worker || lls1.CurrentRole == Role.Worker);

                    var masterRedisStore = lls0.CurrentRole == Role.Master ? ls0 : ls1;
                    var workerRedisStore = lls0.CurrentRole == Role.Master ? ls1 : ls0;

                    long diff<TEnum>(CounterCollection<TEnum> c1, CounterCollection<TEnum> c2, TEnum name)
                        where TEnum : struct => c1[name].Value - c2[name].Value;

                    for (int i = 0; i < 5; i++)
                    {
                        var masterCounters = masterRedisStore.LocalLocationStore.Counters.Snapshot();
                        var workerCounters = workerRedisStore.LocalLocationStore.Counters.Snapshot();

                        // Insert random file in session 0
                        var putResult0 = await sessions[0].PutRandomAsync(context, ContentHashType, false, ContentByteCount, Token).ShouldBeSuccess();

                        // Content should be available in session 0
                        var masterResult = await masterRedisStore.GetBulkAsync(
                            context,
                            new[] { putResult0.ContentHash },
                            Token,
                            UrgencyHint.Nominal,
                            GetBulkOrigin.Local).ShouldBeSuccess();
                        masterResult.ContentHashesInfo[0].Locations.Should().NotBeEmpty();

                        // Making sure that the data exists in the master session but not in the worker
                        var workerResult = await workerRedisStore.GetBulkAsync(
                            context,
                            new[] { putResult0.ContentHash },
                            Token,
                            UrgencyHint.Nominal,
                            GetBulkOrigin.Local).ShouldBeSuccess();
                        workerResult.ContentHashesInfo[0].Locations.Should().BeNullOrEmpty();

                        TestClock.UtcNow += TimeSpan.FromMinutes(2);
                        TestClock.UtcNow += TimeSpan.FromMinutes(masterLeaseExpiryTime.TotalMinutes / 2);

                        // Save checkpoint by heartbeating master
                        await masterRedisStore.LocalLocationStore.HeartbeatAsync(context).ShouldBeSuccess();

                        // Verify file was uploaded
                        // Verify file was skipped (if not first iteration)

                        // Restore checkpoint by  heartbeating worker
                        await workerRedisStore.LocalLocationStore.HeartbeatAsync(context).ShouldBeSuccess();

                        if (useIncrementalCheckpointing)
                        {
                            // Files should be uploaded by master and downloaded by worker
                            diff(masterRedisStore.LocalLocationStore.Counters, masterCounters, ContentLocationStoreCounters.IncrementalCheckpointFilesUploaded).Should().BePositive();
                            diff(workerRedisStore.LocalLocationStore.Counters, workerCounters, ContentLocationStoreCounters.IncrementalCheckpointFilesDownloaded).Should().BePositive();

                            if (i != 0)
                            {
                                // Prior files should be skipped on subsequent iterations
                                diff(masterRedisStore.LocalLocationStore.Counters, masterCounters, ContentLocationStoreCounters.IncrementalCheckpointFilesUploadSkipped).Should().BePositive();
                                diff(workerRedisStore.LocalLocationStore.Counters, workerCounters, ContentLocationStoreCounters.IncrementalCheckpointFilesDownloadSkipped).Should().BePositive();
                            }
                        }

                        // Master should retain its role since the lease expiry time has not elapsed
                        Assert.Equal(Role.Master, masterRedisStore.LocalLocationStore.CurrentRole);
                        Assert.Equal(Role.Worker, workerRedisStore.LocalLocationStore.CurrentRole);

                        // Now the data should be in the worker session.
                        workerResult = await workerRedisStore.GetBulkAsync(
                            context,
                            new[] { putResult0.ContentHash },
                            Token,
                            UrgencyHint.Nominal,
                            GetBulkOrigin.Local).ShouldBeSuccess();
                        workerResult.ContentHashesInfo[0].Locations.Should().NotBeNullOrEmpty();
                    }

                    // Roles should be retained if heartbeat happen within lease expiry window
                    TestClock.UtcNow += TimeSpan.FromMinutes(masterLeaseExpiryTime.TotalMinutes / 2);
                    await workerRedisStore.LocalLocationStore.HeartbeatAsync(context).ShouldBeSuccess();
                    await masterRedisStore.LocalLocationStore.HeartbeatAsync(context).ShouldBeSuccess();
                    Assert.Equal(Role.Worker, workerRedisStore.LocalLocationStore.CurrentRole);
                    Assert.Equal(Role.Master, masterRedisStore.LocalLocationStore.CurrentRole);

                    // Increment the time to ensure master lease expires
                    // then heartbeat worker first to ensure it steals the lease
                    // Master heartbeat trigger it to become a worker since the other
                    // machine will
                    TestClock.UtcNow += masterLeaseExpiryTime;
                    TestClock.UtcNow += TimeSpan.FromMinutes(masterLeaseExpiryTime.TotalMinutes * 2);
                    await workerRedisStore.LocalLocationStore.HeartbeatAsync(context).ShouldBeSuccess();
                    await masterRedisStore.LocalLocationStore.HeartbeatAsync(context).ShouldBeSuccess();

                    // Worker should steal master role since it h
                    // Worker should steal master role since it has expired
                    Assert.Equal(Role.Master, workerRedisStore.LocalLocationStore.CurrentRole);
                    Assert.Equal(Role.Worker, masterRedisStore.LocalLocationStore.CurrentRole);

                    // Test releasing role
                    await workerRedisStore.LocalLocationStore.ReleaseRoleIfNecessaryAsync(context);
                    Assert.Equal(null, workerRedisStore.LocalLocationStore.CurrentRole);

                    // Master redis store should now be able to reacquire master role
                    await masterRedisStore.LocalLocationStore.HeartbeatAsync(context).ShouldBeSuccess();
                    Assert.Equal(Role.Master, masterRedisStore.LocalLocationStore.CurrentRole);
                });
        }

        [Fact]
        public async Task EventStreamContentLocationStoreBasicTests()
        {
            ConfigureWithOneMaster();

            await RunTestAsync(
                new Context(Logger),
                3,
                async context =>
                {
                    var sessions = context.Sessions;

                    var worker = context.GetFirstWorker();
                    var master = context.GetMaster();

                    // Insert random file in session 0
                    var putResult0 = await sessions[0].PutRandomAsync(context, ContentHashType, false, ContentByteCount, Token).ShouldBeSuccess();

                    var workerResult = await worker.GetBulkAsync(
                        context,
                        new[] { putResult0.ContentHash },
                        Token,
                        UrgencyHint.Nominal,
                        GetBulkOrigin.Local).ShouldBeSuccess();
                    workerResult.ContentHashesInfo[0].Locations.Should().BeNullOrEmpty("Worker should not have the content.");

                    var masterResult = await master.GetBulkAsync(
                        context,
                        new[] { putResult0.ContentHash },
                        Token,
                        UrgencyHint.Nominal,
                        GetBulkOrigin.Local).ShouldBeSuccess();
                    masterResult.ContentHashesInfo[0].Locations.Count.Should().Be(1, "Master should receive an event and add the content to local store");

                    masterResult = await master.GetBulkAsync(
                        context,
                        new[] { putResult0.ContentHash },
                        Token,
                        UrgencyHint.Nominal,
                        GetBulkOrigin.Global).ShouldBeSuccess();
                    masterResult.ContentHashesInfo[0].Locations.Count.Should().Be(1, "Master should be able to get the content from the global store");

                    await UploadCheckpointOnMasterAndRestoreOnWorkers(context);

                    workerResult = await worker.GetBulkAsync(
                        context,
                        new[] { putResult0.ContentHash },
                        Token,
                        UrgencyHint.Nominal,
                        GetBulkOrigin.Local).ShouldBeSuccess();
                    workerResult.ContentHashesInfo[0].Locations.Should().NotBeNullOrEmpty("Worker should get the content in local database after sync");

                    // Remove the location from backing content location store so that in the absence of pin caching the
                    // result of pin should be false.
                    await master.TrimBulkAsync(
                        context,
                        masterResult.ContentHashesInfo.Select(c => c.ContentHash).ToList(),
                        Token,
                        UrgencyHint.Nominal).ShouldBeSuccess();

                    await UploadCheckpointOnMasterAndRestoreOnWorkers(context);

                    // Verify no locations for the content
                    workerResult = await worker.GetBulkAsync(
                        context,
                        new[] { putResult0.ContentHash },
                        Token,
                        UrgencyHint.Nominal,
                        GetBulkOrigin.Local).ShouldBeSuccess();
                    workerResult.ContentHashesInfo[0].Locations.Should().BeNullOrEmpty();

                    // Verify no locations for the content
                    masterResult = await master.GetBulkAsync(
                        context,
                        new[] { putResult0.ContentHash },
                        Token,
                        UrgencyHint.Nominal,
                        GetBulkOrigin.Global).ShouldBeSuccess();
                    masterResult.ContentHashesInfo[0].Locations.Should().NotBeNullOrEmpty("With LLS only mode, content is not eagerly removed from Redis.");

                    masterResult = await master.GetBulkAsync(
                        context,
                        new[] { putResult0.ContentHash },
                        Token,
                        UrgencyHint.Nominal,
                        GetBulkOrigin.Local).ShouldBeSuccess();
                    masterResult.ContentHashesInfo[0].Locations.Should().BeNullOrEmpty("The result should not be available in LLS.");
                });
        }

        private static void CopyDirectory(string sourceRoot, string destinationRoot, bool overwriteExistingFiles = false)
        {
            sourceRoot = sourceRoot.TrimEnd('\\');
            destinationRoot = destinationRoot.TrimEnd('\\');

            var allFiles = Directory
                .GetFiles(sourceRoot, "*.*", SearchOption.AllDirectories);
            foreach (var file in allFiles)
            {
                var destinationFileName = Path.Combine(destinationRoot, file.Substring(sourceRoot.Length + 1));
                if (File.Exists(destinationFileName) && !overwriteExistingFiles)
                {
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destinationFileName));
                File.Copy(file, destinationFileName);
                File.SetAttributes(destinationFileName, File.GetAttributes(destinationFileName) & ~FileAttributes.ReadOnly);
            }
        }

        [Fact(Skip = "Diagnostic purposes only")]
        public async Task TestDistributedEviction()
        {
            var testDbPath = new AbsolutePath(@"ADD PATH TO LLS DB HERE");
            _testDatabasePath = TestRootDirectoryPath / "tempdb";
            CopyDirectory(testDbPath.Path, _testDatabasePath.Path);

            var contentDirectoryPath = new AbsolutePath(@"ADD PATH TO CONTENT DIRECTORY HERE");
            ConfigureWithOneMaster();

            await RunTestAsync(
                new Context(Logger),
                1,
                async context =>
                {
                    var sessions = context.Sessions;

                    var master = context.GetMaster();

                    var root = TestRootDirectoryPath / "memdir";
                    var tempDbDir = TestRootDirectoryPath / "tempdb";


                    FileSystem.CreateDirectory(root);
                    var dir = new MemoryContentDirectory(new PassThroughFileSystem(), root);

                    File.Copy(contentDirectoryPath.Path, dir.FilePath.Path, overwrite: true);
                    await dir.StartupAsync(context).ThrowIfFailure();

                    master.LocalLocationStore.OverrideMachineId = new MachineId(144);

                    var lruContent = await dir.GetLruOrderedCacheContentWithTimeAsync();

                    var tracer = context.Context;

                    tracer.Debug($"LRU content count = {lruContent.Count}");
                    int pageNumber = 0;
                    int cumulativeCount = 0;
                    long lastTime = 0;
                    long lastOriginalTime = 0;
                    HashSet<ContentHash> hashes = new HashSet<ContentHash>();
                    foreach (var page in master.GetLruPages(context, lruContent))
                    {
                        cumulativeCount += page.Count;
                        tracer.Debug($"Page {pageNumber++}: {page.Count} / {cumulativeCount}");

                        foreach (var item in page)
                        {
                            tracer.Debug($"{item}");
                            tracer.Debug($"LTO: {item.LastAccessTime.Ticks - lastTime}, LOTO: {item.OriginalLastAccessTime.Ticks - lastOriginalTime}, IsDupe: {!hashes.Add(item.ContentHash)}");

                            lastTime = item.LastAccessTime.Ticks;
                            lastOriginalTime = item.OriginalLastAccessTime.Ticks;
                        }
                    }

                    await Task.Yield();
                });
        }

        [Fact]
        public async Task DualRedundancyGlobalRedisTest()
        {
            _enableSecondaryRedis = true;
            ConfigureWithOneMaster();
            int machineCount = 3;

            await RunTestAsync(
                new Context(Logger),
                machineCount,
                async context =>
                {
                    var sessions = context.Sessions;

                    var masterSession = sessions[context.GetMasterIndex()];
                    var workerSession = sessions[context.GetFirstWorkerIndex()];
                    var master = context.GetMaster();
                    var worker = context.GetFirstWorker();

                    // Heartbeat the master to ensure cluster state is mirrored to secondary
                    TestClock.UtcNow += _configurations[0].ClusterStateMirrorInterval + TimeSpan.FromSeconds(1);
                    await master.LocalLocationStore.HeartbeatAsync(context).ShouldBeSuccess();

                    var keys = _primaryGlobalStoreDatabase.Keys.ToList();

                    // Delete cluster state from primary
                    (await _primaryGlobalStoreDatabase.KeyDeleteAsync(((RedisGlobalStore)master.LocalLocationStore.GlobalStore).FullyQualifiedClusterStateKey)).Should().BeTrue();

                    var masterClusterState = master.LocalLocationStore.ClusterState;

                    var clusterState = new ClusterState();
                    await worker.LocalLocationStore.GlobalStore.UpdateClusterStateAsync(context, clusterState).ShouldBeSuccess();

                    clusterState.MaxMachineId.Should().Be(machineCount);

                    for (int machineIndex = 1; machineIndex <= clusterState.MaxMachineId; machineIndex++)
                    {
                        var machineId = new MachineId(machineIndex);
                        clusterState.TryResolve(machineId, out var machineLocation).Should().BeTrue();
                        masterClusterState.TryResolve(machineId, out var masterResolvedMachineLocation).Should().BeTrue();
                        machineLocation.Should().BeEquivalentTo(masterResolvedMachineLocation);
                    }

                    // Ensure resiliency to removal from both primary and secondary
                    await verifyContentResiliency(_primaryGlobalStoreDatabase, _secondaryGlobalStoreDatabase);
                    await verifyContentResiliency(_secondaryGlobalStoreDatabase, _primaryGlobalStoreDatabase);

                    async Task verifyContentResiliency(LocalRedisProcessDatabase redis1, LocalRedisProcessDatabase redis2)
                    {
                        // Insert random file in session 0
                        var putResult = await masterSession.PutRandomAsync(context, ContentHashType, false, ContentByteCount, Token).ShouldBeSuccess();

                        var globalGetBulkResult = await worker.GetBulkAsync(
                            context,
                            new[] { putResult.ContentHash },
                            Token,
                            UrgencyHint.Nominal,
                            GetBulkOrigin.Global).ShouldBeSuccess();
                        globalGetBulkResult.ContentHashesInfo[0].Locations.Count.Should().Be(1, "Content should be registered with the global store");

                        // Delete key from primary database
                        (await redis1.DeleteStringKeys(s => s.Contains(RedisGlobalStore.GetRedisKey(putResult.ContentHash)))).Should().BeGreaterThan(0);

                        globalGetBulkResult = await worker.GetBulkAsync(
                            context,
                            new[] { putResult.ContentHash },
                            Token,
                            UrgencyHint.Nominal,
                            GetBulkOrigin.Global).ShouldBeSuccess();

                        globalGetBulkResult.ContentHashesInfo[0].Locations.Count.Should().Be(1, "Content should be registered with the global store since locations are backed up in other store");

                        // Delete key from secondary database
                        (await redis2.DeleteStringKeys(s => s.Contains(RedisGlobalStore.GetRedisKey(putResult.ContentHash)))).Should().BeGreaterThan(0);

                        globalGetBulkResult = await worker.GetBulkAsync(
                            context,
                            new[] { putResult.ContentHash },
                            Token,
                            UrgencyHint.Nominal,
                            GetBulkOrigin.Global).ShouldBeSuccess();
                        globalGetBulkResult.ContentHashesInfo[0].Locations.Should().BeNullOrEmpty("Content should be missing from global store after removal from both redis databases");
                    }
                });
        }

        [Fact]
        public async Task ClusterStateIsPersistedLocally()
        {
            ConfigureWithOneMaster();
            int machineCount = 3;

            await RunTestAsync(
                new Context(Logger),
                machineCount,
                async context =>
                {
                    var sessions = context.Sessions;

                    var masterSession = sessions[context.GetMasterIndex()];
                    var workerSession = sessions[context.GetFirstWorkerIndex()];
                    var master = context.GetMaster();
                    var worker = context.GetFirstWorker();

                    // Heartbeat the master to ensure cluster state is written to local db
                    TestClock.UtcNow += _configurations[0].ClusterStateMirrorInterval + TimeSpan.FromSeconds(1);
                    await master.LocalLocationStore.HeartbeatAsync(context).ShouldBeSuccess();

                    var masterClusterState = master.LocalLocationStore.ClusterState;

                    var clusterState = new ClusterState();

                    // Try populating cluster state from local db
                    master.LocalLocationStore.Database.UpdateClusterState(context, clusterState, write: false);

                    clusterState.MaxMachineId.Should().Be(machineCount);

                    for (int machineIndex = 1; machineIndex <= clusterState.MaxMachineId; machineIndex++)
                    {
                        var machineId = new MachineId(machineIndex);
                        clusterState.TryResolve(machineId, out var machineLocation).Should().BeTrue();
                        masterClusterState.TryResolve(machineId, out var masterResolvedMachineLocation).Should().BeTrue();
                        machineLocation.Should().BeEquivalentTo(masterResolvedMachineLocation);
                    }
                });
        }

        [Fact]
        public async Task GarbageCollectionTests()
        {
            ConfigureWithOneMaster();

            await RunTestAsync(
                new Context(Logger),
                3,
                async context =>
                {
                    var sessions = context.Sessions;

                    var workerSession = sessions[context.GetFirstWorkerIndex()];
                    var master = context.GetMaster();

                    // Insert random file in session 0
                    var putResult0 = await workerSession.PutRandomAsync(context, ContentHashType, false, ContentByteCount, Token).ShouldBeSuccess();

                    var masterResult = await master.GetBulkAsync(
                        context,
                        new[] { putResult0.ContentHash },
                        Token,
                        UrgencyHint.Nominal,
                        GetBulkOrigin.Local).ShouldBeSuccess();
                    masterResult.ContentHashesInfo[0].Locations.Count.Should().Be(1, "Master should receive an event and add the content to local store");

                    // Add time so worker machine is inactive
                    TestClock.UtcNow += TimeSpan.FromMinutes(20);
                    await master.LocalLocationStore.HeartbeatAsync(context).ShouldBeSuccess();

                    masterResult = await master.GetBulkAsync(
                        context,
                        new[] { putResult0.ContentHash },
                        Token,
                        UrgencyHint.Nominal,
                        GetBulkOrigin.Local).ShouldBeSuccess();
                    masterResult.ContentHashesInfo[0].Locations.Should().BeNullOrEmpty("After heartbeat, worker location should be filtered due to inactivity");

                    master.LocalLocationStore.Database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCleanedEntries].Value.Should().Be(0, "No entries should be cleaned before GC is called");
                    master.LocalLocationStore.Database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCollectedEntries].Value.Should().Be(0, "No entries should be cleaned before GC is called");

                    master.LocalLocationStore.Database.GarbageCollect(context);

                    master.LocalLocationStore.Database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCollectedEntries].Value.Should().Be(1, "After GC, the entry with only a location from the expired machine should be collected");
                });
        }

        [Fact]
        public async Task SelfEvictionTests()
        {
            ConfigureWithOneMaster();

            await RunTestAsync(
                new Context(Logger),
                3,
                async context =>
                {
                    var sessions = context.Sessions;

                    var worker0 = context.GetFirstWorker();
                    var worker1 = context.EnumerateWorkers().ElementAt(1);
                    var workerSession = sessions[context.GetFirstWorkerIndex()];
                    var workerContentStore = (IRepairStore)context.Stores[context.GetFirstWorkerIndex()];
                    var master = context.GetMaster();

                    // Insert random file in session 0
                    var putResult0 = await workerSession.PutRandomAsync(context, ContentHashType, false, ContentByteCount, Token).ShouldBeSuccess();

                    worker0.LocalLocationStore.Counters[ContentLocationStoreCounters.LocationAddRecentInactiveEager].Value.Should().Be(0);

                    var masterResult = await master.GetBulkAsync(
                        context,
                        new[] { putResult0.ContentHash },
                        Token,
                        UrgencyHint.Nominal,
                        GetBulkOrigin.Local).ShouldBeSuccess();
                    masterResult.ContentHashesInfo[0].Locations.Count.Should().Be(1, "Master should receive an event and add the content to local store");

                    // Add time so machine recomputes inactive machines
                    TestClock.UtcNow += TimeSpan.FromSeconds(1);

                    // Call heartbeat first to ensure last heartbeat time is up to date but then call remove from tracker to ensure marked unavailable
                    await worker0.LocalLocationStore.HeartbeatAsync(context).ShouldBeSuccess();
                    await workerContentStore.RemoveFromTrackerAsync(context).ShouldBeSuccess();

                    // Heartbeat the master to ensure set of inactive machines is updated
                    await master.LocalLocationStore.HeartbeatAsync(context).ShouldBeSuccess();

                    masterResult = await master.GetBulkAsync(
                        context,
                        new[] { putResult0.ContentHash },
                        Token,
                        UrgencyHint.Nominal,
                        GetBulkOrigin.Local).ShouldBeSuccess();
                    masterResult.ContentHashesInfo[0].Locations.Should().BeNullOrEmpty("After heartbeat, worker location should be filtered due to inactivity");

                    master.LocalLocationStore.Database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCleanedEntries].Value.Should().Be(0, "No entries should be cleaned before GC is called");
                    master.LocalLocationStore.Database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCollectedEntries].Value.Should().Be(0, "No entries should be cleaned before GC is called");

                    master.LocalLocationStore.Database.FlushIfEnabled(context);

                    master.LocalLocationStore.Database.GarbageCollect(context);

                    master.LocalLocationStore.Database.Counters[ContentLocationDatabaseCounters.TotalNumberOfCollectedEntries].Value.Should().Be(1, "After GC, the entry with only a location from the expired machine should be collected");

                    // Heartbeat worker to switch back to active state
                    await worker0.LocalLocationStore.HeartbeatAsync(context).ShouldBeSuccess();

                    // Insert random file in session 0
                    var putResult1 = await workerSession.PutRandomAsync(context, ContentHashType, false, ContentByteCount, Token).ShouldBeSuccess();

                    worker0.LocalLocationStore.Counters[ContentLocationStoreCounters.LocationAddRecentInactiveEager].Value.Should().Be(1, "Putting content after inactivity should eagerly go to global store.");

                    var worker1GlobalResult = await worker1.GetBulkAsync(
                        context,
                        new[] { putResult1.ContentHash },
                        Token,
                        UrgencyHint.Nominal,
                        GetBulkOrigin.Global).ShouldBeSuccess();
                    worker1GlobalResult.ContentHashesInfo[0].Locations.Should()
                        .NotBeNullOrEmpty("Putting content on worker 0 after inactivity should eagerly go to global store.");

                },
                enableRepairHandling: true);
        }

        [Fact]
        public async Task EventStreamContentLocationStoreEventHubBasicTests()
        {
            var centralStoreConfiguration = new LocalDiskCentralStoreConfiguration(TestRootDirectoryPath / "centralstore", Guid.NewGuid().ToString());

            if (!ConfigureRocksDbContentLocationBasedTestWithEventHub(
                (index, testRootDirectory, config, storageConnectionString) =>
                {
                    var role = index == 0 ? Role.Master : Role.Worker;
                    config.Checkpoint = new CheckpointConfiguration(testRootDirectory)
                    {
                        CreateCheckpointInterval = TimeSpan.FromMinutes(1),
                        RestoreCheckpointInterval = TimeSpan.FromMinutes(1),
                        HeartbeatInterval = Timeout.InfiniteTimeSpan,
                        Role = role,
                    };

                    config.CentralStore = centralStoreConfiguration;
                }))
            {
                // Test is misconfigured.
                Output.WriteLine("The test is skipped.");
                return;
            }

            // TODO: How to wait for events?
            const int EventPropagationDelayMs = 5000;

            await RunTestAsync(
                new Context(Logger),
                3,
                async context =>
                {
                    // Here is the user scenario that the test verifies:
                    // Setup:
                    //   - Session0: EH (master) + RocksDb
                    //   - Session1: EH (worker) + RocksDb
                    //   - Session2: EH (worker) + RocksDb
                    //
                    // 1. Put a location into Worker1
                    // 2. Get a local location from Master0. Location should exists in a local database, because master synchronizes events eagerly.
                    // 3. Get a local location from Worker2. Location SHOULD NOT exists in a local database, because worker does not receive events eagerly.
                    // 4. Remove the location from Worker1
                    // 5. Get a local location from Master0 (should not exists)
                    // 6. Get a local location from Worker2 (should still exists).
                    var sessions = context.Sessions;

                    var master0 = context.GetMaster();
                    var worker1Session = sessions[context.GetFirstWorkerIndex()];
                    var worker1 = context.EnumerateWorkers().ElementAt(0);
                    var worker2 = context.EnumerateWorkers().ElementAt(1);

                    // Only session0 is a master. So we need to put a location into a worker session and check that master received a sync event.
                    var putResult0 = await worker1Session.PutRandomAsync(context, ContentHashType, false, ContentByteCount, Token).ShouldBeSuccess();

                    await Task.Delay(EventPropagationDelayMs);

                    // Content SHOULD be registered locally for master.
                    var localGetBulkResultMaster = await master0.GetBulkAsync(context, putResult0.ContentHash, GetBulkOrigin.Local).ShouldBeSuccess();
                    localGetBulkResultMaster.ContentHashesInfo[0].Locations.Should().NotBeNullOrEmpty();

                    // Content SHOULD NOT be registered locally for the second worker, because it does not receive events eagerly.
                    var localGetBulkResultWorker2 = await worker2.GetBulkAsync(context, putResult0.ContentHash, GetBulkOrigin.Local).ShouldBeSuccess();
                    localGetBulkResultWorker2.ContentHashesInfo[0].Locations.Should().BeNullOrEmpty();

                    // Content SHOULD be available globally via the second worker
                    var globalGetBulkResult1 = await worker2.GetBulkAsync(context, putResult0.ContentHash, GetBulkOrigin.Global).ShouldBeSuccess();
                    globalGetBulkResult1.ContentHashesInfo[0].Locations.Should().NotBeNullOrEmpty();

                    // Remove the location from backing content location store so that in the absence of pin caching the
                    // result of pin should be false.
                    await worker1.TrimBulkAsync(
                        context,
                        globalGetBulkResult1.ContentHashesInfo.Select(c => c.ContentHash).ToList(),
                        Token,
                        UrgencyHint.Nominal).ShouldBeSuccess();

                    await Task.Delay(EventPropagationDelayMs);

                    // Verify no locations for the content
                    var postLocalTrimGetBulkResult0a = await master0.GetBulkAsync(context, putResult0.ContentHash, GetBulkOrigin.Local).ShouldBeSuccess();
                    postLocalTrimGetBulkResult0a.ContentHashesInfo[0].Locations.Should().BeNullOrEmpty();
                });
        }

        // TODO: add a test case to cover different epochs
        // TODO: run tests against event hub automatically

        [Fact]
        public async Task EventStreamContentLocationStoreEventHubWithBlobStorageBasedCentralStore()
        {
            var centralStoreConfiguration = new LocalDiskCentralStoreConfiguration(TestRootDirectoryPath / "centralstore", Guid.NewGuid().ToString());
            var checkpointsKey = Guid.NewGuid().ToString();

            if (!ConfigureRocksDbContentLocationBasedTestWithEventHub(
                (index, testRootDirectory, config, storageConnectionString) =>
                {
                    var role = index == 0 ? Role.Master : Role.Worker;
                    config.Checkpoint = new CheckpointConfiguration(testRootDirectory)
                    {
                        CreateCheckpointInterval = TimeSpan.FromMinutes(1),
                        RestoreCheckpointInterval = TimeSpan.FromMinutes(1),
                        HeartbeatInterval = Timeout.InfiniteTimeSpan,
                        Role = role,
                    };
                    config.CentralStore = centralStoreConfiguration;

                    config.CentralStore = new BlobCentralStoreConfiguration(
                                              connectionString: storageConnectionString,
                                              containerName: "checkpointscontainer",
                                              checkpointsKey: checkpointsKey)
                    {
                        RetentionTime = TimeSpan.FromMinutes(1)
                    };
                }))
            {
                // Test is misconfigured.
                Output.WriteLine("The test is skipped.");
                return;
            }

            // TODO: How to wait for events?
            const int EventPropagationDelayMs = 5000;

            await RunTestAsync(
                new Context(Logger),
                4,
                async context =>
                {
                    // Here is the user scenario that the test verifies:
                    // Setup:
                    //   - Session0: EH (master) + RocksDb
                    //   - Session1: EH (master) + RocksDb
                    //   - Session2: EH (worker) + RocksDb
                    //   - Session3: EH (worker) + RocksDb
                    //
                    // 1. Put a location into Worker1
                    // 2. Get a local location from Master0. Location should exist in a local database, because master synchronizes events eagerly.
                    // 3. Get a local location from Worker2. Location SHOULD NOT exist in a local database, because worker does not receive events eagerly.
                    // 4. Force checkpoint creation, by triggering heartbeat on Master0
                    // 5. Get checkpoint on Worker2, by triggering heartbeat on Worker2
                    // 6. Get a local location from Worker2. LOcation should exist in local database, because database was updated with new checkpoint
                    var sessions = context.Sessions;

                    var master0 = context.GetLocationStore(0);
                    var master1 = context.GetLocationStore(1);
                    var worker2 = context.GetLocationStore(2);

                    // Only session0 is a master. So we need to put a location into a worker session and check that master received a sync event.
                    var putResult0 = await sessions[1].PutRandomAsync(context, ContentHashType, false, ContentByteCount, Token).ShouldBeSuccess();

                    await Task.Delay(EventPropagationDelayMs);

                    // Content SHOULD be registered locally for master.
                    var localGetBulkResultMaster = await master0.GetBulkAsync(context, putResult0.ContentHash, GetBulkOrigin.Local).ShouldBeSuccess();
                    localGetBulkResultMaster.ContentHashesInfo[0].Locations.Should().NotBeNullOrEmpty();

                    // Content SHOULD NOT be registered locally for the second worker, because it does not receive events eagerly.
                    var localGetBulkResultWorker2 = await worker2.GetBulkAsync(context, putResult0.ContentHash, GetBulkOrigin.Local).ShouldBeSuccess();
                    localGetBulkResultWorker2.ContentHashesInfo[0].Locations.Should().BeNullOrEmpty();

                    // Content SHOULD be available globally via the second worker
                    var globalGetBulkResult1 = await worker2.GetBulkAsync(context, putResult0.ContentHash, GetBulkOrigin.Global).ShouldBeSuccess();
                    globalGetBulkResult1.ContentHashesInfo[0].Locations.Should().NotBeNullOrEmpty();

                    TestClock.UtcNow += TimeSpan.FromMinutes(2);
                    await master0.LocalLocationStore.HeartbeatAsync(context).ShouldBeSuccess();
                    await master1.LocalLocationStore.HeartbeatAsync(context).ShouldBeSuccess();
                    await worker2.LocalLocationStore.HeartbeatAsync(context).ShouldBeSuccess();

                    // Waiting for some time to make the difference between entry insertion time and the touch time that updates it.
                    TestClock.UtcNow += TimeSpan.FromMinutes(2);

                    // Content SHOULD be available local via the WORKER 2 after downloading checkpoint (touches content)
                    var localGetBulkResultWorker2b = await worker2.GetBulkAsync(context, putResult0.ContentHash, GetBulkOrigin.Local).ShouldBeSuccess();
                    localGetBulkResultWorker2b.ContentHashesInfo[0].Locations.Should().NotBeNullOrEmpty();

                    // Waiting for events to be propagated from the worker to the master
                    await Task.Delay(EventPropagationDelayMs);

                    // TODO[LLS]: change it or remove completely. (bug 1365340)
                    // Waiting for another 2 minutes before triggering the GC
                    //TestClock.UtcNow += TimeSpan.FromMinutes(2);
                    //((RocksDbContentLocationDatabase)master1.Database).GarbageCollect(context);

                    //// 4 minutes already passed after the entry insertion. It means that the entry should be collected unless touch updates the entry
                    //// Master1 still should have an entry in a local database
                    //localGetBulkResultMaster1 = await master1.GetBulkAsync(context, putResult0.ContentHash, GetBulkOrigin.Local).ShouldBeSuccess();
                    //Assert.True(localGetBulkResultMaster1.ContentHashesInfo[0].Locations.Count == 1);

                    //// Waiting for another 2 minutes forcing the entry to fall out of the local database
                    //TestClock.UtcNow += TimeSpan.FromMinutes(2);
                    //((RocksDbContentLocationDatabase)master1.Database).GarbageCollect(context);

                    //localGetBulkResultMaster1 = await master1.GetBulkAsync(context, putResult0.ContentHash, GetBulkOrigin.Local).ShouldBeSuccess();
                    //Assert.True(localGetBulkResultMaster1.ContentHashesInfo[0].Locations.NullOrEmpty());
                });
        }

        private static async Task<ContentHash> PutContentInSession0_PopulateSession1LocalDb_RemoveContentFromGlobalStore(
            IList<IContentSession> sessions,
            Context context,
            TransitioningContentLocationStore worker,
            TransitioningContentLocationStore master)
        {
            // Insert random file in session 0
            var putResult0 = await sessions[0].PutRandomAsync(context, ContentHashType, false, ContentByteCount, Token).ShouldBeSuccess();

            // Content SHOULD NOT be registered locally since it has not been queried
            var localGetBulkResult1a = await worker.GetBulkAsync(
                context,
                putResult0.ContentHash,
                GetBulkOrigin.Local).ShouldBeSuccess();
            localGetBulkResult1a.ContentHashesInfo[0].Locations.Should().BeNullOrEmpty();

            var globalGetBulkResult1 = await worker.GetBulkAsync(
                context,
                new[] { putResult0.ContentHash },
                Token,
                UrgencyHint.Nominal,
                GetBulkOrigin.Global).ShouldBeSuccess();
            globalGetBulkResult1.ContentHashesInfo[0].Locations.Should().NotBeNullOrEmpty();

            // Content SHOULD be registered locally since it HAS been queried as a result of GetBulk with GetBulkOrigin.Global
            var localGetBulkResult1b = await master.GetBulkAsync(
                context,
                putResult0.ContentHash,
                GetBulkOrigin.Local).ShouldBeSuccess();
            localGetBulkResult1b.ContentHashesInfo[0].Locations.Should().NotBeNullOrEmpty();

            // Remove the location from backing content location store so that in the absence of pin caching the
            // result of pin should be false.
            await worker.TrimBulkAsync(
                context,
                globalGetBulkResult1.ContentHashesInfo.Select(c => c.ContentHash).ToList(),
                Token,
                UrgencyHint.Nominal).ShouldBeSuccess();

            // Verify no locations for the content
            var postTrimGetBulkResult = await master.GetBulkAsync(
                context, putResult0.ContentHash,
                GetBulkOrigin.Global).ShouldBeSuccess();
            postTrimGetBulkResult.ContentHashesInfo[0].Locations.Should().NotBeNullOrEmpty("TrimBulkAsync does not clean global store.");
            return putResult0.ContentHash;
        }

        private async Task UploadCheckpointOnMasterAndRestoreOnWorkers(TestContext context)
        {
            // Update time to trigger checkpoint upload and restore on master and workers respectively
            TestClock.UtcNow += TimeSpan.FromMinutes(2);

            var masterStore = context.GetMaster();

            // Heartbeat master first to upload checkpoint
            await masterStore.LocalLocationStore.HeartbeatAsync(context).ShouldBeSuccess();

            // Next heartbeat workers to restore checkpoint
            foreach (var workerStore in context.EnumerateWorkers())
            {
                await workerStore.LocalLocationStore.HeartbeatAsync(context).ShouldBeSuccess();
            }
        }

        protected void ConfigureWithOneMaster(CentralStoreConfiguration centralStoreConfiguration = null)
        {
            centralStoreConfiguration = centralStoreConfiguration ?? new LocalDiskCentralStoreConfiguration(TestRootDirectoryPath / "centralstore", Guid.NewGuid().ToString());

            // Extremely hacky way to make the first redis instance to be a master and the rest to be workers.
            ConfigureRocksDbContentLocationBasedTest(
                configureInMemoryEventStore: true,
                (index, testRootDirectory, config) =>
                {
                    var role = index == 0 ? Role.Master : Role.Worker;
                    config.Checkpoint = new CheckpointConfiguration(testRootDirectory)
                    {
                        CreateCheckpointInterval = TimeSpan.FromMinutes(1),
                        RestoreCheckpointInterval = TimeSpan.FromMinutes(1),
                        HeartbeatInterval = Timeout.InfiniteTimeSpan,
                        Role = role,
                    };
                    config.CentralStore = centralStoreConfiguration;
                });
        }

        private static readonly TimeSpan LocalDatabaseEntryTimeToLive = TimeSpan.FromMinutes(3);
        private const int SafeToLazilyUpdateMachineCountThreshold = 3;
        private const int ReplicaCreditInMinutes = 3;
        protected bool _enableReconciliation;
        private ContentLocationMode _readMode = ContentLocationMode.LocalLocationStore;
        private ContentLocationMode _writeMode = ContentLocationMode.LocalLocationStore;
        private bool _enableSecondaryRedis = false;
        private AbsolutePath _testDatabasePath = null;

        private RedisContentLocationStoreConfiguration CreateRedisContentLocationStoreConfiguration(
            AbsolutePath storeLocationRoot,
            ContentLocationEventStoreConfiguration eventStoreConfiguration = null)
        {
            return new RedisContentLocationStoreConfiguration()
            {
                MachineExpiry = TimeSpan.FromMinutes(10),
                EnableReconciliation = _enableReconciliation,
                InlinePostInitialization = true,
                ReplicaPenaltyInMinutes = ReplicaCreditInMinutes,

                // Set recompute time to zero to force recomputation on every heartbeat
                RecomputeInactiveMachinesExpiry = TimeSpan.Zero,
                Database =
                           new RocksDbContentLocationDatabaseConfiguration(storeLocationRoot / "rocksdb")
                           {
                               // Don't GC
                               LocalDatabaseGarbageCollectionInterval = Timeout.InfiniteTimeSpan,
                               TestInitialCheckpointPath = _testDatabasePath,

                           },
                CentralStore = new LocalDiskCentralStoreConfiguration(storeLocationRoot, "chkpoints"),
                SafeToLazilyUpdateMachineCountThreshold = SafeToLazilyUpdateMachineCountThreshold,
                Checkpoint = new CheckpointConfiguration(storeLocationRoot)
                {
                    HeartbeatInterval = TimeSpan.FromMinutes(1),
                },
                EventStore = eventStoreConfiguration ?? new MemoryContentLocationEventStoreConfiguration(),
                ReadMode = _readMode,
                WriteMode = _writeMode,
            };
        }

        private void ConfigureRocksDbContentLocationBasedTest(bool configureInMemoryEventStore = false, Action<int, AbsolutePath, RedisContentLocationStoreConfiguration> configurationPostProcessor = null, bool configurePin = true, AbsolutePath overrideContentLocationStoreDirectory = null)
        {
            var eventStoreConfiguration = configureInMemoryEventStore ? new MemoryContentLocationEventStoreConfiguration() : null;
            CreateContentLocationStoreConfiguration = (testRootDirectory, index) =>
            {
                var result = CreateRedisContentLocationStoreConfiguration(
                    overrideContentLocationStoreDirectory ?? testRootDirectory,
                    eventStoreConfiguration);

                configurationPostProcessor?.Invoke(index, testRootDirectory, result);
                return result;
            };

            if (configurePin)
            {
                PinConfiguration = new PinConfiguration()
                {
                    // Low risk and high risk tolerance for machine or file loss to prevent pin better from kicking in
                    MachineRisk = 0.0000001,
                    FileRisk = 0.0000001,
                    PinRisk = 0.9999,
                    UsePinCache = false
                };

                ContentAvailabilityGuarantee = ReadOnlyDistributedContentSession<AbsolutePath>.ContentAvailabilityGuarantee.RedundantFileRecordsOrCheckFileExistence;
            }
        }

        private bool ConfigureRocksDbContentLocationBasedTestWithEventHub(Action<int, AbsolutePath, RedisContentLocationStoreConfiguration, string> configurationPostProcessor)
        {
            if (!ReadConfiguration(out var storageAccountKey, out var storageAccountName, out var eventHubConnectionString, out var eventHubName))
            {
                return false;
            }

            string storageConnectionString = $"DefaultEndpointsProtocol=https;AccountName={storageAccountName};AccountKey={storageAccountKey}";
            var eventStoreConfiguration = new EventHubContentLocationEventStoreConfiguration(
                                              eventHubName: eventHubName,
                                              eventHubConnectionString:
                                              eventHubConnectionString,
                                              consumerGroupName: "$Default",
                                              epoch: Guid.NewGuid().ToString())
            {
                // Send events eagerly as they come in.
                EventBatchSize = 1,
                Epoch = null,
                MaxEventProcessingConcurrency = 2,
            };
            CreateContentLocationStoreConfiguration = (testRootDirectory, index) =>
            {
                var result = CreateRedisContentLocationStoreConfiguration(
                    testRootDirectory,
                    eventStoreConfiguration);

                configurationPostProcessor?.Invoke(index, testRootDirectory, result, storageConnectionString);
                return result;
            };

            PinConfiguration = new PinConfiguration()
            {
                // Low risk and high risk tolerance for machine or file loss to prevent pin better from kicking in
                MachineRisk = 0.0000001,
                FileRisk = 0.0000001,
                PinRisk = 0.9999,
                UsePinCache = false
            };

            ContentAvailabilityGuarantee = ReadOnlyDistributedContentSession<AbsolutePath>.ContentAvailabilityGuarantee.FileRecordsExist;
            return true;
        }

        private bool ReadConfiguration(out string storageAccountKey, out string storageAccountName, out string eventHubConnectionString, out string eventHubName)
        {
            storageAccountKey = Environment.GetEnvironmentVariable("TestEventHub_StorageAccountKey");
            storageAccountName = Environment.GetEnvironmentVariable("TestEventHub_StorageAccountName");
            eventHubConnectionString = Environment.GetEnvironmentVariable("TestEventHub_EventHubConnectionString");
            eventHubName = Environment.GetEnvironmentVariable("TestEventHub_EventHubName");

            if (storageAccountKey == null)
            {
                Output.WriteLine("Please specify 'TestEventHub_StorageAccountKey' to run this test");
                return false;
            }

            if (storageAccountName == null)
            {
                Output.WriteLine("Please specify 'TestEventHub_StorageAccountName' to run this test");
                return false;
            }

            if (eventHubConnectionString == null)
            {
                Output.WriteLine("Please specify 'TestEventHub_EventHubConnectionString' to run this test");
                return false;
            }

            if (eventHubName == null)
            {
                Output.WriteLine("Please specify 'TestEventHub_EventHubName' to run this test");
                return false;
            }

            Output.WriteLine("The test is configured correctly.");
            return true;
        }

        [Fact(Skip = "For manual usage only")]
        public async Task MultiThreadedStressTestRocksDbContentLocationDatabaseOnNewEntries()
        {
            bool useIncrementalCheckpointing = true;
            int numberOfMachines = 100;
            int addsPerMachine = 25000;
            int maximumBatchSize = 1000;
            int warmupBatches = 10000;

            var centralStoreConfiguration = new LocalDiskCentralStoreConfiguration(TestRootDirectoryPath / "centralstore", Guid.NewGuid().ToString());
            var masterLeaseExpiryTime = TimeSpan.FromMinutes(3);

            MemoryContentLocationEventStoreConfiguration memoryContentLocationEventStore = null;
            ConfigureRocksDbContentLocationBasedTest(
                configureInMemoryEventStore: true,
                (index, testRootDirectory, config) =>
                {
                    config.Checkpoint = new CheckpointConfiguration(testRootDirectory)
                    {
                        /* Set role to null to automatically choose role using master election */
                        Role = null,
                        UseIncrementalCheckpointing = useIncrementalCheckpointing,
                        CreateCheckpointInterval = TimeSpan.FromMinutes(1),
                        RestoreCheckpointInterval = TimeSpan.FromMinutes(1),
                        HeartbeatInterval = Timeout.InfiniteTimeSpan,
                        MasterLeaseExpiryTime = masterLeaseExpiryTime
                    };
                    config.CentralStore = centralStoreConfiguration;
                    memoryContentLocationEventStore = (MemoryContentLocationEventStoreConfiguration)config.EventStore;
                });

            var events = GenerateAddEvents(numberOfMachines, addsPerMachine, maximumBatchSize);

            await RunTestAsync(
                new Context(Logger),
                2,
                async context =>
                {
                    var sessions = context.Sessions;
                    Warmup(maximumBatchSize, warmupBatches, memoryContentLocationEventStore);
                    context.GetMaster().LocalLocationStore.Database.FlushIfEnabled(context);
                    PrintCacheStatistics(context);

                    {
                        var stopWatch = new Stopwatch();
                        Output.WriteLine("[Benchmark] Starting in 5s (use this when analyzing with dotTrace)");
                        await Task.Delay(5000);

                        // Benchmark
                        stopWatch.Restart();
                        Parallel.ForEach(Enumerable.Range(0, numberOfMachines), machineId => {
                            var eventHub = memoryContentLocationEventStore.Hub;

                            foreach (var ev in events[machineId])
                            {
                                eventHub.LockFreeSend(ev);
                            }
                        });
                        context.GetMaster().LocalLocationStore.Database.FlushIfEnabled(context);
                        stopWatch.Stop();

                        var ts = stopWatch.Elapsed;
                        var elapsedTime = string.Format("{0:00}:{1:00}:{2:00}.{3:00}",
                            ts.Hours, ts.Minutes, ts.Seconds,
                            ts.Milliseconds / 10);
                        Output.WriteLine("[Benchmark] Total Time: " + ts);
                    }

                    PrintCacheStatistics(context);
                    await Task.Delay(5000);
                });
        }

        private void Warmup(int maximumBatchSize, int warmupBatches, MemoryContentLocationEventStoreConfiguration memoryContentLocationEventStore)
        {
            Output.WriteLine("[Warmup] Starting");
            var warmupEventHub = memoryContentLocationEventStore.Hub;
            var warmupRng = new Random(Environment.TickCount);

            var stopWatch = new Stopwatch();
            stopWatch.Start();
            foreach (var _ in Enumerable.Range(0, warmupBatches))
            {
                var machineId = new MachineId(warmupRng.Next());
                var batch = Enumerable.Range(0, maximumBatchSize).Select(x => new ShortHash(ContentHash.Random())).ToList();
                warmupEventHub.Send(new RemoveContentLocationEventData(machineId, batch));
            }
            stopWatch.Stop();

            var ts = stopWatch.Elapsed;
            var elapsedTime = string.Format("{0:00}:{1:00}:{2:00}.{3:00}",
                ts.Hours, ts.Minutes, ts.Seconds,
                ts.Milliseconds / 10);
            Output.WriteLine("[Warmup] Total Time: " + ts);
        }

        private static List<List<ContentLocationEventData>> GenerateAddEvents(int numberOfMachines, int addsPerMachine, int maximumBatchSize)
        {
            var randomSeed = Environment.TickCount;
            var events = new List<List<ContentLocationEventData>>(numberOfMachines);
            events.AddRange(Enumerable.Range(0, numberOfMachines).Select(x => (List<ContentLocationEventData>)null));

            Parallel.ForEach(Enumerable.Range(0, numberOfMachines), machineId =>
            {
                var machineIdObject = new MachineId(machineId);
                var rng = new Random(Interlocked.Increment(ref randomSeed));

                var addedContent = Enumerable.Range(0, addsPerMachine).Select(_ => ContentHash.Random()).ToList();

                var machineEvents = new List<ContentLocationEventData>();
                for (var pendingHashes = addedContent.Count; pendingHashes > 0;)
                {
                    // Add the hashes in random batches
                    var batchSize = rng.Next(1, Math.Min(maximumBatchSize, pendingHashes));
                    var batch = addedContent.GetRange(addedContent.Count - pendingHashes, batchSize).Select(hash => new ShortHashWithSize(new ShortHash(hash), 200)).ToList();
                    machineEvents.Add(new AddContentLocationEventData(machineIdObject, batch));
                    pendingHashes -= batchSize;
                }
                events[machineId] = machineEvents;
            });

            return events;
        }

        [Fact(Skip = "For manual usage only")]
        public async Task MultiThreadedStressTestRocksDbContentLocationDatabaseOnMixedAddAndDelete()
        {
            bool useIncrementalCheckpointing = true;
            int numberOfMachines = 100;
            int deletesPerMachine = 25000;
            int maximumBatchSize = 2000;
            int warmupBatches = 10000;

            var centralStoreConfiguration = new LocalDiskCentralStoreConfiguration(TestRootDirectoryPath / "centralstore", Guid.NewGuid().ToString());
            var masterLeaseExpiryTime = TimeSpan.FromMinutes(3);

            MemoryContentLocationEventStoreConfiguration memoryContentLocationEventStore = null;
            ConfigureRocksDbContentLocationBasedTest(
                configureInMemoryEventStore: true,
                (index, testRootDirectory, config) =>
                {
                    config.Checkpoint = new CheckpointConfiguration(testRootDirectory)
                    {
                        /* Set role to null to automatically choose role using master election */
                        Role = null,
                        UseIncrementalCheckpointing = useIncrementalCheckpointing,
                        CreateCheckpointInterval = TimeSpan.FromMinutes(1),
                        RestoreCheckpointInterval = TimeSpan.FromMinutes(1),
                        HeartbeatInterval = Timeout.InfiniteTimeSpan,
                        MasterLeaseExpiryTime = masterLeaseExpiryTime
                    };
                    config.CentralStore = centralStoreConfiguration;
                    memoryContentLocationEventStore = (MemoryContentLocationEventStoreConfiguration)config.EventStore;
                });

            var events = GenerateMixedAddAndDeleteEvents(numberOfMachines, deletesPerMachine, maximumBatchSize);

            await RunTestAsync(
                new Context(Logger),
                2,
                async context =>
                {
                    var sessions = context.Sessions;
                    Warmup(maximumBatchSize, warmupBatches, memoryContentLocationEventStore);
                    context.GetMaster().LocalLocationStore.Database.FlushIfEnabled(context);
                    PrintCacheStatistics(context);

                    {
                        var stopWatch = new Stopwatch();
                        Output.WriteLine("[Benchmark] Starting in 5s (use this when analyzing with dotTrace)");
                        await Task.Delay(5000);

                        // Benchmark
                        stopWatch.Restart();
                        Parallel.ForEach(Enumerable.Range(0, numberOfMachines), machineId => {
                            var eventHub = memoryContentLocationEventStore.Hub;

                            foreach (var ev in events[machineId])
                            {
                                eventHub.LockFreeSend(ev);
                            }
                        });
                        context.GetMaster().LocalLocationStore.Database.FlushIfEnabled(context);
                        stopWatch.Stop();

                        var ts = stopWatch.Elapsed;
                        var elapsedTime = string.Format("{0:00}:{1:00}:{2:00}.{3:00}",
                            ts.Hours, ts.Minutes, ts.Seconds,
                            ts.Milliseconds / 10);
                        Output.WriteLine("[Benchmark] Total Time: " + ts);

                        PrintCacheStatistics(context);
                    }

                    await Task.Delay(5000);
                });
        }

        private class FstComparer<T> : IComparer<(int, T)>
        {
            public int Compare((int, T) x, (int, T) y)
            {
                return x.Item1.CompareTo(y.Item1);
            }
        }

        private static List<List<ContentLocationEventData>> GenerateMixedAddAndDeleteEvents(int numberOfMachines, int deletesPerMachine, int maximumBatchSize)
        {
            var randomSeed = Environment.TickCount;

            var events = new List<List<ContentLocationEventData>>(numberOfMachines);
            events.AddRange(Enumerable.Range(0, numberOfMachines).Select(x => (List<ContentLocationEventData>)null));

            Parallel.ForEach(Enumerable.Range(0, numberOfMachines), machineId =>
            {
                var machineIdObject = new MachineId(machineId);
                var rng = new Random(Interlocked.Increment(ref randomSeed));

                // We want deletes to be performed in any arbitrary order, so the first in the pair is a random integer
                // This distribution is obviously not uniform at the end, but it doesn't matter, all we want is for
                // add -> delete pairs not to be contiguous.
                var addedPool = new BuildXL.Cache.ContentStore.Utils.PriorityQueue<(int, ShortHash)>(deletesPerMachine, new FstComparer<ShortHash>());

                var machineEvents = new List<ContentLocationEventData>();
                var totalAddsPerfomed = 0;
                // We can only delete after we have added, so we only reach the condition at the end
                for (var totalDeletesPerformed = 0; totalDeletesPerformed < deletesPerMachine; )
                {
                    bool addEnabled = totalAddsPerfomed < deletesPerMachine;
                    // We can only delete when it is causally consistent to do so
                    bool deleteEnabled = totalDeletesPerformed < deletesPerMachine && addedPool.Count > 0;
                    bool performDelete = deleteEnabled && rng.Next(0, 10) > 8 || !addEnabled;

                    if (performDelete)
                    {
                        var batchSize = Math.Min(deletesPerMachine - totalDeletesPerformed, addedPool.Count);
                        batchSize = rng.Next(1, batchSize);
                        batchSize = Math.Min(batchSize, maximumBatchSize);

                        var batch = new List<ShortHash>(batchSize);
                        foreach (var _ in Enumerable.Range(0, batchSize))
                        {
                            var shortHash = addedPool.Top.Item2;
                            addedPool.Pop();
                            batch.Add(shortHash);
                        }

                        machineEvents.Add(new RemoveContentLocationEventData(machineIdObject, batch));
                        totalDeletesPerformed += batch.Count;
                    } else
                    {
                        var batchSize = Math.Min(deletesPerMachine - totalAddsPerfomed, maximumBatchSize);
                        batchSize = rng.Next(1, batchSize);

                        var batch = new List<ShortHashWithSize>(batchSize);
                        foreach (var x in Enumerable.Range(0, batchSize))
                        {
                            var shortHash = new ShortHash(ContentHash.Random());
                            batch.Add(new ShortHashWithSize(shortHash, 200));
                            addedPool.Push((rng.Next(), shortHash));
                        }

                        machineEvents.Add(new AddContentLocationEventData(machineIdObject, batch));
                        totalAddsPerfomed += batch.Count;
                    }
                }

                events[machineId] = machineEvents;
            });

            return events;
        }

        [Fact(Skip = "For manual usage only")]
        public async Task MultiThreadedStressTestRocksDbContentLocationDatabaseOnUniqueAddsWithCacheHit()
        {
            bool useIncrementalCheckpointing = true;
            int warmupBatches = 10000;
            int numberOfMachines = 100;
            int operationsPerMachine = 25000;
            float cacheHitRatio = 0.5f;
            int maximumBatchSize = 1000;

            var centralStoreConfiguration = new LocalDiskCentralStoreConfiguration(TestRootDirectoryPath / "centralstore", Guid.NewGuid().ToString());
            var masterLeaseExpiryTime = TimeSpan.FromMinutes(3);

            MemoryContentLocationEventStoreConfiguration memoryContentLocationEventStore = null;
            ConfigureRocksDbContentLocationBasedTest(
                configureInMemoryEventStore: true,
                (index, testRootDirectory, config) =>
                {
                    config.Checkpoint = new CheckpointConfiguration(testRootDirectory)
                    {
                        /* Set role to null to automatically choose role using master election */
                        Role = null,
                        UseIncrementalCheckpointing = useIncrementalCheckpointing,
                        CreateCheckpointInterval = TimeSpan.FromMinutes(1),
                        RestoreCheckpointInterval = TimeSpan.FromMinutes(1),
                        HeartbeatInterval = Timeout.InfiniteTimeSpan,
                        MasterLeaseExpiryTime = masterLeaseExpiryTime
                    };
                    config.CentralStore = centralStoreConfiguration;
                    memoryContentLocationEventStore = (MemoryContentLocationEventStoreConfiguration)config.EventStore;
                });

            var events = GenerateUniquenessWorkload(numberOfMachines, cacheHitRatio, maximumBatchSize, operationsPerMachine, randomSeedOverride: 42);

            await RunTestAsync(
                new Context(Logger),
                2,
                async context =>
                {
                    var sessions = context.Sessions;
                    Warmup(maximumBatchSize, warmupBatches, memoryContentLocationEventStore);
                    context.GetMaster().LocalLocationStore.Database.FlushIfEnabled(context);
                    PrintCacheStatistics(context);

                    {
                        var stopWatch = new Stopwatch();
                        Output.WriteLine("[Benchmark] Starting in 5s (use this when analyzing with dotTrace)");
                        await Task.Delay(5000);

                        // Benchmark
                        stopWatch.Restart();
                        Parallel.ForEach(Enumerable.Range(0, numberOfMachines), machineId => {
                            var eventHub = memoryContentLocationEventStore.Hub;

                            foreach (var ev in events[machineId])
                            {
                                eventHub.LockFreeSend(ev);
                            }
                        });
                        context.GetMaster().LocalLocationStore.Database.FlushIfEnabled(context);
                        stopWatch.Stop();

                        var ts = stopWatch.Elapsed;
                        var elapsedTime = string.Format("{0:00}:{1:00}:{2:00}.{3:00}",
                            ts.Hours, ts.Minutes, ts.Seconds,
                            ts.Milliseconds / 10);
                        Output.WriteLine("[Benchmark] Total Time: " + ts);

                        PrintCacheStatistics(context);
                    }

                    await Task.Delay(5000);
                });
        }

        private void PrintCacheStatistics(TestContext context)
        {
            var db = context.GetMaster().LocalLocationStore.Database;
            var counters = db.Counters;

            if (db.IsInMemoryCacheEnabled)
            {
                Output.WriteLine("CACHE ENABLED");
            }
            else
            {
                Output.WriteLine("CACHE DISABLED");
            }

            Output.WriteLine("[Statistics] TotalNumberOfCacheHit: " + counters[ContentLocationDatabaseCounters.TotalNumberOfCacheHit].ToString());
            Output.WriteLine("[Statistics] TotalNumberOfCacheMiss: " + counters[ContentLocationDatabaseCounters.TotalNumberOfCacheMiss].ToString());
            Output.WriteLine("[Statistics] CacheFlush: " + counters[ContentLocationDatabaseCounters.CacheFlush].ToString());
            Output.WriteLine("[Statistics] TotalNumberOfCacheFlushes: " + counters[ContentLocationDatabaseCounters.TotalNumberOfCacheFlushes].ToString());
            Output.WriteLine("[Statistics] NumberOfCacheFlushesTriggeredByUpdates: " + counters[ContentLocationDatabaseCounters.NumberOfCacheFlushesTriggeredByUpdates].ToString());
            Output.WriteLine("[Statistics] NumberOfCacheFlushesTriggeredByTimer: " + counters[ContentLocationDatabaseCounters.NumberOfCacheFlushesTriggeredByTimer].ToString());
            Output.WriteLine("[Statistics] NumberOfCacheFlushesTriggeredByGarbageCollection: " + counters[ContentLocationDatabaseCounters.NumberOfCacheFlushesTriggeredByGarbageCollection].ToString());
            Output.WriteLine("[Statistics] NumberOfCacheFlushesTriggeredByCheckpoint: " + counters[ContentLocationDatabaseCounters.NumberOfCacheFlushesTriggeredByCheckpoint].ToString());
        }

        private static List<List<ContentLocationEventData>> GenerateUniquenessWorkload(int numberOfMachines, float cacheHitRatio, int maximumBatchSize, int operationsPerMachine, int? randomSeedOverride = null)
        {
            var randomSeed = randomSeedOverride ?? Environment.TickCount;

            var events = new List<List<ContentLocationEventData>>(numberOfMachines);
            events.AddRange(Enumerable.Range(0, numberOfMachines).Select(x => (List<ContentLocationEventData>)null));
            
            var cacheHitHashPool = new ConcurrentBigSet<ShortHash>();
            Parallel.ForEach(Enumerable.Range(0, numberOfMachines), machineId =>
            {
                var machineIdObject = new MachineId(machineId);
                var rng = new Random(Interlocked.Increment(ref randomSeed));

                var machineEvents = new List<ContentLocationEventData>();
                for (var operations = 0; operations < operationsPerMachine; )
                {
                    // Done this way to ensure batches don't get progressively smaller and hog memory
                    var batchSize = rng.Next(1, maximumBatchSize);
                    batchSize = Math.Min(batchSize, operationsPerMachine - operations);

                    var hashes = new List<ShortHashWithSize>();
                    while (hashes.Count < batchSize)
                    {
                        var shouldHitCache = rng.NextDouble() < cacheHitRatio;

                        ShortHash hashToUse;
                        if (cacheHitHashPool.Count > 0 && shouldHitCache)
                        {
                            // Since this set is grow-only, this should always work
                            hashToUse = cacheHitHashPool[rng.Next(0, cacheHitHashPool.Count)];
                        }
                        else
                        {
                            do
                            {
                                hashToUse = new ShortHash(ContentHash.Random());
                            } while (cacheHitHashPool.Contains(hashToUse) || !cacheHitHashPool.Add(hashToUse));
                        }

                        hashes.Add(new ShortHashWithSize(hashToUse, 200));
                    }
                    
                    machineEvents.Add(new AddContentLocationEventData(
                        machineIdObject,
                        hashes));

                    operations += batchSize;
                }

                events[machineId] = machineEvents;
            });

            return events;
        }
    }
}
