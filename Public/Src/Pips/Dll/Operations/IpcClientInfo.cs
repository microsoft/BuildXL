// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Ipc.Common;
using BuildXL.Ipc.Interfaces;
using BuildXL.Utilities;

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// Information necessary for creating an IPC client (via <see cref="IIpcProvider.GetClient"/>).
    /// </summary>
    public sealed class IpcClientInfo
    {
        /// <summary>
        /// Id of the moniker to use to obtain a connection string for <see cref="IIpcProvider.GetClient"/>.
        /// </summary>
        public StringId IpcMonikerId { get; }

        /// <summary>
        /// Config to pass to <see cref="IIpcProvider.GetClient"/>.
        /// </summary>
        public IClientConfig IpcClientConfig { get; }

        /// <nodoc />
        public IpcClientInfo(StringId monikerId, IClientConfig config)
        {
            Contract.Requires(monikerId.IsValid);
            Contract.Requires(config != null);

            IpcMonikerId = monikerId;
            IpcClientConfig = config;
        }

        /// <nodoc />
        internal static IpcClientInfo Deserialize(PipReader reader)
        {
            var moniker = reader.ReadStringId();
            var config = ClientConfig.Deserialize(reader);
            return new IpcClientInfo(moniker, config);
        }

        /// <nodoc />
        internal void Serialize(PipWriter writer)
        {
            writer.Write(IpcMonikerId);
            ClientConfig.Serialize(IpcClientConfig, writer);
        }
    }
}
