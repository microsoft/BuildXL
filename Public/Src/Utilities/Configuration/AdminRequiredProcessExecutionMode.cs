// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Execution mode for processes that require admin privilege.
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
        /// This mode is introduced to check the functionality of process execution via sandboxed process executor tool, which "simulates"
        /// the process execution in VM. This mode is mainly used for testing purpose.
        /// </remarks>
        ExternalTool,

        /// <summary>
        /// The admin-required sandboxed process will be launched from a VM via a separate sandboxed process executor tool 
        /// </summary>
        ExternalVM,
    }
}
