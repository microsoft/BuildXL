// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Native.IO;
using BuildXL.Utilities;

namespace BuildXL.Engine.Cache.Artifacts
{
    /// <summary>
    /// Provides existence queries for local file system
    /// </summary>
    public interface ILocalDiskFileSystemExistenceView
    {
        /// <summary>
        /// Probes for the existence of a file or directory at the given path.
        /// Changes to existence (e.g. creation of a file where one was previously not present) are tracked.
        /// </summary>
        Possible<PathExistence, Failure> TryProbeAndTrackPathForExistence(ExpandedAbsolutePath path, bool? isReadOnly = default);
    }
}
