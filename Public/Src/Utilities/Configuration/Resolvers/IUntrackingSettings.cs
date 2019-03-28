using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BuildXL.Utilities.Configuration.Resolvers
{
    /// <summary>
    /// Settings for resolvers which allow configurable untracking
    /// </summary>
    public interface IUntrackingSettings
    {
        /// <summary>
        /// Cones to flag as untracked
        /// </summary>
        IReadOnlyList<DirectoryArtifact> UntrackedDirectoryScopes { get; }

        /// <summary>
        /// Files to  flag as untracked
        /// </summary>
        IReadOnlyList<FileArtifact> UntrackedFiles { get; }

        /// <summary>
        /// Directories to flag as untracked
        /// </summary>
        IReadOnlyList<DirectoryArtifact> UntrackedDirectories { get; }
    }
}
