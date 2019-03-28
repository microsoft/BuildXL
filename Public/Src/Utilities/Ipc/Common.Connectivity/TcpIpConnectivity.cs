// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Ipc.Interfaces;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Ipc.Common.Connectivity
{
    /// <summary>
    /// Provides connectivity info for TCP/IP
    /// </summary>
    public sealed class TcpIpConnectivity : IConnectivityProvider<Socket>
    {
        private readonly Lazy<TcpListener> m_lazyTcpListener;

        /// <nodoc />
        public int Port { get; }

        /// <nodoc />
        public TcpIpConnectivity(int port)
        {
            Port = port;

            m_lazyTcpListener = Lazy.Create(() =>
            {
                var listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                return listener;
            });
        }

        /// <summary>
        /// Used in unit tests only to force this listener to start listening.
        /// </summary>
        internal void StartListening()
        {
            m_lazyTcpListener.Value.Start();
        }

        /// <inheritdoc />
        public Task<Socket> AcceptClientAsync(CancellationToken token)
        {
            // unfortunatelly AcceptSocketAsync does not support cancellation token
            return m_lazyTcpListener.Value.AcceptSocketAsync();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (m_lazyTcpListener.IsValueCreated)
            {
                m_lazyTcpListener.Value.Stop();
            }
        }

        /// <summary>Wraps the socket into a <see cref="NetworkStream"/>.</summary>
        public static Stream GetStreamForClient(Socket client)
        {
            return new NetworkStream(client);
        }

        /// <summary>Connects to server running on localhost:<see cref="Port"/>.</summary>
        public async Task<TcpClient> ConnectToServerAsync(int numTimesToRetry = 3, int delayMillis = 250)
        {
            try
            {
                var tcpClient = new TcpClient();
                await tcpClient.ConnectAsync(IPAddress.Loopback, Port);
                return tcpClient;
            }
            catch (SocketException)
            {
                if (numTimesToRetry > 0)
                {
                    await Task.Yield();
                    await Task.Delay(millisecondsDelay: delayMillis);
                    return await ConnectToServerAsync(numTimesToRetry - 1, delayMillis * 2);
                }

                throw;
            }
        }

        /// <summary>Remote end point of the socket.</summary>
        public static string Describe(Socket client)
        {
            return client.RemoteEndPoint.ToString();
        }

        /// <summary>Disconnects given socket without flagging it for reuse.</summary>
        public static void DisconnectClient(Socket client)
        {
            client.Disconnect(reuseSocket: false);
        }

        /// <summary>
        /// Converts <paramref name="connectionString"/> to int; expects it to be a positive integer.
        /// </summary>
        public static int ParsePortNumber(string connectionString)
        {
            int port;
            if (!int.TryParse(connectionString, out port) || port <= 0)
            {
                throw new IpcException(
                    IpcException.IpcExceptionKind.InvalidMoniker,
                    I($"Illegal connection string ('{connectionString}') for ${nameof(TcpIpConnectivity)}."));
            }

            return port;
        }

        #region Explicit Interface Implementation
        Stream IConnectivityProvider<Socket>.GetStreamForClient(Socket client) => GetStreamForClient(client);

        string IConnectivityProvider<Socket>.Describe(Socket client) => Describe(client);

        void IConnectivityProvider<Socket>.DisconnectClient(Socket client) => DisconnectClient(client);
        #endregion
    }
}
