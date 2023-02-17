// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Core;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Defines a named root
    /// </summary>
    public partial interface IMount : ITrackedValue
    {
        /// <summary>
        /// The name of the mount
        /// </summary>
        PathAtom Name { get; }

        /// <summary>
        /// The absolute path to the root
        /// </summary>
        AbsolutePath Path { get; }

        /// <summary>
        /// Indicates whether hashing is enabled under the root
        /// </summary>
        bool TrackSourceFileChanges { get; }

        /// <summary>
        /// Indicates whether writes are allowed under the root
        /// </summary>
        bool IsWritable { get; }

        /// <summary>
        /// Indicates whether reads are allowed under the root
        /// </summary>
        bool IsReadable { get; }

        /// <summary>
        /// Internal use only.
        /// Indicates whether the root represents a system location (such as Program Files)
        /// </summary>
        bool IsSystem { get; }

        /// <summary>
        /// Internal use only.
        /// Indicates whether the root represents a mount statically added at the beginning of the build (e.g. LogsDirectory)
        /// </summary>
        bool IsStatic { get; }

        /// <summary>
        /// Indicates whether a mount may be scrubbed (have files not registered in the current build graph as inputs or
        /// outputs deleted)
        /// </summary>
        bool IsScrubbable { get; }

        /// <summary>
        /// Indicates whether a directory is allowed to be created at the root of the mount
        /// </summary>
        bool AllowCreateDirectory { get; }
    }
}
