// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#if NET6_0_OR_GREATER

using System;
using System.Collections.Concurrent;
using System.Globalization;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.Common.Connectivity;
using BuildXL.Ipc.Interfaces;
using Grpc.Core;

namespace BuildXL.Ipc.GrpcBasedIpc
{
    /// <summary>
    /// An implementation of <see cref="IIpcProvider"/> based on gRPC async unary calls
    /// </summary>
    internal sealed class GrpcIpcProvider : IIpcProvider
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

        IClient IIpcProvider.GetClient(string connectionString, IClientConfig config) => new Client(config, TcpIpConnectivity.ParsePortNumber(connectionString));

        IServer IIpcProvider.GetServer(string connectionString, IServerConfig config) => new GrpcIpcServer(TcpIpConnectivity.ParsePortNumber(connectionString), config);

        IServer IIpcProvider.GetServer(IServerConfig config) => new GrpcIpcServer(ServerPort.PickUnused, config);

        void IIpcProvider.UnsafeSetConnectionStringForMoniker(IpcMoniker ipcMoniker, string connectionString)
        {
            m_moniker2connectionString[ipcMoniker.Id] = new Lazy<string>(connectionString);
        }
    }
}

#endif
