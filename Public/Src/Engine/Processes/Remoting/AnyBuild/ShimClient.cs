// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities;

#nullable enable

namespace BuildXL.Processes.Remoting
{
    /// <summary>
    /// Low-level protocol interface for communicating to an AnyBuild shim server running on a loopback socket.
    /// </summary>
    /// <remarks>
    /// CODESYNC:
    /// - (AnyBuild) src/Client/ClientLibNetStd/Shim/ShimClient.css
    /// </remarks>
    public interface IShimClient : IDisposable
    {
        /// <summary>
        /// Indicates whether a connection exists to the AnyBuild shim server.
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Connects to the AnyBuild service process.
        /// </summary>
        Task ConnectAsync();

        /// <summary>
        /// Awaits a string message from the service process.
        /// </summary>
        Task<string> ReceiveStringAsync();

        /// <summary>
        /// Sends a string message to the service process, with message type prefix.
        /// </summary>
        Task WriteAsync(char messageTypePrefix, string message);
    }

    /// <summary>
    /// Low-level class wrapping TCP client for sending message to AnyBuild shim server.
    /// <see cref="RemoteCommand"/> is a higher-level class that implements remote execution
    /// and caching of individual processes/commands.
    /// </summary>
    /// <remarks>
    /// CODESYNC:
    /// - (AnyBuild) src/Client/ClientLibNetStd/Shim/ShimClient.css
    /// </remarks>
    internal sealed class ShimClient : IShimClient, IDisposable
    {
        private static readonly Encoding s_messageEncoding = OperatingSystemHelper.IsLinuxOS
            ? Encoding.UTF8
            : Encoding.Unicode;
        private static readonly int s_messageEncodingBytesPerChar = s_messageEncoding.GetByteCount(new[] { 'c' });
        internal const int DefaultReceiveBufferSizeChars = 52 * 1024;
        private static readonly int s_defaultReceiveBufferSize = DefaultReceiveBufferSizeChars * s_messageEncodingBytesPerChar;

        private readonly int m_port;
        private readonly CancellationToken m_cancellationToken;
        private NetworkStream? m_networkStream;
        private readonly TcpClient m_tcpClient = new() { NoDelay = true };
        private byte[] m_writeBuffer = new byte[s_defaultReceiveBufferSize];
        private byte[] m_readBuffer = new byte[s_defaultReceiveBufferSize];

        /// <summary>
        /// Creates an instance of <see cref="ShimClient"/>.
        /// </summary>
        /// <param name="port">The TCP port of the localhost AnyBuild service process.</param>
        /// <param name="cancellationToken">A cancellation token for this client's actions.</param>
        public ShimClient(int port, CancellationToken cancellationToken)
        {
            m_port = port;
            m_cancellationToken = cancellationToken;
        }

        /// <nodoc/>
        public bool IsConnected => m_tcpClient.Connected;

        /// <nodoc/>
        public async Task ConnectAsync()
        {
            await m_tcpClient.ConnectAsync(IPAddress.Loopback, m_port);
            if (!m_tcpClient.Connected)
            {
                throw new InvalidOperationException("BUGBUG: Client is not connected");
            }

            m_networkStream = m_tcpClient.GetStream();
        }

        /// <nodoc/>
        public async Task<string> ReceiveStringAsync()
        {
            CheckConnected();

            int offset = 0;
            int payloadSize = -1;

            // Get the first 4 bytes containing the payload size.
            while (payloadSize < 0)
            {
                int bytesRead = await m_networkStream!.ReadAsync(m_readBuffer, offset, 4 - offset, m_cancellationToken);
                if (bytesRead == 0)
                {
                    // Socket closed. Unexpected if we have not received data yet.
                    if (offset > 0)
                    {
                        throw new InvalidOperationException("Unexpected close during message receive");
                    }
                }

                offset += bytesRead;
                if (offset == 4)
                {
                    payloadSize = BitConverter.ToInt32(m_readBuffer, 0);
                }
            }

            if (payloadSize > m_readBuffer.Length)
            {
                m_readBuffer = new byte[payloadSize];
            }

            offset = 0;
            while (offset < payloadSize)
            {
                int bytesRead = await m_networkStream!.ReadAsync(m_readBuffer, offset, payloadSize - offset, m_cancellationToken);
                if (bytesRead == 0)
                {
                    // Socket closed.
                    throw new OperationCanceledException("Unexpected close during message receive");
                }

                offset += bytesRead;
            }

            return s_messageEncoding.GetString(m_readBuffer, 0, payloadSize);
        }

        /// <summary>
        /// Writes a prefix character and a string. Note that the socket server typically needs a terminating null character
        /// on the string, and that this is the caller's responsibility.
        /// </summary>
        public Task WriteAsync(char messageTypePrefix, string message)
        {
            CheckConnected();

            int writeBufferStart = 4;  // Length field
            int totalSize = writeBufferStart + (1 /*messageType*/ + message.Length) * s_messageEncodingBytesPerChar;
            if (m_writeBuffer.Length < totalSize)
            {
                m_writeBuffer = new byte[totalSize];
            }

            unsafe
            {
                fixed (byte* pBuf = &m_writeBuffer[0])
                {
                    *(int*)pBuf = totalSize - writeBufferStart;
                }
            }

            writeBufferStart += s_messageEncoding.GetBytes(new[] { messageTypePrefix }, 0, 1, m_writeBuffer, writeBufferStart);
            writeBufferStart += s_messageEncoding.GetBytes(message, 0, message.Length, m_writeBuffer, writeBufferStart);

            if (writeBufferStart != totalSize)
            {
                throw new InvalidOperationException($"BUGBUG: Packet length mismatch, estimated {totalSize}, from encoding {writeBufferStart}");
            }

            return m_networkStream!.WriteAsync(m_writeBuffer, 0, totalSize, m_cancellationToken);
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            try
            {
                m_tcpClient.Dispose();
            }
#pragma warning disable ERP022
            catch
            {
                // May have self-disposed on close.
            }
#pragma warning restore ERP022

            m_networkStream?.Dispose();
        }

        private void CheckConnected()
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException(nameof(IsConnected) + " is false");
            }
        }
    }
}
