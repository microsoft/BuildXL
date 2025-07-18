﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using EngineLogEventId = BuildXL.Engine.Tracing.LogEventId;
using ProcessesLogEventId = BuildXL.Processes.Tracing.LogEventId;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;

namespace ExternalToolTest.BuildXL.Scheduler
{
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
        public void RunProcessReferencingUnsetEnvironmentVariable()
        {
            ProcessBuilder builder = CreatePipBuilder(new[] { Operation.ReadFile(CreateSourceFile()), Operation.WriteFile(CreateOutputFileArtifact()) });
            builder.Options |= Process.Options.RequiresAdmin;
            ProcessWithOutputs process = SchedulePipBuilder(builder);
            // HOMEDRIVE is no longer set by CloudBuild. Test to make sure environment variable forwarding code can handle this
            builder.SetPassthroughEnvironmentVariable(Context.StringTable.AddString("HOMEDRIVE"));
            RunScheduler().AssertSuccess();
            RunScheduler().AssertCacheHit(process.Process.PipId);
        }

        [FactIfSupported(TestRequirements.WindowsOs)] // The test is not working properly on Linux (#2145450).
        public void RunSingleProcessWithSharedOpaqueOutputLogging()
        {
            Configuration.Schedule.UnsafeLazySODeletion = true;

            var sharedOpaqueDir = Path.Combine(ObjectRoot, "partialDir");
            var sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);
            var sharedOpaqueDirectoryArtifact = DirectoryArtifact.CreateWithZeroPartialSealId(sharedOpaqueDirPath);
            var outputInSharedOpaque = CreateOutputFileArtifact(sharedOpaqueDir);
            var source = CreateSourceFile();

            var builder = CreatePipBuilder(new[]
            {
                Operation.WriteFile(outputInSharedOpaque, content: "sod-out", doNotInfer: true)
            });
            builder.AddOutputDirectory(sharedOpaqueDirectoryArtifact, SealDirectoryKind.SharedOpaque);
            builder.Options |= Process.Options.RequiresAdmin;

            var pip = SchedulePipBuilder(builder);

            // run once and assert success
            var result = RunScheduler().AssertSuccess();

            // check that shared opaque outputs have been logged in the sideband file
            AssertWritesJournaled(result, pip, outputInSharedOpaque);

            // run again, assert cache hit, assert sideband files were used to postpone scrubbing
            RunScheduler().AssertCacheHit(pip.Process.PipId);
            AssertWritesJournaled(result, pip, outputInSharedOpaque);
            AssertInformationalEventLogged(EngineLogEventId.PostponingDeletionOfSharedOpaqueOutputs, count: 1);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        public void RunSingleBreakawayProcess()
        {
            var source = CreateSourceFile();
            var output = CreateOutputFileArtifact();

            var builder = CreatePipBuilder(new[]
            {
                Operation.Spawn(
                    Context.PathTable,
                    waitToFinish: true,
                    Operation.ReadFile(source),
                    Operation.WriteFile(output)),

                Operation.AugmentedWrite(output),
                Operation.AugmentedRead(source),
                Operation.WriteFile(CreateOutputFileArtifact(root: null, prefix: "dummy"))
            }) ;

            builder.AddInputFile(source);
            builder.AddOutputFile(output.Path);

            builder.Options |= Process.Options.RequiresAdmin;
            // Configure the test process itself to escape the sandbox
            builder.ChildProcessesToBreakawayFromSandbox = ReadOnlyArray<IBreakawayChildProcess>.FromWithoutCopy(
                new[] { new BreakawayChildProcess() { ProcessName = PathAtom.Create(Context.StringTable, TestProcessToolName) } });

            SchedulePipBuilder(builder);

            // run once and assert success
            RunScheduler().AssertSuccess();
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
            AssertWarningEventLogged(ProcessesLogEventId.PipProcessWarning, count: 1);
        }

        [Fact]
        public void ExecutionRespectFileAccessManifest()
        {
            ProcessBuilder builder = CreatePipBuilder(new[] { Operation.ReadFile(CreateSourceFile(), doNotInfer: true), Operation.WriteFile(CreateOutputFileArtifact()) });
            builder.Options |= Process.Options.RequiresAdmin;
            ProcessWithOutputs process = SchedulePipBuilder(builder);

            RunScheduler().AssertFailure();
            AssertWarningEventLogged(LogEventId.ProcessNotStoredToCacheDueToFileMonitoringViolations, count: 1);
            AssertErrorEventLogged(LogEventId.FileMonitoringError, count: 1);
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
            AssertErrorEventLogged(ProcessesLogEventId.PipProcessTookTooLongError, count: 1);

            if (OperatingSystemHelper.IsUnixOS)
            {
                // Creating dump is not supported on non-Windows.
                AssertWarningEventLogged(ProcessesLogEventId.PipFailedToCreateDumpFile, count: 0, allowMore: true);
            }
        }

        [Fact]
        public void ExecutionUntrackTempFolder()
        {
            AbsolutePath tempDirectory = CreateUniqueDirectory(ObjectRoot);
            FileArtifact tempFile = CreateOutputFileArtifact(tempDirectory);

            ProcessBuilder builder = CreatePipBuilder(new[]
            {
                Operation.ReadFile(CreateSourceFile()),
                Operation.WriteFile(CreateOutputFileArtifact()),
                Operation.WriteFile(tempFile, doNotInfer: true),
                Operation.ReadFile(tempFile, doNotInfer: true)
            });

            builder.Options |= Process.Options.RequiresAdmin;
            builder.SetTempDirectory(DirectoryArtifact.CreateWithZeroPartialSealId(tempDirectory));

            ProcessWithOutputs process = SchedulePipBuilder(builder);

            RunScheduler().AssertSuccess();
            RunScheduler().AssertCacheHit(process.Process.PipId);
        }

        [Fact]
        public void ExecutionWithTraceFile()
        {
            ProcessBuilder builder = CreatePipBuilder(new[] { Operation.ReadFile(CreateSourceFile()), Operation.WriteFile(CreateOutputFileArtifact()) });

            builder.SetTraceFile(CreateUniqueObjPath("trace", Path.Combine(ObjectRoot, "MyTraceDir")));
            builder.Options |= Process.Options.RequiresAdmin;
            ProcessWithOutputs process = SchedulePipBuilder(builder);

            RunScheduler().AssertSuccess();
            Assert.True(File.Exists(ArtifactToString(process.Process.TraceFile)));

            RunScheduler().AssertCacheHit(process.Process.PipId);
            Assert.True(File.Exists(ArtifactToString(process.Process.TraceFile)));
        }
    }
}
