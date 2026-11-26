// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Configuration.Mutable;

namespace BuildXL.Utilities.Configuration.Resolvers.Mutable
{
    /// <summary>
    /// Base settings for resolver settings allowing untracking.
    /// </summary>
    /// <remarks>
    /// This class is meant for code reuse through inheritance, is thus abstract
    /// </remarks>
    public abstract class UntrackingResolverSettings : ResolverSettings, IUntrackingSettings
    {
        /// <nodoc />
        public UntrackingResolverSettings()
        {
        }

        /// <nodoc />
        public UntrackingResolverSettings(IResolverSettings template, IUntrackingSettings untrackingTemplate, PathRemapper pathRemapper) : base(template, pathRemapper)
        {
            UntrackedDirectoryScopes = untrackingTemplate.UntrackedDirectoryScopes?.Select(pathRemapper.Remap).ToList();
            UntrackedFiles = untrackingTemplate.UntrackedFiles?.Select(pathRemapper.Remap).ToList();
            UntrackedDirectories = untrackingTemplate.UntrackedDirectories?.Select(pathRemapper.Remap).ToList();
            UntrackedGlobalDirectoryScopes = untrackingTemplate.UntrackedGlobalDirectoryScopes?.Select(pathRemapper.Remap).ToList();
            ChildProcessesToBreakawayFromSandbox = untrackingTemplate.ChildProcessesToBreakawayFromSandbox?.Select(pathRemapper.Remap).ToList();
            AllowedSurvivingChildProcesses = untrackingTemplate.AllowedSurvivingChildProcesses?.Select(pathRemapper.Remap).ToList();
        }

        /// <inheritdoc />
        public IReadOnlyList<DiscriminatingUnion<DirectoryArtifact, RelativePath>> UntrackedDirectoryScopes { get; set; }

        /// <inheritdoc />
        public IReadOnlyList<FileArtifact> UntrackedFiles { get; set; }

        /// <inheritdoc />
        public IReadOnlyList<DiscriminatingUnion<DirectoryArtifact, RelativePath>> UntrackedDirectories { get; set; }

        /// <inheritdoc />
        public IReadOnlyList<RelativePath> UntrackedGlobalDirectoryScopes { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<DiscriminatingUnion<PathAtom, IBreakawayChildProcess>> ChildProcessesToBreakawayFromSandbox { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<PathAtom> AllowedSurvivingChildProcesses { get; set; }
    }
}
