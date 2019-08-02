// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Linq;
using BuildXL.Analyzers.Core.XLGPlusPlus;
using BuildXL.Execution.Analyzer;
using BuildXL.Pips.Operations;
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
            var uniqueDirPath = Guid.NewGuid().ToString();

            OutputDirPath = Combine(AbsolutePath.Create(Context.PathTable, ObjectRoot), "XlgToDb-", uniqueDirPath);
            TestDirPath = Combine(AbsolutePath.Create(Context.PathTable, SourceRoot), "XlgToDbTest");

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

            var pipA = CreateAndSchedulePipBuilder(new []
            {
                Operation.WriteFile(file),
            }).Process;

            var pipB = CreateAndSchedulePipBuilder(new []
            {
                Operation.ReadFile(file),
                Operation.WriteFile(CreateOutputFileArtifact())
            }).Process;

            var buildA = RunScheduler().AssertSuccess();

            var analyzerRes = RunAnalyzer(buildA).AssertSuccess();

            XAssert.AreNotEqual(Directory.GetFiles(OutputDirPath.ToString(Context.PathTable), "*.sst").Length, 0);
        }

        /// <summary>
        /// This test makes sure that the number of events logged (per event type) match 
        /// what is expected based on the build schedule that we manually populate and build.
        /// </summary>
        [Fact]
        public void DBEventCountVerification()
        {
            Configuration.Logging.LogExecution = true;

            var fileOne = FileArtifact.CreateOutputFile(Combine(TestDirPath, "foo.txt"));
            var fileTwo = FileArtifact.CreateOutputFile(Combine(TestDirPath, "bar.txt"));
            var dirOne = Path.Combine(ReadonlyRoot, "baz");
            Directory.CreateDirectory(dirOne);
            File.WriteAllText(Path.Combine(dirOne, "abc.txt"), "text");
            File.WriteAllText(Path.Combine(dirOne, "xyz.txt"), "text12");

            var pipA = CreateAndSchedulePipBuilder(new []
            {
                Operation.WriteFile(fileOne),
            }).Process;

            var pipB = CreateAndSchedulePipBuilder(new []
            {
                Operation.WriteFile(fileTwo),
            }).Process;

            var pipC = CreateAndSchedulePipBuilder(new []
            {
                Operation.ReadFile(fileOne),
                Operation.WriteFile(CreateOutputFileArtifact())
            }).Process;

            var pipD = CreateAndSchedulePipBuilder(new []
            {
                Operation.EnumerateDir(new DirectoryArtifact(AbsolutePath.Create(Context.PathTable, dirOne), 0, false)),
                Operation.WriteFile(CreateOutputFileArtifact())
            }).Process;

            var buildA = RunScheduler().AssertSuccess();

            var analyzerRes = RunAnalyzer(buildA).AssertSuccess();

            var dataStore = new XldbDataStore(storeDirectory: OutputDirPath.ToString(Context.PathTable));

            XAssert.AreEqual(dataStore.GetFileArtifactContentDecidedEvents().Count(), LastGraph.AllFiles.Count());
            XAssert.AreEqual(dataStore.GetWorkerListEvents().Count(), 0);
            XAssert.AreEqual(dataStore.GetPipExecutionPerformanceEvents().Count(), LastGraph.RetrievePipReferencesOfType(PipType.Process).Count());
            XAssert.AreEqual(dataStore.GetDirectoryMembershipHashedEvents().Count(), 1);
            XAssert.AreEqual(dataStore.GetProcessExecutionMonitoringReportedEvents().Count(), LastGraph.RetrievePipReferencesOfType(PipType.Process).Count());
            XAssert.AreEqual(dataStore.GetProcessFingerprintComputationEvents().Count(), 8);
            XAssert.AreEqual(dataStore.GetExtraEventDataReportedEvents().Count(), 1);
            XAssert.AreEqual(dataStore.GetDependencyViolationReportedEvents().Count(), 0);
            XAssert.AreEqual(dataStore.GetPipExecutionStepPerformanceReportedEvents().Count(), 42);
            XAssert.AreEqual(dataStore.GetPipCacheMissEvents().Count(), 4);
            XAssert.AreEqual(dataStore.GetStatusReportedEvents().Count(), 1);
            XAssert.AreEqual(dataStore.GetBXLInvocationEvents().Count(), 1);
            XAssert.AreEqual(dataStore.GetPipExecutionDirectoryOutputsEvents().Count(), 4);
        }
    }
}
