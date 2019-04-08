// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Ipc.Interfaces;

namespace BuildXL.Ipc.Common
{
    /// <summary>
    /// A straightforward implementation of <see cref="IServerConfig"/> using public properties.
    /// </summary>
    public sealed class ServerConfig : IServerConfig
    {
        /// <inheritdoc />
        public ILogger Logger { get; set; } = VoidLogger.Instance;

        /// <inheritdoc />
        public int MaxConcurrentClients { get; set; } = 10;

        /// <inheritdoc />
        public bool StopOnFirstFailure { get; set; } = false;
    }
}
