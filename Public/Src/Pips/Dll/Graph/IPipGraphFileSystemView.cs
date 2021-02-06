// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using System.Collections.Generic;

namespace BuildXL.Pips.Graph
{
    /// <summary>
    /// View of a filesystem based off of a PipGraph
    /// </summary>
    public interface IPipGraphFileSystemView
    {
        /// <nodoc/>
        FileArtifact TryGetLatestFileArtifactForPath(AbsolutePath path);

        /// <nodoc/>
        bool IsPathUnderOutputDirectory(AbsolutePath path, out bool isItSharedOpaque);

        /// <nodoc/>
        IReadOnlySet<FileArtifact> GetExistenceAssertionsUnderOpaqueDirectory(DirectoryArtifact directoryArtifact);
    }
}