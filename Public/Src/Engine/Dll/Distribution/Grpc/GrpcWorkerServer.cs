// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Distribution.Grpc;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
#if NET_COREAPP_31
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
#endif

namespace BuildXL.Engine.Distribution.Grpc
{
    /// <summary>
    /// Worker server 
    /// </summary>
    public class GrpcWorkerServer : GrpcServer
    {
        private readonly GrpcWorker m_grpcWorker;

        /// <nodoc/>
        internal GrpcWorkerServer(LoggingContext loggingContext, IWorkerService workerService, DistributedInvocationId invocationId)
            : base(loggingContext, invocationId)
        {
            m_grpcWorker = new GrpcWorker(workerService);
        }

        /// <inheritdoc/>
        public override void Start(int port)
        {
            if (EngineEnvironmentSettings.GrpcKestrelServerEnabled)
            {
#if NET_COREAPP_31
                Action<object> configure = (endpoints) => ((IEndpointRouteBuilder)endpoints).MapGrpcService<GrpcWorker>();
                _ = StartKestrel(port, configure);
#endif
            }
            else
            {
                var serviceDefinition = Worker.BindService(m_grpcWorker);
                Start(port, serviceDefinition);
            }
        }
    }
}