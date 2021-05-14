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

namespace Test.BuildXL.Distribution
{
    public class GrpcTests : GrpcTestsBase
    {
        private const string DefaultActivityId = "81f86bbd-3555-4fff-ad4d-24ce033882a2";
        private static readonly DistributedBuildId s_defaultDistributedBuildId = new(DefaultActivityId, "Test");

        public GrpcTests(ITestOutputHelper output) : base(output)
        {
        }

        [Fact]
        public async Task GrpcConnectionTest()
        {
            var orchestratorHarness = new OrchestratorHarness(LoggingContext, s_defaultDistributedBuildId);

            var workerHarness = new WorkerHarness(LoggingContext, s_defaultDistributedBuildId);
            var workerServicePort = workerHarness.StartServer();

            var remoteWorker = orchestratorHarness.AddWorker(workerServicePort);
            remoteWorker.Start();

            var attachResult = await remoteWorker.AttachAsync();
            Assert.True(attachResult.Succeeded);
            Assert.True(workerHarness.ReceivedAttachCall);

            var exitResult = await remoteWorker.ExitAsync();
            Assert.True(exitResult.Succeeded);
            Assert.True(workerHarness.ReceivedExitCall);

            await StopServices(orchestratorHarness, workerHarness);
            Assert.False(workerHarness.ClientConnectionFailure);
            Assert.False(remoteWorker.HadConnectionFailure);
        }

        [Fact]
        public async Task GrpcConnectionTimeout()
        {
            EngineEnvironmentSettings.DistributionConnectTimeout.Value = TimeSpan.FromSeconds(0.5);

            // Use a random port which we won't open
            var workerServicePort = 9091;
            var remoteWorker = new RemoteWorkerHarness(LoggingContext, s_defaultDistributedBuildId, workerServicePort);
            remoteWorker.Start();

            // Try to attach to an absent worker. The connection timeout will kick in and the call will fail
            var result = await remoteWorker.AttachAsync();
            Assert.False(result.Succeeded);

            // Task should complete on timeout
            var shutdownTask = remoteWorker.ShutdownTask;
            var completedTaskOrTimeout = await Task.WhenAny(shutdownTask, Task.Delay(10000)); // Pass a dummy delay task so the test doesn't hang. 10 seconds should be enough.
            Assert.True(shutdownTask == completedTaskOrTimeout);                              // The shutdown task should be completed

            // Timeout event should have been triggered
            Assert.True(remoteWorker.HadConnectionFailure);
            await StopServices(remoteWorker);
        }

        [Fact]
        public async Task BuildIdMismatch()
        {
            var orchestratorHarness = new OrchestratorHarness(LoggingContext, s_defaultDistributedBuildId);

            var mismatchedId = new DistributedBuildId(Guid.NewGuid().ToString(), "Test");

            var workerHarness = new WorkerHarness(LoggingContext, mismatchedId);
            var workerServicePort = workerHarness.StartServer();

            var remoteWorker = orchestratorHarness.AddWorker(workerServicePort);
            remoteWorker.Start();

            var attachResult = await remoteWorker.AttachAsync();
            Assert.False(attachResult.Succeeded);

            await StopServices(orchestratorHarness, workerHarness);

            AssertLogContains(true, "The receiver and sender build ids do not match");
            Assert.False(workerHarness.ClientConnectionFailure);
            Assert.False(remoteWorker.HadConnectionFailure);
        }
    }

}