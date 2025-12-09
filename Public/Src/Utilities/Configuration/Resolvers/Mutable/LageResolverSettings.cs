// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Core;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <summary>
    /// Resolver settings for the Lage front-end.
    /// </summary>
    public class LageResolverSettings : JavaScriptResolverSettings, ILageResolverSettings
    {
        /// <nodoc/>
        public LageResolverSettings()
        {
        }

        /// <nodoc/>
        public LageResolverSettings(ILageResolverSettings template, PathRemapper pathRemapper) 
        : base(template, pathRemapper)
        {
            NpmLocation = pathRemapper.Remap(template.NpmLocation);
            LageLocation = pathRemapper.Remap(template.LageLocation);
            Since = template.Since;
            UseYarnStrictAwarenessTracking = template.UseYarnStrictAwarenessTracking;
            DisallowWritesUnderYarnStrictStore = template.DisallowWritesUnderYarnStrictStore;
        }

        /// <inheritdoc/>
        public FileArtifact? NpmLocation { get; set; }

        /// <inheritdoc/>
        public FileArtifact? LageLocation { get; set; }

        /// <inheritdoc/>
        public string Since { get; set; }

        /// <inheritdoc/>
        public bool? UseYarnStrictAwarenessTracking { get; set; }

        /// <inheritdoc/>
        public bool? DisallowWritesUnderYarnStrictStore { get; set; }
    }
}
