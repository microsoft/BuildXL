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
using static BuildXL.Engine.Distribution.Grpc.ClientConnectionManager;

namespace BuildXL.Engine.Distribution.Grpc
{
    /// <nodoc/>
    internal sealed class GrpcOrchestratorClient : IOrchestratorClient
    {
        private readonly DistributedInvocationId m_invocationId;
        private Orchestrator.OrchestratorClient m_client;
        private ClientConnectionManager m_connectionManager;
        private readonly LoggingContext m_loggingContext;
        private volatile bool m_initialized;

        public GrpcOrchestratorClient(LoggingContext loggingContext, DistributedInvocationId invocationId)
        {
            m_invocationId = invocationId;
            m_loggingContext = loggingContext;
        }

        public void Initialize(string ipAddress, 
            int port, 
            EventHandler<ConnectionFailureEventArgs> onConnectionFailureAsync)
        {
            m_connectionManager = new ClientConnectionManager(m_loggingContext, ipAddress, port, m_invocationId);
            m_connectionManager.OnConnectionFailureAsync += onConnectionFailureAsync;
            m_client = new Orchestrator.OrchestratorClient(m_connectionManager.Channel);
            m_initialized = true;
        }

        public Task CloseAsync()
        {
            if (!m_initialized)
            {
                return Task.CompletedTask;
            }

            return m_connectionManager.CloseAsync();
        }

        public async Task<RpcCallResult<Unit>> AttachCompletedAsync(AttachCompletionInfo message)
        {
            Contract.Assert(m_initialized);

            var attachmentCompletion = await m_connectionManager.CallAsync(
                async (callOptions) => await m_client.AttachCompletedAsync(message, options: callOptions),
                "AttachCompleted",
                waitForConnection: true);

            if (attachmentCompletion.Succeeded)
            {
                m_connectionManager.OnAttachmentCompleted();
            }

            return attachmentCompletion;
        }

        public Task<RpcCallResult<Unit>> NotifyAsync(WorkerNotificationArgs message, IList<long> semiStableHashes, CancellationToken cancellationToken = default)
        {
            Contract.Assert(m_initialized);

            return m_connectionManager.CallAsync(
               async (callOptions) => await m_client.NotifyAsync(message, options: callOptions),
               DistributionHelpers.GetNotifyDescription(message, semiStableHashes),
               cancellationToken: cancellationToken);
        }
    }
}