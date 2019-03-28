// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.Common.Multiplexing;
using BuildXL.Ipc.Interfaces;
using BuildXL.Utilities;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Ipc.MultiplexingSocketBasedIpc
{
    /// <summary>
    /// Implementation based on TCP/IP socket with multiplexing of Ipc operations.
    /// </summary>
    /// <remarks>
    /// Immutable.
    /// </remarks>
    internal sealed class Client : IClient
    {
        private readonly Lazy<Task<Possible<MultiplexingClient>>> m_multiplexingClientLazyTask;

        /// <inheritdoc />
        public IClientConfig Config { get; }

        /// <nodoc />
        public int Port { get; }

        /// <inheritdoc />
        public Task Completion => HasMultiplexingClientBeenCreated()
            ? m_multiplexingClientLazyTask.Value.Result.Result.Completion
            : Unit.VoidTask;

        /// <inheritdoc />
        public void RequestStop()
        {
            if (HasMultiplexingClientBeenCreated())
            {
                m_multiplexingClientLazyTask.Value.Result.Result.RequestStop();
            }
        }

        /// <nodoc />
        internal Client(IClientConfig config, int port)
        {
            Contract.Requires(config != null);

            Config = config;
            Port = port;
            m_multiplexingClientLazyTask = Lazy.Create(CreateMultiplexingClientAsync);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (HasMultiplexingClientBeenCreated())
            {
                MultiplexingClient multiplexingClient = m_multiplexingClientLazyTask.Value.Result.Result;
                multiplexingClient.Dispose();
            }
        }

        /// <inheritdoc />
        public async Task<IIpcResult> Send(IIpcOperation operation)
        {
            Contract.Requires(operation != null);

            var maybeClient = await m_multiplexingClientLazyTask.Value;
            if (maybeClient.Succeeded)
            {
                return await maybeClient.Result.Send(operation);
            }
            else
            {
                return new IpcResult(IpcResultStatus.ConnectionError, "Failing this client because creating multiplexing client failed" + maybeClient.Failure.Describe());
            }
        }

        /// <remarks>
        /// It is essential that this returns a Task and the returned task is non-blocking (uses ConnectAsync instead of Connect);
        /// otherwise, when used from BuildXL for IPC pips, BuildXL scheduler gets super congested.
        /// </remarks>
        [SuppressMessage("Microsoft.Reliability", "CA2000:DisposeTcpClient", Justification = "TcpClient.Dispose is inaccessible due to its protection level")]
        private async Task<Possible<MultiplexingClient>> CreateMultiplexingClientAsync()
        {
            Config.Logger.Verbose("CreateMultiplexingClient called");
            var maybeConnected = await Utils.ConnectAsync(
                Config.MaxConnectRetries,
                Config.ConnectRetryDelay,
                async () =>
                {
                    var tcpClient = new TcpClient();
                    await tcpClient.ConnectAsync(IPAddress.Loopback.ToString(), Port);
                    return tcpClient.GetStream();
                });

            return maybeConnected.Then(stream => new MultiplexingClient(Config, stream));
        }

        private bool HasMultiplexingClientBeenCreated()
        {
            return
                m_multiplexingClientLazyTask.IsValueCreated &&
                m_multiplexingClientLazyTask.Value.GetAwaiter().GetResult().Succeeded;
        }
    }
}
