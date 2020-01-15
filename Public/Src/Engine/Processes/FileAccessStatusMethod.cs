// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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

        /// <summary>
        /// File access was reported by a trusted tool
        /// </summary>
        /// <remarks>
        /// Trusted tools are allowed to report an augmented set of file accesses when they have processes
        /// that are allowed to breakaway from the sandbox, and therefore need to compensate for missing accesses.
        /// </remarks>
        TrustedTool = 2,
    }
}
