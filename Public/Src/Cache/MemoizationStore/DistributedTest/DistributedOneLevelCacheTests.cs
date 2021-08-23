// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Distributed.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.Synchronization;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.Host.Configuration;
using BuildXL.Cache.Host.Service;
using BuildXL.Cache.MemoizationStore.Distributed.Stores;
using BuildXL.Cache.MemoizationStore.Interfaces.Caches;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.InterfacesTest.Results;
using BuildXL.Cache.MemoizationStore.Sessions;
using BuildXL.Utilities.Tasks;
using ContentStoreTest.Distributed.Redis;
using ContentStoreTest.Distributed.Sessions;
using FluentAssertions;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.Cache.MemoizationStore.Distributed.Test
{
    [Trait("Category", "LongRunningTest")]
    [Collection("Redis-based tests")]
    [TestClassIfSupported(requiresWindowsBasedOperatingSystem: true)]
    public class DistributedOneLevelCacheTests : LocalLocationStoreDistributedContentTestsBase<ICache, ICacheSession>
    {
        private const int RandomContentByteCount = 100;

        public DistributedOneLevelCacheTests(LocalRedisFixture redis, ITestOutputHelper output)
            : base(redis, output)
        {
            //_test = new InnerDistributedTest(redis, output);
            ConfigureWithOneMaster();
        }

        [Theory]
        [InlineData(null)]
        [InlineData(ContentMetadataStoreMode.Redis)]
        [InlineData(ContentMetadataStoreMode.WriteBothPreferDistributed)]
        [InlineData(ContentMetadataStoreMode.Distributed)]
        public Task BasicDistributedAddAndGet(ContentMetadataStoreMode? mode)
        {
            // Add this to make sure that construction of a publishing cache works. Shouldn't affect the behavior of the rest of the build.
            EnablePublishingCache = true;

            UseGrpcServer = mode != null;

            ConfigureWithOneMaster(dcs =>
            {
                dcs.TouchContentHashLists = true;
                if (mode != null)
                {
                    dcs.ContentMetadataEnableResilience = true;
                    dcs.ContentMetadataStoreMode = mode.Value;
                    dcs.UseMemoizationContentMetadataStore = true;
                }
            });

            return RunTestAsync(
                3,
                async context =>
                {
                    var sf = StrongFingerprint.Random();
                    var touchedSf = StrongFingerprint.Random();

                    var workerCaches = context.EnumerateWorkersIndices().Select(i => context.Sessions[i]).ToArray();
                    var workerCache0 = workerCaches[0];
                    var workerCache1 = workerCaches[1];
                    var masterCache = context.Sessions[context.GetMasterIndex()];

                    var workerStores = context.EnumerateWorkersIndices().Select(i => context.GetLocalLocationStore(i)).ToArray();
                    var workerStore0 = workerStores[0];
                    var workerStore1 = workerStores[1];
                    var masterStore = context.GetLocalLocationStore(context.GetMasterIndex());

                    TraceLine("Initial put");
                    var putResult = await workerCache0.PutRandomAsync(
                        context,
                        ContentHashType,
                        false,
                        RandomContentByteCount,
                        Token).ShouldBeSuccess();
                    var contentHashList = new ContentHashList(new[] { putResult.ContentHash });

                    async Task<int?> findLevelAsync(ICacheSession cacheSession)
                    {
                        var levelCacheSession = (ILevelSelectorsProvider)cacheSession;

                        int level = 0;
                        while (true)
                        {
                            // Only levels 1 and 2 should be available from cache session
                            level.Should().BeLessThan(2);

                            var result = await levelCacheSession.GetLevelSelectorsAsync(context, sf.WeakFingerprint, Token, level)
                                .ShouldBeSuccess();
                            if (result.Value.Selectors.Any(s => s.Equals(sf.Selector)))
                            {
                                return level;
                            }
                            else if (!result.Value.HasMore)
                            {
                                return null;
                            }

                            level++;
                        }
                    }

                    async Task ensureLevelAsync(ICacheSession cacheSession, int? expectedLevel)
                    {
                        if (expectedLevel != null)
                        {
                            var getResult = await cacheSession.GetContentHashListAsync(context, sf, Token).ShouldBeSuccess();
                            Assert.Equal(contentHashList, getResult.ContentHashListWithDeterminism.ContentHashList);
                        }
                        else
                        {
                            var getResult = await cacheSession.GetContentHashListAsync(context, sf, Token).ShouldBeSuccess();
                            Assert.Equal(null, getResult.ContentHashListWithDeterminism.ContentHashList);
                        }

                        var level = await findLevelAsync(cacheSession);
                        level.Should().Be(expectedLevel);
                    }

                    DateTime getTouchedFingerprintLastAccessTime(LocalLocationStore store)
                    {
                        return store.Database.GetMetadataEntry(context, touchedSf, touch: false).ShouldBeSuccess().Value.Value.LastAccessTimeUtc;
                    }

                    // Verify not found initially
                    await ensureLevelAsync(workerCache0, null);
                    await ensureLevelAsync(workerCache1, null);

                    TraceLine("Initial add CHL");
                    var addResult = await workerCache0.AddOrGetContentHashListAsync(
                        context,
                        sf,
                        new ContentHashListWithDeterminism(contentHashList, CacheDeterminism.None),
                        Token).ShouldBeSuccess();

                    TraceLine("Initial add touched CHL");
                    await workerCache0.AddOrGetContentHashListAsync(
                        context,
                        touchedSf,
                        new ContentHashListWithDeterminism(contentHashList, CacheDeterminism.None),
                        Token).ShouldBeSuccess();
                    Assert.Equal(null, addResult.ContentHashListWithDeterminism.ContentHashList);
                    await ensureLevelAsync(masterCache, 0 /* Master DB gets updated immediately */);

                    TraceLine("Verifying CHL in worker caches via remote");
                    // Verify found remotely
                    await ensureLevelAsync(workerCache0, 1);
                    await ensureLevelAsync(workerCache1, 1);

                    // Get original last access time for strong fingerprint which will be touched
                    var initialTouchTime = getTouchedFingerprintLastAccessTime(masterStore);
                    initialTouchTime.Should().Be(TestClock.UtcNow);

                    TestClock.UtcNow += TimeSpan.FromDays(1);

                    // Restore (update) db on worker 1
                    await masterStore.HeartbeatAsync(context, inline: true).ShouldBeSuccess();
                    await masterStore.CreateCheckpointAsync(context).ShouldBeSuccess();
                    await workerStore1.HeartbeatAsync(context, inline: true, forceRestore: true).ShouldBeSuccess();
                    await ensureLevelAsync(workerCache0, 1 /* Worker 0 has not updated its db it should still be found remotely */);
                    await ensureLevelAsync(workerCache1, 0 /* Worker 1 should find locally after restoring DB */);

                    // Replace entry
                    putResult = await workerCache0.PutRandomAsync(
                         context,
                         ContentHashType,
                         false,
                         RandomContentByteCount,
                         Token).ShouldBeSuccess();
                    var newContentHashList = new ContentHashList(new[] { putResult.ContentHash });

                    if (mode == ContentMetadataStoreMode.Distributed)
                    {
                        // Test restore of content hash list
                        var cms = context.GetContentMetadataService();
                        await cms.OnRoleUpdatedAsync(context, Role.Worker);
                        await cms.OnRoleUpdatedAsync(context, Role.Master);
                    }

                    TraceLine("Try replace CHL");
                    var update = await workerCache0.AddOrGetContentHashListAsync(
                        context,
                        sf,
                        new ContentHashListWithDeterminism(newContentHashList, CacheDeterminism.None),
                        Token).ShouldBeSuccess();

                    if (mode == null)
                    {
                        // In this case, content store is not passed to memoization which then treats all CHLs as
                        // unbacked so they can be replaced.
                        contentHashList = newContentHashList;
                        Assert.Equal(null, update.ContentHashListWithDeterminism.ContentHashList);
                    }
                    else
                    {
                        Assert.NotEqual(null, update.ContentHashListWithDeterminism.ContentHashList);
                    }

                    await workerCache0.GetContentHashListAsync(
                        context,
                        touchedSf,
                        Token).ShouldBeSuccess();

                    await ensureLevelAsync(masterCache, 0 /* Master db gets updated immediately */);

                    await masterStore.CreateCheckpointAsync(context).ShouldBeSuccess();
                    await workerStore1.HeartbeatAsync(context, inline: true, forceRestore: true).ShouldBeSuccess();
                    await ensureLevelAsync(workerCache0, 1 /* Worker 0 has not updated its db it should still be found remotely */);
                    await ensureLevelAsync(workerCache1, 0 /* Worker 1 should find locally after restoring DB */);

                    // Touch should have propagated to master and worker 1 (worker 1 restored checkpoint above)
                    getTouchedFingerprintLastAccessTime(masterStore).Should().Be(TestClock.UtcNow);
                    getTouchedFingerprintLastAccessTime(workerStore1).Should().Be(TestClock.UtcNow);
                });
        }

        [Fact]
        public Task BasicDistributedAddAndGetRedisBackCompat()
        {
            UseGrpcServer = true;

            ConfigureWithOneMaster(dcs =>
            {
                dcs.TouchContentHashLists = true;
                dcs.ContentMetadataEnableResilience = true;
                dcs.ContentMetadataStoreMode = dcs.TestIteration == 1
                    ? ContentMetadataStoreMode.WriteBothPreferRedis
                    : ContentMetadataStoreMode.Redis;
                dcs.UseMemoizationContentMetadataStore = dcs.TestIteration == 1;
            });

            var sf = StrongFingerprint.Random();
            var touchedSf = StrongFingerprint.Random();

            ContentHashList contentHashList = default;
            ContentHashList contentHashList2 = default;
            PutResult putResult = default;

            return RunTestAsync(
                3,
                async context =>
                {
                    var workerCaches = context.EnumerateWorkersIndices().Select(i => context.Sessions[i]).ToArray();
                    var workerCache0 = workerCaches[0];
                    var workerCache1 = workerCaches[1];
                    var masterCache = context.Sessions[context.GetMasterIndex()];

                    var workerStores = context.EnumerateWorkersIndices().Select(i => context.GetLocalLocationStore(i)).ToArray();
                    var workerStore0 = workerStores[0];
                    var workerStore1 = workerStores[1];
                    var masterStore = context.GetLocalLocationStore(context.GetMasterIndex());

                    if (context.Iteration == 0)
                    {
                        putResult = await workerCache0.PutRandomAsync(
                            context,
                            ContentHashType,
                            false,
                            RandomContentByteCount,
                            Token).ShouldBeSuccess();
                        contentHashList = new ContentHashList(new[] { putResult.ContentHash });

                        // Verify not found initially
                        await ensureLevelAsync(workerCache0, null);
                        await ensureLevelAsync(workerCache1, null);
                        var addResult = await workerCache0.AddOrGetContentHashListAsync(
                            context,
                            sf,
                            new ContentHashListWithDeterminism(contentHashList, CacheDeterminism.None),
                            Token).ShouldBeSuccess();
                        Assert.Equal(null, addResult.ContentHashListWithDeterminism.ContentHashList);
                    }
                    else if (context.Iteration == 1)
                    {
                        var putResult2 = await workerCache0.PutRandomAsync(
                            context,
                            ContentHashType,
                            false,
                            RandomContentByteCount,
                            Token).ShouldBeSuccess();
                        contentHashList2 = new ContentHashList(new[] { putResult2.ContentHash });

                        var addResult = await workerCache0.AddOrGetContentHashListAsync(
                            context,
                            sf,
                            new ContentHashListWithDeterminism(contentHashList2, CacheDeterminism.None),
                            Token).ShouldBeSuccess();
                        Assert.NotEqual(null, addResult.ContentHashListWithDeterminism.ContentHashList);

                        var secondAddResult = await workerCache0.AddOrGetContentHashListAsync(
                            context,
                            touchedSf,
                            new ContentHashListWithDeterminism(contentHashList, CacheDeterminism.None),
                            Token).ShouldBeSuccess();
                        Assert.Equal(null, secondAddResult.ContentHashListWithDeterminism.ContentHashList);
                        await ensureLevelAsync(masterCache, 0 /* Master DB gets updated immediately */);

                        // Verify found remotely
                        await ensureLevelAsync(workerCache0, 1);
                        await ensureLevelAsync(workerCache1, 1);

                        // Get original last access time for strong fingerprint which will be touched
                        var initialTouchTime = getTouchedFingerprintLastAccessTime(masterStore);
                        initialTouchTime.Should().Be(TestClock.UtcNow);

                        TestClock.UtcNow += TimeSpan.FromDays(1);

                        // Restore (update) db on worker 1
                        await masterStore.HeartbeatAsync(context, inline: true).ShouldBeSuccess();
                        await masterStore.CreateCheckpointAsync(context).ShouldBeSuccess();
                        await workerStore1.HeartbeatAsync(context, inline: true, forceRestore: true).ShouldBeSuccess();
                        await ensureLevelAsync(workerCache0, 1 /* Worker 0 has not updated its db it should still be found remotely */);
                        await ensureLevelAsync(workerCache1, 0 /* Worker 1 should find locally after restoring DB */);

                        // Attempt to replace entry (should return conflict)
                        putResult = await workerCache0.PutRandomAsync(
                             context,
                             ContentHashType,
                             false,
                             RandomContentByteCount,
                             Token).ShouldBeSuccess();
                        var newContentHashList = new ContentHashList(new[] { putResult.ContentHash });
                        var replaceResult = await workerCache0.AddOrGetContentHashListAsync(
                            context,
                            sf,
                            new ContentHashListWithDeterminism(newContentHashList, CacheDeterminism.None),
                            Token).ShouldBeSuccess();
                        Assert.Equal(contentHashList, replaceResult.ContentHashListWithDeterminism.ContentHashList);
                    }
                    else if (context.Iteration == 2)
                    {
                        var getResult = await workerCache0.GetContentHashListAsync(
                            context,
                            sf,
                            Token).ShouldBeSuccess();
                        getResult.ContentHashListWithDeterminism.ContentHashList.Should().Be(contentHashList);

                        var getResult2 = await workerCache0.GetContentHashListAsync(
                            context,
                            touchedSf,
                            Token).ShouldBeSuccess();
                        getResult2.ContentHashListWithDeterminism.ContentHashList.Should().Be(contentHashList);

                        await ensureLevelAsync(masterCache, 0 /* Master db gets updated immediately */);

                        await masterStore.CreateCheckpointAsync(context).ShouldBeSuccess();
                        await workerStore1.HeartbeatAsync(context, inline: true, forceRestore: true).ShouldBeSuccess();
                        await ensureLevelAsync(workerCache1, 0 /* Worker 1 should find locally after restoring DB */);

                        // Touch should have propagated to master and worker 1 (worker 1 restored checkpoint above)
                        getTouchedFingerprintLastAccessTime(masterStore).Should().Be(TestClock.UtcNow);
                        getTouchedFingerprintLastAccessTime(workerStore1).Should().Be(TestClock.UtcNow);
                    }

                    async Task<int?> findLevelAsync(ICacheSession cacheSession)
                    {
                        var levelCacheSession = (ILevelSelectorsProvider)cacheSession;

                        int level = 0;
                        while (true)
                        {
                            // Only levels 1 and 2 should be available from cache session
                            level.Should().BeLessThan(2);

                            var result = await levelCacheSession.GetLevelSelectorsAsync(context, sf.WeakFingerprint, Token, level)
                                .ShouldBeSuccess();
                            if (result.Value.Selectors.Any(s => s.Equals(sf.Selector)))
                            {
                                return level;
                            }
                            else if (!result.Value.HasMore)
                            {
                                return null;
                            }

                            level++;
                        }
                    }

                    async Task ensureLevelAsync(ICacheSession cacheSession, int? expectedLevel)
                    {
                        if (expectedLevel != null)
                        {
                            var getResult = await cacheSession.GetContentHashListAsync(context, sf, Token).ShouldBeSuccess();
                            Assert.Equal(contentHashList, getResult.ContentHashListWithDeterminism.ContentHashList);
                        }

                        var level = await findLevelAsync(cacheSession);
                        level.Should().Be(expectedLevel);
                    }

                    DateTime getTouchedFingerprintLastAccessTime(LocalLocationStore store)
                    {
                        return store.Database.GetMetadataEntry(context, touchedSf, touch: false).ShouldBeSuccess().Value.Value.LastAccessTimeUtc;
                    }
                },
                iterations: 3);
        }

        [Fact]
        public Task RespectPreferSharedWithSinglePhaseNonDeterminism()
        {
            ConfigureWithOneMaster(dcs =>
            {
                dcs.TouchContentHashLists = true;
            });

            return RunTestAsync(
                3,
                async context =>
                {
                    var sf = StrongFingerprint.Random();

                    var workerCaches = context.EnumerateWorkersIndices().Select(i => context.Sessions[i]).ToArray();
                    var workerCache0 = workerCaches[0];

                    var workerStores = context.EnumerateWorkersIndices().Select(i => context.GetLocalLocationStore(i)).ToArray();
                    var workerStore0 = workerStores[0];
                    var masterStore = context.GetLocalLocationStore(context.GetMasterIndex());

                    var putResult = await workerCache0.PutRandomAsync(
                        context,
                        ContentHashType,
                        false,
                        RandomContentByteCount,
                        Token).ShouldBeSuccess();
                    var contentHashList = new ContentHashList(new[] { putResult.ContentHash });

                    var addResult = await workerCache0.AddOrGetContentHashListAsync(
                        context,
                        sf,
                        new ContentHashListWithDeterminism(contentHashList, CacheDeterminism.SinglePhaseNonDeterministic),
                        Token).ShouldBeSuccess();
                    addResult.ContentHashListWithDeterminism.ContentHashList.Should().BeNull();

                    TestClock.UtcNow += TimeSpan.FromDays(1);

                    // Restore (update) db on worker 1
                    await masterStore.CreateCheckpointAsync(context).ShouldBeSuccess();
                    await workerStore0.HeartbeatAsync(context, inline: true, forceRestore: true).ShouldBeSuccess();

                    // Making sure that we can get the content hash list
                    var contentHashListResult = await workerCache0.GetContentHashListAsync(context, sf, Token).ShouldBeSuccess();
                    contentHashListResult.Source.Should().Be(ContentHashListSource.Local);
                    contentHashListResult.ContentHashListWithDeterminism.ContentHashList.Should().Be(contentHashList);

                    // Replace entry
                    putResult = await workerCache0.PutRandomAsync(
                        context,
                        ContentHashType,
                        false,
                        RandomContentByteCount,
                        Token).ShouldBeSuccess();
                    var newContentHashList = new ContentHashList(new[] { putResult.ContentHash });
                    var updateResult = await workerCache0.AddOrGetContentHashListAsync(
                        context,
                        sf,
                        new ContentHashListWithDeterminism(newContentHashList, CacheDeterminism.SinglePhaseNonDeterministic),
                        Token).ShouldBeSuccess();

                    updateResult.ContentHashListWithDeterminism.ContentHashList.Should().BeNull("The update result should return a previous content hash list.");

                    var localContentHashListResult = await workerCache0.GetContentHashListAsync(context, sf, Token).ShouldBeSuccess();
                    localContentHashListResult.Source.Should().Be(ContentHashListSource.Local);
                    localContentHashListResult.ContentHashListWithDeterminism.ContentHashList.Should().Be(contentHashList); // should be the old content hash list

                    var sharedContentHashListResult = await workerCache0.GetContentHashListAsync(context, sf, Token, UrgencyHint.PreferShared).ShouldBeSuccess();
                    sharedContentHashListResult.ContentHashListWithDeterminism.ContentHashList.Should().Be(newContentHashList); // Should get a new content hash list from the global store.
                    sharedContentHashListResult.Source.Should().Be(ContentHashListSource.Shared);
                });
        }

        protected override CreateSessionResult<ICacheSession> CreateSession(ICache store, Context context, string name, ImplicitPin implicitPin)
        {
            return store.CreateSession(context, name, implicitPin);
        }

        protected override Task<GetStatsResult> GetStatsAsync(ICache store, Context context)
        {
            return store.GetStatsAsync(context);
        }

        protected override IContentStore UnwrapRootContentStore(ICache store)
        {
            if (store is IComponentWrapper<ICache> cw)
            {
                store = cw.Inner;
            }

            return ((IComponentWrapper<IContentStore>)store).Inner;
        }

        protected override IContentSession UnwrapRootContentSession(ICacheSession session)
        {
            if (session is OneLevelCacheSession oneLevelSession)
            {
                return oneLevelSession.ContentSession;
            }

            return base.UnwrapRootContentSession(session);
        }

        protected override DistributedCacheServiceArguments ModifyArguments(DistributedCacheServiceArguments arguments)
        {
            var dcs = arguments.Configuration.DistributedContentSettings;
            dcs.EnableDistributedCache = true;

            return base.ModifyArguments(arguments);
        }

        protected override ICache CreateFromTopLevelContentStore(IContentStore store)
        {
            return new DistributedOneLevelCache(store, TestContext.GetTypedStore<DistributedContentStore>(store), Guid.NewGuid(), passContentToMemoization: false);
        }
    }
}
