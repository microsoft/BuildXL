// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Diagnostics.Tracing;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.Interfaces.Test;
using BuildXL.Cache.Interfaces;
using BuildXL.Cache.VerticalAggregator;
using BuildXL.Storage;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace BuildXL.Cache.Tests
{
    /// <summary>
    /// Tests to ensure that the VerticalCacheAggregator respects the IsDisconnected bit on its remote cache
    /// </summary>
    /// <remarks>
    /// The goal of the tests in this class are to ensure that the IsDisconencted bit is honored and when set no calls are made to
    /// the remote cache. Functionality like testing how the aggregator handles disconnect transitions and re-connects wrt determinism
    /// will be done elsewhere.
    /// </remarks>
    public class VerticalAggregatorDisconnectTests : TestCacheCore
    {
        protected override IEnumerable<EventSource> EventSources => new[] { VerticalCacheAggregator.EventSource };

        public override string NewCache(string cacheId, bool strictMetadataCasCoupling, bool authoritative = false)
        {
            return NewWrappedRemoteCache(cacheId, strictMetadataCasCoupling, false);
        }

        /// <summary>
        /// Returns the config string for a VerticalCacheAggregator that has a remote cache wrapped by the CallbackCacheWrapper using the InMemoryCache as the local and remote
        /// backing stores
        /// </summary>
        /// <param name="cacheId">Id of the cache.</param>
        /// <param name="strictMetadataCasCoupling">If the cache should require a strick metadata CAS coupling.</param>
        /// <param name="writeThroughCasData">If the VerticalAggregator should force write through of CAS data.</param>
        /// <returns>A VerticalCacheAggregator </returns>
        internal static string NewWrappedRemoteCache(string cacheId, bool strictMetadataCasCoupling, bool writeThroughCasData)
        {
            TestInMemory memTests = new TestInMemory();
            string localCacheString = memTests.NewCache(cacheId + VerticalAggregatorBaseTests.LocalMarker, strictMetadataCasCoupling);
            string remoteCacheString = memTests.NewCache(cacheId + VerticalAggregatorBaseTests.RemoteMarker, strictMetadataCasCoupling, authoritative: true);

            remoteCacheString = TestCallbackCache.FormatNewCacheConfig(remoteCacheString);

            string vertCacheConfig = VerticalAggregatorBaseTests.NewCacheString(cacheId, localCacheString, remoteCacheString, false, false, writeThroughCasData);
            return vertCacheConfig;
        }

        /// <summary>
        /// Returns the config string for a VerticalCacheAggregator that has a local cache wrapped by the CallbackCacheWrapper using the InMemoryCache as the local and remote
        /// backing stores
        /// </summary>
        /// <param name="cacheId">Id of the cache.</param>
        /// <param name="strictMetadataCasCoupling">If the cache should require a strick metadata CAS coupling.</param>
        /// <param name="writeThroughCasData">If the VerticalAggregator should force write through of CAS data.</param>
        /// <returns>A VerticalCacheAggregator </returns>
        internal static string NewWrappedLocalCache(string cacheId, bool strictMetadataCasCoupling, bool writeThroughCasData)
        {
            TestInMemory memTests = new TestInMemory();
            string localCacheString = memTests.NewCache(cacheId + VerticalAggregatorBaseTests.LocalMarker, strictMetadataCasCoupling);
            string remoteCacheString = memTests.NewCache(cacheId + VerticalAggregatorBaseTests.RemoteMarker, strictMetadataCasCoupling, authoritative: true);

            localCacheString = TestCallbackCache.FormatNewCacheConfig(localCacheString);

            string vertCacheConfig = VerticalAggregatorBaseTests.NewCacheString(cacheId, localCacheString, remoteCacheString, false, false, writeThroughCasData);
            return vertCacheConfig;
        }

        private static void PoisonROSession(CallbackCacheReadOnlySessionWrapper cacheSession)
        {
            cacheSession.EnumerateStrongFingerprintsCallback = (WeakFingerprintHash weak, UrgencyHint hint, Guid activityId, ICacheReadOnlySession wrappedSession) =>
            {
                XAssert.Fail("Remote Cache was called when disconnected (EnumerateStrongFingerprints)");
                return null;
            };

            cacheSession.GetCacheEntryAsyncCallback = (StrongFingerprint strong, UrgencyHint urgencyHint, Guid activityId, ICacheReadOnlySession wrappedSession) =>
            {
                XAssert.Fail("Remote Cache was called when disconnected (GetCacheEntryAsync)");
                return null;
            };

            cacheSession.GetStreamAsyncCallback = (CasHash hash, UrgencyHint urgencyHint, Guid activityId, ICacheReadOnlySession wrappedSession) =>
            {
                XAssert.Fail("Remote Cache was called when disconnected (GetStreamAsync)");
                return null;
            };

            cacheSession.PinToCasAsyncCallback = (CasHash hash, UrgencyHint urgencyHint, Guid activityId, ICacheReadOnlySession wrappedSession) =>
            {
                XAssert.Fail("Remote Cache was called when disconnected (PinToCasAsync)");
                return null;
            };

            cacheSession.PinToCasMultipleAsyncCallback = (CasEntries hashes, UrgencyHint urgencyHint, Guid activityId, ICacheReadOnlySession wrappedSession) =>
            {
                XAssert.Fail("Remote Cache was called when disconnected (PinToCasMultiple)");
                return null;
            };

            cacheSession.ProduceFileAsyncCallback = (CasHash hash, string filename, FileState fileState, UrgencyHint urgencyHint, Guid activityId, ICacheReadOnlySession wrappedSession) =>
            {
                XAssert.Fail("Remote Cache was called when disconnected (ProduceFileAsync)");
                return null;
            };
        }

        private static void PoisonSession(CallbackCacheSessionWrapper cacheSession)
        {
            PoisonROSession(cacheSession);

            cacheSession.AddOrGetAsyncCallback = (WeakFingerprintHash weak, CasHash casElement, Hash hashElement, CasEntries hashes, UrgencyHint urgencyHint, Guid activityId, ICacheSession wrappedSession) =>
            {
                XAssert.Fail("Remote Cache was called when disconnected (AddOrGetAsync)");
                return null;
            };

            cacheSession.AddToCasAsyncCallback = (Stream filestream, CasHash? casHash, UrgencyHint urgencyHint, Guid activityId, ICacheSession wrappedSession) =>
            {
                XAssert.Fail("Remote Cache was called when disconnected (AddToCasAsync)");
                return null;
            };

            cacheSession.AddToCasFilenameAsyncCallback = (string filename, FileState fileState, CasHash? hash, UrgencyHint urgencyHint, Guid activityId, ICacheSession wrappedSession) =>
            {
                XAssert.Fail("Remote Cache was called when disconnected (AddToCasFilenameAsync)");
                return null;
            };

            cacheSession.IncorporateRecordsAsyncCallback = (IEnumerable<Task<StrongFingerprint>> strongFingerprints, Guid activityId, ICacheSession wrappedSession) =>
            {
                XAssert.Fail("Remote Cache was called when disconnected (IncorporateRecordsAsync)");
                return null;
            };
        }

        internal static void PoisonAllRemoteSessions(ICache cache)
        {
            Contract.Requires(cache is VerticalCacheAggregator);

            VerticalCacheAggregator vertCache = cache as VerticalCacheAggregator;
            CallbackCacheWrapper remoteCache = vertCache.RemoteCache as CallbackCacheWrapper;
            XAssert.IsNotNull(remoteCache);

            remoteCache.CreateNamedSessionAsyncCallback = async (string sessionId, ICache cacheInstance) =>
            {
                ICacheSession session = await cacheInstance.CreateSessionAsync(sessionId).SuccessAsync();

                CallbackCacheSessionWrapper wrappedSession = new CallbackCacheSessionWrapper(session);

                PoisonSession(wrappedSession);

                return new BuildXL.Utilities.Possible<ICacheSession, BuildXL.Utilities.Failure>(session);
            };

            remoteCache.CreateSessionAsyncCallback = async (ICache cacheInstance) =>
            {
                ICacheSession session = await cacheInstance.CreateSessionAsync().SuccessAsync();

                CallbackCacheSessionWrapper wrappedSession = new CallbackCacheSessionWrapper(session);

                PoisonSession(wrappedSession);

                return new BuildXL.Utilities.Possible<ICacheSession, BuildXL.Utilities.Failure>(session);
            };

            remoteCache.CreateReadOnlySessionAsyncCallback = async (ICache cacheInstance) =>
            {
                ICacheReadOnlySession session = await cacheInstance.CreateReadOnlySessionAsync().SuccessAsync();

                CallbackCacheReadOnlySessionWrapper wrappedSession = new CallbackCacheReadOnlySessionWrapper(session);

                PoisonROSession(wrappedSession);

                return new BuildXL.Utilities.Possible<ICacheReadOnlySession, BuildXL.Utilities.Failure>(session);
            };

            remoteCache.CacheGuidGetCallback = (ICache wrappedcache) =>
            {
                XAssert.Fail("Remote Cache was called when disconnected (CacheGuid)");
                return Guid.Empty;
            };
        }

        internal static void ConnectCache(ICache cache)
        {
            Contract.Requires(cache is CallbackCacheWrapper);

            ((CallbackCacheWrapper)cache).IsDisconnectedCallback = null;
        }

        internal static void DisconnectCache(ICache cache)
        {
            Contract.Requires(cache is CallbackCacheWrapper);

            ((CallbackCacheWrapper)cache).IsDisconnectedCallback = (ICache wrappedCache) => { return true; };
        }

        private static void DisconnectRemoteCache(ICache cache)
        {
            DisconnectCache(UnwrapVerticalCache(cache).RemoteCache);
        }

        internal static VerticalCacheAggregator UnwrapVerticalCache(ICache cache)
        {
            Contract.Requires(cache is VerticalCacheAggregator);

            return cache as VerticalCacheAggregator;
        }

        internal static CallbackCacheSessionWrapper UnwrapRemoteSession(ICacheSession session)
        {
            Contract.Requires(session is VerticalCacheAggregatorSession);
            Contract.Requires(((VerticalCacheAggregatorSession)session).RemoteRoSession is CallbackCacheSessionWrapper);

            return ((VerticalCacheAggregatorSession)session).RemoteRoSession as CallbackCacheSessionWrapper;
        }

        internal static CallbackCacheSessionWrapper UnwrapLocalSession(ICacheSession session)
        {
            Contract.Requires(session is VerticalCacheAggregatorSession);
            Contract.Requires(((VerticalCacheAggregatorSession)session).LocalSession is CallbackCacheSessionWrapper);

            return ((VerticalCacheAggregatorSession)session).LocalSession as CallbackCacheSessionWrapper;
        }

        [Fact]
        public async Task DisconnectedCacheNotQueriedForStrongFingerprints()
        {
            string testCacheId = "Disconnected";
            ICache testCache = await InitializeCacheAsync(NewCache(testCacheId, false)).SuccessAsync();
            PoisonAllRemoteSessions(testCache);

            ICacheReadOnlySession roSession = await testCache.CreateReadOnlySessionAsync().SuccessAsync();

            DisconnectRemoteCache(testCache);

            FakeBuild fb = new FakeBuild("test", 1);

            foreach (var fingerprint in roSession.EnumerateStrongFingerprints(new WeakFingerprintHash(FingerprintUtilities.Hash("fingerprint").ToByteArray())))
            {
                // Just run the enumerator, should all return.
            }
        }

        /// <summary>
        /// After adding a fingerprint to an empty local, the FP information is available in both caches and is deterministic in the local cache.
        /// </summary>
        [Fact]
        public virtual Task AddToEmptyCacheWithDeterministicRemoteDisconnected()
        {
            return AddToEmptyCacheAsync(false);
        }

        /// <summary>
        /// Adds deterministic to the local cache and verifies it is deterministic in both caches.
        /// </summary>
        [Fact]
        public virtual Task AddDeterministicContentToEmptyCacheDisconnectedRemote()
        {
            return AddToEmptyCacheAsync(true);
        }

        private async Task AddToEmptyCacheAsync(bool contentIsDeterministic)
        {
            string testCacheId = "Disconnected";
            ICache testCache = await InitializeCacheAsync(NewCache(testCacheId, false)).SuccessAsync();

            VerticalCacheAggregator vertCache = testCache as VerticalCacheAggregator;
            XAssert.IsNotNull(vertCache);

            CacheDeterminism localDeterminism = CacheDeterminism.None;

            if (contentIsDeterministic)
            {
                localDeterminism = CacheDeterminism.Tool;
            }

            CacheDeterminism initialDeterminism = contentIsDeterministic ? CacheDeterminism.Tool : CacheDeterminism.None;

            ICacheSession session = (await testCache.CreateSessionAsync()).Success();

            VerticalCacheAggregatorSession vertSession = session as VerticalCacheAggregatorSession;
            XAssert.IsNotNull(vertSession);

            CallbackCacheSessionWrapper wrappedSession = vertSession.RemoteSession as CallbackCacheSessionWrapper;
            XAssert.IsNotNull(wrappedSession);
            PoisonSession(wrappedSession);
            DisconnectRemoteCache(testCache);

            FullCacheRecord cacheRecord = await FakeBuild.DoPipAsync(session, "TestPip", determinism: initialDeterminism);

            await VerticalAggregatorBaseTests.ValidateItemsInCacheAsync(
                vertCache.LocalCache,
                                            cacheRecord.StrongFingerprint.WeakFingerprint,
                                            new List<CasHash>(cacheRecord.CasEntries),
                                            localDeterminism,
                                            cacheRecord.StrongFingerprint.CasElement,
                                            vertCache.LocalCache.CacheId,
                                            1);

            await VerticalAggregatorBaseTests.ValidateItemsInCacheAsync(
                vertCache.RemoteCache,
                                cacheRecord.StrongFingerprint.WeakFingerprint,
                                new List<CasHash>(cacheRecord.CasEntries),
                                localDeterminism,
                                cacheRecord.StrongFingerprint.CasElement,
                                vertCache.RemoteCache.CacheId,
                                0);

            XAssert.IsTrue((await testCache.ShutdownAsync()).Succeeded);
        }

        /// <summary>
        /// A local cache hit that is marked as non-deterministic does not result in a query to a disconnected cache.
        /// </summary>
        [Fact]
        public virtual async Task NonDeterministicContentRespectsDisconnect()
        {
            string testCacheId = "Disconnected";
            ICache testCache = await InitializeCacheAsync(NewCache(testCacheId, false)).SuccessAsync();

            VerticalCacheAggregator vertCache = testCache as VerticalCacheAggregator;
            XAssert.IsNotNull(vertCache);

            ICacheSession aggregatorSession = (await testCache.CreateSessionAsync()).Success();
            ICacheSession localSession = (await vertCache.LocalCache.CreateSessionAsync()).Success();
            ICacheSession remoteSession = (await vertCache.RemoteCache.CreateSessionAsync()).Success();

            VerticalCacheAggregatorSession vertSession = aggregatorSession as VerticalCacheAggregatorSession;
            XAssert.IsNotNull(vertSession);

            CallbackCacheSessionWrapper wrappedSession = vertSession.RemoteSession as CallbackCacheSessionWrapper;
            XAssert.IsNotNull(wrappedSession);
            PoisonSession(wrappedSession);

            CacheDeterminism determinismSource = CacheDeterminism.None;

            const string PipName = "TestPip";

            // Populate the remote cache with one set of outputs.
            FullCacheRecord remoteCacheRecord = await FakeBuild.DoNonDeterministicPipAsync(remoteSession, PipName);

            // And the local cache with a set forced to be unique.
            FullCacheRecord localCacheRecord = await FakeBuild.DoNonDeterministicPipAsync(localSession, PipName, generateVerifiablePip: true);

            PoisonAllRemoteSessions(testCache);
            DisconnectRemoteCache(testCache);

            // Now query each cache, and verify only the remote content is in each.
            // Make sure the content is in each cache. (Placing the aggregator cache first will cause backfill of the local cache)
            foreach (var currentCache in new Tuple<ICache, CacheDeterminism, string, int>[]
            {
                new Tuple<ICache, CacheDeterminism, string, int>(testCache, CacheDeterminism.None, vertCache.LocalCache.CacheId, 1),
                new Tuple<ICache, CacheDeterminism, string, int>(vertCache.LocalCache, CacheDeterminism.None, vertCache.LocalCache.CacheId, 1)
            })
            {
                await VerticalAggregatorBaseTests.ValidateItemsInCacheAsync(
                    currentCache.Item1,
                                                localCacheRecord.StrongFingerprint.WeakFingerprint,
                                                new List<CasHash>(localCacheRecord.CasEntries),
                                                currentCache.Item2,
                                                localCacheRecord.StrongFingerprint.CasElement,
                                                currentCache.Item3,
                                                currentCache.Item4);
            }

            XAssert.IsTrue((await testCache.ShutdownAsync()).Succeeded);
        }

        // Remote is readonly, local is writeable.
        [Fact]
        public virtual async Task ReadOnlyRemoteIsNotUpdatedWhenDisconnected()
        {
            string testCacheId = "Disconnected";
            ICache testCache = await InitializeCacheAsync(NewCache(testCacheId, false)).SuccessAsync();
            VerticalCacheAggregator vertCache = testCache as VerticalCacheAggregator;
            XAssert.IsNotNull(vertCache);

            PoisonAllRemoteSessions(testCache);
            DisconnectRemoteCache(testCache);

            ICacheSession session = (await testCache.CreateSessionAsync()).Success();

            FullCacheRecord cacheRecord = await FakeBuild.DoPipAsync(session, "TestPip");

            await VerticalAggregatorBaseTests.ValidateItemsInCacheAsync(
                vertCache.LocalCache,
                                            cacheRecord.StrongFingerprint.WeakFingerprint,
                                            new List<CasHash>(cacheRecord.CasEntries),
                                            CacheDeterminism.None,
                                            cacheRecord.StrongFingerprint.CasElement,
                                            vertCache.LocalCache.CacheId,
                                            1);

            var remoteSession = await vertCache.RemoteCache.CreateReadOnlySessionAsync().SuccessAsync();

            int fingerprintsReturned = 0;

            foreach (var fingerprint in remoteSession.EnumerateStrongFingerprints(cacheRecord.StrongFingerprint.WeakFingerprint))
            {
                fingerprintsReturned++;
            }

            XAssert.AreEqual(0, fingerprintsReturned, "No fingerprints should have been found in the remote cache.");
            AssertSuccess(await testCache.ShutdownAsync());
        }

        [Fact]
        public virtual async Task WritethroughRemoteIsNotWrittenToWhenDisconnected()
        {
            string testCacheId = "Disconnected";
            ICache testCache = await InitializeCacheAsync(NewWrappedRemoteCache(testCacheId, false, true)).SuccessAsync();

            PoisonAllRemoteSessions(testCache);
            DisconnectRemoteCache(testCache);

            ICacheSession aggSession = await testCache.CreateSessionAsync().SuccessAsync();

            FakeBuild fb = new FakeBuild("test", 1);

            AssertSuccess(await aggSession.AddToCasAsync(fb.Outputs[0]));
        }

        [Fact]
        public virtual async Task WritethroughRemoteIsNotWrittenToWhenDisconnectedFileName()
        {
            string testCacheId = "Disconnected";
            ICache testCache = await InitializeCacheAsync(NewWrappedRemoteCache(testCacheId, false, true)).SuccessAsync();

            PoisonAllRemoteSessions(testCache);
            DisconnectRemoteCache(testCache);

            ICacheSession aggSession = await testCache.CreateSessionAsync().SuccessAsync();

            FakeBuild fb = new FakeBuild("test", 1);

            AssertSuccess(await aggSession.AddToCasAsync(fb.Outputs[0]));
        }

        [Fact]
        public virtual async Task DisconnectedCacheUsesLocalCacheGuid()
        {
            string testCacheId = "Disconnected";
            ICache testCache = await InitializeCacheAsync(NewWrappedRemoteCache(testCacheId, false, true)).SuccessAsync();

            PoisonAllRemoteSessions(testCache);
            DisconnectRemoteCache(testCache);

            var vertCache = UnwrapVerticalCache(testCache);

            XAssert.AreEqual(vertCache.LocalCache.CacheGuid, testCache.CacheGuid);
        }

        private void AssertSuccess<T>(Possible<T, Failure> possible) 
        {
            Assert.True(possible.Succeeded);
        }
    }
}
