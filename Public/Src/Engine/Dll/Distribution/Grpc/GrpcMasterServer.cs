// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Net;
using System.Threading.Tasks;
using BuildXL.Distribution.Grpc;
using BuildXL.Engine.Tracing;
using BuildXL.Utilities.Instrumentation.Common;
using Grpc.Core;

namespace BuildXL.Engine.Distribution.Grpc
{
    /// <summary>
    /// GrpcMaster service impl
    /// </summary>
    public sealed class GrpcMasterServer : Master.MasterBase, IServer
    {
        private MasterService m_masterService;
        private Server m_server;
        private LoggingContext m_loggingContext;
        /// <summary>
        /// Class constructor
        /// </summary>
        public GrpcMasterServer(LoggingContext loggingContext, MasterService masterService)
        {
            m_loggingContext = loggingContext;
            m_masterService = masterService;
        }

        /// <nodoc/>
        public void Start(int port)
        {
            m_server = new Server(ClientConnectionManager.DefaultChannelOptions)
            {
                Services = { Master.BindService(this)},
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