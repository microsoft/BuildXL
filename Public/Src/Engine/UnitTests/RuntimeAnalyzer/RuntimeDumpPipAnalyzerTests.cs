// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Text.Json;
using BuildXL.Scheduler.Tracing;
using BuildXL.Pips.Operations;
using ProcessEventId = BuildXL.Processes.Tracing.LogEventId;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;
using Test.BuildXL.Scheduler;
using Xunit;
using Xunit.Abstractions;
using Test.BuildXL.Executables.TestProcess;

namespace Test.BuildXL.RuntimeAnalyzer
{
    public class RuntimeDumpPipAnalyzerTests : SchedulerIntegrationTestBase
    {
        public RuntimeDumpPipAnalyzerTests(ITestOutputHelper output) : base(output)
        {
            ShouldCreateLogDir = true;
            Configuration.Logging.DumpFailedPips = true;
        }

        /// <summary>
        /// Verifies that a pip dump has been created when one fails
        /// </summary>
        /// <param name="failPip">Whether to fail the pip or not.</param>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void TestFailingPipDump(bool failPip)
        {
            BaseSetup();

            FileArtifact sourceArtifact = failPip ? FileArtifact.CreateSourceFile(CreateUniqueSourcePath(SourceRootPrefix)) : CreateSourceFile();
            FileArtifact destinationArtifact = CreateOutputFileArtifact();
            var copyFile = CreateCopyFile(sourceArtifact, destinationArtifact);

            PipGraphBuilder.AddCopyFile(copyFile);

            var schedulerResult = RunScheduler();

            var logFolder = Path.Combine(schedulerResult.Config.Logging.LogsDirectory.ToString(Context.PathTable), "FailedPips");
            var pipDumpFile = Path.Combine(logFolder, $"{copyFile.FormattedSemiStableHash}.json");

            if (failPip)
            {
                schedulerResult.AssertFailure();
                SetExpectedFailures(1, 0); // One error logged for the failing pip
                AssertErrorCount();

                Assert.True(Directory.Exists(logFolder));
                Assert.True(File.Exists(pipDumpFile));
                Assert.True(new FileInfo(pipDumpFile).Length > 0); //Ensure that some content was written to the file
            }
            else
            {
                schedulerResult.AssertSuccess();
                // FailedPips log folder should not have been created when we run successfully
                Assert.False(Directory.Exists(logFolder));
                Assert.False(File.Exists(pipDumpFile));
            }
        }

        /// <summary>
        /// Set the log limit to 1, with two failing pips, ensure that the log limit event is logged, and only one file is logged.
        /// </summary>
        [Fact]
        public void TestLogLimit()
        {
            var failingCopyFile1 = new CopyFile(FileArtifact.CreateSourceFile(CreateUniqueSourcePath(SourceRootPrefix)), CreateOutputFileArtifact(), ReadOnlyArray<StringId>.Empty, PipProvenance.CreateDummy(Context));
            var failingCopyFile2 = new CopyFile(FileArtifact.CreateSourceFile(CreateUniqueSourcePath(SourceRootPrefix)), CreateOutputFileArtifact(), ReadOnlyArray<StringId>.Empty, PipProvenance.CreateDummy(Context));
            
            PipGraphBuilder.AddCopyFile(failingCopyFile1);
            PipGraphBuilder.AddCopyFile(failingCopyFile2);

            Configuration.Logging.DumpFailedPipsLogLimit = 1;

            var schedulerResult = RunScheduler().AssertFailure();

            var logFolder = Path.Combine(schedulerResult.Config.Logging.LogsDirectory.ToString(Context.PathTable), "FailedPips");

            SetExpectedFailures(2, 0);
            AssertErrorCount();
            AssertVerboseEventLogged(LogEventId.RuntimeDumpPipLiteLogLimitReached, count: 1);
            Assert.True(Directory.GetFiles(logFolder).Length == 1);
        }

        /// <summary>
        /// Tests dump pip lite with observed file access logging.
        /// </summary>
        [Fact]
        public void TestFailingPipDumpWithObservedFileAccesses()
        {
            Configuration.Logging.DumpFailedPipsWithDynamicData = true;
            Configuration.Sandbox.LogObservedFileAccesses = true;

            var output = CreateOutputFileArtifact();

            var builder = CreatePipBuilder(new[]
            {
                Operation.SpawnExe
                (
                    Context.PathTable,
                    CmdExecutable,
                    string.Format(OperatingSystemHelper.IsUnixOS ? "-c \"echo 'hi' > {0}\"" : "/d /c echo 'hi' > {0}", output.Path.ToString(Context.PathTable))
                ),
                Operation.WriteFile(CreateOutputFileArtifact()),
                Operation.Fail()
            });
            builder.AddInputFile(CmdExecutable);

            var pip = SchedulePipBuilder(builder).Process;
            var schedulerResult = RunScheduler().AssertFailure();

            // Expected to hit these because we made the pip fail
            AssertErrorEventLogged(LogEventId.FileMonitoringError);
            AssertErrorEventLogged(ProcessEventId.PipProcessError);

            var pipDumpFile = Path.Combine(schedulerResult.Config.Logging.LogsDirectory.ToString(Context.PathTable), "FailedPips", $"{pip.FormattedSemiStableHash}.json");
            var serializedPip = File.ReadAllBytes(pipDumpFile);
            var utf8Reader = new Utf8JsonReader(serializedPip);
            var deserialized = JsonSerializer.Deserialize<SerializedPip>(ref utf8Reader);

            Assert.True(File.Exists(pipDumpFile));
            Assert.NotNull(deserialized.ReportedFileAccesses);
            Assert.True(deserialized.ReportedFileAccesses.Count > 0);

            // Ensure that no additional dump pip lite warnings are logging
            AssertWarningCount();
        }

        [Fact]
        public void TestPassingAndFailingPips()
        {
            var existantCopyFileSrc = CreateSourceFile();
            var existantCopyFileDest = CreateOutputFileArtifact();

            File.WriteAllText(existantCopyFileSrc.Path.ToString(Context.PathTable), "copy file test");

            var failingCopyFile = new CopyFile(FileArtifact.CreateSourceFile(CreateUniqueSourcePath(SourceRootPrefix)), CreateOutputFileArtifact(), ReadOnlyArray<StringId>.Empty, PipProvenance.CreateDummy(Context));
            var passingCopyFile = CreateCopyFile(existantCopyFileSrc, existantCopyFileDest);

            PipGraphBuilder.AddCopyFile(failingCopyFile);
            PipGraphBuilder.AddCopyFile(passingCopyFile);

            var schedulerResult = RunScheduler().AssertFailure();
            var logFolder = Path.Combine(schedulerResult.Config.Logging.LogsDirectory.ToString(Context.PathTable), "FailedPips");
            var failingLogFile = Path.Combine(logFolder, $"{failingCopyFile.FormattedSemiStableHash}.json");
            var passingLogFile = Path.Combine(logFolder, $"{passingCopyFile.FormattedSemiStableHash}.json");

            SetExpectedFailures(1, 0); // One error logged for the failing pip
            AssertErrorCount();

            Assert.True(File.Exists(failingLogFile));
            Assert.False(File.Exists(passingLogFile));
        }
    }
}