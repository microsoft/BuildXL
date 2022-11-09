// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Engine.Cache;
using BuildXL.Engine.Cache.Fingerprints.TwoPhase;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using static BuildXL.Scheduler.Tracing.FingerprintStore;
using FingerprintStoreClass = BuildXL.Scheduler.Tracing.FingerprintStore;
using static BuildXL.Scheduler.Tracing.CacheMissAnalysisUtilities;
using static BuildXL.Scheduler.Tracing.FingerprintStoreTestHooks;
using BuildXL.Scheduler.Fingerprints;
using ProcessesLogEventId = BuildXL.Processes.Tracing.LogEventId;
using System.Threading;

namespace Test.BuildXL.FingerprintStore
{
    public class RuntimeCacheMissAnalyzerTests : SchedulerIntegrationTestBase
    {
        private string m_cacheMissAnalysisDetail;
        private List<string> m_cacheMissAnalysisBatchList;
        public RuntimeCacheMissAnalyzerTests(ITestOutputHelper output)
            : base(output)
        {
            EnableFingerprintStore();

            // Forces unique, time-stamped logs directory between different scheduler runs within the same test
            Configuration.Logging.LogsToRetain = int.MaxValue;
            RuntimeCacheMissAnalyzer.s_numberOfBatchesLogged = 0;
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
            FingerprintStoreTestHooks = new FingerprintStoreTestHooks() { MaxEntryAge = TimeSpan.FromDays(1) }
        };

        [Fact]
        public void FingerprintStoresNotExistPostBuild()
        {
            var sourceFile1 = CreateSourceFile();
            var outputFile1 = CreateOutputFileArtifact();
            var process1 = CreateAndSchedulePipBuilder(new[]
            {
                Operation.ReadFile(sourceFile1),
                Operation.WriteFile(outputFile1)
            }).Process;

            var sourceFile2 = CreateSourceFile();
            var outputFile2 = CreateOutputFileArtifact();
            var process2 = CreateAndSchedulePipBuilder(new[]
            {
                Operation.ReadFile(sourceFile2),
                Operation.WriteFile(outputFile2),
            }).Process;

            var sourceFile3 = CreateSourceFile();
            var outputFile3 = CreateOutputFileArtifact();
            var process3 = CreateAndSchedulePipBuilder(new[]
            {
                Operation.ReadFile(sourceFile3),
                Operation.WriteFile(outputFile3),
            }).Process;

            RunScheduler(m_testHooks).AssertCacheMiss(process1.PipId, process2.PipId, process3.PipId);

            XAssert.DirectoryDoesNotExist(Configuration.Logging.FingerprintsLogDirectory.ToString(Context.PathTable), "FingerprintStores in Log dir should have been deleted after build.");
        }

        [Fact]
        public void BatchingCacheMissAnalysisResultEnqueueAndDequeueCounters()
        {
            var sourceFile1 = CreateSourceFile();
            var outputFile1 = CreateOutputFileArtifact();
            var process1 = CreateAndSchedulePipBuilder(new[]
            {
                Operation.ReadFile(sourceFile1),
                Operation.WriteFile(outputFile1)
            }).Process;

            var sourceFile2 = CreateSourceFile();
            var outputFile2 = CreateOutputFileArtifact();
            var process2 = CreateAndSchedulePipBuilder(new[]
            {
                Operation.ReadFile(sourceFile2),
                Operation.WriteFile(outputFile2),
            }).Process;

            var sourceFile3 = CreateSourceFile();
            var outputFile3 = CreateOutputFileArtifact();
            var process3 = CreateAndSchedulePipBuilder(new[]
            {
                Operation.ReadFile(sourceFile3),
                Operation.WriteFile(outputFile3),
            }).Process;

            RunScheduler(m_testHooks).AssertCacheMiss(process1.PipId, process2.PipId, process3.PipId);

            XAssert.AreEqual(m_testHooks.FingerprintStoreCounters.GetCounterValue(FingerprintStoreCounters.CacheMissBatchingEnqueueCount), 3);
            XAssert.AreEqual(m_testHooks.FingerprintStoreCounters.GetCounterValue(FingerprintStoreCounters.CacheMissBatchingDequeueCount), 3);
        }

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
            XAssert.AreEqual(CacheMissAnalysisResult.WeakFingerprintMismatch, cacheMiss.DetailAndResult.Result);
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
            XAssert.AreEqual(CacheMissAnalysisResult.StrongFingerprintMismatch, cacheMiss.DetailAndResult.Result);
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
            XAssert.AreEqual(CacheMissAnalysisResult.WeakFingerprintMismatch, cacheMiss.DetailAndResult.Result);
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
            XAssert.AreEqual(CacheMissAnalysisResult.StrongFingerprintMismatch, cacheMiss.DetailAndResult.Result);
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
            XAssert.AreEqual(CacheMissAnalysisResult.StrongFingerprintMismatch, cacheMiss.DetailAndResult.Result);
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
            XAssert.AreEqual(CacheMissAnalysisResult.PathSetHashMismatch, cacheMiss.DetailAndResult.Result);
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
            XAssert.AreEqual(CacheMissAnalysisResult.StrongFingerprintMismatch, cacheMiss.DetailAndResult.Result);
            XAssert.IsTrue(cacheMiss.IsFromCacheLookUp);

