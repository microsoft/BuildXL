// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using JetBrains.Annotations;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Nuget package definition
    /// </summary>
    public partial interface INugetPackage
    {
        /// <summary>
        /// The id of the package
        /// </summary>
        string Id { get; }

        /// <summary>
        /// Optional version.
        /// </summary>
        string Version { get; }

        /// <summary>
        /// Optional alias
        /// </summary>
        string Alias { get; }

        /// <summary>
        /// Optional target framework
        /// </summary>
        string Tfm { get; }

        /// <summary>
        /// Optional dependent Nuget packages to skip when resolving dependencies during package generation, a list of Nuget package Ids
        /// </summary>
        List<string> DependentPackageIdsToSkip { get; }

        /// <summary>
        /// Optional dependent Nuget packages to skip when resolving dependencies, a list of Nuget package Ids
        /// </summary>
        List<string> DependentPackageIdsToIgnore { get; }

        /// <summary>
        /// Optional flag to force package spec generation to use full framework qualifiers only
        /// </summary>
        bool ForceFullFrameworkQualifiersOnly { get; }
    }

    /// <nodoc/>
    public static class NugetPackageExtensionMethods
    {
        /// <summary>
        /// Returns the package alias if it is not null or empty, otherwise the package ID
        /// </summary>
        [NotNull]
        public static string GetPackageIdentity(this INugetPackage nugetPackage)
        {
            Contract.Requires(!string.IsNullOrEmpty(nugetPackage.Id), "Every package should have an ID");
            return !string.IsNullOrEmpty(nugetPackage.Alias) ? nugetPackage.Alias : nugetPackage.Id;
        }
    }
}
