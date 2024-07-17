// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities;
using BuildXL.Utilities.Core;
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

        /// <summary>
        /// If exists, returns a path to a declared output directory containing <paramref name="path"/> and
        /// an indicator of whether that output directory is shared or exclusive.
        /// </summary>
        public Optional<(AbsolutePath path, bool isShared)> TryGetParentOutputDirectory(AbsolutePath path);

        /// <nodoc/>
        IReadOnlySet<FileArtifact> GetExistenceAssertionsUnderOpaqueDirectory(DirectoryArtifact directoryArtifact);
    }
}