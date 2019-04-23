// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.Utilities.Configuration.Resolvers;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <summary>
    /// Concrete implementation of the settings for CMake resolver
    /// </summary>
    public class CMakeResolverSettings : ResolverSettings, ICMakeResolverSettings
    {
        /// <nodoc />
        public CMakeResolverSettings()
        {
            // We allow the source directory to be writable by default
            AllowWritableSourceDirectory = true;
        }

        /// <nodoc />
        public CMakeResolverSettings(ICMakeResolverSettings template, PathRemapper pathRemapper) : base(template, pathRemapper)
        {
            ProjectRoot = pathRemapper.Remap(template.ProjectRoot);
            BuildDirectory = template.BuildDirectory;
            ModuleName = template.ModuleName;
            CacheEntries = template.CacheEntries;
            CMakeSearchLocations = template.CMakeSearchLocations;
            RemoveAllDebugFlags = template.RemoveAllDebugFlags;
            UntrackingSettings = template.UntrackingSettings;
        }

        /// <inheritdoc />
        public AbsolutePath ProjectRoot { get; set; }

        /// <inheritdoc />
        public RelativePath BuildDirectory { get; set; }

        /// <inheritdoc />
        public string ModuleName { get; set; }

        /// <inheritdoc />
        public IUntrackingSettings UntrackingSettings { get; set; }

        /// <inheritdoc />
        public IReadOnlyDictionary<string, string> CacheEntries { get; set; }

        /// <inheritdoc />
        public IReadOnlyList<DirectoryArtifact> CMakeSearchLocations { get; set; }

        /// <inheritdoc />
        public bool? RemoveAllDebugFlags { get; set; }
    }
}
