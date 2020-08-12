// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using BuildXL.Storage;
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

        /// <summary>
        /// For a given path (which must be one of the pip's input artifacts; not an output), returns a content hash if that path is not under any sealed container 
        /// (source or full/partial seal directory), but undeclared source reads are allowed. In that
        /// case the path is also unversioned because immutability is also guaranteed by dynamic enforcements.
        /// This method always succeeds or fails synchronously.
        /// </summary>
        Task<FileContentInfo?> TryQueryUndeclaredInputContentAsync(AbsolutePath path, string consumerDescription = null);
    }
}
