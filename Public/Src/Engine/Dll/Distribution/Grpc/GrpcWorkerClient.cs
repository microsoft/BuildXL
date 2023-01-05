// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Distribution.Grpc;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;
using Grpc.Core;
using static BuildXL.Engine.Distribution.DistributionHelpers;
using static BuildXL.Engine.Distribution.Grpc.ClientConnectionManager;

namespace BuildXL.Engine.Distribution.Grpc
{
    /// <nodoc/>
    internal sealed class GrpcWorkerClient : IWorkerClient
    {
        private readonly LoggingContext m_loggingContext;
        private readonly EventHandler<ConnectionFailureEventArgs> m_onConnectionFailureAsync;
        private readonly DistributedInvocationId m_invocationId;
        private ClientConnectionManager m_connectionManager;
        private Worker.WorkerClient m_client;

        public GrpcWorkerClient(LoggingContext loggingContext, DistributedInvocationId invocationId, EventHandler<ConnectionFailureEventArgs> onConnectionFailureAsync)
        {
            m_loggingContext = loggingContext;
            m_onConnectionFailureAsync = onConnectionFailureAsync;
            m_invocationId = invocationId;
        }
        
        /// <summary>
        /// Receives a worker location and starts the connection management to that location
        /// </summary>
        public void SetWorkerLocation(ServiceLocation serviceLocation)
        {
            Contract.Assert(m_connectionManager == null, "The worker location can only be set once");
            Contract.Assert(serviceLocation != null);

            m_connectionManager = new ClientConnectionManager(m_loggingContext, serviceLocation.IpAddress, serviceLocation.Port, m_invocationId);
            m_connectionManager.OnConnectionFailureAsync += m_onConnectionFailureAsync;
            m_client = new Worker.WorkerClient(m_connectionManager.Channel);
        }

        public Task CloseAsync()
        {
            return m_connectionManager?.CloseAsync();
        }

        public void Dispose()
        { }

        public async Task<RpcCallResult<Unit>> AttachAsync(BuildStartData message, CancellationToken cancellationToken)
        {
            Contract.Assert(m_connectionManager != null, "The worker location should be known before attaching");

            var attachment = await m_connectionManager.CallAsync(
                (callOptions) => m_client.AttachAsync(message, options: callOptions),
                "Attach",
                cancellationToken,
                waitForConnection: true);

            if (attachment.Succeeded)
            {
                m_connectionManager.OnAttachmentCompleted();
            }

            return attachment;
        }

        public Task<RpcCallResult<Unit>> ExecutePipsAsync(PipBuildRequest message, string description)
        {
            Contract.Assert(m_connectionManager != null, "The worker location should be known if calling ExecutePips");

            return m_connectionManager.CallAsync(
               (callOptions) => m_client.ExecutePipsAsync(message, options: callOptions),
               description);
        }

        public AsyncClientStreamingCall<PipBuildRequest, RpcResponse> StreamExecutePips(CancellationToken cancellationToken = default)
        {
            Contract.Assert(m_connectionManager != null, "The worker location should be known if calling ExecutePips");

            var headerResult = GrpcUtils.InitializeHeaders(m_invocationId);
            return m_client.StreamExecutePips(headers: headerResult.headers, cancellationToken: cancellationToken);
        }

        public Task<RpcCallResult<Unit>> ExitAsync(BuildEndData message, CancellationToken cancellationToken)
        {
            Contract.Assert(m_connectionManager != null, "The worker location should be known if calling Exit");

            m_connectionManager.ReadyForExit();

            return m_connectionManager.CallAsync(
                (callOptions) => m_client.ExitAsync(message, options: callOptions),
                "Exit",
                cancellationToken);
        }
    }
}