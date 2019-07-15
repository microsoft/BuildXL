// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Tracing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Cache.BasicFilesystem;
using BuildXL.Cache.Interfaces;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.Cache.Tests
{
    [ExcludeFromCodeCoverage]
    public class TestBasicFilesystemGc : TestCacheCore
    {
        // Local TextWriter wrapper class over the ITestOutputHelper that XUnit provides
        private class MyTextOutput : TextWriter
        {
            private readonly ITestOutputHelper m_output;

            public MyTextOutput(ITestOutputHelper output)
            {
                m_output = output;
            }

            public override Encoding Encoding => Encoding.ASCII;

            public override void WriteLine(string data)
            {
                m_output.WriteLine(data);
            }

            public void LogStats(Dictionary<string, double> stats)
            {
                foreach (var kv in stats.OrderBy(v => v.Key))
                {
                    m_output.WriteLine("{0} = {1}", kv.Key, kv.Value);
                }
            }
        }

        protected override IEnumerable<EventSource> EventSources => new EventSource[0];

        public override string NewCache(string cacheId, bool strictMetadataCasCoupling, bool authoritative = false)
        {
            throw new NotImplementedException();
        }

        private readonly MyTextOutput m_output;

        public TestBasicFilesystemGc(ITestOutputHelper output)
        {
            m_output = new MyTextOutput(output);
            TestType = new TestBasicFilesystem();
        }

        protected virtual TestCacheCore TestType { get; }

        // Local helper class to collect/store the files that are
        // in the cache.
        private class CacheEntries
        {
            public readonly Dictionary<string, FileInfo> FingerprintFiles = new Dictionary<string, FileInfo>();
            public readonly Dictionary<string, FileInfo> CasFiles = new Dictionary<string, FileInfo>();

            private static IEnumerable<FileInfo> EnumerateFiles(string path)
            {
                DirectoryInfo dir = new DirectoryInfo(path);
                if (dir.Exists)
                {
                    foreach (FileInfo file in dir.EnumerateFiles("*", SearchOption.AllDirectories))
                    {
                        yield return file;
                    }
                }
            }

            public CacheEntries(BasicFilesystemCache cache)
            {
                foreach (string oneFingerprintRoot in cache.FingerprintRoots.Distinct())
                {
                    foreach (FileInfo file in EnumerateFiles(oneFingerprintRoot))
                    {
                        FingerprintFiles[file.FullName] = file;
                    }
                }

                foreach (string onCasRoot in cache.CasRoots.Distinct())
                {
                    foreach (FileInfo file in EnumerateFiles(onCasRoot))
                    {
                        CasFiles[file.FullName] = file;
                    }
                }
            }

            private IEnumerable<FileInfo> All()
            {
                foreach (FileInfo file in FingerprintFiles.Values)
                {
                    yield return file;
                }

                foreach (FileInfo file in CasFiles.Values)
                {
                    yield return file;
                }
            }

            public int Count => FingerprintFiles.Count + CasFiles.Count;

            public void AgeAll()
            {
                DateTime target = DateTime.UtcNow.AddMonths(-1);
                foreach (FileInfo file in All())
                {
                    file.LastWriteTimeUtc = target;
                }
            }

            public void AssertExists()
            {
                foreach (FileInfo file in All())
                {
                    XAssert.IsTrue(file.Exists, "Missing {0}", file.FullName);
                }
            }

            public void AssertMissingSome()
            {
                foreach (FileInfo file in All())
                {
                    // Any missing file will be a miss (and good)
                    if (!file.Exists)
                    {
                        return;
                    }
                }

                XAssert.Fail("Not missing any files!");
            }

            // Returns true if there are differences
            public bool AreDifferences(CacheEntries other)
            {
                if (other.FingerprintFiles.Count != FingerprintFiles.Count)
                {
                    return true;
                }

                if (other.CasFiles.Count != CasFiles.Count)
                {
                    return true;
                }

                foreach (string name in FingerprintFiles.Keys)
                {
                    if (!other.FingerprintFiles.ContainsKey(name))
                    {
                        return true;
                    }
                }

                foreach (string name in CasFiles.Keys)
                {
                    if (!other.CasFiles.ContainsKey(name))
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        private async Task BuildPips(BasicFilesystemCache cache, string sessionName, PipDefinition[] pips)
        {
            ICacheSession session = await cache.CreateSessionAsync(sessionName).SuccessAsync();

            await pips.BuildAsync(session);

            AssertSuccess(await session.CloseAsync());
        }

        private async Task<CacheEntries> BuildPipsAndGetCacheEntries(BasicFilesystemCache cache, string sessionName, PipDefinition[] pips)
        {
            await BuildPips(cache, sessionName, pips);

            return new CacheEntries(cache);
        }

        private Dictionary<string, double> FullGc(BasicFilesystemCache cache, string action = "FullGc")
        {
            Dictionary<string, double> statsFp = cache.CollectUnreferencedFingerprints(m_output).Success("{0}:CollectUnreferencedFingerprints\n{1}", action);
            Dictionary<string, double> statsCas = cache.CollectUnreferencedCasItems(m_output).Success("{0}:CollectUnreferencedCasItems\n{1}", action);

            foreach (var kv in statsCas)
            {
                statsFp.Add(kv.Key, kv.Value);
            }

            m_output.LogStats(statsFp);

            return statsFp;
        }

        private IEnumerable<Task> DoBuilds(BasicFilesystemCache cache, int number)
        {
            for (int i = 0; i < number; i++)
            {
                // We do builds with one PIP the same and one PIP unique in each
                PipDefinition[] pips =
                {
                    new PipDefinition("Pip"),
                    new PipDefinition("Pip" + i)
                };

                yield return BuildPips(cache, "Build" + i, pips);
            }
        }

        private bool IsPendingDelete(string filename)
        {
            return BasicFilesystemCache.IsPendingDelete(filename);
        }

        private bool IsNotPendingDelete(string filename)
        {
            return !IsPendingDelete(filename);
        }

        [Fact]
        public async Task TestGcBasic()
        {
            string cacheConfig = TestType.NewCache(nameof(TestGcBasic), true);
            BasicFilesystemCache cache = (await InitializeCacheAsync(cacheConfig).SuccessAsync()) as BasicFilesystemCache;
            XAssert.IsNotNull(cache, "Failed to create cache for GC tests!");

            // Verify that we don't have prior content in the cache
            XAssert.AreEqual(0, new CacheEntries(cache).Count, "Test cache did not start out empty!");

            PipDefinition[] pipsSession1 =
            {
                new PipDefinition("Pip1", pipSize: 3),
                new PipDefinition("Pip2", pipSize: 4),
                new PipDefinition("Pip3", pipSize: 5)
            };

            CacheEntries session1Files = await BuildPipsAndGetCacheEntries(cache, "Session1", pipsSession1);

            // Nothing should change on this GC since the files and are all referenced
            FullGc(cache);
            session1Files.AssertExists();

            session1Files.AgeAll();

            // Everything is rooted so nothing should happen here
            FullGc(cache);

            XAssert.IsFalse(new CacheEntries(cache).AreDifferences(session1Files), "We changed the cache when we should not have!");

            // Now, if we delete the session, we should collect things.
            cache.DeleteSession("Session1");
            FullGc(cache);

            // All of the fingerprints should be changed to pending but nothing should be deleted
            CacheEntries session1gcFpPending = new CacheEntries(cache);
            XAssert.AreEqual(session1Files.Count, session1gcFpPending.Count, "Nothing should have been added or deleted!");
            XAssert.IsFalse(session1gcFpPending.FingerprintFiles.Keys.Any(IsNotPendingDelete), "All fingerprints should be pending delete");
            XAssert.IsFalse(session1gcFpPending.CasFiles.Keys.Any(IsPendingDelete), "All cas should not be pending delete");

            // Nothing to happen here as the pending files are too new
            FullGc(cache);

            // Nothing changed...
            XAssert.IsFalse(new CacheEntries(cache).AreDifferences(session1gcFpPending), "We changed the cache when we should not have!");

            // Now age the pending delete such that they are collected
            session1gcFpPending.AgeAll();

            FullGc(cache);

            CacheEntries session1gcCas1 = new CacheEntries(cache);

            XAssert.AreEqual(0, session1gcCas1.FingerprintFiles.Count, "Should have collected all fingerprints");

            // And, we should have moved to pending all CAS items (since there is no pending)
            XAssert.IsFalse(session1gcCas1.CasFiles.Keys.Any(IsNotPendingDelete), "All cas should be pending delete");

            FullGc(cache); // Should do nothing as they are not old enough pending
            XAssert.IsFalse(new CacheEntries(cache).AreDifferences(session1gcCas1), "We changed the cache when we should not have!");

            // After getting all to be old, this should finally GC it all
            session1gcCas1.AgeAll();
            FullGc(cache);

            XAssert.AreEqual(0, new CacheEntries(cache).Count, "Should have collected everything.");

            AssertSuccess(await cache.ShutdownAsync());
        }

        [Fact]
        public async Task TestGcMultiSession()
        {
            string cacheConfig = TestType.NewCache(nameof(TestGcMultiSession), true);
            BasicFilesystemCache cache = (await InitializeCacheAsync(cacheConfig).SuccessAsync()) as BasicFilesystemCache;
            XAssert.IsNotNull(cache, "Failed to create cache for GC tests!");

            // Verify that we don't have prior content in the cache
            XAssert.AreEqual(0, new CacheEntries(cache).Count, "Test cache did not start out empty!");

            // Also used for session 3
            PipDefinition[] pipsSession1 =
            {
                new PipDefinition("Pip1", pipSize: 3),
                new PipDefinition("Pip2", pipSize: 4),
                new PipDefinition("Pip3", pipSize: 5)
            };

            // This should just bring back from the "pending" one pip with 4 outputs
            // Also used for session 4
            PipDefinition[] pipsSession2 =
            {
                new PipDefinition("Pip2", pipSize: 4)
            };

            CacheEntries session1Files = await BuildPipsAndGetCacheEntries(cache, "Session1", pipsSession1);
            CacheEntries session2Files = await BuildPipsAndGetCacheEntries(cache, "Session2", pipsSession2);

            // The second session should not have changed anything as the pip already existed
            XAssert.IsFalse(session2Files.AreDifferences(session1Files), "We changed the cache when we should not have!");

            // Nothing should change on this GC since the files are all referenced
            session1Files.AgeAll();
            FullGc(cache);
            session1Files.AssertExists();

            // Nothing should change because of session 2 deleting since the fingerprint still exists in session1
            cache.DeleteSession("Session2");
            FullGc(cache);
            session1Files.AssertExists();

            // Deleteing session 1 should cause all fingerprints to become pending delete
            cache.DeleteSession("Session1");
            FullGc(cache);
            CacheEntries session1GcFpPending = new CacheEntries(cache);
            XAssert.IsFalse(session1GcFpPending.FingerprintFiles.Keys.Any(IsNotPendingDelete), "All fingerprints should be pending delete");
            XAssert.IsFalse(session1GcFpPending.CasFiles.Keys.Any(IsPendingDelete), "All cas should not be pending delete");

            // Rebuilding the pips from session1a should restore the pending delete to non-pending delete (in fact, restore to session1Files)
            CacheEntries session3Files = await BuildPipsAndGetCacheEntries(cache, "Session3", pipsSession1);
            XAssert.IsFalse(session3Files.AreDifferences(session1Files), "Should be back to the same after rebuilding - no pending");

            cache.DeleteSession("Session3");
            session3Files.AgeAll();
            FullGc(cache);

            CacheEntries session3GcFpPending = new CacheEntries(cache);
            XAssert.IsFalse(session3GcFpPending.FingerprintFiles.Keys.Any(IsNotPendingDelete), "All fingerprints should be pending delete");
            XAssert.IsFalse(session3GcFpPending.CasFiles.Keys.Any(IsPendingDelete), "All cas should not be pending delete");

            // Build the session2 single pip (as session 4) to recover the pending
            CacheEntries session4Files = await BuildPipsAndGetCacheEntries(cache, "Session4", pipsSession2);
            XAssert.AreEqual(session1Files.Count, session4Files.Count, "Should not have made any extra files");
            XAssert.AreEqual(1, session4Files.FingerprintFiles.Keys.Count(IsNotPendingDelete), "Should have 1 non-pending delete fingerprint");

            // This should collect all but the one fingerprint from session 4 and mark pending all of the cas entries
            // except the 5 cas entries from session 4.  (4 cas outputs plus the cas input list in the fingerprint)
            session4Files.AgeAll();
            FullGc(cache);
            CacheEntries session4Gc = new CacheEntries(cache);
            XAssert.AreEqual(1, session4Gc.FingerprintFiles.Count, "Should only have one fingerprint file left");
            XAssert.AreEqual(session1Files.CasFiles.Count, session4Gc.CasFiles.Count);
            XAssert.AreEqual(5, session4Gc.CasFiles.Keys.Count(IsNotPendingDelete), "Only Pip2 cas should be non-pending");

            cache.DeleteSession("Session4");

            // Pip2 fingerprint to pending
            session4Gc.AgeAll();
            FullGc(cache);

            // Pip2 fingerprint from pending to delete - cas entries to pending
            new CacheEntries(cache).AgeAll();
            FullGc(cache);
            CacheEntries session4GcCasPending = new CacheEntries(cache);
            XAssert.AreEqual(0, session4GcCasPending.FingerprintFiles.Count, "All fingerprints should be gone");
            XAssert.IsFalse(session4GcCasPending.CasFiles.Keys.Any(IsNotPendingDelete), "All cas should be pending delete");

            // Pip2 cas entries from pending to delete
            session4GcCasPending.AgeAll();
            FullGc(cache);
            XAssert.AreEqual(0, new CacheEntries(cache).Count, "All should be collected now");

            AssertSuccess(await cache.ShutdownAsync());
        }

        [Fact(Skip = "Flaky test")]
        public async Task TestGcMulti()
        {
            // To keep test time down, we do all of the "safely skipped during GC" tests in one go
            // This is actually safe as I then validate that it recovers later.  Under normal conditions
            // this rarely happens but we want to make sure that the GC can handle the concurrent access
            // characteristics and this hits those other code paths.
            string cacheConfig = TestType.NewCache(nameof(TestGcMulti), true);
            BasicFilesystemCache cache = (await InitializeCacheAsync(cacheConfig).SuccessAsync()) as BasicFilesystemCache;
            XAssert.IsNotNull(cache, "Failed to create cache for GC tests!");

            foreach (var build in DoBuilds(cache, 4).OutOfOrderTasks())
            {
                await build;
            }

            // Now delete the sessions one at a time and age at each step
            for (int i = 0; i < 4; i++)
            {
                new CacheEntries(cache).AgeAll();
                FullGc(cache);
                cache.DeleteSession("Build" + i);
            }

            // At this point we should have deleted a few items at each level
            // The last session should still have been alive at the last GC
            // so we should now have both pending and non-pending in both the
            // fingerprints and CAS
            CacheEntries files = new CacheEntries(cache);

            XAssert.IsTrue(files.FingerprintFiles.Keys.Any(IsPendingDelete));
            XAssert.IsTrue(files.FingerprintFiles.Keys.Any(IsNotPendingDelete));
            XAssert.IsTrue(files.CasFiles.Keys.Any(IsPendingDelete));
            XAssert.IsTrue(files.CasFiles.Keys.Any(IsNotPendingDelete));

            // Age them all
            files.AgeAll();

            // Now, we need to open some files of each time and run a GC to see that it works.
            using (var notPendingFingerprint = files.FingerprintFiles.First(kv => IsNotPendingDelete(kv.Key)).Value.OpenRead())
            {
                using (var pendingFingerprint = files.FingerprintFiles.First(kv => IsPendingDelete(kv.Key)).Value.OpenRead())
                {
                    using (var notPendingCas = files.CasFiles.First(kv => IsNotPendingDelete(kv.Key)).Value.OpenRead())
                    {
                        using (var pendingCas = files.CasFiles.First(kv => IsPendingDelete(kv.Key)).Value.OpenRead())
                        {
                            // This should allow the GC to continue but some things will not be collected
                            // to do failed deletes and failed renames to pending
                            var stats = FullGc(cache);

                            XAssert.AreEqual(1, stats["CAS_Skipped"], "Should have skipped a CAS entry");
                            XAssert.AreEqual(2, stats["Fingerprint_Skipped"], "Should have skipped 2 Fingerprints");
                        }
                    }
                }
            }

            // Now with the files not blocked from delete/rename, run the GC again (no fresh aging)
            var statsRemaining = FullGc(cache);
            XAssert.AreEqual(1, statsRemaining["Fingerprint_Collected"], "Collected the one fingerprint that was held open");
            XAssert.AreEqual(1, statsRemaining["Fingerprint_Pending"], "Moved to pending the one fingerprint that was held open");
            XAssert.AreEqual(1, statsRemaining["CAS_Collected"], "Collected the one CAS item that was held open");
            XAssert.AreEqual(4, statsRemaining["CAS_Pending"], "Moved to pending the one CAS item that was held open and the 3 CAS items that were referenced by the fingerprint that was held");

            // Check that the partition counts match as needed
            XAssert.AreEqual((TestType is TestBasicFilesystemSharded) ? TestBasicFilesystemSharded.SHARD_COUNT : 1, statsRemaining["Fingerprint_Partitions"]);
            XAssert.AreEqual((TestType is TestBasicFilesystemSharded) ? TestBasicFilesystemSharded.SHARD_COUNT : 1, statsRemaining["CAS_Partitions"]);

            AssertSuccess(await cache.ShutdownAsync());
        }

        [Fact]
        public async Task TestGcPrefix()
        {
            string cacheConfig = TestType.NewCache(nameof(TestGcPrefix), true);
            BasicFilesystemCache cache = (await InitializeCacheAsync(cacheConfig).SuccessAsync()) as BasicFilesystemCache;
            XAssert.IsNotNull(cache, "Failed to create cache for GC tests!");

            PipDefinition[] pips =
            {
                new PipDefinition("Pip1", pipSize: 3),
                new PipDefinition("Pip2", pipSize: 4),
                new PipDefinition("Pip3", pipSize: 5)
            };

            // First, lets filter the GC to only do one of the files.
            CacheEntries files = await BuildPipsAndGetCacheEntries(cache, "Build", pips);
            cache.DeleteSession("Build");
            for (int i = 1; i < 4; i++)
            {
                files.AgeAll();

                string targetFile = files.FingerprintFiles.Keys.First();

                // Get the shard directory of the weak fingerprint
                string prefix = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(targetFile)));
                XAssert.AreEqual(3, prefix.Length);
                m_output.WriteLine("GC Prefix: [{0}]", prefix);
                var stats = cache.CollectUnreferencedFingerprints(m_output, prefixFilter: prefix.Substring(0, i));

                XAssert.IsFalse(File.Exists(targetFile), "Should have moved this one to pending");
                BasicFilesystemCache.UndoPendingDelete(targetFile);
                files.AssertExists();
            }

            AssertSuccess(await cache.ShutdownAsync());
        }

        [Fact]
        public async Task TestGcReadFailure()
        {
            string cacheConfig = TestType.NewCache(nameof(TestGcReadFailure), true);
            BasicFilesystemCache cache = (await InitializeCacheAsync(cacheConfig).SuccessAsync()) as BasicFilesystemCache;
            XAssert.IsNotNull(cache, "Failed to create cache for GC tests!");

            PipDefinition[] pips =
            {
                new PipDefinition("Pip1", pipSize: 3),
                new PipDefinition("Pip2", pipSize: 4)
            };

            bool disconnectErrorReported = false;

            // Assumes that the cache will be erroring out because of a file access error and not because of some
            // other random reason.
            cache.SuscribeForCacheStateDegredationFailures((failure) => { disconnectErrorReported = true; });

            CacheEntries files = await BuildPipsAndGetCacheEntries(cache, "Build", pips);

            // We will hold on to one of the files in the fingerprints in such a way as to fail the GC
            FileInfo fileInfo = files.FingerprintFiles.Values.Last();

            using (var fileStream = fileInfo.Open(FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var failureExpected = cache.CollectUnreferencedCasItems(m_output);
                XAssert.IsFalse(failureExpected.Succeeded, "Failed to stop CAS GC even when a fingerprint was not readable!");
            }

            // This time, we need to hold onto a session file - preventing the reading of roots
            var sessionFiles = new DirectoryInfo(cache.SessionRoot).GetFiles();
            using (var fileStream = sessionFiles[0].Open(FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var failureExpected = cache.CollectUnreferencedFingerprints(m_output);
                XAssert.IsFalse(failureExpected.Succeeded, "Failed to stop Fingerprint GC even when a session was not readable!");
            }

            // After these errors, the cache should have disconnected due to lack of access to the session file
            XAssert.IsTrue(cache.IsDisconnected);
            XAssert.IsTrue(disconnectErrorReported);

            AssertSuccess(await cache.ShutdownAsync());
        }

        [Fact]
        public async Task TestGcCorruptedFingerprint()
        {
            string cacheConfig = TestType.NewCache(nameof(TestGcCorruptedFingerprint), true);
            BasicFilesystemCache cache = (await InitializeCacheAsync(cacheConfig).SuccessAsync()) as BasicFilesystemCache;
            XAssert.IsNotNull(cache, "Failed to create cache for GC tests!");

            PipDefinition[] pips =
            {
                new PipDefinition("Pip1"),
                new PipDefinition("Pip2"),
                new PipDefinition("Pip3")
            };

            CacheEntries files = await BuildPipsAndGetCacheEntries(cache, "Build", pips);

            // We will corrupt one of the fingerprints
            var fp = files.FingerprintFiles.Values.ToArray();

            // Corrupt the fingerprint by keeping the same size but writing all zeros
            // Which is a common corruption on hardware/OS resets.
            long length = fp[0].Length;
            using (FileStream fs = fp[0].OpenWrite())
            {
                while (length-- > 0)
                {
                    fs.WriteByte(0);
                }
            }

            // Corrupt the second fingerprint by trimming the file length
            using (FileStream fs = fp[1].OpenWrite())
            {
                fs.SetLength(0);
            }

            // Now do a GC that reads the fingerprints (collecting the CAS items)
            Dictionary<string, double> statsCas = cache.CollectUnreferencedCasItems(m_output).Success("{0}:CollectUnreferencedCasItems - corrupted fingerprint");

            m_output.LogStats(statsCas);

            // There should now be only 1 valid fingerprint and 2 different
            // invalid ones.
            XAssert.AreEqual(statsCas["CAS_ReadRoots_Fingerprints"], 1);
            XAssert.AreEqual(statsCas["CAS_ReadRoots_InvalidFingerprints"], 2);

            // Check that the partition counts match as needed
            XAssert.AreEqual((TestType is TestBasicFilesystemSharded) ? TestBasicFilesystemSharded.SHARD_COUNT : 1, statsCas["CAS_Partitions"]);

            AssertSuccess(await cache.ShutdownAsync());
        }

        private void ParallelConcurrentFullGc(BasicFilesystemCache cache, string action)
        {
            // I picked 16 concurrent as a way to really push the the tests
            // Making this higher significantly increases the test time without
            // actually increasing test coverage.  Even at 16 this is relatively
            // high contention rates (which requires the skipped/retry flag).
            var gcTasks = new Task<Dictionary<string, double>>[16];

            for (int i = 0; i < gcTasks.Length; i++)
            {
                string name = string.Format("{0}:ParrallelConcurrentFullGc-{1:X}", action, i);
                gcTasks[i] = Task.Run(() =>
                {
                    Dictionary<string, double> statsFp = cache.CollectUnreferencedFingerprints(m_output).Success("{0}:CollectUnreferencedFingerprints\n{1}", name);
                    Dictionary<string, double> statsCas = cache.CollectUnreferencedCasItems(m_output).Success("{0}:CollectUnreferencedCasItems\n{1}", name);

                    foreach (var kv in statsCas)
                    {
                        statsFp.Add(kv.Key, kv.Value);
                    }

                    return statsFp;
                });
            }

            Task.WaitAll(gcTasks);

            // render the stats in a sane way for the concurrent GCs
            // Note that the GC is specifically designed to just let
            // files that are contended stay in place and then be
            // collected next time the GC runs.  The GC will complain
            // if the cache is in error.
            for (int i = 0; i < gcTasks.Length; i++)
            {
                m_output.WriteLine("Concurrent GC #{0} - {1}", i, action);
                var stats = gcTasks[i].Result;
                m_output.LogStats(stats);
            }
        }

        [Fact]
        public async Task TestGcConcurrency()
        {
            // This is just minimal concurrency testing such that it does not completely mess up
            // Most of the work in making the GC safe was in the design and hand testing as this
            // is about having a GC run while multiple mutators run (and multiple GCs run)
            string cacheConfig = TestType.NewCache(nameof(TestGcConcurrency), true);
            BasicFilesystemCache cache = (await InitializeCacheAsync(cacheConfig).SuccessAsync()) as BasicFilesystemCache;
            XAssert.IsNotNull(cache, "Failed to create cache for GC tests!");

            PipDefinition[] pips =
            {
                new PipDefinition("Pip1", pipSize: 3),
                new PipDefinition("Pip2", pipSize: 4),
                new PipDefinition("Pip3", pipSize: 5),
                new PipDefinition("Pip4", pipSize: 4),
                new PipDefinition("Pip5", pipSize: 3)
            };

            CacheEntries files = await BuildPipsAndGetCacheEntries(cache, "Build", pips);

            // First have the GC do nothing (all items still rooted but old enough to collect)
            files.AgeAll();
            ParallelConcurrentFullGc(cache, "Noop");
            files.AssertExists();

            // Now delete the session and make sure we collect correctly
            cache.DeleteSession("Build");

            // This may take a few tries to get all of the items GC'ed
            // Since we assume that the GC will always work, any error returned
            // by the GC will trigger a fault and failure of the test.
            // However, the GC is designed to, if anything is in question at
            // all, to just skip deleting (or marking as pending delete)
            // any item that is busy or otherwise engaged.  This does mean that
            // multiple concurrent GC can, in rare cases, cause a file
            // (specifically a strong fingerprint) to not get collected for
            // a given pass due to the file being in use by another GC.
            // This is perfectly correct and will cause that item to be
            // collected later.  However, later may require another
            // aging of the files since the file age may have been
            // reset during the failed attempt to mark it pending.
            // (Which is a good thing since failing to mark pending is
            // a potential sign that something is using it and thus
            // it is not yet ready to be changed)
            // Anyway, the number of passes and the exact state of the GC
            // is not directly knowable due to these races but they sure should
            // not be greater than 100
            int pass = 0;
            while (files.Count > 0)
            {
                files.AgeAll();
                pass++;
                ParallelConcurrentFullGc(cache, "Pass #" + pass);

                // Make sure some progress happened
                // That means some items had to be marked pending or
                // got collected each time through the process.
                // This will make sure we are always making some
                // progress with the GC and thus will terminate.
                files.AssertMissingSome();

                files = new CacheEntries(cache);
            }

            AssertSuccess(await cache.ShutdownAsync());
        }

        private void AssertSuccess<T>(Possible<T> possible)
        {
            Assert.True(possible.Succeeded);
        }

        private void AssertSuccess<T>(Possible<T, Failure> possible)
        {
            Assert.True(possible.Succeeded);
        }
    }
}
