// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using System.Text;
using BuildXL.Execution.Analyzer;
using BuildXL.Utilities;
using Test.BuildXL.Executables.TestProcess;
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
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void TestDumpPipLite(bool logObservedFileAccesses)
        {
            Configuration.Sandbox.LogObservedFileAccesses = logObservedFileAccesses;
            Configuration.Sandbox.LogProcessDetouringStatus = logObservedFileAccesses;
            Configuration.Sandbox.LogProcesses = logObservedFileAccesses;

            var output = CreateOutputFileArtifact();
            var builder = CreatePipBuilder(new[]
            {
                Operation.SpawnExe
                (
                    Context.PathTable,
                    CmdExecutable,
                    string.Format(OperatingSystemHelper.IsUnixOS ? "-c \"echo 'hi' > {0}\"" : "/d /c echo 'hi' > {0}", output.Path.ToString(Context.PathTable))
                ),
                Operation.WriteFile(CreateOutputFileArtifact()),
            });
            builder.AddInputFile(CmdExecutable);
            var pip = SchedulePipBuilder(builder).Process;
            var schedulerResult = RunScheduler().AssertFailure();

            var logFolder = Path.Combine(schedulerResult.Config.Logging.LogsDirectory.ToString(Context.PathTable), "FailedPips");
            var pipDumpFile = Path.Combine(logFolder, $"{pip.FormattedSemiStableHash}.json");

            var options = new List<Option>
            {
                new Option()
                {
                    Name="o",
                    Value=schedulerResult.Config.Logging.LogsDirectory.ToString(Context.PathTable)
                },
                new Option()
                {
                    Name="p",
                    Value=pip.FormattedSemiStableHash
                },
                new Option()
                {
                    Name = "dumpObservedFileAccesses" + (logObservedFileAccesses ? "+" : "-")
                }
            };

            RunAnalyzer(schedulerResult, null, options);

            Assert.True(Directory.Exists(logFolder));
            Assert.True(File.Exists(pipDumpFile));

            var dumpString = Encoding.UTF8.GetString(File.ReadAllBytes(pipDumpFile));

            if (logObservedFileAccesses)
            {
                Assert.True(dumpString.Contains("ReportedFileAccesses"));
                // This specific test doesn't appear to report any processes on macos, but should still work for Windows
                if (!OperatingSystemHelper.IsUnixOS)
                {
                    Assert.True(dumpString.Contains("ProcessDetouringStatuses"));
                    Assert.True(dumpString.Contains("ReportedProcesses"));
                }
            }
            else
            {
                Assert.False(dumpString.Contains("ReportedFileAccesses"));
                Assert.False(dumpString.Contains("ProcessDetouringStatuses"));
                Assert.False(dumpString.Contains("ReportedProcesses"));
            }
        }
    }
}
