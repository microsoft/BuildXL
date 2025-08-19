// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Distribution.Grpc;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
#if NETCOREAPP
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
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
#if NETCOREAPP
                _ = StartKestrel(port, 
                    services => services.AddSingleton(m_grpcWorker), 
                    endpoints => endpoints.MapGrpcService<GrpcWorker>());
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