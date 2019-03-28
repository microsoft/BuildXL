// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Cache.ContentStore.Sessions
{
    /// <summary>
    /// The RPC configuration for the service client.
    /// </summary>
    public sealed class ServiceClientRpcConfiguration
    {
        /// <summary>
        /// The period of time between heartbeats.
        /// </summary>
        public readonly TimeSpan? HeartbeatInterval;

        /// <summary>
        /// Gets the GRPC port.
        /// </summary>
        public int GrpcPort { get; }

        /// <summary>
        /// Creates a configuration based on grpc.
        /// </summary>
        public static ServiceClientRpcConfiguration CreateGrpc(int grpcPort, TimeSpan? heartbeatInterval = null)
        {
            return new ServiceClientRpcConfiguration(grpcPort, heartbeatInterval);
        }

        /// <summary>
        /// Creates a configuration based on grpc.
        /// The method left for backward compatibility.
        /// </summary>
        public static ServiceClientRpcConfiguration CreateGrpc(uint grpcPort, TimeSpan? heartbeatInterval = null)
        {
            return CreateGrpc((int)grpcPort, heartbeatInterval);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ServiceClientRpcConfiguration"/> class.
        /// </summary>
        public ServiceClientRpcConfiguration(int grpcPort, TimeSpan? heartbeatInterval = null)
        {
            GrpcPort = grpcPort;
            HeartbeatInterval = heartbeatInterval;
        }
    }
}
