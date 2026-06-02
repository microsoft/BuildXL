// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using BuildXL.Execution.Analyzer;
using BuildXL.Pips.Builders;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using static BuildXL.ToolSupport.CommandLineUtilities;
using ITestOutputHelper = Xunit.ITestOutputHelper;

namespace Test.Tool.Analyzers
{
    public class DependencyAnalyzerTests : AnalyzerTestBase
    {
        private readonly string m_outputFile;

        public DependencyAnalyzerTests(ITestOutputHelper output) : base(output)
        {
            AnalysisMode = AnalysisMode.DependencyAnalyzer;
            m_outputFile = Path.Combine(TemporaryDirectory, "dependency_analyzer_output.txt");
            ModeSpecificDefaultArgs = new List<Option>
            {
                new Option { Name = "outputFile", Value = m_outputFile }
            };
            ResultFileToRead = m_outputFile;
        }

        [Fact]
        public void ProjectTagIncludedWhenFlagIsSet()
        {
            var outputFile = CreateOutputFileArtifact();
            var builder = CreatePipBuilder(new[]
            {
                Operation.WriteFile(outputFile, "hello"),
            });

            builder.AddTags(Context.StringTable, "project:testproject");

            SchedulePipBuilder(builder);
            var schedulerResult = RunScheduler().AssertSuccess();

            var options = new List<Option>
            {
                new Option { Name = "includeProjectTag" }
            };

            var result = RunAnalyzer(schedulerResult, null, options);
            XAssert.IsTrue(File.Exists(m_outputFile), "Analyzer should produce an output file");
            Assert.Contains("ProjectTag:testproject", result.FileOutput);
        }

        [Fact]
        public void ProjectTagDefaultsToProjectAgnosticWhenNoTag()
        {
            var outputFile = CreateOutputFileArtifact();
            var builder = CreatePipBuilder(new[]
            {
                Operation.WriteFile(outputFile, "hello"),
            });

            SchedulePipBuilder(builder);
            var schedulerResult = RunScheduler().AssertSuccess();

            var options = new List<Option>
            {
                new Option { Name = "includeProjectTag" }
            };

            var result = RunAnalyzer(schedulerResult, null, options);
            XAssert.IsTrue(File.Exists(m_outputFile), "Analyzer should produce an output file");
            Assert.Contains("ProjectTag:ProjectAgnostic", result.FileOutput);
        }

        [Fact]
        public void ProjectTagOmittedWhenFlagNotSet()
        {
            var outputFile = CreateOutputFileArtifact();
            var builder = CreatePipBuilder(new[]
            {
                Operation.WriteFile(outputFile, "hello"),
            });

            builder.AddTags(Context.StringTable, "project:testproject");

            SchedulePipBuilder(builder);
            var schedulerResult = RunScheduler().AssertSuccess();

            var result = RunAnalyzer(schedulerResult);
            XAssert.IsTrue(File.Exists(m_outputFile), "Analyzer should produce an output file");
            Assert.DoesNotContain("ProjectTag:", result.FileOutput);
        }
    }
}
