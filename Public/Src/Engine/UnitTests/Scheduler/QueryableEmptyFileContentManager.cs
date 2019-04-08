// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
