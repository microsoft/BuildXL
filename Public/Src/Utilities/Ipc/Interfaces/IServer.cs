// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using JetBrains.Annotations;

namespace BuildXL.Ipc.Interfaces
{
    /// <summary>
    /// An abstraction for implementing an IPC server.  In this context, an IPC client
    /// is a process providing operations that other processes may call.
    /// </summary>
    public interface IServer : IStoppable
    {
        /// <summary>
        /// The configuration used to create this server (via <see cref="IIpcProvider.GetServer"/>).
        /// </summary>
        [System.Diagnostics.Contracts.Pure]
        IServerConfig Config { get; }

        /// <summary>
        /// Starts serving requests from clients.
        ///
        /// <strong>Must be non-blocking.</strong>  Should return as soon as the server has initialized.
        /// Use <see cref="IStoppable.Completion"/> to wait for the server to finish.
        /// </summary>
        /// <remarks>
        /// Whenever an <see cref="IIpcOperation"/> is received, <paramref name="executor"/>
        /// is used to execute it; if the operation is marked as synchronous (<see cref="IIpcOperation.ShouldWaitForServerAck"/>)
        /// the result returned by the <paramref name="executor"/> is returned to the client.
        ///
        /// Concrete implementations need not sequentialize processing of requests, hence,
        /// <paramref name="executor"/> is expected to be thread-safe, as it may be invoked
        /// concurrently from a concrete <see cref="IServer"/>.
        ///
        /// If the <paramref name="executor"/> throws an exception, the <see cref="IIpcResult.ExitCode"/>
        /// of the returned <see cref="IIpcResult"/> should be set to <see cref="IpcResultStatus.ExecutionError"/>.
        ///
        /// This method must not be called more than once.
        /// </remarks>
        void Start([JetBrains.Annotations.NotNull]IIpcOperationExecutor executor);
    }
}
