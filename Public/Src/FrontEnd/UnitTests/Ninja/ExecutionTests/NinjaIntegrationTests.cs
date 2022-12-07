// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Engine.Tracing;
using BuildXL.Pips;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using Test.BuildXL.EngineTestUtilities;
using Xunit;
using Xunit.Abstractions;
using BuildXL.Engine;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;

namespace Test.BuildXL.FrontEnd.Ninja
{
    /// <summary>
    /// Uses a Ninja resolver to schedule and execute pips based on Ninja build files.
    /// </summary>
    /// <remarks>
    /// These tests actually execute pips, and are therefore expensive
    /// </remarks>
    public class NinjaIntegrationTest : NinjaPipExecutionTestBase
    {
        private const string OutputFileName = "foo.txt";

        public NinjaIntegrationTest(ITestOutputHelper output)
            : base(output)
        {
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)] // The test fails on Linux almost all the time. Work Item - https://dev.azure.com/mseng/1ES/_workitems/edit/2014674"
        public void EndToEndSinglePipExecution()
        {
            var config = BuildAndGetConfiguration(CreateHelloWorldProject(OutputFileName));
            var engineResult = RunEngineWithConfig(config);
            Assert.True(engineResult.IsSuccess);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)] // The test fails on Linux almost all the time. Work Item - https://dev.azure.com/mseng/1ES/_workitems/edit/2014674"
        public void EndToEndExecutionWithDependencies()
        {
            var config = BuildAndGetConfiguration(CreateWriteReadProject("first.txt", "second.txt"));
            var engineResult = RunEngineWithConfig(config);

            Assert.True(engineResult.IsSuccess);
            var pipGraph = engineResult.EngineState.PipGraph;

            // We should find two process pips
            var processPips = pipGraph.RetrievePipsOfType(PipType.Process).ToList();
            Assert.Equal(2, processPips.Count);

            // Check if the dependency is present in the graph
            AssertReachability(processPips, pipGraph, "first.txt", "second.txt");

            
            // Make sure pips ran and the files are there
            Assert.True(File.Exists(Path.Combine(SourceRoot, DefaultProjectRoot, "first.txt")));
            Assert.True(File.Exists(Path.Combine(SourceRoot, DefaultProjectRoot, "second.txt")));
        }

        [TheoryIfSupported(requiresWindowsBasedOperatingSystem: true)] // The test fails on Linux almost all the time. Work Item - https://dev.azure.com/mseng/1ES/_workitems/edit/2014674"
        [InlineData(true)]
        [InlineData(false)]
        public void BuildWithEnvironmentVariables(bool exposeVariable)
        {
            var outContents = "chelo";
            var exposedVariable = exposeVariable ? "MY_VAR" : "OTHER_VAR"; 

            var config = BuildAndGetConfiguration(CreatePrintEnvVariableProject(OutputFileName, exposedVariable), 
                environment: new List<(string, string)> { ("MY_VAR", outContents) });

            var engineResult = RunEngineWithConfig(config);
            Assert.True(engineResult.IsSuccess);

            var outFilePath = Path.Combine(SourceRoot, DefaultProjectRoot, OutputFileName);

            // Defining a custom environment will hide the rest of it
            // The build will print %exposedVariable% but will only have %MY_VAR% set
            // On linux, it will print empty string for unexposed variable
            var expectedContents = exposeVariable ? outContents : OperatingSystemHelper.IsWindowsOS ? "%OTHER_VAR%" : "";

            var contents = File.ReadAllText(outFilePath);

            // Use .Contains() because echo can add whitepsace / newlines
            Assert.True(contents.Contains(expectedContents));
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)] // The test fails on Linux almost all the time. Work Item - https://dev.azure.com/mseng/1ES/_workitems/edit/2014674"
        public void BuildExposesEnvironmentWhenNoVariablesSpecifiedInSpec()
        {
            var expectedOutContents = "chelivery";

            Environment.SetEnvironmentVariable("MY_VAR", expectedOutContents);

            // Build without specifying an environment - the whole environment should be exposed
            var config = BuildAndGetConfiguration(CreatePrintEnvVariableProject(OutputFileName, "MY_VAR"));

            var engineResult = RunEngineWithConfig(config);
            Assert.True(engineResult.IsSuccess);

            var outFilePath = Path.Combine(SourceRoot, DefaultProjectRoot, OutputFileName);

            var actualContents = File.ReadAllText(outFilePath);
            
            // Use .Contains() because echo can add whitepsace / newlines
            Assert.True(actualContents.Contains(expectedOutContents));
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)] // The test fails on Linux almost all the time. Work Item - https://dev.azure.com/mseng/1ES/_workitems/edit/2014674"
        public void BuildWithExplicitEmptyEnvironment()
        {
            var expectedOutContents = "chelivery";

            Environment.SetEnvironmentVariable("MY_VAR", expectedOutContents);

            // Build with an explicit empty environment
            // This means the variable shouldn't be exposed
            var config = BuildAndGetConfiguration(CreatePrintEnvVariableProject(OutputFileName, "MY_VAR"), environment: new List<(string, string)>());

            var engineResult = RunEngineWithConfig(config);
            Assert.True(engineResult.IsSuccess);

            var outFilePath = Path.Combine(SourceRoot, DefaultProjectRoot, OutputFileName);

            var actualContents = File.ReadAllText(outFilePath);

            // Use .Contains() because echo can add whitepsace / newlines
            Assert.False(actualContents.Contains(expectedOutContents));
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)] // The test fails on Linux almost all the time. Work Item - https://dev.azure.com/mseng/1ES/_workitems/edit/2014674"
        public void BuildExposesPassthroughVariables()
        {
            var expectedOutContents = "chelo_passthrough";

            Environment.SetEnvironmentVariable("MY_VAR", expectedOutContents);

            // Build without specifying an environment
            var config = BuildAndGetConfiguration(CreatePrintEnvVariableProject(OutputFileName, "MY_VAR"), passthroughs: new List<string> { "MY_VAR" });

            var engineResult = RunEngineWithConfig(config);
            Assert.True(engineResult.IsSuccess);

            var outFilePath = Path.Combine(SourceRoot, DefaultProjectRoot, OutputFileName);

            var actualContents = File.ReadAllText(outFilePath);

            // Use .Contains() because echo can add whitepsace / newlines
            Assert.True(actualContents.Contains(expectedOutContents));
        }

        [Fact (Skip = "This test needs the double write policy to be set to unsafe. Deleting from a shared opaque is blocked," +
            "this was working before just because a bug was there.")]
        public void EndToEndExecutionWithDeletion()
        {
            var config = BuildAndGetConfiguration(CreateWriteReadDeleteProject("first.txt", "second.txt"));
            var engineResult = RunEngineWithConfig(config);

            Assert.True(engineResult.IsSuccess);
            var pipGraph = engineResult.EngineState.PipGraph;

            // We should find two process pips
            var processPips = pipGraph.RetrievePipsOfType(PipType.Process).ToList();
            Assert.Equal(2, processPips.Count);

            // Make sure pips ran and did its job
            Assert.False(File.Exists(Path.Combine(SourceRoot, DefaultProjectRoot, "first.txt")));
            Assert.True(File.Exists(Path.Combine(SourceRoot, DefaultProjectRoot, "second.txt")));
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)] // The test fails on Linux almost all the time. Work Item - https://dev.azure.com/mseng/1ES/_workitems/edit/2014674"
        public void OrderOnlyDependenciesHonored()
        {
            var config = BuildAndGetConfiguration(CreateProjectWithOrderOnlyDependencies("first.txt", "second.txt"));
            var engineResult = RunEngineWithConfig(config);

            Assert.True(engineResult.IsSuccess);
            var pipGraph = engineResult.EngineState.PipGraph;

            // We should find two process pips
            var processPips = pipGraph.RetrievePipsOfType(PipType.Process).ToList();
            Assert.Equal(2, processPips.Count);

            // Check that the dependency is there
            AssertReachability(processPips, pipGraph, "first.txt", "second.txt");

            // Make sure pips ran and did its job
            Assert.True(File.Exists(Path.Combine(SourceRoot, DefaultProjectRoot, "first.txt")));
            Assert.True(File.Exists(Path.Combine(SourceRoot, DefaultProjectRoot, "second.txt")));
        }


        /// <summary>
        /// Leave either projectRoot or specFile unspecified in config.dsc,
        /// in any is absent it can be inferred from the other
        /// </summary>
        [TheoryIfSupported(requiresWindowsBasedOperatingSystem: true)] // The test fails on Linux almost all the time. Work Item - https://dev.azure.com/mseng/1ES/_workitems/edit/2014674"
        [InlineData(true, false)]
        [InlineData(false, true)]
        public void CanOmmitSpecFileOrProjectRoot(bool includeProjectRoot, bool includeSpecFile)
        {
            var config = BuildAndGetConfiguration(CreateHelloWorldProject(OutputFileName), includeProjectRoot, includeSpecFile);
            var engineResult = RunEngineWithConfig(config);
            Assert.True(engineResult.IsSuccess);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)] // The test fails on Linux almost all the time. Work Item - https://dev.azure.com/mseng/1ES/_workitems/edit/2014674"
        public void PipGraphIsCachedCorrectly()
        {
            var testCache = new TestCache();
            var config = (CommandLineConfiguration)BuildAndGetConfiguration(CreateWriteReadProject("first.txt", "second.txt"));

            config.Cache.CacheGraph = true;
            config.Cache.AllowFetchingCachedGraphFromContentCache = true;
            config.Cache.Incremental = true;

            // First time the graph should be computed
            var engineResult = RunEngineWithConfig(config, testCache);
            Assert.True(engineResult.IsSuccess);

            AssertInformationalEventLogged(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.FrontEndStartEvaluateValues);
            AssertInformationalEventLogged(LogEventId.EndSerializingPipGraph);
            AssertLogContains(false, "Storing pip graph descriptor to cache: Status: Success");

            // The second build should fetch and reuse the graph from the cache
            engineResult = RunEngineWithConfig(config, testCache);
            Assert.True(engineResult.IsSuccess);

            AssertInformationalEventLogged(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.FrontEndStartEvaluateValues, count: 0);
            AssertInformationalEventLogged(LogEventId.EndDeserializingEngineState);
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)] // The test fails on Linux almost all the time. Work Item - https://dev.azure.com/mseng/1ES/_workitems/edit/2014674"
        public void EndToEndExecutionWithResponseFile()
        {
            var responseFileContent = "Hello, world!";
            var rspFile = "resp.txt";
            var output = "out.txt";

            var config = BuildAndGetConfiguration(CreateProjectThatCopiesResponseFile(output, rspFile, responseFileContent));
            var engineResult = RunEngineWithConfig(config);

            Assert.True(engineResult.IsSuccess);

            // Make sure the files are there
            var outputPath = Path.Combine(SourceRoot, DefaultProjectRoot, output);
            var rspFilePath = Path.Combine(SourceRoot, DefaultProjectRoot, rspFile);
            Assert.True(File.Exists(outputPath));
            Assert.True(File.Exists(rspFilePath));

            // Check that the contents were written correctly
            var contents = File.ReadAllText(rspFilePath);
            Assert.Equal(responseFileContent, contents);
        }

        [TheoryIfSupported(requiresWindowsBasedOperatingSystem: true)]
        [InlineData(true)]
        [InlineData(false)]
        public void BuildWithAdditionalOutputDirectory(bool declareAdditionalOutput)
        {
            // The build will create an output file outside of the project root that we declare as the 'standard' cone for outputs
            var extraneousDirectory = Path.Combine(SourceRoot, "ExtraneousOutputs");
            var extraneousOutput = Path.Combine(extraneousDirectory, "foo.txt");
            var project = CreateProjectWithExtraneousWrite("first.txt", "second.txt", extraneousOutput);

            var additionalOutputDirectories = declareAdditionalOutput ? new[] { $"p`{extraneousDirectory}`" } : null;
            var config = BuildAndGetConfiguration(project, additionalOutputDirectories: additionalOutputDirectories);

            var engineResult = RunEngineWithConfig(config);
            if (declareAdditionalOutput)
            {
                Assert.True(engineResult.IsSuccess);
            }
            else
            {
                // If the extraneous directory is not declared as an additional output, the build should fail with monitoring violations
                Assert.False(engineResult.IsSuccess);
                AssertVerboseEventLogged(global::BuildXL.Processes.Tracing.LogEventId.PipProcessDisallowedFileAccess, count: 1);
                AssertErrorEventLogged(global::BuildXL.Scheduler.Tracing.LogEventId.FileMonitoringError, count: 1);
                AssertErrorEventLogged(global::BuildXL.Processes.Tracing.LogEventId.PipProcessError, count: 1);
                AssertVerboseEventLogged(global::BuildXL.Scheduler.Tracing.LogEventId.DependencyViolationUndeclaredOutput, count: 1);
            }
        }

        // Check whether a pip (which produced secondPipOutput) is reachable from another pip (which has to produce firstPipOutput)
        private void AssertReachability(List<Pip> processPips, PipGraph pipGraph, string firstPipOutput, string secondPipOutput)
        {
            var firstPip = processPips.Find(p => p.PipId == FindPipIdThatProducedFile(pipGraph, firstPipOutput));
            var secondPip = processPips.Find(p => p.PipId == FindPipIdThatProducedFile(pipGraph, secondPipOutput));

            Assert.NotNull(firstPip);
            Assert.NotNull(secondPip);

            Assert.True(pipGraph.DataflowGraph.IsReachableFrom(firstPip.PipId.ToNodeId(), secondPip.PipId.ToNodeId(), true));
        }

        // A convoluted way of finding a PipId knowing some output from the pip and assuming the outputs' filenames are unique
        private PipId FindPipIdThatProducedFile(PipGraph pipGraph, string filename)
        {
            foreach (var kvp in pipGraph.AllFilesAndProducers)
            {
                if (kvp.Key.Path.ToString(PathTable).Contains(filename))
                {
                    return kvp.Value;
                }
            }

            return PipId.Invalid;
        }

        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)] // The test fails on Linux almost all the time. Work Item - https://dev.azure.com/mseng/1ES/_workitems/edit/2014674"
        public void DummyTargetsAreOptionalFiles()
        {
            const string PhonyOutputFile1 = "a.txt";
            const string PhonyOutputFile2 = "b.txt";
            const string DummyFile = "dummy.txt";
            const string EffectiveFile = "real.txt";

            var config = BuildAndGetConfiguration(CreateDummyFilePhonyProject(PhonyOutputFile1, PhonyOutputFile2, DummyFile, EffectiveFile));
            var engineResult = RunEngineWithConfig(config);

            Assert.True(engineResult.IsSuccess);

            // The dummy file should be declared as output and be optional. It may or not be present afterwards
            var pipGraph = engineResult.EngineState.PipGraph;
            var pipId = FindPipIdThatProducedFile(pipGraph, DummyFile);
            var pip = pipGraph.GetPipFromPipId(pipId) as Process;
            Assert.False(pip.FileOutputs.Any(file => file.IsRequiredOutputFile));

            // In this case, the dummy file should not exist; implies nothing weird happened
            var dummyOutputPath = Path.Combine(SourceRoot, DefaultProjectRoot, DummyFile);
            Assert.False(File.Exists(dummyOutputPath));

            // The file that is written by the rule that has the dummy dependency must exist; implies execution happened
            var effectiveOutputPath = Path.Combine(SourceRoot, DefaultProjectRoot, EffectiveFile);
            Assert.True(File.Exists(effectiveOutputPath));

        }
    }
}
