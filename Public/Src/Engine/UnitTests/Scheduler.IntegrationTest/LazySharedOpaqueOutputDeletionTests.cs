// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Linq;
using BuildXL.Engine.Tracing;
using BuildXL.Pips;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Filter;
using BuildXL.Pips.Operations;
using BuildXL.Processes.Sideband;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using SidebandIntegrityCheckFailReason = BuildXL.Engine.SidebandExaminer.SidebandIntegrityCheckFailReason;

namespace IntegrationTest.BuildXL.Scheduler
{
    [Trait("Category", "SharedOpaqueDirectoryTests")]
    [Feature(Features.SharedOpaqueDirectory)]
    public class LazySharedOpaqueOutputDeletionTests : SchedulerIntegrationTestBase
    {
        public LazySharedOpaqueOutputDeletionTests(ITestOutputHelper output) : base(output)
        {
            Configuration.Schedule.UnsafeLazySODeletion = true;
        }

        [Fact]
        public void FilesAreScrubbed()
        {
            var sharedOpaqueDir = Path.Combine(ObjectRoot, $"sod-{nameof(TestPipRemovedFromGraph)}");
            var sourceA = CreateSourceFile();

            var outputCount = 10;
            var sodOutA = Enumerable.Range(0, outputCount).Select(i => CreateOutputFileArtifact(sharedOpaqueDir, prefix: $"PipA_{i}")).ToArray();

            var pipBuilderA = CreateSharedOpaqueProducer(sharedOpaqueDir, FileArtifact.Invalid, sourceA, filesToProduceDynamically: sodOutA);
            var pipA1 = SchedulePipBuilder(pipBuilderA);

            RunScheduler().AssertCacheMiss(pipA1.Process.PipId);

            AssertSharedOpaqueOutputDeletionNotPostponed();

            // Force cache miss
            File.WriteAllText(ToString(sourceA), "New content");
            RunScheduler().AssertCacheMiss(pipA1.Process.PipId);

            AssertSharedOpaqueOutputDeletionPostponed(numPipsLazilyScrubbed: 1);

            // The count is in logged message SharedOpaqueOutputsDeletedLazily 
            AssertLogContains(true, $"Lazily deleted {outputCount} shared opaque output files");
        }

        [Fact]
        public void TestBasicCaching()
        {
            // Setup: PipA => sharedOpaqueDir => PipB
            var sharedOpaqueDir = Path.Combine(ObjectRoot, $"sod-{nameof(TestBasicCaching)}");
            var outputInSharedOpaque = CreateOutputFileArtifact(sharedOpaqueDir);
            var source = CreateSourceFile();

            var pipA = CreateAndScheduleSharedOpaqueProducer(
                sharedOpaqueDir, 
                fileToProduceStatically: FileArtifact.Invalid, 
                sourceFileToRead: source,
                filesToProduceDynamically: outputInSharedOpaque);

            var builderB = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(outputInSharedOpaque, doNotInfer:true),
                Operation.WriteFile(CreateOutputFileArtifact())
            });

            builderB.AddInputDirectory(pipA.ProcessOutputs.GetOpaqueDirectory(ToPath(sharedOpaqueDir)));
            var pipB = SchedulePipBuilder(builderB);

            // B should be able to consume the file in the opaque directory.
            var result = RunScheduler().AssertCacheMiss(pipA.Process.PipId, pipB.Process.PipId);
            AssertWritesJournaled(result, pipA, outputInSharedOpaque);
            AssertSharedOpaqueOutputDeletionNotPostponed();
            XAssert.FileExists(ToString(outputInSharedOpaque));

            // Second build should have both cache hits; deletions should be postponed
            RunScheduler().AssertCacheHit(pipA.Process.PipId, pipB.Process.PipId);
            AssertSharedOpaqueOutputDeletionPostponed(numPipsLazilyScrubbed: 0);
            XAssert.FileExists(ToString(outputInSharedOpaque));

            // Make sure we can replay the file in the opaque directory; still cache hits and deletions should be postponed
            File.Delete(ToString(outputInSharedOpaque));
            RunScheduler().AssertCacheHit(pipA.Process.PipId, pipB.Process.PipId);
            AssertSharedOpaqueOutputDeletionPostponed(numPipsLazilyScrubbed: 0);
            XAssert.FileExists(ToString(outputInSharedOpaque));

