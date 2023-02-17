// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Utilities.Core;

namespace BuildXL.Utilities.Configuration.Resolvers
{
    /// <summary>
    /// Settings for resolvers which allow configurable untracking
    /// </summary>
    /// <remarks>
    /// Relative paths are interpreted relative to the corresponding project root
    /// </remarks>
    public interface IUntrackingSettings
    {
        /// <summary>
        /// Cones to flag as untracked
        /// </summary>
        IReadOnlyList<DiscriminatingUnion<DirectoryArtifact, RelativePath>> UntrackedDirectoryScopes { get; }

        /// <summary>
        /// Files to  flag as untracked
        /// </summary>
        IReadOnlyList<FileArtifact> UntrackedFiles { get; }

        /// <summary>
        /// Directories to flag as untracked
        /// </summary>
        IReadOnlyList<DiscriminatingUnion<DirectoryArtifact, RelativePath>> UntrackedDirectories { get; }

        /// <summary>
        /// Cones (directories and its recursive content) to flag as untracked for all projects in the build.
        /// The relative path is interepreted relative to each available project
        /// </summary>
        IReadOnlyList<RelativePath> UntrackedGlobalDirectoryScopes { get; }

        /// <summary>
        /// Process names that will break away from the sandbox when spawned by the main process
        /// </summary>
        /// <remarks>
        /// The accesses of processes that break away from the sandbox won't be observed.
        /// Processes that breakaway can survive the lifespan of the sandbox.
        /// Only add to this list processes that are trusted and whose accesses can be safely predicted by some other means.
        /// </remarks>
        public IReadOnlyList<PathAtom> ChildProcessesToBreakawayFromSandbox { get; }

        /// <summary>
        /// The process names, e.g. "mspdbsrv.exe", allowed to be cleaned up by a process pip sandbox job object after the main process has exited (which would otherwise throw a build error DX0041). 
        /// </summary>
        /// <remarks>
        /// Observe this doesn't mean the process is allowed to survive the sandbox, only that if it tries to survive bxl will terminate it without flagging the corresponding pip as failed.
        /// </remarks>
        public IReadOnlyList<PathAtom> AllowedSurvivingChildProcesses { get; }
    }
}