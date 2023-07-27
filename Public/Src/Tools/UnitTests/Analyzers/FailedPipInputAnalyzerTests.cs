// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler;
using BuildXL.Utilities.Core;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities.Xunit;
using BuildXL.Execution.Analyzer;
using Xunit;
using Xunit.Abstractions;
using static BuildXL.ToolSupport.CommandLineUtilities;
using static Test.Tool.Analyzers.AnalyzerTestBase;
using BuildXLConfiguration = BuildXL.Utilities.Configuration;

namespace Test.Tool.Analyzers
{
    public class FailedPipInputAnalyzerTests : AnalyzerTestBase
    {
        public FailedPipInputAnalyzerTests(ITestOutputHelper output) : base(output)
        {
            AnalysisMode = AnalysisMode.FailedPipInput;

            string outputDirectory = Path.Combine(TemporaryDirectory, "FailedPipInputAnalyzerTests");
            Directory.CreateDirectory(outputDirectory);
            ResultFileToRead = Path.Combine(outputDirectory, "FailedPipInputAnalyzerTests.txt");

            ModeSpecificDefaultArgs = new[]
            {
                new Option
                {
                    Name = "outputFile",
                    Value = ResultFileToRead
                }
            };  
        }

        /// <summary>
        /// Test that the analyzer runs for pips succeed with file monitoring violations 
        /// </summary>
        [Fact]
        public void AnalyzerRunForPipsWithDX0268()
        {
            FileArtifact unexpectedFile = CreateSourceFile();
            Process pip = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.WriteFile(CreateOutputFileArtifact()),
                Operation.ReadFile(unexpectedFile, doNotInfer: true)
            }).Process;

            // Pip succeeds with unexpected file access as warning
            ScheduleRunResult buildA = RunScheduler();
            // Run FailedPipInputAnalyzer
            AnalyzerResult analyzerResult = RunAnalyzer(buildA).AssertSuccess();

            // Analyzer report should have pip information
            string fileOutPut = analyzerResult.FileOutput;
            Assert.Contains(pip.FormattedSemiStableHash, fileOutPut,  StringComparison.OrdinalIgnoreCase);
        }
    }
}