// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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

        /// <summary>
        /// Presents a relevant mix of the real filesystem and known immediate dependencies. This is a slightly
        /// stricter mode than <see cref="MinimalGraph"/>.
        /// In particular:
        /// * Immediate output dependencies are always present
        /// * Known outputs that are not part of the immediate dependencies are always absent
        /// * Files that are not recognized as (present or yet absent) outputs and are present in the file system. Observe this last
        ///   category includes undeclared source reads.
        /// </summary>
        MinimalGraphWithAlienFiles,
    }
}
