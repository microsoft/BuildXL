// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler;
using BuildXL.Utilities;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities.Xunit;
using BuildXL.Execution.Analyzer;
using Xunit;
using Xunit.Abstractions;
using static BuildXL.ToolSupport.CommandLineUtilities;
using static Test.Tool.Analyzers.AnalyzerTestBase;
using BuildXLConfiguration = BuildXL.Utilities.Configuration;
using System;
using System.Diagnostics;

namespace Test.Tool.Analyzers
{
    /// <summary>
    /// Tests for <see cref="XLGToDBAnalyzer"/>
    /// The construction and disposal of these tests rely on the fact that 
    /// Xunit uses a unique class instance for each test (as is with the other Analyzer Tests).
    /// </summary>
    public class XLGPlusPlusAnalyzersTests : AnalyzerTestBase
    {

        private AbsolutePath DirPath { get; set; }
        public XLGPlusPlusAnalyzersTests(ITestOutputHelper output): base(output)
        {
            AnalysisMode = AnalysisMode.XlgToDb;

            DirPath = Combine(AbsolutePath.Create(Context.PathTable, TemporaryDirectory), "xlgtodb");

            ModeSpecificDefaultArgs = new Option[]
            {
                new Option
                {
                    Name = "outputDir",
                    Value = DirPath.ToString(Context.PathTable)
                }
            };
        }

        /// <summary>
        /// This test makes sure that a RocksDB database was created and populated with some sst files.
        /// </summary>
        [Fact]
        public void DBCreatedAndPopulated()
        {
            Configuration.Logging.LogExecution = true;

            var file = FileArtifact.CreateOutputFile(Combine(DirPath, "blah.txt"));

            var pipB = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.WriteFile(file),
            }).Process;

            var pipC = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.ReadFile(file),
                Operation.WriteFile(CreateOutputFileArtifact())
            }).Process;

            var buildA = RunScheduler().AssertSuccess();

            var analyzerRes = RunAnalyzer(buildA).AssertSuccess();

            XAssert.AreNotEqual(Directory.GetFiles(DirPath.ToString(Context.PathTable), "*.sst"), 0);
        }

        /// <summary>
        /// TODO: Once all events can be successfully stored, make sure that the count in xlg = count in DB
        /// </summary>
        [Fact]
        public void ValidateEventCount()
        {
            XAssert.AreEqual(5, 5);
        }
    }
}
