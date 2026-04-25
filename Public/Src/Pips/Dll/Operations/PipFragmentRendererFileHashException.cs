// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Utilities.Core;

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// Exception thrown when a <see cref="PipFragmentRenderer"/> cannot resolve a content hash for a specific file or directory artifact.
    /// </summary>
    public sealed class PipFragmentRendererHashException : Exception
    {
        /// <summary>
        /// FileOrDirectoryArtifact associated with the exception (i.e., the file or directory artifact for which the hash could not be computed).
        /// </summary>
        public FileOrDirectoryArtifact FileOrDirectoryArtifact { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="PipFragmentRendererHashException"/> class with a specified error message.
        /// </summary>
        public PipFragmentRendererHashException(string message, FileOrDirectoryArtifact fileOrDirectoryArtifact, Exception innerException)
            : base(message, innerException)
        {
            FileOrDirectoryArtifact = fileOrDirectoryArtifact;
        }
    }
}
