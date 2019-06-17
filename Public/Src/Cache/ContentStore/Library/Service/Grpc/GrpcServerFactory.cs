// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Net;
using Grpc.Core;

namespace BuildXL.Cache.ContentStore.Service.Grpc
{
    /// <summary>
    /// A helper factory for constructing <see cref="Server"/> instances.
    /// </summary>
    public static class GrpcServerFactory
    {
        /// <nodoc />
        public static global::Grpc.Core.Server Create(ServerServiceDefinition service, int grpcPort, int? requestCallTokensPerCompletionQueue = null)
        {
            Contract.Requires(service != null);

            GrpcEnvironment.InitializeIfNeeded();
            return new Server(GrpcEnvironment.DefaultConfiguration)
            {
                Services = { service },
                Ports = { new ServerPort(IPAddress.Any.ToString(), grpcPort, ServerCredentials.Insecure) },
                // need a higher number here to avoid throttling: 7000 worked for initial experiments.
                RequestCallTokensPerCompletionQueue = requestCallTokensPerCompletionQueue ?? LocalServerConfiguration.DefaultRequestCallTokensPerCompletionQueue,
            };
        }

        /// <nodoc />
        public static global::Grpc.Core.Server Create(int grpcPort, int requestCallTokensPerCompletionQueue, params ServerServiceDefinition[] services)
        {
            Contract.Requires(services.Length != 0);

            GrpcEnvironment.InitializeIfNeeded();
            var server = new Server(GrpcEnvironment.DefaultConfiguration)
            {
                Ports = { new ServerPort(IPAddress.Any.ToString(), grpcPort, ServerCredentials.Insecure) },
                // need a higher number here to avoid throttling: 7000 worked for initial experiments.
                RequestCallTokensPerCompletionQueue = requestCallTokensPerCompletionQueue
            };

            foreach (var service in services)
            {
                server.Services.Add(service);
            }

            return server;
        }
    }
}
