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
        public override Task<RpcResponse> AttachCompleted(AttachCompletionInfo message, ServerCallContext context)
        {
            var bondMessage = message.ToOpenBond();

            m_orchestratorService.AttachCompleted(bondMessage);

            return Task.FromResult(new RpcResponse());
        }

        /// <inheritdoc/>
        public override async Task<RpcResponse> Notify(WorkerNotificationArgs message, ServerCallContext context)
        {
            var bondMessage = message.ToOpenBond();

            var notifyTask = m_orchestratorService.ReceivedWorkerNotificationAsync(bondMessage);
            if (EngineEnvironmentSettings.InlineWorkerXLGHandling)
            {
                await notifyTask;
            }
            else
            {
                notifyTask.Forget();
            }

            return new RpcResponse();
        }
    }
}