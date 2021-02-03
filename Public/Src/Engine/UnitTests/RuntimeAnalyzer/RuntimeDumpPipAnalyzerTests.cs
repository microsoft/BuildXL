// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using BuildXL.Utilities;
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
    }
}