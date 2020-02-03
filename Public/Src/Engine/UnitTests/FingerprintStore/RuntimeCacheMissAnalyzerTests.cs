// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.Fingerprints.TwoPhase;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities.Configuration;
using Newtonsoft.Json.Linq;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using static BuildXL.Scheduler.Tracing.FingerprintStore;
using FingerprintStoreClass = BuildXL.Scheduler.Tracing.FingerprintStore;


namespace Test.BuildXL.FingerprintStore
{
    public class RuntimeCacheMissAnalyzerTests : SchedulerIntegrationTestBase
    {
        public RuntimeCacheMissAnalyzerTests(ITestOutputHelper output)
            : base(output)
        {
            EnableFingerprintStore();

            // Forces unique, time-stamped logs directory between different scheduler runs within the same test
            Configuration.Logging.LogsToRetain = int.MaxValue;
        }

        private void EnableFingerprintStore()
        {
            Configuration.Logging.StoreFingerprints = true;
            Configuration.Logging.FingerprintStoreMode = FingerprintStoreMode.ExecutionFingerprintsOnly;
            Configuration.Logging.CacheMissAnalysisOption = CacheMissAnalysisOption.LocalMode();
        }

        private void DisableFingerprintStore()
        {
            Configuration.Logging.StoreFingerprints = false;
            Configuration.Logging.FingerprintStoreMode = FingerprintStoreMode.Invalid;
            Configuration.Logging.CacheMissAnalysisOption = CacheMissAnalysisOption.Disabled();
        }

        private readonly SchedulerTestHooks m_testHooks = new SchedulerTestHooks()
        {
            FingerprintStoreTestHooks = new FingerprintStoreTestHooks()
        };

        [Fact]
        public void WeakFingerprintMissIsPerformedPostExecution()
        {
            var sourceFile = CreateSourceFile();
            var process = CreateAndSchedulePipBuilder(new[]
            {
                Operation.ReadFile(sourceFile),
                Operation.WriteFile(CreateOutputFileArtifact())
            }).Process;

            RunScheduler().AssertCacheMiss(process.PipId);
            File.WriteAllText(ArtifactToString(sourceFile), "modified");
            RunScheduler(m_testHooks).AssertCacheMiss(process.PipId);

            XAssert.IsTrue(m_testHooks.FingerprintStoreTestHooks.TryGetCacheMiss(process.PipId, out var cacheMiss));
            XAssert.AreEqual(CacheMissAnalysisResult.WeakFingerprintMismatch, cacheMiss.Result);
            XAssert.IsFalse(cacheMiss.IsFromCacheLookUp);
        }

        [Fact]
        public void StrongFingerprintMissWithMatchedPathSetIsPerformedPostCacheLookUp()
        {
            var directory = CreateUniqueDirectoryArtifact();
            var sourceFile = CreateSourceFile(directory.Path);
            var sealedDirectory = CreateAndScheduleSealDirectoryArtifact(
                directory.Path,
                global::BuildXL.Pips.Operations.SealDirectoryKind.SourceAllDirectories);
            var builder = CreatePipBuilder(new[]
            {
                Operation.ReadFile(sourceFile, doNotInfer: true),
                Operation.WriteFile(CreateOutputFileArtifact())
            });
            builder.AddInputDirectory(sealedDirectory);
            var process = SchedulePipBuilder(builder).Process;

            RunScheduler().AssertCacheMiss(process.PipId);
            File.WriteAllText(ArtifactToString(sourceFile), "modified");
            RunScheduler(m_testHooks).AssertCacheMiss(process.PipId);

            XAssert.IsTrue(m_testHooks.FingerprintStoreTestHooks.TryGetCacheMiss(process.PipId, out var cacheMiss));
            XAssert.AreEqual(CacheMissAnalysisResult.StrongFingerprintMismatch, cacheMiss.Result);
            XAssert.IsTrue(cacheMiss.IsFromCacheLookUp);
        }

