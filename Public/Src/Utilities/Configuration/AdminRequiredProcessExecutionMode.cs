// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// The verbosity level for logging
    /// </summary>
    public enum AdminRequiredProcessExecutionMode : byte
    {
        /// <summary>
        /// The admin-required sandboxed process will be launched from inside BuildXL process.
        /// </summary>
        InProc,

        /// <summary>
        /// The admin-required sandboxed process will be launched from a separate sandboxed process executor tool.
        /// </summary>
        /// <remarks>
        /// This mode is mainly used for testing purpose.
        /// </remarks>
        OutOfProc,

        /// <summary>
        /// The admin-required sandboxed process will be launched from a VM via a separate sandboxed process executor tool 
        /// </summary>
        VM,
    }
}
