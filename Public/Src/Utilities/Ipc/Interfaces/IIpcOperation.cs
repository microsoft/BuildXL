// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Ipc.Common;

namespace BuildXL.Ipc.Interfaces
{
    /// <summary>
    /// A low-level abstraction for an IPC operation.
    ///
    /// Consists of a payload represented as a string (<see cref="Payload"/>), and a
    /// marker indicating whether this operation should be executed synchronously or
    /// asynchronously (<see cref="ShouldWaitForServerAck"/>).
    /// </summary>
    public interface IIpcOperation
    {
        /// <summary>
        /// Whether this is a synchronous operation.
        /// </summary>
        bool ShouldWaitForServerAck { get; }

        /// <summary>
        /// Payload of the operation, to be transmitted to the other end as is.
        /// </summary>
        string Payload { get; }

        /// <nodoc/>
        IpcOperationTimestamp Timestamp { get; }
    }
}
