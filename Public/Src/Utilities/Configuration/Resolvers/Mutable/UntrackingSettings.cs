// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Utilities.Configuration.Resolvers.Mutable
{
    /// <nodoc />
    public struct UntrackingSettings : IUntrackingSettings 
    {
        /// <inheritdoc />
        public IReadOnlyList<DirectoryArtifact> UntrackedDirectoryScopes { get; set; }
        
        /// <inheritdoc />

        public IReadOnlyList<FileArtifact> UntrackedFiles { get; set; }
       
        /// <inheritdoc />
        public IReadOnlyList<DirectoryArtifact> UntrackedDirectories { get; set; }
    }
}
