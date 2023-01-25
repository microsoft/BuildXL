// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Distribution.Grpc;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
using Grpc.Core;
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
        private AsyncClientStreamingCall<PipBuildRequest, RpcResponse> m_pipBuildRequestStream;
        private readonly CounterCollection<DistributionCounter> m_counters;

        public GrpcWorkerClient(LoggingContext loggingContext, DistributedInvocationId invocationId, EventHandler<ConnectionFailureEventArgs> onConnectionFailureAsync, CounterCollection<DistributionCounter> counters)
        {
            m_loggingContext = loggingContext;
            m_onConnectionFailureAsync = onConnectionFailureAsync;
            m_invocationId = invocationId;
            m_counters = counters;
        }
        
        /// <summary>
        /// Receives a worker location and starts the connection management to that location
        /// </summary>
        public void SetWorkerLocation(ServiceLocation serviceLocation)
        {
            Contract.Assert(m_connectionManager == null, "The worker location can only be set once");
            Contract.Assert(serviceLocation != null);

            m_connectionManager = new ClientConnectionManager(m_loggingContext, serviceLocation.IpAddress, serviceLocation.Port, m_invocationId, m_counters);
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

        public Task<RpcCallResult<Unit>> ExecutePipsAsync(PipBuildRequest message, string description, CancellationToken cancellationToken = default)
        {
            Contract.Assert(m_connectionManager != null, "The worker location should be known if calling ExecutePips");

            Func<CallOptions, Task> func;

            if (EngineEnvironmentSettings.GrpcStreamingEnabled)
            {
                if (m_pipBuildRequestStream == null)
                {
                    var headerResult = GrpcUtils.InitializeHeaders(m_invocationId);
                    m_pipBuildRequestStream = m_client.StreamExecutePips(headers: headerResult.headers, cancellationToken: cancellationToken);
                }

                func = async (callOptions) => await m_pipBuildRequestStream.RequestStream.WriteAsync(message);
            }
            else
            {
                func = async (callOptions) => await m_client.ExecutePipsAsync(message, options: callOptions);
            }

            return m_connectionManager.CallAsync(func, description);
        }

        public Task<RpcCallResult<Unit>> ExitAsync(BuildEndData message, CancellationToken cancellationToken)
        {
            Contract.Assert(m_connectionManager != null, "The worker location should be known if calling Exit");

            m_connectionManager.ReadyForExit();

            return m_connectionManager.CallAsync(
                async (callOptions) => await m_client.ExitAsync(message, options: callOptions),
                "Exit",
                cancellationToken);
        }

        public void FinalizeStreaming()
        {
            if (m_pipBuildRequestStream != null)
            {
                m_pipBuildRequestStream.RequestStream.CompleteAsync().GetAwaiter().GetResult();
                m_pipBuildRequestStream.GetAwaiter().GetResult();
                m_pipBuildRequestStream.Dispose();
            }
        }
    }
}