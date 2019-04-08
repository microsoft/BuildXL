// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BuildXL.Ipc.Common.Connectivity
{
    /// <summary>
    /// Interface for abstracting how client connections are accepted.
    /// </summary>
    public interface IConnectivityProvider<TClient> : IDisposable
    {
        /// <summary>Returns a task which completes when a client is connected.</summary>
        Task<TClient> AcceptClientAsync(CancellationToken token);

        /// <summary>Retrieves a read/write stream for the client.</summary>
        Stream GetStreamForClient(TClient client);

        /// <summary>Describes the client.</summary>
        string Describe(TClient client);

        /// <summary>Disconnects the client.</summary>
        void DisconnectClient(TClient client);
    }
}
