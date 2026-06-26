// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using BuildXL.Engine.Tracing;
using BuildXL.Scheduler;
using BuildXL.Utilities.Configuration;
using System.Collections.Generic;

namespace Test.BuildXL.Distribution
{
    public sealed class WorkerServiceTests : WorkerServiceTestsBase
    {
        public WorkerServiceTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public async Task WorkerServiceStartAndExit()
        {
            var testRun = await CreateRunAttachAndStart();

            testRun.ReceiveExitCallFromOrchestator();

            await testRun.EndRunAsync();
            
            Assert.True(testRun.NotificationManager.Started);
            AssertCorrectShutdown(testRun);
        }

        [Fact]
        public async Task AttachmentTimeout()
        {
            EngineEnvironmentSettings.WorkerAttachTimeout.Value = TimeSpan.FromSeconds(1);
            
            var testRun = CreateTestRun();
            testRun.WaitForOrchestratorAttach(expectSuccess: false);
            bool success = await testRun.WorkerService.ExitCompletion;
            
            Assert.False(success);
            AssertErrorEventLogged(LogEventId.DistributionWorkerTimeoutFailure);
            Assert.False(testRun.NotificationManager.Started);
            Assert.True(testRun.WorkerServer.ShutdownWasCalled);
        }

        [Fact]
        public async Task RepeatedRequestIsRunOnce()
        {
            var testRun = await CreateRunAttachAndStart();

            var buildRequest = testRun.CreateBuildRequest((1, PipExecutionStep.MaterializeInputs), (2, PipExecutionStep.ExecuteProcess));

            // Send same gRPC message twice
            testRun.ReceiveBuildRequest(buildRequest);
            testRun.ReceiveBuildRequest(buildRequest);
            testRun.WaitForRequestsToBeProcessed(expectedCount: 2);

            // We should only see two pip results reported
            Assert.Equal(2, testRun.NotificationManager.ReportResultCalls);

            testRun.ReceiveBuildRequest((3, PipExecutionStep.CacheLookup), (4, PipExecutionStep.CacheLookup));
            testRun.ReceiveBuildRequest(buildRequest);
            testRun.WaitForRequestsToBeProcessed(expectedCount: 4);
            
            // Only the two new ones should be added
            Assert.Equal(4, testRun.NotificationManager.ReportResultCalls);
            
            testRun.ReceiveExitCallFromOrchestator();
            await testRun.EndRunAsync(true);
            AssertCorrectShutdown(testRun);
        }

        [Fact]
        public async Task MaterializeOutputsNotReportedBackToOrchestrator()
        {
            var testRun = await CreateRunAttachAndStart();

            testRun.ReceiveBuildRequest((1, PipExecutionStep.MaterializeOutputs), (2, PipExecutionStep.MaterializeOutputs));
            testRun.WaitForRequestsToBeProcessed(expectedCount: 2);

            testRun.ReceiveExitCallFromOrchestator();
            await testRun.EndRunAsync();
            
            // Materialize outputs results shouldn't be reported back to the orchestrator
            Assert.Equal(0, testRun.NotificationManager.ReportResultCalls);
            AssertCorrectShutdown(testRun);
        }

        [Fact]
        public async Task FailedPip()
        {
            var testRun = await CreateRunAttachAndStart();

            testRun.PipExecutionService.StepsToFail.Add((1, PipExecutionStep.CacheLookup));
            testRun.ReceiveBuildRequest((1, PipExecutionStep.CacheLookup), (2, PipExecutionStep.CacheLookup));
            testRun.WaitForRequestsToBeProcessed(expectedCount: 2);

            testRun.ReceiveExitCallFromOrchestator();

            // Build should fail
            await testRun.EndRunAsync(expectSuccess: false);

            Assert.Equal(2, testRun.NotificationManager.ReportResultCalls);
            AssertCorrectShutdown(testRun);
        }

        [Fact]
        public async Task FailureFromOrchestrator()
        {
            var testRun = await CreateRunAttachAndStart();

            testRun.ReceiveBuildRequest((1, PipExecutionStep.CacheLookup), (2, PipExecutionStep.CacheLookup));
            testRun.WaitForRequestsToBeProcessed(expectedCount: 2);

            testRun.ReceiveExitCallFromOrchestator(isFailed: true);

            // Build should fail
            await testRun.EndRunAsync(expectSuccess: false);

            AssertErrorEventLogged(LogEventId.DistributionWorkerExitFailure);
            AssertCorrectShutdown(testRun);
        }

        [Fact]
        public async Task EarlyReleaseWhileAttachingDoesntCauseFailure()
        {
            var testRun = CreateTestRun();

            // Attach and exit before triggering the start of the service, which will make
            // the WorkerService finally call AttachCompletedAsync (that we intentionally fail). 
            // This simulates the worker being early-released in the middle of attachment
            // We don't want to fail the build in that case even though the call failed.
            testRun.AttachOrchestrator();
            testRun.ReceiveExitCallFromOrchestator();
            testRun.OrchestratorClient.StartFailing();

            await testRun.StartServiceAsync();
            await testRun.EndRunAsync(expectSuccess: true);
            AssertCorrectShutdown(testRun);
            AssertVerboseEventLogged(LogEventId.AttachmentFailureAfterOrchestratorExit);
        }

