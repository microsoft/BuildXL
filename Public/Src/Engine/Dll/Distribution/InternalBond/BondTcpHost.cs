// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#if !DISABLE_FEATURE_BOND_RPC

using System;
using System.Diagnostics.ContractsLight;
using System.Net;
using BondNetlibTransport;
using BondTransport;

namespace BuildXL.Engine.Distribution.InternalBond
{
    /// <summary>
    /// Simple Bond host for TCP-based, binary-serialized connections
    /// </summary>
    /// <remarks>
    /// These utilities were imported from BuildCache to handle dependency issues
    /// </remarks>
    public sealed class BondTcpHost : IDisposable
    {
        /// <summary>
        /// The TCP port this BondTcpHost is listening on
        /// </summary>
        public readonly ushort Port;

        /// <summary>
        /// Underlying Bond server
        /// </summary>
        private readonly BondNetlibServer m_server;

        /// <summary>
        /// Simple Bond host for TCP-based, binary-serialized connections
        /// </summary>
        /// <param name="port">Port to listen on. 0 to chose any open port.</param>
        /// <param name="listeningAddress">Which address to listen for connections on.</param>
        /// <param name="services">Services to run on this server.</param>
        public BondTcpHost(ushort port, IPAddress listeningAddress, params BondService[] services)
        {
            Contract.Requires(services != null);

            m_server = new BondNetlibServer(
                new BinaryProtocolFactory(),
                listeningAddress,
                port);

            foreach (BondService service in services)
            {
                m_server.Register(service);
            }

            m_server.Start();
            Port = (ushort)m_server.LocalEndPoint.Port;
        }

        /// <summary>
        /// Stop the server
        /// </summary>
        public void Dispose()
        {
            m_server.Stop();
        }
    }
}
#endif
