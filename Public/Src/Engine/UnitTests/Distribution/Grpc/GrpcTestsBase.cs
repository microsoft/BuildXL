// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Distribution.Grpc;
using BuildXL.Engine.Distribution;
using BuildXL.Engine.Distribution.Grpc;
using BuildXL.Scheduler;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using static BuildXL.Engine.Distribution.Grpc.ClientConnectionManager;
using static BuildXL.Engine.Distribution.Grpc.ClientConnectionManager.ConnectionFailureEventArgs;
using static BuildXL.Engine.Distribution.RemoteWorker;

namespace Test.BuildXL.Distribution
{
    public class GrpcTestsBase : XunitBuildXLTest
    {
        protected ConfigurationImpl Configuration;
        
        // Passing 0 as the port value makes gRPC pick an unused port
        private const int PickUnused = 0;        

        public GrpcTestsBase(ITestOutputHelper output) : base(output)
        {
            RegisterEventSource(global::BuildXL.Engine.ETWLogger.Log);

            // Reset default static values between tests
            ResetDefaultSettings();
        }

        protected static void ResetDefaultSettings()
        {
            EngineEnvironmentSettings.DistributionConnectTimeout.Value = EngineEnvironmentSettings.DefaultDistributionConnectTimeout;
            EngineEnvironmentSettings.WorkerAttachTimeout.Value = EngineEnvironmentSettings.DefaultWorkerAttachTimeout;
        }

        internal abstract class DistributedActorHarness
        {
            protected LoggingContext LoggingContext;
            protected DistributedInvocationId InvocationId;

            public DistributedActorHarness(LoggingContext loggingContext, DistributedInvocationId invocationId)
            {
                LoggingContext = loggingContext;
                InvocationId = invocationId;
            }

            /// <summary>
            /// Stop all active services started by this harness
            /// </summary>
            public abstract Task StopAllServicesAsync();
        }

        internal interface ITestDistributedClient
        {
            /// <summary>
            /// A task that completes after  the client shuts down (i.e., after Exit is called or if a connection failure event is raised)
            /// A value of false indicates that we shut down with failure
            /// </summary>
            Task<bool> ClientShutdownTask { get; }

            Optional<ConnectionFailureType> ClientConnectionFailure { get;  }

            void StartClient(int port);
        }

        /// <summary>
        /// A worker machine, which starts a server to listen to the orchestrator
        /// and a client to send messages to it
        /// </summary>
        internal class WorkerHarness : DistributedActorHarness, ITestDistributedClient, IWorkerService
        {
            public GrpcOrchestratorClient Client { get; private set; }

            /// <inheritdoc />
            public Task<bool> ClientShutdownTask => m_clientShutdownTcs.Task;
            private readonly TaskCompletionSource<bool> m_clientShutdownTcs = new TaskCompletionSource<bool>();

            // Used to simulate operations taking a long time so the RPC times out
            private TimeSpan? m_callDelay;

            public Optional<ConnectionFailureType> ClientConnectionFailure { get; private set; }

            public bool ReceivedAttachCall { get; private set; }
            public bool ReceivedExitCall { get; private set; }

            public GrpcWorkerServer Server { get; private set; }

            /// <summary>
            /// Starts server
            /// </summary>
            /// <returns>The port the server is bound</returns>
            public int StartServer()
            {
                Server = new GrpcWorkerServer(this, LoggingContext, InvocationId);
                Server.Start(PickUnused);
                var port = Server.Port;
                Assert.True(port.HasValue);
                return port.Value;
            }

            public void StartClient(int port)
            {
                Client = new GrpcOrchestratorClient(LoggingContext, InvocationId);
                Client.Initialize("localhost", port, OnConnectionFailureAsync);
            }

            public WorkerHarness(LoggingContext loggingContext, DistributedInvocationId invocationId, TimeSpan? callDelay = null) : base(loggingContext, invocationId)
            {
                m_callDelay = callDelay;
            }

            public override async Task StopAllServicesAsync()
            {
                if (Server != null)
                {
                    await Server.ShutdownAsync();
                }

                if (Client != null)
                {
                    await Client.CloseAsync();
                    m_clientShutdownTcs.TrySetResult(true);
                }
            }

            private async void OnConnectionFailureAsync(object sender, ConnectionFailureEventArgs args)
            {
                ClientConnectionFailure = args.Type;
                await Client.CloseAsync();
                m_clientShutdownTcs.TrySetResult(false);
            }

            private void Delay()
            {
                if (m_callDelay.HasValue) {
                    Thread.Sleep(m_callDelay.Value);
                }
            }

            #region IWorkerService methods

            void IWorkerService.Attach(global::BuildXL.Engine.Distribution.OpenBond.BuildStartData buildStartData, string sender)
            {
                Delay();
                ReceivedAttachCall = true;
            }

            void IWorkerService.ExecutePips(global::BuildXL.Engine.Distribution.OpenBond.PipBuildRequest request)
            {
                Delay();
            }

            void IWorkerService.ExitRequested(Optional<string> failure)
            {
                Delay();
                ReceivedExitCall = true;
            }

            #endregion
        }

        /// <summary>
        /// The orchestrator machine, which starts a server to listen to workers 
        /// and manages a number or RemoteWorkerHarnesses to send messages to the different workers
        /// </summary>
        internal class OrchestratorHarness : DistributedActorHarness, IOrchestratorService
        {
            public RemoteWorkerHarness[] RemoteWorkers;
            public int WorkerCount { get; private set; }

            public GrpcOrchestratorServer Server;

