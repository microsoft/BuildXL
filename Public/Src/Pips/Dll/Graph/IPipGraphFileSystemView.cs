// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities;

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
    }
}