// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Processes;
using BuildXL.Utilities.Collections;

namespace BuildXL.ProcessPipExecutor
{
    /// <summary>
    /// A temporary mutable structure to accumulate reported file accesses and observation flags for a given path
    /// </summary>
    /// <remarks>
    /// After all accesses have been processed, this structure can be converted into an immutable <see cref="ObservedFileAccess"/>
    /// </remarks>
    public sealed record ReportedFileAccessesAndFlagsMutable
    {
        /// <summary>
        /// All the accesses seen so far on a given path
        /// </summary>
        public CompactSet<ReportedFileAccess> ReportedFileAccesses { get; set; }

        /// <summary>
        /// Computed observation flags for the path based on all accesses seen so far
        /// </summary>
        /// <remarks>
        /// <see cref="ObservationFlags.FileProbe"/> is set only if all accesses on the path are probes. So we start with that flag set and remove it as needed.
        /// <see cref="ObservationFlags.Enumeration"/> is set as soon as any access suggests it, and once set it will always be true.
        /// <see cref="ObservationFlags.DirectoryLocation"/> is set as soon as any access suggests it, and once set it will always be true unless <see cref="HasDirectoryReparsePointTreatedAsFile"/> is true. So
        /// the final value of that flag may need to be adjusted after all accesses have been processed.
        /// </remarks>
        public ObservationFlags ObservationFlags { get; set; } = ObservationFlags.None;

        /// <summary>
        /// We carry this value since if determined true, it blocks treating the final observed access as a directory location, even if later accesses
        /// would suggest that.
        /// </summary>
        public bool HasDirectoryReparsePointTreatedAsFile { get; set; } = false;

        /// <summary>
        /// Whether the path was already determined to be a shared opaque output
        /// </summary>
        /// <remarks>
        /// Just avoids re-computing the value multiple times.
        /// </remarks>
        public bool IsSharedOpaqueOutput { get; set; } = false;

        /// <summary>
        /// Whether the path was determined to be an absent access (path/file not found)
        /// </summary>
        /// <remarks>
        /// When all accesses are processed, the final value reflects whether all the accesses on the same path were absent accesses.
        /// </remarks>
        public bool IsAbsentAccess { get; set; } = true;
    }
}