        /// <summary>
        /// An explicit signal byte on the orchestrator-termination pipe (written by the runner when its
        /// monitor observes the orchestrator job Failed/Canceled) must drive a clean worker exit through
        /// the same path used by an orchestrator exit RPC.
        /// </summary>
        [Fact]
        public async Task OrchestratorTerminationPipeByteSignalExitsWorker()
        {
            var testRun = CreateTestRunWithTerminationPipe(out var pipeServer);
            using (pipeServer)
            {
                // Runner observed the orchestrator is gone: write the signal byte.
                pipeServer.WriteByte(0x01);
                await pipeServer.FlushAsync();

                var exitTask = testRun.WorkerService.ExitCompletion;
                var winner = await Task.WhenAny(exitTask, Task.Delay(TimeSpan.FromSeconds(30)));
                Assert.True(winner == exitTask, "Worker did not exit after receiving the pipe signal byte.");

                // failure is empty -> a successful (early-released) exit.
                Assert.True(await exitTask);
                Assert.True(testRun.WorkerService.OrchestratorAbandoned);
            }

            AssertInformationalEventLogged(LogEventId.DistributionWorkerExternalTerminationSignalReceived);
        }

        /// <summary>
        /// Closing the orchestrator-termination pipe WITHOUT writing a byte (the runner exited, or the
        /// orchestrator finished -- possibly successfully) is ambiguous and must NOT tear down the worker.
        /// The watcher takes no action, leaving the worker to rely on the normal gRPC heartbeat /
        /// attach-timeout paths.
        ///
        /// Cross-process (production, and the Linux container repro) the worker's inherited read handle is
        /// independent of the runner's write handle, so the close surfaces as a clean EOF (read == 0). In
        /// this in-process test the two ends alias the same OS handle, so disposing the server may instead
        /// fault the pending read. Either way the contract is identical -- the worker must not abandon or
        /// exit -- so this test asserts the behavior rather than which of the two paths was taken.
        /// </summary>
        [Fact]
        public void OrchestratorTerminationPipeCloseWithoutSignalDoesNotExitWorker()
        {
            // Baseline the debug-message count BEFORE closing the pipe so that we wait for the watcher's
            // OWN message rather than returning early on some unrelated message logged during setup.
            var debugMessagesBefore = EventListener.GetEventCount((int)LogEventId.DistributionDebugMessage);

            var testRun = CreateTestRunWithTerminationPipe(out var pipeServer);

            // Close the write end without signaling.
            pipeServer.Dispose();

            // Wait until the watcher has reacted to the close (it logs a debug message and returns).
            var reacted = SpinWait.SpinUntil(
                () => EventListener.GetEventCount((int)LogEventId.DistributionDebugMessage) > debugMessagesBefore,
                TimeSpan.FromSeconds(30));
            Assert.True(reacted, "The orchestrator-termination watcher did not react to the pipe close within the timeout.");

            // The worker took no action: it was neither abandoned nor did it begin exiting.
            Assert.False(testRun.WorkerService.OrchestratorAbandoned);
            Assert.False(testRun.WorkerService.ExitCompletion.IsCompleted);

            AssertVerboseEventLogged(LogEventId.DistributionDebugMessage);
        }

        /// <summary>
        /// CODESYNC: Public/Src/Engine/Dll/Distribution/WorkerService.cs (OrchestratorTerminationPipeEnvVar).
        /// </summary>
        private const string OrchestratorTerminationPipeEnvVar = "BUILDXL_ORCH_TERMINATION_PIPE_HANDLE";

        /// <summary>
        /// Creates a worker test run whose orchestrator-termination watcher is wired to a fresh anonymous
        /// pipe. The returned server stream is the write end the runner would own: write a byte to signal
        /// "orchestrator gone", or dispose it to simulate the runner closing the pipe without signaling (EOF).
        /// </summary>
        private WorkerServiceTestRun CreateTestRunWithTerminationPipe(out AnonymousPipeServerStream pipeServer)
        {
            var server = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.None);
            var previous = Environment.GetEnvironmentVariable(OrchestratorTerminationPipeEnvVar);
            try
            {
                // The watcher reads the env var and opens the client end synchronously in the
                // WorkerService constructor, so it is safe to clear the variable right afterwards.
                Environment.SetEnvironmentVariable(OrchestratorTerminationPipeEnvVar, server.GetClientHandleAsString());
                var testRun = CreateTestRun();
                pipeServer = server;
                return testRun;
            }
            finally
            {
                Environment.SetEnvironmentVariable(OrchestratorTerminationPipeEnvVar, previous);
                // NB: we intentionally do NOT call server.DisposeLocalCopyOfClientHandle() here. Unlike the
                // cross-process production case (where the worker inherits an independent handle), in-process
                // the watcher's client stream aliases the SAME OS handle as the server's client-handle copy,
                // so disposing the server's copy would break the watcher's read. Disposing the whole server
                // stream later is what severs the pipe for the close-without-signal scenario.
            }
        }

        private async Task<WorkerServiceTestRun> CreateRunAttachAndStart()
        {
            var testRun = CreateTestRun();
            var startTask = testRun.StartServiceAsync();
            testRun.AttachOrchestrator();
            await startTask;
            return testRun;
        }

        private void AssertCorrectShutdown(WorkerServiceTestRun testRun)
        {
            Assert.True(testRun.WorkerServer.ShutdownWasCalled);
            Assert.True(testRun.NotificationManager.Exited);
        }
    }
}
