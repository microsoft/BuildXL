// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using BuildXL.Distribution.Grpc;
using Grpc.Core;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Configuration;

namespace BuildXL.Engine.Distribution.Grpc
{
    /// <summary>
    /// Orchestrator service impl
    /// </summary>
    public sealed class GrpcOrchestrator : Orchestrator.OrchestratorBase
    {
        private readonly IOrchestratorService m_orchestratorService; 
        internal GrpcOrchestrator(IOrchestratorService service)
        {
            m_orchestratorService = service;
        }

        /// <inheritdoc/>
        public override Task<RpcResponse> Hello(ServiceLocation workerLocation, ServerCallContext context)
        {
            m_orchestratorService.Hello(workerLocation);
            return Task.FromResult(new RpcResponse());
        }

        /// <inheritdoc/>
        public override Task<RpcResponse> AttachCompleted(AttachCompletionInfo message, ServerCallContext context)
        {
            m_orchestratorService.AttachCompleted(message);
            return GrpcUtils.EmptyResponse;
        }

        /// <inheritdoc/>
        public override Task<RpcResponse> ReportPipResults(PipResultsInfo message, ServerCallContext context)
        {
            m_orchestratorService.ReceivedPipResults(message).Forget();
            return GrpcUtils.EmptyResponse;
        }

        /// <inheritdoc/>
        public override Task<RpcResponse> ReportExecutionLog(ExecutionLogInfo message, ServerCallContext context)
        {
            m_orchestratorService.ReceivedExecutionLog(message).Forget();
            return GrpcUtils.EmptyResponse;
        }
    }
}