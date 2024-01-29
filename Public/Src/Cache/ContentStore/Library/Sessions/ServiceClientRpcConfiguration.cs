// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Grpc;

#nullable enable

namespace BuildXL.Cache.ContentStore.Sessions
{
    /// <summary>
    /// Configuration for cache clients that communicate with a service instance via gRPC
    /// </summary>
    public sealed class ServiceClientRpcConfiguration
    {
        /// <summary>
        /// Host name where gRPC cache service resides
        /// 
        /// Defaults to localhost
        /// </summary>
        public MachineLocation Location { get; set; } = MachineLocation.UnencryptedLocalCacheService;

        /// <summary>
        /// The period of time between heartbeats.
        /// </summary>
        public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromMinutes(1);

        /// <summary>
        /// The soft timeout for heartbeats.
        ///
        /// When this amount of time passes, the operation is cooperatively cancelled with the server.
        /// </summary>
        public TimeSpan HeartbeatTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// The hard timeout for heartbeats.
        ///
        /// When this amount of time passes, the operation is abandoned.
        /// </summary>
        public TimeSpan HardHeartbeatTimeout { get; set; } = TimeSpan.FromSeconds(36);

        /// <summary>
        /// An optional deadline for all grpc operation operation (unless <see cref="PinDeadline"/> or <see cref="PlaceDeadline"/> are provided to control the deadline of the respective operations.
        /// </summary>
        public TimeSpan? Deadline { get; set; }

        /// <nodoc />
        public TimeSpan? PinDeadline { get; set; }

        /// <nodoc />
        public TimeSpan? PlaceDeadline { get; set; }

        /// <nodoc />
        public bool TraceGrpcCalls { get; set; }

        /// <summary>
        /// gRPC options that apply to gRPC Core, the C library that actually performs communication with gRPC.
        ///
        /// WARNING: Do NOT modify unless you know what you are doing.
        /// </summary>
        public GrpcCoreClientOptions? GrpcCoreClientOptions { get; set; }

        /// <summary>
        /// Options used by Grpc.Net client.
        /// </summary>
        public GrpcDotNetClientOptions? GrpcDotNetClientOptions { get; set; }

        /// <nodoc />
        public ServiceClientRpcConfiguration()
        {
        }

        /// <summary>
        /// Constructor usually used by clients that are integrating into CASaaS.
        ///
        /// WARNING: this is kept here for legacy purposes. Do NOT use.
        /// </summary>
        public ServiceClientRpcConfiguration(
            int grpcPort,
            TimeSpan? heartbeatInterval = null)
        {
            Contract.Requires(grpcPort > 0, $"Local server must have a positive GRPC port. Found {grpcPort}.");

            Location = Location.WithPort(grpcPort);

            if (heartbeatInterval != null)
            {
                HeartbeatInterval = heartbeatInterval.Value;
            }
        }
    }
}
