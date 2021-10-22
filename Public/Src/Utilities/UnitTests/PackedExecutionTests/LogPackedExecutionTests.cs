// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.PackedExecution;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Xunit.Abstractions;

namespace Test.Tool.Analyzers
{
    /// <summary>
    /// Tests for <see cref="PackedExecutionExporter"/>.
    /// The construction and disposal of these tests rely on the fact that 
    /// Xunit uses a unique class instance for each test.
    /// /// </summary>
    public class LogPackedExecutionTests : AnalyzerTestBase
    {
        public LogPackedExecutionTests(ITestOutputHelper output) : base(output)
        {
            // the key feature under test
            Configuration.Logging.LogExecution = true;
            Configuration.Logging.LogPackedExecution = true;
        }

        // TODO: determine whether it is practical to get this style of test to work for what this is trying to cover,
        // specifically XLG and PXL log creation. Right now this test does not work (the Scheduler invocation fails
        // and the Configuration.Logging.ExecutionLog property is Invalid), and it is not clear how feasible it is to
        // fix it.
        //[Fact]
        public void TestLogPackedExecution()
        {
            FileArtifact srcA = CreateSourceFile();
            FileArtifact outA = CreateOutputFileArtifact();
            Process pipA = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.ReadFile(srcA),
                Operation.WriteFile(outA)
            }).Process;

            // Make pipB dependent on pipA
            FileArtifact srcB = CreateSourceFile();
            Process pipB = CreateAndSchedulePipBuilder(new Operation[]
            {
                Operation.ReadFile(srcB),
                Operation.ReadFile(outA),
                Operation.WriteFile(CreateOutputFileArtifact())
            }).Process;

            System.Diagnostics.Debugger.Launch();

            ScheduleRunResult result = RunScheduler(); // .AssertCacheMiss(pipA.PipId, pipB.PipId);

            AbsolutePath executionLogPath = Configuration.Logging.ExecutionLog;
            string packedExecutionPath = Path.ChangeExtension(executionLogPath.ToString(Context.PathTable), "PXL"); // Packed eXecution Log

            // Try reading it
            PackedExecution pex = new PackedExecution();
            pex.LoadFromDirectory(packedExecutionPath);

        }
    }
}