        [Fact]
        public void CacheMissAnalysisIsOnlyPerformedOnFrontierPips()
        {
            var sourceFile1 = CreateSourceFile();
            var outputFile1 = CreateOutputFileArtifact();
            var process1 = CreateAndSchedulePipBuilder(new[]
            {
                Operation.ReadFile(sourceFile1),
                Operation.WriteFile(outputFile1)
            }).Process;

            var sourceFile2 = CreateSourceFile();

            var process2 = CreateAndSchedulePipBuilder(new[]
            {
                Operation.ReadFile(sourceFile2),
                Operation.ReadFile(outputFile1),
                Operation.WriteFile(CreateOutputFileArtifact())
            }).Process;

            RunScheduler().AssertCacheMiss(process1.PipId, process2.PipId);
            File.WriteAllText(ArtifactToString(sourceFile1), "modified");
            File.WriteAllText(ArtifactToString(sourceFile2), "modified");
            RunScheduler(m_testHooks).AssertCacheMiss(process1.PipId, process2.PipId);

            XAssert.IsTrue(m_testHooks.FingerprintStoreTestHooks.TryGetCacheMiss(process1.PipId, out var cacheMiss));
            XAssert.AreEqual(CacheMissAnalysisResult.WeakFingerprintMismatch, cacheMiss.Result);
            XAssert.IsFalse(cacheMiss.IsFromCacheLookUp);

            // Process2's cache miss is not analyzed as it is not a frontier pip.
            XAssert.IsFalse(m_testHooks.FingerprintStoreTestHooks.TryGetCacheMiss(process2.PipId, out var _));
        }

        [Fact]
        public void StrongFingerprintMissWithUnmatchedButExistPathSetInFingerprintStoreIsPerformedPostCacheLookUp()
        {
            // This test is different from StrongFingerprintMissWithMatchedPathSetIsPerformedPostCacheLookUp
            // in the sense that the second build will have a different pathset. However, the pathset used
            // for cache look-up exists already in the fingerprint store due to the first build.

            var directory = CreateUniqueDirectoryArtifact();
            var sourceFile = CreateSourceFile(directory.Path);
            var readFileA = CreateSourceFile(directory.Path);
            var readFileB = CreateSourceFile(directory.Path);

            File.WriteAllText(ArtifactToString(sourceFile), ArtifactToString(readFileA));
            var sealedDirectory = CreateAndScheduleSealDirectoryArtifact(
                directory.Path,
                global::BuildXL.Pips.Operations.SealDirectoryKind.SourceAllDirectories);

            var builder = CreatePipBuilder(new[]
            {
                Operation.ReadFileFromOtherFile(sourceFile, doNotInfer: true),
                Operation.WriteFile(CreateOutputFileArtifact())
            });
            builder.AddInputDirectory(sealedDirectory);
            var process = SchedulePipBuilder(builder).Process;

            RunScheduler().AssertCacheMiss(process.PipId);

            // Modify read file to readFileB to force different pathset.
            File.WriteAllText(ArtifactToString(sourceFile), ArtifactToString(readFileB));

            RunScheduler(m_testHooks).AssertCacheMiss(process.PipId);

            XAssert.IsTrue(m_testHooks.FingerprintStoreTestHooks.TryGetCacheMiss(process.PipId, out var cacheMiss));

            // The pathset used during the cache look-up exists in the fingerprint store due to the first build.
            // So, the cache miss analysis is performed during the cache look-up.
            XAssert.AreEqual(CacheMissAnalysisResult.StrongFingerprintMismatch, cacheMiss.Result);
            XAssert.IsTrue(cacheMiss.IsFromCacheLookUp);
        }

