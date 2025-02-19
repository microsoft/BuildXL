// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Utilities.Core;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Settings for Rush resolver
    /// </summary>
    public interface IRushResolverSettings : IJavaScriptResolverSettings
    {
        /// <summary>
        /// The base directory location to look for @microsoft/rush-lib module, used to build the project-to-project graph
        /// </summary>
        /// <remarks>
        /// If not provided, BuildXL will try to find it based on Rush installation location
        /// Cannot be specified together with <see cref="RushLocation"/>. This is enforced by the DScript type checker.
        /// </remarks>
        DirectoryArtifact? RushLibBaseLocation { get; }

        /// <summary>
        /// The location of Rush (with rush-build-graph-plugin), used to build the script-to-script graph.
        /// </summary>
        /// <remarks>
        /// If not provided, BuildXL will try to find it based on Rush installation location
        /// Cannot be specified together with <see cref="RushLibBaseLocation"/>. This is enforced by the DScript type checker.
        /// </remarks>
        FileArtifact? RushLocation { get; }

        /// <summary>
        /// Whether to use rush-lib or rush build graph plugin to build the graph.
        /// </summary>
        /// <remarks>
        /// If not specified (and it cannot be inferred from other fields), rush-lib is used.
        /// When not null, the value can be "rush-lib" or "rush-build-graph", enforced by the DScript type checker.
        /// </remarks>
        string GraphConstructionMode { get; }

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

        /// <summary>
        /// The command passed to the plugin. This includes custom commands that may be defined on a per-repo basis. 
        /// </summary>
        /// <remarks>
        /// See https://rushjs.io/pages/maintainer/custom_commands. 
        /// Defaults to 'build'.
        /// Only available when <see cref="GraphConstructionMode"/> is set to 'rush-build-graph'. Enforced by the type checker
        /// </remarks>
        string RushCommand { get; }

        /// <summary>
        /// Additional custom parameters to be passed to the plugin.
        /// </summary>
        /// <remarks>
        /// Check https://rushjs.io/pages/maintainer/custom_commands/.
        /// Additional parameters can be just flags, e.g. 'production', or name value pairs, e.g. {name: 'locale', value: 'en-us'}
        /// Only available when <see cref="GraphConstructionMode"/> is set to 'rush-build-graph'. Enforced by the type checker
        /// </remarks>
        IReadOnlyList<DiscriminatingUnion<string, IAdditionalNameValueParameter>> AdditionalRushParameters { get; }
    }
}
