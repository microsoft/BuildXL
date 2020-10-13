// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Supersede mode for file change tracker.
    /// </summary>
    public enum FileChangeTrackerSupersedeMode : byte
    {
        /// <summary>
        /// Supersede on all paths including files and directories (or container paths).
        /// </summary>
        All = 0,

        /// <summary>
        /// Legacy mode where superseding is only applied to file paths.
        /// </summary>
        FileOnly = 1,

        /// <summary>
        /// In addition to superseding a file as in <see cref="FileOnly"/>, the file's parents are superseded with the file's USN.
        /// </summary>
        FileAndParents = 2,
    }
}
