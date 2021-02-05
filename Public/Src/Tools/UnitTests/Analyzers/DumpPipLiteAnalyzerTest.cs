// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using BuildXL.Execution.Analyzer;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using Xunit;
using Xunit.Abstractions;
using static BuildXL.ToolSupport.CommandLineUtilities;

namespace Test.Tool.Analyzers
{
    public class DumpPipLiteAnalyzerTest : AnalyzerTestBase
    {
        public DumpPipLiteAnalyzerTest(ITestOutputHelper output) : base(output)
        {
            AnalysisMode = AnalysisMode.DumpPipLite;
            ModeSpecificDefaultArgs = new List<Option>();
        }

        /// <summary>
        /// Tests basic functionality with any pip to ensure that is dumped by the analyzer.
        /// </summary>
        [Fact]
        public void TestDumpPipLite()
        {
            var copyFile = new CopyFile(CreateSourceFile(), CreateOutputFileArtifact(), new List<StringId>().ToReadOnlyArray(), PipProvenance.CreateDummy(Context));

            PipGraphBuilder.AddCopyFile(copyFile);

            var schedulerResult = RunScheduler().AssertSuccess();

            var logFolder = Path.Combine(schedulerResult.Config.Logging.LogsDirectory.ToString(Context.PathTable), "FailedPips");
            var pipDumpFile = Path.Combine(logFolder, $"{copyFile.FormattedSemiStableHash}.json");

            List<Option> options = new List<Option>
            {
                new Option()
                {
                    Name="o",
                    Value=schedulerResult.Config.Logging.LogsDirectory.ToString(Context.PathTable)
                },
                new Option()
                {
                    Name="p",
                    Value=copyFile.FormattedSemiStableHash
                }
            };

            RunAnalyzer(schedulerResult, null, options);

            Assert.True(Directory.Exists(logFolder));
            Assert.True(File.Exists(pipDumpFile));
        }

        /// <summary>
        /// Tests the dumpAllFailing pips flag by passing in one passing and one failing pip.
        /// </summary>
        /// <param name="dumpAllFailingPips"></param>
        [Fact]
        public void TestDumpPipLiteWithPassingAndFailingPips()
        {
            var passingCopyFile = new CopyFile(CreateSourceFile(), CreateOutputFileArtifact(), new List<StringId>().ToReadOnlyArray(), PipProvenance.CreateDummy(Context));
            var failingCopyFile = new CopyFile(FileArtifact.CreateSourceFile(CreateUniqueSourcePath(SourceRootPrefix)), CreateOutputFileArtifact(), new List<StringId>().ToReadOnlyArray(), PipProvenance.CreateDummy(Context));

            PipGraphBuilder.AddCopyFile(passingCopyFile);
            PipGraphBuilder.AddCopyFile(failingCopyFile);

            var schedulerResult = RunScheduler().AssertFailure();

            var logFolder = Path.Combine(schedulerResult.Config.Logging.LogsDirectory.ToString(Context.PathTable), "FailedPips");
            var failingPipDumpFile = Path.Combine(logFolder, $"{failingCopyFile.FormattedSemiStableHash}.json");
            var passingPipDumpFile = Path.Combine(logFolder, $"{passingCopyFile.FormattedSemiStableHash}.json");


            List<Option> options = new List<Option>
            {
                new Option()
                {
                    Name="d+"
                }
            };

            RunAnalyzer(schedulerResult, null, options);

            Assert.True(Directory.Exists(logFolder));
            Assert.True(File.Exists(failingPipDumpFile));
            Assert.True(!File.Exists(passingPipDumpFile));
        }
    }
}
