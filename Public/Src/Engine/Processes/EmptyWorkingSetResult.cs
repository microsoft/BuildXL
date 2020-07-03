// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Processes
{
    /// <summary>
    /// Return status for EmptyWorkingSet operation
    /// </summary>
    [Flags]
    public enum EmptyWorkingSetResult : byte
    {
        /// <summary>
        /// Process is terminated before calling the operation or 
        /// the operation is not supported.
        /// </summary>
        None = 0,

        /// <summary>
        /// Success
        /// </summary>
        Success = 1,

        /// <summary>
        /// EmptyWorkingSet operation has failed
        /// </summary>
        EmptyWorkingSetFailed = 1 << 1,

        /// <summary>
        /// SetProcessWorkingSetSizeEx has failed
        /// </summary>
        SetMaxWorkingSetFailed = 1 << 2,

        /// <summary>
        /// SuspendThread operation has failed
        /// </summary>
        SuspendFailed = 1 << 3,
    }

}
