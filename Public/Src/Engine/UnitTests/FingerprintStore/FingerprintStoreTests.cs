// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Engine.Cache.KeyValueStores;
using BuildXL.Engine.Cache.Serialization;
using BuildXL.Native.IO;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Tracing;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using static BuildXL.Scheduler.Tracing.FingerprintStore;

using FingerprintStoreClass = BuildXL.Scheduler.Tracing.FingerprintStore;
using BuildXLConfiguration = BuildXL.Utilities.Configuration;

namespace Test.BuildXL.FingerprintStore
{
    public class FingerprintStoreTests : SchedulerIntegrationTestBase
    {
        public FingerprintStoreTests(ITestOutputHelper output)
            : base(output)
        {
            Configuration.Logging.StoreFingerprints = true;
            // Forces unique, time-stamped logs directory between different scheduler runs within the same test
            Configuration.Logging.LogsToRetain = int.MaxValue;
        }

        [Fact]
        public void VerifyFingerprintStoreEntryComplete()
        {
            var dir = CreateOutputDirectoryArtifact(ReadonlyRoot);
            Directory.CreateDirectory(ArtifactToString(dir));

            var nestedFile = CreateOutputFileArtifact(ArtifactToString(dir));
            File.WriteAllText(ArtifactToString(nestedFile), "nestedFile");

            var pipA = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.EnumerateDir(dir),
                Operation.WriteFile(CreateOutputFileArtifact())
            }).Process;

            var result = RunScheduler().AssertCacheMissWithFingerprintStore(Context.PathTable, Expander, pipA.PipId);

            var entry = default(FingerprintStoreEntry);
            var directoryMembershipFingerprintEntry = default(KeyValuePair<string, string>);
            FingerprintStoreSession(ResultToStoreDirectory(result), store =>
            {
                store.TryGetFingerprintStoreEntryBySemiStableHash(pipA.FormattedSemiStableHash, out entry);

                // There may be a 1-to-n relationship between fingerprint entries to relevant directory membership fingerprints, so they are stored separately
                // Try to parse out the directory enumeration's hash from the strong fingerprint to validate there is an entry for the directory membership fingerprint
                var reader = new JsonReader(entry.StrongFingerprintEntry.StrongFingerprintToInputs.Value);
                XAssert.IsTrue(reader.TryGetPropertyValue(ObservedInputConstants.DirectoryEnumeration, out var directoryFingerprint));

                store.TryGetContentHashValue(directoryFingerprint, out var directoryMembers);
                // Validate this is the correct enumeration
                XAssert.IsTrue(directoryMembers.Contains(Path.GetFileName(ArtifactToString(nestedFile))));

                directoryMembershipFingerprintEntry = new KeyValuePair<string, string>(directoryFingerprint, directoryMembers);
            });

            // Check that all values of the entry are filled out
            var keys = entry.PipToFingerprintKeys.Value;
            var pipToFingerprintKeysStrings = new string[]
            {
                entry.PipToFingerprintKeys.Key,
                keys.WeakFingerprint,
                keys.StrongFingerprint,
                keys.FormattedPathSetHash
            };

            foreach (var str in pipToFingerprintKeysStrings)
            {
                XAssert.IsTrue(!string.IsNullOrEmpty(str));
            }

            var kvps = new KeyValuePair<string, string>[]
            {
                entry.WeakFingerprintToInputs,
                entry.StrongFingerprintEntry.StrongFingerprintToInputs,
                entry.StrongFingerprintEntry.PathSetHashToInputs,
                directoryMembershipFingerprintEntry
            };