        [Fact]
        public void StrongFingerprintMissWithUnmatchedAndNonExistentPathSetInFingerprintStoreIsPerformedPostCacheLookUp()
        {
            var directory = CreateUniqueDirectoryArtifact();
            var sourceFile = CreateSourceFile(directory.Path);
            var readFileA = CreateSourceFile(directory.Path);
            var readFileB = CreateSourceFile(directory.Path);

            File.WriteAllText(ArtifactToString(sourceFile), ArtifactToString(readFileA));
            var sealedDirectory = CreateAndScheduleSealDirectoryArtifact(
                directory.Path,
                global::BuildXL.Pips.Operations.SealDirectoryKind.SourceAllDirectories);

            var builder = CreatePipBuilder(new[]
            {
                Operation.ReadFileFromOtherFile(sourceFile, doNotInfer: true),
                Operation.WriteFile(CreateOutputFileArtifact())
            });
            builder.AddInputDirectory(sealedDirectory);
            var process = SchedulePipBuilder(builder).Process;

            // This run will insert cache entry ce1 with #pathset1, where pathset1 = [ readFileA ], into the published cache entry list.
            RunScheduler().AssertCacheMiss(process.PipId);
            
            // Modify read file to readFileB to force a different pathset.
            File.WriteAllText(ArtifactToString(sourceFile), ArtifactToString(readFileB));

            // This run will insert cache entry ce2 with #pathset2, where pathset2 = [ readFileB ], into the published cache entry list.
            RunScheduler().AssertCacheMiss(process.PipId);

            // Revert read file to readFileA, but to avoid cache hit, modify the content of readFileA.
            File.WriteAllText(ArtifactToString(sourceFile), ArtifactToString(readFileA));
            File.WriteAllText(ArtifactToString(readFileA), "modified");

            // This run will check cache entries with [ce2, ce1] during cache look-up. The chosen cache entry for runtime cache
            // miss analysis is still ce2 because, even though ce1 is the last one checked, ce2 has the pathset that is stored
            // in the fingerprint store. Thus, the runtime cache miss analysis will be done post cache look-up.
            RunScheduler(m_testHooks).AssertCacheMiss(process.PipId);

            XAssert.IsTrue(m_testHooks.FingerprintStoreTestHooks.TryGetCacheMiss(process.PipId, out var cacheMiss));
            XAssert.AreEqual(CacheMissAnalysisResult.StrongFingerprintMismatch, cacheMiss.Result);
            XAssert.IsTrue(cacheMiss.IsFromCacheLookUp);
        }

        [Fact]
        public void PathSetMissIsPerformedPostExecution()
        {
            var directory = CreateUniqueDirectoryArtifact();
            var sourceFile = CreateSourceFile(directory.Path);
            var readFileA = CreateSourceFile(directory.Path);
            var readFileB = CreateSourceFile(directory.Path);
            var readFileC = CreateSourceFile(directory.Path);

            File.WriteAllText(ArtifactToString(sourceFile), ArtifactToString(readFileA));
            var sealedDirectory = CreateAndScheduleSealDirectoryArtifact(
                directory.Path,
                global::BuildXL.Pips.Operations.SealDirectoryKind.SourceAllDirectories);

            var builder = CreatePipBuilder(new[]
            {
                Operation.ReadFileFromOtherFile(sourceFile, doNotInfer: true),
                Operation.WriteFile(CreateOutputFileArtifact())
            });
            builder.AddInputDirectory(sealedDirectory);
            var process = SchedulePipBuilder(builder).Process;

            // This run will insert cache entry ce1 with #pathset1, where pathset1 = [ readFileA ], into the published cache entry list.
            // The #pathset1 will be inserted into the fingerprint store.
            RunScheduler().AssertCacheMiss(process.PipId);

            // Make two phase cache forget everything, so ce1 is gone, but only from the two-phase cache, not from the fingerprint store.
            MakeTwoPhaseCacheForgetEverything();

            DisableFingerprintStore();

            // Modify read file to readFileB to force a different pathset.
            File.WriteAllText(ArtifactToString(sourceFile), ArtifactToString(readFileB));

            // This run will insert cache entry ce2 with #pathset2, where pathset2 = [ readFileB ], into the published cache entry list.
            // Since fingerprint store is disabled, nothing is inserted into the fingerprint store.
            RunScheduler().AssertCacheMiss(process.PipId);

            EnableFingerprintStore();

            // Modify read file to readFileC to force a different pathset.
            File.WriteAllText(ArtifactToString(sourceFile), ArtifactToString(readFileC));

            // The fingerprint store at post cache look-up will have #pathset2 to be chosen. However, since #pathset2 is not in
            // the previous fingeprint store (recall we disable fingeprint store in 2nd build), fingerprint store won't do cache
            // miss analysis post cache look-up, and will do it post execution instead.
            // The previous fingerprint store only has #pathset1 from 1st build. But after execution the pathset will be different,
            // thus we should have pathset miss.
            RunScheduler(m_testHooks).AssertCacheMiss(process.PipId);

            XAssert.IsTrue(m_testHooks.FingerprintStoreTestHooks.TryGetCacheMiss(process.PipId, out var cacheMiss));
            XAssert.AreEqual(CacheMissAnalysisResult.PathSetHashMismatch, cacheMiss.Result);
            XAssert.IsFalse(cacheMiss.IsFromCacheLookUp);
        }

