// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.Synchronization;
using BuildXL.Cache.MemoizationStore.Distributed.Stores;
using BuildXL.Cache.MemoizationStore.Interfaces.Results;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.InterfacesTest.Results;
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
    public class DistributedOneLevelCacheTests : LocalLocationStoreDistributedContentTestsBase
    {
        private const int RandomContentByteCount = 100;

        public DistributedOneLevelCacheTests(LocalRedisFixture redis, ITestOutputHelper output)
            : base(redis, output)
        {
            //_test = new InnerDistributedTest(redis, output);
            ConfigureWithOneMaster();
        }

        [Fact]
        public Task BasicDistributedAddAndGet()
        {
            // Add this to make sure that construction of a publishing cache works. Shouldn't affect the behavior of the rest of the build.
            EnablePublishingCache = true;

            ConfigureWithOneMaster(dcs =>
            {
                dcs.TouchContentHashLists = true;
            });

           return RunTestAsync(
               3,
               async context =>
               {
                   var sf = StrongFingerprint.Random();
                   var touchedSf = StrongFingerprint.Random();

                   var workerCaches = context.EnumerateWorkersIndices().Select(i => context.CacheSessions[i]).ToArray();
                   var workerCache0 = workerCaches[0];
                   var workerCache1 = workerCaches[1];
                   var masterCache = context.CacheSessions[context.GetMasterIndex()];

                   var workerStores = context.EnumerateWorkersIndices().Select(i => context.GetLocalLocationStore(i)).ToArray();
                   var workerStore0 = workerStores[0];
                   var workerStore1 = workerStores[1];
                   var masterStore = context.GetLocalLocationStore(context.GetMasterIndex());

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
                   var addResult = await workerCache0.AddOrGetContentHashListAsync(
                       context,
                       sf,
                       new ContentHashListWithDeterminism(contentHashList, CacheDeterminism.None),
                       Token).ShouldBeSuccess();

                   await workerCache0.AddOrGetContentHashListAsync(
                       context,
                       touchedSf,
                       new ContentHashListWithDeterminism(contentHashList, CacheDeterminism.None),
                       Token).ShouldBeSuccess();
                   Assert.Equal(null, addResult.ContentHashListWithDeterminism.ContentHashList);
                   await ensureLevelAsync(masterCache, 0 /* Master DB gets updated immediately */);

                   // Verify found remotely
                   await ensureLevelAsync(workerCache0, 1);
                   await ensureLevelAsync(workerCache1, 1);

                   // Get original last access time for strong fingerprint which will be touched
                   var initialTouchTime = getTouchedFingerprintLastAccessTime(masterStore);
                   initialTouchTime.Should().Be(TestClock.UtcNow);

                   TestClock.UtcNow += TimeSpan.FromDays(1);

                   // Restore (update) db on worker 1
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
                   contentHashList = new ContentHashList(new[] { putResult.ContentHash });
                   await workerCache0.AddOrGetContentHashListAsync(
                       context,
                       sf,
                       new ContentHashListWithDeterminism(contentHashList, CacheDeterminism.None),
                       Token).ShouldBeSuccess();

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

                    var workerCaches = context.EnumerateWorkersIndices().Select(i => context.CacheSessions[i]).ToArray();
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

        protected Task RunTestAsync(
            int storeCount,
            Func<MetadataTestContext, Task> testFunc)
        {
            var context = new Context(Logger);

            return RunTestAsync(
                context,
                storeCount,
                context => testFunc((MetadataTestContext)context));
        }

        protected override TestContext ConfigureTestContext(TestContext context)
        {
            return new MetadataTestContext(context);
        }

        protected class MetadataTestContext : TestContext
        {
            public IReadOnlyList<ICacheSession> CacheSessions { get; private set; }
            public readonly IReadOnlyList<DistributedOneLevelCache> Caches;

            public MetadataTestContext(TestContext other)
                : base(other)
            {
                Caches = other.Stores.Select((store, i) => new DistributedOneLevelCache(store, other.GetDistributedStore(i), Guid.NewGuid(), passContentToMemoization: false)).ToList();
            }

            public ICacheSession GetMasterCacheSession() => CacheSessions[GetMasterIndex()];

            public ICacheSession GetFirstWorkerCacheSession() => CacheSessions[GetFirstWorkerIndex()];

            public override IContentSession GetSession(int idx)
            {
                return ((OneLevelCacheSession)CacheSessions[idx]).ContentSession;
            }

            public override async Task StartupAsync(ImplicitPin implicitPin, int? storeToStartupLast, string buildId = null)
            {
                var startupResults = await TaskUtilities.SafeWhenAll(Caches.Select(async store => await store.StartupAsync(Context)));
                Assert.True(startupResults.All(x => x.Succeeded), $"Failed to startup: {string.Join(Environment.NewLine, startupResults.Where(s => !s))}");

                CacheSessions = Caches.Select((store, id) => store.CreateSession(Context, GetSessionName(id, buildId), implicitPin).Session).ToList();
                await TaskUtilities.SafeWhenAll(CacheSessions.Select(async session => await session.StartupAsync(Context)));

                Sessions = CacheSessions.ToList<IContentSession>();
            }

            protected override async Task ShutdownStoresAsync()
            {
                await TaskUtilities.SafeWhenAll(Caches.Select(async store => await store.ShutdownAsync(Context)));

                foreach (var store in Caches)
                {
                    store.Dispose();
                }
            }
        }
    }
}
