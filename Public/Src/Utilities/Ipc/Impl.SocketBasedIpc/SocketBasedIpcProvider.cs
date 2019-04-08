// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net.Sockets;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.Common.Connectivity;
using BuildXL.Ipc.Common.Multiplexing;
using BuildXL.Ipc.Interfaces;

namespace BuildXL.Ipc.SocketBasedIpc
{
    /// <summary>
    /// An implementation of <see cref="IIpcProvider"/> based on TCP/IP sockets.
    /// </summary>
    internal sealed class SocketBasedIpcProvider : IIpcProvider
    {
        private readonly ConcurrentDictionary<string, string> m_moniker2connectionString = new ConcurrentDictionary<string, string>();

        /// <summary>
        /// Creates and returns a new moniker tied to an arbitrary free port.
        /// </summary>
        /// <remarks>
        /// Ensures that unique monikers are returned throughout one program execution.
        /// </remarks>
        IIpcMoniker IIpcProvider.CreateNewMoniker()
        {
            return new StringMoniker(Guid.NewGuid().ToString());
        }

        IIpcMoniker IIpcProvider.LoadOrCreateMoniker(string monikerId)
        {
            return new StringMoniker(monikerId);
        }

        /// <summary>
        /// Returns an existing connection string for the given moniker ID or
        /// finds an unused port number and renders it to a string.
        /// </summary>
        string IIpcProvider.RenderConnectionString(IIpcMoniker moniker)
        {
            return m_moniker2connectionString.GetOrAdd(moniker.Id, (mId) => Utils.GetUnusedPortNumber().ToString(CultureInfo.InvariantCulture));
        }

        IClient IIpcProvider.GetClient(string connectionString, IClientConfig config)
        {
            return new Client(TcpIpConnectivity.ParsePortNumber(connectionString), config);
        }

        [SuppressMessage("Microsoft.Reliability", "CA2000:DisposeTcpIpConnectivity", Justification = "Disposed by NonMultiplexingServer")]
        IServer IIpcProvider.GetServer(string connectionString, IServerConfig config)
        {
            return new NonMultiplexingServer<Socket>(
                "TcpIpServer",
                config,
                new TcpIpConnectivity(TcpIpConnectivity.ParsePortNumber(connectionString)));
        }
    }
}
