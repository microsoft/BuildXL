// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Specifies when unexpected file accesses for symlink paths are reported
    /// </summary>
    public enum UnexpectedSymlinkAccessReportingMode : byte
    {
        /// <summary>
        /// All outputs are required
        /// </summary>
        None,

        /// <summary>
        /// Report unexpected symlink accesses for executed processes only (i.e. not cached)
        /// </summary>
        ExecutionOnly,

        /// <summary>
        /// Report unexpected symlink accesses for executed and cached processes
        /// </summary>
        All,
    }
}
