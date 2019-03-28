// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Scheduler
{
    /// <summary>
    /// How a directory should be enumerated
    /// </summary>
    public enum DirectoryEnumerationMode
    {
        /// <summary>
        /// Using the real filesystem
        /// </summary>
        RealFilesystem,

        /// <summary>
        /// Use the full pip graph based filesystem
        /// </summary>
        FullGraph,

        /// <summary>
        /// Using a minimal pip graph based filesystem (immediate declared dependencies)
        /// </summary>
        MinimalGraph,

        /// <summary>
        /// The directory gets evaluated to the default fingerprint. This essentially means the directory will not be considered
        /// </summary>
        /// <remarks>
        /// Enumerations to directories that are under un-hashable or non-readable non-writable mounts do not contribute to fingerprints
        /// </remarks>
        DefaultFingerprint,
    }
}
