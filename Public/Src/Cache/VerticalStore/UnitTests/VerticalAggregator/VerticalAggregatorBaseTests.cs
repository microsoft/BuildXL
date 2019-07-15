// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Diagnostics.Tracing;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.Interfaces;
using BuildXL.Cache.VerticalAggregator;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace BuildXL.Cache.Tests
{
    /// <summary>
    /// Tests for the vertical aggregating cache.
    /// </summary>
    /// <remarks>
    /// The public methods are marked as [Fact] to require derived classes to override as [Theory] and provide
    /// factories.
    /// </remarks>
    public abstract class VerticalAggregatorBaseTests : TestCacheCore, IDisposable
    {
        private ICache m_testCache;
        public const string LocalMarker = "Local";
        public const string RemoteMarker = "Remote";

        private static readonly string VerticalAggregatorConfigJSONData = @"{{ 
                ""Assembly"":""BuildXL.Cache.VerticalAggregator"",
                ""Type"": ""BuildXL.Cache.VerticalAggregator.VerticalCacheAggregatorFactory"",
                ""RemoteIsReadOnly"":{0},
                ""PreFetchCasData"":{1},
                ""LocalCache"":{2},
	            ""RemoteCache"":{3},
                ""WriteThroughCasData"":{4},
                ""FailIfRemoteFails"":{5}
            }}";

        protected override IEnumerable<EventSource> EventSources => new[] { VerticalCacheAggregator.EventSource };

        protected virtual bool CacheStoreCannotBeRemote => false;

        protected static readonly Guid RemoteReferenceGuild = Guid.Parse("{8799C847-F7DA-4F6E-99C8-E79AE0C9DAE1}");

        /// <summary>
        /// Returns the type of the test class to get the configuration for the backing store being tested from.
        /// </summary>
        protected abstract Type TestType { get; }

        /// <summary>
        /// Returns the type of the reference test type.
        /// </summary>
        /// <remarks>
        /// This is used as the reference test type for aggregator tests.
        /// Most (all really) derived classes should return the type of the InMemory test class.
        /// </remarks>
        protected abstract Type ReferenceType { get; }

        public override async Task<ICache> CreateCacheAsync(string cacheId, bool strictMetadataCasCoupling = true)
        {
            string cacheConfigData = NewCache(cacheId, strictMetadataCasCoupling);

            Possible<ICache, Failure> cachePossible = await InitializeCacheAsync(cacheConfigData);

            ICache cache = cachePossible.Success();
            XAssert.AreEqual(cacheId + LocalMarker + "_" + cacheId + RemoteMarker, cache.CacheId);

            return cache;
        }

        public override string NewCache(string cacheId, bool strictMetadataCasCoupling, bool authoritative = false)
        {
            throw new NotImplementedException("Nothing should be calling this.");
        }

        private TestCacheCore GetCacheTestClass(BackingStoreTestClass cacheTestType)
        {
            Type type;

            switch (cacheTestType)
            {
                case BackingStoreTestClass.Memory:
                    type = ReferenceType;
                    break;
                case BackingStoreTestClass.Self:
                    type = TestType;
                    break;
                default:
                    throw new NotImplementedException($"BackingStoreTestClass {cacheTestType} has not been implemented");
            }

            return (TestCacheCore)Activator.CreateInstance(type);
        }

        protected virtual Task<ICache> NewCacheAsync(string cacheId, BackingStoreTestClass localCacheTestType, BackingStoreTestClass remoteCacheTestType, bool remoteReadOnly = false)
        {
            return NewCacheAsync(cacheId, GetCacheTestClass(localCacheTestType), GetCacheTestClass(remoteCacheTestType), remoteReadOnly);
        }

        private async Task<ICache> NewCacheAsync(string cacheId, TestCacheCore localTestClass, TestCacheCore remoteTestClass, bool remoteReadOnly = false)
        {
            string localCacheConfigJSONDataString = localTestClass.NewCache(cacheId + LocalMarker, false);
            string remoteCacheConfigJSONDataString = remoteTestClass.NewCache(cacheId + RemoteMarker, true, authoritative: true);

            string verticalAggregatorCacheConfigJSONDataString = NewCacheString(cacheId, localCacheConfigJSONDataString, remoteCacheConfigJSONDataString, false, remoteReadOnly, false);

            Possible<ICache, Failure> verticalCache = await InitializeCacheAsync(verticalAggregatorCacheConfigJSONDataString);

            m_testCache = verticalCache.Success();

            InspectAggregatorCache(m_testCache as VerticalCacheAggregator);

            return m_testCache;
        }

        private void InspectAggregatorCache(VerticalCacheAggregator cache)
        {
            if (cache != null)
            {
                var aggregator = cache.LocalCache as VerticalCacheAggregator;
                if (aggregator != null)
                {
                    InspectAggregatorCache(aggregator);
                }
                else
                {
                    InspectLocalCache(cache.LocalCache);
                }

                aggregator = cache.RemoteCache as VerticalCacheAggregator;
                if (aggregator != null)
                {
                    InspectAggregatorCache(aggregator);
                }
                else
                {
                    InspectRemoteCache(cache.RemoteCache);
                }
            }
        }

        protected virtual void InspectRemoteCache(ICache cacheRemoteCache)
        {
        }

        protected virtual void InspectLocalCache(ICache cacheLocalCache)
        {
        }

        public static string NewCacheString(
            string cacheId,
                                            string localCacheConfigJSONDataString,
                                            string remoteCacheConfigJSONDataString,
                                            bool prefetchCacheData,
                                            bool remoteReadOnly,
                                            bool writeThroughCasData,
                                            bool failIfRemoteFails = true)
        {
            string verticalAggregatorCacheConfigJSONDataString = string.Format(
                VerticalAggregatorConfigJSONData,
                                                                               remoteReadOnly.ToString().ToLowerInvariant(),
                                                                               prefetchCacheData.ToString().ToLowerInvariant(),
                                                                               localCacheConfigJSONDataString,
                                                                               remoteCacheConfigJSONDataString,
                                                                               writeThroughCasData.ToString().ToLowerInvariant(),
                                                                               failIfRemoteFails.ToString().ToLowerInvariant());

            return verticalAggregatorCacheConfigJSONDataString;
        }

        protected async Task AddToEmptyCacheAsync(
            bool contentIsDeterministic,
                                           BackingStoreTestClass localCacheTestClass,
                                           BackingStoreTestClass remoteCacheTestClass)
        {
            if (CacheStoreCannotBeRemote && remoteCacheTestClass == BackingStoreTestClass.Self)
            {
                return;
            }

            string testCacheId = "TestCache";
            var localTestClass = GetCacheTestClass(localCacheTestClass);
            var remoteTestClass = GetCacheTestClass(remoteCacheTestClass);
            ICache testCache = await NewCacheAsync(testCacheId, localTestClass, remoteTestClass);
            ICache remoteCache = GetMostRemoteCache(testCache);

            VerticalCacheAggregator vertCache = testCache as VerticalCacheAggregator;
            XAssert.IsNotNull(vertCache);

            CacheDeterminism localDeterminism = CacheDeterminism.ViaCache(vertCache.RemoteCache.CacheGuid, CacheDeterminism.NeverExpires);

            if (contentIsDeterministic)
            {
                localDeterminism = CacheDeterminism.Tool;
            }

            CacheDeterminism initialDeterminism = contentIsDeterministic ? CacheDeterminism.Tool : CacheDeterminism.None;

            ICacheSession session = (await testCache.CreateSessionAsync()).Success();

            FullCacheRecord cacheRecord = await FakeBuild.DoPipAsync(session, "TestPip", determinism: initialDeterminism);

            var remoteDeterminism = localDeterminism;
            await session.CloseAsync().SuccessAsync();

            int expectedRemoteSenintels = session.CacheId.Split('_').Length - 2;

            var expectedStats = new Dictionary<string, double>();

            expectedStats.Add(
                "EnumerateStrongFingerprints_Local_Sum",
                (localTestClass.ReturnsSentinelWhenEmpty ? 1 : 0) + (remoteTestClass.ReturnsSentinelWhenEmpty ? 1 : 0));
            expectedStats.Add("EnumerateStrongFingerprints_Remote_Sum", expectedRemoteSenintels);
            expectedStats.Add("FilesTransitedToRemote_Filecount_Sum", 4);
            expectedStats.Add("AddOrGet_FingerprintsAddedRemote_Count", 1);

            ValidateStatistics(session, expectedStats);

            // Make sure the content is in each cache.
            foreach (var currentCache in new Tuple<ICache, CacheDeterminism, string>[]
            {
                new Tuple<ICache, CacheDeterminism, string>(vertCache.LocalCache, localDeterminism, vertCache.LocalCache.CacheId),
                new Tuple<ICache, CacheDeterminism, string>(remoteCache, remoteDeterminism, remoteCache.CacheId)
            })
            {
                await ValidateItemsInCacheAsync(
                    currentCache.Item1,
                                                cacheRecord.StrongFingerprint.WeakFingerprint,
                                                new List<CasHash>(cacheRecord.CasEntries),
                                                currentCache.Item2,
                                                cacheRecord.StrongFingerprint.CasElement,
                                                currentCache.Item3,
                                                1);
            }

            XAssert.IsTrue((await testCache.ShutdownAsync()).Succeeded);
        }

        /// <summary>
        /// Validates the statistics are correct for the passed in session
        /// </summary>
        /// <param name="session">A closed session</param>
        /// <param name="statsToCheck">Dictionary of statistics to check</param>
        public static void ValidateStatistics(ICacheReadOnlySession session, Dictionary<string, double> statsToCheck)
        {
            var statistics = session.GetStatisticsAsync().SuccessAsync().Result;

            CacheSessionStatistics cacheStats = default(CacheSessionStatistics);

            foreach (CacheSessionStatistics stats in statistics)
            {
                if (stats.CacheId == session.CacheId)
                {
                    cacheStats = stats;
                    break;
                }
            }

            XAssert.AreNotEqual(statsToCheck, default(CacheSessionStatistics), "Failed to find cache statistics for cache {0}", session.CacheId);

            var statsDictionary = cacheStats.Statistics;

            foreach (string key in statsToCheck.Keys)
            {
                XAssert.IsTrue(statsDictionary.ContainsKey(key), "Returned Dictionary did not contain key {0}", key);
                XAssert.AreEqual(statsToCheck[key], statsDictionary[key], "Values for key {0} were not equal.", key);
            }
        }

        public static async Task ValidateItemsInCacheAsync(
            ICache cache,
                                                WeakFingerprintHash weakFingerprintToValidate,
                                                IReadOnlyList<CasHash> associatedCasEntries,
                                                CacheDeterminism determinismSource,
                                                CasHash fileContentsHash,
                                                string cacheIdOfFirstHit,
                                                int expectedFingerprints,
                                                Dictionary<string, double> statsToVerify = null)
        {
            Contract.Requires(cacheIdOfFirstHit != null);

            ICacheReadOnlySession currentSession = cache.IsReadOnly ? (await cache.CreateReadOnlySessionAsync()).Success() : (await cache.CreateSessionAsync()).Success();
            if (cacheIdOfFirstHit == null)
            {
                cacheIdOfFirstHit = cache.CacheId;
            }

            StrongFingerprint firstSfp = null;
            int numFingerprints = 0;

            foreach (var sfpTask in currentSession.EnumerateStrongFingerprints(weakFingerprintToValidate))
            {
                var sfp = (await sfpTask).Success();

                if (sfp is StrongFingerprintSentinel)
                {
                    continue;
                }

                numFingerprints++;

                if (firstSfp == null)
                {
                    firstSfp = sfp;

                    bool found = false;

                    // Split incase the cache passed in an aggregated cache name, in that case
                    // any cache in the list will do. more exacting tests should be used if
                    // the goal is to target an exact cache.
                    foreach (string oneCache in cacheIdOfFirstHit.Split('_'))
                    {
                        if (oneCache == firstSfp.CacheId)
                        {
                            found = true;
                        }
                    }

                    XAssert.IsTrue(found, "The first StrongFingerprint hit must be from the predicted cache, it {0} was not found in cache {1}", firstSfp.CacheId, cacheIdOfFirstHit);
                }

                XAssert.AreEqual(firstSfp, sfp, "Multiple strong fingerprints were returned");

                var fpRecord = (await currentSession.GetCacheEntryAsync(sfp)).Success();

                XAssert.AreEqual(fpRecord.Count, associatedCasEntries.Count);
                for (int i = 0; i < associatedCasEntries.Count; i++)
                {
                    XAssert.AreEqual(associatedCasEntries[i], fpRecord[i]);
                }

                XAssert.AreEqual(
                    determinismSource.EffectiveGuid,
                                 fpRecord.Determinism.EffectiveGuid,
                                 "Validate that the correct caches have the determinism bit set for cache {0}",
                                 cache.CacheId);

                // Make sure the CAS entries are where we want them.
                if (!fileContentsHash.Equals(CasHash.NoItem))
                {
                    await FakeBuild.CheckContentsAsync(currentSession, fileContentsHash, fpRecord);
                }
            }

            XAssert.AreEqual(expectedFingerprints, numFingerprints, "Number of weak fingerprint hits was not as expected");
            (await currentSession.CloseAsync()).Success();

            if (statsToVerify != null)
            {
                ValidateStatistics(currentSession, statsToVerify);
            }
        }

        /// <summary>
        /// A hit in a remote cache places the fp information into the local cache and marks it as deterministic before returning it.
        /// </summary>
        public virtual async Task HitInDeterministicRemotePromotesToEmptyLocal(BackingStoreTestClass localCacheTestClass, BackingStoreTestClass remoteCacheTestClass)
        {
            if (CacheStoreCannotBeRemote && remoteCacheTestClass == BackingStoreTestClass.Self)
            {
                return;
            }

            string testCacheId = "TestCache";
            ICache testCache = await NewCacheAsync(testCacheId, localCacheTestClass, remoteCacheTestClass);
            ICache remoteCache = GetMostRemoteCache(testCache);

            VerticalCacheAggregator vertCache = testCache as VerticalCacheAggregator;
            XAssert.IsNotNull(vertCache);

            ICacheSession aggregatorSession = (await testCache.CreateSessionAsync()).Success();
            ICacheSession localSession = (await vertCache.LocalCache.CreateSessionAsync()).Success();
            ICacheSession remoteSession = (await remoteCache.CreateSessionAsync()).Success();

            CacheDeterminism determinismSource = CacheDeterminism.ViaCache(vertCache.CacheGuid, CacheDeterminism.NeverExpires);

            FullCacheRecord cacheRecord = await FakeBuild.DoPipAsync(remoteSession, "TestPip");

            Dictionary<string, double> statsToCheck = new Dictionary<string, double>();
            statsToCheck.Add("GetCacheEntry_HitRemote_Count", 1);

            // Make sure the content is in each cache. (Placing the aggregator cache first will cause backfill of the local cache)
            foreach (var currentCache in new Tuple<ICache, CacheDeterminism, string, int, Dictionary<string, double>>[]
            {
                new Tuple<ICache, CacheDeterminism, string, int, Dictionary<string, double>>(vertCache, determinismSource, vertCache.RemoteCache.CacheId, 1, statsToCheck),
                new Tuple<ICache, CacheDeterminism, string, int, Dictionary<string, double>>(vertCache.LocalCache, determinismSource, vertCache.LocalCache.CacheId, 1, null),
                new Tuple<ICache, CacheDeterminism, string, int, Dictionary<string, double>>(remoteCache, determinismSource, vertCache.RemoteCache.CacheId, 1, null)
            })
            {
                await ValidateItemsInCacheAsync(
                    currentCache.Item1,
                                                cacheRecord.StrongFingerprint.WeakFingerprint,
                                                new List<CasHash>(cacheRecord.CasEntries),
                                                currentCache.Item2,
                                                cacheRecord.StrongFingerprint.CasElement,
                                                currentCache.Item3,
                                                currentCache.Item4,
                                                currentCache.Item5);
            }

            XAssert.IsTrue((await testCache.ShutdownAsync()).Succeeded);
        }

        /// <summary>
        /// A local cache hit that is marked as non-deterministic is replaced with content from a remote cache.
        /// </summary>
        public virtual async Task NonDeterministicContentReplaced(BackingStoreTestClass localCacheTestClass, BackingStoreTestClass remoteCacheTestClass)
        {
            if (CacheStoreCannotBeRemote && remoteCacheTestClass == BackingStoreTestClass.Self)
            {
                return;
            }

            string testCacheId = "TestCacheNonDeterministicContentReplaced";
            ICache testCache = await NewCacheAsync(testCacheId, localCacheTestClass, remoteCacheTestClass);
            ICache remoteCache = GetMostRemoteCache(testCache);

            VerticalCacheAggregator vertCache = testCache as VerticalCacheAggregator;
            XAssert.IsNotNull(vertCache);

            ICacheSession aggregatorSession = (await testCache.CreateSessionAsync()).Success();
            ICacheSession localSession = (await vertCache.LocalCache.CreateSessionAsync()).Success();
            ICacheSession remoteSession = (await remoteCache.CreateSessionAsync()).Success();

            CacheDeterminism determinismSource = CacheDeterminism.ViaCache(vertCache.CacheGuid, CacheDeterminism.NeverExpires);

            const string PipName = "TestPip";
            int pipSize = 3;

            // Populate the remote cache with one set of outputs.
            FullCacheRecord remoteCacheRecord = await FakeBuild.DoNonDeterministicPipAsync(remoteSession, PipName, pipSize: pipSize, generateVerifiablePip: true);

            // And the local cache with a set forced to be unique.
            FullCacheRecord localCacheRecord = await FakeBuild.DoNonDeterministicPipAsync(localSession, PipName);
            Console.WriteLine("Local: {0}, Remote: {1}", localCacheTestClass, remoteCacheTestClass);

            int cacheDepth = testCache.CacheId.Split('_').Length;

            Dictionary<string, double> statsToCheck = new Dictionary<string, double>();
            statsToCheck.Add("GetCacheEntry_DeterminismRecovered_Count", 1);
            statsToCheck.Add("FilesTransitedToLocal_Filecount_Sum", pipSize);
            statsToCheck.Add("PinToCas_HitLocal_Count", ((pipSize + 1) * cacheDepth) - pipSize); // pipSize + 1 CAS entries per pip, one hit for each layer of the cache, except the first hit downlaods the pip size.
            statsToCheck.Add("PinToCas_HitRemote_Count", pipSize);
            statsToCheck.Add("GetStream_HitLocal_Count", ((pipSize + 1) * cacheDepth) - pipSize); // pipSize + 1 CAS entries per pip, one hit for each layer of the cache, except the first hit downlaods the pip size.
            statsToCheck.Add("GetStream_HitRemote_Count", pipSize);

            var remoteDeterminism = determinismSource;

            // Now query each cache, and verify only the remote content is in each.
            // Make sure the content is in each cache. (Placing the aggregator cache first will cause backfill of the local cache)
            foreach (var currentCache in new Tuple<ICache, CacheDeterminism, string, int, Dictionary<string, double>>[]
            {
                new Tuple<ICache, CacheDeterminism, string, int, Dictionary<string, double>>(vertCache, determinismSource, vertCache.LocalCache.CacheId, vertCache.CacheId.Split('_').Length, statsToCheck),
                new Tuple<ICache, CacheDeterminism, string, int, Dictionary<string, double>>(vertCache.LocalCache, determinismSource, vertCache.LocalCache.CacheId, 1, null),
                new Tuple<ICache, CacheDeterminism, string, int, Dictionary<string, double>>(remoteCache, remoteDeterminism, remoteCache.CacheId, 1, null)
            })
            {
                await ValidateItemsInCacheAsync(
                    currentCache.Item1,
                                                remoteCacheRecord.StrongFingerprint.WeakFingerprint,
                                                new List<CasHash>(remoteCacheRecord.CasEntries),
                                                currentCache.Item2,
                                                remoteCacheRecord.StrongFingerprint.CasElement,
                                                currentCache.Item3,
                                                currentCache.Item4,
                                                currentCache.Item5);
            }

            XAssert.IsTrue((await testCache.ShutdownAsync()).Succeeded);
        }

        /// <summary>
        /// Adding a new fingerprint when the remote has a more deterministic output replaces the local value w/ the
        /// remote one.
        /// </summary>
        public virtual async Task AddingFpReplacedWithExistingRemote(
            BackingStoreTestClass localTestClass,
                                                                     BackingStoreTestClass remoteTestClass,
                                                                     CacheDeterminism initialDeterminismLocal,
                                                                     CacheDeterminism initialDeterminsimRemote,
                                                                     CacheDeterminism finalDeterminismLocal,
                                                                     CacheDeterminism finalDeterminismRemote)
        {
            if (CacheStoreCannotBeRemote && remoteTestClass == BackingStoreTestClass.Self)
            {
                return;
            }

            string testCacheId = "TestCacheAddingFpReplacedWithExistingRemote";
            ICache testCache = await NewCacheAsync(testCacheId, localTestClass, remoteTestClass);
            ICache remoteCache = GetMostRemoteCache(testCache);

            VerticalCacheAggregator vertCache = testCache as VerticalCacheAggregator;
            XAssert.IsNotNull(vertCache);

            ICacheSession aggregatorSession = (await testCache.CreateSessionAsync()).Success();
            ICacheSession localSession = (await vertCache.LocalCache.CreateSessionAsync()).Success();
            ICacheSession remoteSession = (await remoteCache.CreateSessionAsync()).Success();

            if (finalDeterminismLocal.Guid == RemoteReferenceGuild)
            {
                finalDeterminismLocal = CacheDeterminism.ViaCache(vertCache.CacheGuid, CacheDeterminism.NeverExpires);
            }

            const string PipName = "TestPip";

            // Nothing should be returned for this as the add should have worked.
            FullCacheRecord cacheRecord = await FakeBuild.DoPipAsync(remoteSession, PipName, determinism: initialDeterminsimRemote);

            // Generate the local file streams.
            FakeBuild fb = new FakeBuild(PipName, cacheRecord.CasEntries.Count, forceUniqueOutputs: true);
            XAssert.AreEqual(cacheRecord.CasEntries.Count, fb.Outputs.Length, "Both the builds must have the same number of output files.");

            // Add the files
            CasHash casElementHash = await aggregatorSession.AddToCasAsync(fb.OutputList).SuccessAsync();
            XAssert.AreEqual(cacheRecord.StrongFingerprint.CasElement, casElementHash, "Remote build and new build's CasElement must have the same hash");

            List<CasHash> aggregatorCasHashes = new List<CasHash>(fb.Outputs.Length);

            foreach (Stream s in fb.Outputs)
            {
                aggregatorCasHashes.Add(await aggregatorSession.AddToCasAsync(s).SuccessAsync());
            }

            // Place the contents in the aggregator. Should return the existing data from the remote, and place it in
            // the local cache.
            FullCacheRecordWithDeterminism aggregatorCacheRecord = await aggregatorSession.AddOrGetAsync(
                cacheRecord.StrongFingerprint.WeakFingerprint,
                                                                                          cacheRecord.StrongFingerprint.CasElement,
                                                                                          cacheRecord.StrongFingerprint.HashElement,
                                                                                          aggregatorCasHashes.ToArray()).SuccessAsync();

            XAssert.AreNotEqual(null, aggregatorCacheRecord);
            XAssert.AreEqual(aggregatorCacheRecord.Record.CacheId, vertCache.RemoteCache.CacheId, "Cache record returned was not from remote cache ({0}) but was from ({1})", vertCache.RemoteCache.CacheId, cacheRecord.CacheId);
            XAssert.AreEqual(aggregatorCacheRecord.Record.CasEntries.Count, fb.Outputs.Length, "Count of files returned was not correct");

            await aggregatorSession.CloseAsync().SuccessAsync();

            Dictionary<string, double> statsToCheck = new Dictionary<string, double>();
            statsToCheck.Add("AddOrGet_DeterminismRecovered_Count", 1);

            ValidateStatistics(aggregatorSession, statsToCheck);

            // Now query each cache, and verify only the remote content is in each.
            // Make sure the content is in each cache. (Placing the aggregator cache first will cause backfill of the local cache)
            foreach (var currentCache in new Tuple<ICache, CacheDeterminism, string, int>[]
            {
                new Tuple<ICache, CacheDeterminism, string, int>(vertCache, finalDeterminismLocal, vertCache.LocalCache.CacheId, vertCache.CacheId.Split('_').Length),
                new Tuple<ICache, CacheDeterminism, string, int>(vertCache.LocalCache, finalDeterminismLocal, vertCache.LocalCache.CacheId, 1),
                new Tuple<ICache, CacheDeterminism, string, int>(remoteCache, finalDeterminismLocal, vertCache.RemoteCache.CacheId, 1)
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

            XAssert.IsTrue((await testCache.ShutdownAsync()).Succeeded);
        }

        /// <summary>
        /// Fetching non-deterministic content from the local cache pushes content to empty remote cache,
        /// updates local cache to be deterministic, and returns determinstic content.
        /// </summary>
        public virtual async Task FetchingContentFromLocalCacheUpdatesRemoteCacheForDeterministicContentEmptyRemote(
            BackingStoreTestClass localTestClass,
            BackingStoreTestClass remoteTestClass,
            CacheDeterminism initialDeterminismLocal,
            CacheDeterminism initialDeterminsimRemote,
            CacheDeterminism finalDeterminismLocal,
            CacheDeterminism finalDeterminismRemote)
        {
            if (CacheStoreCannotBeRemote && remoteTestClass == BackingStoreTestClass.Self)
            {
                return;
            }

            // Ok, so we build this big matrix. And it's got a hole...
            if (!initialDeterminismLocal.IsDeterministicTool && finalDeterminismRemote.IsDeterministicTool)
            {
                return;
            }

            string testCacheId = "TestCacheNonDeterministicContentReplaced";
            ICache testCache = await NewCacheAsync(testCacheId, localTestClass, remoteTestClass);
            ICache remoteCache = GetMostRemoteCache(testCache);

            VerticalCacheAggregator vertCache = testCache as VerticalCacheAggregator;
            XAssert.IsNotNull(vertCache);

            ICacheSession localSession = (await vertCache.LocalCache.CreateSessionAsync()).Success();

            if (finalDeterminismLocal.Guid == RemoteReferenceGuild)
            {
                finalDeterminismLocal = CacheDeterminism.ViaCache(vertCache.CacheGuid, CacheDeterminism.NeverExpires);
            }

            finalDeterminismRemote = CacheDeterminism.None;

            if (initialDeterminismLocal.IsDeterministicTool)
            {
                finalDeterminismRemote = CacheDeterminism.Tool;
            }

            var remoteDeterminism = finalDeterminismLocal;

            const string PipName = "TestPip";

            // Nothing should be returned for this as the add should have worked.
            FullCacheRecord cacheRecord = await FakeBuild.DoPipAsync(localSession, PipName, determinism: initialDeterminismLocal);

            int expectedHitsInAggregator = initialDeterminismLocal.IsDeterministicTool ? 1 : testCache.CacheId.Split('_').Length;

            // Tool deterministic content is not promoted on get.
            Dictionary<string, double> statsToCheck = new Dictionary<string, double>();
            statsToCheck.Add("FilesTransitedToRemote_Filecount_Sum", initialDeterminismLocal.IsDeterministicTool ? 0 : 4);
            statsToCheck.Add("GetCacheEntry_FingerprintsPromotedRemote_Count", initialDeterminismLocal.IsDeterministicTool ? 0 : 1);

            // Now query each cache, and verify only the remote content is in each.
            // Make sure the content is in each cache. (Placing the aggregator cache first will cause backfill of the local cache)
            foreach (var currentCache in new Tuple<ICache, CacheDeterminism, string, int, Dictionary<string, double>>[]
            {
                new Tuple<ICache, CacheDeterminism, string, int, Dictionary<string, double>>(vertCache, finalDeterminismLocal, vertCache.LocalCache.CacheId, expectedHitsInAggregator, statsToCheck),
                new Tuple<ICache, CacheDeterminism, string, int, Dictionary<string, double>>(vertCache.LocalCache, finalDeterminismLocal, vertCache.LocalCache.CacheId, 1, null),
                new Tuple<ICache, CacheDeterminism, string, int, Dictionary<string, double>>(remoteCache, remoteDeterminism, remoteCache.CacheId, initialDeterminismLocal.IsDeterministicTool ? 0 : 1, null)
            })
            {
                await ValidateItemsInCacheAsync(
                    currentCache.Item1,
                                                cacheRecord.StrongFingerprint.WeakFingerprint,
                                                new List<CasHash>(cacheRecord.CasEntries),
                                                currentCache.Item2,
                                                cacheRecord.StrongFingerprint.CasElement,
                                                currentCache.Item3,
                                                currentCache.Item4,
                                                currentCache.Item5);
            }

            XAssert.IsTrue((await testCache.ShutdownAsync()).Succeeded);
        }

        /// <summary>
        /// Comin back online from the airplane scenario when both you and the remote have content.
        /// </summary>
        public virtual async Task FetchingContentFromLocalCacheUpdatesLocalCacheForDeterministicContentPopulatedRemote(
            BackingStoreTestClass localTestClass,
                                                                                                                       BackingStoreTestClass remoteTestClass,
                                                                                                                       CacheDeterminism initialDeterminismLocal,
                                                                                                                       CacheDeterminism initialDeterminismRemote,
                                                                                                                       CacheDeterminism finalDeterminismLocal,
                                                                                                                       CacheDeterminism finalDeterminismRemote)
        {
            if (CacheStoreCannotBeRemote && remoteTestClass == BackingStoreTestClass.Self)
            {
                return;
            }

            string testCacheId = "TestCache";
            ICache testCache = await NewCacheAsync(testCacheId, localTestClass, remoteTestClass);
            ICache remoteCache = GetMostRemoteCache(testCache);

            VerticalCacheAggregator vertCache = testCache as VerticalCacheAggregator;
            XAssert.IsNotNull(vertCache);

            ICacheSession localSession = (await vertCache.LocalCache.CreateSessionAsync()).Success();
            ICacheSession remoteSession = (await remoteCache.CreateSessionAsync()).Success();

            if (finalDeterminismLocal.Guid == RemoteReferenceGuild)
            {
                finalDeterminismLocal = CacheDeterminism.ViaCache(vertCache.CacheGuid, CacheDeterminism.NeverExpires);
            }

            const string PipName = "TestPip";

            // Nothing should be returned for this as the add should have worked.
            FullCacheRecord cacheRecord = await FakeBuild.DoNonDeterministicPipAsync(localSession, PipName, determinism: initialDeterminismLocal);

            FullCacheRecord remoteRecord = await FakeBuild.DoNonDeterministicPipAsync(remoteSession, PipName, determinism: initialDeterminismRemote, generateVerifiablePip: true);

            // If they're both Tool Deterministic, they need to actually have the same content. Or we're just setting ourselves up in an invalid state.
            // (i.e., a tool that's lying about its determinism)
            if (initialDeterminismLocal.IsDeterministicTool && initialDeterminismLocal.EffectiveGuid == initialDeterminismRemote.EffectiveGuid)
            {
                cacheRecord = await FakeBuild.DoPipAsync(localSession, PipName, determinism: initialDeterminismLocal);
                remoteRecord = await FakeBuild.DoPipAsync(remoteSession, PipName, determinism: initialDeterminismLocal);
            }

            XAssert.AreEqual(cacheRecord.StrongFingerprint, remoteRecord.StrongFingerprint, "Both caches must have the same StrongFingerprint used");

            var remoteDeterminism = finalDeterminismLocal;
            Dictionary<string, double> statsToCheck = new Dictionary<string, double>();
            statsToCheck.Add("GetCacheEntry_DeterminismRecovered_Count", initialDeterminismLocal.IsDeterministicTool ? 0 : 1);

            // Now query each cache, and verify only the remote content is in each.
            // Make sure the content is in each cache. (Placing the aggregator cache first will cause backfill of the local cache)
            foreach (var currentCache in new Tuple<ICache, CacheDeterminism, string, int, Dictionary<string, double>>[]
            {
                // If it's tool deterministic, a get will not push it upstream, so it will be in the local and the remote. Anything else will result in a enumerate / get loop wich will populate any middle layers.
                new Tuple<ICache, CacheDeterminism, string, int, Dictionary<string, double>>(vertCache, finalDeterminismLocal, vertCache.LocalCache.CacheId, initialDeterminismLocal.IsDeterministicTool ? 2 : vertCache.CacheId.Split('_').Length, statsToCheck),
                new Tuple<ICache, CacheDeterminism, string, int, Dictionary<string, double>>(vertCache.LocalCache, finalDeterminismLocal, vertCache.LocalCache.CacheId, 1, null),
                new Tuple<ICache, CacheDeterminism, string, int, Dictionary<string, double>>(remoteCache, remoteDeterminism, vertCache.RemoteCache.CacheId, 1, null)
            })
            {
                await ValidateItemsInCacheAsync(
                    currentCache.Item1,
                                                cacheRecord.StrongFingerprint.WeakFingerprint,
                                                new List<CasHash>(remoteRecord.CasEntries),
                                                currentCache.Item2,
                                                cacheRecord.StrongFingerprint.CasElement,
                                                currentCache.Item3,
                                                currentCache.Item4,
                                                currentCache.Item5);
            }

            XAssert.IsTrue((await testCache.ShutdownAsync()).Succeeded);
        }

        private ICache GetMostRemoteCache(ICache rootCache)
        {
            while (rootCache is VerticalCacheAggregator)
            {
                rootCache = ((VerticalCacheAggregator)rootCache).RemoteCache;
            }

            return rootCache;
        }

        private async Task<HashSet<T>> Collect<T>(IEnumerable<Task<T>> items)
        {
            HashSet<T> set = new HashSet<T>();
            foreach (Task<T> taskItem in items)
            {
                set.Add(await taskItem);
            }

            return set;
        }

        private HashSet<FullCacheRecord> GatherRecords(ICacheReadOnlySession readOnlySession, HashSet<StrongFingerprint> strongFingerprints)
        {
            HashSet<FullCacheRecord> records = new HashSet<FullCacheRecord>();
            foreach (var strongFingerprint in strongFingerprints)
            {
                var casEntries = readOnlySession.GetCacheEntryAsync(strongFingerprint).Result.Success();
                records.Add(new FullCacheRecord(strongFingerprint, casEntries));
            }

            return records;
        }

        private async Task<string> ValidateSession(VerticalCacheAggregator cache, ICacheSession session)
        {
            string sessionId = (await session.CloseAsync()).Success();

            if (!ImplementsTrackedSessions)
            {
                return sessionId;
            }

            ICacheReadOnlySession readOnlySession = (await cache.CreateReadOnlySessionAsync()).Success();

            var localStrongFingerprints = await Collect(cache.LocalCache.EnumerateSessionStrongFingerprints(sessionId).Success().OutOfOrderTasks());
            var remoteStrongFingerprints = await Collect(cache.RemoteCache.EnumerateSessionStrongFingerprints(sessionId).Success().OutOfOrderTasks());

            var local = GatherRecords(readOnlySession, localStrongFingerprints);
            var remote = GatherRecords(readOnlySession, remoteStrongFingerprints);

            XAssert.AreEqual(local.Count, remote.Count, "The session record counts should match between local and remote!");

            foreach (var item in local)
            {
                XAssert.IsTrue(remote.Contains(item), "Remote session does not contain a record that local has!");
            }

            // Now, also make sure that the aggregator produces a consistent view
            var aggregatorStrongFingerprints = await Collect(cache.EnumerateSessionStrongFingerprints(sessionId).Success().OutOfOrderTasks());

            var aggregator = GatherRecords(readOnlySession, aggregatorStrongFingerprints);

            XAssert.AreEqual(aggregator.Count, remote.Count, "The session record counts should match between aggregator and remote!");

            foreach (var item in aggregator)
            {
                XAssert.IsTrue(remote.Contains(item), "Remote session does not contain a record that the aggregator has!");
            }

            return sessionId;
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
        public virtual async Task SessionRecordTransferTest(BackingStoreTestClass localCacheTestClass, BackingStoreTestClass remoteCacheTestClass)
        {
            string testCacheId = "TestSessionRecordTrasferTest";
            ICache testCache = await NewCacheAsync(testCacheId, localCacheTestClass, remoteCacheTestClass);

            VerticalCacheAggregator vertCache = testCache as VerticalCacheAggregator;
            XAssert.IsNotNull(vertCache);

            string sessionId = "Session1";
            ICacheSession session = (await testCache.CreateSessionAsync(sessionId)).Success();

            FullCacheRecord[] records = new FullCacheRecord[4];

            for (int pip = 0; pip < records.Length; pip++)
            {
                records[pip] = await FakeBuild.DoPipAsync(session, "Pip" + pip);

                // Since this is the first build, make sure it had no cache hits
                XAssert.AreEqual(FakeBuild.NewRecordCacheId, records[pip].CacheId);
            }

            XAssert.AreEqual(sessionId, await ValidateSession(vertCache, session));

            // This validates that all of the content we expect is in the
            // cache and is correct.
            await FakeBuild.CheckContentsAsync(testCache, records);

            sessionId = "Session2";
            session = (await testCache.CreateSessionAsync(sessionId)).Success();

            for (int pip = 0; pip < records.Length; pip++)
            {
                FullCacheRecord record = await FakeBuild.DoPipAsync(session, "Pip" + pip);

                // Since this is the second build, make sure it has cache hits
                XAssert.AreNotEqual(FakeBuild.NewRecordCacheId, record);

                // And make sure they are equal to the original
                XAssert.AreEqual(records[pip], record, "The cache hit should have matched!");
            }

            XAssert.AreEqual(sessionId, await ValidateSession(vertCache, session));

            XAssert.IsTrue((await testCache.ShutdownAsync()).Succeeded);
        }

        public virtual async Task EnsureSentinelReturnedDuringEnumeration(BackingStoreTestClass localCacheTestClass, BackingStoreTestClass remoteCacheTestClass)
        {
            string testCacheId = "EnsureSentinelReturnedDuringEnumeration";
            ICache testCache = await NewCacheAsync(testCacheId, localCacheTestClass, remoteCacheTestClass);
            VerticalCacheAggregator vertCache = testCache as VerticalCacheAggregator;

            ICacheSession session = (await testCache.CreateSessionAsync()).Success();

            FullCacheRecord pipRecord = await FakeBuild.DoPipAsync(session, "fakePip");

            await session.CloseAsync().SuccessAsync();

            session = (await testCache.CreateSessionAsync()).Success();

            // So this pip should exist twice in the cache, once for the local cache, and once for the remote cache.
            int enumeration = 0;
            int sentinelCount = 0;
            string[] caches = testCache.CacheId.Split('_');
            HashSet<string> seenCaches = new HashSet<string>();

            foreach (var sfpTask in session.EnumerateStrongFingerprints(pipRecord.StrongFingerprint.WeakFingerprint))
            {
                StrongFingerprint sfp = (await sfpTask).Success();

                if (!(sfp is StrongFingerprintSentinel))
                {
                    XAssert.AreEqual(caches[enumeration - sentinelCount], sfp.CacheId, "Source of strong fingerprint was not as expected.");
                    XAssert.IsTrue(seenCaches.Add(sfp.CacheId), "Cache {0} was already seen for this weak fingerprint", sfp.CacheId);
                }
                else
                {
                    XAssert.IsTrue(enumeration % 2 != 0 || enumeration == 1, "Sentinel should only be seen after an even number of records, it was seen after {0}", enumeration);
                    sentinelCount++;
                }

                enumeration++;
            }

            XAssert.AreEqual(caches.Length, enumeration - sentinelCount, "Number of weak fingerprints seen not as expected");

            await session.CloseAsync().SuccessAsync();

            Dictionary<string, double> statsToCheck = new Dictionary<string, double>();
            statsToCheck.Add("EnumerateStrongFingerprints_Count", 1);
            statsToCheck.Add("EnumerateStrongFingerprints_Local_Sum", 1);
            statsToCheck.Add("EnumerateStrongFingerprints_Sentinel", 1);
            statsToCheck.Add("EnumerateStrongFingerprints_Remote_Sum", 1 + ((caches.Length - 2) * 2));

            ValidateStatistics(session, statsToCheck);
        }

        public virtual async Task AggreatorReturnsRemoteCacheGuid(BackingStoreTestClass localCacheTestClass, BackingStoreTestClass remoteCacheTestClass)
        {
            string testCacheId = "AggreatorReturnsRemoteCacheGuid";

            ICache testCache = await NewCacheAsync(testCacheId, localCacheTestClass, remoteCacheTestClass);
            VerticalCacheAggregator vertCache = testCache as VerticalCacheAggregator;

            XAssert.AreEqual(vertCache.CacheGuid, testCache.CacheGuid, "VerticalAggreator should return the Guid of the remote cache as its Guid");
            XAssert.AreNotEqual(vertCache.LocalCache.CacheGuid, vertCache.RemoteCache.CacheGuid, "Each cache should have a seperate Guid");
        }

        // Remote is readonly, local is writeable.
        public virtual async Task ReadOnlyRemoteIsNotUpdated(BackingStoreTestClass localCacheTestClass, BackingStoreTestClass remoteCacheTestClass)
        {
            string testCaseId = "ReadOnlyRemoteIsNotUpdated";

            ICache testCache = await NewCacheAsync(testCaseId, localCacheTestClass, remoteCacheTestClass, remoteReadOnly: true);
            ICache remoteCache = GetMostRemoteCache(testCache);

            VerticalCacheAggregator vertCache = testCache as VerticalCacheAggregator;
            XAssert.IsNotNull(vertCache);

            ICacheSession session = (await testCache.CreateSessionAsync()).Success();

            FullCacheRecord cacheRecord = await FakeBuild.DoPipAsync(session, "TestPip");

            Dictionary<string, double> statsToCheck = new Dictionary<string, double>();
            statsToCheck.Add("AddOrGet_FingerprintsAddedLocalOnly_Count", 1);

            await session.CloseAsync().SuccessAsync();
            ValidateStatistics(session, statsToCheck);

            await ValidateItemsInCacheAsync(
                vertCache.LocalCache,
                                cacheRecord.StrongFingerprint.WeakFingerprint,
                                new List<CasHash>(cacheRecord.CasEntries),
                                CacheDeterminism.None,
                                cacheRecord.StrongFingerprint.CasElement,
                                vertCache.LocalCache.CacheId,
                                1);

            var remoteSession = await remoteCache.CreateReadOnlySessionAsync().SuccessAsync();

            int fingerprintsReturned = 0;

            foreach (var fingerprint in remoteSession.EnumerateStrongFingerprints(cacheRecord.StrongFingerprint.WeakFingerprint))
            {
                if (!(await fingerprint.SuccessAsync() is StrongFingerprintSentinel))
                {
                    fingerprintsReturned++;
                }
            }

            XAssert.AreEqual(0, fingerprintsReturned, "No fingerprints should have been found in the remote cache.");
            AssertSuccess(await testCache.ShutdownAsync());
        }

        public virtual async Task ReadOnlyRemoteIsNotUpdatedForLocalHit(BackingStoreTestClass localCacheTestClass, BackingStoreTestClass remoteCacheTestClass)
        {
            string testCaseId = "ReadOnlyRemoteIsNotUpdated";

            ICache testCache = await NewCacheAsync(testCaseId, localCacheTestClass, remoteCacheTestClass, remoteReadOnly: true);
            VerticalCacheAggregator vertCache = testCache as VerticalCacheAggregator;
            XAssert.IsNotNull(vertCache);

            ICacheSession session = (await testCache.CreateSessionAsync()).Success();
            ICacheSession localSession = await vertCache.LocalCache.CreateSessionAsync().SuccessAsync();

            FullCacheRecord cacheRecord = await FakeBuild.DoPipAsync(localSession, "TestPip");

            Dictionary<string, double> statsToCheck = new Dictionary<string, double>();
            statsToCheck.Add("GetCacheEntry_HitLocal_Count", 1);

            await ValidateItemsInCacheAsync(
                vertCache,
                                            cacheRecord.StrongFingerprint.WeakFingerprint,
                                            new List<CasHash>(cacheRecord.CasEntries),
                                            CacheDeterminism.None,
                                            cacheRecord.StrongFingerprint.CasElement,
                                            vertCache.LocalCache.CacheId,
                                            1,
                                            statsToCheck);

            var remoteSession = await vertCache.RemoteCache.CreateReadOnlySessionAsync().SuccessAsync();

            int fingerprintsReturned = 0;

            foreach (var fingerprint in remoteSession.EnumerateStrongFingerprints(cacheRecord.StrongFingerprint.WeakFingerprint))
            {
                if (!(await fingerprint.SuccessAsync() is StrongFingerprintSentinel))
                {
                    fingerprintsReturned++;
                }
            }

            XAssert.AreEqual(0, fingerprintsReturned, "No fingerprints should have been found in the remote cache.");
            AssertSuccess(await testCache.ShutdownAsync());
        }

        public virtual async Task CacheMiss(BackingStoreTestClass localCacheTestClass, BackingStoreTestClass remoteCacheTestClass, bool remoteReadOnly)
        {
            string testCaseId = "CacheMiss";

            ICache testCache = await NewCacheAsync(testCaseId, localCacheTestClass, remoteCacheTestClass, remoteReadOnly: remoteReadOnly);
            VerticalCacheAggregator vertCache = testCache as VerticalCacheAggregator;
            XAssert.IsNotNull(vertCache);

            ICacheSession session = (await testCache.CreateSessionAsync()).Success();

            FakeBuild fb = new FakeBuild("test", FakeBuild.DefaultFakeBuildSize);

            var weakFingerprint = new WeakFingerprintHash(new CasHash(fb.OutputHashes[0]).ToFingerprint());
            StrongFingerprint fakeFingerprint = new StrongFingerprint(weakFingerprint, new CasHash(fb.OutputHashes[0]), fb.OutputHashes[0], "fake");

            var response = await session.GetCacheEntryAsync(fakeFingerprint);
            XAssert.IsFalse(response.Succeeded);
            XAssert.AreEqual(typeof(NoMatchingFingerprintFailure), response.Failure.GetType(), response.Failure.Describe());

            await session.CloseAsync().SuccessAsync();

            Dictionary<string, double> statsToCheck = new Dictionary<string, double>();
            statsToCheck.Add("GetCacheEntry_Miss_Count", 1);

            ValidateStatistics(session, statsToCheck);
        }

        // Writing to protect against regression for bug fix.
        // Not making very generic as we can delete when SinglePhaseDeterministic goes away. Hopefully soon.
        public virtual async Task ReadOnlyRemoteSinglePhaseRemoteAdd(BackingStoreTestClass localCacheTestClass, BackingStoreTestClass remoteCacheTestClass)
        {
            string testCacheId = "singlePhaseRemote";

            ICache testCache = await NewCacheAsync(testCacheId, localCacheTestClass, remoteCacheTestClass, true);
            ICache remoteCache = GetMostRemoteCache(testCache);

            VerticalCacheAggregator vertCache = testCache as VerticalCacheAggregator;
            XAssert.IsNotNull(vertCache);

            ICacheSession aggregatorSession = (await testCache.CreateSessionAsync()).Success();
            ICacheSession remoteSession = (await remoteCache.CreateSessionAsync()).Success();

            const string PipName = "TestPip";

            // Nothing should be returned for this as the add should have worked.
            FullCacheRecord cacheRecord = await FakeBuild.DoNonDeterministicPipAsync(remoteSession, PipName, determinism: CacheDeterminism.SinglePhaseNonDeterministic);

            FakeBuild fb = new FakeBuild(PipName, FakeBuild.DefaultFakeBuildSize);

            // A cache miss - add the content to the cache and then
            // add the build.
            CasHash inputList = await aggregatorSession.AddToCasAsync(await remoteSession.GetStreamAsync(cacheRecord.StrongFingerprint.CasElement).SuccessAsync()).SuccessAsync();
            CasHash[] items = new CasHash[fb.Outputs.Length];
            for (int i = 0; i < items.Length; i++)
            {
                items[i] = await aggregatorSession.AddToCasAsync(fb.Outputs[i]).SuccessAsync();
            }

            CasEntries entries = new CasEntries(items, CacheDeterminism.SinglePhaseNonDeterministic);

            FullCacheRecordWithDeterminism aggregatorRecord = await aggregatorSession.AddOrGetAsync(cacheRecord.StrongFingerprint.WeakFingerprint, inputList, cacheRecord.StrongFingerprint.HashElement, entries).SuccessAsync();
            XAssert.AreEqual(null, aggregatorRecord.Record);

            // Now query each cache, and verify only the remote content is in each.
            foreach (var currentCache in new Tuple<ICache, CacheDeterminism, string, int>[]
            {
                new Tuple<ICache, CacheDeterminism, string, int>(vertCache, CacheDeterminism.SinglePhaseNonDeterministic, vertCache.LocalCache.CacheId, 2), // This one stays 2, the read only remote causes no query to go up stack for the single phase.
                new Tuple<ICache, CacheDeterminism, string, int>(vertCache.LocalCache, CacheDeterminism.SinglePhaseNonDeterministic, vertCache.LocalCache.CacheId, 1)
            })
            {
                await ValidateItemsInCacheAsync(
                    currentCache.Item1,
                                                cacheRecord.StrongFingerprint.WeakFingerprint,
                                                new List<CasHash>(entries),
                                                currentCache.Item2,
                                                cacheRecord.StrongFingerprint.CasElement,
                                                currentCache.Item3,
                                                currentCache.Item4);
            }
        }

        public virtual async Task SinglePhaseDeterminismStaysSinglePhase(BackingStoreTestClass localCacheTestClass, BackingStoreTestClass remoteCacheTestClass)
        {
            string testCacheId = "SinglePhaseDeterminismStaysSinglePhase";

            ICache testCache = await NewCacheAsync(testCacheId, localCacheTestClass, remoteCacheTestClass, true);
            ICache remoteCache = GetMostRemoteCache(testCache);

            VerticalCacheAggregator vertCache = testCache as VerticalCacheAggregator;
            XAssert.IsNotNull(vertCache);

            ICacheSession aggregatorSession = (await testCache.CreateSessionAsync()).Success();
            ICacheSession remoteSession = (await remoteCache.CreateSessionAsync()).Success();

            const string PipName = "TestPip";

            // Nothing should be returned for this as the add should have worked.
            FullCacheRecord cacheRecord = await FakeBuild.DoPipAsync(remoteSession, PipName, determinism: CacheDeterminism.SinglePhaseNonDeterministic);

            // Now query each cache, and verify only the remote content is in each.
            foreach (var currentCache in new Tuple<ICache, CacheDeterminism, string, int>[]
            {
                new Tuple<ICache, CacheDeterminism, string, int>(vertCache, CacheDeterminism.SinglePhaseNonDeterministic, vertCache.RemoteCache.CacheId, 1),
                new Tuple<ICache, CacheDeterminism, string, int>(vertCache.LocalCache, CacheDeterminism.SinglePhaseNonDeterministic, vertCache.LocalCache.CacheId, 1)
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
        }

        public virtual async Task RecordsAreIncooperated(BackingStoreTestClass localCacheTestClass, BackingStoreTestClass remoteCacheTestClass)
        {
            string testCacheId = "RecordsAreIncooperated";

            ICache testCache = await NewCacheAsync(testCacheId, localCacheTestClass, remoteCacheTestClass, true);
            ICache remoteCache = GetMostRemoteCache(testCache);

            VerticalCacheAggregator vertCache = testCache as VerticalCacheAggregator;
            XAssert.IsNotNull(vertCache);

            ICacheSession aggregatorSession = (await testCache.CreateSessionAsync()).Success();
            ICacheSession remoteSession = (await remoteCache.CreateSessionAsync()).Success();

            const string PipName = "TestPip";

            // Nothing should be returned for this as the add should have worked.
            FullCacheRecord cacheRecord = await FakeBuild.DoPipAsync(remoteSession, PipName, determinism: CacheDeterminism.SinglePhaseNonDeterministic);

            // Now query each cache, and verify only the remote content is in each.
            foreach (var currentCache in new Tuple<ICache, CacheDeterminism, string, int>[]
            {
                new Tuple<ICache, CacheDeterminism, string, int>(vertCache, CacheDeterminism.SinglePhaseNonDeterministic, vertCache.RemoteCache.CacheId, 1),
                new Tuple<ICache, CacheDeterminism, string, int>(vertCache.LocalCache, CacheDeterminism.SinglePhaseNonDeterministic, vertCache.LocalCache.CacheId, 1)
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
        }

        /// <summary>
        /// Adding a new fingerprint when the remote has a more deterministic output replaces the local value w/ the
        /// remote one.
        /// </summary>
        public virtual async Task AddingFpReplacedWithExistingRORemote(
            BackingStoreTestClass localTestClass,
                                                                     BackingStoreTestClass remoteTestClass,
                                                                     CacheDeterminism initialDeterminismLocal,
                                                                     CacheDeterminism initialDeterminsimRemote,
                                                                     CacheDeterminism finalDeterminismLocal,
                                                                     CacheDeterminism finalDeterminismRemote)
        {
            if (CacheStoreCannotBeRemote && remoteTestClass == BackingStoreTestClass.Self)
            {
                return;
            }

            string testCacheId = "TestCacheAddingFpReplacedWithExistingRemote";
            ICache testCache = await NewCacheAsync(testCacheId, localTestClass, remoteTestClass, true);
            ICache remoteCache = GetMostRemoteCache(testCache);

            VerticalCacheAggregator vertCache = testCache as VerticalCacheAggregator;
            XAssert.IsNotNull(vertCache);

            ICacheSession aggregatorSession = (await testCache.CreateSessionAsync()).Success();
            ICacheSession localSession = (await vertCache.LocalCache.CreateSessionAsync()).Success();
            ICacheSession remoteSession = (await remoteCache.CreateSessionAsync()).Success();

            if (finalDeterminismLocal.Guid == RemoteReferenceGuild)
            {
                finalDeterminismLocal = CacheDeterminism.ViaCache(vertCache.CacheGuid, CacheDeterminism.NeverExpires);
            }

            const string PipName = "TestPip";

            // Nothing should be returned for this as the add should have worked.
            FullCacheRecord cacheRecord = await FakeBuild.DoPipAsync(remoteSession, PipName, determinism: initialDeterminsimRemote);

            // Generate the local file streams.
            FakeBuild fb = new FakeBuild(PipName, cacheRecord.CasEntries.Count, forceUniqueOutputs: true);
            XAssert.AreEqual(cacheRecord.CasEntries.Count, fb.Outputs.Length, "Both the builds must have the same number of output files.");

            // Add the files
            CasHash casElementHash = await aggregatorSession.AddToCasAsync(fb.OutputList).SuccessAsync();
            XAssert.AreEqual(cacheRecord.StrongFingerprint.CasElement, casElementHash, "Remote build and new build's CasElement must have the same hash");

            List<CasHash> aggregatorCasHashes = new List<CasHash>(fb.Outputs.Length);

            foreach (Stream s in fb.Outputs)
            {
                aggregatorCasHashes.Add(await aggregatorSession.AddToCasAsync(s).SuccessAsync());
            }

            // Place the contents in the aggregator. Should return the existing data from the remote, and place it in
            // the local cache.
            FullCacheRecordWithDeterminism aggregatorCacheRecord = await aggregatorSession.AddOrGetAsync(
                cacheRecord.StrongFingerprint.WeakFingerprint,
                                                                                          cacheRecord.StrongFingerprint.CasElement,
                                                                                          cacheRecord.StrongFingerprint.HashElement,
                                                                                          aggregatorCasHashes.ToArray()).SuccessAsync();

            XAssert.AreNotEqual(null, cacheRecord);
            XAssert.AreEqual(aggregatorCacheRecord.Record.CacheId, vertCache.RemoteCache.CacheId, "Cache record returned was not from remote cache ({0}) but was from ({1})", vertCache.RemoteCache.CacheId, cacheRecord.CacheId);
            XAssert.AreEqual(aggregatorCacheRecord.Record.CasEntries.Count, fb.Outputs.Length, "Count of files returned was not correct");

            var remoteDeterminism = finalDeterminismLocal;

            Dictionary<string, double> statsToCheck = new Dictionary<string, double>();
            statsToCheck.Add("AddOrGet_DeterminismRecovered_Count", 1);

            await aggregatorSession.CloseAsync().SuccessAsync();
            ValidateStatistics(aggregatorSession, statsToCheck);

            statsToCheck.Clear();
            statsToCheck.Add("FilesTransitedToLocal_Filecount_Sum", 4);

            // Now query each cache, and verify only the remote content is in each.
            // Make sure the content is in each cache. (Placing the aggregator cache first will cause backfill of the local cache)
            foreach (var currentCache in new Tuple<ICache, CacheDeterminism, string, int, Dictionary<string, double>>[]
            {
                new Tuple<ICache, CacheDeterminism, string, int, Dictionary<string, double>>(vertCache, finalDeterminismLocal, vertCache.LocalCache.CacheId, vertCache.CacheId.Split('_').Length, statsToCheck),
                new Tuple<ICache, CacheDeterminism, string, int, Dictionary<string, double>>(vertCache.LocalCache, finalDeterminismLocal, vertCache.LocalCache.CacheId, 1, null),
                new Tuple<ICache, CacheDeterminism, string, int, Dictionary<string, double>>(remoteCache, remoteDeterminism, remoteCache.CacheId, 1, null)
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

            XAssert.IsTrue((await testCache.ShutdownAsync()).Succeeded);
        }

        #region IDisposable Support

        protected override void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    if (m_testCache != null && !m_testCache.IsShutdown)
                    {
                        var result = m_testCache.ShutdownAsync().Result;
                        XAssert.IsTrue(result.Succeeded, "Failed to shutdown cache");
                        m_testCache = null;
                    }
                }
            }

            base.Dispose(disposing);
        }
        #endregion

        private void AssertSuccess<T>(Possible<T> possible)
        {
            Assert.True(possible.Succeeded);
        }

        private void AssertSuccess<T>(Possible<T, Failure> possible)
        {
            Assert.True(possible.Succeeded);
        }

        /// <summary>
        /// Represents which backing store class should be used to test with the aggerator.
        /// </summary>
        public enum BackingStoreTestClass
        {
            Self,
            Memory
        }
    }
}