        [Fact]
        public void StrongFingerprintMissWithAugmentedWeakFingerprintPostCacheLookUp()
        {
            Configuration.Cache.AugmentWeakFingerprintPathSetThreshold = 2;

            var directory = CreateUniqueDirectoryArtifact();
            var sourceFile = CreateSourceFile(directory.Path);
            var readFileA = CreateSourceFile(directory.Path);
            var readFileB = CreateSourceFile(directory.Path);
            var readFileC = CreateSourceFile(directory.Path);

            var sealedDirectory = CreateAndScheduleSealDirectoryArtifact(
                directory.Path,
                global::BuildXL.Pips.Operations.SealDirectoryKind.SourceAllDirectories);

            var builder = CreatePipBuilder(new[]
            {
                Operation.ReadFileFromOtherFile(sourceFile, doNotInfer: true),
                Operation.WriteFile(CreateOutputFileArtifact())
            });
            builder.AddInputDirectory(sealedDirectory);
            var process = SchedulePipBuilder(builder).Process;

            // Pip will read file source and file A.
            // Two-phase cache will contain mapping 
            //     wp => (#{source, A}, sp1)
            File.WriteAllText(ArtifactToString(sourceFile), ArtifactToString(readFileA));
            RunScheduler().AssertCacheMiss(process.PipId);

            // Pip will read file source and file B.
            // Two-phase cache will contain mapping 
            //     wp => (#{source, B}, sp2), (#{source, A}, sp1)
            File.WriteAllText(ArtifactToString(sourceFile), ArtifactToString(readFileB));
            var result = RunScheduler().AssertCacheMiss(process.PipId);
            var cacheInfo2 = result.RunData.ExecutionCachingInfos[process.PipId];

            // Pip will read file source and file C.
            // Two-phase cache will contain mappings 
            //     wp      => (#{source}, aug_marker), (#{source, B}, sp2), (#{source, A}, sp1)
            //     aug_wp1 => (#{source, C}, sp3)
            //
            // Fingerprint store contains mapping
            //     pip  => (wp, #{source, C}, sp3)
            File.WriteAllText(ArtifactToString(sourceFile), ArtifactToString(readFileC));
            result = RunScheduler().AssertCacheMiss(process.PipId);
            var cacheInfo3 = result.RunData.ExecutionCachingInfos[process.PipId]; ;

            FingerprintStoreSession(
                result,
                store =>
                {
                    store.TryGetFingerprintStoreEntryBySemiStableHash(process.FormattedSemiStableHash, out var entry);

                    // Weak fingerprint stored in the fingerprint store is the original one; not the augmented one.
                    // So use the weak fingerprint from previous build, i.e., cacheInfo2.
                    XAssert.AreEqual(cacheInfo2.WeakFingerprint.ToString(), entry.PipToFingerprintKeys.Value.WeakFingerprint);
                    
                    XAssert.AreEqual(
                        JsonFingerprinter.ContentHashToString(cacheInfo3.PathSetHash),
                        entry.PipToFingerprintKeys.Value.FormattedPathSetHash);
                    XAssert.AreEqual(cacheInfo3.StrongFingerprint.ToString(), entry.PipToFingerprintKeys.Value.StrongFingerprint);
                });

            // Modify file C.
            File.WriteAllText(ArtifactToString(readFileC), "modified");

            // Runtime cache miss analysis will be done post cache look-up.
            // Although perhaps the last evaluated cache entry is (#{source, A}, sp1), the fingerprint store
            // will try to find the most relevant entry for cache miss analysis, which in this case (#{source, C}, sp3).
            RunScheduler(m_testHooks).AssertCacheMiss(process.PipId);

            XAssert.IsTrue(m_testHooks.FingerprintStoreTestHooks.TryGetCacheMiss(process.PipId, out var cacheMiss));
            XAssert.AreEqual(CacheMissAnalysisResult.StrongFingerprintMismatch, cacheMiss.Result);
            XAssert.IsTrue(cacheMiss.IsFromCacheLookUp);

            // Ensure that the diff contains file C because the fingerprint store uses the most relevant cache entry for
            // cache miss analysis, i.e., (#{source, C}, sp3)
            XAssert.Contains(cacheMiss.Reason.ToUpperInvariant(), ArtifactToString(readFileC).Replace("\\", "\\\\").ToUpperInvariant());
        }

