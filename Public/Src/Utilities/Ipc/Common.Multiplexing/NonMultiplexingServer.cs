// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Ipc.Common.Connectivity;
using BuildXL.Ipc.Interfaces;
using JetBrains.Annotations;

namespace BuildXL.Ipc.Common.Multiplexing
{
    /// <summary>
    /// Implements a server that processes only a single <see cref="IIpcOperation"/> from
    /// a client's connection stream before disposing the client and the stream.
    /// </summary>
    /// <typeparam name="TClient">
    /// Type of the connection.
    /// </typeparam>
    public sealed class NonMultiplexingServer<TClient> : IServer where TClient : IDisposable
    {
        private readonly IConnectivityProvider<TClient> m_connectivityProvider;
        private readonly GenericServer<TClient> m_clientListener;

        /// <summary>Arbitrary name only for descriptive purposes.</summary>
        public string Name { get; }

        /// <nodoc/>
        public NonMultiplexingServer([CanBeNull]string name, IServerConfig config, IConnectivityProvider<TClient> connectivityProvider)
        {
            Contract.Requires(config != null);
            Contract.Requires(connectivityProvider != null);

            Name = name ?? GetType().Name;
            m_connectivityProvider = connectivityProvider;

            m_clientListener = new GenericServer<TClient>(
                name: Name,
                config: config,
                listener: connectivityProvider.AcceptClientAsync);
        }

        /// <inheritdoc/>
        public void Start(IIpcOperationExecutor executor)
        {
            Contract.Requires(executor != null);

            m_clientListener.Start(async (client) =>
            {
                using (client)
                using (var bundle = m_connectivityProvider.GetStreamForClient(client))
                {
                    await Utils.ReceiveOperationAndExecuteLocallyAsync(bundle, executor, CancellationToken.None);
                }
            });
        }

        /// <nodoc/>
        public void Dispose()
        {
            m_clientListener.Dispose();
            m_connectivityProvider.Dispose();
        }

        #region IServer Implementation Through m_clientHandlingServer field

        /// <see cref="GenericServer{TClient}.Completion"/>
        public Task Completion => m_clientListener.Completion;

        /// <see cref="GenericServer{TClient}.Config"/>
        public IServerConfig Config => m_clientListener.Config;

        /// <see cref="GenericServer{TClient}.Complete"/>
        public void RequestStop() => m_clientListener.Complete();
        #endregion
    }
}
