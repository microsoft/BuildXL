// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Distribution.Grpc;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;
using static BuildXL.Engine.Distribution.DistributionHelpers;
using static BuildXL.Engine.Distribution.Grpc.ClientConnectionManager;

namespace BuildXL.Engine.Distribution.Grpc
{
    /// <nodoc/>
    internal sealed class GrpcWorkerClient : IWorkerClient
    {
        private readonly LoggingContext m_loggingContext;
        private readonly ClientConnectionManager m_connectionManager;
        private readonly Worker.WorkerClient m_client;

        public GrpcWorkerClient(LoggingContext loggingContext, DistributedInvocationId invocationId, string ipAddress, int port, EventHandler<ConnectionFailureEventArgs> onConnectionFailureAsync)
        {
            m_loggingContext = loggingContext;
            m_connectionManager = new ClientConnectionManager(loggingContext, ipAddress, port, invocationId);
            m_connectionManager.OnConnectionFailureAsync += onConnectionFailureAsync;
            m_client = new Worker.WorkerClient(m_connectionManager.Channel);
        }

        public Task CloseAsync()
        {
            return m_connectionManager.CloseAsync();
        }

        public void Dispose()
        { }

        public async Task<RpcCallResult<Unit>> AttachAsync(BuildStartData message, CancellationToken cancellationToken)
        {
            var attachment = await m_connectionManager.CallAsync(
                async (callOptions) => await m_client.AttachAsync(message, options: callOptions),
                "Attach",
                cancellationToken,
                waitForConnection: true);

            if (attachment.Succeeded)
            {
                m_connectionManager.OnAttachmentCompleted();
            }

            return attachment;
        }

        public Task<RpcCallResult<Unit>> ExecutePipsAsync(PipBuildRequest message, IList<long> semiStableHashes)
        {
            return m_connectionManager.CallAsync(
               async (callOptions) => await m_client.ExecutePipsAsync(message, options: callOptions),
               GetExecuteDescription(semiStableHashes, message.Hashes.Count));
        }

        public Task<RpcCallResult<Unit>> ExitAsync(BuildEndData message, CancellationToken cancellationToken)
        {
            m_connectionManager.ReadyForExit();

            return m_connectionManager.CallAsync(
                async (callOptions) => await m_client.ExitAsync(message, options: callOptions),
                "Exit",
                cancellationToken);
        }
    }
}