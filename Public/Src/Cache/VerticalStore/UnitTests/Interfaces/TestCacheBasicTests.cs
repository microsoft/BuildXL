// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.Interfaces;
using BuildXL.Storage;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace BuildXL.Cache.Tests
{
    /// <summary>
    /// This is an abstract class that provides core cache testing for
    /// any cache that implements the cache interfaces.  Just provide
    /// the method to get a new test cache instance with given names
    /// and the tests will then run against your specific configuration.
    ///
    /// All cache implementations should have at least one test class that
    /// is a subclass of this core test suite.
    /// </summary>
    [ExcludeFromCodeCoverage]
    public abstract class TestCacheBasicTests : TestCacheCore
    {
        protected virtual bool RequiresPinBeforeGet => true;

        /// <summary>
        /// Set this to true if the cache can test corruption of a CAS entry
        /// Should only be set to true if the below CorruptCasEntry method
        /// is implemented
        /// </summary>
        protected virtual bool CanTestCorruption => false;

        /// <summary>
        /// Implement this method to corrupt a given CAS entry in the
        /// cache.  This is used to allow testing of the corruption mitigation
        /// </summary>
        /// <param name="cache">The ICache instance to work on</param>
        /// <param name="hash">The CAS hash to corrupt</param>
        /// <returns>Completion / not throwing if you implemented it</returns>
        /// <remarks>
        /// This method needs to be implemented to corrupt a CAS entry in the
        /// cache such that corruption tests can be run.  It is not required
        /// that a cache test implement this as some caches may not be able
        /// to do corruption recovery operations.
        /// </remarks>
        protected virtual Task CorruptCasEntry(ICache cache, CasHash hash)
        {
            throw new NotImplementedException();
        }

        /// <nodoc/>
        [Fact]
        public async Task CreateEmptyCache()
        {
            const string TestName = nameof(CreateEmptyCache);
            string testCacheId = MakeCacheId(TestName);
            ICache cache = await CreateCacheAsync(testCacheId);

            XAssert.AreNotEqual(default(Guid), cache.CacheGuid);

            if (ImplementsTrackedSessions)
            {
                foreach (var sessions in cache.EnumerateCompletedSessions())
                {
                    XAssert.Fail("There should not be any sessions in this cache!");
                }
            }

            // Check that a second cache does not have the same GUID
            string testCacheId2 = testCacheId + "Two";
            ICache cache2 = await CreateCacheAsync(testCacheId2);
            XAssert.AreNotEqual(cache.CacheGuid, cache2.CacheGuid);
            await ShutdownCacheAsync(cache2, testCacheId2);

            await ShutdownCacheAsync(cache, testCacheId);
        }

        /// <nodoc/>
        [Fact]
        public async Task EmptySession()
        {
            // Empty sessions (those with no content records) should be abandoned
            const string TestName = nameof(EmptySession);
            string testCacheId = MakeCacheId(TestName);
            ICache cache = await CreateCacheAsync(testCacheId);

            string testSessionId = "Session1-" + testCacheId;
            ICacheSession session = await CreateSessionAsync(cache, testSessionId);

            await CloseSessionAsync(session, testSessionId);

            if (ImplementsTrackedSessions)
            {
                foreach (var sessions in cache.EnumerateCompletedSessions())
                {
                    XAssert.Fail("There should not be any sessions in this cache!");
                }
            }

            await ShutdownCacheAsync(cache, testCacheId);
        }

        /// <nodoc/>
        [Fact]
        public async Task UnclosedSession()
        {
            if (!ImplementsTrackedSessions)
            {
                return;
            }

            // Sessions that are not closed at cache shutdown should cause shutdown failure
            const string TestName = nameof(UnclosedSession);
            string testCacheId = MakeCacheId(TestName);
            ICache cache = await CreateCacheAsync(testCacheId);

            string testSessionId = "Session1-" + testCacheId;
            ICacheSession session = await CreateSessionAsync(cache, testSessionId);

            XAssert.IsFalse(cache.IsShutdown, "Should not be shutdown yet");

            var shutdown = await cache.ShutdownAsync();
            XAssert.IsFalse(shutdown.Succeeded, "Shutdown should have failed due to open named sessions");
            XAssert.IsTrue(cache.IsShutdown, "Should be shutdown now!");
        }

        /// <nodoc/>
        [Fact]
        public async Task DuplicateActiveSession()
        {
            if (!ImplementsTrackedSessions)
            {
                return;
            }

            // Should not be able to create two active sessions of the same name
            const string TestName = nameof(DuplicateActiveSession);
            string testCacheId = MakeCacheId(TestName);
            ICache cache = await CreateCacheAsync(testCacheId);

            string testSessionId = "Session1-" + testCacheId;
            ICacheSession session = await CreateSessionAsync(cache, testSessionId);

            var failedSession = await cache.CreateSessionAsync(testSessionId);
            XAssert.IsTrue(!failedSession.Succeeded, "Session should not have been successfully created");
            XAssert.IsTrue(failedSession.Failure is DuplicateSessionIdFailure);

            await CloseSessionAsync(session, testSessionId);

            foreach (var sessions in cache.EnumerateCompletedSessions())
            {
                XAssert.Fail("There should not be any sessions in this cache!");
            }

            await ShutdownCacheAsync(cache, testCacheId);
        }

        /// <nodoc/>
        [Theory]
        [InlineData(FileState.Writeable)]
        [InlineData(FileState.ReadOnly)]
        public async Task ProduceFileWithDirectories(FileState fileState)
        {
            const string TestName = nameof(ProduceFileWithDirectories);
            string testCacheId = MakeCacheId(TestName);
            ICache cache = await CreateCacheAsync(testCacheId);

            string testSessionId = "Session1-" + testCacheId;
            ICacheSession session = await CreateSessionAsync(cache, testSessionId);

            FakeBuild fake = new FakeBuild(TestName, 1);
            CasHash item = await session.AddToCasAsync(fake.OutputList).SuccessAsync();

            // Now, lets make a temp file name with a path that does not exist
            // We need to make sure it gets produced during ProduceFile
            string tmpFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("D"), "Test.txt");
            await session.ProduceFileAsync(item, tmpFile, fileState).SuccessAsync("Did not produce a file with a directory that did not exist!");
            Directory.Delete(Path.GetDirectoryName(tmpFile), true);

            await CloseSessionAsync(session, testSessionId);
            await ShutdownCacheAsync(cache, testCacheId);
        }

        /// <nodoc/>
        [Fact]
        public async Task GetWithoutPin()
        {
            const string TestName = nameof(GetWithoutPin);
            string testCacheId = MakeCacheId(TestName);
            ICache cache = await CreateCacheAsync(testCacheId);

            string testSessionId = "Session1-" + testCacheId;
            ICacheSession session = await CreateSessionAsync(cache, testSessionId);

            FakeBuild fake = new FakeBuild(TestName, 1);
            CasHash item = (await session.AddToCasAsync(fake.OutputList)).Success();

            // Verify that we can read the content after it was added in
            // this session since it was pinned
            using (var stream = (await session.GetStreamAsync(item)).Success())
            {
               stream.AsString();
            }

            await CloseSessionAsync(session, testSessionId);

            testSessionId = "Session2-" + testCacheId;
            session = await CreateSessionAsync(cache, testSessionId);

            if (RequiresPinBeforeGet)
            {
               // This time we will just get the content stream and it should fail
               // in this session since it is not open.
               var error = await session.GetStreamAsync(item);
               XAssert.IsTrue(!error.Succeeded, "Call to GetStream was successful, and should have failed for unpinned CAS access.");
               XAssert.IsTrue(error.Failure is UnpinnedCasEntryFailure, "Failed with unexpected error type {0} and error {1}", error.Failure, error.Failure.Describe());

               // Doing it twice did not change the case
               error = await session.GetStreamAsync(item);
               XAssert.IsTrue(!error.Succeeded, "Second call to GetStream was successful, and should have failed for unpinned CAS access.");
               XAssert.IsTrue(error.Failure is UnpinnedCasEntryFailure, "Failed with unexpected error type {0} and error {1}", error.Failure, error.Failure.Describe());
            }

            await CloseSessionAsync(session, testSessionId);

            await ShutdownCacheAsync(cache, testCacheId);
        }

        /// <nodoc/>
        [Fact]
        public async Task AddWithoutPin()
        {
            const string TestName = nameof(AddWithoutPin);
            string testCacheId = MakeCacheId(TestName);
            ICache cache = await CreateCacheAsync(testCacheId);

            string testSessionId = "Session1-" + testCacheId;
            ICacheSession session = await CreateSessionAsync(cache, testSessionId);

            FakeBuild fake = new FakeBuild(TestName, 2);

            CasHash inputList = (await session.AddToCasAsync(fake.OutputList)).Success();

            // Verify that we can read the content after it was added in
            // this session since it was pinned
            using (var stream = (await session.GetStreamAsync(inputList)).Success())
            {
                stream.AsString();
            }

            CasHash[] items = new CasHash[fake.Outputs.Length];
            for (int i = 0; i < fake.Outputs.Length; i++)
            {
                items[i] = (await session.AddToCasAsync(fake.Outputs[i])).Success();

                // Verify that we can read the content after it was added in
                // this session since it was pinned
                using (var stream = (await session.GetStreamAsync(items[i])).Success())
                {
                    stream.AsString();
                }
            }

            await CloseSessionAsync(session, testSessionId);

            testSessionId = "Session2-" + testCacheId;
            session = await CreateSessionAsync(cache, testSessionId);

            // We use the hash of our output list as the weak fingerprint and extra hash
            var outputListFingerprintHash = fake.OutputListHash.ToFingerprint();
            WeakFingerprintHash weak = new WeakFingerprintHash(outputListFingerprintHash);

            if (cache.StrictMetadataCasCoupling)
            {
                // This should fail as we did not pin or add the content yet
                var error = await session.AddOrGetAsync(weak, inputList, outputListFingerprintHash, items);
                XAssert.IsFalse(error.Succeeded, "Add of weak fingerprint when items not first added to CAS was successful.");
                XAssert.IsTrue(error.Failure is UnpinnedCasEntryFailure, "Error failure was not expected type {0}, it was {1} {2}", typeof(UnpinnedCasEntryFailure).Name, error.Failure.GetType().Name, error.Failure.Describe());

                // Doing it twice does not change things...
                error = await session.AddOrGetAsync(weak, inputList, outputListFingerprintHash, items);
                XAssert.IsFalse(error.Succeeded, "Add of weak fingerprint when items not first added to CAS was successful.");
                XAssert.IsTrue(error.Failure is UnpinnedCasEntryFailure, "Error failure was not expected type {0}, it was {1}, {2}", typeof(UnpinnedCasEntryFailure).Name, error.Failure.GetType().Name, error.Failure.Describe());
            }

            await CloseSessionAsync(session, testSessionId);

            await ShutdownCacheAsync(cache, testCacheId);
        }

        /// <nodoc/>
        [Fact]
        public async Task AddWithoutPinWeak()
        {
            const string TestName = nameof(AddWithoutPinWeak);
            string testCacheId = MakeCacheId(TestName);
            ICache cache = await CreateCacheAsync(testCacheId, strictMetadataCasCoupling: false);

            // Only caches that are not strict metadata to CAS coupling need this test
            if (cache.StrictMetadataCasCoupling)
            {
                (await cache.ShutdownAsync()).Success();
                return;
            }

            string testSessionId = "Session1-" + testCacheId;
            ICacheSession session = await CreateSessionAsync(cache, testSessionId);

            FakeBuild fake = new FakeBuild(TestName, 2);

            // We fake up a CasHash - never actually add to the cache - weak cas
            CasHash inputList = fake.OutputListHash;

            CasHash[] items = new CasHash[fake.Outputs.Length];
            for (int i = 0; i < fake.Outputs.Length; i++)
            {
                items[i] = new CasHash(fake.OutputHashes[i]);
            }

            // We use the hash of our output list as the weak fingerprint and extra hash
            var outputListFingerprintHash = fake.OutputListHash.ToFingerprint();
            WeakFingerprintHash weak = new WeakFingerprintHash(outputListFingerprintHash);

            // This should work since the session is weak.
            FullCacheRecordWithDeterminism record = (await session.AddOrGetAsync(weak, inputList, outputListFingerprintHash, items)).Success();
            XAssert.IsNull(record.Record, "There should not have been anything in the cache");

            // Doing it twice does not change things...
            record = (await session.AddOrGetAsync(weak, inputList, outputListFingerprintHash, items)).Success();
            XAssert.IsNull(record.Record, "It matches exactly, so no bother");

            await CloseSessionAsync(session, testSessionId);

            await ShutdownCacheAsync(cache, testCacheId);
        }

        /// <nodoc/>
        [Fact]
        public async Task SimpleSession()
        {
            const string TestName = nameof(SimpleSession);
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

            // Validate that there is a session and it has the one cache record it needs.
            HashSet<FullCacheRecord> found = new HashSet<FullCacheRecord>();
            if (ImplementsTrackedSessions || DummySessionName != null)
            {
                foreach (var strongFingerprintTask in cache.EnumerateSessionStrongFingerprints(DummySessionName ?? testSessionId).Success().OutOfOrderTasks())
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

                if (ImplementsTrackedSessions)
                {
                    // Check that we can not create another session with the same ID
                    var failedSession = await cache.CreateSessionAsync(testSessionId);
                    XAssert.IsTrue(failedSession.Failure is DuplicateSessionIdFailure, failedSession.Failure.Describe());
                }
            }

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

            if (ImplementsTrackedSessions || DummySessionName != null)
            {
                // Now that we have done the second build via a cache hit, it should produce the
                // same cache record as before
                foreach (var strongFingerprintTask in cache.EnumerateSessionStrongFingerprints(DummySessionName ?? testSessionId).Success().OutOfOrderTasks())
                {
                    StrongFingerprint strongFingerprint = await strongFingerprintTask;
                    CasEntries casEntries = (await readOnlySession.GetCacheEntryAsync(strongFingerprint)).Success();
                    FullCacheRecord record = new FullCacheRecord(strongFingerprint, casEntries);

                    XAssert.IsTrue(found.Contains(record), "Second session should produce the same cache record but did not!");
                }
            }

            (await readOnlySession.CloseAsync()).Success();

            await ShutdownCacheAsync(cache, testCacheId);
        }

        /// <nodoc/>
        [Fact]
        public async Task NoItemFingerprint()
        {
            const string TestName = nameof(NoItemFingerprint);
            string testCacheId = MakeCacheId(TestName);
            ICache cache = await CreateCacheAsync(testCacheId);

            // Now for the session (which we base on the cache ID)
            string testSessionId = "Session1-" + testCacheId;

            ICacheSession session = await CreateSessionAsync(cache, testSessionId);

            // Note that we will be making a new fingerprint with a CasHash of NoItem
            // without pre-sending it as NoItem is a special case - it is nothing
            FullCacheRecord record = await FakeBuild.DoPipAsync(session, TestName);

            // We place this in and did not pin the NoItem yet or even send it around
            // Note that this also is doing a zero-length CasEntries
            var strong = new StrongFingerprint(record.StrongFingerprint.WeakFingerprint, CasHash.NoItem, new Hash(FingerprintUtilities.ZeroFingerprint), TestName);
            FullCacheRecordWithDeterminism oldRecord = (await session.AddOrGetAsync(
                strong.WeakFingerprint,
                                                                     strong.CasElement,
                                                                     strong.HashElement,
                                                                     CasEntries.FromCasHashes())).Success("Should work even though I did not pin CasHash.NoItem, instead it failed with {0}");

            XAssert.IsNull(oldRecord.Record, "Should have been the first one like this");

            var result = await session.GetCacheEntryAsync(strong).SuccessAsync();
            XAssert.AreEqual(0, result.Count, "We should have gotten a zero-length CasEntries");

            // We place this in and did not pin the NoItem yet or even send it around
            // Note that this does an array of NoItem CasEntries and use the
            // record.CasElement as the weak fingerprint
            CasHash[] empties = { CasHash.NoItem, CasHash.NoItem, CasHash.NoItem };
            strong = new StrongFingerprint(new WeakFingerprintHash(strong.CasElement.ToFingerprint()), CasHash.NoItem, new Hash(FingerprintUtilities.ZeroFingerprint), TestName);
            oldRecord = (await session.AddOrGetAsync(
                strong.WeakFingerprint,
                                                     CasHash.NoItem,
                                                     new Hash(FingerprintUtilities.ZeroFingerprint),
                                                     empties)).Success("Should work even though I did not pin CasHash.NoItem, instead it failed with {0}");

            XAssert.IsNull(oldRecord.Record, "Should have been the first one like this");

            result = await session.GetCacheEntryAsync(strong).SuccessAsync();
            XAssert.AreEqual(empties, result, "We should have gotten the set of empties");

            await CloseSessionAsync(session, testSessionId);

            await ShutdownCacheAsync(cache, testCacheId);
        }

        [Fact]
        public async Task CacheMissTest()
        {
            const string TestName = nameof(CacheMissTest);
            string testCacheId = MakeCacheId(TestName);
            ICache cache = await CreateCacheAsync(testCacheId);

            ICacheSession session = await cache.CreateSessionAsync().SuccessAsync();

            FakeBuild fb = new FakeBuild("test", 3);

            StrongFingerprint fakeFingerprint = new StrongFingerprint(WeakFingerprintHash.Random(), new CasHash(fb.OutputHashes[0]), fb.OutputHashes[0], "fake");

            var response = await session.GetCacheEntryAsync(fakeFingerprint);
            XAssert.IsFalse(response.Succeeded);
            XAssert.AreEqual(typeof(NoMatchingFingerprintFailure), response.Failure.GetType(), response.Failure.Describe());

            await CloseSessionAsync(session, null);
            await ShutdownCacheAsync(cache, testCacheId);
        }

        [Fact]
        public async Task PinErrors()
        {
            const string TestName = nameof(PinErrors);
            string testCacheId = MakeCacheId(TestName);
            ICache cache = await CreateCacheAsync(testCacheId);

            ICacheSession session = await cache.CreateSessionAsync().SuccessAsync();

            FakeBuild fb = new FakeBuild("test", 3);

            var result = await session.PinToCasAsync(new CasHash(fb.OutputHashes[0]));
            XAssert.IsFalse(result.Succeeded, "Pin should fail");
            XAssert.AreEqual(typeof(NoCasEntryFailure), result.Failure.GetType(), "Incorrect failure returned {0}", result.Failure.Describe());

            await CloseSessionAsync(session, null);
            await ShutdownCacheAsync(cache, testCacheId);
        }

        /// <nodoc/>
        [Theory]
        [InlineData(FakeBuild.CasAccessMethod.Stream)]
        [InlineData(FakeBuild.CasAccessMethod.FileSystem)]
        public Task MultiplePips_DifferentWeak(FakeBuild.CasAccessMethod accessMethod)
        {
                PipDefinition[] pips =
                {
                new PipDefinition("PipA"),
                new PipDefinition("PipB"),
                new PipDefinition("PipC")
            };

                // We will do multiple unique pips and validate that they all ran
                // These will all have different weak fingerprints
                const string TestName = nameof(MultiplePips_DifferentWeak);
                return TestMultiplePipsAsync(TestName, pips, accessMethod);
        }

        /// <nodoc/>
        [Theory]
        [InlineData(FakeBuild.CasAccessMethod.Stream)]
        [InlineData(FakeBuild.CasAccessMethod.FileSystem)]
        public Task MultiplePips_DifferentCas(FakeBuild.CasAccessMethod accessMethod)
        {
            // Different CasHash but same weak fingerprint and hash
            PipDefinition[] pips =
            {
                new PipDefinition("PipSameHash", pipSize: 3),
                new PipDefinition("PipSameHash", pipSize: 4),
                new PipDefinition("PipSameHash", pipSize: 5)
            };

            const string TestName = nameof(MultiplePips_DifferentCas);
            return TestMultiplePipsAsync(TestName, pips, accessMethod);
        }

        /// <nodoc/>
        [Theory]
        [InlineData(FakeBuild.CasAccessMethod.Stream)]
        [InlineData(FakeBuild.CasAccessMethod.FileSystem)]
        public Task MultiplePips_DifferentHash(FakeBuild.CasAccessMethod accessMethod)
        {
            // Different hash but same weak fingerprint and CasHash
            PipDefinition[] pips =
            {
                new PipDefinition("PipSameCas", pipSize: 5, hashIndex: 2),
                new PipDefinition("PipSameCas", pipSize: 5, hashIndex: 3),
                new PipDefinition("PipSameCas", pipSize: 5, hashIndex: 4)
            };

            const string TestName = nameof(MultiplePips_DifferentHash);
            return TestMultiplePipsAsync(TestName, pips, accessMethod);
        }

        /// <nodoc/>
        [Theory]
        [InlineData(FakeBuild.CasAccessMethod.Stream)]
        [InlineData(FakeBuild.CasAccessMethod.FileSystem)]
        public Task MultiplePips_Mixed(FakeBuild.CasAccessMethod accessMethod)
        {
            // Various different pips
            PipDefinition[] pips =
            {
                new PipDefinition("VariousA"),
                new PipDefinition("VariousSameHash", pipSize: 3),
                new PipDefinition("VariousSameCas", pipSize: 5, hashIndex: 2),
                new PipDefinition("VariousB"),
                new PipDefinition("VariousSameHash", pipSize: 4),
                new PipDefinition("VariousSameCas", pipSize: 5, hashIndex: 3),
                new PipDefinition("VariousC"),
                new PipDefinition("VariousSameHash", pipSize: 5),
                new PipDefinition("VariousSameCas", pipSize: 5, hashIndex: 4)
            };

            const string TestName = nameof(MultiplePips_Mixed);
            return TestMultiplePipsAsync(TestName, pips, accessMethod);
        }

        /// <nodoc/>
        [Theory]
        [InlineData(FakeBuild.CasAccessMethod.Stream)]
        [InlineData(FakeBuild.CasAccessMethod.FileSystem)]
        public Task DeterministicTool(FakeBuild.CasAccessMethod accessMethod)
        {
            PipDefinition[] pips =
            {
                new PipDefinition("PipA", determinism: CacheDeterminism.Tool),
                new PipDefinition("PipB", determinism: CacheDeterminism.Tool),
                new PipDefinition("PipC")
            };

            // We will do multiple unique pips and validate that they all ran
            // These will all have different weak fingerprints
            const string TestName = nameof(DeterministicTool);
            return TestMultiplePipsAsync(TestName, pips, accessMethod);
        }

        /// <nodoc/>
        [Fact]
        public async Task WriteableFilesAreWriteable()
        {
            const string TestName = nameof(WriteableFilesAreWriteable);
            string testCacheId = MakeCacheId(TestName);
            ICache cache = await CreateCacheAsync(testCacheId);

            ICacheSession session = await cache.CreateSessionAsync().SuccessAsync();

            string origionalFileContents = "foo";
            CasHash hash;

            string filePath = Path.GetTempFileName();
            try
            {
                using (StreamWriter sw = new StreamWriter(filePath))
                {
                    await sw.WriteAsync(origionalFileContents);
                    await sw.FlushAsync();
                    sw.Close();
                }

                hash = await session.AddToCasAsync(filePath, FileState.Writeable).SuccessAsync();

                // Great, so it added.
                using (StreamWriter sw = new StreamWriter(filePath, true))
                {
                    await sw.WriteLineAsync("Bar");
                }

                // Get the origional file and read it to ensure it didn't change.
                StreamReader sr = new StreamReader(await session.GetStreamAsync(hash).SuccessAsync());

                string cacheContents = await sr.ReadToEndAsync();

                XAssert.AreEqual(origionalFileContents, cacheContents, "File content in the cache should not have been modified by a write to a file added as writeable.");
            }
            finally
            {
                File.Delete(filePath);
            }

            // Now, materialize the file and ensure we can write to it.
            filePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

            string placedPath = await session.ProduceFileAsync(hash, filePath, FileState.Writeable).SuccessAsync();
            try
            {
                XAssert.AreEqual(filePath, placedPath, "File should be placed where asked...");

                using (StreamWriter sw = new StreamWriter(filePath, true))
                {
                    await sw.WriteLineAsync("Bar");
                }
            }
            finally
            {
                File.Delete(placedPath);
            }

            await session.CloseAsync().SuccessAsync();

            await ShutdownCacheAsync(cache, testCacheId);
        }

        /// <nodoc/>
        [Fact]
        public async Task CorruptionRecovery()
        {
            const string TestName = nameof(CorruptionRecovery);
            string testCacheId = MakeCacheId(TestName);
            ICache cache = await CreateCacheAsync(testCacheId);

            string testSessionId = "Session1-" + testCacheId;
            ICacheSession session = await CreateSessionAsync(cache, testSessionId);

            // Use the testname to generate a CAS items.
            CasHash item = (await session.AddToCasAsync(TestName.AsStream())).Success();

            // Verify that we can read the content after it was added in
            // this session since it was pinned
            using (var stream = (await session.GetStreamAsync(item)).Success())
            {
                XAssert.AreEqual(TestName, stream.AsString(), "Failed to read back matching content from cache");
            }

            ValidateContentStatus goodStatus = (await session.ValidateContentAsync(item)).Success();

            // We can have implemented ValidateContent and not have a way to test corruption but
            // we must have implemented ValidateCotent if we have a way to test corruption
            if (CanTestCorruption || (goodStatus != ValidateContentStatus.NotSupported))
            {
                // We should have returned Ok since the content was not corrupted
                XAssert.AreEqual(ValidateContentStatus.Ok, goodStatus, "Content should have matched in hash at this point!");

                // NoItem should always be valid
                XAssert.AreEqual(ValidateContentStatus.Ok, (await session.ValidateContentAsync(CasHash.NoItem)).Success(), "NoItem should always be valid!");

                // Now, only if we can test corruption (which requires that we can corrupt an item)
                // do we go down this next path
                if (CanTestCorruption)
                {
                    await CorruptCasEntry(cache, item);

                    using (var stream = (await session.GetStreamAsync(item)).Success())
                    {
                        XAssert.AreNotEqual(TestName, stream.AsString(), "Failed to corrupt CAS entry!");
                    }

                    ValidateContentStatus corruptedStatus = (await session.ValidateContentAsync(item)).Success();

                    // At this point, caches can do a number of possible things
                    // They can not return OK or NotImplemented (since we already checked that earlier)
                    XAssert.AreNotEqual(ValidateContentStatus.Ok, corruptedStatus, "The item was corrupted - something should have happened");
                    XAssert.AreNotEqual(ValidateContentStatus.NotSupported, corruptedStatus, "It was implemented a moment earlier");
                }
            }

            await CloseSessionAsync(session, testSessionId);

            await ShutdownCacheAsync(cache, testCacheId);
        }

        /// <nodoc/>
        [Fact]
        public async Task Deterministic()
        {
            const string TestName = nameof(Deterministic);
            string testCacheId = MakeCacheId(TestName);
            ICache cache = await CreateCacheAsync(testCacheId);

            ICacheSession session = await cache.CreateSessionAsync().SuccessAsync();

            PipDefinition[] pips =
            {
                new PipDefinition("PipA"),
                new PipDefinition("PipB")
            };

            FullCacheRecord[] records = (await pips.BuildAsync(session)).ToArray();
            XAssert.AreEqual(2, records.Length);

            // Now, we should be able to redo those adds but swap the CasEntries in the records
            // and get them replaced
            for (int i = 0; i < 2; i++)
            {
                // Make sure we have our own strong fingerprint (no cheating by the cache for this one)
                var strong = records[i].StrongFingerprint;
                strong = new StrongFingerprint(strong.WeakFingerprint, strong.CasElement, strong.HashElement, "testing");

                // Validate that the GetCacheEntry produces what we expect
                var entries = await session.GetCacheEntryAsync(strong).SuccessAsync();
                XAssert.AreEqual(records[i].CasEntries, entries);

                // Validate that the other record I am going to do is different
                var other = records[1 - i].CasEntries;
                XAssert.AreNotEqual(entries, other, "Other entries must be different!");

                var newRecord = await session.AddOrGetAsync(strong.WeakFingerprint, strong.CasElement, strong.HashElement, other).SuccessAsync();
                XAssert.IsNotNull(newRecord.Record, "Should have returned prior version");
                XAssert.AreEqual(records[i], newRecord.Record);

                var fail = await session.AddOrGetAsync(strong.WeakFingerprint, strong.CasElement, strong.HashElement, new CasEntries(other, CacheDeterminism.SinglePhaseNonDeterministic));
                XAssert.IsFalse(fail.Succeeded, "Should not have succeeded in replacing normal with SinglePhaseNonDeterministic");
                XAssert.AreEqual(typeof(SinglePhaseMixingFailure), fail.Failure.GetType(), fail.Failure.Describe());

                // Should not matter if the CasEntries are the same or not
                fail = await session.AddOrGetAsync(strong.WeakFingerprint, strong.CasElement, strong.HashElement, new CasEntries(entries, CacheDeterminism.SinglePhaseNonDeterministic));
                XAssert.IsFalse(fail.Succeeded, "Should not have succeeded in replacing normal with SinglePhaseNonDeterministic");
                XAssert.AreEqual(typeof(SinglePhaseMixingFailure), fail.Failure.GetType(), fail.Failure.Describe());
            }

            // Now for tool deterministic rules
            PipDefinition[] toolPips =
            {
                new PipDefinition("ToolPipA", determinism: CacheDeterminism.Tool),
                new PipDefinition("ToolPipB", determinism: CacheDeterminism.Tool)
            };

            records = (await toolPips.BuildAsync(session)).ToArray();
            XAssert.AreEqual(2, records.Length);

            // Now, we should not be able to change tool deterministic results as that is
            // a major error (not just a failure to add with prior record returned)
            for (int i = 0; i < 2; i++)
            {
                // Make sure we have our own strong fingerprint (no cheating by the cache for this one)
                var strong = records[i].StrongFingerprint;
                strong = new StrongFingerprint(strong.WeakFingerprint, strong.CasElement, strong.HashElement, "testing");

                // Validate that the GetCacheEntry produces what we expect
                var entries = await session.GetCacheEntryAsync(strong).SuccessAsync();
                XAssert.AreEqual(records[i].CasEntries, entries);

                // Validate that the other record I am going to do is different
                var other = records[1 - i].CasEntries;
                XAssert.AreNotEqual(entries, other, "Other entries must be different!");

                // Setting our own should be just fine (same content with tool determinism)
                var newRecord = await session.AddOrGetAsync(strong.WeakFingerprint, strong.CasElement, strong.HashElement, entries).SuccessAsync();
                XAssert.IsNull(newRecord.Record);

                // A non-tool deteriminism update should have returned the prior version
                newRecord = await session.AddOrGetAsync(strong.WeakFingerprint, strong.CasElement, strong.HashElement, new CasEntries(other, CacheDeterminism.None)).SuccessAsync();
                XAssert.AreEqual(records[i], newRecord.Record);

                // Giving a tool deterministic different answer should fail
                var fail = await session.AddOrGetAsync(strong.WeakFingerprint, strong.CasElement, strong.HashElement, other);
                XAssert.IsFalse(fail.Succeeded, "Should have failed when trying to give conflicting tool deterministic entries");
                XAssert.AreEqual(typeof(NotDeterministicFailure), fail.Failure.GetType(), fail.Failure.Describe());
            }

            await session.CloseAsync().SuccessAsync();
            await ShutdownCacheAsync(cache, testCacheId);
        }

        /// <summary>
        /// Basic test of the SinglePhaseNonDeterministic case
        /// </summary>
        /// <returns>async task only - no value</returns>
        /// <remarks>
        /// SinglePhaseNonDeterministic is a special case were
        /// these entries always overwrite and there is no
        /// assurance of determinism (compatible with old
        /// semantic behavior of the single-phase lookup cache)
        /// </remarks>
        [Fact]
        public async Task SinglePhaseNonDeterministic()
        {
            const string TestName = nameof(SinglePhaseNonDeterministic);
            string testCacheId = MakeCacheId(TestName);
            ICache cache = await CreateCacheAsync(testCacheId);

            // Now for the session (which we base on the cache ID)
            string testSessionId = "Session1-" + testCacheId;
            ICacheSession session = await CreateSessionAsync(cache, testSessionId);

            PipDefinition[] pips =
            {
                new PipDefinition("PipA", determinism: CacheDeterminism.SinglePhaseNonDeterministic),
                new PipDefinition("PipB", determinism: CacheDeterminism.SinglePhaseNonDeterministic)
            };

            FullCacheRecord[] records = (await pips.BuildAsync(session)).ToArray();
            XAssert.AreEqual(2, records.Length);

            // Now, we should be able to redo those adds but swap the CasEntries in the records
            // and get them replaced
            for (int i = 0; i < 2; i++)
            {
                // Make sure we have our own strong fingerprint (no cheating by the cache for this one)
                var strong = records[i].StrongFingerprint;
                strong = new StrongFingerprint(strong.WeakFingerprint, strong.CasElement, strong.HashElement, "testing");

                // Validate that the GetCacheEntry produces what we expect
                var entries = await session.GetCacheEntryAsync(strong).SuccessAsync();
                XAssert.AreEqual(records[i].CasEntries, entries);

                // Validate that the other record I am going to do is different
                var other = records[1 - i].CasEntries;
                XAssert.AreNotEqual(entries, other, "Other record must be different!");

                var newRecord = await session.AddOrGetAsync(strong.WeakFingerprint, strong.CasElement, strong.HashElement, other).SuccessAsync();
                XAssert.IsNull(newRecord.Record, "Should have replaced");

                entries = await session.GetCacheEntryAsync(strong).SuccessAsync();
                XAssert.AreEqual(other, entries);
                XAssert.AreNotEqual(records[i].CasEntries, entries);

                // Try to replace the existing SinglePhaseNonDeterministic case with a recird that is deterministic with a cache
                var fail = await session.AddOrGetAsync(strong.WeakFingerprint, strong.CasElement, strong.HashElement, new CasEntries(records[i].CasEntries, CacheDeterminism.ViaCache(CacheDeterminism.NewCacheGuid(), CacheDeterminism.NeverExpires)));
                XAssert.IsFalse(fail.Succeeded, "Should have failed trying to replace SinglePhaseNonDeterministic with cache deterministic");
                XAssert.AreEqual(typeof(SinglePhaseMixingFailure), fail.Failure.GetType());

                // Try to replace the existing SinglePhaseNonDeterministic case with a recird that is deterministic tool
                fail = await session.AddOrGetAsync(strong.WeakFingerprint, strong.CasElement, strong.HashElement, new CasEntries(records[i].CasEntries, CacheDeterminism.Tool));
                XAssert.IsFalse(fail.Succeeded, "Should have failed trying to replace SinglePhaseNonDeterministic with tool deterministic");
                XAssert.AreEqual(typeof(SinglePhaseMixingFailure), fail.Failure.GetType());

                // Try to replace the existing SinglePhaseNonDeterministic case with a recird that is not deterministic
                fail = await session.AddOrGetAsync(strong.WeakFingerprint, strong.CasElement, strong.HashElement, new CasEntries(records[i].CasEntries, CacheDeterminism.None));
                XAssert.IsFalse(fail.Succeeded, "Should have failed trying to replace SinglePhaseNonDeterministic with non-deterministic");
                XAssert.AreEqual(typeof(SinglePhaseMixingFailure), fail.Failure.GetType());
            }

            await CloseSessionAsync(session, testSessionId);
            await ShutdownCacheAsync(cache, testCacheId);
        }

        [Fact]
        public async Task CanRegisterForNotifications()
        {
            const string TestName = nameof(CanRegisterForNotifications);
            string testCacheId = MakeCacheId(TestName);
            ICache cache = await CreateCacheAsync(testCacheId);

            try
            {
                cache.SuscribeForCacheStateDegredationFailures((failure) => { Console.WriteLine(failure.Describe()); });
            }
            finally
            {
                await ShutdownCacheAsync(cache, testCacheId);
            }
        }
    }
}