            // Ensure that the diff contains file C because the fingerprint store uses the most relevant cache entry for
            // cache miss analysis, i.e., (#{source, C}, sp3)
            XAssert.Contains(JsonConvert.SerializeObject(cacheMiss.DetailAndResult.Detail.Info).ToUpperInvariant(), ArtifactToString(readFileC).Replace("\\", "\\\\").ToUpperInvariant());
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
            XAssert.AreEqual(CacheMissAnalysisResult.PathSetHashMismatch, cacheMiss.DetailAndResult.Result);

            // Cache miss analysis wasn't performed post cache look-up because at that time
            // fingerprint store has "pip => (wp, #{source, C}, sp3)" mapping, and none of
            // the strong fingerprint computation data has #{source, C} as pathset hash.
            // Note that the list of strong fingerprint computation data won't include #{source, C}
            // form aug_wp1 mapping because of miss augmented weak fingerprint.
            XAssert.IsFalse(cacheMiss.IsFromCacheLookUp);
        }

        [Theory]
        [InlineData(true, true)]
        [InlineData(true, false)]
        [InlineData(false, false)]
        public void CacheMissesOfMultiplePipsTest(bool cacheMissBatch, bool exceedMaxLogSize)
        {
            Configuration.Logging.CacheMissBatch = cacheMissBatch;
            Configuration.Logging.OptimizeConsoleOutputForAzureDevOps = true;

            var dir = Path.Combine(ObjectRoot, "Dir");
            var dirPath = AbsolutePath.Create(Context.PathTable, dir);
            var pipNumber = 102;

            var processes = new List<Process>();
            for (var i = 0; i < pipNumber; i++)
            {
                FileArtifact input = CreateSourceFile(root: dirPath);
                FileArtifact output = CreateOutputFileArtifact(root: dirPath);
                var pipBuilder = CreatePipBuilder(new[] { Operation.ReadFile(input), Operation.WriteFile(output) });
                if (exceedMaxLogSize)
                {
                    pipBuilder.ToolDescription = StringId.Create(Context.StringTable, new string('*', Configuration.Logging.AriaIndividualMessageSizeLimitBytes / pipNumber));
                }

                var pip = SchedulePipBuilder(pipBuilder);
                processes.Add(pip.Process);
            }

            SetExtraSalts("FirstSalt", true);
            ScheduleRunResult initialbuild = RunScheduler().AssertCacheMiss(processes.Select(p => p.PipId).ToArray());
            ScheduleRunResult cacheHitBuild = RunScheduler().AssertCacheHit(processes.Select(p => p.PipId).ToArray());

            SetExtraSalts("SecondSalt", false);
            ScheduleRunResult cacheMissBuild = RunScheduler(m_testHooks).AssertCacheMiss(processes.Select(p => p.PipId).ToArray());

            for (var i = 0; i < pipNumber; i++)
            {
                XAssert.IsTrue(m_testHooks.FingerprintStoreTestHooks.TryGetCacheMiss(processes[i].PipId, out var cacheMiss));
                XAssert.AreEqual(CacheMissAnalysisResult.WeakFingerprintMismatch, cacheMiss.DetailAndResult.Result);
            }

            if (cacheMissBatch)
            {
                AssertVerboseEventLogged(SharedLogEventId.CacheMissAnalysisBatchResults, allowMore: true);
            }
            else
            {
                AssertVerboseEventLogged(SharedLogEventId.CacheMissAnalysis, allowMore: true);
            }

        }

