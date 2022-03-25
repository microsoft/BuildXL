// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using BuildXL.Native.IO;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler;
using BuildXL.Scheduler.IncrementalScheduling;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using ArtificialCacheMissConfig = BuildXL.Utilities.Configuration.Mutable.ArtificialCacheMissConfig;
using StorageLogEventId = BuildXL.Storage.Tracing.LogEventId;

namespace IntegrationTest.BuildXL.Scheduler.IncrementalSchedulingTests
{
    /// <summary>
    /// Tests that validate functionality of incremental scheduling.
    /// </summary>
    /// <remarks>
    /// TODO: "Duplicate" integration tests that have specific validation for incremental scheduling here.
    /// </remarks>
    [Trait("Category", "IncrementalSchedulingTests")]
    [TestClassIfSupported(requiresJournalScan: true)]
    public class IncrementalSchedulingTests : SchedulerIntegrationTestBase
    {
        public IncrementalSchedulingTests(ITestOutputHelper output) : base(output)
        {
            Configuration.Schedule.IncrementalScheduling = true;
            Configuration.Schedule.SkipHashSourceFile = false;
        }

        [Feature(Features.DirectoryProbe)]
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ValidateCachingAbsentDirectoryProbes(bool sourceMount)
        {
            // Set up absent directory
            DirectoryArtifact absentDirectory;
            if (sourceMount)
            {
                // Source mounts (i.e. read only mounts) use the actual filesystem and check the existence of files/directories on disk
                absentDirectory = DirectoryArtifact.CreateWithZeroPartialSealId(CreateUniqueDirectory(ReadonlyRoot));
                Directory.Delete(ArtifactToString(absentDirectory)); // start with absent directory
            }
            else
            {
                // Output mounts (i.e. read/write mounts) use the graph filesystem and do not check the existence of files/directories on disk
                absentDirectory = CreateOutputDirectoryArtifact();
            }

            // Pip probes absent input and directory
            Process pip = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.Probe(absentDirectory),
                Operation.WriteFile(CreateOutputFileArtifact())
            }).Process;

            RunScheduler().AssertScheduled(pip.PipId).AssertCacheMiss(pip.PipId);
            RunScheduler().AssertNotScheduled(pip.PipId);

            // Create /absentDirectory
            Directory.CreateDirectory(ArtifactToString(absentDirectory));

            // Source mounts check the existence of files/directories on disk (so cache miss)
            // Output mounts do not check the existence of files/directories on disk (so cache hit)
            if (sourceMount)
            {
                RunScheduler().AssertScheduled(pip.PipId).AssertCacheMiss(pip.PipId);
            }

            RunScheduler().AssertNotScheduled(pip.PipId);
            
