// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Cache.Interfaces.Test;
using BuildXL.Cache.Interfaces;
using BuildXL.Cache.Tests;
using BuildXL.Cache.VerticalAggregator;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace BuildXL.Cache.InMemory.Test
{
    /// <summary>
    /// Class to test stacked vertical aggregators.
    /// </summary>
    /// <remarks>
    /// Uses two vertical aggregators to implement a 3 level cache.
    /// </remarks>
    public class MultipleVerticalAggregators : VerticalAggregatorBaseTests
    {
        protected override Type ReferenceType => typeof(TestInMemory);

        protected override Type TestType => typeof(TestInMemory);

        public override string NewCache(string cacheId, bool strictMetadataCasCoupling, bool authoritative = false)
        {
            return NewCache(cacheId, strictMetadataCasCoupling, false);
        }

        private string NewCache(string cacheId, bool strictMetadataCasCoupling, bool readOnly, bool wrapRemoteCaches = false)
        {
            // 3 layer cache.
            TestInMemory memoryCache = new TestInMemory();

            string l1ConfigString = memoryCache.NewCache(cacheId + "L1", false);
            string l2ConfigString = memoryCache.NewCache(cacheId + "L2", false);
            if (wrapRemoteCaches)
            {
                l2ConfigString = TestCallbackCache.FormatNewCacheConfig(l2ConfigString);
            }

            string l3ConfigString = memoryCache.NewCache(cacheId + "L3", strictMetadataCasCoupling, authoritative: true);
            if (wrapRemoteCaches)
            {
                l3ConfigString = TestCallbackCache.FormatNewCacheConfig(l3ConfigString);
            }

            string topVertConfig = NewCacheString(cacheId + "V2", l2ConfigString, l3ConfigString, false, false, false);
            if (wrapRemoteCaches)
            {
                topVertConfig = TestCallbackCache.FormatNewCacheConfig(topVertConfig);
            }

            string bottomVertConfig = NewCacheString(cacheId, l1ConfigString, topVertConfig, false, readOnly, false);

            return bottomVertConfig;
        }

        protected override Task<ICache> NewCacheAsync(string cacheId, BackingStoreTestClass localCacheTestType, BackingStoreTestClass remoteCacheTestType, bool remoteReadOnly = false)
        {
            string cacheConfig = NewCache(cacheId, true, remoteReadOnly);

            return InitializeCacheAsync(cacheConfig).SuccessAsync();
        }

        [Fact]
        public async Task DisconnectMostRemoteCacheAddNew()
        {
            string cacheId = "MutlipleCacheRemote";
            ICache testCache = await InitializeCacheAsync(NewCache(cacheId, true, false, true)).SuccessAsync();

            VerticalCacheAggregator lowerVert = testCache as VerticalCacheAggregator;
            XAssert.IsNotNull(lowerVert);

            CallbackCacheWrapper callbackCache = lowerVert.RemoteCache as CallbackCacheWrapper;
            XAssert.IsNotNull(callbackCache);

            VerticalCacheAggregator upperVert = callbackCache.WrappedCache as VerticalCacheAggregator;
            XAssert.IsNotNull(upperVert);

            VerticalAggregatorDisconnectTests.PoisonAllRemoteSessions(upperVert);

            ICacheSession session = await testCache.CreateSessionAsync().SuccessAsync();

            VerticalAggregatorDisconnectTests.DisconnectCache(upperVert.RemoteCache);

            FullCacheRecord cacheRecord = await FakeBuild.DoPipAsync(session, "Test Pip");

            // Now query each cache, and verify only the remote content is in each.
            foreach (var currentCache in new Tuple<ICache, CacheDeterminism, string, int>[]
            {
                new Tuple<ICache, CacheDeterminism, string, int>(lowerVert.LocalCache, CacheDeterminism.None, lowerVert.LocalCache.CacheId, 1),
                new Tuple<ICache, CacheDeterminism, string, int>(upperVert.LocalCache, CacheDeterminism.None, upperVert.LocalCache.CacheId, 1),
                new Tuple<ICache, CacheDeterminism, string, int>(upperVert.RemoteCache, CacheDeterminism.None, upperVert.RemoteCache.CacheId, 0),
            })
            {
                await ValidateItemsInCacheAsync(
                    currentCache.Item1,
                                                cacheRecord.StrongFingerprint.WeakFingerprint,
                                                new List<CasHash>(cacheRecord.CasEntries),
                                                currentCache.Item2,
                                                cacheRecord.StrongFingerprint.CasElement,
                                                currentCache.Item3,
                                                currentCache.Item4);
            }

            await testCache.ShutdownAsync().SuccessAsync();
        }

        [Fact]
        public async Task DisconnectMostRemoteCacheAddNewReconnect()
        {
            string cacheId = "MutlipleCacheRemote";
            ICache testCache = await InitializeCacheAsync(NewCache(cacheId, true, false, true)).SuccessAsync();

            VerticalCacheAggregator lowerVert = testCache as VerticalCacheAggregator;
            XAssert.IsNotNull(lowerVert);

            CallbackCacheWrapper callbackCache = lowerVert.RemoteCache as CallbackCacheWrapper;
            XAssert.IsNotNull(callbackCache);

            VerticalCacheAggregator upperVert = callbackCache.WrappedCache as VerticalCacheAggregator;
            XAssert.IsNotNull(upperVert);

            ICacheSession session = await testCache.CreateSessionAsync().SuccessAsync();

            VerticalAggregatorDisconnectTests.DisconnectCache(upperVert.RemoteCache);

            FullCacheRecord cacheRecord = await FakeBuild.DoPipAsync(session, "Test Pip");

            VerticalAggregatorDisconnectTests.ConnectCache(upperVert.RemoteCache);

            // Now query each cache, and verify only the remote content is in each.
            var aggregatorStats = new Dictionary<string, double>();
            var remoteDeterminism = CacheDeterminism.ViaCache(upperVert.RemoteCache.CacheGuid, CacheDeterminism.NeverExpires);

            foreach (var currentCache in new Tuple<ICache, CacheDeterminism, string, int, Dictionary<string, double>>[]
            {
                new Tuple<ICache, CacheDeterminism, string, int, Dictionary<string, double>>(testCache, remoteDeterminism, lowerVert.LocalCache.CacheId, 3, aggregatorStats),
                new Tuple<ICache, CacheDeterminism, string, int, Dictionary<string, double>>(lowerVert.LocalCache, remoteDeterminism, lowerVert.LocalCache.CacheId, 1, null),
                new Tuple<ICache, CacheDeterminism, string, int, Dictionary<string, double>>(upperVert.LocalCache, remoteDeterminism, upperVert.LocalCache.CacheId, 1, null),
                new Tuple<ICache, CacheDeterminism, string, int, Dictionary<string, double>>(upperVert.RemoteCache, remoteDeterminism, upperVert.RemoteCache.CacheId, 1, null)
            })
            {
                await ValidateItemsInCacheAsync(
                    currentCache.Item1,
                                                cacheRecord.StrongFingerprint.WeakFingerprint,
                                                new List<CasHash>(cacheRecord.CasEntries),
                                                currentCache.Item2,
                                                cacheRecord.StrongFingerprint.CasElement,
                                                currentCache.Item3,
                                                currentCache.Item4);
            }

            await testCache.ShutdownAsync().SuccessAsync();
        }

        [Fact]
        public async Task DisconnectMostRemoteAfterBuildReturnsMostRemoteCacheDeterminism()
        {
            string cacheId = "MutlipleCacheRemote";
            ICache testCache = await InitializeCacheAsync(NewCache(cacheId, true, false, true)).SuccessAsync();

            VerticalCacheAggregator lowerVert = testCache as VerticalCacheAggregator;
            XAssert.IsNotNull(lowerVert);

            CallbackCacheWrapper callbackCache = lowerVert.RemoteCache as CallbackCacheWrapper;
            XAssert.IsNotNull(callbackCache);

            VerticalCacheAggregator upperVert = callbackCache.WrappedCache as VerticalCacheAggregator;
            XAssert.IsNotNull(upperVert);

            ICacheSession session = await testCache.CreateSessionAsync().SuccessAsync();

            FullCacheRecord cacheRecord = await FakeBuild.DoPipAsync(session, "Test Pip");

            VerticalAggregatorDisconnectTests.DisconnectCache(upperVert.RemoteCache);

            // Now query each cache, and verify only the remote content is in each.
            var remoteDeterminism = CacheDeterminism.ViaCache(upperVert.RemoteCache.CacheGuid, CacheDeterminism.NeverExpires);
            foreach (var currentCache in new Tuple<ICache, CacheDeterminism, string, int>[]
            {
                new Tuple<ICache, CacheDeterminism, string, int>(testCache, remoteDeterminism, lowerVert.LocalCache.CacheId, 2),
                new Tuple<ICache, CacheDeterminism, string, int>(lowerVert.LocalCache, remoteDeterminism, lowerVert.LocalCache.CacheId, 1),
                new Tuple<ICache, CacheDeterminism, string, int>(upperVert.LocalCache, remoteDeterminism, upperVert.LocalCache.CacheId, 1),
                new Tuple<ICache, CacheDeterminism, string, int>(upperVert.RemoteCache, remoteDeterminism, upperVert.RemoteCache.CacheId, 1),
            })
            {
                await ValidateItemsInCacheAsync(
                    currentCache.Item1,
                                                cacheRecord.StrongFingerprint.WeakFingerprint,
                                                new List<CasHash>(cacheRecord.CasEntries),
                                                currentCache.Item2,
                                                cacheRecord.StrongFingerprint.CasElement,
                                                currentCache.Item3,
                                                currentCache.Item4);
            }

            // And make sure it flips back on re-connect.
            remoteDeterminism = CacheDeterminism.ViaCache(upperVert.RemoteCache.CacheGuid, CacheDeterminism.NeverExpires);
            VerticalAggregatorDisconnectTests.ConnectCache(upperVert.RemoteCache);

            foreach (var currentCache in new Tuple<ICache, CacheDeterminism, string, int>[]
            {
                new Tuple<ICache, CacheDeterminism, string, int>(testCache, CacheDeterminism.ViaCache(upperVert.RemoteCache.CacheGuid, CacheDeterminism.NeverExpires), lowerVert.LocalCache.CacheId, 3),
                new Tuple<ICache, CacheDeterminism, string, int>(lowerVert.LocalCache, CacheDeterminism.ViaCache(upperVert.RemoteCache.CacheGuid, CacheDeterminism.NeverExpires), lowerVert.LocalCache.CacheId, 1),
                new Tuple<ICache, CacheDeterminism, string, int>(upperVert.LocalCache, CacheDeterminism.ViaCache(upperVert.RemoteCache.CacheGuid, CacheDeterminism.NeverExpires), upperVert.LocalCache.CacheId, 1),
                new Tuple<ICache, CacheDeterminism, string, int>(upperVert.RemoteCache, remoteDeterminism, upperVert.RemoteCache.CacheId, 1),
            })
            {
                await ValidateItemsInCacheAsync(
                    currentCache.Item1,
                                                cacheRecord.StrongFingerprint.WeakFingerprint,
                                                new List<CasHash>(cacheRecord.CasEntries),
                                                currentCache.Item2,
                                                cacheRecord.StrongFingerprint.CasElement,
                                                currentCache.Item3,
                                                currentCache.Item4);
            }

            await testCache.ShutdownAsync().SuccessAsync();
        }

        #region standard tests

        /// <summary>
        /// After adding a fingerprint to an empty local, the FP information is available in both caches and is deterministic in the local cache.
        /// </summary>
        [Fact]
        public Task AddToEmptyCacheWithDeterministicRemote()
        {
            return AddToEmptyCacheAsync(false, BackingStoreTestClass.Memory, BackingStoreTestClass.Memory);
        }

        /// <summary>
        /// Adds deterministic to the local cache and verifies it is deterministic in both caches.
        /// </summary>
        [Fact]
        public Task AddDeterministicContentToEmptyCache()
        {
            return AddToEmptyCacheAsync(true, BackingStoreTestClass.Memory, BackingStoreTestClass.Memory);
        }

        /// <summary>
        /// A hit in a remote cache places the fp information into the local cache and marks it as deterministic before returning it.
        /// </summary>
        [Fact]
        public Task HitInDeterministicRemotePromotesToEmptyLocal()
        {
            return this.HitInDeterministicRemotePromotesToEmptyLocal(BackingStoreTestClass.Memory, BackingStoreTestClass.Memory);
        }

        /// <summary>
        /// A local cache hit that is marked as non-deterministic is replaced with content from a remote cache.
        /// </summary>
        [Fact]
        public Task NonDeterministicContentReplaced()
        {
            return this.NonDeterministicContentReplaced(BackingStoreTestClass.Memory, BackingStoreTestClass.Memory);
        }

        /// <summary>
        /// Adding a new fingerprint when the remote has a more deterministic output replaces the local value w/ the
        /// remote one.
        /// </summary>
        [Theory]
        [MemberData("BuildDeterminismMatrix")]
        public override Task AddingFpReplacedWithExistingRemote(
            BackingStoreTestClass localTestClass,
                                                                     BackingStoreTestClass remoteTestClass,
                                                                     CacheDeterminism initialDeterminismLocal,
                                                                     CacheDeterminism initialDeterminsimRemote,
                                                                     CacheDeterminism finalDeterminismLocal,
                                                                     CacheDeterminism finalDeterminismRemote)
        {
            return base.AddingFpReplacedWithExistingRemote(
                localTestClass,
                                                           remoteTestClass,
                                                           initialDeterminismLocal,
                                                           initialDeterminsimRemote,
                                                           finalDeterminismLocal,
                                                           finalDeterminismRemote);
        }

        protected static IEnumerable<object[]> BuildDeterminismMatrix()
        {
            for (int i = -1; i < determinisms.Length; i++)
            {
                int localIndex = Math.Max(0, i);
                CacheDeterminism startDeterminismLocal = determinisms[localIndex];
                CacheDeterminism startDeterminismRemote = determinisms[Math.Max(localIndex, 1)];
                CacheDeterminism endDetermismLocal = startDeterminismLocal.IsDeterministicTool || startDeterminismRemote.IsDeterministicTool ? CacheDeterminism.Tool : CacheDeterminism.ViaCache(RemoteReferenceGuild, CacheDeterminism.NeverExpires);
                CacheDeterminism endDeterminismRemote = startDeterminismRemote;

                yield return new object[] { BackingStoreTestClass.Self, BackingStoreTestClass.Self, startDeterminismLocal, startDeterminismRemote, endDetermismLocal, endDeterminismRemote };
            }
        }

        private static readonly CacheDeterminism[] determinisms = new CacheDeterminism[]
        {
            CacheDeterminism.None,
            CacheDeterminism.ViaCache(Guid.Parse("{E98CD792-5436-456B-92F5-63D635A3BFAC}"), CacheDeterminism.NeverExpires),
            CacheDeterminism.Tool
        };

        /// <summary>
        /// Fetching non-deterministic content from the local cache pushes content to empty remote cache,
        /// updates local cache to be deterministic, and returns determinstic content.
        /// </summary>
        [Theory]
        [MemberData("BuildDeterminismMatrix")]
        public override Task FetchingContentFromLocalCacheUpdatesRemoteCacheForDeterministicContentEmptyRemote(
            BackingStoreTestClass localTestClass,
                                                                                                                    BackingStoreTestClass remoteTestClass,
                                                                                                                    CacheDeterminism initialDeterminismLocal,
                                                                                                                    CacheDeterminism initialDeterminsimRemote,
                                                                                                                    CacheDeterminism finalDeterminismLocal,
                                                                                                                    CacheDeterminism finalDeterminismRemote)
        {
            return base.FetchingContentFromLocalCacheUpdatesRemoteCacheForDeterministicContentEmptyRemote(
                localTestClass,
                                                                                                                      remoteTestClass,
                                                                                                                      initialDeterminismLocal,
                                                                                                                      initialDeterminsimRemote,
                                                                                                                      finalDeterminismLocal,
                                                                                                                      finalDeterminismRemote);
        }

        /// <summary>
        /// Comin back online from the airplane scenario when both you and the remote have content.
        /// </summary>
        [Theory]
        [MemberData("BuildDeterminismMatrix")]
        public override Task FetchingContentFromLocalCacheUpdatesLocalCacheForDeterministicContentPopulatedRemote(
            BackingStoreTestClass localTestClass,
                                                                                                                        BackingStoreTestClass remoteTestClass,
                                                                                                                        CacheDeterminism initialDeterminismLocal,
                                                                                                                        CacheDeterminism initialDeterminismRemote,
                                                                                                                        CacheDeterminism finalDeterminismLocal,
                                                                                                                        CacheDeterminism finalDeterminismRemote)
        {
            return base.FetchingContentFromLocalCacheUpdatesLocalCacheForDeterministicContentPopulatedRemote(
                localTestClass,
                                                                                                                         remoteTestClass,
                                                                                                                         initialDeterminismLocal,
                                                                                                                         initialDeterminismRemote,
                                                                                                                         finalDeterminismLocal,
                                                                                                                         finalDeterminismRemote);
        }

        /// <summary>
        /// When cache hits happen in L1, the L2 may not see some or all of
        /// the cache hit requests (good thing) but that would prevent it from
        /// knowing of the use and being able to track the session, GC content,
        /// or LRU the data.  Thus, the aggregator can provide the L2 the set
        /// of fingerprints that were actually used during the session.
        ///
        /// Only of use for the cases where the L2 is not read-only and, of course,
        /// where the session is named (where such tracking is even possible).
        /// </summary>
        [Fact]
        public Task SessionRecordTransferTest()
        {
            return this.SessionRecordTransferTest(BackingStoreTestClass.Memory, BackingStoreTestClass.Memory);
        }

        [Fact]
        public Task EnsureSentinelReturnedDuringEnumeration()
        {
            return this.EnsureSentinelReturnedDuringEnumeration(BackingStoreTestClass.Memory, BackingStoreTestClass.Memory);
        }

        [Fact]
        public Task AggreatorReturnsRemoteCacheGuid()
        {
            return this.AggreatorReturnsRemoteCacheGuid(BackingStoreTestClass.Memory, BackingStoreTestClass.Memory);
        }

        // Remote is readonly, local is writeable.
        [Fact]
        public Task ReadOnlyRemoteIsNotUpdated()
        {
            return this.ReadOnlyRemoteIsNotUpdated(BackingStoreTestClass.Memory, BackingStoreTestClass.Memory);
        }

        [Fact]
        public Task ReadOnlyRemoteIsNotUpdatedForLocalHit()
        {
            return this.ReadOnlyRemoteIsNotUpdatedForLocalHit(BackingStoreTestClass.Memory, BackingStoreTestClass.Memory);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public Task CacheMiss(bool remoteReadOnly)
        {
            return this.CacheMiss(BackingStoreTestClass.Memory, BackingStoreTestClass.Memory, remoteReadOnly);
        }

        // Writing to protect against regression for bug fix.
        // Not making very generic as we can delete when SinglePhaseDeterministic goes away. Hopefully soon.
        [Fact]
        public Task ReadOnlyRemoteSinglePhaseRemoteAdd()
        {
            return this.ReadOnlyRemoteSinglePhaseRemoteAdd(BackingStoreTestClass.Memory, BackingStoreTestClass.Memory);
        }

        [Fact]
        public Task SinglePhaseDeterminismStaysSinglePhase()
        {
            return this.SinglePhaseDeterminismStaysSinglePhase(BackingStoreTestClass.Memory, BackingStoreTestClass.Memory);
        }

        [Fact]
        public Task RecordsAreIncooperated()
        {
            return this.RecordsAreIncooperated(BackingStoreTestClass.Memory, BackingStoreTestClass.Memory);
        }

        /// <summary>
        /// Adding a new fingerprint when the remote has a more deterministic output replaces the local value w/ the
        /// remote one.
        /// </summary>
        [Theory]
        [MemberData("BuildDeterminismMatrix")]
        public override Task AddingFpReplacedWithExistingRORemote(
            BackingStoreTestClass localTestClass,
                                                                     BackingStoreTestClass remoteTestClass,
                                                                     CacheDeterminism initialDeterminismLocal,
                                                                     CacheDeterminism initialDeterminsimRemote,
                                                                     CacheDeterminism finalDeterminismLocal,
                                                                     CacheDeterminism finalDeterminismRemote)
        {
            return base.AddingFpReplacedWithExistingRORemote(
                localTestClass,
                                                                      remoteTestClass,
                                                                      initialDeterminismLocal,
                                                                      initialDeterminsimRemote,
                                                                      finalDeterminismLocal,
                                                                      finalDeterminismRemote);
        }

        #endregion
    }
}
