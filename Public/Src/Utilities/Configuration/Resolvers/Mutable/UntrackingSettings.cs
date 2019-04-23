// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
