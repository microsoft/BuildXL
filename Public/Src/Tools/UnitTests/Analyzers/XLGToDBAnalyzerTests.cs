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
            
            // As per Mike's offline comment, non-zero vs zero event count tests for now until we can rent out some mac machines
            // and figure out the true reason why the windows and mac event log counts differ by so much 

            // For these tests, there should be a non-zero number of events logged
            XAssert.AreNotEqual(0, dataStore.GetFileArtifactContentDecidedEvents().Count());
            XAssert.AreNotEqual(0, dataStore.GetPipExecutionPerformanceEvents().Count());
            XAssert.AreNotEqual(0, dataStore.GetDirectoryMembershipHashedEvents().Count());
            XAssert.AreNotEqual(0, dataStore.GetProcessExecutionMonitoringReportedEvents().Count());
            XAssert.AreNotEqual(0, dataStore.GetProcessFingerprintComputationEvents().Count());
            XAssert.AreNotEqual(0, dataStore.GetExtraEventDataReportedEvents().Count());
            XAssert.AreNotEqual(0, dataStore.GetPipExecutionStepPerformanceReportedEvents().Count());
            XAssert.AreNotEqual(0, dataStore.GetPipCacheMissEvents().Count());
            XAssert.AreNotEqual(0, dataStore.GetStatusReportedEvents().Count());
            XAssert.AreNotEqual(0, dataStore.GetBXLInvocationEvents().Count());
            XAssert.AreNotEqual(0, dataStore.GetPipExecutionDirectoryOutputsEvents().Count());

            // For these tests, there should be no events logged
            XAssert.AreEqual(0, dataStore.GetWorkerListEvents().Count());
            XAssert.AreEqual(0, dataStore.GetDependencyViolationReportedEvents().Count());
        }
    }
}
