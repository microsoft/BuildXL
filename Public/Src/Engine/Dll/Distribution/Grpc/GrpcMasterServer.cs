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
    /// GrpcMaster service impl
    /// </summary>
    public sealed class GrpcMasterServer : Master.MasterBase, IServer
    {
        private readonly MasterService m_masterService;
        private readonly LoggingContext m_loggingContext;
        private readonly string m_buildId;

        private Server m_server;

        /// <summary>
        /// Class constructor
        /// </summary>
        public GrpcMasterServer(LoggingContext loggingContext, MasterService masterService, string buildId)
        {
            m_loggingContext = loggingContext;
            m_masterService = masterService;
            m_buildId = buildId;
        }

        /// <nodoc/>
        public void Start(int port)
        {
            var interceptor = new ServerInterceptor(m_loggingContext, m_buildId);
            m_server = new Server(ClientConnectionManager.DefaultChannelOptions)
            {
                Services = { Master.BindService(this).Intercept(interceptor) },
                Ports = { new ServerPort(IPAddress.Any.ToString(), port, ServerCredentials.Insecure) },
            };
            m_server.Start();
        }

        /// <nodoc/>
        public void Dispose()
        {
            m_server.ShutdownAsync().GetAwaiter().GetResult();
        }

        #region Service Methods

        /// <inheritdoc/>
        public override Task<RpcResponse> AttachCompleted(AttachCompletionInfo message, ServerCallContext context)
        {
            var bondMessage = message.ToOpenBond();

            m_masterService.AttachCompleted(bondMessage);

            return Task.FromResult(new RpcResponse());
        }

        /// <inheritdoc/>
        public override Task<RpcResponse> Notify(WorkerNotificationArgs message, ServerCallContext context)
        {
            var bondMessage = message.ToOpenBond();

            m_masterService.ReceivedWorkerNotificationAsync(bondMessage);
            return Task.FromResult(new RpcResponse());
        }

        #endregion Service Methods
    }
}