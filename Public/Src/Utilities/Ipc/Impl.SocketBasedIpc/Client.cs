// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.Interfaces;
using BuildXL.Utilities;
using BuildXL.Utilities.Tasks;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Ipc.SocketBasedIpc
{
    /// <summary>
    /// Implementation based on TCP/IP sockets.
    /// </summary>
    /// <remarks>
    /// Immutable.
    /// </remarks>
    internal sealed class Client : IClient
    {
        public IClientConfig Config { get; }

        public int Port { get; }

        /// <remarks>
        /// It is ok to provide <paramref name="config"/> whose <see cref="IClientConfig.Logger"/>
        /// is null; in that case, <see cref="VoidLogger"/> will be used.
        /// </remarks>
        internal Client(int port, IClientConfig config)
        {
            Contract.Requires(port > 0);
            Contract.Requires(config != null);

            Port = port;
            Config = config;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Connects to server over TCP/IP, then serializes given <paramref name="operation"/>
        /// (<see cref="IpcOperation.SerializeAsync(Stream, IIpcOperation, CancellationToken)"/>)
        /// and sends it over TCP/IP <see cref="Stream"/>.
        /// </remarks>
        Task<IIpcResult> IClient.Send(IIpcOperation operation)
        {
            Contract.Requires(operation != null);

            return ConnectAndExecute((stream) =>
            {
                return Utils.SendOperationAndExecuteRemotelyAsync(operation, stream, CancellationToken.None);
            });
        }

        /// <inheritdoc />
        void IStoppable.RequestStop() { }

        /// <inheritdoc />
        Task IStoppable.Completion => Unit.VoidTask;

        /// <inheritdoc />
        public void Dispose()
        {
            // nothing to dispose
        }

        /// <summary>
        /// First tries to establish a TCP/IP connection with the server; if that fails,
        /// the exit code of the result is <see cref="IpcResultStatus.ConnectionError"/>.
        ///
        /// Next, executes provided <paramref name="func"/>, handling all exceptions it may
        /// throw; if indeed <paramref name="func"/> throws, the exit code of the result is
        /// <see cref="IpcResultStatus.TransmissionError"/>.
        /// </summary>
        [SuppressMessage("Microsoft.Usage", "CA2202:stream disposed multiple times", Justification = "False positive")]
        private async Task<IIpcResult> ConnectAndExecute(Func<Stream, Task<IIpcResult>> func)
        {
            Possible<TcpClient> maybeConnected = await Utils.ConnectAsync(
                Config.MaxConnectRetries,
                Config.ConnectRetryDelay,
                ConnectAsync);

            if (!maybeConnected.Succeeded)
            {
                return new IpcResult(
                    IpcResultStatus.ConnectionError,
                    I($"Could not connect to port {Port} in {Config.MaxConnectRetries} attempts with {Config.ConnectRetryDelay} delay between attempts. {maybeConnected.Failure.Describe()}"));
            }

            TcpClient client = maybeConnected.Result;
            return await Utils.HandleExceptionsAsync(
                IpcResultStatus.TransmissionError,
                async () =>
                {
                    using (client)
                    using (var stream = client.GetStream())
                    {
                        return await func(stream);
                    }
                });
        }

        private async Task<TcpClient> ConnectAsync()
        {
            var tcpClient = new TcpClient();
            await tcpClient.ConnectAsync(IPAddress.Loopback.ToString(), Port);
            return tcpClient;
        }
    }
}
