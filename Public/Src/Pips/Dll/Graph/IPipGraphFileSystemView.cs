// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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