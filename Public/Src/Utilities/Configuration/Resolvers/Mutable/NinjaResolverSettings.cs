// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Utilities.Configuration.Resolvers;
using BuildXL.Utilities.Configuration.Resolvers.Mutable;
using BuildXL.Utilities.Core;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <summary>
    /// Concrete implementation of the settings for Ninja resolver
    /// </summary>
    public class NinjaResolverSettings : UntrackingResolverSettings, INinjaResolverSettings
    {
        /// <nodoc />
        public NinjaResolverSettings()
        {
            // We allow the source directory to be writable by default
            AllowWritableSourceDirectory = true;
        }

        /// <nodoc />
        public NinjaResolverSettings(INinjaResolverSettings template, PathRemapper pathRemapper) : base(template, template, pathRemapper)
        {
            Targets = template.Targets;
            Root = pathRemapper.Remap(template.Root);
            ModuleName = template.ModuleName;
            KeepProjectGraphFile = template.KeepProjectGraphFile;
            SpecFile = pathRemapper.Remap(template.SpecFile);
            Environment = template.Environment;
            AdditionalOutputDirectories = template.AdditionalOutputDirectories;
        }

        /// <inheritdoc />
        public IReadOnlyList<string> Targets { get; set; }

        /// <summary>
        /// Root of the project. Typically, where the build.ninja file is located
        /// </summary>
        public AbsolutePath Root { get; set; }

        /// <inheritdoc />
        public AbsolutePath SpecFile { get; set; }

        /// <inheritdoc />
        public string ModuleName { get; set; }

        /// <inheritdoc />
        public bool? KeepProjectGraphFile { get; set; }

        /// <inheritdoc />
        public IReadOnlyDictionary<string, EnvironmentData> Environment { get; set; }

        /// <inheritdoc />
        public IReadOnlyList<DiscriminatingUnion<AbsolutePath, RelativePath>> AdditionalOutputDirectories { get; set; }
    }
}
