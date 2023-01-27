// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
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
    internal sealed class GrpcOrchestratorClient : IOrchestratorClient
    {
        private readonly DistributedInvocationId m_invocationId;
        private Orchestrator.OrchestratorClient m_client;
        private ClientConnectionManager m_connectionManager;
        private readonly LoggingContext m_loggingContext;
        private volatile bool m_initialized;
        private AsyncClientStreamingCall<ExecutionLogInfo, RpcResponse> m_executionLogStream;
        private AsyncClientStreamingCall<PipResultsInfo, RpcResponse> m_pipResultsStream;
        private readonly CounterCollection<DistributionCounter> m_counters;

        public GrpcOrchestratorClient(LoggingContext loggingContext, DistributedInvocationId invocationId, CounterCollection<DistributionCounter> counters)
        {
            m_invocationId = invocationId;
            m_loggingContext = loggingContext;
            m_counters = counters;
        }

        public Task<RpcCallResult<Unit>> SayHelloAsync(ServiceLocation myLocation, CancellationToken cancellationToken = default)
        {
            return m_connectionManager.CallAsync(
               async (callOptions) => await m_client.HelloAsync(myLocation, options: callOptions),
               "Hello",
               cancellationToken: cancellationToken,
               waitForConnection: true);
        }

        public void Initialize(string ipAddress, 
            int port, 
            EventHandler<ConnectionFailureEventArgs> onConnectionFailureAsync)
        {
            m_connectionManager = new ClientConnectionManager(
                m_loggingContext, 
                ipAddress, 
                port, 
                m_invocationId, 
                m_counters, 
                async (callOptions) => await m_client.HeartbeatAsync(GrpcUtils.EmptyResponse, callOptions));
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

        public Task<RpcCallResult<Unit>> ReportPipResultsAsync(PipResultsInfo message, string description, CancellationToken cancellationToken = default)
        {
            Contract.Assert(m_initialized);

            Func<CallOptions, Task> func;

            if (EngineEnvironmentSettings.GrpcStreamingEnabled)
            {
                if (m_pipResultsStream == null)
                {
                    var headerResult = GrpcUtils.InitializeHeaders(m_invocationId);
                    m_pipResultsStream = m_client.StreamPipResults(headers: headerResult.headers, cancellationToken: cancellationToken);
                }

                func = async (callOptions) => await m_pipResultsStream.RequestStream.WriteAsync(message);
            }
            else
            {
                func = async (callOptions) => await m_client.ReportPipResultsAsync(message, options: callOptions);
            }

            return m_connectionManager.CallAsync(func, description, cancellationToken: cancellationToken);
        }

        public Task<RpcCallResult<Unit>> ReportExecutionLogAsync(ExecutionLogInfo message, CancellationToken cancellationToken = default)
        {
            Contract.Assert(m_initialized);

            Func<CallOptions, Task> func;

            if (EngineEnvironmentSettings.GrpcStreamingEnabled)
            {
                if (m_executionLogStream == null)
                {
                    var headerResult = GrpcUtils.InitializeHeaders(m_invocationId);
                    m_executionLogStream = m_client.StreamExecutionLog(headers: headerResult.headers, cancellationToken: cancellationToken);
                }

                func = async (callOptions) => await m_executionLogStream.RequestStream.WriteAsync(message);
            }
            else
            {
                func = async (callOptions) => await m_client.ReportExecutionLogAsync(message, options: callOptions);
            }

            return m_connectionManager.CallAsync(func, $" ReportExecutionLog: Size={message.Events.DataBlob.Count()}, SequenceNumber={message.Events.SequenceNumber}", cancellationToken: cancellationToken);
        }

        public bool TryFinalizeStreaming()
        {
            RpcCallResult<Unit> executionLogResponse = new RpcCallResult<Unit>();
            RpcCallResult<Unit> pipResultsResponse = new RpcCallResult<Unit>();
            if (m_executionLogStream != null)
            {
                executionLogResponse = m_connectionManager.FinalizeStreamAsync(m_executionLogStream).GetAwaiter().GetResult();
            }

            if (m_pipResultsStream != null)
            {
                pipResultsResponse = m_connectionManager.FinalizeStreamAsync(m_pipResultsStream).GetAwaiter().GetResult();
            }

            return executionLogResponse.Succeeded && pipResultsResponse.Succeeded;
        }
    }
}