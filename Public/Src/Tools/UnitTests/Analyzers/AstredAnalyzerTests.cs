// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using BuildXL.Execution.Analyzer;
using Test.BuildXL.TestUtilities;
using Xunit;
using ReportedProcess = global::BuildXL.Processes.ReportedProcess;

namespace Test.Tool.Analyzers
{
    public class AstredAnalyzerTests
    {
        [Fact]
        public void NormalizeCommandLine()
        {
            using (var testEnv = new TestEnv("mytest", Path.GetTempPath()))
            using (AstredAnalyzer analyzer = new AstredAnalyzer(input: AnalysisInput.CreateForTest(testEnv.PipGraph.Build()), outputFilePath: null, useOriginalPaths: false))
            {
                var responseFile = Path.GetTempFileName();
                try
                {
                    File.WriteAllLines(responseFile, [
                        "/fourth",
                    "/fifth: some option"]);

                    ReportedProcess process = new ReportedProcess(processId: 10, path: "dummy", args: $@"/first ""/second:some path with spaces"" /third @{responseFile}");
                    var normalized = analyzer.NormalizeCommandLine(process);
                    Assert.Equal("/first", normalized[0]);
                    Assert.Equal("/second:some path with spaces", normalized[1]);
                    Assert.Equal("/third", normalized[2]);
                    Assert.Equal("/fourth", normalized[3]);
                    Assert.Equal("/fifth: some option", normalized[4]);
                }
                finally
                {
                    File.Delete(responseFile);
                }
            }
        }

        [Fact]
        public void Csc()
        {
            using (var testEnv = new TestEnv("mytest", Path.GetTempPath()))
            using (AstredAnalyzer analyzer = new AstredAnalyzer(input: AnalysisInput.CreateForTest(testEnv.PipGraph.Build()), outputFilePath: null, useOriginalPaths: false))
            {
                // Test detection of csc.exe process invocation
                Assert.False(analyzer.IsCscInvocation(new ReportedProcess(processId: 10, path: "cl.exe", args: "source.cpp")));
                Assert.True(analyzer.IsCscInvocation(new ReportedProcess(processId: 10, path: Path.Combine("tools", "csc.exe"), args: "source.cpp")));
                Assert.True(analyzer.IsCscInvocation(new ReportedProcess(processId: 10, path: Path.Combine("tools", "dotnet.exe"), args: $"{Path.Combine("tools", "dotnet.exe")} {Path.Combine("tools", "csc.dll")} source.cs")));

                // Validate extraction of sources and defines from csc command line
                AstredAnalyzer.Unit unit = new AstredAnalyzer.Unit();
                analyzer.ExtractFromCscCommandLine(new List<string>
                {
                    @"c:\csc.exe",
                    "/r:System.dll",
                    "/define:DEBUG;TRACE",
                    "Program.cs",
                    "Utils.cs"
                }, unit);

                Assert.Contains("Program.cs", unit.Sources);
                Assert.Contains("Utils.cs", unit.Sources);
                Assert.Contains(new KeyValuePair<string, string>("DEBUG", string.Empty), unit.Defines);
                Assert.Contains(new KeyValuePair<string, string>("TRACE", string.Empty), unit.Defines);
            }
        }

        [Fact]
        public void Cl()
        {
            using (var testEnv = new TestEnv("mytest", Path.GetTempPath()))
            using (AstredAnalyzer analyzer = new AstredAnalyzer(input: AnalysisInput.CreateForTest(testEnv.PipGraph.Build()), outputFilePath: null, useOriginalPaths: false))
            {
                AstredAnalyzer.Unit unit = new AstredAnalyzer.Unit();

                analyzer.ExtractClExeIncludesAndDefines(new List<string>
                {
                    @"c:\cl.exe",
                    "/I C:\\IncludePath1",
                    "/I",
                    "C:\\Include Path2",
                    "-I C:\\IncludePath3",
                    "/DDEBUG=1",
                    "/DTRACE=1",
                    "-DRETAIL=1",
                }, unit);

                Assert.Contains("C:\\IncludePath1", unit.IncludePaths);
                Assert.Contains("C:\\Include Path2", unit.IncludePaths);
                Assert.Contains("C:\\IncludePath3", unit.IncludePaths);
                Assert.Contains(new KeyValuePair<string, string>("DEBUG", "1"), unit.Defines);
                Assert.Contains(new KeyValuePair<string, string>("TRACE", "1"), unit.Defines);
                Assert.Contains(new KeyValuePair<string, string>("RETAIL", "1"), unit.Defines);
            }
        }
    }
}