            foreach (var kvp in kvps)
            {
                XAssert.IsTrue(!string.IsNullOrEmpty(kvp.Key));
                XAssert.IsTrue(!string.IsNullOrEmpty(kvp.Value));
            }
        }

        /// <summary>
        /// Content addressable entries like content hashes don't need to be replaced if an entry with the
        /// same key already exists.
        /// </summary>
        [Fact]
        public void DontOverwriteExistingContentAddressableEntries()
        {
            // Use a test hook to capture fingerprint store counters
            var testHooks = new SchedulerTestHooks
            {
                FingerprintStoreTestHooks = new FingerprintStoreTestHooks()
            };

            var dir = CreateOutputDirectoryArtifact(ReadonlyRoot);
            Directory.CreateDirectory(ArtifactToString(dir));

            var nestedFile = CreateOutputFileArtifact(ArtifactToString(dir));
            File.WriteAllText(ArtifactToString(nestedFile), "nestedFile");

            // Schedule two pips that use the same path set entry and directory membership fingerprint,
            // but are run one after the other to prevent any race conditions
            var outFile = CreateOutputFileArtifact();
            var pipA = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.EnumerateDir(dir),
                Operation.WriteFile(outFile)
            }).Process;

            var builderB = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(outFile),
                Operation.EnumerateDir(dir),
                Operation.WriteFile(CreateOutputFileArtifact())
            });
            // Force working directories to be the same so any observed inputs on the working directories do not
            // show up differently in the path set
            builderB.WorkingDirectory = DirectoryArtifact.CreateWithZeroPartialSealId(pipA.WorkingDirectory);

            var pipB = SchedulePipBuilder(builderB).Process;

            RunScheduler(testHooks).AssertCacheMissWithFingerprintStore(Context.PathTable, Expander, pipA.PipId, pipB.PipId);

            var counters = testHooks.FingerprintStoreTestHooks.Counters;
            XAssert.AreEqual(1, counters.GetCounterValue(FingerprintStoreCounters.NumPathSetEntriesPut));
            XAssert.IsTrue(counters.GetCounterValue(FingerprintStoreCounters.NumDirectoryMembershipEntriesPut) >= 1);
        }

        /// <summary>
        /// The FingerprintStore truncates the 256-bit content hashes for the sake of saving disk space.
        /// Test that absent path probes and untracked files, which have special case content hashes, don't collide.
        /// </summary>
        [Fact]
        public void TruncatedHashesDontCollideAbsentPathProbesAndUntrackedPaths()
        {
            // Untracked file (file in nonhashable root)
            var untrackedFile = CreateSourceFile(NonHashableRoot);
            // Absent file
            var absentFile = FileArtifact.CreateSourceFile(ObjectRootPath);

            Process pipUntrackedFile = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.ReadFile(untrackedFile),
                Operation.WriteFile(CreateOutputFileArtifact())
            }).Process;

            Process pipAbsentFile = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.ReadFile(absentFile),
                Operation.WriteFile(CreateOutputFileArtifact())
            }).Process;

            var result = RunScheduler().AssertCacheMissWithFingerprintStore(Context.PathTable, Expander, pipUntrackedFile.PipId, pipAbsentFile.PipId);

            FingerprintStoreSession(ResultToStoreDirectory(result), store =>
            {
                XAssert.IsTrue(store.TryGetFingerprintStoreEntryBySemiStableHash(pipUntrackedFile.FormattedSemiStableHash, out var untrackedEntry));
                XAssert.IsTrue(store.TryGetFingerprintStoreEntryBySemiStableHash(pipAbsentFile.FormattedSemiStableHash, out var absentEntry));

                // As declared dependencies, the files will appear in the weak fingerprints
                // Parse the weak fingerprint input values to get the corresponding file content hashes
                var readerUntracked = new JsonReader(untrackedEntry.WeakFingerprintToInputs.Value);
                XAssert.IsTrue(readerUntracked.TryGetPropertyValue(ArtifactToPrint(untrackedFile), out var untrackedValue));

                var readerAbsent = new JsonReader(absentEntry.WeakFingerprintToInputs.Value);
                XAssert.IsTrue(readerAbsent.TryGetPropertyValue(ArtifactToPrint(absentFile), out var absentValue));

                // The hashes stored in the FingerprintStore should differentiate between the special case values for
                // the content hashes of untracked files and absent files.
                XAssert.AreNotEqual(untrackedValue, absentValue);
            });
        }

        /// <summary>
        /// The FingerprintStore truncates the 256-bit content hashes for the sake of saving disk space.
        /// Test that path sets and directory memberships don't collide.
        /// </summary>
        [Fact]
        public void TruncatedHashesDontCollidePathSetsAndDirectoryMemberships()
        {
            // Use a test hook to capture fingerprint store counters
            var testHooks = new SchedulerTestHooks
            {
                FingerprintStoreTestHooks = new FingerprintStoreTestHooks()
            };

            var dir = CreateOutputDirectoryArtifact(ReadonlyRoot);
            Directory.CreateDirectory(ArtifactToString(dir));

            var nestedFile = CreateOutputFileArtifact(ArtifactToString(dir));
            File.WriteAllText(ArtifactToString(nestedFile), "nestedFile");

            var dir2 = CreateOutputDirectoryArtifact(ReadonlyRoot);
            Directory.CreateDirectory(ArtifactToString(dir2));

            var nestedFile2 = CreateOutputFileArtifact(ArtifactToString(dir2));
            File.WriteAllText(ArtifactToString(nestedFile2), "nestedFile2");

            // Schedule two pips that use the same path set entry and directory membership fingerprint,
            // but are run one after the other to prevent any race conditions
            var outFile = CreateOutputFileArtifact();
            var pipA = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.EnumerateDir(dir),
                Operation.WriteFile(outFile)
            }).Process;

            var pipB = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.ReadFile(outFile),
                Operation.EnumerateDir(dir2),
                Operation.WriteFile(CreateOutputFileArtifact())
            }).Process;

            RunScheduler(testHooks).AssertCacheMissWithFingerprintStore(Context.PathTable, Expander, pipA.PipId, pipB.PipId);

            var counters = testHooks.FingerprintStoreTestHooks.Counters;
            // Make sure there are unique puts for each path set and directory membership
            // The FingerprintStore will not double-put the same content hash
            XAssert.AreEqual(2, counters.GetCounterValue(FingerprintStoreCounters.NumPathSetEntriesPut));
            XAssert.IsTrue(counters.GetCounterValue(FingerprintStoreCounters.NumDirectoryMembershipEntriesPut) >= 2);
        }

        /// <summary>
        /// Verifies that garbage collection runs and only collects entries that are past the max entry age limit.
        ///
        /// Note:
        /// 1. Garbage collect only runs if there is at least one cache miss in the build.
        /// This is verified in <see cref="CancelGarbageCollectOnCacheHitBuild"/>.
        /// 2. A cache hit will still refresh the age of an entry. With incremental scheduling disabled,
        /// a pip must be completely removed from the build to be garbage collected.
        /// </summary>
        [Fact]
        public void VerifyGarbageCollectWorks()
        {
            var testHooks = new SchedulerTestHooks()
            {
                FingerprintStoreTestHooks = new FingerprintStoreTestHooks()
                {
                    MaxEntryAge = TimeSpan.FromMilliseconds(10)
                }
            };

            var dir = CreateOutputDirectoryArtifact(ReadonlyRoot);
            Directory.CreateDirectory(ArtifactToString(dir));

            var nestedFile = CreateOutputFileArtifact(ArtifactToString(dir));
            File.WriteAllText(ArtifactToString(nestedFile), "nestedFile");

            var srcFile = CreateSourceFile();
            var cacheMissPipOps = new Operation[]
            {
                Operation.ReadFile(srcFile),
                Operation.WriteFile(CreateOutputFileArtifact()),
            };

            var cacheHitPipOps = new Operation[]
            {
                Operation.WriteFile(CreateOutputFileArtifact())
            };

            var cacheMissPip = CreateAndSchedulePipBuilder(cacheMissPipOps).Process;

            var cacheHitPip = CreateAndSchedulePipBuilder(cacheHitPipOps).Process;

            var gcPip = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.EnumerateDir(dir),
                Operation.WriteFile(CreateOutputFileArtifact())
            }).Process;

            var build1 = RunScheduler(testHooks).AssertCacheMissWithFingerprintStore(Context.PathTable, Expander, cacheMissPip.PipId);

            XAssert.AreEqual(3, testHooks.FingerprintStoreTestHooks.Counters.GetCounterValue(FingerprintStoreCounters.NumPipFingerprintEntriesPut));
            // No keys are iterated through for garbage collection on first instance of a FingerprintStore
            XAssert.AreEqual(0, testHooks.FingerprintStoreTestHooks.Counters.GetCounterValue(FingerprintStoreCounters.NumPipFingerprintEntriesRemaining));
            XAssert.AreEqual(0, testHooks.FingerprintStoreTestHooks.Counters.GetCounterValue(FingerprintStoreCounters.NumPipFingerprintEntriesGarbageCollected));
            XAssert.AreEqual(0, testHooks.FingerprintStoreTestHooks.Counters.GetCounterValue(FingerprintStoreCounters.NumContentHashEntriesRemaining));
            XAssert.AreEqual(0, testHooks.FingerprintStoreTestHooks.Counters.GetCounterValue(FingerprintStoreCounters.NumContentHashEntriesGarbageCollected));


            // Wait out max entry age
            System.Threading.Thread.Sleep(10);

            // Reset to remove a pip from the build
            ResetPipGraphBuilder();

            // Make sure the two builds use the same fingerprint store
            Configuration.Layout.FingerprintStoreDirectory = build1.Config.Layout.FingerprintStoreDirectory;

            File.WriteAllText(ArtifactToString(srcFile), "asdf");
            cacheMissPip = CreateAndSchedulePipBuilder(cacheMissPipOps).Process;
            cacheHitPip = CreateAndSchedulePipBuilder(cacheHitPipOps).Process;

            var build2 = RunScheduler(testHooks)
                .AssertCacheMissWithFingerprintStore(Context.PathTable, Expander, cacheMissPip.PipId)
                .AssertCacheHit(cacheHitPip.PipId);

            XAssert.AreEqual(build1.Config.Layout.FingerprintStoreDirectory, build2.Config.Layout.FingerprintStoreDirectory);

            // Any pip that goes through cache lookup will have its fingerprint store entry's age refreshed
            // Only gcPip which was not part of this build will be garbage collected
            XAssert.AreEqual(1, testHooks.FingerprintStoreTestHooks.Counters.GetCounterValue(FingerprintStoreCounters.NumPipFingerprintEntriesGarbageCollected));
            // Pip unique output hash entry
            XAssert.AreEqual(1, testHooks.FingerprintStoreTestHooks.Counters.GetCounterValue(FingerprintStoreCounters.NumPipUniqueOutputHashEntriesGarbageCollected));
            // 1 pathset entry, 1 directory membership fingerprint entry
            XAssert.IsTrue(testHooks.FingerprintStoreTestHooks.Counters.GetCounterValue(FingerprintStoreCounters.NumContentHashEntriesGarbageCollected) >= 2);

            FingerprintStoreSession(ResultToStoreDirectory(build2), store =>
            {
                XAssert.IsTrue(store.TryGetFingerprintStoreEntryBySemiStableHash(cacheMissPip.FormattedSemiStableHash, out var cacheMissEntry));
                XAssert.IsTrue(store.TryGetFingerprintStoreEntryBySemiStableHash(cacheHitPip.FormattedSemiStableHash, out var cacheHitEntry));

                XAssert.IsFalse(store.TryGetFingerprintStoreEntryBySemiStableHash(gcPip.FormattedSemiStableHash, out var gcEntry));
            });
        }

        [Fact]
        public void CancelGarbageCollectOnCacheHitBuild()
        {
            // Start with default settings
            var testHooks = new SchedulerTestHooks()
            {
                FingerprintStoreTestHooks = new FingerprintStoreTestHooks()
                {
                    MaxEntryAge = TimeSpan.FromMilliseconds(10)
                }
            };

            var srcFile = CreateSourceFile();
            var cacheMissPipOps = new Operation[]
            {
                Operation.ReadFile(srcFile),
                Operation.WriteFile(CreateOutputFileArtifact()),
            };

            var reusedPip = CreateAndSchedulePipBuilder(cacheMissPipOps).Process;

            var gcPip = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.WriteFile(CreateOutputFileArtifact())
            }).Process;

            var build1 = RunScheduler(testHooks).AssertCacheMissWithFingerprintStore(Context.PathTable, Expander, reusedPip.PipId);

            XAssert.AreEqual(2, testHooks.FingerprintStoreTestHooks.Counters.GetCounterValue(FingerprintStoreCounters.NumPipFingerprintEntriesPut));
            // Nothing is garbage collected on first instance of a FingerprintStore
            XAssert.AreEqual(0, testHooks.FingerprintStoreTestHooks.Counters.GetCounterValue(FingerprintStoreCounters.NumPipFingerprintEntriesGarbageCollected));

            // Wait out max entry age
            System.Threading.Thread.Sleep(10);

            // Reset to remove a pip from the build
            ResetPipGraphBuilder();

            // Make sure the two builds use the same fingerprint store
            Configuration.Layout.FingerprintStoreDirectory = build1.Config.Layout.FingerprintStoreDirectory;

            reusedPip = CreateAndSchedulePipBuilder(cacheMissPipOps).Process;
            var build2 = RunScheduler(testHooks).AssertCacheHit(reusedPip.PipId);

            XAssert.AreEqual(build1.Config.Layout.FingerprintStoreDirectory, build2.Config.Layout.FingerprintStoreDirectory);

            // For performance reasons, garbage collect is force-cancelled on builds with 100% cache hits
            XAssert.AreEqual(0, testHooks.FingerprintStoreTestHooks.Counters.GetCounterValue(FingerprintStoreCounters.NumPipFingerprintEntriesGarbageCollected));

            FingerprintStoreSession(ResultToStoreDirectory(build2), store =>
            {
                XAssert.IsTrue(store.TryGetFingerprintStoreEntryBySemiStableHash(reusedPip.FormattedSemiStableHash, out var entry));
                XAssert.IsTrue(store.TryGetFingerprintStoreEntryBySemiStableHash(gcPip.FormattedSemiStableHash, out var entry3));
            });

            // Cache miss build
            File.WriteAllText(ArtifactToString(srcFile), "asdf");
            var build3 = RunScheduler(testHooks).AssertCacheMiss(reusedPip.PipId);

            // As long as there is at least one miss, garbage collect will run
            XAssert.AreEqual(1, testHooks.FingerprintStoreTestHooks.Counters.GetCounterValue(FingerprintStoreCounters.NumPipFingerprintEntriesGarbageCollected));

            FingerprintStoreSession(ResultToStoreDirectory(build3), store =>
            {
                XAssert.IsTrue(store.TryGetFingerprintStoreEntryBySemiStableHash(reusedPip.FormattedSemiStableHash, out var entry));
                XAssert.IsFalse(store.TryGetFingerprintStoreEntryBySemiStableHash(gcPip.FormattedSemiStableHash, out var gcEntry));
            });
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void NoGarbageCollectTimeOnNoopBuild()
        {
            Configuration.Schedule.IncrementalScheduling = true;
            Configuration.Schedule.SkipHashSourceFile = false;

            // Start with default settings
            var testHooks = new SchedulerTestHooks()
            {
                FingerprintStoreTestHooks = new FingerprintStoreTestHooks()
                {
                    MaxEntryAge = TimeSpan.FromMilliseconds(10)
                }
            };

            var pip = CreateAndSchedulePipBuilder(new Operation[]
                {
                    Operation.WriteFile(CreateOutputFileArtifact())
                }).Process;

            RunScheduler(testHooks).AssertCacheMissWithFingerprintStore(Context.PathTable, Expander, pip.PipId);

            // No-op build, pip should be incrementally skip without going through cache lookup
            RunScheduler(testHooks).AssertCacheHit(pip.PipId);

            var zeroTime = TimeSpan.Zero;

            // In a no-op build, the fingerprint store is skipped completely, so no entries have their ages for garbage collect refreshed
            // No garbage collect, no overhead of managing LRU maps
            XAssert.AreEqual(zeroTime, testHooks.FingerprintStoreTestHooks.Counters.GetElapsedTime(FingerprintStoreCounters.GarbageCollectionTime));
            XAssert.AreEqual(zeroTime, testHooks.FingerprintStoreTestHooks.Counters.GetElapsedTime(FingerprintStoreCounters.SerializeLruEntriesMapsTime));
            XAssert.AreEqual(zeroTime, testHooks.FingerprintStoreTestHooks.Counters.GetElapsedTime(FingerprintStoreCounters.DeserializeLruEntriesMapTime));

            // Turn off incremental scheduling so the fingerprint entry will get its age refreshed
            Configuration.Schedule.IncrementalScheduling = false;

            RunScheduler(testHooks).AssertCacheHit(pip.PipId);
            XAssert.AreEqual(zeroTime, testHooks.FingerprintStoreTestHooks.Counters.GetElapsedTime(FingerprintStoreCounters.GarbageCollectionTime));
            // Mandatory overhead for managing LRU entries when an entry's age changes
            XAssert.AreNotEqual(zeroTime, testHooks.FingerprintStoreTestHooks.Counters.GetElapsedTime(FingerprintStoreCounters.SerializeLruEntriesMapsTime));
            XAssert.AreNotEqual(zeroTime, testHooks.FingerprintStoreTestHooks.Counters.GetElapsedTime(FingerprintStoreCounters.DeserializeLruEntriesMapTime));
        }

        [Fact]
        public void StoreFingerprintsDisabled()
        {
            Configuration.Logging.StoreFingerprints = false;
            CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.WriteFile(CreateOutputFileArtifact())
            });

            var build = RunScheduler().AssertSuccess();

            // No logs should have been recorded
            XAssert.IsFalse(Directory.Exists(build.Config.Logging.ExecutionFingerprintStoreLogDirectory.ToString(Context.PathTable)));

            // No directory should have been created
            var fingerprintStoreDirectory = build.Config.Layout.FingerprintStoreDirectory;
            XAssert.IsFalse(Directory.Exists(fingerprintStoreDirectory.ToString(Context.PathTable)));
        }

        [Fact]
        public void WeakFingerprintMiss()
        {
            var srcA = CreateSourceFile();
            var pipA = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.ReadFile(srcA),
                Operation.WriteFile(CreateOutputFileArtifact())
            }).Process;

            var pipB = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.WriteFile(CreateOutputFileArtifact())
            }).Process;

            var build1 = RunScheduler().AssertCacheMissWithFingerprintStore(Context.PathTable, Expander, pipA.PipId, pipB.PipId);

            // Check that the build's logs hold entries for pipA and pipB
            var storeDirectory = ResultToStoreDirectory(build1);
            FingerprintStoreEntry entry1A = new FingerprintStoreEntry(), entry1B = new FingerprintStoreEntry();
            FingerprintStoreSession(storeDirectory, (store) =>
            {
                XAssert.IsTrue(store.TryGetFingerprintStoreEntryBySemiStableHash(pipA.FormattedSemiStableHash, out entry1A));
                XAssert.IsTrue(store.TryGetFingerprintStoreEntryBySemiStableHash(pipB.FormattedSemiStableHash, out entry1B));
            });

            // Trigger miss on pipA
            File.WriteAllText(ArtifactToString(srcA), "asdf");

            var build2 = RunScheduler().AssertCacheMissWithFingerprintStore(Context.PathTable, Expander, pipA.PipId).AssertCacheHit(pipB.PipId);

            storeDirectory = ResultToStoreDirectory(build2);
            FingerprintStoreEntry entry2A = new FingerprintStoreEntry(), entry2B = new FingerprintStoreEntry();
            FingerprintStoreSession(storeDirectory, (store) =>
            {
                XAssert.IsTrue(store.TryGetFingerprintStoreEntryBySemiStableHash(pipA.FormattedSemiStableHash, out entry2A));
                XAssert.IsTrue(store.TryGetFingerprintStoreEntryBySemiStableHash(pipB.FormattedSemiStableHash, out entry2B));
            });
            // Weak fingerprint miss on pipA
            XAssert.AreNotEqual(entry1A.WeakFingerprintToInputs, entry2A.WeakFingerprintToInputs);
            // Hit on pipB
            XAssert.AreEqual(entry1B.PipToFingerprintKeys, entry2B.PipToFingerprintKeys);
        }

        [Fact]
        public void TestFingerprintStoreEntryToString()
        {
            var dir = CreateOutputDirectoryArtifact(ReadonlyRoot);
            Directory.CreateDirectory(ArtifactToString(dir));

            var nestedFile = CreateOutputFileArtifact(ArtifactToString(dir));
            File.WriteAllText(ArtifactToString(nestedFile), "nestedFile");

            var dir2 = CreateOutputDirectoryArtifact(ReadonlyRoot);
            Directory.CreateDirectory(ArtifactToString(dir2));

            var nestedFile2 = CreateOutputFileArtifact(ArtifactToString(dir2));
            File.WriteAllText(ArtifactToString(nestedFile2), "nestedFile");

            var pipA = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.EnumerateDir(dir),
                Operation.EnumerateDir(dir2),
                Operation.WriteFile(CreateOutputFileArtifact())
            }).Process;

            var result = RunScheduler().AssertSuccess();

            FingerprintStoreSession(ResultToStoreDirectory(result), store =>
            {
                store.TryGetFingerprintStoreEntryBySemiStableHash(pipA.FormattedSemiStableHash, out var entryA);
                var stringA = entryA.ToString();

                // Make sure both directory enumeration paths are printed
                // The exact strings won't match up due to JSON text requiring "\\" instead of "\", but the unique directory names should show
                XAssert.IsTrue(stringA.Contains(Path.GetFileName(ArtifactToString(dir))));
                XAssert.IsTrue(stringA.Contains(Path.GetFileName(ArtifactToString(dir2))));
            });
        }

        [Fact]
        public void StrongFingerprintMiss()
        {
            var dir = CreateOutputDirectoryArtifact(ReadonlyRoot);
            Directory.CreateDirectory(ArtifactToString(dir));

            var nestedFile = CreateOutputFileArtifact(ArtifactToString(dir));
            File.WriteAllText(ArtifactToString(nestedFile), "nestedFile");

            var pipA = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.EnumerateDir(dir),
                Operation.WriteFile(CreateOutputFileArtifact())
            }).Process;

            var pipB = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.WriteFile(CreateOutputFileArtifact())
            }).Process;

            var build1 = RunScheduler().AssertCacheMissWithFingerprintStore(Context.PathTable, Expander, pipA.PipId, pipB.PipId);

            // Check that the build's logs hold entries for pipA and pipB
            var storeDirectory = ResultToStoreDirectory(build1);
            FingerprintStoreEntry entry1A = new FingerprintStoreEntry(), entry1B = new FingerprintStoreEntry();
            FingerprintStoreSession(storeDirectory, (store) =>
            {
                XAssert.IsTrue(store.TryGetFingerprintStoreEntryBySemiStableHash(pipA.FormattedSemiStableHash, out entry1A));
                XAssert.IsTrue(store.TryGetFingerprintStoreEntryBySemiStableHash(pipB.FormattedSemiStableHash, out entry1B));
            });

            // Trigger miss on pipA
            var newNestedFile = CreateOutputFileArtifact(ArtifactToString(dir));
            File.WriteAllText(ArtifactToString(newNestedFile), "newNestedFile");

            var build2 = RunScheduler().AssertCacheMissWithFingerprintStore(Context.PathTable, Expander, pipA.PipId).AssertCacheHit(pipB.PipId);

            storeDirectory = ResultToStoreDirectory(build2);
            FingerprintStoreEntry entry2A = new FingerprintStoreEntry(), entry2B = new FingerprintStoreEntry();
            FingerprintStoreSession(storeDirectory, (store) =>
            {
                XAssert.IsTrue(store.TryGetFingerprintStoreEntryBySemiStableHash(pipA.FormattedSemiStableHash, out entry2A));
                XAssert.IsTrue(store.TryGetFingerprintStoreEntryBySemiStableHash(pipB.FormattedSemiStableHash, out entry2B));
            });

            // Strong fingerprint miss on pipA
            XAssert.AreNotEqual(entry1A.StrongFingerprintEntry.StrongFingerprintToInputs, entry2A.StrongFingerprintEntry.StrongFingerprintToInputs);
            // Hit on pipB
            XAssert.AreEqual(entry1B.PipToFingerprintKeys, entry2B.PipToFingerprintKeys);
        }

        /// <summary>
        /// Checks that the fingerprint store retains the entry for the last
        /// recently used fingerprint, even if it was not the last executed.
        /// </summary>
        [Fact]
        public void ReplaceEntryOnFullFingerprintHit()
        {
            var srcA = CreateSourceFile();
            string originalText = "first";
            File.WriteAllText(ArtifactToString(srcA), originalText);

            var dir = CreateOutputDirectoryArtifact(ReadonlyRoot);
            Directory.CreateDirectory(ArtifactToString(dir));

            var pipA = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.ReadFile(srcA),
                Operation.WriteFile(CreateOutputFileArtifact())
            }).Process;

            var srcB = CreateSourceFile();
            var pipB = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.EnumerateDir(dir),
                Operation.ReadFile(srcB),
                Operation.WriteFile(CreateOutputFileArtifact())
            }).Process;

            var build1 = RunScheduler().AssertCacheMissWithFingerprintStore(Context.PathTable, Expander, pipA.PipId, pipB.PipId);

            // Check that the build's logs hold entries for pipA and pipB
            var storeDirectory = ResultToStoreDirectory(build1);
            FingerprintStoreEntry entry1A = new FingerprintStoreEntry(), entry1B = new FingerprintStoreEntry();
            FingerprintStoreSession(storeDirectory, (store) =>
            {
                XAssert.IsTrue(store.TryGetFingerprintStoreEntryBySemiStableHash(pipA.FormattedSemiStableHash, out entry1A));
                XAssert.IsTrue(store.TryGetFingerprintStoreEntryBySemiStableHash(pipB.FormattedSemiStableHash, out entry1B));
            });

            // Trigger miss on pipA
            File.WriteAllText(ArtifactToString(srcA), "asdf");

            RunScheduler().AssertCacheMissWithFingerprintStore(Context.PathTable, Expander, pipA.PipId).AssertCacheHit(pipB.PipId);

            // Trigger hit on pipA from build1
            File.WriteAllText(ArtifactToString(srcA), originalText);
            // Trigger miss on pipB
            File.WriteAllText(ArtifactToString(srcB), "hjkl");

            var build3 = RunScheduler().AssertCacheHit(pipA.PipId).AssertCacheMissWithFingerprintStore(Context.PathTable, Expander, pipB.PipId);

            storeDirectory = ResultToStoreDirectory(build3);
            FingerprintStoreEntry entry3A = new FingerprintStoreEntry(), entry3B = new FingerprintStoreEntry();
            FingerprintStoreSession(storeDirectory, (store) =>
            {
                XAssert.IsTrue(store.TryGetFingerprintStoreEntryBySemiStableHash(pipA.FormattedSemiStableHash, out entry3A));
                XAssert.IsTrue(store.TryGetFingerprintStoreEntryBySemiStableHash(pipB.FormattedSemiStableHash, out entry3B));
            });

            // Hit on pipA
            XAssert.AreEqual(entry1A.PipToFingerprintKeys, entry3A.PipToFingerprintKeys);
            // Miss on pipB
            XAssert.AreNotEqual(entry1B.PipToFingerprintKeys, entry3B.PipToFingerprintKeys);
        }

        /// <summary>
        /// Checks that the fingerprint store retains the entry for the last
        /// recently used fingerprint, even if it was not the last executed.
        /// </summary>
        [Fact]
        public void ReplaceEntryOnOnlyWeakFingerprintHit()
        {
            var dirA = CreateOutputDirectoryArtifact(ReadonlyRoot);
            Directory.CreateDirectory(ArtifactToString(dirA));

            var dirB = CreateOutputDirectoryArtifact(ReadonlyRoot);
            Directory.CreateDirectory(ArtifactToString(dirB));

            var pipA = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.EnumerateDir(dirA),
                Operation.WriteFile(CreateOutputFileArtifact())
            }).Process;

            var srcB = CreateSourceFile();
            var pipB = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.EnumerateDir(dirB),
                Operation.WriteFile(CreateOutputFileArtifact())
            }).Process;

            var build1 = RunScheduler().AssertCacheMissWithFingerprintStore(Context.PathTable, Expander, pipA.PipId, pipB.PipId);

            // Check that the build's logs hold entries for pipA and pipB
            var storeDirectory = ResultToStoreDirectory(build1);
            FingerprintStoreEntry entry1A = new FingerprintStoreEntry(), entry1B = new FingerprintStoreEntry();
            FingerprintStoreSession(storeDirectory, (store) =>
            {
                XAssert.IsTrue(store.TryGetFingerprintStoreEntryBySemiStableHash(pipA.FormattedSemiStableHash, out entry1A));
                XAssert.IsTrue(store.TryGetFingerprintStoreEntryBySemiStableHash(pipB.FormattedSemiStableHash, out entry1B));
            });

            // Trigger strong fingerprint miss on pipA
            var newFileA = CreateSourceFile(ArtifactToString(dirA));
            File.WriteAllText(ArtifactToString(newFileA), "asdf");

            RunScheduler().AssertCacheMissWithFingerprintStore(Context.PathTable, Expander, pipA.PipId).AssertCacheHit(pipB.PipId);

            // Trigger hit on pipA from build1
            File.Delete(ArtifactToString(newFileA));
            // Trigger strong fingerprint miss on pipB
            var newFileB = CreateSourceFile(ArtifactToString(dirB));
            File.WriteAllText(ArtifactToString(newFileB), "hjkl");

            var build3 = RunScheduler().AssertCacheHit(pipA.PipId).AssertCacheMissWithFingerprintStore(Context.PathTable, Expander, pipB.PipId);

            storeDirectory = ResultToStoreDirectory(build3);
            FingerprintStoreEntry entry3A = new FingerprintStoreEntry(), entry3B = new FingerprintStoreEntry();
            FingerprintStoreSession(storeDirectory, (store) =>
            {
                XAssert.IsTrue(store.TryGetFingerprintStoreEntryBySemiStableHash(pipA.FormattedSemiStableHash, out entry3A));
                XAssert.IsTrue(store.TryGetFingerprintStoreEntryBySemiStableHash(pipB.FormattedSemiStableHash, out entry3B));
            });

            // Hit on pipA
            XAssert.AreEqual(entry1A.PipToFingerprintKeys, entry3A.PipToFingerprintKeys);
            // Miss on pipB
            XAssert.AreNotEqual(entry1B.PipToFingerprintKeys, entry3B.PipToFingerprintKeys);
        }

        [FactIfSupported(requiresJournalScan: true)]
        public void TestSkippedPipNoFingerprintIncrementalScheduling()
        {
            Configuration.Schedule.IncrementalScheduling = true;
            Configuration.Schedule.SkipHashSourceFile = false;

            TestSkippedPipNoFingerprint();
        }

        [Fact]
        public void TestSkippedPipNoFingerprint()
        {
            var src = CreateSourceFile();
            var pip = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.ReadFile(src),
                Operation.WriteFile(CreateOutputFileArtifact())
            }).Process;

            // Puts an entry into the fingerprint store
            RunScheduler().AssertCacheMissWithFingerprintStore(Context.PathTable, Expander, pip.PipId);

            // When enabled, incremental scheduling will skip executing clean pip, no store entry from this build
            var build2 = RunScheduler().AssertCacheHit(pip.PipId);

            var storeDirectory = ResultToStoreDirectory(build2);
            FingerprintStoreEntry entry2 = new FingerprintStoreEntry();
            FingerprintStoreSession(storeDirectory, (store) =>
            {
                // Should still be able to retrieve information about skipped pips
                XAssert.IsTrue(store.TryGetFingerprintStoreEntryBySemiStableHash(pip.FormattedSemiStableHash, out entry2));
            });

            // Cause a miss
            File.WriteAllText(ArtifactToString(src), "asfd");
            var build3 = RunScheduler().AssertCacheMissWithFingerprintStore(Context.PathTable, Expander, pip.PipId);

            storeDirectory = ResultToStoreDirectory(build3);
            FingerprintStoreEntry entry3 = new FingerprintStoreEntry();
            FingerprintStoreSession(storeDirectory, (store) =>
            {
                XAssert.IsTrue(store.TryGetFingerprintStoreEntryBySemiStableHash(pip.FormattedSemiStableHash, out entry3));
            });

            // Even when comparing to a build where the pip is skipped, should be able
            // to diff fingerprint information
            XAssert.AreNotEqual(entry2.PipToFingerprintKeys, entry3.PipToFingerprintKeys);
        }


        /// <summary>
        /// Verifies that when a pip fails and exits the build, the observed inputs for the
        /// pip are still logged to execution log targets.
        /// </summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public virtual void EntryExistsForFailedPip(bool readOnlyMount)
        {
            var testRoot = readOnlyMount ? ReadonlyRoot : ObjectRoot;

            var dir = DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniqueDirectory(testRoot));
            var nestedFile = CreateSourceFile(ArtifactToString(dir));

            var sealDirPath = CreateUniqueDirectory();
            var sealDirString = sealDirPath.ToString(Context.PathTable);
            var absentSealDirFile = FileArtifact.CreateSourceFile(CreateUniqueSourcePath(ObjectRootPrefix, sealDirString));
            var nestedSealDirFileForProbe = CreateSourceFile(sealDirString);
            var nestedSealDirFileForRead = CreateSourceFile(sealDirString);

            var upstreamOutput = CreateOutputFileArtifact();
            var upstreamOps = new Operation[]
            {
                Operation.WriteFile(upstreamOutput)
            };

            // Create a pip that does various dynamically observed inputs
            var output = CreateOutputFileArtifact();
            var passingOps = new System.Collections.Generic.List<Operation>
            {
                Operation.ReadFile(upstreamOutput),
                Operation.Probe(absentSealDirFile, doNotInfer: true),
                Operation.Probe(nestedSealDirFileForProbe, doNotInfer: true),
                Operation.ReadFile(nestedSealDirFileForRead, doNotInfer: true),
                Operation.EnumerateDir(dir),
                Operation.WriteFile(output),
            };

            var downstreamOps = new Operation[]
            {
                Operation.ReadFile(output),
                Operation.WriteFile(CreateOutputFileArtifact())
            };

            /*** Passing run ***/
            DirectoryArtifact sealDir = CreateAndScheduleSealDirectoryArtifact(sealDirPath, SealDirectoryKind.SourceAllDirectories);

            var upstreamPip = CreateAndSchedulePipBuilder(upstreamOps).Process;

            var passingPipBuilder = CreatePipBuilder(passingOps);
            passingPipBuilder.AddInputDirectory(sealDir);
            var passingPip = SchedulePipBuilder(passingPipBuilder).Process;

            var downstreamPip = CreateAndSchedulePipBuilder(downstreamOps).Process;

            var passResult = RunScheduler().AssertSuccess();
            var passEntry = default(FingerprintStoreEntry);

            // Get the fingerprints and path set result from a passing run to compare to later
            FingerprintStoreSession(ResultToStoreDirectory(passResult), store =>
            {
                XAssert.IsTrue(store.TryGetFingerprintStoreEntryBySemiStableHash(passingPip.FormattedSemiStableHash, out passEntry));
            });

            /*** Failed run ***/
            ResetPipGraphBuilder();

            upstreamPip = CreateAndSchedulePipBuilder(upstreamOps).Process;

            // Schedule a new pip with the same dynamically observed inputs, but an extra fail operation
            // This will fail the pip without adding any new file system operations
            // Note, the command line is altered which will impact the weak and strong fingerprint, but not the path set
            passingOps.Add(Operation.Fail());

            // Add miscellaneous dynamically observed operations that will be cut
            // off due to pip failure and should not appear in the resulting path set
            passingOps.Add(Operation.Probe(nestedSealDirFileForProbe, doNotInfer: true));
            passingOps.Add(Operation.EnumerateDir(dir));

            sealDir = CreateAndScheduleSealDirectoryArtifact(sealDirPath, SealDirectoryKind.SourceAllDirectories);
            var failingPipBuilder = CreatePipBuilder(passingOps);
            failingPipBuilder.AddInputDirectory(sealDir);
            var failingPip = SchedulePipBuilder(failingPipBuilder).Process;

            downstreamPip = CreateAndSchedulePipBuilder(downstreamOps).Process;
            var failResult = RunScheduler().AssertFailure();
            AssertErrorEventLogged(EventId.PipProcessError);

            // Get the fingerprints and path set result from the failing run
            var failEntry = default(FingerprintStoreEntry);
            FingerprintStoreSession(ResultToStoreDirectory(failResult), store =>
            {
                XAssert.IsTrue(store.TryGetFingerprintStoreEntryBySemiStableHash(passingPip.FormattedSemiStableHash, out failEntry));
            });

            // Note, the weak fingerprints do not match due to the command line change
            // For the sake of comparing the rest of the strong fingerprint inputs, omit the weak fingerprints
            var passSfInputs = passEntry.StrongFingerprintEntry.StrongFingerprintToInputs.Value;
            var failSfInputs = failEntry.StrongFingerprintEntry.StrongFingerprintToInputs.Value;
            passSfInputs = passSfInputs.Replace(passEntry.PipToFingerprintKeys.Value.WeakFingerprint, "");
            failSfInputs = failSfInputs.Replace(failEntry.PipToFingerprintKeys.Value.WeakFingerprint, "");

            // If all the observed inputs from the failed pip were recorded,
            // the strong fingerprint inputs (path set hash + observed inputs) should match
            XAssert.AreEqual(passSfInputs, failSfInputs);
        }

        /// <summary>
        /// Helper function for verifying build results in <see cref="OnlyWriteToCacheLookupStoreOnStrongFingerprintMiss(FingerprintStoreMode)"/>.
        /// </summary>
        private void VerifyNoCacheLookupStore(FingerprintStoreMode mode, CounterCollection<FingerprintStoreCounters> counters, ScheduleRunResult result, Pip pipCacheMiss)
        {
            XAssert.AreEqual(counters.GetCounterValue(FingerprintStoreCounters.NumCacheLookupFingerprintComputationStored), 0);

            if (mode == FingerprintStoreMode.ExecutionFingerprintsOnly)
            {
                XAssert.IsFalse(Directory.Exists(ResultToStoreDirectory(result, cacheLookupStore: true)));
            }
            else
            {
                FingerprintStoreSession(ResultToStoreDirectory(result, cacheLookupStore: true), store =>
                {
                    XAssert.IsFalse(store.TryGetFingerprintStoreEntryBySemiStableHash(pipCacheMiss.FormattedSemiStableHash, out var entry));
                });
            }
        }

        [Theory]
        [InlineData(FingerprintStoreMode.Default)]
        [InlineData(FingerprintStoreMode.ExecutionFingerprintsOnly)]
        public void OnlyWriteToCacheLookupStoreOnStrongFingerprintMiss(FingerprintStoreMode fingerprintStoreMode)
        {
            Configuration.Logging.FingerprintStoreMode = fingerprintStoreMode;
            var testHooks = new SchedulerTestHooks()
            {
                FingerprintStoreTestHooks = new FingerprintStoreTestHooks()
            };

            var srcFile = CreateSourceFile();
            var dir = CreateUniqueDirectoryArtifact(ReadonlyRoot);
            var pip = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.ReadFile(srcFile),
                Operation.EnumerateDir(dir),
                Operation.WriteFile(CreateOutputFileArtifact()),
            }).Process;

            var build1 = RunScheduler(testHooks).AssertCacheMiss(pip.PipId);
            var counters1 = testHooks.FingerprintStoreTestHooks.Counters;
            // One put in execution fingerprint store
            XAssert.AreEqual(counters1.GetCounterValue(FingerprintStoreCounters.NumPipFingerprintEntriesPut), 1);
            VerifyNoCacheLookupStore(fingerprintStoreMode, counters1, build1, pip);

            var build2 = RunScheduler(testHooks).AssertCacheHit(pip.PipId);

            // Fully cache hit is no puts in either execution or cache lookup fingerprint store
            var counters2 = testHooks.FingerprintStoreTestHooks.Counters;
            XAssert.AreEqual(counters2.GetCounterValue(FingerprintStoreCounters.NumPipFingerprintEntriesPut), 0);
            VerifyNoCacheLookupStore(fingerprintStoreMode, counters2, build2, pip);


            // Cause a weak fingerprint miss
            File.WriteAllText(ArtifactToString(srcFile), "asdf");

            var build3 = RunScheduler(testHooks).AssertCacheMiss(pip.PipId);

            // One put in execution fingerprint store (overwrite)
            var counters3 = testHooks.FingerprintStoreTestHooks.Counters;
            XAssert.AreEqual(counters3.GetCounterValue(FingerprintStoreCounters.NumPipFingerprintEntriesPut), 1);
            VerifyNoCacheLookupStore(fingerprintStoreMode, counters3, build1, pip);

            // Cause a strong fingerprint miss
            CreateSourceFile(ArtifactToString(dir));
            var build4 = RunScheduler(testHooks).AssertCacheMiss(pip.PipId);
            // One put in execution fingerprint store (overwrite), one put in cache lookup fingerprint store
            var counters4 = testHooks.FingerprintStoreTestHooks.Counters;
            if (fingerprintStoreMode == FingerprintStoreMode.ExecutionFingerprintsOnly)
            {
                XAssert.AreEqual(counters4.GetCounterValue(FingerprintStoreCounters.NumPipFingerprintEntriesPut), 1);
                VerifyNoCacheLookupStore(fingerprintStoreMode, counters4, build4, pip);
            }
            else
            {
                XAssert.AreEqual(counters4.GetCounterValue(FingerprintStoreCounters.NumPipFingerprintEntriesPut), 2);
                XAssert.AreEqual(counters4.GetCounterValue(FingerprintStoreCounters.NumCacheLookupFingerprintComputationStored), 1);

                // Cache lookup store should be populated on strong fingerprint misses
                FingerprintStoreSession(ResultToStoreDirectory(build4, cacheLookupStore: true), store =>
                {
                    XAssert.IsTrue(store.TryGetFingerprintStoreEntryBySemiStableHash(pip.FormattedSemiStableHash, out var entry));
                });
            }

            // No persistence build-over-build
            var build5 = RunScheduler(testHooks).AssertCacheHit(pip.PipId);
            var counters5 = testHooks.FingerprintStoreTestHooks.Counters;
            VerifyNoCacheLookupStore(fingerprintStoreMode, counters5, build5, pip);
        }

        [Fact]
        public void RoundTripCacheMissList()
        {
            var cacheMissList = new List<PipCacheMissInfo>();
            foreach (var cacheMissType in (PipCacheMissType[])Enum.GetValues(typeof(PipCacheMissType)))
            {
                var pip = CreateAndSchedulePipBuilder(new Operation[]
                    {
                        Operation.WriteFile(CreateOutputFileArtifact())
                    }).Process;

                cacheMissList.Add(new PipCacheMissInfo
                (
                    pipId: pip.PipId,
                    cacheMissType: cacheMissType
                ));
            }

            using (var fingerprintStore = Open(TemporaryDirectory).Result)
            {
                fingerprintStore.PutCacheMissList(cacheMissList);
            }

            IReadOnlyList<PipCacheMissInfo> returnCacheMisslist = null;
            using (var fingerprintStore = Open(TemporaryDirectory, readOnly: true).Result)
            {
                fingerprintStore.TryGetCacheMissList(out returnCacheMisslist);
            }

            XAssert.AreEqual(returnCacheMisslist.Count, cacheMissList.Count);

            for (int i = 0; i < cacheMissList.Count; ++i)
            {
                XAssert.AreEqual(returnCacheMisslist[i], cacheMissList[i]);
            }
        }

        [Fact]
        public void RoundTripEmptyCacheMissList()
        {
            var cacheMissList = new CacheMissList();

            using (var fingerprintStore = Open(TemporaryDirectory).Result)
            {
                fingerprintStore.PutCacheMissList(cacheMissList);
            }

            using (var fingerprintStore = Open(TemporaryDirectory).Result)
            {
                XAssert.IsTrue(fingerprintStore.TryGetCacheMissList(out var returnCacheMissList));
                XAssert.AreEqual(0, returnCacheMissList.Count);
            }
        }

        [Fact]
        public void RoundTripLruEntriesMap()
        {
            var numEntries = 100;
            var keys = new string[numEntries];
            var lruEntriesMap = new LruEntriesMap();
            for (int i = 0; i < numEntries; ++i)
            {
                keys[i] = Guid.NewGuid().ToString();
                lruEntriesMap.Add(keys[i], DateTime.UtcNow.Ticks);
            }

            using (var fingerprintStore = Open(TemporaryDirectory).Result)
            {
                fingerprintStore.PutLruEntriesMap(lruEntriesMap);
            }

            LruEntriesMap returnLruEntriesMap = null;
            using (var fingerprintStore = Open(TemporaryDirectory).Result)
            {
                fingerprintStore.TryGetLruEntriesMap(out returnLruEntriesMap);
            }

            XAssert.AreEqual(returnLruEntriesMap.Count, lruEntriesMap.Count);

            foreach (var pair in lruEntriesMap)
            {
                XAssert.AreEqual(returnLruEntriesMap[pair.Key], lruEntriesMap[pair.Key]);
            }
        }

        /// <summary>
        /// Make sure the entries in the fingerprint columns are replaced on cache hit/cache miss overwrites,
        /// not putting new entries in and leaving "dangling" entries behind that cannot be removed until garbage collection
        /// age limit is hit.
        /// </summary>
        [Fact]
        public void FingerprintEntriesAreOverwritten()
        {
            var testHooks = new SchedulerTestHooks()
            {
                FingerprintStoreTestHooks = new FingerprintStoreTestHooks()
            };

            var src = CreateSourceFile();
            var pipA = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.ReadFile(src),
                Operation.WriteFile(CreateOutputFileArtifact()),
            }).Process;

            var build1 = RunScheduler(testHooks: testHooks).AssertCacheMiss(pipA.PipId);

            PipFingerprintKeys keys1 = default;
            string wf1 = null, sf1 = null;
            FingerprintStoreSession(ResultToStoreDirectory(build1), store =>
            {
                XAssert.IsTrue(store.TryGetPipFingerprintKeys(pipA.FormattedSemiStableHash, out keys1));
                XAssert.IsTrue(store.TryGetWeakFingerprintValue(pipA.FormattedSemiStableHash, out wf1));
                XAssert.IsTrue(store.TryGetStrongFingerprintValue(pipA.FormattedSemiStableHash, out sf1));
            });

            // Cause cache miss, which will create new weak and strong fingerprints
            File.WriteAllText(ArtifactToString(src), "asdf");

            var build2 = RunScheduler().AssertCacheMiss(pipA.PipId);

            FingerprintStoreSession(ResultToStoreDirectory(build2), store =>
            {
                XAssert.IsTrue(store.TryGetPipFingerprintKeys(pipA.FormattedSemiStableHash, out var keys2));
                XAssert.IsTrue(store.TryGetWeakFingerprintValue(pipA.FormattedSemiStableHash, out var wf2));
                XAssert.IsTrue(store.TryGetStrongFingerprintValue(pipA.FormattedSemiStableHash, out var sf2));

                // The recorded entries for fingerprint input values should have been overwritten for pipA
                XAssert.AreNotEqual(wf1, wf2);
                XAssert.AreNotEqual(sf1, sf2);

                // Even though the weak/strong fingerprint are not used for lookup, they should be kept up to date
                XAssert.AreNotEqual(keys1.WeakFingerprint, keys2.WeakFingerprint);
                XAssert.AreNotEqual(keys1.StrongFingerprint, keys2.StrongFingerprint);
                // Path set hash can be shared between different pips, which is why they are not keyed by pip semistablehash.
                XAssert.AreEqual(keys1.FormattedPathSetHash, keys2.FormattedPathSetHash);
            });
        }

        /// <summary>
        /// This test relies on preventing a file from being deleted to cause the <see cref="FingerprintStore"/>'s recovery mechanism to fail.
        /// UNIX delete is able to delete files with open filestreams, so this test will fail on UNIX.
        /// </summary>
        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void FingerprintStoreUnableToOpenDoesntFailBuild()
        {
            var pip = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.WriteFile(CreateOutputFileArtifact())
            }).Process;

            var build1 = RunScheduler().AssertSuccess();

            var engineCacheStore = build1.Config.Layout.FingerprintStoreDirectory.ToString(Context.PathTable);

            // "Corrupt" the store by deleting some files, including the versioning file
            FileUtilities.EnumerateDirectoryEntries(engineCacheStore, (file, attributes) =>
            {
                if (!file.EndsWith(".sst"))
                {
                    File.Delete(Path.Combine(engineCacheStore, file));
                }
            });

            // Put a random file in store location to make it impossible to delete the directory
            var dontDeleteFile = Path.Combine(engineCacheStore, "dontdelete");
            File.WriteAllText(dontDeleteFile, "asdf");
            // An invalid format version causes the fingerprint store to delete itself before starting a new one
            // The open filestream will prevent the directory from being deleted and prevent a new store from being created
            // The build should continue without error without the fingerprint store
            using (var fileStream = new FileStream(dontDeleteFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var build2 = RunScheduler().AssertSuccess();
                XAssert.IsTrue(File.Exists(dontDeleteFile));
                AssertWarningEventLogged(LogEventId.FingerprintStoreUnableToOpen);
            }
        }

        /// <summary>
        /// Wrapper for <see cref="MultiThreadedMultipleDisposeTest"/> that can catch and log a native exception.
        /// </summary>
        [Fact]
        [System.Runtime.ExceptionServices.HandleProcessCorruptedStateExceptions] // allow this test to catch native rocksdb errors
        public void MultiThreadedMultipleDisposeWrapper()
        {
            try
            {
                MultiThreadedMultipleDisposeTest();
            }
            catch (Xunit.Sdk.TrueException)
            {
                // If the manager layer saw an exception, an XAssert will fail and reach here
                throw;
            }
            catch (Exception ex)
            {
                XAssert.Fail("Native exception was seen that would have taken down the process without logging. Exception: " + ex.ToString());
            }

        }

        public void MultiThreadedMultipleDisposeTest()
        {
            try
            {
                int writes = 0;
                var longString1 = string.Concat(Enumerable.Repeat("asdf", 800000));
                using (var accessor = KeyValueStoreAccessor.Open(TemporaryDirectory).Result)
                {
                    var cancelReadWrites = new CancellationTokenSource();
                    var cancelDispose = new CancellationTokenSource();
                    var tLargeWrites = new Task(() =>
                    {
                        XAssert.IsTrue(accessor.Use(store =>
                        {
                            var value = longString1;
                            store.Put(writes.ToString(), value);
                            writes++;
                        }).Succeeded);
                    });

                    var tWrites = new Task(() =>
                    {
                        XAssert.IsTrue(accessor.Use(store =>
                        {
                            while (!cancelReadWrites.IsCancellationRequested)
                            {
                                store.Put(writes.ToString(), Guid.NewGuid().ToString());
                                writes++;
                                Thread.Sleep(10);
                            }
                        }).Succeeded);
                    });

                    var tReads = new Task(() =>
                    {
                        XAssert.IsTrue(accessor.Use(store =>
                        {
                            for (var count = 0; !cancelReadWrites.IsCancellationRequested; count++)
                            {
                                store.TryGetValue(count.ToString(), out var value);
                                Thread.Sleep(10);
                            }
                        }).Succeeded);
                    });

                    var tDispose = new Task(() =>
                    {
                        while (!cancelDispose.IsCancellationRequested)
                        {
                            Thread.Sleep(10);
                            accessor.Dispose();
                        }
                    });

                    tLargeWrites.Start();
                    tWrites.Start();
                    tReads.Start();
                    tDispose.Start();

                    Thread.Sleep(1000);

                    cancelReadWrites.Cancel();
                    cancelDispose.Cancel();
                }
            }
            catch (Exception ex)
            {
                XAssert.Fail("Managed layer exception: " + ex.ToString());
            }

        }

        [Fact]
        public void TestFingerprintStoreModeIgnoreExistingEntries()
        {
            var dir = CreateOutputDirectoryArtifact(ReadonlyRoot);
            Directory.CreateDirectory(ArtifactToString(dir));

            var nestedFile = CreateOutputFileArtifact(ArtifactToString(dir));
            File.WriteAllText(ArtifactToString(nestedFile), "nestedFile");

            var pip = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.EnumerateDir(dir),
                Operation.WriteFile(CreateOutputFileArtifact())
            }).Process;

            // Use a test hook to capture fingerprint store counters
            var testHooks = new SchedulerTestHooks
            {
                FingerprintStoreTestHooks = new FingerprintStoreTestHooks()
            };

            var build1 = RunScheduler(testHooks);
            var counters1 = testHooks.FingerprintStoreTestHooks.Counters;

            Configuration.Logging.FingerprintStoreMode = BuildXLConfiguration.FingerprintStoreMode.IgnoreExistingEntries;

            var build2 = RunScheduler(testHooks);
            var counters2 = testHooks.FingerprintStoreTestHooks.Counters;

            // Sanity checks
            XAssert.AreEqual(1, counters1.GetCounterValue(FingerprintStoreCounters.NumPipFingerprintEntriesPut));
            XAssert.AreEqual(0, counters2.GetCounterValue(FingerprintStoreCounters.NumFingerprintComputationSkippedSameValueEntryExists));

            XAssert.IsFalse(ReferenceEquals(counters1, counters2));

            // Number of entries put in each build should be the same because build2 was told to ignore all existing entries
            XAssert.AreEqual(counters1.GetCounterValue(FingerprintStoreCounters.NumDirectoryMembershipEntriesPut),
                counters2.GetCounterValue(FingerprintStoreCounters.NumDirectoryMembershipEntriesPut));
            XAssert.AreEqual(counters1.GetCounterValue(FingerprintStoreCounters.NumPathSetEntriesPut),
                counters2.GetCounterValue(FingerprintStoreCounters.NumPathSetEntriesPut));
            XAssert.AreEqual(counters1.GetCounterValue(FingerprintStoreCounters.NumPipUniqueOutputHashEntriesPut),
                counters2.GetCounterValue(FingerprintStoreCounters.NumPipUniqueOutputHashEntriesPut));
            XAssert.AreEqual(counters1.GetCounterValue(FingerprintStoreCounters.NumPipFingerprintEntriesPut),
                counters2.GetCounterValue(FingerprintStoreCounters.NumPipFingerprintEntriesPut));
        }

        private string ResultToStoreDirectory(ScheduleRunResult result, bool cacheLookupStore = false)
        {
            return cacheLookupStore ? result.Config.Logging.CacheLookupFingerprintStoreLogDirectory.ToString(Context.PathTable) : result.Config.Logging.ExecutionFingerprintStoreLogDirectory.ToString(Context.PathTable);
        }

        /// <summary>
        /// Matches the string representation of <see cref="FileOrDirectoryArtifact"/> used by the fingerprint store
        /// when serializing to JSON.
        /// </summary>
        private string ArtifactToPrint(FileOrDirectoryArtifact artifact)
        {
            return Expander.ExpandPath(Context.PathTable, artifact.Path).ToLowerInvariant();
        }

        /// <summary>
        /// Encapsulates one "session" with a fingerprint store.
        /// </summary>
        /// <param name="storeDirectory">
        /// Directory of the fingerprint store.
        /// </param>
        /// <param name="storeOps">
        /// The store operations to execute.
        /// </param>
        public static CounterCollection<FingerprintStoreCounters> FingerprintStoreSession(string storeDirectory, Action<FingerprintStoreClass> storeOps, bool readOnly = true, FingerprintStoreTestHooks testHooks = null)
        {
            using (var fingerprintStore = Open(storeDirectory, readOnly: readOnly, testHooks: testHooks).Result)
            {
                storeOps(fingerprintStore);
                return fingerprintStore.Counters;
            }
        }
    }

    /// <summary>
    /// Extensions for <see cref="ScheduleRunResult"/> for <see cref="FingerprintStoreTests"/>.
    /// </summary>
    public static class ScheduleRunResultFingerprintStoreExtensions
    {
        /// <summary>
        /// Verifies that a cache misses appear in the <see cref="FingerprintStore"/> cache miss list after a scheduler run and
        /// that the corresponding <see cref="FingerprintStoreEntry"/> is discoverable by pip semistable hash and pip unique output hash.
        /// </summary>
        public static ScheduleRunResult AssertCacheMissWithFingerprintStore(this ScheduleRunResult result, PathTable pathTable, PathExpander pathExpander, params PipId[] pipIds)
        {
            var misses = new HashSet<PipId>();
            FingerprintStoreTests.FingerprintStoreSession(result.Config.Logging.ExecutionFingerprintStoreLogDirectory.ToString(pathTable), store =>
            {
                XAssert.IsTrue(store.TryGetCacheMissList(out var cacheMissList));
                foreach (var miss in cacheMissList)
                {
                    misses.Add(miss.PipId);
                    var pip = result.Graph.PipTable.HydratePip(miss.PipId, PipQueryContext.Test);

                    XAssert.IsTrue(store.TryGetFingerprintStoreEntryBySemiStableHash(pip.FormattedSemiStableHash, out var entryA));

                    XAssert.IsTrue((pip as Process).TryComputePipUniqueOutputHash(pathTable, out var pipUniqueOutputHash, pathExpander));
                    XAssert.IsTrue(store.TryGetFingerprintStoreEntryByPipUniqueOutputHash(pipUniqueOutputHash.ToString(), out var entryB));

                    XAssert.AreEqual(entryA.ToString(), entryB.ToString());
                }
            });

            foreach (var pipId in pipIds)
            {
                XAssert.IsTrue(misses.Contains(pipId));
            }

            return result.AssertCacheMiss(pipIds);
        }
    }
}