            /// <summary>
            /// Starts server
            /// </summary>
            /// <returns>The port the server is bound</returns>
            public int StartServer()
            {
                Server = new GrpcOrchestratorServer(LoggingContext, this, InvocationId);
                Server.Start(PickUnused);
                var port = Server.Port;
                Assert.True(port.HasValue);
                return port.Value;
            }

            public async Task ShutdownServerAsync()
            {
                if (Server != null)
                {
                    await Server.ShutdownAsync();
                }
            }

            public OrchestratorHarness(LoggingContext context, DistributedInvocationId invocationId, int workerCount = 1) : base(context, invocationId)
            {
                RemoteWorkers = new RemoteWorkerHarness[workerCount];
            }

            public RemoteWorkerHarness AddWorker()
            {
                var harness = new RemoteWorkerHarness(LoggingContext, InvocationId);
                AddWorker(harness);
                return harness;
            }

            public void AddWorker(RemoteWorkerHarness harness)
            {
                Contract.Requires(WorkerCount < RemoteWorkers.Length);
                RemoteWorkers[WorkerCount++] = harness;
            }

            public override async Task StopAllServicesAsync()
            {
                await ShutdownServerAsync();
                await TaskUtilities.SafeWhenAll(RemoteWorkers.Where(static rw => rw != null).Select(rw => rw.StopAllServicesAsync()));
            }

            #region IOrchestratorService methods

            void IOrchestratorService.AttachCompleted(global::BuildXL.Engine.Distribution.OpenBond.AttachCompletionInfo attachCompletionInfo)
            {
            }

            Task IOrchestratorService.ReceivedWorkerNotificationAsync(global::BuildXL.Engine.Distribution.OpenBond.WorkerNotificationArgs notification)
            {
                return Task.CompletedTask;
            }

            #endregion
        }

        /// <summary>
        /// Represents one of a number of client instances (see <see cref="RemoteWorker"/>) in the orchestrator machine
        /// Each of these has a client to forward orchestrator messages to a remote worker machine.
        /// </summary>
        internal class RemoteWorkerHarness : DistributedActorHarness, ITestDistributedClient
        {
            public GrpcWorkerClient WorkerClient { get; private set; }

            /// <inheritdoc />
            public Task<bool> ClientShutdownTask => m_shutdownTcs.Task;
            private readonly TaskCompletionSource<bool> m_shutdownTcs = new TaskCompletionSource<bool>();
            private int m_nextPipSequenceNumber = 0;
            public Optional<ConnectionFailureType> ClientConnectionFailure { get; private set; }

            public RemoteWorkerHarness(LoggingContext loggingContext, DistributedInvocationId invocationId) : base(loggingContext, invocationId)
            {
            }

            public void StartClient(int port)
            {
                WorkerClient = new GrpcWorkerClient(LoggingContext, InvocationId, "localhost", port, OnConnectionFailureAsync);
            }

            public Task<RpcCallResult<Unit>> AttachAsync()
            {
                var buildStartData = GrpcMockData.BuildStartData;
                return WorkerClient.AttachAsync(buildStartData.ToOpenBond(), CancellationToken.None);
            }

            public Task<RpcCallResult<Unit>> SendBuildRequestAsync()
            {
                var pipRequest = GrpcMockData.PipBuildRequest(m_nextPipSequenceNumber++, (0, PipExecutionStep.CacheLookup));
                return WorkerClient.ExecutePipsAsync(pipRequest.ToOpenBond(), new List<long> { 0 });
            }

            public async Task<RpcCallResult<Unit>> ExitAsync(bool exitWithFailure = false)
            {
                var buildEndData = new global::BuildXL.Engine.Distribution.OpenBond.BuildEndData()
                {
                    Failure = exitWithFailure ? "Some failure" : null
                };

                var result = await WorkerClient.ExitAsync(buildEndData, CancellationToken.None);
                m_shutdownTcs.TrySetResult(exitWithFailure);
                return result;
            }

            private async void OnConnectionFailureAsync(object sender, ConnectionFailureEventArgs args)
            {
                await WorkerClient.CloseAsync();
                ClientConnectionFailure = args.Type;
                args.Log(LoggingContext, "TestWorker");
                m_shutdownTcs.TrySetResult(false);
            }

            public override async Task StopAllServicesAsync()
            {
                if (WorkerClient != null)
                {
                    await WorkerClient.CloseAsync();
                    m_shutdownTcs.TrySetResult(true);
                }
            }
        }

        /// <summary>
        /// Waits for a client to have a connection failure and asserts this is the case.
        /// This method will fail the test if the connection failure doesn't happen after 10 seconds
        /// </summary>
        internal static async Task ExpectConnectionFailureAsync(ITestDistributedClient client, ConnectionFailureType? expectedFailureType = null)
        {
            var shutdownTask = client.ClientShutdownTask;

            // Pass a dummy delay task so the test doesn't hang. 10 seconds should be enough.
            var completedTaskOrTimeout = await Task.WhenAny(client.ClientShutdownTask, Task.Delay(TimeSpan.FromSeconds(10))); 
            Assert.True(shutdownTask == completedTaskOrTimeout);                              // The shutdown task should be completed

            Assert.True(client.ClientConnectionFailure.HasValue);

            if (expectedFailureType.HasValue)
            {
                Assert.Equal(expectedFailureType.Value, client.ClientConnectionFailure.Value);
            }
        }

        /// <summary>
        /// Utility method to stop all running services at the end of a test
        /// </summary>
        internal static Task StopServicesAsync(params DistributedActorHarness[] harnesses)
        {
            return TaskUtilities.SafeWhenAll(harnesses.Select(static h => h.StopAllServicesAsync()));
        }
    }
}