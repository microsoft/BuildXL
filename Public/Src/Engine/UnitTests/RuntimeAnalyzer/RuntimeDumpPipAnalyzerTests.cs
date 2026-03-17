// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Linq;
using System.Text.Json;
using BuildXL.Scheduler.Tracing;
using BuildXL.Pips.Operations;
using ProcessEventId = BuildXL.Processes.Tracing.LogEventId;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;
using Test.BuildXL.Scheduler;
using Xunit;
using Test.BuildXL.Executables.TestProcess;
using ProcessesLogEventId = BuildXL.Processes.Tracing.LogEventId;

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

        /// <summary>
        /// Test DumpPipLite run for pips failed due to DFA
        /// </summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void TestDumpPipLiteRunForDFAPips(bool failOnUnexpectedFileAccesses)
        {
            Configuration.Sandbox.UnsafeSandboxConfigurationMutable.UnexpectedFileAccessesAreErrors = failOnUnexpectedFileAccesses;
            // Process depends on unspecified input
            var ops = new Operation[]
            {
                Operation.ReadFile(CreateSourceFile(), doNotInfer: true /* causes unspecified input */ ),
                Operation.WriteFile(CreateOutputFileArtifact())
            };

            var builder = CreatePipBuilder(ops);
            // Create a graph with a Partial SealDirectory
            DirectoryArtifact dir = SealDirectory(SourceRootPath, SealDirectoryKind.Partial /* don't specify input */ );
            builder.AddInputDirectory(dir);

            var processWithOutputs = SchedulePipBuilder(builder);
            
            var schedulerResult = RunScheduler();
            if (failOnUnexpectedFileAccesses)
            {
                // Fail on unspecified input
                schedulerResult.AssertFailure();
                AssertErrorEventLogged(LogEventId.FileMonitoringError);
                AssertWarningEventLogged(LogEventId.ProcessNotStoredToCacheDueToFileMonitoringViolations);
            }
            else
            {
                schedulerResult.AssertSuccess();
                AssertWarningEventLogged(LogEventId.FileMonitoringWarning);
                AssertWarningEventLogged(LogEventId.ProcessNotStoredToCacheDueToFileMonitoringViolations, 2);
            }
            AssertVerboseEventLogged(LogEventId.DisallowedFileAccessInSealedDirectory);
            AssertVerboseEventLogged(ProcessesLogEventId.PipProcessDisallowedFileAccess);
            AssertVerboseEventLogged(LogEventId.DependencyViolationMissingSourceDependency);

            var logFolder = Path.Combine(schedulerResult.Config.Logging.LogsDirectory.ToString(Context.PathTable), "FailedPips");
            var failingLogFile = Path.Combine(logFolder, $"{processWithOutputs.Process.FormattedSemiStableHash}.json");
            
            // Failed pip json should be dumped
            Assert.True(File.Exists(failingLogFile), failingLogFile);
        }

        /// <summary>
        /// Test that pips involved in a dependency violation (double write in a shared opaque)
        /// get their JSON dump created, even when both processes exit successfully (exit code 0).
        /// This covers the scenario where ExecutionLevel stays Executed and
        /// NumFileAccessViolationsNotAllowlisted is 0, but DependencyViolationReported fires.
        /// </summary>
        [Fact]
        public void TestDumpPipForDependencyViolationInSharedOpaque()
        {
            var sharedOpaqueDir = Path.Combine(ObjectRoot, "sharedopaquedir");
            AbsolutePath sharedOpaqueDirPath = AbsolutePath.Create(Context.PathTable, sharedOpaqueDir);
            var sharedOpaqueDirArtifact = DirectoryArtifact.CreateWithZeroPartialSealId(sharedOpaqueDirPath);

            // Both pips write to the same file inside a shared opaque directory
            FileArtifact doubleWriteArtifact = CreateOutputFileArtifact(sharedOpaqueDir);

            // PipA writes the file
            var builderA = CreatePipBuilder(new Operation[]
            {
                Operation.WriteFileWithRetries(doubleWriteArtifact, doNotInfer: true),
                Operation.WriteFile(CreateOutputFileArtifact(ObjectRoot))
            });
            builderA.AddOutputDirectory(sharedOpaqueDirArtifact, SealDirectoryKind.SharedOpaque);
            var resA = SchedulePipBuilder(builderA);

            // PipB writes the same file, creating a double-write violation
            var builderB = CreatePipBuilder(new Operation[]
            {
                Operation.WriteFileWithRetries(doubleWriteArtifact, doNotInfer: true),
            });
            builderB.AddOutputDirectory(sharedOpaqueDirArtifact, SealDirectoryKind.SharedOpaque);
            // Order B after A to avoid file lock contention
            builderB.AddInputFile(resA.ProcessOutputs.GetOutputFiles().Single());
            var resB = SchedulePipBuilder(builderB);

            IgnoreWarnings();
            var schedulerResult = RunScheduler().AssertFailure();

            AssertVerboseEventLogged(LogEventId.DependencyViolationDoubleWrite);
            AssertErrorEventLogged(LogEventId.FileMonitoringError);
            AllowErrorEventMaybeLogged(LogEventId.StorageCachePutContentFailed);
            AllowErrorEventMaybeLogged(LogEventId.ProcessingPipOutputFileFailed);

            var logFolder = Path.Combine(schedulerResult.Config.Logging.LogsDirectory.ToString(Context.PathTable), "FailedPips");

            // Both pips involved in the dependency violation should have their JSON dump created
            var pipADumpFile = Path.Combine(logFolder, $"{resA.Process.FormattedSemiStableHash}.json");
            var pipBDumpFile = Path.Combine(logFolder, $"{resB.Process.FormattedSemiStableHash}.json");

            Assert.True(File.Exists(pipADumpFile), $"Expected dump for pip A (violator or related): {pipADumpFile}");
            Assert.True(File.Exists(pipBDumpFile), $"Expected dump for pip B (violator or related): {pipBDumpFile}");

            // Verify the JSON content contains accurate dependency violation data
            var pipAJson = JsonSerializer.Deserialize<SerializedPip>(File.ReadAllText(pipADumpFile));
            var pipBJson = JsonSerializer.Deserialize<SerializedPip>(File.ReadAllText(pipBDumpFile));

            Assert.NotNull(pipAJson.DependencyViolations);
            Assert.NotNull(pipBJson.DependencyViolations);
            Assert.True(pipAJson.DependencyViolations.Count > 0, "Pip A should have dependency violation data");
            Assert.True(pipBJson.DependencyViolations.Count > 0, "Pip B should have dependency violation data");

            // Both dumps should reference a DoubleWrite violation on the shared file
            var allViolations = pipAJson.DependencyViolations.Concat(pipBJson.DependencyViolations).ToList();
            Assert.Contains(allViolations, v => v.ViolationType == "DoubleWrite");

            // The violation should reference both pip hashes
            var pipAHash = resA.Process.FormattedSemiStableHash;
            var pipBHash = resB.Process.FormattedSemiStableHash;
            Assert.Contains(allViolations, v =>
                (v.ViolatorPipId == pipAHash || v.ViolatorPipId == pipBHash) &&
                (v.RelatedPipId == pipAHash || v.RelatedPipId == pipBHash));

            // The violation path should reference the double-written file
            var doubleWritePath = doubleWriteArtifact.Path.ToString(Context.PathTable);
            Assert.Contains(allViolations, v => v.Path == doubleWritePath);
        }
    }
}
