// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Core;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Settings for Rush resolver
    /// </summary>
    public interface IRushResolverSettings : IJavaScriptResolverSettings
    {
        /// <summary>
        /// The base directory location to look for @microsoft/rush-lib module, used to build the project graph
        /// </summary>
        /// <remarks>
        /// If not provided, BuildXL will try to find it based on Rush installation location
        /// </remarks>
        DirectoryArtifact? RushLibBaseLocation { get; }

        /// <summary>
        /// Uses each project shrinkwrap-deps.json as a way to track changes in dependencies instead of actually tracking 
        /// all file dependencies under the Rush common temp folder.
        /// </summary>
        /// <remarks>
        /// Setting this option improves the chances of cache hits when compatible dependencies are placed on disk, which may not be the same ones
        /// as previous builds. It may also give some performance advantages since there are actually less files to hash and track for changes.
        /// However, it opens the door to underbuilds in the case any package.json is modified and BuildXL is executed without 
        /// running 'rush update/install' first, since shrinkwrap-deps.json files may be out of date.
        /// Defaults to false.
        /// </remarks>
        bool? TrackDependenciesWithShrinkwrapDepsFile { get; }
    }
}
