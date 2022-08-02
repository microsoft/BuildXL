// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net.Sockets;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.Common.Connectivity;
using BuildXL.Ipc.Common.Multiplexing;
using BuildXL.Ipc.Interfaces;

namespace BuildXL.Ipc.MultiplexingSocketBasedIpc
{
    /// <summary>
    /// An implementation of <see cref="IIpcProvider"/> based on TCP/IP sockets.
    /// </summary>
    internal sealed class MultiplexingSocketBasedIpcProvider : IIpcProvider
    {
        /// <summary>
        /// 'GetOrAdd(key, valueFactory)' guarantees that all concurrent callers receive the same result per key, but it doesn't guarantee
        /// that the 'valueFactory' function is executed only once per key.  To ensure that our 'valueFactory' function
        /// (which calls GetUnusedPortNumber(), which can unnecessarily put a strain on system resources)
        /// is called at most once per key, we wrap the value in a Lazy object, because the 'Lazy.Value' getter
        /// does provide that guarantee when concurrent readers are present.
        /// </summary>
        private readonly ConcurrentDictionary<string, Lazy<string>> m_moniker2connectionString = new ConcurrentDictionary<string, Lazy<string>>();

        /// <summary>
        /// Returns an existing connection string for the given moniker ID or
        /// finds an unused port number and renders it to a string.
        /// </summary>
        string IIpcProvider.RenderConnectionString(IpcMoniker moniker)
        {
            return m_moniker2connectionString
                .GetOrAdd(
                    moniker.Id,
                    valueFactory: (mId) => new Lazy<string>(() => Utils.GetUnusedPortNumber().ToString(CultureInfo.InvariantCulture)))
                .Value;
        }

        IClient IIpcProvider.GetClient(string connectionString, IClientConfig config)
        {
            return new Client(config, TcpIpConnectivity.ParsePortNumber(connectionString));
        }

        [SuppressMessage("Microsoft.Reliability", "CA2000:DisposeTcpIpConnectivity", Justification = "Disposed by MultiplexingServer")]
        IServer IIpcProvider.GetServer(string connectionString, IServerConfig config)
        {
            var port = TcpIpConnectivity.ParsePortNumber(connectionString);
            return new MultiplexingServer<Socket>(
                "TcpIp-" + port,
                config.Logger,
                connectivityProvider: new TcpIpConnectivity(port),
                maxConcurrentClients: config.MaxConcurrentClients,
                maxConcurrentRequestsPerClient: config.MaxConcurrentRequestsPerClient);
        }
    }
}
