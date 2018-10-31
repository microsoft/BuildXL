// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// High level categorization of what was performed
    /// </summary>
    public enum ExitKind
    {
        /// <summary>
        /// Invalid command line
        /// </summary>
        InvalidCommandLine,

        /// <summary>
        /// No build (Execution phase) was performed due to it not being requested
        /// </summary>
        BuildNotRequested,

        /// <summary>
        /// A build (Execution phase) was performed and it succeeded
        /// </summary>
        BuildSucceeded,

        /// <summary>
        /// A build was performed and failed with at least one error not related to a pip's result. There may also be
        /// pip failures, but general errors take precedence.
        /// </summary>
        BuildFailedWithGeneralErrors,

        /// <summary>
        /// A build was performed and failed. All errors were caused by pips returning errors
        /// </summary>
        BuildFailedWithPipErrors,

        /// <summary>
        /// Program execution (possibly a build) was aborted.
        /// </summary>
        /// <remarks>
        /// This can happen when the client of a build dies (Ctrl+C)?, causing the app server to exit.
        /// </remarks>
        Aborted,

        /// <summary>
        /// Client lost connection to server.
        /// </summary>
        /// <remarks>
        /// This can happen when the server running a build dies (crashes, or killed in task manager).
        /// </remarks>
        ConnectionToAppServerLost,

        /// <summary>
        /// Internal error
        /// </summary>
        InternalError,

        /// <summary>
        /// Error caused by the communication layer or other piece of infrastructure.
        /// </summary>
        InfrastructureError,

        /// <summary>
        /// Process aborted due to exhaustion of disk space on an unspecified volume.
        /// </summary>
        OutOfDiskSpace,

        /// <summary>
        /// The server process failed to start
        /// </summary>
        AppServerFailedToStart,

        /// <summary>
        /// Data error - Disk Failure
        /// </summary>
        DataErrorDriveFailure,

        /// <summary>
        /// A build was performed and failed. One or more errors were caused by file monitoring issues
        /// </summary>
        BuildFailedWithFileMonErrors,

        /// <summary>
        /// A build was performed and failed. One or more errors were caused by missing output files
        /// </summary>
        BuildFailedWithMissingOutputErrors,

        /// <summary>
        /// The build failed because of an error evaluating build specifications
        /// </summary>
        BuildFailedSpecificationError,

        /// <summary>
        /// The build failed because an exception occurs when telemetry is shut down.
        /// </summary>
        BuildFailedTelemetryShutdownException,

        /// <summary>
        /// User requested a filtered build that didn't match any pips
        /// </summary>
        NoPipsMatchFilter,

        /// <summary>
        /// The build was cancelled
        /// </summary>
        BuildCancelled,

        /// <summary>
        /// A general user error
        /// </summary>
        UserError,
    }
}