        [Fact]
        public void BatchingProcessTest()
        {
            EventListener.NestedLoggerHandler += eventData =>
            {
                if (eventData.EventId == (int)SharedLogEventId.CacheMissAnalysisBatchResults)
                {
                    m_cacheMissAnalysisBatchList.Add(eventData.Payload.ToArray()[0].ToString());
                }
            };

            m_cacheMissAnalysisBatchList = new List<string>();

            var lenUnit = 100;
            var string1 = new string('a', lenUnit);
            var string2 = new string('b', 2 * lenUnit);
            var string3 = new string('c', 3 * lenUnit);
            var string4 = new string('d', 4 * lenUnit);
            var string5 = new string('e', 3 * lenUnit);
            var string6 = new string('f', 2 * lenUnit);
            var string7 = new string('g', lenUnit);

            var results = new List<JProperty>();
            JProperty result1 = new JProperty("p1", new JObject(new JProperty("Result", string1)));
            JProperty result2 = new JProperty("P2", new JObject(new JProperty("Result", string2)));
            JProperty result3 = new JProperty("P3", new JObject(new JProperty("Result", string3)));
            JProperty result4 = new JProperty("P4", new JObject(new JProperty("Result", string4)));
            JProperty result5 = new JProperty("P5", new JObject(new JProperty("Result", string5)));
            JProperty result6 = new JProperty("P6", new JObject(new JProperty("Result", string6)));
            JProperty result7 = new JProperty("P7", new JObject(new JProperty("Result", string7)));


            results.Add(result1);
            results.Add(result2);
            results.Add(result3);
            results.Add(result4);
            results.Add(result5);
            results.Add(result6);
            results.Add(result7);

            var result1Len = result1.Name.Length + result1.Value.ToString().Length;
            var result2Len = result2.Name.Length + result2.Value.ToString().Length;
            var result3Len = result3.Name.Length + result3.Value.ToString().Length;
            var result4Len = result4.Name.Length + result4.Value.ToString().Length;
            var result5Len = result5.Name.Length + result5.Value.ToString().Length;
            var result6Len = result6.Name.Length + result6.Value.ToString().Length;
            var result7Len = result7.Name.Length + result7.Value.ToString().Length;

            var timer = new Timer(o => { XAssert.IsTrue(false, "Process Timeout."); }, null, 10000, 10000);

            // 1 batch per process
            RuntimeCacheMissAnalyzer.s_numberOfBatchesLogged = 0;
            Configuration.Logging.AriaIndividualMessageSizeLimitBytes = result1Len + result2Len + result3Len + result4Len + result5Len + result6Len + result7Len + 1;
            RuntimeCacheMissAnalyzer.ProcessResults(results.ToList(), Configuration, LoggingContext);
            XAssert.AreEqual(m_cacheMissAnalysisBatchList.Count, 1, "Should have 1 batch logging.");
            XAssert.Contains(m_cacheMissAnalysisBatchList[0], string1, string2, string3, string4, string5, string6, string7);
            m_cacheMissAnalysisBatchList.Clear();

            // 2 batch per process 
            RuntimeCacheMissAnalyzer.s_numberOfBatchesLogged = 0;
            Configuration.Logging.AriaIndividualMessageSizeLimitBytes = result1Len + result2Len + result3Len + result4Len + 1;
            RuntimeCacheMissAnalyzer.ProcessResults(results.ToList(), Configuration, LoggingContext);
            XAssert.AreEqual(m_cacheMissAnalysisBatchList.Count, 2, "Should have 2 batch logging.");
            XAssert.Contains(m_cacheMissAnalysisBatchList[0], string1, string2, string3, string4);
            XAssert.Contains(m_cacheMissAnalysisBatchList[1], string5, string6, string7);
            m_cacheMissAnalysisBatchList.Clear();

            // batch - single - batch in a process
            RuntimeCacheMissAnalyzer.s_numberOfBatchesLogged = 0;
            Configuration.Logging.AriaIndividualMessageSizeLimitBytes = result1Len + result2Len + result3Len + 20;
            RuntimeCacheMissAnalyzer.ProcessResults(results.ToList(), Configuration, LoggingContext);
            XAssert.AreEqual(m_cacheMissAnalysisBatchList.Count, 3, "Should have 3 batch logging.");
            XAssert.Contains(m_cacheMissAnalysisBatchList[0], string1, string2, string3);
            XAssert.Contains(m_cacheMissAnalysisBatchList[1], string4);
            XAssert.Contains(m_cacheMissAnalysisBatchList[2], string5, string6, string7);
            XAssert.ContainsNot(m_cacheMissAnalysisBatchList[2], string4); // Make sure there no previous result in it
            m_cacheMissAnalysisBatchList.Clear();

            // batch - single - single - single - batch in a process
            RuntimeCacheMissAnalyzer.s_numberOfBatchesLogged = 0;
            Configuration.Logging.AriaIndividualMessageSizeLimitBytes = result1Len + result2Len + 1;
            RuntimeCacheMissAnalyzer.ProcessResults(results.ToList(), Configuration, LoggingContext);
            XAssert.AreEqual(m_cacheMissAnalysisBatchList.Count, 5, "Should have 5 batch logging.");
            XAssert.Contains(m_cacheMissAnalysisBatchList[0], string1, string2);
            XAssert.Contains(m_cacheMissAnalysisBatchList[1], string3);
            XAssert.Contains(m_cacheMissAnalysisBatchList[2], string4.Substring(string4.Length - Configuration.Logging.AriaIndividualMessageSizeLimitBytes / 2 + 20));
            XAssert.ContainsNot(m_cacheMissAnalysisBatchList[2], string4);
            XAssert.Contains(m_cacheMissAnalysisBatchList[2], "[...]");
            XAssert.Contains(m_cacheMissAnalysisBatchList[3], string5);
            XAssert.Contains(m_cacheMissAnalysisBatchList[4], string6, string7);
            m_cacheMissAnalysisBatchList.Clear();

            // all single in a process
            RuntimeCacheMissAnalyzer.s_numberOfBatchesLogged = 0;
            Configuration.Logging.AriaIndividualMessageSizeLimitBytes = result1Len + 1;
            RuntimeCacheMissAnalyzer.ProcessResults(results.ToList(), Configuration, LoggingContext);
            XAssert.AreEqual(m_cacheMissAnalysisBatchList.Count, 7, "Should have 7 batch logging.");
            XAssert.Contains(m_cacheMissAnalysisBatchList[0], string1);
            XAssert.Contains(m_cacheMissAnalysisBatchList[1], string2.Substring(string2.Length - Configuration.Logging.AriaIndividualMessageSizeLimitBytes / 2 + 20));
            XAssert.ContainsNot(m_cacheMissAnalysisBatchList[1], string2);
            XAssert.Contains(m_cacheMissAnalysisBatchList[1], "[...]");
            XAssert.Contains(m_cacheMissAnalysisBatchList[2], string3.Substring(string3.Length - Configuration.Logging.AriaIndividualMessageSizeLimitBytes / 2 + 20));
            XAssert.ContainsNot(m_cacheMissAnalysisBatchList[2], string3);
            XAssert.Contains(m_cacheMissAnalysisBatchList[2], "[...]");
            XAssert.Contains(m_cacheMissAnalysisBatchList[3], string4.Substring(string4.Length - Configuration.Logging.AriaIndividualMessageSizeLimitBytes / 2 + 20));
            XAssert.ContainsNot(m_cacheMissAnalysisBatchList[3], string4);
            XAssert.Contains(m_cacheMissAnalysisBatchList[3], "[...]");
            XAssert.Contains(m_cacheMissAnalysisBatchList[4], string5.Substring(string5.Length - Configuration.Logging.AriaIndividualMessageSizeLimitBytes / 2 + 20));
            XAssert.ContainsNot(m_cacheMissAnalysisBatchList[4], string5);
            XAssert.Contains(m_cacheMissAnalysisBatchList[4], "[...]");
            XAssert.Contains(m_cacheMissAnalysisBatchList[5], string6.Substring(string6.Length - Configuration.Logging.AriaIndividualMessageSizeLimitBytes / 2 + 20));
            XAssert.ContainsNot(m_cacheMissAnalysisBatchList[5], string6);
            XAssert.Contains(m_cacheMissAnalysisBatchList[5], "[...]");
            XAssert.Contains(m_cacheMissAnalysisBatchList[6], string7);
            m_cacheMissAnalysisBatchList.Clear();

            // all single in a process, test a result's len == maxLogLen
            RuntimeCacheMissAnalyzer.s_numberOfBatchesLogged = 0;
            Configuration.Logging.AriaIndividualMessageSizeLimitBytes = result3Len;
            RuntimeCacheMissAnalyzer.ProcessResults(results.ToList(), Configuration, LoggingContext);
            XAssert.AreEqual(m_cacheMissAnalysisBatchList.Count, 7, "Should have 7 batch logging.");
            XAssert.Contains(m_cacheMissAnalysisBatchList[0], string1);
            XAssert.Contains(m_cacheMissAnalysisBatchList[1], string2); 
            XAssert.Contains(m_cacheMissAnalysisBatchList[2], string3.Substring(string4.Length - Configuration.Logging.AriaIndividualMessageSizeLimitBytes / 2 + 20));// Result has exact length as maxLogLen
            XAssert.ContainsNot(m_cacheMissAnalysisBatchList[2], string3);
            XAssert.Contains(m_cacheMissAnalysisBatchList[2], "[...]");
            XAssert.Contains(m_cacheMissAnalysisBatchList[3], string4.Substring(string4.Length - Configuration.Logging.AriaIndividualMessageSizeLimitBytes / 2 + 20));
            XAssert.ContainsNot(m_cacheMissAnalysisBatchList[3], string4);
            XAssert.Contains(m_cacheMissAnalysisBatchList[3], "[...]");
            XAssert.Contains(m_cacheMissAnalysisBatchList[4], string5.Substring(string4.Length - Configuration.Logging.AriaIndividualMessageSizeLimitBytes / 2 + 20));// Result has exact length as maxLogLen
            XAssert.ContainsNot(m_cacheMissAnalysisBatchList[4], string5);
            XAssert.Contains(m_cacheMissAnalysisBatchList[4], "[...]");
            XAssert.Contains(m_cacheMissAnalysisBatchList[5], string6);
            XAssert.Contains(m_cacheMissAnalysisBatchList[6], string7);
            m_cacheMissAnalysisBatchList.Clear();

            timer.Dispose();
        }

