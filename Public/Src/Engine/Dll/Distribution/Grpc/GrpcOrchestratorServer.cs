// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Distribution.Grpc;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
#if NETCOREAPP3_1
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
#endif

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
            m_grpcOrchestrator = new GrpcOrchestrator(orchestratorService);
        }

        /// <inheritdoc/>
        public override void Start(int port)
        {
            if (EngineEnvironmentSettings.GrpcKestrelServerEnabled)
            {
#if NETCOREAPP3_1
                Action<object> configure = (endpoints) => ((IEndpointRouteBuilder)endpoints).MapGrpcService<GrpcOrchestrator>();
                _ = StartKestrel(port, configure);
#endif
            }
            else
            {
                var serviceDefinition = Orchestrator.BindService(m_grpcOrchestrator);
                Start(port, serviceDefinition);
            }
        }
    }
}