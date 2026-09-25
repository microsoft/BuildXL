// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Ipc.Interfaces;
using BuildXL.Utilities.Core;

namespace BuildXL.Ipc.Common
{
    /// <summary>
    /// A straightforward implementation of <see cref="IServerConfig"/> using public properties.
    /// </summary>
    public sealed class ServerConfig : IServerConfig
    {
        /// <inheritdoc />
        public IIpcLogger Logger { get; set; } = VoidLogger.Instance;

        /// <inheritdoc />
        public int MaxConcurrentClients { get; set; } = 10;

        /// <inheritdoc />
        public int MaxConcurrentRequestsPerClient { get; set; } = 10;

        /// <inheritdoc />
        public bool StopOnFirstFailure { get; set; } = false;

        /// <summary>
        /// Whether to host the IPC service with gRPC.NET instead of gRPC Core.
        /// </summary>
        public bool UseGrpcDotNet { get; set; } = OperatingSystemHelper.IsMacOS;
    }
}
