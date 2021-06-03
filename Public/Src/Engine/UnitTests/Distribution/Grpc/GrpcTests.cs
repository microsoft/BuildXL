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
        public async Task GrpcConnectionTimeout()
        {
            EngineEnvironmentSettings.DistributionConnectTimeout.Value = TimeSpan.FromSeconds(0.5);

            // Use a random port which we won't open
            var workerServicePort = 9091;
            var remoteWorker = new RemoteWorkerHarness(LoggingContext, s_defaultDistributedInvocationId);
            remoteWorker.StartClient(workerServicePort);

            // Try to attach to an absent worker. The connection timeout will kick in and the call will fail
            var attachResult = await remoteWorker.AttachAsync();
            Assert.False(attachResult.Succeeded);

            // Timeout event should have been triggered
            await ExpectConnectionFailureAsync(remoteWorker, FailureType.Timeout);

            // If the connection is closed we shouldn't communicate anymore
            attachResult = await remoteWorker.AttachAsync();
            Assert.False(attachResult.Succeeded);
            Assert.Equal(RpcCallResultState.Cancelled, attachResult.State);
            Assert.Equal(1, (int)attachResult.Attempts);
            AssertLogContains(true, "Failed to connect");
            AssertLogContains(true, "Channel has reached Shutdown state");

            await StopServicesAsync(remoteWorker);
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
            await ExpectConnectionFailureAsync(remoteWorkerHarness, FailureType.UnrecoverableFailure);

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