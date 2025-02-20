// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Engine;
using BuildXL.Utilities.Configuration;
using Test.BuildXL.FrontEnd.Core;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.FrontEnd.Rush
{
    /// <summary>
    /// For now the build graph plugin is not really available as a plugin. Use a mock to test the behavior of the drop graph.
    /// </summary>
    [Trait("Category", "RushBuildGraphPluginTests")]
    public class RushBuildGraphPluginTests : RushIntegrationTestBase
    {
        
        public RushBuildGraphPluginTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // We don't actually need to execute anything, scheduling is enough
        protected override EnginePhases Phase => EnginePhases.Schedule;

        [Fact]
        public void TestEmptyGraph()
        {
            BuildXLEngineResult result = SchedulePipsUsingBuildGraphPluginMock();

            Assert.True(result.IsSuccess);
            Assert.Equal(0, result.EngineState.PipGraph.RetrieveScheduledPips().Count());
        }

        [Fact]
        public void TestSimpleGraph()
        {
            BuildXLEngineResult result = SchedulePipsUsingBuildGraphPluginMock("A -> B", "A -> C");

            Assert.True(result.IsSuccess);

            // We should be able to retrieve 3 pips
            var pipA = result.EngineState.RetrieveProcess("A", "A");
            var pipB = result.EngineState.RetrieveProcess("B", "B");
            var pipC = result.EngineState.RetrieveProcess("C", "C");

            Assert.True(IsDependencyAndDependent(pipB, pipA));
            Assert.True(IsDependencyAndDependent(pipC, pipA));
        }

        [Fact]
        public void TestDiamondGraph()
        {
            BuildXLEngineResult result = SchedulePipsUsingBuildGraphPluginMock("A -> B1 -> C", "A -> B2 -> C");

            Assert.True(result.IsSuccess);

            // We should be able to retrieve 4 pips
            var pipA = result.EngineState.RetrieveProcess("A", "A");
            var pipB1 = result.EngineState.RetrieveProcess("B1", "B1");
            var pipB2 = result.EngineState.RetrieveProcess("B2", "B2");
            var pipC = result.EngineState.RetrieveProcess("C", "C");

            Assert.True(IsDependencyAndDependent(pipB1, pipA));
            Assert.True(IsDependencyAndDependent(pipC, pipB1));
            Assert.True(IsDependencyAndDependent(pipB2, pipA));
            Assert.True(IsDependencyAndDependent(pipC, pipB2));
        }

        [Theory]
        [InlineData(null, null, "build")]
        [InlineData("'test'", null, "test")]
        [InlineData("'test'", "['production']", "test --production")]
        [InlineData("'test'", "['production', 'non-production']", "test --production --non-production")]
        [InlineData("'test'", "[{name: 'locale', value: 'eng'}]", "test --locale eng")]
        [InlineData("'test'", "['production', {name: 'locale', value: 'eng'}, 'non-production']", "test --production --locale eng --non-production")]
        public void VerifyPluginArguments(string rushCommand, string additionalArgs, string expectedToolArgs)
        {
            BuildXLEngineResult result = SchedulePipsUsingBuildGraphPluginMockWithCommand(rushCommand, additionalArgs);

            Assert.True(result.IsSuccess);
            
            var message = EventListener.GetLogMessagesForEventId((int)global::BuildXL.FrontEnd.JavaScript.Tracing.LogEventId.GraphConstructionFinishedSuccessfullyButWithWarnings).Single();
            Assert.Contains(expectedToolArgs, message);
        }

        [Fact]
        public void TestUncacheableNodes()
        {
            BuildXLEngineResult result = SchedulePipsUsingBuildGraphPluginMockWithUncacheableNodes(
                dependencyChains: new[] { "A -> B", "A -> C" },
                uncacheableNodes: new[] { "B" });
            Assert.True(result.IsSuccess);
            
            // We should be able to retrieve 3 pips
            var pipA = result.EngineState.RetrieveProcess("A", "A");
            var pipB = result.EngineState.RetrieveProcess("B", "B");
            var pipC = result.EngineState.RetrieveProcess("C", "C");

            // B should be uncacheable. A and C should be cacheable.
            Assert.False(pipA.DisableCacheLookup);
            Assert.True(pipB.DisableCacheLookup);
            Assert.False(pipC.DisableCacheLookup);
        }

        private BuildXLEngineResult SchedulePipsUsingBuildGraphPluginMock(params string[] dependencyChains)
        {
            return SchedulePipsUsingBuildGraphPluginMockInternal(rushCommand: null, additionalRushParameter: null, dependencyChains, uncacheableNodes: []);
        }

        private BuildXLEngineResult SchedulePipsUsingBuildGraphPluginMockWithUncacheableNodes(string[] dependencyChains, string[] uncacheableNodes)
        {
            return SchedulePipsUsingBuildGraphPluginMockInternal(rushCommand: null, additionalRushParameter: null, dependencyChains, uncacheableNodes: uncacheableNodes);
        }

        private BuildXLEngineResult SchedulePipsUsingBuildGraphPluginMockWithCommand(string rushCommand, string additionalRushParameter)
        {
            return SchedulePipsUsingBuildGraphPluginMockInternal(rushCommand, additionalRushParameter, dependencyChains: [], uncacheableNodes: []);
        }

        private BuildXLEngineResult SchedulePipsUsingBuildGraphPluginMockInternal(string rushCommand, string additionalRushParameter, string[] dependencyChains, string[] uncacheableNodes)
        {
            // The graph the mock build graph plugin tool generates can be controlled by setting the RUSH_BUILD_GRAPH_MOCK_NODES environment variable.
            var env = dependencyChains.Length == 0 
                ? null 
                : new Dictionary<string, string>() { 
                    ["RUSH_BUILD_GRAPH_MOCK_NODES"] = string.Join(",", dependencyChains),
                    // Windows paths need extra escaping since they end up in a JSON object
                    ["RUSH_BUILD_GRAPH_MOCK_ROOT"] = RelativeSourceRoot.Replace(@"\", @"\\\\"),
                    ["RUSH_BUILD_GRAPH_UNCACHEABLE_NODES"] = string.Join(",", uncacheableNodes)
                };

            var config = Build(
                    rushLocation: PathToBuildGraphPluginMockTool, 
                    rushBaseLibLocation: null, 
                    environment:  env,
                    rushCommand: rushCommand,
                    additionalRushParameters: additionalRushParameter)
                .PersistSpecsAndGetConfiguration();

            // We don't actually need a real rush-based repo to run this test since we are mocking the drop graph tool.
            // But we need a rush.json file to be present in the source directory since the resolver checks for that.
            var pathToRushJson = config.Layout.SourceDirectory.Combine(PathTable, "rush.json").ToString(PathTable);
            File.WriteAllText(pathToRushJson, "{}");

            var result = RunEngine(config);
            return result;
        }
    }
}
