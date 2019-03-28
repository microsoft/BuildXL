// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities;

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
