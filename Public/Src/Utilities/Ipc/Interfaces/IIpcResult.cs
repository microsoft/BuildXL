// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
        /// Indicates that an error occurred on the client while communicating with the server.
        /// </summary>
        TransmissionError = 3,

        /// <summary>
        /// Indicates that an error occurred on the server while executing the operation.
        /// </summary>
        ExecutionError = 4,

        /// <summary>
        /// Indicates that the user input is invalid.
        /// </summary>
        InvalidInput = 5,

        /// <summary>
        /// Indicates an error that occurred on the server and was associated with a BuildXL API Server.
        /// </summary>
        /// <remarks>
        /// This a generic bucket. Every bad state that somehow can be linked to API Server goes here (e.g.,
        /// a call could not be made, returned value is unexpected, file materialization failed, etc.).
        /// </remarks>
        ApiServerError = 6,

        // ----------------------------- external error codes ------------------------------
        
        // The idea behind these error codes is to allow service pips to signal BuildXL about
        // errors that are beyond their control. This should also enable us to better logging
        // and tracking of such errors.

        /// <summary>
        /// Indicates that an error occurred on the server while generating an SBOM.
        /// </summary>
        ManifestGenerationError = 7,

        /// <summary>
        /// Indicates that an error occurred while the server was executing signing-related operation.
        /// </summary>
        SigningError = 8,

        /// <summary>
        /// Indicates that an error occurred while the server was communicating with an external service.
        /// </summary>
        ExternalServiceError = 9,
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
        bool Succeeded { get; }

        /// <summary>
        /// Exit code.
        /// </summary>
        IpcResultStatus ExitCode { get; }

        /// <summary>
        /// Optional payload.
        /// </summary>
        string Payload { get; }

        /// <nodoc/>
        IpcResultTimestamp Timestamp { get; }

        /// <summary>
        /// (Optional) Duration of the action executed by a server.
        /// </summary>
        TimeSpan ActionDuration { get; set; }
    }
}
