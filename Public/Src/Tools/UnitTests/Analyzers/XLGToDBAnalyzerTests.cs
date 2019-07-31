// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using BuildXL.Execution.Analyzer;
using BuildXL.Utilities;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using static BuildXL.ToolSupport.CommandLineUtilities;

namespace Test.Tool.Analyzers
{
    /// <summary>
    /// Tests for <see cref="XLGToDBAnalyzer"/>
    /// The construction and disposal of these tests rely on the fact that 
    /// Xunit uses a unique class instance for each test (as is with the other Analyzer Tests).
    /// </summary>
    public class XLGToDBAnalyzerTests : AnalyzerTestBase
    {
        private AbsolutePath OutputDirPath { get; set; }
        private AbsolutePath TestDirPath { get; set; }

        public XLGToDBAnalyzerTests(ITestOutputHelper output) : base(output)
        {
            AnalysisMode = AnalysisMode.XlgToDb;

            OutputDirPath = Combine(AbsolutePath.Create(Context.PathTable, TemporaryDirectory), "XlgToDb");
            TestDirPath = Combine(AbsolutePath.Create(Context.PathTable, TemporaryDirectory), "XlgToDbTest");

            ModeSpecificDefaultArgs = new Option[]
            {
                new Option
                {
                    Name = "outputDir",
                    Value = OutputDirPath.ToString(Context.PathTable)
                }
            };
        }

        /// <summary>
        /// This test makes sure that a RocksDB database was created. 
        /// If a DB is created, and events have been populated, one or more sst 
        /// files will also be created, and this test makes sure there is at least one
        /// such sst file that is present.
        /// </summary>
        [Fact]
        public void DBCreatedAndPopulated()
        {
            Configuration.Logging.LogExecution = true;

            var file = FileArtifact.CreateOutputFile(Combine(TestDirPath, "blah.txt"));

            var pipA = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.WriteFile(file),
            }).Process;

            var pipB = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.ReadFile(file),
                Operation.WriteFile(CreateOutputFileArtifact())
            }).Process;

            var buildA = RunScheduler().AssertSuccess();

            var analyzerRes = RunAnalyzer(buildA).AssertSuccess();

            XAssert.AreNotEqual(Directory.GetFiles(OutputDirPath.ToString(Context.PathTable), "*.sst").Length, 0);
        }
    }
}
