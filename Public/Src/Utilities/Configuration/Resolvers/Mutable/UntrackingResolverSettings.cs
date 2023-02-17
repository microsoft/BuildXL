// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
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
            UntrackedDirectoryScopes = untrackingTemplate.UntrackedDirectoryScopes;
            UntrackedFiles = untrackingTemplate.UntrackedFiles;
            UntrackedDirectories = untrackingTemplate.UntrackedDirectories;
            UntrackedGlobalDirectoryScopes = untrackingTemplate.UntrackedGlobalDirectoryScopes;
            ChildProcessesToBreakawayFromSandbox = untrackingTemplate.ChildProcessesToBreakawayFromSandbox;
            AllowedSurvivingChildProcesses = untrackingTemplate.AllowedSurvivingChildProcesses;
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
        public IReadOnlyList<PathAtom> ChildProcessesToBreakawayFromSandbox { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<PathAtom> AllowedSurvivingChildProcesses { get; set; }
    }
}
