// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.IO;
using System.Security.AccessControl;
using System.Threading.Tasks;
using BuildXL.Cache.BasicFilesystem;
using BuildXL.Cache.Interfaces;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace BuildXL.Cache.Tests
{
    public class TestBasicFilesystem : TestCachePersistedStore
    {
        // Note that in the test harness we don't expect long contention and,
        // in fact, need to test failure of contended access so we always set
        // the contention backoff to a rather low number of ms as we don't have
        // significant contention in the cache during testing and we need to
        // test a contention failure - warning, setting it too low could end
        // up with a contention failure during tests.
        private static readonly string defaultBasicFileSystemJsonConfigString = @"{{
            ""Assembly"":""BuildXL.Cache.BasicFilesystem"",
            ""Type"": ""BuildXL.Cache.BasicFilesystem.BasicFilesystemCacheFactory"",
            ""CacheId"":""{0}"",
            ""CacheRootPath"":""{1}"",
            ""ReadOnly"":{2},
            ""StrictMetadataCasCoupling"":{3},
            ""IsAuthoritative"":{4},
            ""ContentionBackoffMaxMilliseonds"":32
        }}";

        protected readonly Dictionary<string, string> CacheId2cacheDir = new Dictionary<string, string>();

        protected string JsonConfig(string cacheId, string path, bool readOnly, bool strict, bool authoritative)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                                 defaultBasicFileSystemJsonConfigString,
                                 cacheId,
                                 ConvertToJSONCompatibleString(path),
                                 readOnly.ToString().ToLowerInvariant(),
                                 strict.ToString().ToLowerInvariant(),
                                 authoritative.ToString().ToLower());
        }

        private async Task<ICache> GetExistingCacheAsync(string cacheId, bool strictMetadataCasCoupling, bool readOnly)
        {
            string jsonConfigString = JsonConfig(cacheId, CacheId2cacheDir[cacheId], readOnly, strictMetadataCasCoupling, false);

            Possible<ICache, Failure> basicFilesystemCache = await InitializeCacheAsync(jsonConfigString);
            return basicFilesystemCache.Success();
        }

        protected override Task<ICache> GetExistingCacheAsync(string cacheId, bool strictMetadataCasCoupling)
        {
            return GetExistingCacheAsync(cacheId, strictMetadataCasCoupling, false);
        }

        public override string NewCache(string cacheId, bool strictMetadataCasCoupling, bool authoritative = false)
        {
            string cacheDir = GenerateCacheFolderPath("C");

            CacheId2cacheDir.Remove(cacheId);
            CacheId2cacheDir.Add(cacheId, cacheDir);

            return JsonConfig(cacheId, cacheDir, false, strictMetadataCasCoupling, authoritative);
        }

        protected override IEnumerable<EventSource> EventSources => new[] { BasicFilesystemCache.EventSource };

        protected override bool CanTestCorruption => true;

        protected override Task CorruptCasEntry(ICache cache, CasHash hash)
        {
            // We use this as a Task.Run() just to help prove the test structure
            // Other caches are likely to need async behavior so we needed to support
            // that.  No return result.  This must fail if it can not work.
            return Task.Run(() =>
            {
                BasicFilesystemCache myCache = cache as BasicFilesystemCache;
                XAssert.IsNotNull(myCache, "Invalid cache passed to TestBasicFilesyste CorruptCasEntry test method");

                using (var f = File.AppendText(myCache.ToPath(hash)))
                {
                    f.Write("!Corrupted!");
                }
            });
        }

        // Some tests for read-only cache construction
        // One with asking for read-only and one with getting read-only due to
        // not being able to write to the marker file.
        [Fact]
        public async Task CacheAutoReadOnly()
        {
            string testName = "CacheAutoReadOnly";

            ICache firstInvocation = await CreateCacheAsync(testName, true);
            Guid originalGuid = firstInvocation.CacheGuid;

            await ShutdownCacheAsync(firstInvocation, testName);

            // Now, we need to get the path such that we can read-only the read-write marker
            string path = Path.Combine(CacheId2cacheDir[testName], "ReadWrite-Marker");
            File.SetAttributes(path, FileAttributes.ReadOnly);

            ICache secondInvocation = await GetExistingCacheAsync(testName, true);

            bool failureSeen = false;

            secondInvocation.SuscribeForCacheStateDegredationFailures((failure) =>
            {
                XAssert.IsFalse(failureSeen);
                failureSeen = true;
                XAssert.IsTrue(failure is BuildXL.Cache.BasicFilesystem.CacheFallbackToReadonlyFailure, "Failure was of the wrong type, it was {0}", failure.GetType());
            });

            XAssert.AreEqual(originalGuid, secondInvocation.CacheGuid, "Persistent caches: GUID should not change");

            XAssert.IsTrue(secondInvocation.IsReadOnly, "This should have been a read-only cache");
            XAssert.IsTrue(failureSeen, "The cache did not call the failure callback");

            await ShutdownCacheAsync(secondInvocation, testName);

            // Undo the read-only file
            File.SetAttributes(path, FileAttributes.Normal);
        }

        [Fact]
        public async Task CacheDataPersistedReadOnlyCache()
        {
            string testName = "CacheDataPersistedReadOnly";

            ICache firstInvocation = await CreateCacheAsync(testName, true);
            Guid originalGuid = firstInvocation.CacheGuid;

            ICacheSession session = (await firstInvocation.CreateSessionAsync("sessionName")).Success();

            FullCacheRecord cacheRecord = await FakeBuild.DoPipAsync(session, "PipA");

            (await session.CloseAsync()).Success();
            await ShutdownCacheAsync(firstInvocation, testName);

            ICache secondInvocation = await GetExistingCacheAsync(testName, true, true);

            XAssert.AreEqual(originalGuid, secondInvocation.CacheGuid, "Persistent caches: GUID should not change");

            XAssert.IsTrue(secondInvocation.IsReadOnly, "This should have been a read-only cache");

            ICacheReadOnlySession newSession = (await secondInvocation.CreateReadOnlySessionAsync()).Success();

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
