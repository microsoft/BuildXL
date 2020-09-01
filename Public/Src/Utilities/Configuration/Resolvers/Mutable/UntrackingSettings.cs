// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Utilities.Configuration.Resolvers.Mutable
{
    /// <nodoc />
    public struct UntrackingSettings : IUntrackingSettings 
    {
        /// <inheritdoc />
        public IReadOnlyList<DiscriminatingUnion<DirectoryArtifact, RelativePath>> UntrackedDirectoryScopes { get; set; }

        /// <inheritdoc />

        public IReadOnlyList<FileArtifact> UntrackedFiles { get; set; }

        /// <inheritdoc />
        public IReadOnlyList<DiscriminatingUnion<DirectoryArtifact, RelativePath>> UntrackedDirectories { get; set; }

        /// <inheritdoc />
        public IReadOnlyList<RelativePath> UntrackedGlobalDirectoryScopes { get; set; }
    }
}