        [Fact]
        public void BatchingRealResultTruncateTest()
        {
            EventListener.NestedLoggerHandler += eventData =>
            {
                if (eventData.EventId == (int)SharedLogEventId.CacheMissAnalysisBatchResults)
                {
                    m_cacheMissAnalysisBatchList.Add(eventData.Payload.ToArray()[0].ToString());
                }
            };

            var results = new List<JProperty>();
            string resultJson = @"{
		""Description"": ""PipC1907BAC23BB4039, test"",
		""FromCacheLookup"": false,
		""Detail"": {
			""ActualMissType"": ""MissForDescriptorsDueToStrongFingerprints"",
			""ReasonFromAnalysis"": ""WeakFingerprints of the builds are different."",
			""Info"": {
				""WeakFingerprintMismatchResult"": {
					""WeakFingerprint"": {
						""Old"": ""126B618310A6F95C4B00E9A576CFE5EAF3B5D950"",
						""New"": ""311C6E5835CEA28399F62FACCA5380C8C18347E5""
					},
					""ExecutionAndFingerprintOptions"": {
						""Changed"": {
							""FingerprintVersion"": {
								""Old"": ""80"",
								""New"": ""81""
							}
						}
					},
					""Executable"": {
						""Old"": ""path1"",
						""New"": ""path2""
					},
					""UntrackedPaths"": {
						""Added"": [
							""path3""
						],
						""Removed"": [
							""Path4""
						]
					},
					""OldProvenance:"": {
						""SessionId"": ""fa485571-0100-ffff-0f37-d21e6b002ba0"",
						""RelatedSessionId"": ""5c4a3e30-dd7e-4cc1-864a-d21e6b002ba0""
					}
				}
			}
		}
	}";

