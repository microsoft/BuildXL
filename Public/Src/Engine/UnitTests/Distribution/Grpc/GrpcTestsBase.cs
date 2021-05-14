// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Distribution.Grpc;
using BuildXL.Engine.Distribution;
using BuildXL.Engine.Distribution.Grpc;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration.Mutable;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;
using static BuildXL.Engine.Distribution.Grpc.ClientConnectionManager;

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
        }

        internal abstract class DistributedActorHarness
        {
            protected LoggingContext LoggingContext;
            protected DistributedBuildId BuildId;

            public DistributedActorHarness(LoggingContext loggingContext, DistributedBuildId buildId)
            {
                LoggingContext = loggingContext;
                BuildId = buildId;
            }

            /// <summary>
            /// Stop all active services started by this harness
            /// </summary>
            public abstract Task StopAllServicesAsync();
        }

        /// <summary>
        /// A worker machine, which starts a server to listen to the orchestrator
        /// and a client to send messages to it
        /// </summary>
        internal class WorkerHarness : DistributedActorHarness, IWorkerService
        {
            public GrpcOrchestratorClient Client { get; private set; }
            public bool ClientConnectionFailure { get; private set; }
            public bool ReceivedAttachCall { get; private set; }
            public bool ReceivedExitCall { get; private set; }

            public GrpcWorkerServer Server { get; private set; }

            /// <summary>
            /// Starts server
            /// </summary>
            /// <returns>The port the server is bound</returns>
            public int StartServer()
            {
                Server = new GrpcWorkerServer(this, LoggingContext, BuildId);
                Server.Start(PickUnused);
                var port = Server.Port;
                Assert.True(port.HasValue);
                return port.Value;
            }

            public void StartClient(int port)
            {
                Client = new GrpcOrchestratorClient(LoggingContext, BuildId);
                Client.Initialize("localhost", port, OnConnectionFailureAsync);
            }

            public WorkerHarness(LoggingContext loggingContext, DistributedBuildId buildId) : base(loggingContext, buildId)
            {
            }

            public override async Task StopAllServicesAsync()
            {
                if (Server != null)
                {
                    await Server.ShutdownAsync();
                }
            }

            private async void OnConnectionFailureAsync(object sender, ConnectionTimeoutEventArgs args)
            {
                ClientConnectionFailure = true;
                await Client.CloseAsync();
            }

            #region IWorkerService methods

            void IWorkerService.Attach(global::BuildXL.Engine.Distribution.OpenBond.BuildStartData buildStartData, string sender)
            {
                ReceivedAttachCall = true;
            }

            void IWorkerService.ExecutePips(global::BuildXL.Engine.Distribution.OpenBond.PipBuildRequest request)
            {
            }

            void IWorkerService.ExitRequested(Optional<string> failure)
            {
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
                Server = new GrpcOrchestratorServer(LoggingContext, this, BuildId);
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

            public OrchestratorHarness(LoggingContext context, DistributedBuildId buildId, int workerCount = 1) : base(context, buildId)
            {
                RemoteWorkers = new RemoteWorkerHarness[workerCount];
            }

            public RemoteWorkerHarness AddWorker(int port)
            {
                var harness = new RemoteWorkerHarness(LoggingContext, BuildId, port);
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
        internal class RemoteWorkerHarness : DistributedActorHarness
        {
            public GrpcWorkerClient WorkerClient { get; private set; }

            /// <summary>
            /// A task that completes after shutdown (i.e., after Exit is called or if a connection failure event is raised)
            /// A value of false indicates that we shut down with failure
            /// </summary>
            public Task<bool> ShutdownTask => m_shutdownTcs.Task;

            public bool HadConnectionFailure { get; private set; }

            private readonly TaskCompletionSource<bool> m_shutdownTcs = new TaskCompletionSource<bool>();
            private readonly int m_port;

            public RemoteWorkerHarness(LoggingContext loggingContext, DistributedBuildId buildId, int port) : base(loggingContext, buildId)
            {
                m_port = port;
            }

            public void Start()
            {
                WorkerClient = new GrpcWorkerClient(LoggingContext, BuildId, "localhost", m_port, OnConnectionFailureAsync);
            }

            public Task<RpcCallResult<Unit>> AttachAsync()
            {
                var buildStartData = GrpcMockData.BuildStartData;
                return WorkerClient.AttachAsync(buildStartData.ToOpenBond(), CancellationToken.None);
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

            private async void OnConnectionFailureAsync(object sender, ConnectionTimeoutEventArgs args)
            {
                await WorkerClient.CloseAsync();
                HadConnectionFailure = true;
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
        /// Utility method to stop all running services at the end of a test
        /// </summary>
        internal static Task StopServices(params DistributedActorHarness[] harnesses)
        {
            return TaskUtilities.SafeWhenAll(harnesses.Select(static h => h.StopAllServicesAsync()));
        }
    }
}