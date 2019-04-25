// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading.Tasks;
using JetBrains.Annotations;

namespace BuildXL.Ipc.Interfaces
{
    /// <summary>
    /// An abstraction for implementing an IPC client.  In this context, an IPC
    /// client is a process issuing requests/operations to another process via
    /// some inter-process communication (IPC) protocol.
    /// </summary>
    /// <remarks>
    /// This abstraction is intended to be used in a client/server architecture.
    /// A concrete instance of <see cref="IClient"/> always comes preconfigured
    /// with a remote server endpoint, to which it issues all IPC operations
    /// (requested via the <see cref="Send"/> method).
    ///
    /// Concrete instances of this class should always be obtained through an
    /// <see cref="IIpcProvider"/>.
    /// </remarks>
    public interface IClient : IStoppable
    {
        /// <summary>
        /// Configuration used to create this client (via <see cref="IIpcProvider.GetClient"/>.
        /// </summary>
        [System.Diagnostics.Contracts.Pure]
        [NotNull]
        IClientConfig Config { get; }

        /// <summary>
        /// Executes given <paramref name="operation"/> on the server side.  If the operation is
        /// synchronous (<see cref="IIpcOperation.ShouldWaitForServerAck"/>), waits until it receives an
        /// <see cref="IIpcResult"/> from the server, which it then returns; otherwise, immediatelly
        /// returns success.
        /// </summary>
        /// <remarks>
        /// This operation should never throw.
        ///
        /// In case of an error, the error kind should be indicated by <see cref="IIpcResult.ExitCode"/>,
        /// and the error message by <see cref="IIpcResult.Payload"/>.
        ///
        /// In case of a success, the <see cref="IIpcResult.ExitCode"/> of the result shoud be
        /// <see cref="IpcResultStatus.Success"/>, and any return value should be encoded in
        /// <see cref="IIpcResult.Payload"/>.
        /// </remarks>
        /// <returns>
        /// Returns a task that completes once a result is ready.
        ///
        /// If establishing a connection with the server fails, the <see cref="IIpcResult.ExitCode"/>
        /// of the result should be <see cref="IpcResultStatus.ConnectionError"/>.
        ///
        /// If transmitting the operation over to the server, or receiving a result from the server
        /// (when operation is synchronous) fails, the <see cref="IIpcResult.ExitCode"/> should be
        /// <see cref="IpcResultStatus.TransmissionError"/>.
        ///
        /// Otherwise, the result is what is received from the server.
        /// </returns>
        [NotNull]
        Task<IIpcResult> Send([NotNull]IIpcOperation operation);
    }
}
