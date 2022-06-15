// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
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
        public async Task FireForgetMaterializeOutputsNotReportedBackToOrchestrator()
        {
            Configuration.Distribution.FireForgetMaterializeOutput = true;
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