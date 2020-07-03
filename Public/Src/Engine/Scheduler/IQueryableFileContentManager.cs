// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Query operations on the file content manager
    /// </summary>
    public interface IQueryableFileContentManager
    {
        /// <summary>
        /// Whether there is a directory artifact representing an output directory (shared or exclusive opaque) that contains the given path
        /// </summary>
        bool TryGetContainingOutputDirectory(AbsolutePath path, out DirectoryArtifact containingOutputDirectory);
    }
}
