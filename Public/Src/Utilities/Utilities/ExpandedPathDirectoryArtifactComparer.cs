// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Class for comparing file artifacts by expanding the paths.
    /// </summary>
    public sealed class ExpandedPathDirectoryArtifactComparer : IComparer<DirectoryArtifact>
    {
        private readonly bool m_pathOnly;
        private readonly PathTable.ExpandedAbsolutePathComparer m_pathComparer;

        /// <summary>
        /// Creates an instance of <see cref="ExpandedPathFileArtifactComparer"/>
        /// </summary>
        public ExpandedPathDirectoryArtifactComparer(PathTable.ExpandedAbsolutePathComparer pathComparer, bool pathOnly)
        {
            m_pathComparer = pathComparer;
            m_pathOnly = pathOnly;
        }

        /// <inheritdoc />
        public int Compare(DirectoryArtifact x, DirectoryArtifact y)
        {
            var pathCompare = m_pathComparer.Compare(x.Path, y.Path);
            return pathCompare != 0 || m_pathOnly ? pathCompare : ((x.IsSharedOpaque ? 1 : 0) - (y.IsSharedOpaque ? 1 : 0));
        }
    }
}
