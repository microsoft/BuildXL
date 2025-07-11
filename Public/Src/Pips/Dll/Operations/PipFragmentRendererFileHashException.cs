// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Utilities.Core;

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// Exception thrown when a <see cref="PipFragmentRenderer"/> cannot resolve a content hash for a specific file artifact.
    /// </summary>
    public sealed class PipFragmentRendererFileHashException : Exception
    {
        /// <summary>
        /// FileArtifact associated with the exception (i.e., the file for which the hash could not be computed).
        /// </summary>
        public FileArtifact FileArtifact { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="PipFragmentRendererFileHashException"/> class with a specified error message.
        /// </summary>
        public PipFragmentRendererFileHashException(string message, FileArtifact fileArtifact, Exception innerException)
            : base(message, innerException)
        {
            FileArtifact = fileArtifact;
        }
    }
}
