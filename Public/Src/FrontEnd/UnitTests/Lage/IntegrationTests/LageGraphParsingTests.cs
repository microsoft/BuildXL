// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
        private const string ErrorReport = """{"timestamp":3,"level":10,"msg":"An error reported by Lage"}""";
        private static readonly string s_compactGraph = Graph.Replace("\r", string.Empty).Replace("\n", string.Empty);

        protected override EnginePhases Phase => EnginePhases.Schedule;

        private string PathToLageMock => Path.Combine(TestDeploymentDir, "lage-mock", OperatingSystemHelper.IsWindowsOS ? "lage.exe" : "lage").Replace("\\", "/");

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

            if (warningBeforeGraph)
            {
                Assert.Contains("[Lage warn] Git failed\nUsing all packages, including {A} and \"B\"", AssertGraphConstructionWarning());
            }
        }

        [Theory]
        [InlineData(10, "error")]
        [InlineData(20, "warn")]
        [InlineData(30, null)]
        [InlineData(40, null)]
        [InlineData(50, null)]
        public void DiagnosticsAreClassifiedByLevel(int level, string expectedLevel)
        {
            string diagnostic = $"{{\"timestamp\":1,\"level\":{level},\"msg\":\"Diagnostic mentioning an error\"}}";
            AssertScheduledGraph(RunGraphBuilder(diagnostic + "\n" + s_compactGraph));

            if (expectedLevel == null)
            {
                AssertWarningEventLogged(global::BuildXL.FrontEnd.JavaScript.Tracing.LogEventId.GraphConstructionFinishedSuccessfullyButWithWarnings, count: 0);
            }
            else
            {
                Assert.Contains($"[Lage {expectedLevel}] Diagnostic mentioning an error", AssertGraphConstructionWarning());
            }
        }

        [Fact]
        public void MultipleDiagnosticsAreReportedWithTheirOriginalLevels()
        {
            string output = string.Join("\n", WarningReport, InformationReport, s_compactGraph, ErrorReport);
            AssertScheduledGraph(RunGraphBuilder(output));

            string warning = AssertGraphConstructionWarning();
            Assert.Contains("[Lage warn] Git failed", warning);
            Assert.Contains("[Lage error] An error reported by Lage", warning);
            Assert.DoesNotContain("[Lage info]", warning);
            Assert.Single(warning.Split('\n').Where(line => line.Contains("[Lage warn]")));
            Assert.Single(warning.Split('\n').Where(line => line.Contains("[Lage error]")));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void StandardErrorIsPreserved(bool includeDiagnostic)
        {
            string output = includeDiagnostic ? WarningReport + "\n" + s_compactGraph : s_compactGraph;
            AssertScheduledGraph(RunGraphBuilder(output, standardError: "Existing Lage stderr"));

            string warning = AssertGraphConstructionWarning();
            Assert.Contains("Existing Lage stderr", warning);
            if (includeDiagnostic)
            {
                Assert.Contains("Existing Lage stderr\n[Lage warn] Git failed", warning);
            }
        }

        [Theory]
        [InlineData(1)]
        [InlineData(20)]
        public void NonzeroExitIsRejectedEvenWithAValidGraph(int exitCode)
        {
            // Lage's CLI reports command failures with process.exitCode = 1, independently of JSON log levels:
            // https://github.com/microsoft/lage/blob/e1cfae7beaf573fba1010480290363cbecd92480/packages/cli/src/cli.ts#L23-L36
            AssertGraphConstructionFailed(RunGraphBuilder(s_compactGraph, standardError: "Lage command failed", exitCode: exitCode));
            AssertLogContains(caseSensitive: true, "Lage command failed");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void DiagnosticsAreWrittenToStderrWithoutAnErrorFile(bool omitOptionalArguments)
        {
            Directory.CreateDirectory(SourceRoot);
            string inputFile = Path.Combine(SourceRoot, "lage-output.json");
            string outputGraphFile = Path.Combine(SourceRoot, "graph");
            File.WriteAllText(inputFile, string.Join("\n", WarningReport, s_compactGraph, ErrorReport));

            string adapterPath = Path.Combine(TestDeploymentDir, "tools", "LageGraphBuilder", "main.js").Replace("\\", "/");
            var startInfo = new ProcessStartInfo
            {
                FileName = PathToNode,
                Arguments = $"\"{adapterPath}\" \"{SourceRoot.Replace("\\", "/")}\" \"{outputGraphFile.Replace("\\", "/")}\" \"undefined\" \"build\" \"{PathToLageMock}\""
                    + (omitOptionalArguments ? string.Empty : " \"undefined\" false"),
                WorkingDirectory = SourceRoot,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            startInfo.Environment["LAGE_BUILD_GRAPH_MOCK_OUTPUT_FILE"] = inputFile;
            startInfo.Environment["LAGE_BUILD_GRAPH_MOCK_EXIT_CODE"] = "0";
            startInfo.Environment.Remove("LAGE_BUILD_GRAPH_MOCK_ERROR_FILE");

            using var process = Process.Start(startInfo);
            process.StandardInput.Close();
            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            bool exited = process.WaitForExit(60_000);
            if (!exited)
            {
                process.Kill();
            }

            Assert.True(exited, "The Lage graph adapter did not exit within one minute.");
            string error = standardError.GetAwaiter().GetResult();
            Assert.True(process.ExitCode == 0, $"{standardOutput.GetAwaiter().GetResult()}\n{error}");
            Assert.Contains("[Lage warn] Git failed", error);
            Assert.Contains("[Lage error] An error reported by Lage", error);
            Assert.True(File.Exists(outputGraphFile));
            Assert.False(File.Exists(outputGraphFile + ".err"));
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

        private BuildXLEngineResult RunGraphBuilder(string output, string standardError = "", int exitCode = 0)
        {
            const string outputFile = "lage-output.json";
            const string errorFile = "lage-error.txt";
            var environment = new Dictionary<string, DiscriminatingUnion<string, UnitValue>>
            {
                ["PATH"] = new DiscriminatingUnion<string, UnitValue>(PathToNodeFolder),
                ["LAGE_BUILD_GRAPH_MOCK_OUTPUT_FILE"] = new DiscriminatingUnion<string, UnitValue>(
                    Path.Combine(SourceRoot, outputFile).Replace("\\", "/")),
                ["LAGE_BUILD_GRAPH_MOCK_ERROR_FILE"] = new DiscriminatingUnion<string, UnitValue>(
                    Path.Combine(SourceRoot, errorFile).Replace("\\", "/")),
                ["LAGE_BUILD_GRAPH_MOCK_EXIT_CODE"] = new DiscriminatingUnion<string, UnitValue>(
                    exitCode.ToString(CultureInfo.InvariantCulture)),
            };

            var config = Build(environment: environment, lageLocation: PathToLageMock)
                .AddFile(outputFile, output)
                .AddFile(errorFile, standardError)
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

        private string AssertGraphConstructionWarning()
        {
            var eventId = global::BuildXL.FrontEnd.JavaScript.Tracing.LogEventId.GraphConstructionFinishedSuccessfullyButWithWarnings;
            AssertWarningEventLogged(eventId);
            return Assert.Single(EventListener.GetLogMessagesForEventId((int)eventId));
        }
    }
}
