// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Processes
{
    /// <summary>
    /// The method that was used for determining the file access status
    /// </summary>
    public enum FileAccessStatusMethod : byte
    {
        /// <summary>
        /// File access was determined based on manifest information
        /// </summary>
        PolicyBased = 0,

        /// <summary>
        /// File access was determined by querying the file system
        /// </summary>
        /// <remarks>
        /// Only used when <see cref="FileAccessPolicy.OverrideAllowWriteForExistingFiles"/> is on
        /// </remarks>
        FileExistenceBased = 1,
    }
}
