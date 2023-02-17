// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Core
{
    /// <summary>
    /// The format for printing paths
    /// </summary>
    public enum PathFormat : byte
    {
        /// <summary>
        /// The path separator will be based on the current OS.
        /// This should be used for user facing output i.e., console output and logs
        /// </summary>
        HostOs = 0,

        /// <summary>
        /// Used to target DScript path separators
        /// </summary>
        Script = 1,

        /// <summary>
        /// Should you need to force the windows path layout
        /// </summary>
        Windows = 2,

        /// <summary>
        /// Should you need to force a Unix path layout
        /// </summary>
        Unix = 3,
    }
}
