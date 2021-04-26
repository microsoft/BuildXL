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
        private readonly string m_buildId;
        private Orchestrator.OrchestratorClient m_client;
        private ClientConnectionManager m_connectionManager;
        private readonly LoggingContext m_loggingContext;
        private volatile bool m_initialized;

        public GrpcOrchestratorClient(LoggingContext loggingContext, string buildId)
        {
            m_buildId = buildId;
            m_loggingContext = loggingContext;
        }

        public void Initialize(string ipAddress, int port, EventHandler<ConnectionTimeoutEventArgs> onConnectionTimeOutAsync)
        {
            m_connectionManager = new ClientConnectionManager(m_loggingContext, ipAddress, port, m_buildId);
            m_connectionManager.OnConnectionTimeOutAsync += onConnectionTimeOutAsync;
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

        public Task<RpcCallResult<Unit>> AttachCompletedAsync(OpenBond.AttachCompletionInfo message)
        {
            Contract.Assert(m_initialized);

            var grpcMessage = message.ToGrpc();
            return m_connectionManager.CallAsync(
                async (callOptions) => await m_client.AttachCompletedAsync(grpcMessage, options: callOptions),
                "AttachCompleted",
                waitForConnection: true);
        }

        public Task<RpcCallResult<Unit>> NotifyAsync(OpenBond.WorkerNotificationArgs message, IList<long> semiStableHashes, CancellationToken cancellationToken = default)
        {
            Contract.Assert(m_initialized);

            var grpcMessage = message.ToGrpc();
            return m_connectionManager.CallAsync(
               async (callOptions) => await m_client.NotifyAsync(grpcMessage, options: callOptions),
               DistributionHelpers.GetNotifyDescription(message, semiStableHashes),
               cancellationToken: cancellationToken);
        }
    }
}