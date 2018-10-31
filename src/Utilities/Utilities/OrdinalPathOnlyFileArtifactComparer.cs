// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Utilities
{
    /// <summary>
    /// Provides a total order of file artifacts based on their numeric values.
    /// This order is independent of the artifacts' path table and the actual paths represented.
    /// This comparer does not consider write counts (two artifacts for the same path are always equal).
    /// </summary>
    /// <remarks>
    /// This comparer does not depend on a path table and does not deal in path sorting. This means
    /// that the order will differ for different executions that build a path table and artifacts
    /// (much like a string's hash code may differ per app domain). Consequently the order remains
    /// only valid within one execution (or multiple that share a persisted path table).
    /// Note that this comparer is compatible with arrays sorted with <see cref="OrdinalFileArtifactComparer"/> (this relation is one-way).
    /// </remarks>
    public sealed class OrdinalPathOnlyFileArtifactComparer : ICompatibleComparer<FileArtifact, OrdinalFileArtifactComparer>
    {
        /// <summary>
        /// Singleton (since this comparer is path table independent).
        /// </summary>
        public static readonly OrdinalPathOnlyFileArtifactComparer Instance = new OrdinalPathOnlyFileArtifactComparer();

        private OrdinalPathOnlyFileArtifactComparer() { }

        /// <inheritdoc />
        public int Compare(FileArtifact x, FileArtifact y)
        {
            return x.Path.Value.Value - y.Path.Value.Value;
        }
    }
}
