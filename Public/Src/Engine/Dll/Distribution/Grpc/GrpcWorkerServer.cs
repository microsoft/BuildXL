// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using BuildXL.Distribution.Grpc;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace BuildXL.Engine.Distribution.Grpc
{
    /// <summary>
    /// GrpcWorker service impl
    /// </summary>
    public class GrpcWorkerServer : Worker.WorkerBase, IServer
    {
        private readonly IWorkerService m_workerService;
        private readonly LoggingContext m_loggingContext;
        private readonly DistributedBuildId m_buildId;

        private Server m_server;

        // Expose the port to unit tests
        internal int? Port => m_server?.Ports.FirstOrDefault()?.BoundPort;

        /// <summary>
        /// Class constructor
        /// </summary>
        public GrpcWorkerServer(WorkerService workerService, LoggingContext loggingContext, DistributedBuildId buildId) : this((IWorkerService)workerService, loggingContext, buildId)
        {
        }

        internal GrpcWorkerServer(IWorkerService workerService, LoggingContext loggingContext, DistributedBuildId buildId)
        {
            m_workerService = workerService;
            m_loggingContext = loggingContext;
            m_buildId = buildId;
        }

        /// <nodoc/>
        public void Start(int port)
        {
            var interceptor = new ServerInterceptor(m_loggingContext, m_buildId);
            m_server = new Server(ClientConnectionManager.ServerChannelOptions)
            {
                Services = { Worker.BindService(this).Intercept(interceptor) },
                Ports = { new ServerPort(IPAddress.Any.ToString(), port, ServerCredentials.Insecure) },
            };
            m_server.Start();
        }

        /// <inheritdoc />
        public void Dispose() => DisposeAsync().GetAwaiter().GetResult();

        /// <inheritdoc />
        public Task DisposeAsync() => ShutdownAsync();

        /// <nodoc />
        public async Task ShutdownAsync()
        {
            if (m_server != null)
            {
                try
                {
                    await m_server.ShutdownAsync();
                }
                catch (InvalidOperationException)
                {
                    // Shutdown was already requested
                }
            }
        }

        #region Service Methods
        /// Note: The logic of service methods should be replicated in Test.BuildXL.Distribution.WorkerServerMock
        /// <inheritdoc/>
        public override Task<RpcResponse> Attach(BuildStartData message, ServerCallContext context)
        {
            var bondMessage = message.ToOpenBond();

            GrpcSettings.ParseHeader(context.RequestHeaders, out string sender, out var _, out var _);
            
            m_workerService.Attach(bondMessage, sender);

            return Task.FromResult(new RpcResponse());
        }

        /// <inheritdoc/>
        public override Task<RpcResponse> ExecutePips(PipBuildRequest message, ServerCallContext context)
        {
            var bondMessage = message.ToOpenBond();

            m_workerService.ExecutePips(bondMessage);
            return Task.FromResult(new RpcResponse());
        }

        /// <inheritdoc/>
        public override Task<RpcResponse> Exit(BuildEndData message, ServerCallContext context)
        {
            var failure = string.IsNullOrEmpty(message.Failure) ? Optional<string>.Empty : message.Failure;
            m_workerService.ExitRequested(failure);
            return Task.FromResult(new RpcResponse());
        }

        #endregion Service Methods
    }
}