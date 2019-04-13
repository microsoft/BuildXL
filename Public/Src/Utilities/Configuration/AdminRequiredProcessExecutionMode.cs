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
        /// The admin-required sandboxed process will be launched internally from BuildXL process.
        /// </summary>
        Internal,

        /// <summary>
        /// The admin-required sandboxed process will be launched from a separate sandboxed process executor tool.
        /// </summary>
        /// <remarks>
        /// This mode is mainly used for testing purpose.
        /// </remarks>
        ExternalTool,

        /// <summary>
        /// The admin-required sandboxed process will be launched from a VM via a separate sandboxed process executor tool 
        /// </summary>
        ExternalVM,
    }
}
