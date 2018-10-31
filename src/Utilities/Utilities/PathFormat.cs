// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Utilities
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
        /// Used to target BuildXLScript path separators
        /// </summary>
        BuildXLScript = 1,

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
