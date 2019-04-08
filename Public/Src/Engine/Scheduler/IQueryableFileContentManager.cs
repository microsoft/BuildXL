// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
