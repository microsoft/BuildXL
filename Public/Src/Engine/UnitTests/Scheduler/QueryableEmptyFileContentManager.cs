// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using BuildXL.Scheduler;
using BuildXL.Storage;
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

        /// <inheritdoc/>
        public Task<FileContentInfo?> TryQueryUndeclaredInputContentAsync(AbsolutePath path, string consumerDescription = null)
        {
            return Task.FromResult<FileContentInfo?>(null);
        }
    }
}
