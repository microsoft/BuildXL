// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities.Tracing;

namespace BuildXL.Scheduler.FileSystem
{
    /// <summary>
    /// Counters for operations/stats related to <see cref="FileSystemView"/>
    /// </summary>
    public enum FileSystemViewCounters
    {
        /// <summary>
        /// Counts number of paths which were inferred to be non-existent based on non-existent parent path
        /// </summary>
        InferredNonExistentPaths_NonExistentParent,

        /// <summary>
        /// Counts number of paths which were inferred to be non-existent because parent path is a file
        /// </summary>
        InferredNonExistentPaths_FileParent,

        /// <summary>
        /// Counts number of paths which were inferred to be non-existent because parent directory is enumerated and the
        /// path was not found
        /// </summary>
        InferredNonExistentPaths_EnumeratedDirectParent,

        /// <summary>
        /// Counts number of paths which were inferred to be non-existent because ancestor directory is enumerated and the
        /// path was not found
        /// </summary>
        InferredNonExistentPaths_EnumeratedAncestor,

        /// <summary>
        /// Number of existence checks to disk
        /// </summary>
        RealFileSystemDiskProbes,

        /// <summary>
        /// Number of file system enumerations to disk
        /// </summary>
        RealFileSystemEnumerations,

        /// <nodoc />
        [CounterType(CounterType.Stopwatch)]
        RealFileSystemEnumerationsDuration,

        /// <summary>
        /// Number of file system enumerations to disk trigger by file content manager
        /// </summary>
        RealFileSystemDiskProbes_FileContentManager,

        /// <summary>
        /// <see cref="RealFileSystemDiskProbes_FileContentManager"/> occurrences where cached state matches for existent path
        /// </summary>
        RealFileSystemDiskProbes_FileContentManager_UpToDateExists,

        /// <summary>
        /// <see cref="RealFileSystemDiskProbes_FileContentManager"/> occurrences where cached state matches for non-existent path
        /// </summary>
        RealFileSystemDiskProbes_FileContentManager_UpToDateNonExistence,

        /// <summary>
        /// <see cref="RealFileSystemDiskProbes_FileContentManager"/> occurrences where cached state shows exists but
        /// disk shows path does not exist
        /// </summary>
        RealFileSystemDiskProbes_FileContentManager_StaleExists,

        /// <summary>
        /// <see cref="RealFileSystemDiskProbes_FileContentManager"/> occurrences where cached state shows nonexistent but
        /// disk shows path does existence
        /// </summary>
        RealFileSystemDiskProbes_FileContentManager_StaleNonExistence,

        /// <summary>
        /// <see cref="RealFileSystemDiskProbes_FileContentManager"/> occurrences where cached state shows different existence
        /// file/directory compared to disk
        /// </summary>
        RealFileSystemDiskProbes_FileContentManager_StaleExistenceMismatch,

        /// <summary>
        /// <see cref="RealFileSystemDiskProbes_FileContentManager"/> occurrences where path was newly cached
        /// </summary>
        RealFileSystemDiskProbes_FileContentManager_Added,
    }
}
