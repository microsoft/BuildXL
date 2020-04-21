// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Cache.ContentStore.Service.Grpc;

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
        /// The timeout for heartbeats.
        /// </summary>
        public readonly TimeSpan? HeartbeatTimeout;

        /// <summary>
        /// Gets the GRPC port.
        /// </summary>
        public int GrpcPort { get; }

        /// <summary>
        /// Gets the GRPC host name. 
        /// NOTE: Leaving unspecified equates to using localhost
        /// </summary>
        public string GrpcHost { get; set; } = GrpcEnvironment.Localhost;

        /// <nodoc />
        public ServiceClientRpcConfiguration(int grpcPort, TimeSpan? heartbeatInterval = null, TimeSpan? heartbeatTimeout = null)
        {
            GrpcPort = grpcPort;
            HeartbeatInterval = heartbeatInterval;
            HeartbeatTimeout = heartbeatTimeout;
        }
    }
}
