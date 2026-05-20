// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Distribution.Grpc;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BuildXL.Engine.Distribution.Grpc
{
    /// <summary>
    /// Orchestrator server 
    /// </summary>
    public sealed class GrpcOrchestratorServer : GrpcServer
    {
        private readonly GrpcOrchestrator m_grpcOrchestrator;

        /// <nodoc/>
        internal GrpcOrchestratorServer(LoggingContext loggingContext, IOrchestratorService orchestratorService, DistributedInvocationId invocationId) 
            : base(loggingContext, invocationId)
        {
            m_grpcOrchestrator = new GrpcOrchestrator(loggingContext, orchestratorService);
        }

        /// <inheritdoc/>
        public override void Start(int port)
        {
            if (EngineEnvironmentSettings.GrpcKestrelServerEnabled)
            {
                _ = StartKestrel(port, 
                    services => services.AddSingleton(m_grpcOrchestrator), 
                    endpoints => endpoints.MapGrpcService<GrpcOrchestrator>());
            }
            else
            {
                var serviceDefinition = Orchestrator.BindService(m_grpcOrchestrator);
                Start(port, serviceDefinition);
            }
        }
    }
}