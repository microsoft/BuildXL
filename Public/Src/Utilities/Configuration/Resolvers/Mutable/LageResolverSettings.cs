// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

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
        }

        /// <inheritdoc/>
        public FileArtifact? NpmLocation { get; set; }
    }
}
