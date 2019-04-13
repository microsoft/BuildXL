// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.SandboxedProcessExecutor
{
    /// <summary>
    /// Exit status.
    /// </summary>
    internal enum ExitCode : int
    {
        /// <summary>
        /// Internal error.
        /// </summary>
        InternalError = -1,

        /// <summary>
        /// Success.
        /// </summary>
        Success = 0,

        /// <summary>
        /// Invalid argument.
        /// </summary>
        InvalidArgument = 1,

        /// <summary>
        /// Failed reading inputs.
        /// </summary>
        FailedReadInput = 2,

        /// <summary>
        /// Failed preparing sandboxed process.
        /// </summary>
        FailedSandboxPreparation = 3,

        /// <summary>
        /// Failed starting process.
        /// </summary>
        FailedStartProcess = 4,

        /// <summary>
        /// Failed writing output.
        /// </summary>
        FailedWriteOutput = 5,
    }
}
