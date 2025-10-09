// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Core;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <summary>
    /// Resolver settings for the Nx front-end.
    /// </summary>
    public class NxResolverSettings : JavaScriptResolverSettings, INxResolverSettings
    {
        /// <nodoc/>
        public NxResolverSettings()
        {
        }

        /// <nodoc/>
        public NxResolverSettings(INxResolverSettings template, PathRemapper pathRemapper) 
        : base(template, pathRemapper)
        {
            NxLibLocation = pathRemapper.Remap(template.NxLibLocation);
        }

        /// <inheritdoc/>
        public DirectoryArtifact NxLibLocation { get; set; }

    }
}
