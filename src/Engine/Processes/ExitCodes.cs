// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Processes
{
    /// <summary>
    /// Class listing all custom exit codes BuildXL uses when terminating processes after errors occur
    /// </summary>
    public static class ExitCodes
    {
        /// <summary>
        /// Exit code assigned by BuildXL when a process runs too long
        /// </summary>
        /// <remarks>
        /// basically a fixed random number
        /// </remarks>
        public const int Timeout = 27021977;

        /// <summary>
        /// Exit code assigned by BuildXL when a child process that survived the parent process is killed
        /// </summary>
        /// <remarks>
        /// basically a fixed random number
        /// </remarks>
        public const int KilledSurviving = 2721977;

        /// <summary>
        /// Exit code assigned by BuildXL when a process is killed after an internal error occurred
        /// </summary>
        /// <remarks>
        /// basically a fixed random number
        /// </remarks>
        public const int Killed = 2271977;

        /// <summary>
        /// Exit code assigned by BuildXL when there are failures processing detours messages
        /// </summary>
        /// <remarks>
        /// basically a fixed random number
        /// </remarks>
        public const int MessageProcessingFailure = 2271978;

        /// <summary>
        /// Value used by Execution Log to indicate that the process exit code has not been initailized.
        /// </summary>
        public const uint UninitializedProcessExitCode = 0xBAAAAAAD;
    }
}
