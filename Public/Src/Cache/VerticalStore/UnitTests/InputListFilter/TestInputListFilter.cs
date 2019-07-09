// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.InputListFilter;
using BuildXL.Cache.Interfaces;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace BuildXL.Cache.Tests
{
    public class TestInputListFilter : TestCacheBackingstore
    {
        private static readonly string inputListFilterCacheConfigJSONData = @"{{
            ""Assembly"":""BuildXL.Cache.InputListFilter"",
            ""Type"":""BuildXL.Cache.InputListFilter.InputListFilterCacheFactory"",
            ""MustInclude"":""{0}"",
            ""MustNotInclude"":""{1}"",
            ""FilteredCache"":{2}
        }}";

        private readonly string file = PathGeneratorUtilities.GetAbsolutePath("X", "DIR1", "FILE.TXT");
        private readonly string libraryDll = PathGeneratorUtilities.GetAbsolutePath("X", "DIR1", "SUBDIR", "LIBRARY.DLL");
        private readonly string libDll = PathGeneratorUtilities.GetAbsolutePath("X", "DIR1", "SUBDIR", "LIB.DLL");
        private readonly string tool = PathGeneratorUtilities.GetAbsolutePath("X", "DIR2", "TOOL.EXE");
        private readonly string dllLib = PathGeneratorUtilities.GetAbsolutePath("X", "DIR1", "SUBDIR", "DLL.LIB");
        private readonly string toolPdb = PathGeneratorUtilities.GetAbsolutePath("X", "DIR2", "TOOL.EXE.PDB");

        protected override IEnumerable<EventSource> EventSources
        {
            get { yield break; }
        }

        private string NewFilterCache(string cacheId, bool strictMetadataCasCoupling, bool authoritative, string mustInclude, string mustNotInclude)
        {
            return string.Format(inputListFilterCacheConfigJSONData, mustInclude, mustNotInclude, new TestInMemory().NewCache(cacheId, strictMetadataCasCoupling, authoritative));
        }

        public override string NewCache(string cacheId, bool strictMetadataCasCoupling, bool authoritative = false)
        {
            return NewFilterCache(cacheId, strictMetadataCasCoupling, authoritative, string.Empty, string.Empty);
        }

        private Task<Possible<ICache, Failure>> CreateFilterCacheAsync(string cacheId, string mustInclude, string mustNotInclude)
        {
            string config = NewFilterCache(cacheId, true, false, mustInclude, mustNotInclude);
            return CacheFactory.InitializeCacheAsync(config, default(Guid));
        }

        [Fact]
        public async Task FailureToConstructDueToBadRegex()
        {
            const string TestName = "Failing";
            string testCacheId = MakeCacheId(TestName);
            var possibleCache = await CreateFilterCacheAsync(testCacheId, "*", string.Empty);
            XAssert.IsFalse(possibleCache.Succeeded, "Should have failed with a bad regex");
            XAssert.IsTrue(possibleCache.Failure is RegexFailure, "The failure should be a RegexFailure");

            possibleCache = await CreateFilterCacheAsync(testCacheId, string.Empty, "*");
            XAssert.IsFalse(possibleCache.Succeeded, "Should have failed with a bad regex");
            XAssert.IsTrue(possibleCache.Failure is RegexFailure, "The failure should be a RegexFailure");
        }

        private async Task<CasHash> AddPathSet(ICacheSession session, params string[] thePaths)
        {
            var pathTable = new PathTable();
            ObservedPathEntry[] paths = new ObservedPathEntry[thePaths.Length];
            for (int i = 0; i < thePaths.Length; i++)
            {
                AbsolutePath absPath = AbsolutePath.Create(pathTable, thePaths[i]);
                paths[i] = new ObservedPathEntry(absPath, false, false, false, null, false);
            }

            var emptyObservedAccessFileNames = SortedReadOnlyArray<StringId, CaseInsensitiveStringIdComparer>.FromSortedArrayUnsafe(
                ReadOnlyArray<StringId>.Empty,
                new CaseInsensitiveStringIdComparer(pathTable.StringTable));

            ObservedPathSet pathSet = new ObservedPathSet(
                SortedReadOnlyArray<ObservedPathEntry, ObservedPathEntryExpandedPathComparer>.FromSortedArrayUnsafe(
                    ReadOnlyArray<ObservedPathEntry>.FromWithoutCopy(paths),
                    new ObservedPathEntryExpandedPathComparer(pathTable.ExpandedPathComparer)),
                emptyObservedAccessFileNames,
                null);

            using (var pathSetBuffer = new MemoryStream())
            {
                using (var writer = new BuildXLWriter(stream: pathSetBuffer, debug: false, leaveOpen: true, logStats: false))
                {
                    pathSet.Serialize(pathTable, writer);
                }

                pathSetBuffer.Seek(0, SeekOrigin.Begin);

                // Must await such that the dispose of the MemoryStream is only after the write completes
                return await session.AddToCasAsync(pathSetBuffer).SuccessAsync();
            }
        }

        private async Task<Possible<FullCacheRecordWithDeterminism, Failure>> AddToCache(ICacheSession session, params string[] thePaths)
        {
            WeakFingerprintHash weak = FakeStrongFingerprint.CreateWeak(session.CacheId);
            CasHash casHash = await AddPathSet(session, thePaths);
            Hash simpleHash = FakeStrongFingerprint.CreateHash(session.CacheSessionId);
            return await session.AddOrGetAsync(weak, casHash, simpleHash, CasEntries.FromCasHashes(casHash));
        }

        [Fact]
        public async Task TestMustInclude()
        {
            const string TestName = "TestMustInclude";
            string testCacheId = MakeCacheId(TestName);
            var cache = await CreateFilterCacheAsync(testCacheId, @".*\\.(EXE|DLL)$", string.Empty).SuccessAsync();

            string sessionId = "Session";
            ICacheSession session = (await cache.CreateSessionAsync(sessionId)).Success();
            XAssert.AreEqual(sessionId, session.CacheSessionId);

            // This should absolutely work
            await AddToCache(session, file, libraryDll, tool).SuccessAsync();

            // So should this (wish only one of the possible hits)
            await AddToCache(session, libraryDll).SuccessAsync();

            // But this should fail
            var failed = await AddToCache(session, file, dllLib, toolPdb);
            XAssert.IsFalse(failed.Succeeded, "We should have caught a failure to have a regex match");

            await CloseSessionAsync(session, sessionId);

            await ShutdownCacheAsync(cache, testCacheId);
        }

        [Fact]
        public async Task TestMustNotInclude()
        {
            const string TestName = "TestMustNotInclude";
            string testCacheId = MakeCacheId(TestName);
            var cache = await CreateFilterCacheAsync(testCacheId, string.Empty, @".*\\.LIB$").SuccessAsync();

            string sessionId = "Session";
            ICacheSession session = (await cache.CreateSessionAsync(sessionId)).Success();
            XAssert.AreEqual(sessionId, session.CacheSessionId);

            // This should absolutely work
            await AddToCache(session, file, libDll, tool).SuccessAsync();

            // But this should fail due to DLL.LIB
            var failed = await AddToCache(session, file, dllLib, toolPdb);
            XAssert.IsFalse(failed.Succeeded, "We should have caught a failure to have a regex match");
            XAssert.IsTrue(failed.Failure.Describe().Contains(dllLib), "The failure should have contained the file name that failed");

            await CloseSessionAsync(session, sessionId);

            await ShutdownCacheAsync(cache, testCacheId);
        }
    }
}
