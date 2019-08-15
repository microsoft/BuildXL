// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler;
using BuildXL.Scheduler.IncrementalScheduling;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;
using BuildXL.Utilities.Configuration;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using ArtificialCacheMissConfig = BuildXL.Utilities.Configuration.Mutable.ArtificialCacheMissConfig;

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

            AssertVerboseEventLogged(EventId.IncrementalSchedulingReuseState, count: 3);
            

            foreach (ReuseKind kind in Enum.GetValues(typeof(ReuseKind)))
            {
                if (kind == ReuseKind.Reusable)
                {
                    AssertLogContains(false, "Attempt to reuse existing incremental scheduling state: " + kind);
                }
                else
                {
                    AssertLogNotContains(false, "Attempt to reuse existing incremental scheduling state: " + kind);
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

            AssertVerboseEventLogged(EventId.ConflictDirectoryMembershipFingerprint, count: 1);

            if (changeMembershipBeforeThirdRun)
            {
                // Change directory membership before third run.
                CreateSourceFile(directoryPath);
            }

            // Due to membership change, pipA becomes dirty.
            RunScheduler().AssertScheduled(pipA.Process.PipId);
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

        protected string ReadAllText(FileArtifact file) => File.ReadAllText(ArtifactToString(file));

        protected void ModifyFile(FileArtifact file, string content = null)
        {
            File.WriteAllText(ArtifactToString(file), content ?? System.Guid.NewGuid().ToString());
        }
    }
}
