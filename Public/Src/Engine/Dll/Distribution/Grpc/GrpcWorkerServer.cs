// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using BuildXL.Distribution.Grpc;
using BuildXL.Engine.Cache.Fingerprints;
using Grpc.Core;

namespace BuildXL.Engine.Distribution.Grpc
{
    /// <summary>
    /// GrpcWorker service impl
    /// </summary>
    public class GrpcWorkerServer : Worker.WorkerBase, IServer
    {
        private WorkerService m_workerService;
        private Server m_server;

        /// <summary>
        /// Class constructor
        /// </summary>
        public GrpcWorkerServer(WorkerService workerService)
        {
            m_workerService = workerService;
        }

        /// <nodoc/>
        public void Start(int port)
        {
            m_server = new Server(ClientConnectionManager.DefaultChannelOptions)
            {
                Services = { Worker.BindService(this) },
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

            m_workerService.AttachCore(bondMessage);

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