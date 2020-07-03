// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Ipc.Interfaces;

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
        public bool StopOnFirstFailure { get; set; } = false;
    }
}
