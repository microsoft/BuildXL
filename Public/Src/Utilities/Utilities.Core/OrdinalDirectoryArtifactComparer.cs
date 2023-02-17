// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Utilities.Core
{
    /// <summary>
    /// Provides a total order for directory artifacts based on their numeric values.
    /// This order is independent of the artifacts' path table and the actual paths represented.
    /// </summary>
    /// <remarks>
    /// This comparer does not depend on a path table and does not deal in path sorting. This means
    /// that the order will differ for different executions that build a path table and artifacts
    /// (much like a string's hash code may differ per app domain). Consequently the order remains
    /// only valid within one execution (or multiple that share a persisted path table).
    /// </remarks>
    public sealed class OrdinalDirectoryArtifactComparer : IComparer<DirectoryArtifact>
    {
        /// <summary>
        /// Singleton (since this comparer is path table independent).
        /// </summary>
        public static readonly OrdinalDirectoryArtifactComparer Instance = new OrdinalDirectoryArtifactComparer();

        private OrdinalDirectoryArtifactComparer() { }

        /// <inheritdoc />
        public int Compare(DirectoryArtifact x, DirectoryArtifact y)
        {
            unchecked
            {
                return (int) x.IsSharedOpaquePlusPartialSealId - (int) y.IsSharedOpaquePlusPartialSealId;
            }
        }
    }
}
