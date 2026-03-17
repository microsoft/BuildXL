// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities.Core;
using BuildXL.ProcessPipExecutor;
using BuildXL.Scheduler;
using Test.BuildXL.Executables.TestProcess;
using Test.BuildXL.Scheduler;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;


using ProcessesLogEventId = BuildXL.Processes.Tracing.LogEventId;

namespace IntegrationTest.BuildXL.Scheduler
{
    /// <summary>
    /// Tests that pip retry features
    /// </summary>
    public class RetryTests : SchedulerIntegrationTestBase
    {
        public RetryTests(ITestOutputHelper output) : base(output)
        {
        }

        /// <summary>
        /// Ensure that logging matches up and no crashes happen when a pip is retried due to temp directory cleanup errors
        /// </summary>
        [Fact]
        [Feature(Features.PipRetry)]
        public void TempDirectoryRetry()
        {
            // Set the test hook and reset the graph bulider so it uses the context with the newly set test hook
            Context.TestHooks = new TestHooks() { FailDeletingTempDirectory = true };
            ResetPipGraphBuilder();

            // Add a file to the temp directory
            string tempDir = Path.Combine(TestOutputDirectory, "tmp");
            Directory.CreateDirectory(tempDir);
            File.WriteAllText(Path.Combine(tempDir, "test.txt"), "hello");

            // Set up a pip that uses that temp directory to trigger the file to be deleted
            var outFile = CreateOutputFileArtifact();
            var builder = CreatePipBuilder(new Operation[]
            {
                Operation.WriteFile(outFile)
            });
            builder.TempDirectory = AbsolutePath.Create(Context.PathTable, tempDir);
            SchedulePipBuilder(builder);

            // The build is expected to fail, but all of the error logging consistency checks should pass meaning no crash
            RunScheduler().AssertFailure();

            // Expect a warning for each retry and an error for the final failure
            IgnoreWarnings();
            AssertErrorEventLogged(LogEventId.ExcessivePipRetriesDueToRetryableFailures);
        }


        /// <summary>
        /// Verifies that detours injection failures are retried successfully and the build succeeds, without logging errors for the failed attempts.
        /// </summary>
        [TheoryIfSupported(requiresWindowsBasedOperatingSystem: true)]
        [InlineData(2)]
        [InlineData(3)]
        [Feature(Features.PipRetry)]
        public void DetoursInjectionFailureRetriedSuccessfullyNotError(int numOfRetriesBeforeSucceed)
        {
            Context.TestHooks = new TestHooks() { SimulateDetoursInjectionFailureCount = numOfRetriesBeforeSucceed };
            ResetPipGraphBuilder();

            FileArtifact stateFile = FileArtifact.CreateOutputFile(ObjectRootPath.Combine(Context.PathTable, "stateFile.txt"));

            var builder = CreatePipBuilder(new Operation[]
            {
                Operation.WriteFile(CreateOutputFileArtifact()),
                Operation.SucceedOnRetry(stateFile, failExitCode: -1, numberOfRetriesToSucceed: numOfRetriesBeforeSucceed)
            });
            builder.AddUntrackedFile(stateFile.Path);
            SchedulePipBuilder(builder);

            var result = RunScheduler();
            result.AssertSuccess();

            AssertErrorEventLogged(ProcessesLogEventId.PipProcessError, count: 0);
            AssertErrorEventLogged(LogEventId.PipFailedDueToSandboxInternalError, count: 0);      

            AssertVerboseEventLogged(LogEventId.PipProcessRetriedInline, count: numOfRetriesBeforeSucceed);       
        }

        /// <summary>
        /// Verifies that when all retries are exhausted, the final attempt logs PipFailedDueToSandboxInternalError. 
        /// </summary>
        [FactIfSupported(requiresWindowsBasedOperatingSystem: true)]
        [Feature(Features.PipRetry)]
        public void DetoursInjectionFailureExhaustsRetriesLogsError()
        {
            int retriesExceedingMax = PipExecutor.InternalSandboxedProcessExecutionFailureRetryCountMax + 1;
            Context.TestHooks = new TestHooks() { SimulateDetoursInjectionFailureCount = retriesExceedingMax };
            ResetPipGraphBuilder();
            FileArtifact stateFile = FileArtifact.CreateOutputFile(ObjectRootPath.Combine(Context.PathTable, "stateFile.txt"));

            var builder = CreatePipBuilder(new Operation[]
            {
                Operation.WriteFile(CreateOutputFileArtifact()),
                Operation.SucceedOnRetry(stateFile, failExitCode: -1, numberOfRetriesToSucceed: retriesExceedingMax)
            });
            builder.AddUntrackedFile(stateFile.Path);
            SchedulePipBuilder(builder);

            var result = RunScheduler();
            result.AssertFailure();
            AssertErrorEventLogged(ProcessesLogEventId.PipProcessError, count: 0);  // never logged
            AssertErrorEventLogged(LogEventId.PipFailedDueToSandboxInternalError, count: 1); 
        }
    }
}
