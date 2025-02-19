// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
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
            RushLocation = resolverSettings.RushLocation;
            TrackDependenciesWithShrinkwrapDepsFile = resolverSettings.TrackDependenciesWithShrinkwrapDepsFile;
            GraphConstructionMode = resolverSettings.GraphConstructionMode;
            RushCommand = resolverSettings.RushCommand;
            AdditionalRushParameters = resolverSettings.AdditionalRushParameters;
        }

        /// <inheritdoc/>
        public DirectoryArtifact? RushLibBaseLocation { get; set; }

        /// <inheritdoc/>
        public FileArtifact? RushLocation { get; set; }

        /// <inheritdoc/>
        public bool? TrackDependenciesWithShrinkwrapDepsFile { get; set; }

        /// <inheritdoc/>
        public string GraphConstructionMode { get; set; }

        /// <inheritdoc/>
        public string RushCommand { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<DiscriminatingUnion<string, IAdditionalNameValueParameter>> AdditionalRushParameters { get; set; }
    }
}
