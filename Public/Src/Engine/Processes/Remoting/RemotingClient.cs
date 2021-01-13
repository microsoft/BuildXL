// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities;
using Contract = System.Diagnostics.ContractsLight.Contract;

namespace BuildXL.Processes.Remoting
{
    /// <summary>
    /// Class wrapping TCP client for sending message to AnyBuild shim server.
    /// </summary>
    /// <remarks>
    /// CODESYNC:
    /// - (AnyBuild) src/Client/AnyBuild/SocketClientContext.cs
    /// - (AnyBuild) src/Client/AnyBuild/AsyncSocketServer.cs
    /// </remarks>
    internal class RemotingClient : IDisposable
    {
        private static readonly Encoding s_messageEncoding = OperatingSystemHelper.IsLinuxOS
            ? Encoding.UTF8
            : Encoding.Unicode;
        private static readonly int s_messageEncodingBytesPerChar = s_messageEncoding.GetByteCount(new[] { 'c' });
        private static readonly int s_defaultReceiveBufferSize = 52 * 1024 * s_messageEncodingBytesPerChar;

        private readonly TcpClient m_tcpClient;
        private readonly IPEndPoint m_endPoint;
        private readonly CancellationToken m_cancellationToken;
        private NetworkStream m_networkStream;
        private byte[] m_writeBuffer;
        private byte[] m_readBuffer;

        public bool IsConnected => m_tcpClient.Connected;

        /// <summary>
        /// Creates an instance of <see cref="RemotingClient"/>.
        /// </summary>
        public RemotingClient(IPEndPoint endPoint, CancellationToken cancellationToken)
        {
            m_endPoint = endPoint;
            m_tcpClient = new TcpClient { NoDelay = true };
            m_cancellationToken = cancellationToken;
            m_writeBuffer = new byte[s_defaultReceiveBufferSize];
            m_readBuffer = new byte[s_defaultReceiveBufferSize];
        }

        internal async Task ConnectAsync()
        {
            try
            {
                await m_tcpClient.ConnectAsync(m_endPoint.Address, m_endPoint.Port);
                Contract.Assert(m_tcpClient.Connected);
                m_networkStream = m_tcpClient.GetStream();
            }
            catch (Exception e)
            {
                throw new BuildXLException($"{nameof(RemotingClient)}: Failed to connect", e);
            }
        }

        internal async Task<string> ReceiveStringAsync()
        {
            Contract.Requires(IsConnected);

            int offset = 0;
            int payloadSize = -1;

            // Get the first 4 bytes containing the payload size.
            while (payloadSize < 0)
            {
                int bytesRead = await m_networkStream.ReadAsync(m_readBuffer, offset, 4 - offset, m_cancellationToken);
                if (bytesRead == 0)
                {
                    // Socket closed. Unexpected if we have not received data yet.
                    if (offset > 0)
                    {
                        throw new BuildXLException($"{nameof(RemotingClient)}: Unexpected close during message receive");
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
                int bytesRead = await m_networkStream.ReadAsync(m_readBuffer, offset, payloadSize - offset, m_cancellationToken);
                if (bytesRead == 0)
                {
                    // Socket closed.
                    throw new BuildXLException($"{nameof(RemotingClient)}: Unexpected close during message receive");
                }

                offset += bytesRead;
            }

            return s_messageEncoding.GetString(m_readBuffer, 0, payloadSize);
        }

        public Task WriteAsync(char messageTypePrefix, string message)
        {
            Contract.Requires(IsConnected);

            int writeBufferStart = 4;
            int totalSize = writeBufferStart + (1 + message.Length) * s_messageEncodingBytesPerChar;
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

            return m_networkStream.WriteAsync(m_writeBuffer, 0, totalSize, m_cancellationToken);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            try
            {
                m_tcpClient.Dispose();
            }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
            catch
            {
                // May have self-disposed on close.
            }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler

        }
    }
}
