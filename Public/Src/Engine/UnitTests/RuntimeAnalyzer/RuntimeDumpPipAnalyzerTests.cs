// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using BuildXL.Scheduler.Tracing;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using Test.BuildXL.Scheduler;
using Xunit;
using Xunit.Abstractions;

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
    }
}