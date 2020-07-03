// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Extension class for <see cref="AdminRequiredProcessExecutionMode"/>.
    /// </summary>
    public static class AdminRequiredProcessExecutionModeExtensions
    {
        /// <summary>
        /// True if process should be executed externally.
        /// </summary>
        public static bool ExecuteExternal(this AdminRequiredProcessExecutionMode mode) 
            => mode == AdminRequiredProcessExecutionMode.ExternalTool || mode == AdminRequiredProcessExecutionMode.ExternalVM;

        /// <summary>
        /// True if process should be executed in VM.
        /// </summary>
        public static bool ExecuteExternalVm(this AdminRequiredProcessExecutionMode mode)
            => ExecuteExternal(mode) && mode == AdminRequiredProcessExecutionMode.ExternalVM;
    }
}
