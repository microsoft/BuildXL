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

        /// <summary>
        /// Whether to send a calling machine name via a message header.
        /// False by default.
        /// </summary>
        public readonly bool PropagateCallingMachineName;

        /// <summary>
        /// An optional deadline for all grpc operation operation (unless <see cref="PinDeadline"/> or <see cref="PlaceDeadline"/> are provided to control the deadline of the respective operations.
        /// </summary>
        public readonly TimeSpan? Deadline;

        /// <nodoc />
        public readonly TimeSpan? PinDeadline;

        /// <nodoc />
        public readonly TimeSpan? PlaceDeadline;

        /// <nodoc />
        public readonly bool TraceGrpcCalls;

        /// <nodoc />
        public ServiceClientRpcConfiguration(
            int grpcPort,
            TimeSpan? heartbeatInterval = null,
            TimeSpan? heartbeatTimeout = null,
            bool propagateCallingMachineName = false,
            TimeSpan? deadline = null,
            TimeSpan? pinDeadline = null,
            TimeSpan? placeDeadline = null,
            bool traceGrpcCalls = false)
        {
            GrpcPort = grpcPort;
            HeartbeatInterval = heartbeatInterval;
            HeartbeatTimeout = heartbeatTimeout;
            PropagateCallingMachineName = propagateCallingMachineName;
            Deadline = deadline;
            PinDeadline = pinDeadline;
            PlaceDeadline = placeDeadline;
            TraceGrpcCalls = traceGrpcCalls;
        }
    }
}
