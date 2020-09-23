// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.FileSystem;
using ContentStoreTest.Test;
using System;
using ContentStoreTest.Distributed.Redis;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.MemoizationStore.Distributed.Stores;
using Xunit;
using System.Threading.Tasks;
using System.Collections.Generic;
using ContentStoreTest.Distributed.Sessions;
using Xunit.Abstractions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using System.Linq;
using BuildXL.Cache.ContentStore.Synchronization;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.MemoizationStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using FluentAssertions;
using static ContentStoreTest.Distributed.Sessions.DistributedContentTests;
using System.Threading;
using Test.BuildXL.TestUtilities.Xunit;
using BuildXL.Cache.ContentStore.Distributed.NuCache;

namespace BuildXL.Cache.MemoizationStore.Distributed.Test
{
    [Trait("Category", "LongRunningTest")]
    [Collection("Redis-based tests")]
    [TestClassIfSupported(requiresWindowsBasedOperatingSystem: true)]
    public class DistributedOneLevelCacheTests : TestBase
    {
        private const int RandomContentByteCount = 100;
        protected const HashType ContentHashType = HashType.Vso0;
        protected static readonly CancellationToken Token = CancellationToken.None;

        private readonly LocalLocationStoreDistributedContentTests _test;

        public DistributedOneLevelCacheTests(LocalRedisFixture redis, ITestOutputHelper output)
            : base(() => new PassThroughFileSystem(TestGlobal.Logger), TestGlobal.Logger, output)
        {
            _test = new InnerDistributedTest(redis, output);
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing)
            {
                _test.Dispose();
            }
        }

        [Fact]
        public Task BasicDistributedAddAndGet()
        {
            _test.ConfigureWithOneMaster(dcs =>
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
                   initialTouchTime.Should().Be(_test.TestClock.UtcNow);

                   _test.TestClock.UtcNow += TimeSpan.FromDays(1);

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
                   contentHashList = new ContentHashList(new[] {putResult.ContentHash});
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
                   getTouchedFingerprintLastAccessTime(masterStore).Should().Be(_test.TestClock.UtcNow);
                   getTouchedFingerprintLastAccessTime(workerStore1).Should().Be(_test.TestClock.UtcNow);
               });
        }

        protected Task RunTestAsync(
            int storeCount,
            Func<MetadataTestContext, Task> testFunc)
        {
            var context = new Context(Logger);

            return _test.RunTestAsync(
                context,
                storeCount,
                context => testFunc((MetadataTestContext)context));
        }

        private class InnerDistributedTest : LocalLocationStoreDistributedContentTests
        {
            public InnerDistributedTest(LocalRedisFixture redis, ITestOutputHelper output)
                : base(redis, output)
            {
                ConfigureWithOneMaster();
            }

            protected override TestContext ConfigureTestContext(TestContext context)
            {
                return new MetadataTestContext(context);
            }
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
                var startupResults = await TaskSafetyHelpers.WhenAll(Caches.Select(async store => await store.StartupAsync(Context)));
                Assert.True(startupResults.All(x => x.Succeeded), $"Failed to startup: {string.Join(Environment.NewLine, startupResults.Where(s => !s))}");

                CacheSessions = Caches.Select((store, id) => store.CreateSession(Context, GetSessionName(id, buildId), implicitPin).Session).ToList();
                await TaskSafetyHelpers.WhenAll(CacheSessions.Select(async session => await session.StartupAsync(Context)));

                Sessions = CacheSessions.ToList<IContentSession>();
            }

            protected override async Task ShutdownStoresAsync()
            {
                await TaskSafetyHelpers.WhenAll(Caches.Select(async store => await store.ShutdownAsync(Context)));

                foreach (var store in Caches)
                {
                    store.Dispose();
                }
            }
        }
    }
}
