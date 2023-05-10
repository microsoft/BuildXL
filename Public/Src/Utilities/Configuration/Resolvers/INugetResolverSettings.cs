// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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

        /// <summary>
        /// ESRP Sign Configuration
        /// </summary>
        IEsrpSignConfiguration EsrpSignConfiguration { get; }

        /// <summary>
        /// When true, includes in the analysis nuget package dependencies whose target frameworks are expressed using a 
        /// moniker (e.g. 'net6.0') in addition to the usual framework name (e.g. '.NETCoreAppv6.0')
        /// </summary>
        /// <remarks>
        /// Temporary flag to be able to deploy this change. Monikers should be always considered afterwards.
        /// </remarks>
        bool? IncludeMonikersInNuspecDependencies { get; }
    }
}
