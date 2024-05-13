// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BuildXL.Execution.Analyzer;
using BuildXL.Pips.Operations;
using BuildXL.Utilities.Core;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using static BuildXL.ToolSupport.CommandLineUtilities;

namespace Test.Tool.Analyzers
{
    /// <summary>
    /// It is hard to force the scheduler to run pips in parallel in a reliable way (the test infra has hooks to force pips to *not* run in parallel, but not the other way around).
    /// So these tests are more on the "smoke test" side of things.
    /// </summary>
    public class ConcurrentPipsAnalyzerTest : AnalyzerTestBase
    {
        public ConcurrentPipsAnalyzerTest(ITestOutputHelper output) : base(output)
        {
            AnalysisMode = AnalysisMode.ConcurrentPipsAnalyzer;
            ModeSpecificDefaultArgs = new List<Option>();
        }

        [Fact]
        public void TestNoConcurrencyForPip()
        {
            Process pip = CreateWriteFilePip();
            var schedulerResult = RunScheduler().AssertSuccess();

            var options = new List<Option>
            {
                new Option()
                {
                    Name="p",
                    Value=pip.FormattedSemiStableHash
                },
            };

            RunAnalyzer(schedulerResult, null, options);

            // The output file should be produced using the default location
            var outputFile = schedulerResult.Config.Logging.LogsDirectory.Combine(Context.PathTable, $"{pip.FormattedSemiStableHash}.txt").ToString(Context.PathTable);
            Assert.True(File.Exists(outputFile));

            // Nothing else should be running
            var dumpString = Encoding.UTF8.GetString(File.ReadAllBytes(outputFile));
            XAssert.Contains(dumpString, "None");
        }

        [Fact]
        public void TestPointTime()
        {
            Process pip = CreateWriteFilePip();
            var schedulerResult = RunScheduler().AssertSuccess();

            var perfInfo = schedulerResult.RunData.RunnablePipPerformanceInfos[pip.PipId];

            // Let's pick a time that points to the middle of the execution of this pip
            var pointTime = perfInfo.CompletedTime - (perfInfo.TotalDuration / 2);

            // Let's pick a time which matches the execution time of this single pip
            var options = new List<Option>
            {
                new Option()
                {
                    Name= "t",
                    Value= pointTime.ToString("dddd, dd MMMM, yyyy HH:mm:ss.fff")
                },
            };

            RunAnalyzer(schedulerResult, null, options);

            // The output file should be produced using the default location
            var outputFile = schedulerResult.Config.Logging.LogsDirectory.Combine(Context.PathTable, 
                $"Time{pointTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.txt").ToString(Context.PathTable);

            Assert.True(File.Exists(outputFile));

            // The above pip should be running
            var dumpString = Encoding.UTF8.GetString(File.ReadAllBytes(outputFile));
            XAssert.Contains(dumpString, pip.GetDescription(Context));
        }

        private Process CreateWriteFilePip()
        {
            var output = CreateOutputFileArtifact();
            var builder = CreatePipBuilder(new[]
            {
                Operation.SpawnExe
                (
                    Context.PathTable,
                    CmdExecutable,
                    string.Format(OperatingSystemHelper.IsUnixOS ? "-c \"echo 'hi' > {0}\"" : "/d /c echo 'hi' > {0}", output.Path.ToString(Context.PathTable))
                )
            });
            builder.AddInputFile(CmdExecutable);
            builder.AddOutputFile(output.Path, FileExistence.Required);

            var pip = SchedulePipBuilder(builder).Process;
            return pip;
        }
    }
}
