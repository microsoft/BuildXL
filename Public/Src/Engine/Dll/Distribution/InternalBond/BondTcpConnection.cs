// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
#if !DISABLE_FEATURE_BOND_RPC

using System;
using System.Diagnostics.ContractsLight;
using Netlib;

namespace BuildXL.Engine.Distribution.InternalBond
{
    /// <summary>
    /// Wraps TCP-based bond connection, correspondent proxy and manages their proper disposal
    /// </summary>
    /// <remarks>
    /// These utilities were imported from BuildCache to handle dependency issues
    /// </remarks>
    public sealed class BondTcpConnection<TProxy> : IDisposable where TProxy : class
    {
        /// <summary>
        /// Connection to the Bond service
        /// </summary>
        private IOutgoingConnection m_connection;

        /// <summary>
        /// Wraps TCP-based bond connection, correspondent proxy and manages their proper disposal
        /// </summary>
        /// <param name="connection">Established bond connection</param>
        /// <param name="proxy">Initialized bond server proxy</param>
        public BondTcpConnection(IOutgoingConnection connection, TProxy proxy)
        {
            Contract.Requires(connection != null);
            Contract.Requires(proxy != null);

            m_connection = connection;
            Proxy = proxy;
        }

        /// <summary>
        /// Instance of server proxy initialized once the client is connected. The property is initialized only after a connection
        /// has been successfully established.
        /// </summary>
        public TProxy Proxy { get; private set; }

        /// <inheritdoc />
        public void Dispose()
        {
            if (m_connection != null)
            {
                m_connection.Dispose();
                m_connection = null;
            }

            Proxy = null;
        }
    }
}
#endif
