// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Engine.Distribution;
using BuildXL.Engine.Distribution.Grpc;
using BuildXL.Utilities.Configuration;
using Xunit;
using Xunit.Abstractions;
using static BuildXL.Engine.Distribution.Grpc.ClientConnectionManager.ConnectionFailureEventArgs;
using static BuildXL.Engine.Distribution.RemoteWorker;

namespace Test.BuildXL.Distribution
{
    public class GrpcTests : GrpcTestsBase
    {
        private const string DefaultActivityId = "81f86bbd-3555-4fff-ad4d-24ce033882a2";
        private static readonly DistributedInvocationId s_defaultDistributedInvocationId = new(DefaultActivityId, "Test");

        public GrpcTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public async Task GrpcConnectionTest()
        {
            var orchestratorHarness = new OrchestratorHarness(LoggingContext, s_defaultDistributedInvocationId);

            var workerHarness = new WorkerHarness(LoggingContext, s_defaultDistributedInvocationId);
            var workerServicePort = workerHarness.StartServer();

            var remoteWorker = orchestratorHarness.AddWorker();
            remoteWorker.StartClient(workerServicePort);

            var attachResult = await remoteWorker.AttachAsync();
            Assert.True(attachResult.Succeeded);
            Assert.True(workerHarness.ReceivedAttachCall);

            var exitResult = await remoteWorker.ExitAsync();
            Assert.True(exitResult.Succeeded);
            Assert.True(workerHarness.ReceivedExitCall);

            await StopServicesAsync(orchestratorHarness, workerHarness);
            Assert.False(workerHarness.ClientConnectionFailure.HasValue);
            Assert.False(remoteWorker.ClientConnectionFailure.HasValue);
        }


        [Fact]
        public async Task ConnectionDoesntTimeoutBeforeAttachment()
        {
            // The ClientConnectionManager uses DistributionConnectTimeout to time out if 
            // noticing reconnection issues. This check is done only after we have successfully established a connection
            // to the other machine: before this happens, we shouldn't hit this time out from the ClientConnectionManager. 
            // Choose 0 as timeout value to ensure that the test fails if we are using it.
            EngineEnvironmentSettings.DistributionConnectTimeout.Value = TimeSpan.Zero;

            // Use a random port which we won't open
            var workerServicePort = 9091;
            var remoteWorker = new RemoteWorkerHarness(LoggingContext, s_defaultDistributedInvocationId);

            remoteWorker.StartClient(workerServicePort);
            var shutdownTask = remoteWorker.ClientShutdownTask;

            var completedTaskOrTimeout = await Task.WhenAny(shutdownTask, Task.Delay(3000)); // Use a dummy delay task so we don't wait forever.
            Assert.False(shutdownTask == completedTaskOrTimeout); // We shouldn't have hit the timeout 

            // Timeout event shouldn't have been triggered
            Assert.False(remoteWorker.ClientConnectionFailure.HasValue);
            await StopServicesAsync(remoteWorker);
        }

        [Fact]
        public async Task CallTimeoutDoesntCauseFailureBeforeAttachment()
        {
            // The ClientConnectionManager uses DistributionConnectTimeout to time out the calls
            EngineEnvironmentSettings.DistributionConnectTimeout.Value = TimeSpan.FromSeconds(1);

            var orchestratorHarness = new OrchestratorHarness(LoggingContext, s_defaultDistributedInvocationId);

            // Make the worker calls take a long time so we hit deadline exceeded
            var workerHarness = new WorkerHarness(LoggingContext, s_defaultDistributedInvocationId, callDelay: TimeSpan.FromSeconds(2));
            var workerServicePort = workerHarness.StartServer();

            var remoteWorker = orchestratorHarness.AddWorker();
            remoteWorker.StartClient(workerServicePort);

            // Attachment will result in timeout
            var attachResult = await remoteWorker.AttachAsync();
            Assert.False(attachResult.Succeeded);
            Assert.Equal(GrpcSettings.MaxAttempts, (int)attachResult.Attempts);

            // The worker should be still alive
            Assert.False(remoteWorker.ClientConnectionFailure.HasValue);

            // With a longer timeout the call will succeed
            EngineEnvironmentSettings.DistributionConnectTimeout.Value = TimeSpan.FromSeconds(10);
            attachResult = await remoteWorker.AttachAsync();
            Assert.True(attachResult.Succeeded);

            // Everything should have worked OK after extending the timeout period
            await StopServicesAsync(orchestratorHarness, workerHarness);
            Assert.False(workerHarness.ClientConnectionFailure.HasValue);
            Assert.False(remoteWorker.ClientConnectionFailure.HasValue);
        }

