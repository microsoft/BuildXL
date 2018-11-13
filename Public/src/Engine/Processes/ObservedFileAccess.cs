// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Processes
{
    /// <summary>
    /// Location type
    /// A bitmap of the possible location.
    /// </summary>
    [SuppressMessage("Microsoft.Naming", "CA1717:OnlyFlagsEnumsShouldHavePluralNames")]
    [System.Flags]
    public enum ObservationFlags : byte
    {
        /// <summary>
        /// Represents invalid value for the enum (must be specified to satisfy FxCop rules).
        /// </summary>
        None = 0,

        /// <summary>
        /// Represents a directory location.
        /// </summary>
        DirectoryLocation = 1,

        /// <summary>
        /// An enumeration operation was performed on this path.
        /// </summary>
        Enumeration = 2,

        /// <summary>
        /// A file probe was performed on this path.
        /// </summary>
        /// <remarks>
        /// FileProbe or DirectoryProbe will be decided based on 
        /// existence check and the flags.
        /// If the path exists as a file and the flag has FileProbe, 
        /// then it will be ExistingFileProbe.
        /// If the path exists as a directory and it does not have an
        /// enumeration flag, then it will be ExistingDirectoryProbe.
        /// </remarks>
        FileProbe = 4
    }

    /// <summary>
    /// Extensions for <see cref="ObservationFlags" />.
    /// </summary>
    public static class ObservationFlagsExtensions
    {
        /// <summary>
        /// Indicates if this status is specific to injecting detours / sandboxing; Detours and sandboxing-specific failures
        /// merit an 'internal error' indication rather than suggesting user fault.
        /// </summary>
        public static bool IsHashingRequired(this ObservationFlags flag)
        {
            // File probes, enumeration, and directory locations are not hashed.
            // (flag & ObservationFlags.DirectoryLocation) == 0 &&
            // (flag & ObservationFlags.Enumeration) == 0 &&
            // (flag & ObservationFlags.FileProbe) == 0

            // If we add a new flag, we should reconsider the logic below and
            // think about whether we need to hash the paths containing that new flag.
            return flag == ObservationFlags.None;
        }
    }

    /// <summary>
    /// File access detected under some observation scope (e.g. a sealed directory).
    /// </summary>
    /// <remarks>
    /// Versus a <see cref="ReportedFileAccess"/>:
    /// - The path is well-formed (available as an <see cref="AbsolutePath"/> via <see cref="Path"/>).
    /// - The access (or accesses) occurred under a scope of interest.
    /// - The access or accesses were not denied by the manifest (e.g. it was not a write, if the scope disallows writes).
    /// - Multiple low-level accesses for the same path may be represented.
    /// Note that this class is not structurally hashable / equatable; it is a struct but wraps a collection.
    /// </remarks>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1815:ShouldOverrideEquals")]
    public readonly struct ObservedFileAccess
    {
        // TODO: We put the CompactSet first since it contains a reference and so should be pointer-aligned. The subsequent fields need only 4 byte alignment.
        //       However, there appears to be a CLR bug in which this struct will take 24 bytes instead of the assumed 16 bytes.
        private readonly CompactSet<ReportedFileAccess> m_accesses;
        private readonly AbsolutePath m_path;
        private readonly ObservationFlags m_observationFlags;

        /// <summary>
        /// Creates an <see cref="ObservedFileAccess"/> representing zero or more individual accesses to <paramref name="path"/>.
        /// </summary>
        public ObservedFileAccess(
            AbsolutePath path,
            ObservationFlags observationFlags,
            CompactSet<ReportedFileAccess> accesses)
        {
            Contract.Requires(path.IsValid);

            m_path = path;
            m_observationFlags = observationFlags;
            m_accesses = accesses;
        }

        /// <summary>
        /// The path referred to by each access in <see cref="Accesses"/>
        /// </summary>
        // ReSharper disable once ConvertToAutoProperty
        public AbsolutePath Path => m_path;

        /// <summary>
        /// Gets the observation flags.
        /// </summary>
        public ObservationFlags ObservationFlags => m_observationFlags;

        /// <summary>
        /// Individual accesses to <see cref="Path"/>.
        /// </summary>
        // ReSharper disable once ConvertToAutoProperty
        public CompactSet<ReportedFileAccess> Accesses => m_accesses;
    }
}