            // Modify the input and make sure both are rerun, deletions should still be postponed
            File.WriteAllText(ToString(source), "New content");
            RunScheduler().AssertCacheMiss(pipA.Process.PipId, pipB.Process.PipId);
            AssertSharedOpaqueOutputDeletionPostponed(numPipsLazilyScrubbed: 1);
        }

        [Fact]
        public void CacheMissBecauseOfEmptyDirectoriesNotScrubbed()
        {
            // This test explains why lazy scrubbing causes cache misses
            // when the pip enumerates the root shared opaque and then produces some content under it

            // The pip will
            //   1) enumerate SOD 
            //   2) produce   SOD\A\output.out
            var sharedOpaqueDir = Path.Combine(ObjectRoot, $"sod-{nameof(CacheMissBecauseOfEmptyDirectoriesNotScrubbed)}");
            var sharedOpaqueNestedDirOutput = Path.Combine(sharedOpaqueDir, "A");
            var outputInNestedDir = CreateOutputFileArtifact(sharedOpaqueNestedDirOutput);

            var builder = CreatePipBuilder(new Operation[]
            {
                // This enumeration of the SOD will cause the cache miss
                Operation.EnumerateDir(DirectoryArtifact.CreateWithZeroPartialSealId(ToPath(sharedOpaqueDir))),

                Operation.WriteFile(outputInNestedDir, doNotInfer: true),
                Operation.WriteFile(CreateOutputFileArtifact())
            });

            builder.AddOutputDirectory(ToPath(sharedOpaqueDir), SealDirectoryKind.SharedOpaque);
            builder.Options |= Process.Options.AllowUndeclaredSourceReads;
            var pip = SchedulePipBuilder(builder);

            // Before the process runs for the first time buildxl prepares the directory output, which will be empty 
            // So the enumeration that the process does will be of an empty directory.
            RunScheduler().AssertSuccess();
            AssertSharedOpaqueOutputDeletionNotPostponed();

            // After the first run, here's what the file system looks like:
            //
            //     sod
            //        \ A
            //           \ file.out
            //
            // In cache lookup, the strong fingerprint doesn't match
            // because sod is not empty, but the observation we have is for an "empty directory enumeration" 
            RunScheduler().AssertCacheMiss(pip.Process.PipId);
            AssertSharedOpaqueOutputDeletionPostponed(numPipsLazilyScrubbed: 1);

            // Next run will be a cache hit, because the second run enumerated the directory
            // and the empty directory sod\A was not deleted by the lazy scrubber, 
            // so the strong fingerprint will match the one of the second run.
            RunScheduler().AssertCacheHit(pip.Process.PipId);
        }

        /// <remarks>
        /// This test goes back to bug #1838467
        /// </remarks>
        [Fact]
        public void SidebandPathsAreConsideredAbsentByInputProcessor()
        {
            // We can have an observation of a path initially classified as a file read (i.e., ObservationFlags.None).
            // This doesn't mean that the file was there, rather that the process called an API to read the file in that path.
            //
            // If we do not reclassify the probe as absent, with lazy scrubbing of shared opaques we can get confused because:
            //  1. the actual existence of the file at cache lookup time is "Present",
            //  2. the file will be absent at execution time due to scrubbing (and potentially not produced!).
            // 
            // Because path existence is recorded at cache-lookup time, we would decide that the file is present
            // when it actually isn't, assume the file was actually read, and crash.
            // 
            // This test reproduces a scenario where a pip is non-deterministic w/r to outputs, and 
            // causes this discrepancy in observations
            //
            // This was actually observed in practice (see bug #1838467)

            // We will control the pip non-determinism with this untracked file
            string untracked = Path.Combine(ObjectRoot, "untracked.txt");

            // Pip has opaqueDir as a shared opaque output
            // Let's make the pip produce one of two outputs "non-deterministically":
            //  Scenario A: writes opaqueDir\subdirA\write-if-A.out
            //  Scenario B: writes opaqueDir\subdirB\write-if-B.out
            AbsolutePath opaqueDirPath = AbsolutePath.Create(Context.PathTable, Path.Combine(ObjectRoot, "opaquedir"));
            AbsolutePath nestedPathA = AbsolutePath.Create(Context.PathTable, Path.Combine(ObjectRoot, "opaquedir", "subdirA"));
            AbsolutePath nestedPathB = AbsolutePath.Create(Context.PathTable, Path.Combine(ObjectRoot, "opaquedir", "subdirB"));

            var fileA = CreateOutputFileArtifact(nestedPathA, prefix: "write-if-A");
            var fileB = CreateOutputFileArtifact(nestedPathB, prefix: "write-if-B");

            var builderA = CreatePipBuilder(new Operation[]
            {
                // Enumerate the upper shared opaque directory first
                // This in an empty directory in a clean run, and its non-scrubbed contents changing will cause cache misses
                Operation.EnumerateDir(DirectoryArtifact.CreateWithZeroPartialSealId(opaqueDirPath)), 
                
                Operation.ReadFile(fileA, doNotInfer: true), // Absent "file content read" - this has ObservationFlags.None
                Operation.ReadFile(fileB, doNotInfer: true), // Absent "file content read" - this has ObservationFlags.None

                Operation.WriteFileIfInputEqual(fileA, untracked, "A", "deterministic-content"), // Scenario A
                Operation.WriteFileIfInputEqual(fileB, untracked, "B", "deterministic-content"), // Scenario B
            });

            builderA.AddOutputDirectory(opaqueDirPath, SealDirectoryKind.SharedOpaque);
            builderA.AddUntrackedFile(AbsolutePath.Create(Context.PathTable, untracked));

            builderA.Options |= Process.Options.AllowUndeclaredSourceReads;

            var pipA = SchedulePipBuilder(builderA);

            // Run 1.
            // Exercise scenario A
            File.WriteAllText(untracked, "A");
            RunScheduler().AssertSuccess();
            AssertSharedOpaqueOutputDeletionNotPostponed(); // (First run - no lazy deletions)


            // Run 2.
            // We will exercise scenario B for the pip
            File.WriteAllText(untracked, "B");

            // After the first run, here's what the file system looks like:
            //
            //     opaqueDir
            //        \ subdirA
            //             \ fileA.out
            //
            // 
            // We have these observations for the pip:
            //       Observations1 = [
            //                    opaqueDir: AbsentPathProbe (empty directory enumeration),
            //                    opaqueDir\subdirB\fileB.out : ObservationFlags.None (file was absent for a read operation)
            //                  ]
            // 
            // Because lazy scrubbing is on, when in cache lookup we compute the strong fingerprint 
            // we now see that opaqueDir contains subdirA. This causes a cache miss, because the enumeration
            // that the pip did in the first run results in an empty directory.
            RunScheduler().AssertCacheMiss(pipA.Process.PipId);
            AssertSharedOpaqueOutputDeletionPostponed(numPipsLazilyScrubbed: 1);

            // After the second run, a new pathset is added for subsequent cache lookups based on observations2:
            //
            //       Observations1 = [
            //                         opaqueDir: AbsentPathProbe (empty dir enumeration),
            //                         opaqueDir\subdirB\fileB.out : ObservationFlags.None  (file was absent for a read operation)
            //                       ]
            //                  
            //       Observations2 = [
            //                         opaqueDir: Enumeration {contents: subdirA},
            //                         opaqueDir\subdirA\fileA.out : ObservationFlags.None  (file was absent for a read operation)
            //                       ]
            //                      

            // Run 3.
            // Switch back to scenario A
            File.WriteAllText(untracked, "A");

            // After the second run, here's what the file system looks like:
            //
            //     opaqueDir
            //        \ subdirA             (empty dir not scrubbed by lazy scrubber)
            //        \ subdirB
            //             \ fileB.out
            //
            // Because lazy scrubbing is on, when in cache lookup we compute the strong fingerprint 
            // we now observe that an enumeration to opaqueDir results in  {subdirA, subdirB}.
            // This causes a cache miss (refer to the observations 1 and 2 above)
            //
            // Additionally, opaqueDir\subdirB\fileB.out is present in the file system while computing strong fingerprints
            // (it will be lazily scrubbed just before pip execution).
            //
            // Because this file is relevant for cache lookup (pathset 1 contains it) the file is probed by OIP
            // while computing fingerprints, and so its path existence is cached as ExistsAsFile in the PathExistenceCache.
            //
            // Now, the pip will run, and because we are in scenario A:
            //  - we will have an observation for fileB with ObservationFlags.None (see the Operation.ReadFile in the pip builder)
            //  - fileB will NOT be produced
            // 
            // In the ObservedInputProcessor post-execution processing this happens:
            //   - fileB had an observation with ObservationFlags.None
            //   - fileB is in recorded in the sideband state, because it was produces in the last run
            //  So we will reclassify the observation as an absent probe.
            //
            //  Without this reclassification, we would assume that fileB exists after checking the PathExistenceCache
            //  This makes us characterize the access as a FileContentRead (refer to ObservedInputProcessor.MapPathExistenceToObservedInputType)
            //  But the FileContentManager doesn't have a FileContentInfo for fileB, because it was never produced in this run.
            RunScheduler().AssertCacheMiss(pipA.Process.PipId);
        }

        [Fact]
        public void TestSidebandFileProducedUponCacheHits()
        {
            // Setup: PipA => sharedOpaqueDir
            var sharedOpaqueDir = Path.Combine(ObjectRoot, $"sod-{nameof(TestSidebandFileProducedUponCacheHits)}");
            var outputInSharedOpaque = CreateOutputFileArtifact(sharedOpaqueDir);

            var pipA = CreateAndScheduleSharedOpaqueProducer(sharedOpaqueDir, filesToProduceDynamically: outputInSharedOpaque);

            // first build: cache miss, deletions not postponed, writes are journaled
            var result = RunScheduler().AssertCacheMiss(pipA.Process.PipId);
            AssertWritesJournaled(result, pipA, outputInSharedOpaque);
            AssertSharedOpaqueOutputDeletionNotPostponed();
            XAssert.FileExists(ToString(outputInSharedOpaque));

            // second build: cache hit, deletions be postponed, writes journaled
            result = RunScheduler().AssertCacheHit(pipA.Process.PipId);
            AssertWritesJournaled(result, pipA, outputInSharedOpaque);
            AssertSharedOpaqueOutputDeletionPostponed(numPipsLazilyScrubbed: 0);
            XAssert.FileExists(ToString(outputInSharedOpaque));

            // delete sideband file and build again: 
            //   => cache hit
            //   => deletions not postponed (because sideband is missing)
            //   => writes are still journaled even on cache hits
            AssertDeleteFile(GetSidebandFile(result, pipA.Process));
            result = RunScheduler().AssertCacheHit(pipA.Process.PipId);
            AssertWritesJournaled(result, pipA, outputInSharedOpaque);
            AssertSharedOpaqueOutputDeletionNotPostponed();
            XAssert.FileExists(ToString(outputInSharedOpaque));
        }

        [Fact]
        public void TestMultipleProducers()
        {
            // Setup: PipA, PipB => sharedOpaqueDir
            var sharedOpaqueDir = Path.Combine(ObjectRoot, $"sod-{nameof(TestMultipleProducers)}");
            var sodOutA = CreateOutputFileArtifact(sharedOpaqueDir, prefix: "PipA");
            var sodOutB = CreateOutputFileArtifact(sharedOpaqueDir, prefix: "PipB");
            var sourceA = CreateSourceFile();
            var sourceB = CreateSourceFile();

            var pipA = CreateAndScheduleSharedOpaqueProducer(
                sharedOpaqueDir, 
                fileToProduceStatically: FileArtifact.Invalid, 
                sourceFileToRead: sourceA,
                filesToProduceDynamically: sodOutA);

            var pipB = CreateAndScheduleSharedOpaqueProducer(
                sharedOpaqueDir,
                fileToProduceStatically: FileArtifact.Invalid,
                sourceFileToRead: sourceB,
                filesToProduceDynamically: sodOutB);

            RunScheduler().AssertCacheMiss(pipA.Process.PipId, pipB.Process.PipId);
            AssertSharedOpaqueOutputDeletionNotPostponed();

            // invalidate pipA only
            File.WriteAllText(ToString(sourceA), "New content");
            RunScheduler().AssertCacheMiss(pipA.Process.PipId).AssertCacheHit(pipB.Process.PipId);
            AssertSharedOpaqueOutputDeletionPostponed(numPipsLazilyScrubbed: 1);

            // invalidate pipB only
            File.WriteAllText(ToString(sourceB), "New content");
            RunScheduler().AssertCacheMiss(pipB.Process.PipId).AssertCacheHit(pipA.Process.PipId);
            AssertSharedOpaqueOutputDeletionPostponed(numPipsLazilyScrubbed: 1);

            // invalidate both
            File.WriteAllText(ToString(sourceA), "New content 2");
            File.WriteAllText(ToString(sourceB), "New content 2");
            RunScheduler().AssertCacheMiss(pipA.Process.PipId, pipB.Process.PipId);
            AssertSharedOpaqueOutputDeletionPostponed(numPipsLazilyScrubbed: 2);
        }

        [Fact]
        public void TestPipAddedToGraphForcesEagerDeletion()
        {
            Configuration.Engine.AllowDuplicateTemporaryDirectory = true;
            // Setup: PipA => sharedOpaqueDir; reset; reschedule PipA and add PipB => sharedOpaqueDir
            var sharedOpaqueDir = Path.Combine(ObjectRoot, $"sod-{nameof(TestPipAddedToGraphForcesEagerDeletion)}");
            var sodOutA = CreateOutputFileArtifact(sharedOpaqueDir, prefix: "PipA");
            var pipABuilder = CreateSharedOpaqueProducer(sharedOpaqueDir, filesToProduceDynamically: sodOutA);

            var pipA1 = SchedulePipBuilder(pipABuilder);
            RunScheduler().AssertCacheMiss(pipA1.Process.PipId);
            AssertSharedOpaqueOutputDeletionNotPostponed();

            // reset
            ResetPipGraphBuilder();

            // reschedule pipA and add pipB
            var pipA2 = SchedulePipBuilder(pipABuilder);
            var sodOutB = CreateOutputFileArtifact(sharedOpaqueDir, prefix: "PipB");
            var pipB = CreateAndScheduleSharedOpaqueProducer(sharedOpaqueDir, filesToProduceDynamically: sodOutB);

            // pipB has been added to the graph => since we don't have any sideband info about it,
            // we don't know if the sideband file is missing or what so we cannot confidently postpone output deletion.
            RunScheduler().AssertCacheHit(pipA2.Process.PipId).AssertCacheMiss(pipB.Process.PipId);
            AssertSharedOpaqueOutputDeletionNotPostponed();
        }

        [Theory]
        [MemberData(nameof(TruthTable.GetTable), 1, MemberType = typeof(TruthTable))]
        public void TestPipRemovedFromGraph(bool invalidatePipA)
        {
            // Setup: PipA, PipB => sharedOpaqueDir; reset; reschedule only PipA
            var sharedOpaqueDir = Path.Combine(ObjectRoot, $"sod-{nameof(TestPipRemovedFromGraph)}");
            var sodOutA = CreateOutputFileArtifact(sharedOpaqueDir, prefix: "PipA");
            var sourceA = CreateSourceFile();
            var pipBuilderA = CreateSharedOpaqueProducer(sharedOpaqueDir, FileArtifact.Invalid, sourceA, filesToProduceDynamically: sodOutA);
            var sodOutB = CreateOutputFileArtifact(sharedOpaqueDir, prefix: "PipB");
            var pipBuilderB = CreateSharedOpaqueProducer(sharedOpaqueDir, filesToProduceDynamically: sodOutB);

            var pipA1 = SchedulePipBuilder(pipBuilderA);
            var pipB1 = SchedulePipBuilder(pipBuilderB);
            RunScheduler().AssertCacheMiss(pipA1.Process.PipId, pipB1.Process.PipId);
            AssertSharedOpaqueOutputDeletionNotPostponed();

            // reset
            ResetPipGraphBuilder();

            // reschedule only pipA
            if (invalidatePipA)
            {
                File.WriteAllText(ToString(sourceA), "New content");
            }
            var pipA2 = SchedulePipBuilder(pipBuilderA);

            // pipB has been removed from the graph
            //   => its outputs should be deleted before execution 
            //   => for pipA delayed deletion should still be enabled
            var result = RunScheduler();
            AssertSharedOpaqueOutputDeletionPostponed(numPipsLazilyScrubbed: invalidatePipA ? 1 : 0);
            AssertInformationalEventLogged(LogEventId.DeletingOutputsFromExtraneousSidebandFilesStarted, count: 1);
            XAssert.FileDoesNotExist(ToString(sodOutB));
            if (invalidatePipA)
            {
                result.AssertCacheMiss(pipA2.Process.PipId);
            }
            else
            {
                result.AssertCacheHit(pipA2.Process.PipId);
            }
        }

        [Theory]
        [InlineData(SidebandIntegrityCheckFailReason.FileNotFound)]
        [InlineData(SidebandIntegrityCheckFailReason.ChecksumMismatch)]
        [InlineData(SidebandIntegrityCheckFailReason.MetadataMismatch)]
        public void TestSidebandIntegrityCheckFail(SidebandIntegrityCheckFailReason kind)
        {
            // Setup: PipA => sharedOpaqueDir; invalidate PipA's sideband file afterwards
            var sharedOpaqueDir = Path.Combine(ObjectRoot, $"sod-{nameof(TestSidebandIntegrityCheckFail)}");

            var pipA = CreateAndScheduleSharedOpaqueProducer(sharedOpaqueDir, filesToProduceDynamically: CreateOutputFileArtifact(sharedOpaqueDir, prefix: "PipA"));

            var result = RunScheduler().AssertCacheMiss(pipA.Process.PipId);
            AssertSharedOpaqueOutputDeletionNotPostponed();
            AssertVerboseEventLogged(LogEventId.SidebandIntegrityCheckForProcessFailed);

            // invalidate sideband file and run again
            InvalidateSidebandFile(GetSidebandFile(result, pipA.Process), kind);
            RunScheduler().AssertCacheHit(pipA.Process.PipId);
            AssertSharedOpaqueOutputDeletionNotPostponed();
            AssertVerboseEventLogged(LogEventId.SidebandIntegrityCheckForProcessFailed);

            if (kind == SidebandIntegrityCheckFailReason.ChecksumMismatch)
            {
                AssertWarningEventLogged(global::BuildXL.Processes.Tracing.LogEventId.CannotReadSidebandFileWarning);
            }
        }

        [Fact]
        public void TestPipStaticFingerprintChangeInvalidatesSideband()
        {
            // Setup: PipA => sharedOpaqueDir; reset; reschedule PipA with an extra source dependency
            var sharedOpaqueDir = Path.Combine(ObjectRoot, $"sod-{nameof(TestPipStaticFingerprintChangeInvalidatesSideband)}");

            var sourceA1 = CreateSourceFile(root: SourceRootPath, prefix: "sourceA1");
            var sourceA2 = CreateSourceFile(root: SourceRootPath, prefix: "sourceA2");
            var sodOutA = CreateOutputFileArtifact(sharedOpaqueDir, prefix: "PipA");
            var pipA1 = CreateAndScheduleSharedOpaqueProducer(sharedOpaqueDir, FileArtifact.Invalid, sourceA1, filesToProduceDynamically: sodOutA);

            var result = RunScheduler().AssertCacheMiss(pipA1.Process.PipId);
            AssertSharedOpaqueOutputDeletionNotPostponed();
            AssertVerboseEventLogged(LogEventId.SidebandIntegrityCheckForProcessFailed);

            // reset
            ResetPipGraphBuilder();

            // reschedule pipA with a different source dependency 
            //   => assert semistable hash stayed the same
            //   => assert integrity check failed because metadata (static fingerprint) changed
            var pipA2 = CreateAndScheduleSharedOpaqueProducer(sharedOpaqueDir, FileArtifact.Invalid, sourceA2, filesToProduceDynamically: sodOutA);
            XAssert.AreEqual(pipA1.Process.FormattedSemiStableHash, pipA2.Process.FormattedSemiStableHash);
            RunScheduler().AssertCacheMiss(pipA1.Process.PipId);
            AssertSharedOpaqueOutputDeletionNotPostponed();
            AssertVerboseEventLogged(LogEventId.SidebandIntegrityCheckForProcessFailed);
        }

        [Fact]
        public void TestSemiStableHashColision()
        {
            // Setup: PipA, PipB => sharedOpaqueDir
            var sharedOpaqueDir = Path.Combine(ObjectRoot, $"sod-{nameof(TestSemiStableHashColision)}");

            var pipBuilderA = CreateSharedOpaqueProducer(sharedOpaqueDir, filesToProduceDynamically: CreateOutputFileArtifact(sharedOpaqueDir, prefix: "PipA"));
            var pipBuilderB = CreateSharedOpaqueProducer(sharedOpaqueDir, filesToProduceDynamically: CreateOutputFileArtifact(sharedOpaqueDir, prefix: "PipB"));

            var pipA1 = SchedulePipBuilder(pipBuilderA);
            var pipB1 = SchedulePipBuilder(pipBuilderB);
            RunScheduler().AssertCacheMiss(pipA1.Process.PipId, pipB1.Process.PipId);
            AssertSharedOpaqueOutputDeletionNotPostponed();
            AssertVerboseEventLogged(LogEventId.SidebandIntegrityCheckForProcessFailed);

            // reset
            ResetPipGraphBuilder();

            // add PipA and PipB in the opposite order
            var pipB2 = SchedulePipBuilder(pipBuilderB);
            var pipA2 = SchedulePipBuilder(pipBuilderA);

            // assert that their semistable hashes flipped
            XAssert.AreEqual(pipA1.Process.SemiStableHash, pipB2.Process.SemiStableHash);
            XAssert.AreEqual(pipB1.Process.SemiStableHash, pipA2.Process.SemiStableHash);

            // re-run, assert that deletion was not postponed because sideband integrity check failed
            RunScheduler().AssertCacheMiss(pipA2.Process.PipId, pipB2.Process.PipId);
            AssertSharedOpaqueOutputDeletionNotPostponed();
            AssertVerboseEventLogged(LogEventId.SidebandIntegrityCheckForProcessFailed);
        }

        private const string PipATag = "PipA";
        private const string PipBTag = "PipB";

        [Fact]
        public void TestFiltering()
        {
            // Setup: PipA, PipB => sharedOpaqueDir, use a filter that selects only PipA
            var sharedOpaqueDir = Path.Combine(ObjectRoot, $"sod-{nameof(TestFiltering)}");

            var sourceA = CreateSourceFile();
            var pipBuilderA = CreateSharedOpaqueProducer(sharedOpaqueDir, FileArtifact.Invalid, sourceA, filesToProduceDynamically: CreateOutputFileArtifact(sharedOpaqueDir, prefix: "PipA"));
            var pipBuilderB = CreateSharedOpaqueProducer(sharedOpaqueDir, filesToProduceDynamically: CreateOutputFileArtifact(sharedOpaqueDir, prefix: "PipB"));

            pipBuilderA.AddTags(Context.StringTable, PipATag);
            pipBuilderB.AddTags(Context.StringTable, PipBTag);

            var tagFilter = new TagFilter(StringId.Create(Context.StringTable, PipATag));
            var rootFilter = new RootFilter(tagFilter);

            var pipA = SchedulePipBuilder(pipBuilderA);
            var pipB = SchedulePipBuilder(pipBuilderB);

            RunScheduler(filter: rootFilter)
                .AssertCacheMiss(pipA.Process.PipId)
                .AssertPipResultStatus((pipB.Process.PipId, PipResultStatus.Skipped));
            AssertSharedOpaqueOutputDeletionNotPostponed();
            AssertVerboseEventLogged(LogEventId.SidebandIntegrityCheckForProcessFailed);

            // run again and assert PipA was a cache hit and deletions were postponed
            RunScheduler(filter: rootFilter)
                .AssertCacheHit(pipA.Process.PipId)
                .AssertPipResultStatus((pipB.Process.PipId, PipResultStatus.Skipped));
            AssertSharedOpaqueOutputDeletionPostponed(numPipsLazilyScrubbed: 0);

            // invalidate PipA and run again
            //   => PipA is a cache miss
            //   => deletions are postponed and outputs of PipA are lazily deleted
            File.WriteAllText(ToString(sourceA), "new content");
            RunScheduler(filter: rootFilter)
                .AssertCacheMiss(pipA.Process.PipId)
                .AssertPipResultStatus((pipB.Process.PipId, PipResultStatus.Skipped));
            AssertSharedOpaqueOutputDeletionPostponed(numPipsLazilyScrubbed: 1);
        }

        [Fact]
        public void TestFilteringAfterFullBuild()
        {
            // Setup: PipA, PipB => sharedOpaqueDir, use a filter that selects only PipA
            var sharedOpaqueDir = Path.Combine(ObjectRoot, $"sod-{nameof(TestFilteringAfterFullBuild)}");

            var sourceA = CreateSourceFile();
            var pipBuilderA = CreateSharedOpaqueProducer(sharedOpaqueDir, FileArtifact.Invalid, sourceA, filesToProduceDynamically: CreateOutputFileArtifact(sharedOpaqueDir, prefix: "PipA"));
            var pipBuilderB = CreateSharedOpaqueProducer(sharedOpaqueDir, filesToProduceDynamically: CreateOutputFileArtifact(sharedOpaqueDir, prefix: "PipB"));

            pipBuilderA.AddTags(Context.StringTable, PipATag);
            pipBuilderB.AddTags(Context.StringTable, PipBTag);

            var pipA = SchedulePipBuilder(pipBuilderA);
            var pipB = SchedulePipBuilder(pipBuilderB);

            RunScheduler().AssertCacheMiss(pipA.Process.PipId, pipB.Process.PipId);
            AssertSharedOpaqueOutputDeletionNotPostponed();
            AssertVerboseEventLogged(LogEventId.SidebandIntegrityCheckForProcessFailed);

            var tagFilter = new TagFilter(StringId.Create(Context.StringTable, PipATag));
            var rootFilter = new RootFilter(tagFilter);

            // run again, now with a filter selecting only PipA
            //   => PipA is a cache hit and PipB is skipped
            //   => the sideband file for PipB is deemed extraneous and its recorded paths are deleted
            //   => deletions for PipA are postponed
            RunScheduler(filter: rootFilter)
                .AssertCacheHit(pipA.Process.PipId)
                .AssertPipResultStatus((pipB.Process.PipId, PipResultStatus.Skipped));
            AssertSharedOpaqueOutputDeletionPostponed(numPipsLazilyScrubbed: 0);
            AssertInformationalEventLogged(LogEventId.DeletingOutputsFromExtraneousSidebandFilesStarted, count: 1);

            // invalidate PipA, run again with the same filer
            //   => PipA is a cache miss, PipB is skipped
            //   => deletions for PipA are postponed
            //   => no extraneous sideband files were found
            File.WriteAllText(ToString(sourceA), "new content");
            RunScheduler(filter: rootFilter)
                .AssertCacheMiss(pipA.Process.PipId)
                .AssertPipResultStatus((pipB.Process.PipId, PipResultStatus.Skipped));
            AssertSharedOpaqueOutputDeletionPostponed(numPipsLazilyScrubbed: 1);
            AssertInformationalEventLogged(LogEventId.DeletingOutputsFromExtraneousSidebandFilesStarted, count: 0);
        }

        [Fact]
        public void MissingSidebandForDependencyOfFilteredPip()
        {
            // Setup: PipA -> PipB => sharedOpaqueDir, use a filter that selects only PipB
            var sharedOpaqueDirA = Path.Combine(ObjectRoot, $"sod-{nameof(TestFiltering)}--PipA");
            var explicitOutputA = CreateOutputFileArtifact();
            var sharedOpaqueDirB = Path.Combine(ObjectRoot, $"sod-{nameof(TestFiltering)}--PipB");
            var sourceA = CreateSourceFile();
            var pipBuilderA = CreateSharedOpaqueProducer(sharedOpaqueDirA, explicitOutputA, sourceA, filesToProduceDynamically: CreateOutputFileArtifact(sharedOpaqueDirA, prefix: "PipA"));
            var pipBuilderB = CreateSharedOpaqueProducer(sharedOpaqueDirB, FileArtifact.Invalid, explicitOutputA, filesToProduceDynamically: CreateOutputFileArtifact(sharedOpaqueDirB, prefix: "PipB"));

            pipBuilderA.AddTags(Context.StringTable, PipATag);
            pipBuilderB.AddTags(Context.StringTable, PipBTag);

            var tagFilter = new TagFilter(StringId.Create(Context.StringTable, PipBTag));
            var rootFilter = new RootFilter(tagFilter);

            var pipA = SchedulePipBuilder(pipBuilderA);
            var pipB = SchedulePipBuilder(pipBuilderB);

            // Both pips should be cache misses
            RunScheduler(filter: rootFilter)
                .AssertCacheMiss(pipA.Process.PipId)
                .AssertCacheMiss(pipB.Process.PipId);
            AssertSharedOpaqueOutputDeletionNotPostponed();
            AssertVerboseEventLogged(LogEventId.SidebandIntegrityCheckForProcessFailed);

            // Run again and assert both are hits with deletions postponed
            var result = RunScheduler(filter: rootFilter)
                .AssertCacheHit(pipA.Process.PipId)
                .AssertCacheHit(pipB.Process.PipId);
            AssertSharedOpaqueOutputDeletionPostponed(numPipsLazilyScrubbed: 0);

            // Invalid pipA's sideband file. This should cause deletion to not be postponed
            InvalidateSidebandFile(GetSidebandFile(result, pipA.Process), SidebandIntegrityCheckFailReason.FileNotFound);
            RunScheduler(filter: rootFilter)
                .AssertCacheHit(pipA.Process.PipId)
                .AssertCacheHit(pipB.Process.PipId);
            AssertSharedOpaqueOutputDeletionNotPostponed();
        }

        [Fact]
        public void TestSidebandFileIsAlwaysProducedForPipsWithSharedOpaqueDirectoryOutputs()
        {
            // Setup: PipA => sharedOpaqueDir => PipB
            var sharedOpaqueDir = Path.Combine(ObjectRoot, $"sod-{nameof(TestSidebandFileIsAlwaysProducedForPipsWithSharedOpaqueDirectoryOutputs)}");
            var pipA = CreateAndScheduleSharedOpaqueProducer(
                sharedOpaqueDir,
                fileToProduceStatically: FileArtifact.Invalid,
                sourceFileToRead: CreateSourceFile(),
                filesToProduceDynamically: new FileArtifact[0]);

            var pipB = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.WriteFile(CreateOutputFileArtifact())
            });

            var result = RunScheduler().AssertCacheMiss(pipA.Process.PipId, pipB.Process.PipId);

            XAssert.FileExists(GetSidebandFile(result, pipA.Process), "Sideband file must be produced for all pips with SOD outputs, even when they don't write a single file into SOD");
            XAssert.FileDoesNotExist(GetSidebandFile(result, pipB.Process), "Sideband file should not be produced for pips without any SOD outputs");
        }

        private void InvalidateSidebandFile(string sidebandFile, SidebandIntegrityCheckFailReason kind)
        {
            XAssert.FileExists(sidebandFile, "Sideband file for pipA not found.");
            switch (kind)
            {
                case SidebandIntegrityCheckFailReason.FileNotFound:
                    AssertDeleteFile(sidebandFile, "Could not delete sideband file for pipA.");
                    break;

                case SidebandIntegrityCheckFailReason.ChecksumMismatch:
                    File.WriteAllText(path: sidebandFile, contents: "bogus sideband file");
                    break;

                case SidebandIntegrityCheckFailReason.MetadataMismatch:
                    SidebandMetadata alteredMetadata;
                    // read the header and the metadata from the original
                    using (var reader = new SidebandReader(sidebandFile))
                    {
                        XAssert.IsTrue(reader.ReadHeader(ignoreChecksum: false));
                        var originalMetadata = reader.ReadMetadata();
                        alteredMetadata = new SidebandMetadata(originalMetadata.PipSemiStableHash + 1, originalMetadata.StaticPipFingerprint);
                    }
                    // overwrite the original with a different metadata file where PiPSemiStableHash is different than in the original
                    using (var writer = new SidebandWriter(alteredMetadata, sidebandFile, null))
                    {
                        writer.EnsureHeaderWritten();
                    }
                    break;

                default:
                    XAssert.Fail($"Unknown kind: {kind}");
                    break;
            }
        }

        private void AssertSharedOpaqueOutputDeletionPostponed(int numPipsLazilyScrubbed) => AssertSharedOpaqueOutputDeletion(postponed: true, numPipsLazilyScrubbed);
        private void AssertSharedOpaqueOutputDeletionNotPostponed() => AssertSharedOpaqueOutputDeletion(postponed: false, numPipsLazilyScrubbed: 0);

        private void AssertSharedOpaqueOutputDeletion(bool postponed, int numPipsLazilyScrubbed)
        {
            XAssert.IsTrue(postponed || numPipsLazilyScrubbed == 0);
            AssertInformationalEventLogged(LogEventId.PostponingDeletionOfSharedOpaqueOutputs, count: postponed ? 1 : 0);
            AssertInformationalEventLogged(global::BuildXL.Processes.Tracing.LogEventId.SharedOpaqueOutputsDeletedLazily, count: numPipsLazilyScrubbed);

            // the following events must be logged exactly 0 times when 'postponed'; otherwise, they may or may not be logged
            AssertInformationalEventLogged(LogEventId.ScrubbingSharedOpaquesStarted, count: 0, allowMore: !postponed);
            AssertInformationalEventLogged(LogEventId.DeletingOutputsFromSharedOpaqueSidebandFilesStarted, count: 0, allowMore: !postponed);
        }
    }
}