        [Fact]
        public async Task CallDeadlineExceededCausesFailureAfterAttachment()
        {
            var orchestratorHarness = new OrchestratorHarness(LoggingContext, s_defaultDistributedInvocationId);

            // Make the worker calls take a long time so we hit deadline exceeded after attaching
            var workerHarness = new WorkerHarness(LoggingContext, s_defaultDistributedInvocationId, callDelay: TimeSpan.FromSeconds(2));
            var workerServicePort = workerHarness.StartServer();

            var remoteWorker = orchestratorHarness.AddWorker();
            remoteWorker.StartClient(workerServicePort);

            // Attach
            EngineEnvironmentSettings.DistributionConnectTimeout.Value = TimeSpan.FromSeconds(10);
            var attachResult = await remoteWorker.AttachAsync();
            Assert.True(attachResult.Succeeded);

            // Make the timeout shorter so we time out next
            EngineEnvironmentSettings.DistributionConnectTimeout.Value = TimeSpan.FromSeconds(1);
           
            var callResult = await remoteWorker.SendBuildRequestAsync();

            // Call failed after every attempt
            Assert.Equal(GrpcSettings.MaxAttempts, (int)callResult.Attempts);
            Assert.False(callResult.Succeeded);

            // The connection should have failed and terminated
            await ExpectConnectionFailureAsync(remoteWorker, ConnectionFailureType.CallDeadlineExceeded);
            AssertLogContains(true, "Timed out on a call to the worker. Assuming the worker is dead.");

            await StopServicesAsync(orchestratorHarness, workerHarness);
        }

        [Fact]
        public async Task InvocationIdUnrecoverableMismatch()
        {
            var orchestratorHarness = new OrchestratorHarness(LoggingContext, s_defaultDistributedInvocationId);

            var mismatchedId = new DistributedInvocationId(Guid.NewGuid().ToString(), "Test");

            var workerHarness = new WorkerHarness(LoggingContext, mismatchedId);
            var workerServicePort = workerHarness.StartServer();

            var remoteWorkerHarness = orchestratorHarness.AddWorker();
            remoteWorkerHarness.StartClient(workerServicePort);

            var attachResult = await remoteWorkerHarness.AttachAsync();
            Assert.False(attachResult.Succeeded);
            Assert.Equal(1, (int)attachResult.Attempts);    // The call is not retried

            // The id mismatch should cause an unrecoverable failure which triggers connection shutdown
            await ExpectConnectionFailureAsync(remoteWorkerHarness, ConnectionFailureType.UnrecoverableFailure);

            // Logged id mismatch
            AssertLogContains(true, "The receiver and sender distributed invocation ids do not match");

            await StopServicesAsync(orchestratorHarness, workerHarness);

            Assert.False(workerHarness.ClientConnectionFailure.HasValue);
        }

        [Fact]
        public async Task InvocationIdTolerableMismatch()
        {
            var orchestratorHarness = new OrchestratorHarness(LoggingContext, s_defaultDistributedInvocationId);

            // Use an invocation id that only differs in the environment 
            var mismatchedId = new DistributedInvocationId(s_defaultDistributedInvocationId.RelatedActivityId, s_defaultDistributedInvocationId.Environment + "Junk");

            var workerHarness = new WorkerHarness(LoggingContext, mismatchedId);
            var workerServicePort = workerHarness.StartServer();

            var remoteWorkerHarness = orchestratorHarness.AddWorker();
            remoteWorkerHarness.StartClient(workerServicePort);

            var attachResult = await remoteWorkerHarness.AttachAsync();

            Assert.False(attachResult.Succeeded);
            Assert.Equal(1, (int)attachResult.Attempts);    // the call shouldn't be retried

            attachResult = await remoteWorkerHarness.AttachAsync();

            // Logged id mismatch
            AssertLogContains(true, "The receiver and sender distributed invocation ids do not match");

            // The id mismatch should cause a recoverable failure, so we shouldn't have connection errors
            await StopServicesAsync(orchestratorHarness, workerHarness);
            Assert.False(workerHarness.ClientConnectionFailure.HasValue);
            Assert.False(remoteWorkerHarness.ClientConnectionFailure.HasValue);
        }
    }

}