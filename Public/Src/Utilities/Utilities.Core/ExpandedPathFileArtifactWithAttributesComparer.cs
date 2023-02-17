// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Utilities.Core
{
    /// <summary>
    /// Class for comparing file artifacts by expanding the paths.
    /// </summary>
    public sealed class ExpandedPathFileArtifactWithAttributesComparer : IComparer<FileArtifactWithAttributes>
    {
        private readonly bool m_pathOnly;
        private readonly PathTable.ExpandedAbsolutePathComparer m_pathComparer;

        /// <summary>
        /// Creates an instance of <see cref="ExpandedPathFileArtifactComparer"/>
        /// </summary>
        public ExpandedPathFileArtifactWithAttributesComparer(PathTable.ExpandedAbsolutePathComparer pathComparer, bool pathOnly)
        {
            m_pathComparer = pathComparer;
            m_pathOnly = pathOnly;
        }

        /// <inheritdoc />
        public int Compare(FileArtifactWithAttributes x, FileArtifactWithAttributes y)
        {
            return x.CompareTo(y, m_pathComparer, m_pathOnly);
        }
    }
}