            // Create /absentDirectory/newFile
            CreateSourceFile(ArtifactToString(absentDirectory));
            RunScheduler().AssertNotScheduled(pip.PipId);
        }

        [Feature(Features.DirectoryProbe)]
        [Fact]
        public void ValidateCachingAbsentDirectoryProbesInWritableMount()
        {
            // Output mounts (i.e. read/write mounts) use the graph filesystem and do not check the existence of files/directories on disk

            // Absent path D
            DirectoryArtifact absentDirectory = CreateOutputDirectoryArtifact();
            FileArtifact output1 = CreateOutputFileArtifact();

            // Output D\file
            FileArtifact output2 = CreateOutputFileArtifact(ArtifactToString(absentDirectory));

            // Pip1 probes absent directory and produces some output.
            Process pip1 = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.Probe(absentDirectory, doNotInfer: true),
                Operation.WriteFile(output1)
            }).Process;

            Process pip2 = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.ReadFile(output1),
                Operation.WriteFile(output2)
            }).Process;

            RunScheduler().AssertCacheMiss(pip1.PipId, pip2.PipId);
            RunScheduler().AssertNotScheduled(pip1.PipId, pip2.PipId);
        }

        [Fact]
        public void PreserveOutputsOffThenOn()
        {
            const string CONTENT = "A";
            const string CONTENT_TWICE = CONTENT + CONTENT;

            FileArtifact input = CreateSourceFile();
            FileArtifact output = CreateOutputFileArtifact();

            var builderA = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(input),
                Operation.WriteFile(output, CONTENT, doNotInfer: false)
            });

            builderA.Options |= Process.Options.AllowPreserveOutputs;

            var pipA = SchedulePipBuilder(builderA);

            // Turn off.
            Configuration.Sandbox.UnsafeSandboxConfigurationMutable.PreserveOutputs = PreserveOutputsMode.Disabled;

            // No cache hit.
            RunScheduler().AssertCacheMiss(pipA.Process.PipId);
            XAssert.AreEqual(CONTENT, ReadAllText(output));

            // Skipped by incremental scheduling.
            RunScheduler().AssertNotScheduled(pipA.Process.PipId);
            XAssert.AreEqual(CONTENT, ReadAllText(output));

            // Turn on.
            Configuration.Sandbox.UnsafeSandboxConfigurationMutable.PreserveOutputs = PreserveOutputsMode.Enabled;

            // Skipped by incremental scheduling.
            RunScheduler().AssertNotScheduled(pipA.Process.PipId);
            XAssert.AreEqual(CONTENT, ReadAllText(output));

            // Change the input.
            ModifyFile(input);

            // This should be the only cache miss.
            RunScheduler().AssertCacheMiss(pipA.Process.PipId);
            XAssert.AreEqual(CONTENT_TWICE, ReadAllText(output));

            // Skipped by incremental scheduling.
            RunScheduler().AssertNotScheduled(pipA.Process.PipId);
            XAssert.AreEqual(CONTENT_TWICE, ReadAllText(output));

            // Turn off again.
            Configuration.Sandbox.UnsafeSandboxConfigurationMutable.PreserveOutputs = PreserveOutputsMode.Disabled;

            // This should result in cache miss.
            RunScheduler().AssertCacheMiss(pipA.Process.PipId);
            XAssert.AreEqual(CONTENT, ReadAllText(output));

            // Skipped by incremental scheduling.
            RunScheduler().AssertNotScheduled(pipA.Process.PipId);
            XAssert.AreEqual(CONTENT, ReadAllText(output));
        }

        [Fact]
        public void ReuseIncrementalSchedulingStateFromEngineState()
        {
            FileArtifact input = CreateSourceFile();
            FileArtifact output = CreateOutputFileArtifact();

            var builderA = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(input),
                Operation.WriteFile(output, doNotInfer: false)
            });

            var pipA = SchedulePipBuilder(builderA);
            var result = RunScheduler().AssertScheduled(pipA.Process.PipId).AssertCacheMiss(pipA.Process.PipId);
            result = RunScheduler(schedulerState: result.SchedulerState).AssertNotScheduled(pipA.Process.PipId);

            // Modify input.
            ModifyFile(input);

            result = RunScheduler(schedulerState: result.SchedulerState).AssertScheduled(pipA.Process.PipId).AssertCacheMiss(pipA.Process.PipId);
            result = RunScheduler(schedulerState: result.SchedulerState).AssertNotScheduled(pipA.Process.PipId);

            AssertVerboseEventLogged(LogEventId.IncrementalSchedulingReuseState, count: 3);

            foreach (ReuseFromEngineStateKind kind in Enum.GetValues(typeof(ReuseFromEngineStateKind)))
            {
                if (kind == ReuseFromEngineStateKind.Reusable)
                {
                    AssertLogContains(false, "Attempt to reuse existing incremental scheduling state from engine state: " + kind);
                }
                else
                {
                    AssertLogNotContains(false, "Attempt to reuse existing incremental scheduling state from engine state: " + kind);
                }
            }
        }

        [Fact]
        public void IncrementalSchedulingIsRobustAgainstWildDirectoryMembershipChangeDuringPipExecution()
        {
            var directoryPath = CreateUniqueDirectory(ReadonlyRoot);

            FileArtifact file = CreateSourceFile(directoryPath);
            FileArtifact outputA = CreateOutputFileArtifact();
            DirectoryArtifact enumeratedDirectory = DirectoryArtifact.CreateWithZeroPartialSealId(directoryPath);

            var builderA = CreatePipBuilder(new Operation[]
            {
                Operation.EnumerateDir(enumeratedDirectory, doNotInfer: true),
                Operation.WriteFile(outputA)
            });

            var pipA = SchedulePipBuilder(builderA);

            FileArtifact outputB = CreateOutputFileArtifact();
            FileArtifact rougeOutput = CreateOutputFileArtifact(directoryPath);

            var builderB = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(outputA),
                Operation.WriteFile(outputB),
                Operation.WriteFile(rougeOutput, doNotInfer: true)
            });

            builderB.AddUntrackedDirectoryScope(enumeratedDirectory);

            var pipB = SchedulePipBuilder(builderB);

            FileArtifact outputC = CreateOutputFileArtifact();
            var builderC = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(outputB),
                Operation.EnumerateDir(enumeratedDirectory, doNotInfer: true),
                Operation.WriteFile(outputC)
            });

            var pipC = SchedulePipBuilder(builderC);

            var result = RunScheduler().AssertScheduled(
                pipA.Process.PipId, 
                pipB.Process.PipId, 
                pipC.Process.PipId);

            // Due to pipB that changed the directory membership, pipA gets dirty, and thus pipB and pipC becomes dirty as well.
            RunScheduler().AssertScheduled(
                pipA.Process.PipId, 
                pipB.Process.PipId, 
                pipC.Process.PipId);
            RunScheduler().AssertNotScheduled(
                pipA.Process.PipId, 
                pipB.Process.PipId, 
                pipC.Process.PipId);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void IncrementalSchedulingIsRobustAgainstWildDirectoryMembershipChangeBeforePipExecution(bool changeMembershipBeforeThirdRun)
        {
            var directoryPath = CreateUniqueDirectory(ReadonlyRoot);

            FileArtifact file = CreateSourceFile(directoryPath);
            FileArtifact inputA = CreateSourceFile();
            FileArtifact outputA = CreateOutputFileArtifact();
            DirectoryArtifact enumeratedDirectory = DirectoryArtifact.CreateWithZeroPartialSealId(directoryPath);

            var builderA = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(inputA),
                Operation.EnumerateDir(enumeratedDirectory, doNotInfer: true),
                Operation.WriteFile(outputA)
            });

            var pipA = SchedulePipBuilder(builderA);

            RunScheduler().AssertScheduled(pipA.Process.PipId);
            ModifyFile(inputA);

            // Due to inputA modification, pipA becomes dirty.
            RunScheduler(
                testHooks: new SchedulerTestHooks()
                {
                    IncrementalSchedulingStateAfterJournalScanAction = _ =>
                    {
                        // Change directory membership before pip execution.
                        CreateSourceFile(directoryPath);
                    }
                }).AssertScheduled(pipA.Process.PipId);

            AssertVerboseEventLogged(StorageLogEventId.ConflictDirectoryMembershipFingerprint, count: 1);

            if (changeMembershipBeforeThirdRun)
            {
                // Change directory membership before third run.
                CreateSourceFile(directoryPath);
            }

            // Due to membership change, pipA becomes dirty.
            RunScheduler().AssertScheduled(pipA.Process.PipId);
        }

        [Fact]
        public void IncrementalSchedulingDirectDityPipTests()
        {
            // When an Input A changes, then the seal directory is scheduled, but B is not going to be scheduled
            // When input A changes, A will be run, C will check for cache hit, and D will be skipped
            // 
            //        Seal Directory
            //        \|/      \|/
            // InputA--A        B
            //        \|/      \|/
            //      Output A Output B
            //        \|/
            //         C
            //        \|/
            //      Output C
            //        \|/
            //         D
            //        \|/
            //      Output D

            var directoryPath = CreateUniqueDirectory(ReadonlyRoot);
            var fileInsideDirectory = FileArtifact.CreateSourceFile(Combine(directoryPath, "fileToBeCreated"));
            var otherFileInputA = CreateSourceFile();
            var otherFileInputD = CreateSourceFile();

            FileArtifact outputA = CreateOutputFileArtifact();
            SealDirectory sealedDirectory = CreateAndScheduleSealDirectory(directoryPath, SealDirectoryKind.Partial, fileInsideDirectory);

            var builderA = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(otherFileInputA),
                Operation.ReadFile(fileInsideDirectory, doNotInfer: true),
                Operation.WriteFile(outputA, "Hello World A!")
            }, description: "PipA");

            builderA.AddInputDirectory(sealedDirectory.Directory);

            var pipA = SchedulePipBuilder(builderA);

            FileArtifact outputB = CreateOutputFileArtifact();

            var builderB = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(fileInsideDirectory, doNotInfer: true),
                Operation.WriteFile(outputB, "Hello World B!"),
            }, description: "PipB");

            builderB.AddInputDirectory(sealedDirectory.Directory);

            var pipB = SchedulePipBuilder(builderB);

            FileArtifact outputC = CreateOutputFileArtifact();

            var builderC = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(outputA),
                Operation.WriteFile(outputC, "Hello World C!")
            }, description: "PipC");

            var pipC = SchedulePipBuilder(builderC);

            FileArtifact outputD = CreateOutputFileArtifact();

            var builderD = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(outputC),
                Operation.ReadFile(otherFileInputD),
                Operation.WriteFile(outputD, "Hello World D!")
            }, description: "PipD");

            var pipD = SchedulePipBuilder(builderD);

            var result = RunScheduler().AssertScheduled(
                sealedDirectory.PipId,
                pipA.Process.PipId,
                pipB.Process.PipId,
                pipC.Process.PipId,
                pipD.Process.PipId);

            AssertVerboseEventLogged(LogEventId.PipIsIncrementallySkippedDueToCleanMaterialized, count: 0, allowMore: false);

            ModifyFile(otherFileInputA);

            // When otherFileInputA is changed, then pipA, all downstreams and immediate upstreams (sealDirectory) are marked dirty, and pipA/sealDiretory are marked directory dirty.
            // First, sealedDirectory is run, and marks directDirty to all downstreams THAT ARE ALREADY DIRTY.  This means pipB is left out, and pipA is marked Direct Dirty
            // Pip A is run, and marks it's downstream (PipC) as directDirty
            // Pip C is cache hit, and so it doesn't mark its downstream as direct dirty, and pipD is skipped
            RunScheduler()
                .AssertScheduled(
                    sealedDirectory.PipId,
                    pipA.Process.PipId,
                    pipC.Process.PipId,
                    pipD.Process.PipId)
                .AssertCacheMiss(pipA.Process.PipId)
                .AssertCacheHit(pipC.Process.PipId, pipD.Process.PipId)
                .AssertNotScheduled(pipB.Process.PipId);
            AssertVerboseEventLogged(LogEventId.PipIsIncrementallySkippedDueToCleanMaterialized, count: 2, allowMore: false);

            // PipD should be one of the pips which logged PipIsIncrementallySkippedDueToCleanMaterialized, but to verify this, we make D rebuild and ensure that the PipIsIncrementallySkippedDueToCleanMaterialized has gone to 1.
            ModifyFile(otherFileInputA);
            ModifyFile(otherFileInputD);
            RunScheduler()
                .AssertScheduled(
                sealedDirectory.PipId,
                pipA.Process.PipId,
                pipC.Process.PipId,
                pipD.Process.PipId)
                .AssertCacheMiss(pipA.Process.PipId, pipD.Process.PipId)
                .AssertCacheHit(pipC.Process.PipId)
                .AssertNotScheduled(pipB.Process.PipId);
            AssertVerboseEventLogged(LogEventId.PipIsIncrementallySkippedDueToCleanMaterialized, count: 1, allowMore: false);
        }

        [Fact]
        public void IncrementalSchedulingIsRobustAgainstFileCreationMidBuild()
        {
            var directoryPath = CreateUniqueDirectory(ReadonlyRoot);
            var fileInsideDirectory = FileArtifact.CreateSourceFile(Combine(directoryPath, "fileToBeCreated"));

            FileArtifact outputA = CreateOutputFileArtifact();
            SealDirectory sealedDirectory = CreateAndScheduleSealDirectory(directoryPath, SealDirectoryKind.Partial, fileInsideDirectory);

            var builderA = CreatePipBuilder(new Operation[]
            {
                Operation.Probe(fileInsideDirectory, doNotInfer: true),
                Operation.WriteFile(outputA, "Hello World A!")
            });

            builderA.AddInputDirectory(sealedDirectory.Directory);

            var pipA = SchedulePipBuilder(builderA);

            FileArtifact outputB = CreateOutputFileArtifact();
            FileArtifact rougeOutput = CreateOutputFileArtifact(directoryPath);

            var builderB = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(outputA),
                Operation.WriteFile(outputB, "Hello World B!"),
                Operation.WriteFile(fileInsideDirectory, "Whatever", doNotInfer: true)
            });

            builderB.AddUntrackedDirectoryScope(sealedDirectory.Directory);

            var pipB = SchedulePipBuilder(builderB);

            FileArtifact outputC = CreateOutputFileArtifact();

            var builderC = CreatePipBuilder(new Operation[]
            {
                Operation.ReadFile(outputB),
                Operation.Probe(fileInsideDirectory, doNotInfer: true),
                Operation.WriteFile(outputC, "Hello World C!")
            });

            builderC.AddInputDirectory(sealedDirectory.Directory);

            var pipC = SchedulePipBuilder(builderC);

            var result = RunScheduler().AssertScheduled(
                pipA.Process.PipId,
                pipB.Process.PipId,
                pipC.Process.PipId);

            // File creation blows away incremental scheduling state.
            RunScheduler()
                .AssertScheduled(
                    pipA.Process.PipId,
                    pipB.Process.PipId,
                    pipC.Process.PipId)
                .AssertCacheMiss(pipA.Process.PipId, pipC.Process.PipId)
                .AssertCacheHit(pipB.Process.PipId);
        }

        [Fact]
        public void CacheConvergenceUpdateDynamicObservationsOfIncrementalScheduling()
        {
            // Make cache look-ups always result in cache misses.
            Configuration.Cache.ArtificialCacheMissOptions = new ArtificialCacheMissConfig()
            {
                Rate = ushort.MaxValue,
                Seed = 0,
                IsInverted = false
            };

            var directoryPath = CreateUniqueDirectory(ReadonlyRoot);
            var sealedDirectory = CreateAndScheduleSealDirectory(directoryPath, SealDirectoryKind.SourceTopDirectoryOnly);

            var inputF = CreateSourceFile(directoryPath);
            var outputG = CreateOutputFileArtifact();
            var pipBuilderA = CreatePipBuilder(new[] { Operation.ReadFile(inputF, doNotInfer: true), Operation.WriteFile(outputG) });
            pipBuilderA.AddInputDirectory(sealedDirectory.Directory);
            var processA = SchedulePipBuilder(pipBuilderA).Process;

            // First run should result in a cache miss.
            RunScheduler().AssertCacheMiss(processA.PipId);

            // Modify output to make processA dirty.
            // Without any input change, second run should result in a cache miss due to artificial cache miss.
            // But there will be a cache convergence.
            ModifyFile(outputG);
            var result = RunScheduler().AssertScheduled(processA.PipId).AssertPipResultStatus((processA.PipId, PipResultStatus.DeployedFromCache));
            result.AssertPipExecutorStatCounted(PipExecutorCounter.CacheMissesForDescriptorsDueToArtificialMissOptions, 1);
            result.AssertPipExecutorStatCounted(PipExecutorCounter.ProcessPipTwoPhaseCacheEntriesConverged, 1);

            // Cache convergence should update the dynamic observation part of incremental scheduling state.
            // Modifying dynamic input, inputF, should make processA dirty.
            ModifyFile(inputF);
            RunScheduler().AssertScheduled(processA.PipId).AssertCacheMiss(processA.PipId);
        }

        [Fact]
        public void TestIncrementalSchedulingWithInputChangeList()
        {
            var changeFile = CreateSourceFileWithPrefix(SourceRoot, "changeFile");
            File.WriteAllText(ArtifactToString(changeFile), string.Empty);
            Configuration.Schedule.InputChanges = changeFile.Path;

            var sourceFile = CreateSourceFileWithPrefix(SourceRoot, "sourceFile");

            var process = CreateAndSchedulePipBuilder(new[] { Operation.ReadFile(sourceFile), Operation.WriteFile(CreateOutputFileArtifact()) }).Process;
            RunScheduler().AssertCacheMiss(process.PipId);

            File.WriteAllLines(ArtifactToString(changeFile), new[] { ArtifactToString(sourceFile) });

            RunScheduler()
                .AssertScheduled(process.PipId) // input changes make the pip dirty, and so it gets scheduled.
                .AssertCacheHit(process.PipId); // no changes to the source file, thus pip gets cache hit.
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void DynamicObservationChangeShouldNotCauseOverSchedule(bool triggerFileInsideDirectory)
        {
            var directoryPath = CreateUniqueDirectory(SourceRoot);
            var sealedDirectory = CreateAndScheduleSealDirectory(directoryPath, SealDirectoryKind.SourceTopDirectoryOnly);

            var inputF = CreateSourceFile(directoryPath);
            var inputG = CreateSourceFile(directoryPath);
            var inputH = triggerFileInsideDirectory ? CreateSourceFile(directoryPath) : CreateSourceFile();
            
            // file h points to path to f.
            ModifyFile(inputH, ArtifactToString(inputF));

            var outputX = CreateOutputFileArtifact();
            var pipBuilderA = CreatePipBuilder(new[] { 
                Operation.ReadFileFromOtherFile(inputH, doNotInfer: triggerFileInsideDirectory), 
                Operation.WriteFile(outputX) });
            pipBuilderA.AddInputDirectory(sealedDirectory.Directory);
            var processA = SchedulePipBuilder(pipBuilderA).Process;

            RunScheduler().AssertCacheMiss(processA.PipId);

            // Modifying f should make A scheduled.
            ModifyFile(inputF);

            RunScheduler().AssertScheduled(processA.PipId).AssertCacheMiss(processA.PipId);

            // Modifying g should not make A scheduled.
            ModifyFile(inputG);

            RunScheduler().AssertNotScheduled(processA.PipId);

            // file h points to path to g.
            ModifyFile(inputH, ArtifactToString(inputG));

            RunScheduler().AssertScheduled(processA.PipId).AssertCacheMiss(processA.PipId);

            // Modifying f should not make A scheduled.
            ModifyFile(inputF);

            RunScheduler().AssertNotScheduled(processA.PipId);
        }

        [Fact]
        public void ModifyingProbedStaticInputCausesRescheduledAndRebuild()
        {
            var probedInput = CreateSourceFile();

            var process = CreateAndSchedulePipBuilder(new[] 
            {
                Operation.Probe(probedInput),
                Operation.ReadFile(CreateSourceFile()), 
                Operation.WriteFile(CreateOutputFileArtifact()) 
            }).Process;

            RunScheduler().AssertCacheMiss(process.PipId);

            ModifyFile(probedInput);

            RunScheduler().AssertScheduled(process.PipId).AssertCacheMiss(process.PipId);
        }

        [Theory]
        [MemberData(nameof(TruthTable.GetTable), 2, MemberType = typeof(TruthTable))]
        public void SucceedFastPipsShouldNotCauseOverSchedule(bool useSucceedFast, bool enableStopOnDirtySucceedFast)
        {
            Configuration.Schedule.StopDirtyOnSucceedFastPips = enableStopOnDirtySucceedFast;
            var inputA = CreateSourceFile();
            var outputX = CreateOutputFileArtifact();
            var pipBuilderA = CreatePipBuilder(new[] {
                Operation.ReadFile(inputA),
                Operation.WriteFile(outputX),
                Operation.SucceedWithExitCode(useSucceedFast ? 3 : 0)},
                tags: null,
                description: null,
                environmentVariables: null,
                succeedFastExitCodes: useSucceedFast ? new int[] { 3 } : null);
            var processA = SchedulePipBuilder(pipBuilderA).Process;

            var outputY = CreateOutputFileArtifact();
            var pipBuilderB = CreatePipBuilder(new[] { Operation.ReadFile(outputX), Operation.WriteFile(outputY) });
            var processB = SchedulePipBuilder(pipBuilderB).Process;

            var schedulerResult = RunScheduler();
            schedulerResult.AssertCacheMiss(processA.PipId);

            // If useSucceedFast is set, since processA returns the succeed fast code of 3, the build exits before getting to process B.
            if (useSucceedFast)
            {
                schedulerResult.AssertPipResultStatus((processB.PipId, PipResultStatus.Skipped));
            }
            else
            {
                schedulerResult.AssertCacheMiss(processB.PipId);
            }

            // The succeed fast pip is now cached, so it can continue on and run process B.
            schedulerResult = RunScheduler();
            schedulerResult.AssertCacheHit(processA.PipId);

            if (useSucceedFast)
            {
                schedulerResult.AssertCacheMiss(processB.PipId);
            }
            else
            {
                schedulerResult.AssertCacheHit(processB.PipId);
            }


            // Modifying f should make A scheduled, but B should not be scheduled because stopDirtyOnSucceedFast is set.
            ModifyFile(inputA);

            schedulerResult = RunScheduler();
            schedulerResult.AssertScheduled(processA.PipId);
            schedulerResult.AssertCacheMiss(processA.PipId);

            if (useSucceedFast && enableStopOnDirtySucceedFast)
            {
                schedulerResult.AssertNotScheduled(processB.PipId);
            }
            else
            {
                schedulerResult.AssertScheduled(processB.PipId);
            }

            schedulerResult = RunScheduler();

            // If use succeed fast and not stop on dirty pips, pip B is run this time, meaning pip A is scheduled and cache hit.
            if (useSucceedFast && !enableStopOnDirtySucceedFast)
            {
                schedulerResult.AssertScheduled(processA.PipId);
                schedulerResult.AssertCacheHit(processA.PipId);
                schedulerResult.AssertScheduled(processB.PipId);
                schedulerResult.AssertCacheMiss(processB.PipId);
            }
            else
            {
                schedulerResult.AssertNotScheduled(processA.PipId);
            }

            // Run scheduler so incremental scheduling is up to date.
            schedulerResult = RunScheduler();
            schedulerResult.AssertNotScheduled(processA.PipId);
            schedulerResult.AssertNotScheduled(processB.PipId);

            // Test changing the flag to make sure it can switch properly
            Configuration.Schedule.StopDirtyOnSucceedFastPips = !enableStopOnDirtySucceedFast;

            ModifyFile(inputA);
            schedulerResult = RunScheduler();
            schedulerResult.AssertScheduled(processA.PipId);

            // Change from not stopping to stopping or vice versa
            if (useSucceedFast && !enableStopOnDirtySucceedFast)
            {
                schedulerResult.AssertNotScheduled(processB.PipId);
            }
            else
            {
                schedulerResult.AssertScheduled(processB.PipId);
            }
        }

        [Fact]
        public void ModifyingProbedDynamicInputDoesNotCauseRescheduled()
        {
            var directoryPath = CreateUniqueDirectory(SourceRoot);
            var sealedDirectory = CreateAndScheduleSealDirectory(directoryPath, SealDirectoryKind.SourceTopDirectoryOnly);

            var probedInput = CreateSourceFile(directoryPath);

            var pipBuilder = CreatePipBuilder(new[]
            {
                Operation.Probe(probedInput, doNotInfer: true),
                Operation.ReadFile(CreateSourceFile()),
                Operation.WriteFile(CreateOutputFileArtifact())
            });
            pipBuilder.AddInputDirectory(sealedDirectory.Directory);

            var process = SchedulePipBuilder(pipBuilder).Process;

            RunScheduler().AssertCacheMiss(process.PipId);

            ModifyFile(probedInput);

            RunScheduler().AssertNotScheduled(process.PipId);
        }

        [Fact]
        public void RemovingProbedDynamicInputCauseRescheduledAndRebuild()
        {
            var directoryPath = CreateUniqueDirectory(SourceRoot);
            var sealedDirectory = CreateAndScheduleSealDirectory(directoryPath, SealDirectoryKind.SourceTopDirectoryOnly);

            var probedInput = CreateSourceFile(directoryPath);

            var pipBuilder = CreatePipBuilder(new[]
            {
                Operation.Probe(probedInput, doNotInfer: true),
                Operation.ReadFile(CreateSourceFile()),
                Operation.WriteFile(CreateOutputFileArtifact())
            });
            pipBuilder.AddInputDirectory(sealedDirectory.Directory);

            var process = SchedulePipBuilder(pipBuilder).Process;

            RunScheduler().AssertCacheMiss(process.PipId);

            DeleteFile(probedInput);

            RunScheduler().AssertScheduled(process.PipId).AssertCacheMiss(process.PipId);

            // Re-introducing the file should reschedule the pip, but it should get a cache hit.
            ModifyFile(probedInput);

            RunScheduler().AssertScheduled(process.PipId).AssertCacheHit(process.PipId);
        }

        [Fact]
        public void IncrementalSchedulingForModifiedProbeFile()
        {
            var directoryPath = CreateUniqueDirectory(SourceRoot);
            var sealedDirectory = CreateAndScheduleSealDirectory(directoryPath, SealDirectoryKind.SourceTopDirectoryOnly);

            var probedInput = CreateSourceFile(directoryPath);
            var oldPath = probedInput.Path.ToString(Context.PathTable);
            var newPath = oldPath + "_ext";

            var pipBuilder = CreatePipBuilder(new[]
            {
                Operation.Probe(probedInput, doNotInfer: true),
                Operation.ReadFile(CreateSourceFile()),
                Operation.WriteFile(CreateOutputFileArtifact())
            });
            pipBuilder.AddInputDirectory(sealedDirectory.Directory);

            var process = SchedulePipBuilder(pipBuilder).Process;

            RunScheduler().AssertCacheMiss(process.PipId);

            RenameFile(oldPath, newPath);
            RenameFile(newPath, oldPath);

            RunScheduler().AssertNotScheduled(process.PipId);
        }

        [Fact]
        public void ProducingOutputDirectoryShouldNotInvalidateIncrementalScheduling()
        {
            var outputDirectory = CreateOutputDirectoryArtifact();
            var outputFileInOutputDirectory = CreateOutputFileArtifact(outputDirectory);
            var sourceFile = CreateSourceFile();

            var builder = CreatePipBuilder(new[]
            {
                Operation.ReadFile(sourceFile),
                Operation.WriteFile(outputFileInOutputDirectory, doNotInfer: true)
            });
            builder.AddOutputDirectory(outputDirectory, SealDirectoryKind.Opaque);

            var process = SchedulePipBuilder(builder).Process;

            // 1st Run:
            // - Run should results in a cache miss.
            // - After the run, outputFileInOutputDirectory should be tracked with some USN-1.
            RunScheduler().AssertCacheMiss(process.PipId);

            ModifyFile(sourceFile);

            // 2nd Run:
            // - Run should results in a cache miss because source is modified.
            // - After the run, outputFileInOutputDirectory should be tracked with USN-1, USN-2
            //   but the supersession limit is set to USN-2, which means that any change with USN <= USN-2
            //   will be ignored in the next build.
            RunScheduler().AssertCacheMiss(process.PipId);

            // 3rd Run:
            // - Pip should not be scheduled.
            // - During the journal scanning, file change tracker will see USN-X, USN-2 from the last checkpoint.
            //   USN-X is the result of deleting the file before running the pip. However both USNs <= USN-2, and due to
            //   supersession limit, both USNs are ignored.
            RunScheduler().AssertNotScheduled(process.PipId);
        }

        [Theory]
        [MemberData(nameof(TruthTable.GetTable), 1, MemberType = typeof(TruthTable))]
        public void ProducingNestedOutputDirectoryShouldNotInvalidateIncrementalScheduling(bool runPosixDeleteFirst)
        {
            var outputDirectory = CreateOutputDirectoryArtifact();
            var outputFileInNestedOutputDirectory = CreateOutputFileArtifact(outputDirectory.Path.Combine(Context.PathTable, "nested"));
            var sourceFile = CreateSourceFile();

            var builder = CreatePipBuilder(new[]
            {
                Operation.ReadFile(sourceFile),
                Operation.WriteFile(outputFileInNestedOutputDirectory, doNotInfer: true)
            });
            builder.AddOutputDirectory(outputDirectory, SealDirectoryKind.Opaque);

            var process = SchedulePipBuilder(builder).Process;

            var originalPosixDeleteMode = FileUtilities.PosixDeleteMode;
            FileUtilities.PosixDeleteMode = runPosixDeleteFirst ? PosixDeleteMode.RunFirst : PosixDeleteMode.NoRun;

            try
            {
                // 1st Run:
                // - Run should results in a cache miss.
                // - After the run, outputFileInOutputDirectory should be tracked with some USN-1.
                RunScheduler(runNameOrDescription: "1st Run").AssertCacheMiss(process.PipId);

                ModifyFile(sourceFile);

                // 2nd Run:
                // - Run should results in a cache miss because source is modified.
                // - After the run, outputFileInOutputDirectory should be tracked with USN-1, USN-2
                //   but the supersession limit is set to USN-2, which means that any change with USN <= USN-2
                //   will be ignored in the next build.
                RunScheduler(runNameOrDescription: "2nd Run").AssertCacheMiss(process.PipId);

                var runResult = RunScheduler(runNameOrDescription: "3rd Run");
                
                if (runPosixDeleteFirst)
                {
                    runResult.AssertNotScheduled(process.PipId);
                }
                else
                {
                    runResult.AssertScheduled(process.PipId).AssertCacheHit(process.PipId);
                }
            }
            finally
            {
                FileUtilities.PosixDeleteMode = originalPosixDeleteMode;
            }
        }

        protected string ReadAllText(FileArtifact file) => File.ReadAllText(ArtifactToString(file));

        protected void ModifyFile(FileArtifact file, string content = null) => File.WriteAllText(ArtifactToString(file), content ?? Guid.NewGuid().ToString());
        protected void RenameFile(string oldPath, string newPath) => File.Move(oldPath, newPath);

        protected void DeleteFile(FileArtifact file) => File.Delete(ArtifactToString(file));
    }
}
