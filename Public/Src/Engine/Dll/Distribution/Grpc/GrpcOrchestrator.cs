// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using BuildXL.Distribution.Grpc;
using Grpc.Core;
using BuildXL.Utilities.Core.Tasks;
using BuildXL.Utilities.Configuration;
using System;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Core;

namespace BuildXL.Engine.Distribution.Grpc
{
    /// <summary>
    /// Orchestrator service impl
    /// </summary>
    public sealed class GrpcOrchestrator : Orchestrator.OrchestratorBase
    {
        private readonly LoggingContext m_loggingContext;
        private readonly IOrchestratorService m_orchestratorService; 
        internal GrpcOrchestrator(LoggingContext loggingContext, IOrchestratorService service)
        {
            m_loggingContext = loggingContext;
            m_orchestratorService = service;
        }

        /// <inheritdoc/>
        public override Task<HelloResponse> Hello(HelloRequest helloRequest, ServerCallContext context)
        {
            var result = m_orchestratorService.Hello(helloRequest.Location, helloRequest.RequestedId);
            return Task.FromResult(new HelloResponse { Message = result });
        }

        /// <inheritdoc/>
        public override Task<RpcResponse> AttachCompleted(AttachCompletionInfo message, ServerCallContext context)
        {
            m_orchestratorService.AttachCompleted(message);
            return GrpcUtils.EmptyResponseTask;
        }

        /// <inheritdoc/>
        public override Task<RpcResponse> ReportPipResults(PipResultsInfo message, ServerCallContext context)
        {
            m_orchestratorService.ReceivedPipResults(message).Forget(ex => Tracing.Logger.Log.GrpcEventHandlerExceptionOccurred(m_loggingContext, ex.ToStringDemystified()));
            return GrpcUtils.EmptyResponseTask;
        }

        /// <inheritdoc/>
        public override Task<RpcResponse> ReportExecutionLog(ExecutionLogInfo message, ServerCallContext context)
        {
            m_orchestratorService.ReceivedExecutionLog(message).Forget(ex => Tracing.Logger.Log.GrpcEventHandlerExceptionOccurred(m_loggingContext, ex.ToStringDemystified()));
            return GrpcUtils.EmptyResponseTask;
        }

        /// <inheritdoc/>
        public override Task<RpcResponse> Heartbeat(RpcResponse message, ServerCallContext context)
        {
            return GrpcUtils.EmptyResponseTask;
        }

        /// <inheritdoc/>
#pragma warning disable 1998 // Disable the warning for "This async method lacks 'await'"
        public override async Task<RpcResponse> StreamExecutionLog(IAsyncStreamReader<ExecutionLogInfo> requestStream, ServerCallContext context)
        {
#if NETCOREAPP
            await foreach (var message in requestStream.ReadAllAsync())
            {
                m_orchestratorService.ReceivedExecutionLog(message).Forget();
            }
            
            return GrpcUtils.EmptyResponse;
#else
            throw new NotImplementedException();
#endif
        }
#pragma warning restore 1998

        /// <inheritdoc/>
#pragma warning disable 1998 // Disable the warning for "This async method lacks 'await'"
        public override async Task<RpcResponse> StreamPipResults(IAsyncStreamReader<PipResultsInfo> requestStream, ServerCallContext context)
        {
#if NETCOREAPP
            await foreach (var message in requestStream.ReadAllAsync())
            {
                m_orchestratorService.ReceivedPipResults(message).Forget();
            }

            return GrpcUtils.EmptyResponse;
#else
            throw new NotImplementedException();
#endif
        }
#pragma warning restore 1998
    }
}