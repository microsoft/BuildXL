// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Processes.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace IntegrationTest.BuildXL.Scheduler
{
    public class SucceedFastTests : SchedulerIntegrationTestBase
    {
        public SucceedFastTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public void TestSucceedFastPipBasic()
        {
            var opsA = new[]
            {
                Operation.WriteFile(CreateOutputFileArtifact()),
                Operation.SucceedWithExitCode(1)
            };

            var pipBuilderA = CreatePipBuilder(opsA);
            pipBuilderA.SucceedFastExitCodes = ReadOnlyArray<int>.FromWithoutCopy(new[] { 1 });
            pipBuilderA.SuccessExitCodes = pipBuilderA.SucceedFastExitCodes;

            Process pipA = SchedulePipBuilder(pipBuilderA).Process;

            var opsB = new[]
            {
                Operation.WriteFile(CreateOutputFileArtifact())
            };

            var pipBuilderB = CreatePipBuilder(opsB);
            pipBuilderB.AddInputFile(pipA.GetOutputs().First());

            Process pipB = SchedulePipBuilder(pipBuilderB).Process;

            var opsC = new[]
            {
                Operation.WriteFile(CreateOutputFileArtifact())
            };

            var pipBuilderC = CreatePipBuilder(opsC);
            Process pipC = SchedulePipBuilder(pipBuilderC).Process;

            var opsD = new[]
            {
                Operation.WriteFile(CreateOutputFileArtifact())
            };

            var pipBuilderD = CreatePipBuilder(opsD);
            pipBuilderD.AddInputFile(pipA.GetOutputs().First());
            pipBuilderD.AddInputFile(pipC.GetOutputs().First());
            Process pipD = SchedulePipBuilder(pipBuilderD).Process;

            var scheduleResult = RunScheduler();
            scheduleResult.AssertCacheMiss(pipA.PipId);
            scheduleResult.AssertPipResultStatus((pipB.PipId, PipResultStatus.Skipped));
            scheduleResult.AssertCacheMiss(pipC.PipId);
            scheduleResult.AssertPipResultStatus((pipD.PipId, PipResultStatus.Skipped));
            scheduleResult.AssertSuccess();

            var scheduleResult2 = RunScheduler();
            scheduleResult2.AssertCacheHit(pipA.PipId);
            scheduleResult2.AssertCacheHit(pipC.PipId);
            scheduleResult2.AssertCacheMiss(pipB.PipId);
            scheduleResult2.AssertCacheMiss(pipD.PipId);
            scheduleResult2.AssertSuccess();

            var scheduleResult3 = RunScheduler();
            scheduleResult3.AssertCacheHit(pipA.PipId);
            scheduleResult3.AssertCacheHit(pipB.PipId);
            scheduleResult3.AssertCacheHit(pipC.PipId);
            scheduleResult3.AssertCacheHit(pipD.PipId);
            scheduleResult3.AssertSuccess();
        }

        [Fact]
        public void TestSucceedFastPipExitCode()
        {
            var opsA = new[]
            {
                Operation.WriteFile(CreateOutputFileArtifact()),
                Operation.SucceedWithExitCode(1)
            };

            var pipBuilderA = CreatePipBuilder(opsA);
            pipBuilderA.SucceedFastExitCodes = ReadOnlyArray<int>.FromWithoutCopy(new[] { 1 });
            pipBuilderA.SuccessExitCodes = pipBuilderA.SucceedFastExitCodes;

            Process pipA = SchedulePipBuilder(pipBuilderA).Process;

            var opsB = new[]
            {
                Operation.WriteFile(CreateOutputFileArtifact())
            };

            var pipBuilderB = CreatePipBuilder(opsB);
            pipBuilderA.SucceedFastExitCodes = ReadOnlyArray<int>.FromWithoutCopy(new[] { 1 });
            pipBuilderA.SuccessExitCodes = pipBuilderA.SucceedFastExitCodes;

            Process pipB = SchedulePipBuilder(pipBuilderB).Process;

            var opsC = new[]
            {
                Operation.WriteFile(CreateOutputFileArtifact())
            };

            var pipBuilderC = CreatePipBuilder(opsC);
            pipBuilderC.AddInputFile(pipA.GetOutputs().First());
            Process pipC = SchedulePipBuilder(pipBuilderC).Process;

            var opsD = new[]
            {
                Operation.WriteFile(CreateOutputFileArtifact())
            };

            var pipBuilderD = CreatePipBuilder(opsD);
            pipBuilderD.AddInputFile(pipB.GetOutputs().First());
            Process pipD = SchedulePipBuilder(pipBuilderD).Process;

            var scheduleResult = RunScheduler();
            scheduleResult.AssertCacheMiss(pipA.PipId);
            scheduleResult.AssertPipResultStatus((pipC.PipId, PipResultStatus.Skipped));
            scheduleResult.AssertCacheMiss(pipB.PipId);
            scheduleResult.AssertCacheMiss(pipD.PipId);
            scheduleResult.AssertSuccess();

            var scheduleResult2 = RunScheduler();
            scheduleResult2.AssertCacheHit(pipA.PipId);
            scheduleResult2.AssertCacheHit(pipB.PipId);
            scheduleResult2.AssertCacheMiss(pipC.PipId);
            scheduleResult2.AssertCacheHit(pipD.PipId);
            scheduleResult2.AssertSuccess();
        }

        [Fact]
        public void TestSucceedFastPipFail_Late()
        {
            var opsFail = new[]
            {
                Operation.Echo("Foo"),
                Operation.WriteFile(CreateOutputFileArtifact()),
                Operation.Fail()
            };

            var pipBuilderFail = CreatePipBuilder(opsFail);
            pipBuilderFail.Priority = 1;

            Process pipFail = SchedulePipBuilder(pipBuilderFail).Process;

            var opsA = new[]
            {
                Operation.WriteFile(CreateOutputFileArtifact()),
                Operation.SucceedWithExitCode(1)
            };

            var pipBuilderA = CreatePipBuilder(opsA);
            pipBuilderA.Priority = 0;
            pipBuilderA.SucceedFastExitCodes = ReadOnlyArray<int>.FromWithoutCopy(new[] { 1 });
            pipBuilderA.SuccessExitCodes = pipBuilderA.SucceedFastExitCodes;

            Process pipA = SchedulePipBuilder(pipBuilderA).Process;

            var opsB = new[]
            {
                Operation.WriteFile(CreateOutputFileArtifact())
            };

            var pipBuilderB = CreatePipBuilder(opsB);
            pipBuilderB.Priority = 0;
            pipBuilderB.AddInputFile(pipA.GetOutputs().First());

            Process pipB = SchedulePipBuilder(pipBuilderB).Process;

            var scheduleResult = RunScheduler();
            scheduleResult.AssertFailure();
            AssertErrorEventLogged(LogEventId.PipProcessError);
        }

        [Fact]
        public void TestSucceedFastPipFail_Early()
        {
            var opsFail = new[]
            {
                Operation.Echo("Foo"),
                Operation.WriteFile(CreateOutputFileArtifact()),
                Operation.Fail()
            };

            var pipBuilderFail = CreatePipBuilder(opsFail);
            pipBuilderFail.Priority = 0;

            Process pipFail = SchedulePipBuilder(pipBuilderFail).Process;

            var opsA = new[]
            {
                Operation.WriteFile(CreateOutputFileArtifact()),
                Operation.SucceedWithExitCode(1)
            };

            var pipBuilderA = CreatePipBuilder(opsA);
            pipBuilderA.Priority = 1;
            pipBuilderA.SucceedFastExitCodes = ReadOnlyArray<int>.FromWithoutCopy(new[] { 1 });
            pipBuilderA.SuccessExitCodes = pipBuilderA.SucceedFastExitCodes;

            Process pipA = SchedulePipBuilder(pipBuilderA).Process;

            var opsB = new[]
            {
                Operation.WriteFile(CreateOutputFileArtifact())
            };

            var pipBuilderB = CreatePipBuilder(opsB);
            pipBuilderB.Priority = 1;
            pipBuilderB.AddInputFile(pipA.GetOutputs().First());

            Process pipB = SchedulePipBuilder(pipBuilderB).Process;

            var scheduleResult = RunScheduler();
            scheduleResult.AssertFailure();
            AssertErrorEventLogged(LogEventId.PipProcessError);
        }
    }
}
