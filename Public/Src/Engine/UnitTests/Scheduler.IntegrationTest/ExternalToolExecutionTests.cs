// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace IntegrationTest.BuildXL.Scheduler
{
    [TestClassIfSupported(requiresWindowsBasedOperatingSystem: true)]
    public class ExternalToolExecutionTests : SchedulerIntegrationTestBase
    {
        public ExternalToolExecutionTests(ITestOutputHelper output) : base(output)
        {
            Configuration.Sandbox.AdminRequiredProcessExecutionMode = global::BuildXL.Utilities.Configuration.AdminRequiredProcessExecutionMode.ExternalTool;
        }

        [Fact]
        public void RunSingleProcess()
        {
            ProcessBuilder builder = CreatePipBuilder(new[] { Operation.ReadFile(CreateSourceFile()), Operation.WriteFile(CreateOutputFileArtifact()) });
            builder.Options |= Process.Options.RequiresAdmin;
            ProcessWithOutputs process = SchedulePipBuilder(builder);
            RunScheduler().AssertSuccess();
            RunScheduler().AssertCacheHit(process.Process.PipId);
        }

        [Fact]
        public void RunMultipleAdminRequiredProcesses()
        {
            for (int i = 0; i < 5; ++i)
            {
                ProcessBuilder builder = CreatePipBuilder(new[] { Operation.ReadFile(CreateSourceFile()), Operation.WriteFile(CreateOutputFileArtifact()) });
                builder.Options |= Process.Options.RequiresAdmin;
                ProcessWithOutputs process = SchedulePipBuilder(builder);
            }

            RunScheduler().AssertSuccess();
        }

        [Fact]
        public void RunMultipleMixedProcesses()
        {
            for (int i = 0; i < 5; ++i)
            {
                ProcessBuilder builder = CreatePipBuilder(new[] { Operation.ReadFile(CreateSourceFile()), Operation.WriteFile(CreateOutputFileArtifact()) });
                if ((i % 2) == 0)
                {
                    builder.Options |= Process.Options.RequiresAdmin;
                }

                ProcessWithOutputs process = SchedulePipBuilder(builder);
            }

            RunScheduler().AssertSuccess();
        }

        [Fact]
        public void ExternalToolPreserveWarning()
        {
            ProcessBuilder builder = CreatePipBuilder(new[] {
                Operation.ReadFile(CreateSourceFile()),
                Operation.Echo("WARN this is a warning"),
                Operation.WriteFile(CreateOutputFileArtifact()) });
            builder.Options |= Process.Options.RequiresAdmin;
            builder.WarningRegex = new RegexDescriptor(StringId.Create(Context.StringTable, @"^WARN"), System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            ProcessWithOutputs process = SchedulePipBuilder(builder);

            ScheduleRunResult result = RunScheduler().AssertSuccess();
            AssertWarningEventLogged(EventId.PipProcessWarning, count: 1);
        }

        [Fact]
        public void ExecutionRespectFailure()
        {
            ProcessBuilder builder = CreatePipBuilder(new[] {
                Operation.ReadFile(CreateSourceFile()),
                Operation.Fail(),
                Operation.WriteFile(CreateOutputFileArtifact()) });
            builder.Options |= Process.Options.RequiresAdmin;
            ProcessWithOutputs process = SchedulePipBuilder(builder);

            RunScheduler().AssertFailure();
            AssertErrorEventLogged(EventId.PipProcessError, count: 1);
        }

        [Fact]
        public void ExecutionRespectFileAccessManifest()
        {
            ProcessBuilder builder = CreatePipBuilder(new[] { Operation.ReadFile(CreateSourceFile(), doNotInfer: true), Operation.WriteFile(CreateOutputFileArtifact()) });
            builder.Options |= Process.Options.RequiresAdmin;
            ProcessWithOutputs process = SchedulePipBuilder(builder);

            RunScheduler().AssertFailure();
            AssertWarningEventLogged(EventId.ProcessNotStoredToCacheDueToFileMonitoringViolations, count: 1);
            AssertErrorEventLogged(EventId.FileMonitoringError, count: 1);
        }

        [Fact]
        public void ExecutionRecordsReportedFileAccesses()
        {
            FileArtifact sourceFile = CreateSourceFile();

            SealDirectory sourceDirectory = CreateAndScheduleSealDirectory(sourceFile.Path.GetParent(Context.PathTable), SealDirectoryKind.SourceAllDirectories);
            ProcessBuilder builder = CreatePipBuilder(new[] { Operation.ReadFile(sourceFile, doNotInfer: true), Operation.WriteFile(CreateOutputFileArtifact()) });
            builder.AddInputDirectory(sourceDirectory.Directory);
            builder.Options |= Process.Options.RequiresAdmin;
            ProcessWithOutputs process = SchedulePipBuilder(builder);

            RunScheduler().AssertSuccess();
            RunScheduler().AssertCacheHit(process.Process.PipId);

            File.WriteAllText(ArtifactToString(sourceFile), Guid.NewGuid().ToString());

            RunScheduler().AssertCacheMiss(process.Process.PipId);
        }

        [Fact]
        public void ExecutionProcessReadingStdIn()
        {
            FileArtifact stdOut = CreateOutputFileArtifact();
            ProcessBuilder builder = CreatePipBuilder(new[] { Operation.ReadStdIn() });
            PipDataBuilder dataBuilder = new PipDataBuilder(Context.PathTable.StringTable);
            dataBuilder.Add("Data0");
            dataBuilder.Add("Data1");
            dataBuilder.Add("Data2");
            builder.StandardInput = global::BuildXL.Pips.StandardInput.CreateFromData(dataBuilder.ToPipData(Environment.NewLine, PipDataFragmentEscaping.NoEscaping));
            builder.SetStandardOutputFile(stdOut.Path);
            builder.Options |= Process.Options.RequiresAdmin;
            ProcessWithOutputs process = SchedulePipBuilder(builder);

            RunScheduler().AssertSuccess();

            string[] output = File.ReadAllLines(ArtifactToString(stdOut));
            string actualContent = string.Join(Environment.NewLine, output);

            XAssert.AreEqual(3, output.Length, "Actual content: {0}{1}", Environment.NewLine, string.Join(Environment.NewLine, output));
            for (int i = 0; i < 3; ++i)
            {
                XAssert.AreEqual("Data" + i, output[i], "Actual content: {0}", output[i]);
            }
        }

        [Fact]
        public void ExecutionRespectTimeout()
        {
            ProcessBuilder builder = CreatePipBuilder(new[] {
                Operation.ReadFile(CreateSourceFile()),
                Operation.Block(),
                Operation.WriteFile(CreateOutputFileArtifact()) });
            builder.Timeout = TimeSpan.FromSeconds(1);
            builder.Options |= Process.Options.RequiresAdmin;
            
            ProcessWithOutputs process = SchedulePipBuilder(builder);

            RunScheduler().AssertFailure();
            AssertErrorEventLogged(EventId.PipProcessTookTooLongError, count: 1);
            AssertErrorEventLogged(EventId.PipProcessError, count: 1);
        }
    }
}
