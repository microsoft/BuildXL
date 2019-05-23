// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Engine.Tracing;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities;
using Test.BuildXL.EngineTestUtilities;
using Test.BuildXL.FrontEnd.Ninja;
using Xunit;
using Xunit.Abstractions;

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
        public NinjaIntegrationTest(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public void EndToEndSinglePipExecution()
        {
            var config = BuildAndGetConfiguration(CreateHelloWorldProject("foo.txt"));
            var engineResult = RunEngineWithConfig(config);
            Assert.True(engineResult.IsSuccess);
        }


        [Fact]
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


        [Fact]
        public void OrderOnlyDependenciesHonored()
        {
            var config = BuildAndGetConfiguration(CreateProjectWithOrderOnlyDependencies("first.txt", "boo.txt", "second.txt"));
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
            Assert.True(File.Exists(Path.Combine(SourceRoot, DefaultProjectRoot, "boo.txt")));
        }


        /// <summary>
        /// Leave either projectRoot or specFile unspecified in config.dsc,
        /// in any is absent it can be inferred from the other
        /// </summary>
        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public void CanOmmitSpecFileOrProjectRoot(bool includeProjectRoot, bool includeSpecFile)
        {
            var config = BuildAndGetConfiguration(CreateHelloWorldProject("foo.txt"), includeProjectRoot, includeSpecFile);
            var engineResult = RunEngineWithConfig(config);
            Assert.True(engineResult.IsSuccess);
        }

        [Fact]
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

        [Fact]
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

        [Fact]
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
