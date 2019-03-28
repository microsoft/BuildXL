// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BuildXL.Cache.Analyzer;
using BuildXL.Cache.InMemory;
using BuildXL.Cache.Interfaces;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace BuildXL.Cache.Tests
{
    /// <summary>
    /// Contains the unit tests for the ConsistencyChecker. Tests the
    /// SingleCacheChecker class, TwoLevelCacheChecker class, and the command
    /// line interface.
    /// </summary>
    public class TestConsistencyChecker
    {
        /// <summary>
        /// Constructs the json config string for an InMemory cache
        /// </summary>
        /// <param name="cacheId">Id of the cache being constructed</param>
        /// <returns>json config string</returns>
        private static string CreateInMemoryJsonConfigString(string cacheId)
        {
            const string DefaultInMemoryJsonConfigString = @"{{
                ""Assembly"":""BuildXL.Cache.InMemory"",
                ""Type"": ""BuildXL.Cache.InMemory.MemCacheFactory"",
                ""CacheId"":""{0}"",
                ""StrictMetadataCasCoupling"":false
            }}";

            return string.Format(DefaultInMemoryJsonConfigString, cacheId);
        }

        /// <summary>
        /// Constructs an InMemory cache
        /// </summary>
        /// <param name="cacheId">Id of the cache being constructed</param>
        /// <returns>Cache instance that was just constructed</returns>
        private Task<ICache> InitializeCache(string cacheId)
        {
            string jsonConfigString = CreateInMemoryJsonConfigString(cacheId);

            return CacheFactory.InitializeCacheAsync(jsonConfigString, default(Guid)).SuccessAsync();
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

        /// <summary>
        /// Enumeration of the types of fake builds
        /// </summary>
        private enum FakeBuildType
        {
            /// <summary>
            /// Standard build
            /// </summary>
            Standard,

            /// <summary>
            /// Output file contents are changed from standard output file
            /// contents
            /// </summary>
            InjectDeterminismProblem,

            /// <summary>
            /// Files are not added, CasHash values are calculated independent
            /// of cache
            /// </summary>
            DoNotAddFilesToCas,

            /// <summary>
            /// Files are corrupted after they are added to the CAS
            /// </summary>
            AddCorruptedFileToCas,

            /// <summary>
            /// Files are not added to CAS. Instead, CasHash values are set to NoItem
            /// </summary>
            FilesAsNoItem,

            /// <summary>
            /// The file corresponding to the cas element of the strong
            /// fingerprint being added is tripled in size before being added
            /// to the cas
            /// </summary>
            InjectInputAssertionListAnomaly,

            /// <summary>
            /// Normally the input assertion list is not a properly serialized
            /// path set. This option specifies to serialize the path set.
            /// </summary>
            InjectRealInputAssertionList
        }

        /// <summary>
        /// Performs a fake build. Adds the CAS items but does not add the record. The record is returned to the caller.
        /// </summary>
        /// <param name="session">Session to add CAS items to</param>
        /// <param name="fakeBuildType">The type of fake build to perform</param>
        /// <param name="startIndex">Start index used when generating fake content.</param>
        /// <param name="thePaths">The paths to serialize for the input assertion list</param>
        /// <returns>FullCacheRecord which represents the result of the fake build. NOTE The CacheId of the StrongFingerprint is just the empty string</returns>
        private async Task<FullCacheRecord> PerformFakeBuild(ICacheSession session, FakeBuildType fakeBuildType, int startIndex = 0, string[] thePaths = null)
        {
            FakeBuild fake = new FakeBuild("TestPrefix", 3, startIndex);
            CasHash inputListCashHash;
            if (fakeBuildType.Equals(FakeBuildType.InjectInputAssertionListAnomaly))
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    await fake.OutputList.CopyToAsync(ms);
                    fake.OutputList.Seek(0, SeekOrigin.Begin);
                    await fake.OutputList.CopyToAsync(ms);
                    fake.OutputList.Seek(0, SeekOrigin.Begin);
                    await fake.OutputList.CopyToAsync(ms);
                    ms.Seek(0, SeekOrigin.Begin);
                    string jsonString = Encoding.ASCII.GetString(ms.ToArray());
                    ms.Seek(0, SeekOrigin.Begin);
                    inputListCashHash = await session.AddToCasAsync(ms).SuccessAsync();
                }
            }
            else if (fakeBuildType.Equals(FakeBuildType.InjectRealInputAssertionList))
            {
                inputListCashHash = await AddPathSet(session, thePaths);
            }
            else
            {
                inputListCashHash = await session.AddToCasAsync(fake.OutputList).SuccessAsync();
            }

            CasHash[] items = new CasHash[fake.Outputs.Length];

            for (int i = 0; i < fake.Outputs.Length; i++)
            {
                switch (fakeBuildType)
                {
                    case FakeBuildType.InjectInputAssertionListAnomaly:
                    case FakeBuildType.InjectRealInputAssertionList:
                    case FakeBuildType.Standard:
                    {
                        items[i] = await session.AddToCasAsync(fake.Outputs[i]).SuccessAsync();
                        break;
                    }

                    case FakeBuildType.InjectDeterminismProblem:
                    {
                        Stream nonDeterministicStream = new MemoryStream(Encoding.UTF8.GetBytes("NonDeterministicOutput"));
                        items[i] = await session.AddToCasAsync(nonDeterministicStream).SuccessAsync();
                        break;
                    }

                    case FakeBuildType.DoNotAddFilesToCas:
                    {
                        ContentHash contentHash = await ContentHashingUtilities.HashContentStreamAsync(fake.Outputs[i]);
                        Hash hash = new Hash(contentHash);
                        CasHash casHash = new CasHash(hash);
                        items[i] = casHash;
                        break;
                    }

                    case FakeBuildType.AddCorruptedFileToCas:
                    {
                        MemCacheSession memCacheSession = session as MemCacheSession;
                        items[i] = await session.AddToCasAsync(fake.Outputs[i]).SuccessAsync();
                        byte[] buffer = memCacheSession.Cache.CasStorage[items[i]];
                        if (buffer[0] != 0)
                        {
                            buffer[0] = 0;
                        }
                        else
                        {
                            buffer[0] = 1;
                        }

                        break;
                    }

                    case FakeBuildType.FilesAsNoItem:
                    {
                        items[i] = CasHash.NoItem;
                        break;
                    }

                    default:
                    {
                        XAssert.Fail();
                        break;
                    }
                }
            }

            var outputListFingerprintHash = fake.OutputListHash.ToFingerprint();
            WeakFingerprintHash weak = new WeakFingerprintHash(outputListFingerprintHash);

            StrongFingerprint strongFingerprint = new StrongFingerprint(weak, inputListCashHash, outputListFingerprintHash, string.Empty);
            CasEntries casEntries = new CasEntries(items);

            return new FullCacheRecord(strongFingerprint, casEntries);
        }

        /// <summary>
        /// Adds a session to the cache with one FullCacheRecord
        /// </summary>
        /// <param name="cache">Cache to add session to</param>
        /// <param name="sessionName">Name of the session to add</param>
        public async Task AddSimpleSessionToCache(ICache cache, string sessionName)
        {
            ICacheSession session = await cache.CreateSessionAsync(sessionName).SuccessAsync();

            FullCacheRecord fullCacheRecord = await PerformFakeBuild(session, FakeBuildType.Standard);

            StrongFingerprint sfp = fullCacheRecord.StrongFingerprint;

            FullCacheRecordWithDeterminism record = await session.AddOrGetAsync(sfp.WeakFingerprint, sfp.CasElement, sfp.HashElement, fullCacheRecord.CasEntries).SuccessAsync();

            AssertSuccess(await  session.CloseAsync());
        }

        /// <summary>
        /// Adds multiple sessions to the cache, where each session
        /// produces some unique content and some shared content.
        /// </summary>
        /// <param name="cache">Cache to add the session to</param>
        /// <param name="sessionNames">Enumeration of session names to add</param>
        /// <returns>Awaitable task that will create and add the sessions.</returns>
        public async Task AddSharedSessionsToCache(ICache cache, IEnumerable<string> sessionNames)
        {
            int i = 0;
            foreach (var sessionName in sessionNames)
            {
                ICacheSession session = await cache.CreateSessionAsync(sessionName).SuccessAsync();

                FullCacheRecord fullCacheRecord = await PerformFakeBuild(session, FakeBuildType.Standard, ++i);

                StrongFingerprint sfp = fullCacheRecord.StrongFingerprint;

                FullCacheRecordWithDeterminism record = await session.AddOrGetAsync(sfp.WeakFingerprint, sfp.CasElement, sfp.HashElement, fullCacheRecord.CasEntries).SuccessAsync();

                AssertSuccess(await session.CloseAsync());
            }
        }

        /// <summary>
        /// Adds a session to the cache with one FullCacheRecord but without adding the associated files to the CAS
        /// </summary>
        /// <param name="cache">Cache to add session to</param>
        /// <param name="sessionName">Name of the session to add</param>
        public async Task AddSessionWithMissingContentToCache(ICache cache, string sessionName)
        {
            ICacheSession session = await cache.CreateSessionAsync(sessionName).SuccessAsync();

            FullCacheRecord fullCacheRecord = await PerformFakeBuild(session, FakeBuildType.DoNotAddFilesToCas);

            StrongFingerprint sfp = fullCacheRecord.StrongFingerprint;

            FullCacheRecordWithDeterminism record = await session.AddOrGetAsync(sfp.WeakFingerprint, sfp.CasElement, sfp.HashElement, fullCacheRecord.CasEntries).SuccessAsync();

            AssertSuccess(await session.CloseAsync());
        }

        /// <summary>
        /// Adds a session to the cache with one FullCacheRecord where the files are corrupted before being added to the CAS so the CasHash does not match the actual contents
        /// </summary>
        /// <param name="cache">Cache to add session to</param>
        /// <param name="sessionName">Name of the session to add</param>
        public async Task AddSessionWithCorruptedContentToCache(ICache cache, string sessionName)
        {
            ICacheSession session = await cache.CreateSessionAsync(sessionName).SuccessAsync();

            FullCacheRecord fullCacheRecord = await PerformFakeBuild(session, FakeBuildType.AddCorruptedFileToCas);

            StrongFingerprint sfp = fullCacheRecord.StrongFingerprint;

            await session.AddOrGetAsync(sfp.WeakFingerprint, sfp.CasElement, sfp.HashElement, fullCacheRecord.CasEntries).SuccessAsync();

            AssertSuccess(await session.CloseAsync());
        }

        /// <summary>
        /// Adds a session to the cache with one FullCacheRecord where the files are set to be CasHash.NoItem
        /// </summary>
        /// <param name="cache">Cache to add session to</param>
        /// <param name="sessionName">Name of the session to add</param>
        public async Task AddSessionWithCasHashNoItemsToCache(ICache cache, string sessionName)
        {
            ICacheSession session = await cache.CreateSessionAsync(sessionName).SuccessAsync();

            FullCacheRecord fullCacheRecord = await PerformFakeBuild(session, FakeBuildType.FilesAsNoItem);

            StrongFingerprint sfp = fullCacheRecord.StrongFingerprint;

            await session.AddOrGetAsync(sfp.WeakFingerprint, sfp.CasElement, sfp.HashElement, fullCacheRecord.CasEntries).SuccessAsync();

            AssertSuccess(await session.CloseAsync());
        }

        /// <summary>
        /// Adds a session to the cache where the StrongFingerprints do not have CasEntries associated with them
        /// </summary>
        /// <param name="cache">Cache to add session to</param>
        /// <param name="sessionName">Name of the session to add</param>
        public async Task AddSessionWithBadStrongFingerprint(ICache cache, string sessionName)
        {
            ICacheSession session = await cache.CreateSessionAsync(sessionName).SuccessAsync();
            MemCacheSession memCacheSession = session as MemCacheSession;
            memCacheSession.SessionEntries.TryAdd(new StrongFingerprint(WeakFingerprintHash.NoHash, CasHash.NoItem, new Hash(FingerprintUtilities.ZeroFingerprint), "NonExistentCache"), 1);
            AssertSuccess(await session.CloseAsync());
        }

        /// <summary>
        /// Adds a session to the cache with one FullCacheRecord that is set to be tool deterministic but the content files of the CasEntries all contain bad data
        /// </summary>
        /// <param name="cache">Cache to add session to</param>
        /// <param name="sessionName">Name of the session to add</param>
        public async Task AddToolDeterminismProblemSessionToCache(ICache cache, string sessionName)
        {
            ICacheSession session = await cache.CreateSessionAsync(sessionName).SuccessAsync();

            FullCacheRecord fullCacheRecord = await PerformFakeBuild(session, FakeBuildType.InjectDeterminismProblem);

            StrongFingerprint sfp = fullCacheRecord.StrongFingerprint;
            CasEntries casEntries = new CasEntries(fullCacheRecord.CasEntries, CacheDeterminism.Tool);

            await session.AddOrGetAsync(sfp.WeakFingerprint, sfp.CasElement, sfp.HashElement, casEntries).SuccessAsync();

            AssertSuccess(await session.CloseAsync());
        }

        /// <summary>
        /// Adds a session to the cache with one FullCacheRecord that is set to be cache deterministic but the content files of the CasEntries all contain bad data
        /// </summary>
        /// <param name="cache">Cache to add session to</param>
        /// <param name="sessionName">Name of the session to add</param>
        /// <param name="remoteCacheGuid">Sets the CasEntries object of the FullCacheRecord to be deterministic relative to this cache guid</param>
        public async Task AddCacheDeterminismProblemSessionToCache(ICache cache, string sessionName, Guid remoteCacheGuid)
        {
            ICacheSession session = await cache.CreateSessionAsync(sessionName).SuccessAsync();

            FullCacheRecord fullCacheRecord = await PerformFakeBuild(session, FakeBuildType.InjectDeterminismProblem);

            StrongFingerprint sfp = fullCacheRecord.StrongFingerprint;
            CasEntries casEntries = new CasEntries(fullCacheRecord.CasEntries, CacheDeterminism.ViaCache(remoteCacheGuid, CacheDeterminism.NeverExpires));

            await session.AddOrGetAsync(sfp.WeakFingerprint, sfp.CasElement, sfp.HashElement, casEntries).SuccessAsync();

            AssertSuccess(await session.CloseAsync());
        }

        public async Task AddInputAssertionListAnomalySessionToCache(ICache cache, string sessionName)
        {
            ICacheSession session = await cache.CreateSessionAsync(sessionName).SuccessAsync();

            FullCacheRecord fullCacheRecordNormal = await PerformFakeBuild(session, FakeBuildType.Standard);

            StrongFingerprint sfpNormal = fullCacheRecordNormal.StrongFingerprint;

            FullCacheRecordWithDeterminism record = await session.AddOrGetAsync(sfpNormal.WeakFingerprint, sfpNormal.CasElement, sfpNormal.HashElement, fullCacheRecordNormal.CasEntries).SuccessAsync();

            FullCacheRecord fullCacheRecordBad = await PerformFakeBuild(session, FakeBuildType.InjectInputAssertionListAnomaly);

            StrongFingerprint sfpBad = fullCacheRecordBad.StrongFingerprint;

            FullCacheRecordWithDeterminism recordBad = await session.AddOrGetAsync(sfpBad.WeakFingerprint, sfpBad.CasElement, sfpBad.HashElement, fullCacheRecordBad.CasEntries).SuccessAsync();

            AssertSuccess(await session.CloseAsync());
        }

        public async Task AddRealInputAssertionListSessionToCache(ICache cache, string sessionName, string[] thePaths)
        {
            ICacheSession session = await cache.CreateSessionAsync(sessionName).SuccessAsync();

            FullCacheRecord fullCacheRecord = await PerformFakeBuild(session, FakeBuildType.InjectRealInputAssertionList, thePaths: thePaths);

            StrongFingerprint sfp = fullCacheRecord.StrongFingerprint;

            FullCacheRecordWithDeterminism recordBad = await session.AddOrGetAsync(sfp.WeakFingerprint, sfp.CasElement, sfp.HashElement, fullCacheRecord.CasEntries).SuccessAsync();

            AssertSuccess(await session.CloseAsync());
        }

        /// <summary>
        /// Searches the collection of errors for a specific error type. If found, returns true.
        /// </summary>
        /// <param name="errors">Collection of CacheErrors to search over</param>
        /// <param name="errorType">Specific error type to search for</param>
        /// <returns>True if the error type was found</returns>
        public bool FindErrorType(IEnumerable<CacheError> errors, CacheErrorType errorType)
        {
            foreach (CacheError error in errors)
            {
                if (error.Type == errorType)
                {
                    return true;
                }
            }

            return false;
        }

        /// <nodoc/>
        [Fact]
        public async Task NoErrorsSingleCacheTest()
        {
            string sessionName = "TestSession";
            ICache cache = await InitializeCache("LocalCacheId");
            await AddSimpleSessionToCache(cache, sessionName);

            SingleCacheChecker cacheChecker = new SingleCacheChecker(cache, false);
            IEnumerable<CacheError> errors = await cacheChecker.CheckCache(new Regex(".*"));

            XAssert.AreEqual(0, errors.Count());
            XAssert.AreEqual(1, cacheChecker.NumSessions);
            XAssert.AreEqual(1, cacheChecker.NumSessionsChecked);
        }

        /// <nodoc/>
        [Fact]
        public async Task CorruptedCasFileTest()
        {
            string sessionName = "TestSession";
            ICache cache = await InitializeCache("LocalCacheId");
            await AddSessionWithCorruptedContentToCache(cache, sessionName);

            SingleCacheChecker cacheChecker = new SingleCacheChecker(cache, true);
            IEnumerable<CacheError> errors = await cacheChecker.CheckCache(new Regex(".*"));

            bool errorFound = FindErrorType(errors, CacheErrorType.CasHashError);
            XAssert.IsTrue(errorFound);
            XAssert.AreEqual(1, cacheChecker.NumSessions);
            XAssert.AreEqual(1, cacheChecker.NumSessionsChecked);
        }

        /// <nodoc/>
        [Fact]
        public async Task CasHashNoItemTest()
        {
            string sessionName = "TestSession";
            ICache cache = await InitializeCache("LocalCacheId");
            await AddSessionWithCasHashNoItemsToCache(cache, sessionName);

            SingleCacheChecker cacheChecker = new SingleCacheChecker(cache, true);
            IEnumerable<CacheError> errors = await cacheChecker.CheckCache(new Regex(".*"));

            XAssert.AreEqual(0, errors.Count());
            XAssert.AreEqual(1, cacheChecker.NumSessions);
            XAssert.AreEqual(1, cacheChecker.NumSessionsChecked);
        }

        /// <nodoc/>
        [Fact]
        public async Task MissingCasFileTest()
        {
            string sessionName = "TestSession";
            ICache cache = await InitializeCache("LocalCacheId");
            await AddSessionWithMissingContentToCache(cache, sessionName);

            SingleCacheChecker cacheChecker = new SingleCacheChecker(cache, true);
            IEnumerable<CacheError> errors = await cacheChecker.CheckCache(new Regex(".*"));

            bool errorFound = FindErrorType(errors, CacheErrorType.CasHashError);
            XAssert.IsTrue(errorFound);
            XAssert.AreEqual(1, cacheChecker.NumSessions);
            XAssert.AreEqual(1, cacheChecker.NumSessionsChecked);
        }

        /// <nodoc/>
        [Fact]
        public async Task BadStrongFingerprintTest()
        {
            string sessionName = "TestSession";
            ICache cache = await InitializeCache("LocalCacheId");

            await AddSessionWithBadStrongFingerprint(cache, sessionName);

            SingleCacheChecker cacheChecker = new SingleCacheChecker(cache, false);
            IEnumerable<CacheError> errors = await cacheChecker.CheckCache(new Regex(".*"));

            bool errorFound = FindErrorType(errors, CacheErrorType.StrongFingerprintError);
            XAssert.IsTrue(errorFound);
            XAssert.AreEqual(1, cacheChecker.NumSessions);
            XAssert.AreEqual(1, cacheChecker.NumSessionsChecked);
        }

        /// <nodoc/>
        [Fact]
        public async Task NoErrorsTwoLevelCacheTest()
        {
            string sessionName = "TestSession";
            ICache localCache = await InitializeCache("LocalCacheId");
            ICache remoteCache = await InitializeCache("RemoteCacheId");

            await AddSimpleSessionToCache(remoteCache, sessionName);
            await AddSimpleSessionToCache(localCache, sessionName);

            TwoLevelCacheChecker twoLevelCacheChecker = new TwoLevelCacheChecker(localCache, remoteCache, false);
            IEnumerable<CacheError> errors = await twoLevelCacheChecker.CheckCache(new Regex(".*"));

            XAssert.AreEqual(0, errors.Count());
            XAssert.AreEqual(1, twoLevelCacheChecker.NumSessions);
            XAssert.AreEqual(1, twoLevelCacheChecker.NumSessionsChecked);
        }

        /// <nodoc/>
        [Fact]
        public async Task NoErrorsCacheDeterminismTest()
        {
            string sessionName = "TestSession";
            ICache localCache = await InitializeCache("LocalCacheId");
            ICache remoteCache = await InitializeCache("RemoteCacheId");

            await AddSimpleSessionToCache(remoteCache, sessionName);
            await AddCacheDeterminismProblemSessionToCache(localCache, sessionName, default(Guid));

            TwoLevelCacheChecker twoLevelCacheChecker = new TwoLevelCacheChecker(localCache, remoteCache, false);
            IEnumerable<CacheError> errors = await twoLevelCacheChecker.CheckCache(new Regex(".*"));

            bool errorFound = FindErrorType(errors, CacheErrorType.DeterminismError);
            XAssert.IsFalse(errorFound);
            XAssert.AreEqual(1, twoLevelCacheChecker.NumSessions);
            XAssert.AreEqual(1, twoLevelCacheChecker.NumSessionsChecked);
        }

        /// <nodoc/>
        [Fact]
        public async Task LocalMissingFingerprintTest()
        {
            string sessionName = "TestSession";
            ICache localCache = await InitializeCache("LocalCacheId");
            ICache remoteCache = await InitializeCache("RemoteCacheId");

            await AddSimpleSessionToCache(remoteCache, sessionName);
            await AddSimpleSessionToCache(localCache, sessionName);

            TwoLevelCacheChecker twoLevelCacheChecker = new TwoLevelCacheChecker(localCache, remoteCache, false);
            IEnumerable<CacheError> errors = await twoLevelCacheChecker.CheckCache(new Regex(".*"));

            XAssert.AreEqual(0, errors.Count());
            XAssert.AreEqual(1, twoLevelCacheChecker.NumSessions);
            XAssert.AreEqual(1, twoLevelCacheChecker.NumSessionsChecked);
        }

        /// <nodoc/>
        [Fact]
        public async Task BrokenCacheDeterminismTest()
        {
            string sessionName = "TestSession";
            ICache localCache = await InitializeCache("LocalCacheId");
            ICache remoteCache = await InitializeCache("RemoteCacheId");

            await AddSimpleSessionToCache(remoteCache, sessionName);
            await AddCacheDeterminismProblemSessionToCache(localCache, sessionName, remoteCache.CacheGuid);

            TwoLevelCacheChecker twoLevelCacheChecker = new TwoLevelCacheChecker(localCache, remoteCache, false);
            IEnumerable<CacheError> errors = await twoLevelCacheChecker.CheckCache(new Regex(".*"));

            bool errorFound = FindErrorType(errors, CacheErrorType.DeterminismError);
            XAssert.IsTrue(errorFound);
            XAssert.AreEqual(1, twoLevelCacheChecker.NumSessions);
            XAssert.AreEqual(1, twoLevelCacheChecker.NumSessionsChecked);
        }

        /// <nodoc/>
        [Fact]
        public async Task BrokenToolDeterminismTest()
        {
            string sessionName = "TestSession";
            ICache localCache = await InitializeCache("LocalCacheId");
            ICache remoteCache = await InitializeCache("RemoteCacheId");

            await AddSimpleSessionToCache(remoteCache, sessionName);
            await AddToolDeterminismProblemSessionToCache(localCache, sessionName);

            TwoLevelCacheChecker twoLevelCacheChecker = new TwoLevelCacheChecker(localCache, remoteCache, false);
            IEnumerable<CacheError> errors = await twoLevelCacheChecker.CheckCache(new Regex(".*"));

            bool errorFound = FindErrorType(errors, CacheErrorType.DeterminismError);
            XAssert.IsTrue(errorFound);
            XAssert.AreEqual(1, twoLevelCacheChecker.NumSessions);
            XAssert.AreEqual(1, twoLevelCacheChecker.NumSessionsChecked);
        }

        /// <nodoc/>
        [Fact]
        public async Task SimpleStatisticalAnalyzerTest()
        {
            string sessionName = "TestSession";
            ICache cache = await InitializeCache("TestCacheId");

            await AddSimpleSessionToCache(cache, sessionName);

            StatisticalAnalyzer statisticalAnalyzer = new StatisticalAnalyzer(cache);
            IEnumerable<SessionChurnInfo> sessionChurnInfo = statisticalAnalyzer.Analyze(new Regex(".*"), false);

            XAssert.AreEqual(1, sessionChurnInfo.Count());

            SessionChurnInfo testSessionChurnInfo = sessionChurnInfo.First();

            XAssert.AreEqual(sessionName, testSessionChurnInfo.SessionName);

            SessionStrongFingerprintChurnInfo testSessionStrongFingerprintChurnInfo = testSessionChurnInfo.StrongFingerprintChurnInfo;

            XAssert.AreEqual(100.0, testSessionStrongFingerprintChurnInfo.PercentageUniqueStrongFingerprints);
        }

        /// <nodoc/>
        [Fact]
        public async Task DuplicateSessionStatisticalAnalyzerTest()
        {
            string originalSessionName = "TestSessionA";
            string duplicateSessionName = "TestSessionB";
            ICache cache = await InitializeCache("TestCacheId");

            await AddSimpleSessionToCache(cache, originalSessionName);
            await AddSimpleSessionToCache(cache, duplicateSessionName);

            StatisticalAnalyzer statisticalAnalyzer = new StatisticalAnalyzer(cache);
            IEnumerable<SessionChurnInfo> sessionChurnInfo = statisticalAnalyzer.Analyze(new Regex(".*"), false);

            XAssert.AreEqual(2, sessionChurnInfo.Count());

            SessionChurnInfo originalSessionChurnInfo = sessionChurnInfo.First();

            XAssert.AreEqual(originalSessionName, originalSessionChurnInfo.SessionName);

            SessionStrongFingerprintChurnInfo originalSessionStrongFingerprintChurnInfo = originalSessionChurnInfo.StrongFingerprintChurnInfo;

            XAssert.AreEqual(100.0, originalSessionStrongFingerprintChurnInfo.PercentageUniqueStrongFingerprints);

            SessionChurnInfo duplicateSessionChurnInfo = sessionChurnInfo.ElementAt(1);

            XAssert.AreEqual(duplicateSessionName, duplicateSessionChurnInfo.SessionName);

            SessionStrongFingerprintChurnInfo duplicateSessionStrongFingerprintChurnInfo = duplicateSessionChurnInfo.StrongFingerprintChurnInfo;

            // Duplicate session should have zero unique strong fingerprints from the first session so we expect this to be 0%
            XAssert.AreEqual(0.0, duplicateSessionStrongFingerprintChurnInfo.PercentageUniqueStrongFingerprints);
        }

        /// <nodoc/>
        [Fact]
        public async Task SessionCountStatisticalAnalyzerTest()
        {
            string originalSessionName = "TestSessionA";
            string duplicateSessionName = "TestSessionB";
            ICache cache = await InitializeCache("TestCacheId");

            await AddSimpleSessionToCache(cache, originalSessionName);
            await AddSimpleSessionToCache(cache, duplicateSessionName);

            StatisticalAnalyzer statisticalAnalyzer = new StatisticalAnalyzer(cache);
            IEnumerable<SessionChurnInfo> sessionChurnInfo = statisticalAnalyzer.Analyze(new Regex(".*B"), false);

            foreach (SessionChurnInfo info in sessionChurnInfo)
            {
                // We are just forcing the enumerable to enumerate
            }

            XAssert.AreEqual(2, statisticalAnalyzer.NumSessions);

            // Only one of the two sessions should have been analyzed because of the regex used
            XAssert.AreEqual(1, statisticalAnalyzer.NumSessionsAnalyzed);
        }

        /// <nodoc/>
        [Fact]
        public async Task InputAssertionListCheckerNoAnomaliesTest()
        {
            string sessionName = "TestSession";
            ICache cache = await InitializeCache("TestCacheId");

            await AddSimpleSessionToCache(cache, sessionName);

            InputAssertionListChecker inputAssertionListChecker =
                new InputAssertionListChecker(cache, InputAssertionListChecker.DefaultDisparityFactor);
            ConcurrentDictionary<CacheError, int> cacheErrors = new ConcurrentDictionary<CacheError, int>();
            IEnumerable<InputAssertionListAnomaly> inputAssertionListAnomalies =
                inputAssertionListChecker.PerformAnomalyCheck(new Regex(".*"), cacheErrors);

            XAssert.AreEqual(0, cacheErrors.Count);
            XAssert.AreEqual(0, inputAssertionListAnomalies.Count());
            XAssert.AreEqual(1, inputAssertionListChecker.NumSessions);
            XAssert.AreEqual(1, inputAssertionListChecker.NumSessionsChecked);
            XAssert.AreEqual(1, inputAssertionListChecker.NumWeakFingerprintsChecked);
            XAssert.AreEqual(0, inputAssertionListChecker.NumWeakFingerprintsWithTwoOrMoreSFPs);
        }

        /// <nodoc/>
        [Fact]
        public async Task InputAssertionListCheckerAnomaliesTest()
        {
            string sessionName = "TestSession";
            ICache cache = await InitializeCache("TestCacheId");

            await AddInputAssertionListAnomalySessionToCache(cache, sessionName);

            InputAssertionListChecker inputAssertionListChecker =
                new InputAssertionListChecker(cache, InputAssertionListChecker.DefaultDisparityFactor);
            ConcurrentDictionary<CacheError, int> cacheErrors = new ConcurrentDictionary<CacheError, int>();
            ConcurrentDictionary<WeakFingerprintHash, byte> weakFingerprintsFoundFromSessions = new ConcurrentDictionary<WeakFingerprintHash, byte>();
            IEnumerable<InputAssertionListAnomaly> inputAssertionListAnomalies =
                inputAssertionListChecker.PerformAnomalyCheck(new Regex(".*"), cacheErrors, weakFingerprintsFoundFromSessions);

            XAssert.AreEqual(1, inputAssertionListAnomalies.Count());

            // Cache errors are because the fake build input assertion lists cannot be deserialized
            XAssert.AreEqual(2, cacheErrors.Count);
            XAssert.AreEqual(1, inputAssertionListChecker.NumSessions);
            XAssert.AreEqual(1, inputAssertionListChecker.NumSessionsChecked);
            XAssert.AreEqual(1, inputAssertionListChecker.NumWeakFingerprintsChecked);
            XAssert.AreEqual(1, inputAssertionListChecker.NumWeakFingerprintsWithTwoOrMoreSFPs);

            cacheErrors.Clear();
            inputAssertionListAnomalies =
                inputAssertionListChecker.PerformAnomalyCheck(weakFingerprintsFoundFromSessions.Keys, cacheErrors);

            XAssert.AreEqual(1, inputAssertionListAnomalies.Count());

            // Cache errors are because the fake build input assertion lists cannot be deserialized
            XAssert.AreEqual(2, cacheErrors.Count);
            XAssert.AreEqual(1, inputAssertionListChecker.NumSessions);
            XAssert.AreEqual(1, inputAssertionListChecker.NumSessionsChecked);
            XAssert.AreEqual(1, inputAssertionListChecker.NumWeakFingerprintsChecked);
            XAssert.AreEqual(1, inputAssertionListChecker.NumWeakFingerprintsWithTwoOrMoreSFPs);
        }

        /// <nodoc/>
        [Fact]
        public async Task InputAssertionListCheckerAnomaliesRegexTest()
        {
            string sessionName = "TestSession";
            ICache cache = await InitializeCache("TestCacheId");

            await AddInputAssertionListAnomalySessionToCache(cache, sessionName);

            InputAssertionListChecker inputAssertionListChecker =
                new InputAssertionListChecker(cache, InputAssertionListChecker.DefaultDisparityFactor);
            ConcurrentDictionary<CacheError, int> cacheErrors = new ConcurrentDictionary<CacheError, int>();
            IEnumerable<InputAssertionListAnomaly> inputAssertionListAnomalies =
                inputAssertionListChecker.PerformAnomalyCheck(new Regex("RESTRICTIVEREGEX"), cacheErrors);

            // There is an anomaly in the cache but the regex should have
            // filtered out all sessions so no anomalies should be found
            XAssert.AreEqual(0, inputAssertionListAnomalies.Count());
            XAssert.AreEqual(0, cacheErrors.Count);
            XAssert.AreEqual(1, inputAssertionListChecker.NumSessions);
            XAssert.AreEqual(0, inputAssertionListChecker.NumSessionsChecked);
            XAssert.AreEqual(0, inputAssertionListChecker.NumWeakFingerprintsChecked);
            XAssert.AreEqual(0, inputAssertionListChecker.NumWeakFingerprintsWithTwoOrMoreSFPs);
        }

        /// <nodoc/>
        [Fact]
        public async Task InputAssertionListCheckerAnomaliesDisparityFactorTest()
        {
            string sessionName = "TestSession";
            ICache cache = await InitializeCache("TestCacheId");

            await AddInputAssertionListAnomalySessionToCache(cache, sessionName);

            InputAssertionListChecker inputAssertionListChecker = new InputAssertionListChecker(cache, 3);
            ConcurrentDictionary<CacheError, int> cacheErrors = new ConcurrentDictionary<CacheError, int>();
            IEnumerable<InputAssertionListAnomaly> inputAssertionListAnomalies =
                inputAssertionListChecker.PerformAnomalyCheck(new Regex(".*"), cacheErrors);

            // There is an anomaly in the cache but the minimum disparity value
            // was set to be higher than the disparity that exists in the cache
            // so no anomalies should be found
            XAssert.AreEqual(0, inputAssertionListAnomalies.Count());
            XAssert.AreEqual(0, cacheErrors.Count);
            XAssert.AreEqual(1, inputAssertionListChecker.NumSessions);
            XAssert.AreEqual(1, inputAssertionListChecker.NumSessionsChecked);
            XAssert.AreEqual(1, inputAssertionListChecker.NumWeakFingerprintsChecked);
            XAssert.AreEqual(1, inputAssertionListChecker.NumWeakFingerprintsWithTwoOrMoreSFPs);
        }

        /// <nodoc/>
        [Fact]
        public async Task InputAssertionListDumpAllTest()
        {
            string sessionName = "TestSession";
            ICache cache = await InitializeCache("TestCacheId");

            string[] inputs = new string[3];
            inputs[0] = PathGeneratorUtilities.GetAbsolutePath("B", "FIRSTINPUT.CS");
            inputs[1] = PathGeneratorUtilities.GetAbsolutePath("B", "SECONDINPUT.CS");
            inputs[2] = PathGeneratorUtilities.GetAbsolutePath("B", "THIRDINPUT.TXT");
            await AddRealInputAssertionListSessionToCache(cache, sessionName, inputs);

            InputAssertionListChecker inputAssertionListChecker = new InputAssertionListChecker(cache);
            ConcurrentDictionary<CacheError, int> cacheErrors = new ConcurrentDictionary<CacheError, int>();
            Func<string, bool> inputListCheck;
            Regex inputAssertionListDumpMustIncludeRegex = new Regex("FIRST");
            Regex inputAssertionListDumpMustNotIncludeRegex = new Regex("OUTPUT");
            inputListCheck = (inputList) =>
            {
                return inputAssertionListDumpMustIncludeRegex.IsMatch(inputList) && !inputAssertionListDumpMustNotIncludeRegex.IsMatch(inputList);
            };
            IEnumerable<InputAssertionList> inputAssertionLists = inputAssertionListChecker.GetSuspectInputAssertionLists(new Regex(".*"), inputListCheck, cacheErrors);
            XAssert.AreEqual(1, inputAssertionLists.Count());
            XAssert.AreEqual(0, cacheErrors.Count);
            XAssert.AreEqual(1, inputAssertionListChecker.NumSessions);
            XAssert.AreEqual(1, inputAssertionListChecker.NumSessionsChecked);
        }

        /// <nodoc/>
        [Fact]
        public async Task InputAssertionListDumpNoneTest()
        {
            string sessionName = "TestSession";
            ICache cache = await InitializeCache("TestCacheId");

            string[] inputs = new string[3];
            inputs[0] = PathGeneratorUtilities.GetAbsolutePath("B", "FIRSTINPUT.CS");
            inputs[1] = PathGeneratorUtilities.GetAbsolutePath("B", "SECONDINPUT.CS");
            inputs[2] = PathGeneratorUtilities.GetAbsolutePath("B", "THIRDINPUT.TXT");
            await AddRealInputAssertionListSessionToCache(cache, sessionName, inputs);

            InputAssertionListChecker inputAssertionListChecker = new InputAssertionListChecker(cache);
            ConcurrentDictionary<CacheError, int> cacheErrors = new ConcurrentDictionary<CacheError, int>();
            Func<string, bool> inputListCheck;
            Regex inputAssertionListDumpMustIncludeRegex = new Regex("FIRST");
            Regex inputAssertionListDumpMustNotIncludeRegex = new Regex("INPUT");
            inputListCheck = (inputList) =>
            {
                return inputAssertionListDumpMustIncludeRegex.IsMatch(inputList) && !inputAssertionListDumpMustNotIncludeRegex.IsMatch(inputList);
            };
            IEnumerable<InputAssertionList> inputAssertionLists = inputAssertionListChecker.GetSuspectInputAssertionLists(new Regex(".*"), inputListCheck, cacheErrors);
            XAssert.AreEqual(0, inputAssertionLists.Count());
            XAssert.AreEqual(0, cacheErrors.Count);
            XAssert.AreEqual(1, inputAssertionListChecker.NumSessions);
            XAssert.AreEqual(1, inputAssertionListChecker.NumSessionsChecked);
        }

        /// <nodoc/>
        [Fact]
        public void AnalyzerHelpTest()
        {
            string[] args = { "/?" };
            bool exceptionThrown = false;
            try
            {
                int returnValue = BuildXL.Cache.Analyzer.Analyzer.Main(args);
                XAssert.AreEqual(0, returnValue);
            }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
            catch
            {
                exceptionThrown = true;
            }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler

            XAssert.IsFalse(exceptionThrown);
        }

        /// <nodoc/>
        [Fact]
        public void NoJsonStringProvidedTest()
        {
            string[] args = { string.Empty };

            // This should return a non-zero value because a json string was not provided
            int returnValue = BuildXL.Cache.Analyzer.Analyzer.Main(args);
            XAssert.AreNotEqual(0, returnValue);
        }

        /// <nodoc/>
        [Fact]
        public void ModeNotSpecifiedTest()
        {
            string jsonCacheConfig = CreateInMemoryJsonConfigString("EmptyTestCache");
            string[] args = { "/js:" + jsonCacheConfig };

            // This should return a non-zero value because a mode was not provided
            // (mode options being consistencyCheck or statisticalAnalysis)
            int returnValue = BuildXL.Cache.Analyzer.Analyzer.Main(args);
            XAssert.AreNotEqual(0, returnValue);
        }

        /// <nodoc/>
        [Fact]
        public void EmptyCacheConsistencyCheckTest()
        {
            string jsonCacheConfig = CreateInMemoryJsonConfigString("EmptyTestCache");
            string[] args = { "/js:" + jsonCacheConfig, "/cc" };

            // This should return a zero because these are valid command line options
            int returnValue = BuildXL.Cache.Analyzer.Analyzer.Main(args);
            XAssert.AreEqual(0, returnValue);
        }

        /// <nodoc/>
        [Fact]
        public void EmptyCacheStatisticalAnalysisTest()
        {
            string jsonCacheConfig = CreateInMemoryJsonConfigString("EmptyTestCache");
            string[] args = { "/js:" + jsonCacheConfig, "/sa" };

            // This should return a zero because these are valid command line options
            int returnValue = BuildXL.Cache.Analyzer.Analyzer.Main(args);
            XAssert.AreEqual(0, returnValue);
        }

        /// <nodoc/>
        [Fact]
        public void BadSessionIDFilterTest()
        {
            string jsonCacheConfig = CreateInMemoryJsonConfigString("EmptyTestCache");
            string[] args = { "/js:" + jsonCacheConfig, "/cc", "/sessionIDFilter:*." };

            // This should return a non-zero value because a badly formatted regex string was provided
            int returnValue = BuildXL.Cache.Analyzer.Analyzer.Main(args);
            XAssert.AreNotEqual(0, returnValue);
        }

        /// <nodoc/>
        [Fact]

        public async Task ContentAnalysisTest()
        {
            var sessionNames = new List<string>() { "TestSessionA", "TestSessionB", "TestSessionC" };
            ICache cache = await InitializeCache("TestCacheId");

            await AddSharedSessionsToCache(cache, sessionNames);

            StatisticalAnalyzer statisticalAnalyzer = new StatisticalAnalyzer(cache);
            var sessionChurnInfo = statisticalAnalyzer.Analyze(new Regex(".*"), true).ToArray();

            XAssert.AreEqual(sessionNames.Count, sessionChurnInfo.Length);

            SessionChurnInfo testSessionChurnInfo;
            SessionContentInfo contentInfo;

            // ChurnInfo should include the correct session names and non zero
            // total content sizes and counts. New content size and count should
            // always be zero or greater.
            for (int i = 0; i < sessionNames.Count; ++i)
            {
                testSessionChurnInfo = sessionChurnInfo[i];
                contentInfo = testSessionChurnInfo.ContentInfo;

                XAssert.AreEqual(sessionNames[i], testSessionChurnInfo.SessionName);
                XAssert.IsTrue(contentInfo.TotalContentCount > 0);
                XAssert.IsTrue(contentInfo.TotalContentSize > 0);
                XAssert.IsTrue(contentInfo.NewContentCount >= 0);
                XAssert.IsTrue(contentInfo.NewContentSize >= 0);
                XAssert.AreEqual(contentInfo.ContentErrors, 0);
            }

            // Additionally, session A will have *all* new content (FakeBuild usually makes each entry 12 bytes,
            // but I'm not hard coding that here).
            testSessionChurnInfo = sessionChurnInfo[0];
            contentInfo = testSessionChurnInfo.ContentInfo;

            XAssert.IsTrue(contentInfo.TotalContentCount == contentInfo.NewContentCount);
            XAssert.IsTrue(contentInfo.TotalContentSize == contentInfo.NewContentSize);

            // Sessions B and C will each have new content, but will also share some with A
            // and each other, so new content size and count should be smaller than total.
            for (int i = 1; i < sessionNames.Count; ++i)
            {
                testSessionChurnInfo = sessionChurnInfo[i];
                contentInfo = testSessionChurnInfo.ContentInfo;

                XAssert.IsTrue(contentInfo.TotalContentSize > 0);
                XAssert.IsTrue(contentInfo.NewContentSize > 0);
                XAssert.IsTrue(contentInfo.TotalContentCount > contentInfo.NewContentCount);
                XAssert.IsTrue(contentInfo.TotalContentSize > contentInfo.NewContentSize);
            }
        }

        [Fact]
        public async Task ContentBreakdownTest()
        {
            var sessionNames = new List<string>() { "Elephant", "CedarWaxwing", "CedarTree" };
            ICache cache = await InitializeCache("TestCacheId");

            await AddSharedSessionsToCache(cache, sessionNames);

            var analyzer = new ContentBreakdownAnalyzer(cache);
            var breakdownInfo = analyzer.Analyze(new Regex("Cedar*")).ToArray();

            // Should have excluded "Elephant"
            XAssert.AreEqual(sessionNames.Count - 1, breakdownInfo.Length);

            // Skip the Elephant
            for (int i = 1; i < sessionNames.Count; ++i)
            {
                // Verify we're not talking past each other by matching the session names.
                ContentBreakdownInfo breakdown = breakdownInfo.Where((a) => a.SessionName.Equals(sessionNames[i])).FirstOrDefault();
                XAssert.IsNotNull(breakdown);

                // Add up the cas entry and element sizes for the session
                long casEntryTotal = breakdown.CasEntrySizes.Sizes.Sum();
                long casElementTotal = breakdown.CasElementSizes.Sizes.Sum();

                // TODO: I hate relying on these hard coded values. I need a backchannel out
                //       of fakebuild for this info for when it changes.

                // Build content is "TestPrefix:N", or 12 characters. There are three of them.
                XAssert.AreEqual(3 * 12, casEntryTotal);
                XAssert.AreEqual(3, breakdown.CasEntrySizes.Count);

                // Input lists are... a list of the content (plus newlines). There should be 1.
                XAssert.AreEqual(3 * 13, casElementTotal);
                XAssert.AreEqual(1, breakdown.CasElementSizes.Count);
            }
        }

        [Fact]
        public void PercentileCdgTest()
        {
            var A = new long[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };
            var B = new long[] { 1, 1, 1, 1, 1, 1, 1, 1, 1, 1 };

            var percentiles = new int[] { 10, 50, 90 };
            var unorderedPercentiles = new int[] { 50, 10, 90 };
            var badPercentiles = new int[] { 50, 10, 130, 90 };

            var breakdown = A.GetPercentilesAndCdg(percentiles);
            XAssert.AreEqual(1, breakdown[10].Item1);
            XAssert.AreEqual(0, breakdown[10].Item2);
            XAssert.AreEqual(5, breakdown[50].Item1);
            XAssert.AreEqual(10, breakdown[50].Item2);
            XAssert.AreEqual(9, breakdown[90].Item1);
            XAssert.AreEqual(36, breakdown[90].Item2);

            breakdown = A.GetPercentilesAndCdg(unorderedPercentiles);
            XAssert.AreEqual(1, breakdown[10].Item1);
            XAssert.AreEqual(0, breakdown[10].Item2);
            XAssert.AreEqual(5, breakdown[50].Item1);
            XAssert.AreEqual(10, breakdown[50].Item2);
            XAssert.AreEqual(9, breakdown[90].Item1);
            XAssert.AreEqual(36, breakdown[90].Item2);

            Exception ex = Assert.Throws<ArgumentException>(() => A.GetPercentilesAndCdg(badPercentiles));
            XAssert.AreEqual("Invalid percentile specified: 130", ex.Message);

            breakdown = B.GetPercentilesAndCdg(percentiles);
            XAssert.AreEqual(1, breakdown[10].Item1);
            XAssert.AreEqual(1, breakdown[10].Item2);
            XAssert.AreEqual(1, breakdown[50].Item1);
            XAssert.AreEqual(5, breakdown[50].Item2);
            XAssert.AreEqual(1, breakdown[90].Item1);
            XAssert.AreEqual(9, breakdown[90].Item2);
        }

        private void AssertSuccess<T>(Possible<T, Failure> possible)
        {
            XAssert.IsTrue(possible.Succeeded);
        }
    }
}
