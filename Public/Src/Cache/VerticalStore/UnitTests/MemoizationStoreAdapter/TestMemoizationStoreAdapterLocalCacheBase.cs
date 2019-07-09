// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.Interfaces;
using BuildXL.Cache.MemoizationStoreAdapter;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace BuildXL.Cache.Tests
{
    public abstract class TestMemoizationStoreAdapterLocalCacheBase : TestCachePersistedStore
    {
        protected override bool ImplementsTrackedSessions => false;

        protected override string DummySessionName => MemoizationStoreAdapterCache.DummySessionName;

        public override bool ReturnsSentinelWhenEmpty => true;

        protected override bool RequiresPinBeforeGet => false;

        private readonly ConcurrentDictionary<string, string> m_cacheIdToJsonConfigString = new ConcurrentDictionary<string, string>();

        protected override async Task<ICache> GetExistingCacheAsync(string cacheId, bool strictMetadataCasCoupling)
        {
            return (await InitializeCacheAsync(m_cacheIdToJsonConfigString[cacheId])).Success();
        }

        protected abstract string DefaultMemoizationStoreJsonConfigString { get; }

        protected virtual string CreateJsonConfigString(string cacheId)
        {
            var cacheDir = ConvertToJSONCompatibleString(GenerateCacheFolderPath("MemoStore."));
            var cacheLogDir = ConvertToJSONCompatibleString(Path.Combine(cacheDir, "Cache.Log"));
            var jsonConfigString = string.Format(DefaultMemoizationStoreJsonConfigString, cacheId, 5000, 50000, cacheDir, cacheLogDir);

            return jsonConfigString;
        }

        public sealed override string NewCache(string cacheId, bool strictMetadataCasCoupling, bool authoritative = false)
        {
            var jsonConfigString = CreateJsonConfigString(cacheId);

            var addSucceeded = m_cacheIdToJsonConfigString.TryAdd(cacheId, jsonConfigString);
            XAssert.IsTrue(addSucceeded, "Tried to create a new cache with the same cacheId twice.");

            return jsonConfigString;
        }

        protected override IEnumerable<EventSource> EventSources => new EventSource[0];

        [Fact]
        public async Task SimpleDummySession()
        {
            const string TestName = "SimpleSession";
            string testCacheId = MakeCacheId(TestName);
            ICache cache = await CreateCacheAsync(testCacheId);

            // Now for the session (which we base on the cache ID)
            string testSessionId = "Session1-" + testCacheId;

            ICacheSession session = await CreateSessionAsync(cache, testSessionId);

            // Do the default fake build for this test (first time, no cache hit)
            FullCacheRecord built = await FakeBuild.DoPipAsync(session, TestName);
            XAssert.AreEqual(FakeBuild.NewRecordCacheId, built.CacheId, "Should have been a new cache entry!");

            // Now we see if we can get back the items we think we should
            await CloseSessionAsync(session, testSessionId);

            // We need a read only session to get the CasEntries
            ICacheReadOnlySession readOnlySession = (await cache.CreateReadOnlySessionAsync()).Success();

            // Validate that the cache contains a dummy session and it has the one cache record it needs.
            HashSet<FullCacheRecord> found = new HashSet<FullCacheRecord>();
            foreach (var strongFingerprintTask in cache.EnumerateSessionStrongFingerprints(MemoizationStoreAdapterCache.DummySessionName).Success().OutOfOrderTasks())
            {
                StrongFingerprint strongFingerprint = await strongFingerprintTask;
                CasEntries casEntries = (await readOnlySession.GetCacheEntryAsync(strongFingerprint)).Success();
                FullCacheRecord record = new FullCacheRecord(strongFingerprint, casEntries);

                // If it is not the record we already found...
                if (!found.Contains(record))
                {
                    found.Add(record);
                }

                XAssert.AreEqual(1, found.Count, "There should be only 1 unique record in the session");

                XAssert.AreEqual(built.StrongFingerprint.WeakFingerprint, record.StrongFingerprint.WeakFingerprint);
                XAssert.AreEqual(built.StrongFingerprint.CasElement, record.StrongFingerprint.CasElement);
                XAssert.AreEqual(built.StrongFingerprint.HashElement, record.StrongFingerprint.HashElement);
                XAssert.AreEqual(built.CasEntries.Count, record.CasEntries.Count, "Did not return the same number of items");
                XAssert.IsTrue(record.CasEntries.Equals(built.CasEntries), "Items returned are not the same hash and/or order order");

                XAssert.AreEqual(built, record);

                // We can not check record.CasEntries.IsDeterministic
                // as the cache may have determined that they are deterministic
                // via cache determinism recovery.
            }

            XAssert.AreEqual(1, found.Count, "There should be 1 and only 1 record in the session!");

            await readOnlySession.CloseAsync().SuccessAsync();

            // Check that the cache has the items in it
            await FakeBuild.CheckContentsAsync(cache, built);

            // Now redo the "build" with a cache hit
            testSessionId = "Session2-" + testCacheId;
            session = await CreateSessionAsync(cache, testSessionId);

            FullCacheRecord rebuilt = await FakeBuild.DoPipAsync(session, TestName);

            XAssert.AreEqual(built, rebuilt, "Should have been the same build!");

            // We make sure we did get it from a cache rather than a manual rebuild.
            XAssert.AreNotEqual(built.CacheId, rebuilt.CacheId, "Should not be the same cache ID");

            await CloseSessionAsync(session, testSessionId);

            readOnlySession = await cache.CreateReadOnlySessionAsync().SuccessAsync();

            // Now that we have done the second build via a cache hit, it should produce the
            // same cache record as before
            foreach (var strongFingerprintTask in cache.EnumerateSessionStrongFingerprints(MemoizationStoreAdapterCache.DummySessionName).Success().OutOfOrderTasks())
            {
                StrongFingerprint strongFingerprint = await strongFingerprintTask;
                CasEntries casEntries = (await readOnlySession.GetCacheEntryAsync(strongFingerprint)).Success();
                FullCacheRecord record = new FullCacheRecord(strongFingerprint, casEntries);

                XAssert.IsTrue(found.Contains(record), "Second session should produce the same cache record but did not!");
            }

            (await readOnlySession.CloseAsync()).Success();

            await ShutdownCacheAsync(cache, testCacheId);
        }
    }
}
