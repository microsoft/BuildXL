// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Settings for Nuget resolvers
    /// </summary>
    public partial interface INugetResolverSettings : IResolverSettings
    {
        /// <summary>
        /// Optional configuration to fix the version of nuget to use.
        /// When not specified the latest one will be used.
        /// </summary>
        INugetConfiguration Configuration { get; }

        /// <summary>
        /// The list of repositories to use to resolve. Keys are the name, values are the urls
        /// </summary>
        IReadOnlyDictionary<string, string> Repositories { get; }

        /// <summary>
        /// The packages to retrieve
        /// </summary>
        IReadOnlyList<INugetPackage> Packages { get; }

        /// <summary>
        /// Whether to enforce that the version range specified for dependencies in a NuGet package match the package version specified in the configuration file
        /// </summary>
        bool DoNotEnforceDependencyVersions { get; }
    }
}
