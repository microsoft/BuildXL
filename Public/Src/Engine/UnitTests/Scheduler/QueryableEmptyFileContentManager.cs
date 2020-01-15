// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Scheduler;
using BuildXL.Utilities;

namespace Test.BuildXL.Scheduler
{
    /// <summary>
    /// A queryable file content manager with no content
    /// </summary>
    public sealed class QueryableEmptyFileContentManager : IQueryableFileContentManager
    {
        /// <inheritdoc/>
        public bool TryGetContainingOutputDirectory(AbsolutePath path, out DirectoryArtifact containingOutputDirectory)
        {
            containingOutputDirectory = DirectoryArtifact.Invalid;
            return false;
        }
    }
}
