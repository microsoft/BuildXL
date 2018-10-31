// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Provides a total order of file artifacts based on their numeric values.
    /// This order is independent of the artifacts' path table and the actual paths represented.
    /// </summary>
    /// <remarks>
    /// This comparer does not depend on a path table and does not deal in path sorting. This means
    /// that the order will differ for different executions that build a path table and artifacts
    /// (much like a string's hash code may differ per app domain). Consequently the order remains
    /// only valid within one execution (or multiple that share a persisted path table).
    /// </remarks>
    public sealed class OrdinalFileArtifactComparer : IComparer<FileArtifact>
    {
        /// <summary>
        /// Singleton (since this comparer is path table independent).
        /// </summary>
        public static readonly OrdinalFileArtifactComparer Instance = new OrdinalFileArtifactComparer();

        private OrdinalFileArtifactComparer() { }

        /// <inheritdoc />
        public int Compare(FileArtifact x, FileArtifact y)
        {
            int pathDiff = x.Path.Value.Value - y.Path.Value.Value;

            if (pathDiff != 0)
            {
                return pathDiff;
            }

            return x.RewriteCount - y.RewriteCount;
        }
    }
}
