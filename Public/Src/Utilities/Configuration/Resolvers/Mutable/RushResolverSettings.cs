// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Core;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <summary>
    /// Resolver settings for the Rush front-end.
    /// </summary>
    public class RushResolverSettings : JavaScriptResolverSettings, IRushResolverSettings
    {
        /// <nodoc/>
        public RushResolverSettings()
        {
        }

        /// <nodoc/>
        public RushResolverSettings(
            IRushResolverSettings resolverSettings,
            PathRemapper pathRemapper)
            : base(resolverSettings, pathRemapper)
        {
            RushLibBaseLocation = resolverSettings.RushLibBaseLocation;
            TrackDependenciesWithShrinkwrapDepsFile = resolverSettings.TrackDependenciesWithShrinkwrapDepsFile;
        }

        /// <inheritdoc/>
        public DirectoryArtifact? RushLibBaseLocation { get; set; }

        /// <inheritdoc/>
        public bool? TrackDependenciesWithShrinkwrapDepsFile { get; set; }
    }
}
