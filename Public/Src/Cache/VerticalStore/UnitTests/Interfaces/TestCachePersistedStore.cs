// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.Interfaces;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace BuildXL.Cache.Tests
{
    /// <summary>
    /// Represents test cases for backing stores that persist data.
    /// </summary>
    /// <remarks>
    /// This is basically every store except for the InMemoryTest one
    /// </remarks>
    public abstract class TestCachePersistedStore : TestCacheBackingstore
    {
        /// <summary>
        /// Subclasses must provide a way to re-open a cache with
        /// the matching ID from a prior open.
        /// </summary>
        /// <param name="cacheId">The prior cache ID within this same test session</param>
        /// <returns>The ICache instance for use against the existing cache</returns>
        /// <param name="strictMetadataCasCoupling">If possible, use this strictness setting</param>
        /// <remarks>
        /// The mechanism by which a cache may re-open a prior cache is up to the
        /// implementation of the test harness.
        /// </remarks>
        protected abstract Task<ICache> GetExistingCacheAsync(string cacheId, bool strictMetadataCasCoupling);

        /// <summary>
        /// Verifies that data is actually persisted between cache invocations.
        /// </summary>
        [Fact]
        public async Task CacheDataPersisted()
        {
            string testName = "CacheDataPersisted";

            ICache firstInvocation = await CreateCacheAsync(testName, true);
            Guid originalGuid = firstInvocation.CacheGuid;

            ICacheSession session = (await firstInvocation.CreateSessionAsync("sessionName")).Success();

            FullCacheRecord cacheRecord = await FakeBuild.DoPipAsync(session, "PipA");

            (await session.CloseAsync()).Success();
            await ShutdownCacheAsync(firstInvocation, testName);

            ICache secondInvocation = await GetExistingCacheAsync(testName, true);

            XAssert.AreEqual(originalGuid, secondInvocation.CacheGuid, "Persistent caches: GUID should not change");

            ICacheSession newSession = (await secondInvocation.CreateSessionAsync()).Success();

            int fingerprintsFound = 0;
            foreach (var singleFingerprint in newSession.EnumerateStrongFingerprints(cacheRecord.StrongFingerprint.WeakFingerprint))
            {
                var sfp = (await singleFingerprint).Success();

                XAssert.AreEqual(cacheRecord.StrongFingerprint, sfp, "Fingerprints must match");

                fingerprintsFound++;
            }

            XAssert.AreEqual(1, fingerprintsFound, "A single instance of the fingerprint should have been found after restarting the cache.");

            (await newSession.CloseAsync()).Success();

            await ShutdownCacheAsync(secondInvocation, testName);
        }
    }
}
