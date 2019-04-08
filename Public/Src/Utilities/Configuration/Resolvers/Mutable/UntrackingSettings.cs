using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