        [Fact]
        public void MissAugmentedWeakFingerprintIsClassifiedAsPathSetMissButAlwaysPostCacheExecution()
        {
            Configuration.Cache.AugmentWeakFingerprintPathSetThreshold = 2;

            var directory = CreateUniqueDirectoryArtifact();
            var sourceFile = CreateSourceFile(directory.Path);
            var readFileA = CreateSourceFile(directory.Path);
            var readFileB = CreateSourceFile(directory.Path);
            var readFileC = CreateSourceFile(directory.Path);
            var readFileD = CreateSourceFile(directory.Path);

            var sealedDirectory = CreateAndScheduleSealDirectoryArtifact(
                directory.Path,
                global::BuildXL.Pips.Operations.SealDirectoryKind.SourceAllDirectories);

            var builder = CreatePipBuilder(new[]
            {
                Operation.ReadFileFromOtherFile(sourceFile, doNotInfer: true),
                Operation.WriteFile(CreateOutputFileArtifact())
            });
            builder.AddInputDirectory(sealedDirectory);
            var process = SchedulePipBuilder(builder).Process;

            // Pip will read file source and file A.
            // Two-phase cache will contain mapping 
            //     wp => (#{source, A}, sp1)
            File.WriteAllText(ArtifactToString(sourceFile), ArtifactToString(readFileA));
            RunScheduler().AssertCacheMiss(process.PipId);

            // Pip will read file source and file B.
            // Two-phase cache will contain mapping 
            //     wp => (#{source, B}, sp2), (#{source, A}, sp1)
            File.WriteAllText(ArtifactToString(sourceFile), ArtifactToString(readFileB));
            RunScheduler().AssertCacheMiss(process.PipId);

            // Pip will read file source and file C.
            // Two-phase cache will contain mappings 
            //     wp      => (#{source}, aug_marker), (#{source, B}, sp2), (#{source, A}, sp1)
            //     aug_wp1 => (#{source, C}, sp3)
            //
            // Fingerprint store contains mapping
            //     pip  => (wp, #{source, C}, sp3)
            File.WriteAllText(ArtifactToString(sourceFile), ArtifactToString(readFileC));
            var result = RunScheduler().AssertCacheMiss(process.PipId);

            // Modify source to point to file D.
            File.WriteAllText(ArtifactToString(sourceFile), ArtifactToString(readFileD));

            RunScheduler(m_testHooks).AssertCacheMiss(process.PipId);

            XAssert.IsTrue(m_testHooks.FingerprintStoreTestHooks.TryGetCacheMiss(process.PipId, out var cacheMiss));
            XAssert.AreEqual(CacheMissAnalysisResult.PathSetHashMismatch, cacheMiss.Result);

            // Cache miss analysis wasn't performed post cache look-up because at that time
            // fingerprint store has "pip => (wp, #{source, C}, sp3)" mapping, and none of
            // the strong fingerprint computation data has #{source, C} as pathset hash.
            // Note that the list of strong fingerprint computation data won't include #{source, C}
            // form aug_wp1 mapping because of miss augmented weak fingerprint.
            XAssert.IsFalse(cacheMiss.IsFromCacheLookUp);
        }

        private void MakeTwoPhaseCacheForgetEverything()
        {
            var twoPhaseCache = (InMemoryTwoPhaseFingerprintStore)Cache.TwoPhaseFingerprintStore;
            twoPhaseCache.Forget();
        }

        public void FingerprintStoreSession(
            ScheduleRunResult result,
            Action<FingerprintStoreClass> storeOps,
            bool cacheLookupStore = false,
            bool readOnly = true,
            FingerprintStoreTestHooks testHooks = null)
        {
            var storeDirectory = cacheLookupStore
                ? result.Config.Logging.CacheLookupFingerprintStoreLogDirectory.ToString(Context.PathTable)
                : result.Config.Logging.ExecutionFingerprintStoreLogDirectory.ToString(Context.PathTable);

            using (var fingerprintStore = Open(storeDirectory, readOnly: readOnly, testHooks: testHooks).Result)
            {
                storeOps(fingerprintStore);
            }
        }
    }
}
