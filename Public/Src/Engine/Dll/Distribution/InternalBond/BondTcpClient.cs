// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if !DISABLE_FEATURE_BOND_RPC

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using BondNetlibTransport;
using BondTransport;
using BuildXL.Utilities.Tasks;
using Netlib;

namespace BuildXL.Engine.Distribution.InternalBond
{
    /// <summary>
    /// Bond client for TCP-based, binary-serialized connections
    /// </summary>
    /// <remarks>
    /// These utilities were imported from BuildCache to handle dependency issues
    /// </remarks>
    internal sealed class BondTcpClient<TProxy> where TProxy : class
    {
        /// <summary>
        /// Callback to create a bond proxy. Example: client => new FooService_Proxy(client)
        /// </summary>
        /// <param name="client">Connected bond client for the proxy</param>
        /// <returns>Initialized proxy instance</returns>
        public delegate TProxy CreateProxyCallback(IBondTransportClient client);

        private readonly BondHostLog m_logger;
        private readonly BondConnectOptions m_options;

        /// <summary>
        /// Bond client for TCP-based, binary-serialized connections
        /// </summary>
        /// <param name="logger">Logger for the client</param>
        /// <param name="options">Client's connection options</param>
        public BondTcpClient(BondHostLog logger, BondConnectOptions options = null)
        {
            Contract.Requires(logger != null);

            m_logger = logger;
            m_options = options ?? BondConnectOptions.Default;
        }

        /// <summary>
        /// Synchronously connect to a bond server
        /// </summary>
        /// <param name="server">The server to connect to</param>
        /// <param name="port">The port the server is listening on</param>
        /// <param name="createProxyCallback">The CreateProxyCallback</param>
        public BondTcpConnection<TProxy> Connect(string server, int port, CreateProxyCallback createProxyCallback)
        {
            Contract.Requires(server != null);
            Contract.Requires(server.Length > 0);
            Contract.Requires(port > 0);
            Contract.Requires(createProxyCallback != null);

            return ConnectAsync(server, port, createProxyCallback).Result;
        }

        /// <summary>
        /// Asynchronously connect to a bond server
        /// </summary>
        /// <param name="server">The server to connect to</param>
        /// <param name="port">The port the server is listening on</param>
        /// <param name="createProxyCallback">The CreateProxyCallback</param>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        public async Task<BondTcpConnection<TProxy>> ConnectAsync(
            string server,
            int port,
            CreateProxyCallback createProxyCallback)
        {
            Contract.Requires(server != null);
            Contract.Requires(server.Length > 0);
            Contract.Requires(port > 0);
            Contract.Requires(createProxyCallback != null);

            m_logger.Debug(
                "Connecting to {0} with timeout {1} ms",
                GetServerDescription(server, port),
                m_options.TimeoutInMs);

            NetlibConnectionConfig clientConfig = NetlibConnectionConfig.Default;
            clientConfig.Timeout = m_options.TimeoutInMs == 0 ? SocketConfiguration.InfiniteTimeout : TimeSpan.FromMilliseconds(m_options.TimeoutInMs);

            var connection = new NetlibConnection(clientConfig)
                             {
                                 AlwaysReconnect = m_options.ReconnectAutomatically,
                             };

            try
            {
                var completionSource = TaskSourceSlim.Create<SocketError>();
                connection.ConnectComplete += (sender, status) => completionSource.TrySetResult(status);
                connection.Disconnected += (sender, status) =>
                    m_logger.Debug("Disconnected from {0} ({1})", GetServerDescription(server, port), status);

                // NetlibConnection uses Dns.GetDnsEntry to resolve even if the server string contains an IP address.
                // CB uses IPs in the production environment and reverse lookup is not available there.
                // IP address can also belong to a machine behind a VIP where reverse lookup doesn't make sense.
                IPAddress ip;
                if (IPAddress.TryParse(server, out ip))
                {
                    connection.ConnectAsync(new IPEndPoint(ip, port));
                }
                else
                {
                    connection.ConnectAsync(server, port);
                }

                var result = await completionSource.Task;

                return OnConnectComplete(server, port, createProxyCallback, connection, result);
            }
            catch (Exception)
            {
                connection.Dispose();
                throw;
            }
        }

        private BondTcpConnection<TProxy> OnConnectComplete(
            string server,
            int port,
            CreateProxyCallback createProxyCallback,
            IOutgoingConnection connection,
            SocketError status)
        {
            if (status != SocketError.Success)
            {
                throw new IOException(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Failed to connect to {0} ({1})",
                        GetServerDescription(server, port),
                        status));
            }

            var bondClient = new BondNetlibClient(connection, new BinaryProtocolFactory());
            TProxy proxy = createProxyCallback(bondClient);
            m_logger.Debug("Connected to {0}", GetServerDescription(server, port));

            return new BondTcpConnection<TProxy>(connection, proxy);
        }

        private static string GetServerDescription(string server, int port)
        {
            return string.Format(CultureInfo.InvariantCulture, "{0} at {1}:{2}", typeof(TProxy).Name, server, port);
        }
    }
}
#endif
