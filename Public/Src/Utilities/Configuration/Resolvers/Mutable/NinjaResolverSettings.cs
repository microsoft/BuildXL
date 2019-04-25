// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.Utilities.Configuration.Resolvers;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <summary>
    /// Concrete implementation of the settings for Ninja resolver
    /// </summary>
    public class NinjaResolverSettings : ResolverSettings, INinjaResolverSettings
    {
        /// <nodoc />
        public NinjaResolverSettings()
        {
            // We allow the source directory to be writable by default
            AllowWritableSourceDirectory = true;
        }

        /// <nodoc />
        public NinjaResolverSettings(INinjaResolverSettings template, PathRemapper pathRemapper) : base(template, pathRemapper)
        {

            Targets = template.Targets;
            ProjectRoot = pathRemapper.Remap(template.ProjectRoot);
            ModuleName = template.ModuleName;
            KeepToolFiles = template.KeepToolFiles;
            SpecFile = pathRemapper.Remap(template.SpecFile);
            RemoveAllDebugFlags = template.RemoveAllDebugFlags;
            UntrackingSettings = template.UntrackingSettings;
            AllowWritableSourceDirectory = template.AllowWritableSourceDirectory;
        }

        /// <inheritdoc />
        public IReadOnlyList<string> Targets { get; set; }

        /// <inheritdoc />
        public AbsolutePath ProjectRoot { get; set; }

        /// <inheritdoc />
        public AbsolutePath SpecFile { get; set; }

        /// <inheritdoc />
        public string ModuleName { get; set; }

        /// <inheritdoc />
        public IUntrackingSettings UntrackingSettings { get; set; }

        /// <inheritdoc />
        public bool? KeepToolFiles { get; set; }

        /// <inheritdoc />
        public bool? RemoveAllDebugFlags { get; set; }
    }
}
