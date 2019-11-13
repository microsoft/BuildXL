// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Ipc.Common;

namespace BuildXL.Ipc.Interfaces
{
    /// <summary>
    /// Exit status of an IPC operation.
    /// </summary>
    public enum IpcResultStatus : byte
    {
        /// <summary>
        /// Indicates successful execution.
        /// </summary>
        Success = 0,

        // ---------------------------------- error codes ----------------------------------

        /// <summary>
        /// Indicates a generic (unexplained) error.
        /// </summary>
        GenericError = 1,

        /// <summary>
        /// Indicates that the client could not establish a connection with the server.
        /// </summary>
        ConnectionError = 2,

        /// <summary>
        /// Indicates that an error occured on the client while communicating with the server.
        /// </summary>
        TransmissionError = 3,

        /// <summary>
        /// Indicates that an error occured on the server while executing the operation.
        /// </summary>
        ExecutionError = 4,

        /// <summary>
        /// Indicates that the user input is invalid.
        /// </summary>
        InvalidInput = 5
    }

    /// <summary>
    /// A low-level abstraction of the result of an <see cref="IIpcOperation"/>.
    ///
    /// Consists of an exit code <see cref="ExitCode"/> and a payload (<see cref="Payload"/>).
    /// </summary>
    public interface IIpcResult
    {
        /// <summary>
        /// Whether the call succeeded.
        /// </summary>
        [Pure]
        bool Succeeded { get; }

        /// <summary>
        /// Exit code.
        /// </summary>
        [Pure]
        IpcResultStatus ExitCode { get; }

        /// <summary>
        /// Optional payload.
        /// </summary>
        [Pure]
        string Payload { get; }

        /// <nodoc/>
        IpcResultTimestamp Timestamp { get; }

        /// <summary>
        /// (Optional) Duration of the action executed by a server.
        /// </summary>
        TimeSpan ActionDuration { get; set; }
    }
}
