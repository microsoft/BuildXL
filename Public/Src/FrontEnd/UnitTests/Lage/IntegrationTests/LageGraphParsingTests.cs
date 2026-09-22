// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Engine;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Core;
using Test.BuildXL.FrontEnd.Core;
using Xunit;

namespace Test.BuildXL.FrontEnd.Lage
{
    public class LageGraphParsingTests : LageIntegrationTestBase
    {
        private const string Graph = """
            {
              "timestamp": 1,
              "level": 30,
              "msg": "info",
              "data": {
                "command": ["build"],
                "packageTasks": [
                  {
                    "id": "A#build",
                    "package": "A",
                    "task": "build",
                    "command": ["node", "main.js"],
                    "workingDirectory": "src/A",
                    "dependencies": ["B#build"]
                  },
                  {
                    "id": "B#build",
                    "package": "B",
                    "task": "build",
                    "command": ["node", "main.js"],
                    "workingDirectory": "src/B",
                    "dependencies": []
                  }
                ]
              }
            }
            """;

        private const string WarningReport = """{"timestamp":1,"level":20,"msg":"Git failed\nUsing all packages, including {A} and \"B\""}""";
        private const string InformationReport = """{"timestamp":2,"level":30,"msg":"info","data":{"scope":["A","B"]}}""";
        private static readonly string s_compactGraph = Graph.Replace("\r", string.Empty).Replace("\n", string.Empty);

        protected override EnginePhases Phase => EnginePhases.Schedule;

        public LageGraphParsingTests(ITestOutputHelper output) : base(output)
        {
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void SingleGraphIsParsed(bool prettyPrinted)
        {
            AssertScheduledGraph(RunGraphBuilder(prettyPrinted ? Graph : s_compactGraph));
        }

        [Theory]
        [InlineData("\n", true, false)]
        [InlineData("\n", false, true)]
        [InlineData("\n", true, true)]
        [InlineData("\r\n", true, false)]
        [InlineData("\r\n", false, true)]
        [InlineData("\r\n", true, true)]
        public void NewlineSeparatedReportsAreParsed(string newline, bool warningBeforeGraph, bool informationAfterGraph)
        {
            var reports = new List<string>();
            if (warningBeforeGraph)
            {
                reports.Add(WarningReport);
            }

            reports.Add(s_compactGraph);

            if (informationAfterGraph)
            {
                reports.Add(InformationReport);
            }

            string output = " \t" + newline + string.Join(newline + newline, reports) + newline + " ";
            AssertScheduledGraph(RunGraphBuilder(output));
        }

        [Fact]
        public void EmptyGraphIsParsed()
        {
            var result = RunGraphBuilder("""{"timestamp":1,"level":30,"msg":"info","data":{"packageTasks":[]}}""");

            Assert.True(result.IsSuccess);
            Assert.Empty(result.EngineState.RetrieveProcesses());
        }

        [Theory]
        [InlineData("")]
        [InlineData(WarningReport)]
        [InlineData(InformationReport)]
        [InlineData("""{"msg":"info","data":{"packageTasks":{}}}""")]
        public void MissingGraphIsRejected(string output)
        {
            AssertGraphConstructionFailed(RunGraphBuilder(output));
        }

        [Theory]
        [InlineData("not json\n", "")]
        [InlineData("", "\nnot json")]
        [InlineData("", "\n{\"msg\":")]
        public void MalformedReportIsRejected(string prefix, string suffix)
        {
            AssertGraphConstructionFailed(RunGraphBuilder(prefix + s_compactGraph + suffix));
        }

        [Fact]
        public void MultipleGraphsAreRejected()
        {
            AssertGraphConstructionFailed(RunGraphBuilder(s_compactGraph + "\n" + s_compactGraph));
        }

        private BuildXLEngineResult RunGraphBuilder(string output)
        {
            const string outputFile = "lage-output.json";
            var environment = new Dictionary<string, DiscriminatingUnion<string, UnitValue>>
            {
                ["PATH"] = new DiscriminatingUnion<string, UnitValue>(PathToNodeFolder),
                ["LAGE_BUILD_GRAPH_MOCK_OUTPUT_FILE"] = new DiscriminatingUnion<string, UnitValue>(
                    Path.Combine(SourceRoot, outputFile).Replace("\\", "/")),
            };
            string lageLocation = Path.Combine(TestDeploymentDir, "lage-mock", OperatingSystemHelper.IsWindowsOS ? "lage.exe" : "lage").Replace("\\", "/");

            var config = Build(environment: environment, lageLocation: lageLocation)
                .AddFile(outputFile, output)
                .AddJavaScriptProject("A", "src/A")
                .AddJavaScriptProject("B", "src/B")
                .PersistSpecsAndGetConfiguration();

            return RunEngine(config);
        }

        private void AssertScheduledGraph(BuildXLEngineResult result)
        {
            Assert.True(result.IsSuccess);
            Assert.Equal(2, result.EngineState.RetrieveProcesses().Count());

            var projectA = result.EngineState.RetrieveProcess("A#build", "build");
            var projectB = result.EngineState.RetrieveProcess("B#build", "build");
            Assert.NotNull(projectA);
            Assert.NotNull(projectB);
            Assert.True(IsDependencyAndDependent(projectB, projectA));
        }

        private void AssertGraphConstructionFailed(BuildXLEngineResult result)
        {
            Assert.False(result.IsSuccess);
            AssertErrorEventLogged(global::BuildXL.FrontEnd.JavaScript.Tracing.LogEventId.ProjectGraphConstructionError);
            AssertErrorEventLogged(global::BuildXL.FrontEnd.Core.Tracing.LogEventId.CannotBuildWorkspace);
        }
    }
}