            m_cacheMissAnalysisBatchList = new List<string>();
            var pipHash = "PipC1907BAC23BB4039";
            JProperty result = new JProperty(pipHash, JObject.Parse(resultJson));
            results.Add(result);
            RuntimeCacheMissAnalyzer.s_numberOfBatchesLogged = 0;
            Configuration.Logging.AriaIndividualMessageSizeLimitBytes = 600;
            RuntimeCacheMissAnalyzer.ProcessResults(results.ToList(), Configuration, LoggingContext);
            XAssert.AreEqual(m_cacheMissAnalysisBatchList.Count, 1, "Should have 1 batch logging.");
            XAssert.Contains(m_cacheMissAnalysisBatchList[0], pipHash);
            XAssert.Contains(m_cacheMissAnalysisBatchList[0], "[...]");
            m_cacheMissAnalysisBatchList.Clear();
        }

        /// <summary>
        /// This test is created for making sure the format of the cachemiss analysis result is stable.
        /// If you do need to update the format, update this test as well
        /// </summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void FormatContractTesting(bool cacheMissBatch)
        {
            EventListener.NestedLoggerHandler += eventData =>
            {
                if (eventData.EventId == (int)SharedLogEventId.CacheMissAnalysis)
                {
                    m_cacheMissAnalysisDetail = eventData.Payload.ToArray()[1].ToString();
                }
                if (eventData.EventId == (int)SharedLogEventId.CacheMissAnalysisBatchResults)
                {
                    m_cacheMissAnalysisDetail = eventData.Payload.ToArray()[0].ToString();
                }
            };

            if (!cacheMissBatch)
            {
                Configuration.Logging.CacheMissBatch = false;
            }

            var dir = Path.Combine(ObjectRoot, "Dir");
            var dirPath = AbsolutePath.Create(Context.PathTable, dir);

            FileArtifact input = CreateSourceFile(root: dirPath, prefix: "input-file");
            FileArtifact output = CreateOutputFileArtifact(root: dirPath, prefix: "output-file");
            var pipBuilder = CreatePipBuilder(new[] { Operation.ReadFile(input), Operation.WriteFile(output) });
            var pip = SchedulePipBuilder(pipBuilder);

            RunScheduler(m_testHooks).AssertCacheMiss(pip.Process.PipId);

            if (cacheMissBatch)
            {
                XAssert.IsTrue(m_testHooks.FingerprintStoreTestHooks.TryGetCacheMiss(pip.Process.PipId, out var cacheMiss));
                XAssert.Contains(m_cacheMissAnalysisDetail, cacheMiss.DetailAndResult.Detail.ToJObjectWithPipInfo(pip.Process.FormattedSemiStableHash, pip.Process.GetDescription(Context), false).Value.ToString(), "CacheMissAnalysisResults");
            }
            else
            {
                var detail = new JObject(
                    new JProperty(nameof(CacheMissAnalysisDetail.ActualMissType), PipCacheMissType.MissForDescriptorsDueToWeakFingerprints.ToString()),
                    new JProperty(nameof(CacheMissAnalysisDetail.ReasonFromAnalysis), $"No fingerprint computation data found from old build. This may be the first execution where pip outputs were stored to the cache. {RepeatedStrings.DisallowedFileAccessesOrPipFailuresPreventCaching}"),
                    new JProperty(nameof(CacheMissAnalysisDetail.Info), null));
                XAssert.AreEqual(detail.ToString(), m_cacheMissAnalysisDetail);
            }
        }


        [Fact]
        public void DirectoryMembershipExistenceTest()
        {
            CacheMissData cacheMiss;

            DirectoryArtifact dir = DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniqueDirectory(ReadonlyRoot));
            Directory.CreateDirectory(ArtifactToString(dir));

            Process pip = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.EnumerateDir(dir),
                Operation.WriteFile(CreateOutputFileArtifact())
            }).Process;

            Configuration.Sandbox.UnsafeSandboxConfigurationMutable.IgnorePreloadedDlls = true;
            FileArtifact srcFile1 = CreateSourceFile(dir);
            File.WriteAllText(ArtifactToString(srcFile1), "member1");
            RunScheduler().AssertCacheMiss(pip.PipId);
            ScheduleRunResult buildA = RunScheduler().AssertCacheHit(pip.PipId);

            FileArtifact srcFile2 = CreateSourceFile(dir);
            File.WriteAllText(ArtifactToString(srcFile2), "member2");
            ScheduleRunResult buildB = RunScheduler(m_testHooks).AssertCacheMiss(pip.PipId);
            XAssert.IsTrue(m_testHooks.FingerprintStoreTestHooks.TryGetCacheMiss(pip.PipId, out cacheMiss));
            XAssert.Contains(cacheMiss.DetailAndResult.Detail.Info.ToString(), srcFile2.Path.GetName(Context.PathTable).ToString(Context.PathTable.StringTable));

            Configuration.Sandbox.UnsafeSandboxConfigurationMutable.IgnorePreloadedDlls = false;
            ScheduleRunResult buildC = RunScheduler(m_testHooks).AssertCacheMiss(pip.PipId);
            XAssert.IsTrue(m_testHooks.FingerprintStoreTestHooks.TryGetCacheMiss(pip.PipId, out cacheMiss));
            XAssert.Contains(cacheMiss.DetailAndResult.Detail.Info.ToString(), ObservedPathSet.Labels.UnsafeOptions);

            FileArtifact srcFile3 = CreateSourceFile(dir);
            File.WriteAllText(ArtifactToString(srcFile3), "member3");
            ScheduleRunResult buildD = RunScheduler(m_testHooks).AssertCacheMiss(pip.PipId);

            XAssert.IsTrue(m_testHooks.FingerprintStoreTestHooks.TryGetCacheMiss(pip.PipId, out cacheMiss));
            XAssert.Contains(cacheMiss.DetailAndResult.Detail.Info.ToString(), srcFile3.Path.GetName(Context.PathTable).ToString(Context.PathTable.StringTable));
            XAssert.ContainsNot(cacheMiss.DetailAndResult.Detail.Info.ToString(), RepeatedStrings.MissingDirectoryMembershipFingerprint);
        }

        [Fact]
        public void FailurePipCachemissAnalysisTest()
        {
            var inFile = CreateSourceFile();
            var outFile = CreateOutputFileArtifact();

            var builderA = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(inFile),
                Operation.WriteFile(outFile),
                Operation.Fail()
            });
            SchedulePipBuilder(builderA);
            RunScheduler().AssertFailure();
            AssertErrorEventLogged(ProcessesLogEventId.PipProcessError);

            var builderB = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(inFile),
                Operation.WriteFile(outFile),
            });

            ResetPipGraphBuilder();

            var pip = SchedulePipBuilder(builderB).Process;
            RunScheduler(m_testHooks).AssertCacheMiss(pip.PipId);

            XAssert.IsTrue(m_testHooks.FingerprintStoreTestHooks.TryGetCacheMiss(pip.PipId, out var cacheMiss));
            XAssert.AreEqual(cacheMiss.DetailAndResult.Result, CacheMissAnalysisResult.MissingFromOldBuild);
        }

        /// <summary>
        /// Cachemiss analysis won't perform for fail pips. 
        /// We don't know at which point the pip will fail. 
        /// The ProcessFingerprintComputationData can be incomplete. 
        /// Performing cache miss analysis with incomplete data can give misleading result to users.
        /// </summary>
        [Fact]
        public void MissDueToFailedPipInPreviousBuildHasAnalysisOutput()
        {
            var failPip = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.WriteFile(CreateOutputFileArtifact()),
                Operation.Fail(),
            }).Process;

            // Both builds will fail to cache any output to the cache due to the pip failure
            var buildA = RunScheduler().AssertFailure();
            var buildB = RunScheduler().AssertFailure();
            AssertErrorEventLogged(ProcessesLogEventId.PipProcessError, 2);

            XAssert.IsFalse(m_testHooks.FingerprintStoreTestHooks.TryGetCacheMiss(failPip.PipId, out var cacheMiss));
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
                : result.Config.Layout.FingerprintStoreDirectory.ToString(Context.PathTable);

            using (var fingerprintStore = Open(storeDirectory, readOnly: readOnly, testHooks: testHooks).Result)
            {
                storeOps(fingerprintStore);
            }
        }
    }
}
