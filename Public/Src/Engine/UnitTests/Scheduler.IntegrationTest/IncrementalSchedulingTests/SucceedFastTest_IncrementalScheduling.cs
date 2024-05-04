// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities.Core;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace IntegrationTest.BuildXL.Scheduler.IncrementalSchedulingTests
{
    [Feature(Features.IncrementalScheduling)]
    [TestClassIfSupported(requiresJournalScan: true)]
    public class SucceedFastTests_IncrementalScheduling : SucceedFastTests
    {
        public SucceedFastTests_IncrementalScheduling(ITestOutputHelper output) : base(output)
        {
            Configuration.Schedule.IncrementalScheduling = true;
            Configuration.Schedule.SkipHashSourceFile = false;
        }

        [Theory]
        [MemberData(nameof(TruthTable.GetTable), 1, MemberType = typeof(TruthTable))]
        public void SucceedFastPipsShouldNotCauseOverSchedule(bool enableStopOnDirtySucceedFast)
        {
            // inputA -> processA (succeedFast if useSucceedFast) -> outputX -> processB -> outputY
            // inputC -> processC (NOT succeedFast) -> outputZ               -> processB

            //
            //           inputA           inputC
            //           |                |
            //           processA(sf)     processC(not sf)
            //           |                |
            //           outputX          outputZ
            //           |                |
            //           processB <--------
            //           |
            //           outputY
            //
            //
            // Modifying inputA alone should only schedule processA and skip processB when stopDirtyOnSucceedFast is enabled. 
            // Modifying inputC should always schedule processC and processB.
            //

            Configuration.Schedule.StopDirtyOnSucceedFastPips = enableStopOnDirtySucceedFast;
            var inputA = CreateSourceFile();
            var outputX = CreateOutputFileArtifact();
            var pipBuilderA = CreatePipBuilder(new[] {
                Operation.ReadFile(inputA),
                Operation.WriteFile(outputX, "outputX"),
                Operation.SucceedWithExitCode(3)},
                tags: null,
                description: "Pip A",
                environmentVariables: null,
                succeedFastExitCodes: new int[] { 3 });

            var processA = SchedulePipBuilder(pipBuilderA).Process;

            var inputC = CreateSourceFile();
            var outputZ = CreateOutputFileArtifact();
            var pipBuilderC = CreatePipBuilder(new[] {
                Operation.ReadFile(inputC),
                Operation.WriteFile(outputZ) },
                tags: null,
                description: "Pip C",
                environmentVariables: null);
            var processC = SchedulePipBuilder(pipBuilderC).Process;

            var outputY = CreateOutputFileArtifact();
            var pipBuilderB = CreatePipBuilder(new[] { Operation.ReadFile(outputX), Operation.ReadFile(outputZ), Operation.WriteFile(outputY) }, description: "Pip B");
            var processB = SchedulePipBuilder(pipBuilderB).Process;

            // When running the build for the first time
            // processA : Cache Miss
            // processC: Cache Miss
            // processB: Skipped since processA is succeed fast and cache miss
            SucceedFastPipsShouldNotCauseOverSchedule_CheckFirstBuild(processA, processC, processB);

            // When running the build for the second time
            // processA : Incrementally skipped (Cache Hit)
            // processC: Incrementally skipped (Cache Hit)
            // processB: Cache Miss since it did not run before
            SucceedFastPipsShouldNotCauseOverSchedule_CheckSecondBuild(processA, processC, processB);

            // Modify InputA, process output will be the same
            // processA : Cache Miss
            // processC: Incrementally skipped (Cache Hit)
            // processB: stopDirtyOnSucceedFast ? Skipped since only processA changed and processC got cache hit  : CacheHit (Output of ProcessA is always same)
            SucceedFastPipsShouldNotCauseOverSchedule_ModifyInputA(inputA, processA, processC, processB, enableStopOnDirtySucceedFast);

            // Modify InputC
            // processA : Incrementally skipped (Cache Hit)
            // processC: Cache Miss
            // processB: Cache Miss
            SucceedFastPipsShouldNotCauseOverSchedule_ModifyInputC(processA, inputC, processC, processB);
        }

        private void SucceedFastPipsShouldNotCauseOverSchedule_ModifyInputA(FileArtifact inputA, Process processA, Process processC, Process processB, bool stopDirtyOnSucceedFast)
        {
            ModifyFile(inputA, "InputAContent");
            var schedulerResult = RunScheduler();

            schedulerResult.AssertCacheMiss(processA.PipId);
            schedulerResult.AssertCacheHit(processC.PipId);
            AssertVerboseEventLogged(LogEventId.PipIsIncrementallySkippedDueToCleanMaterialized, count: 1, allowMore: false);

            if (stopDirtyOnSucceedFast)
            {
                schedulerResult.AssertPipResultStatus((processB.PipId, PipResultStatus.Skipped));
            }
            else
            {
                schedulerResult.AssertCacheHit(processB.PipId);
            }
        }

        private void SucceedFastPipsShouldNotCauseOverSchedule_ModifyInputC(Process processA, FileArtifact inputC, Process processC, Process processB)
        {
            ModifyFile(inputC, "InputCContent");
            var schedulerResult = RunScheduler();

            AssertVerboseEventLogged(LogEventId.PipIsIncrementallySkippedDueToCleanMaterialized, count: 1, allowMore: false);
            schedulerResult.AssertCacheHit(processA.PipId);
            schedulerResult.AssertCacheMiss(processC.PipId);
            schedulerResult.AssertCacheMiss(processB.PipId);
        }
        private void SucceedFastPipsShouldNotCauseOverSchedule_CheckSecondBuild(Process processA, Process processC, Process processB)
        {
            var schedulerResult = RunScheduler();
            schedulerResult.AssertCacheHit(processA.PipId);
            schedulerResult.AssertCacheHit(processC.PipId);

            // Pip A and Pip C are skipped in the scheduler.
            AssertVerboseEventLogged(LogEventId.PipIsIncrementallySkippedDueToCleanMaterialized, count: 2, allowMore: false);
            schedulerResult.AssertCacheMiss(processB.PipId);
        }

        private ScheduleRunResult SucceedFastPipsShouldNotCauseOverSchedule_CheckFirstBuild(Process processA, Process processC, Process processB)
        {
            var schedulerResult = RunScheduler();
            AssertVerboseEventLogged(LogEventId.PipIsIncrementallySkippedDueToCleanMaterialized, count: 0, allowMore: false);

            schedulerResult.AssertCacheMiss(processA.PipId);
            schedulerResult.AssertCacheMiss(processC.PipId);
            schedulerResult.AssertPipResultStatus((processB.PipId, PipResultStatus.Skipped));

            return schedulerResult;
        }

        protected void ModifyFile(FileArtifact file, string content = null) => File.WriteAllText(ArtifactToString(file), content ?? Guid.NewGuid().ToString());

    }
}
