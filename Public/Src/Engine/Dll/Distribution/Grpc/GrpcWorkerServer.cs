// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Net;
using System.Threading.Tasks;
using BuildXL.Distribution.Grpc;
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
        private readonly WorkerService m_workerService;
        private readonly LoggingContext m_loggingContext;
        private readonly string m_buildId;

        private Server m_server;

        /// <summary>
        /// Class constructor
        /// </summary>
        public GrpcWorkerServer(WorkerService workerService, LoggingContext loggingContext, string buildId)
        {
            m_workerService = workerService;
            m_loggingContext = loggingContext;
            m_buildId = buildId;
        }

        /// <nodoc/>
        public void Start(int port)
        {
            var interceptor = new ServerInterceptor(m_loggingContext, m_buildId);
            m_server = new Server(ClientConnectionManager.DefaultChannelOptions)
            {
                Services = { Worker.BindService(this).Intercept(interceptor) },
                Ports = { new ServerPort(IPAddress.Any.ToString(), port, ServerCredentials.Insecure) },
            };
            m_server.Start();
        }

        /// <nodoc/>
        public void Dispose()
        {
            m_server?.ShutdownAsync().GetAwaiter().GetResult();
        }

        #region Service Methods

        /// <inheritdoc/>
        public override Task<RpcResponse> Attach(BuildStartData message, ServerCallContext context)
        {
            var bondMessage = message.ToOpenBond();

            GrpcSettings.ParseHeader(context.RequestHeaders, out string sender, out string _, out string _);
            
            m_workerService.AttachCore(bondMessage, sender);

            return Task.FromResult(new RpcResponse());
        }

        /// <inheritdoc/>
        public override Task<RpcResponse> ExecutePips(PipBuildRequest message, ServerCallContext context)
        {
            var bondMessage = message.ToOpenBond();

            m_workerService.ExecutePipsCore(bondMessage);
            return Task.FromResult(new RpcResponse());
        }

        /// <inheritdoc/>
        public override Task<RpcResponse> Exit(BuildEndData message, ServerCallContext context)
        {
            m_workerService.BeforeExit();
            m_workerService.Exit(timedOut: false, failure: message.Failure);

            return Task.FromResult(new RpcResponse());
        }

        #endregion Service Methods
    }
}