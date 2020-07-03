// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
        /// Host operating systems on which to skip downloading this NuGet package.
        /// 
        /// A stub DScript module is always generated.
        /// 
        /// For valid values are: "win", "macOS", and "unix".
        /// </summary>
        List<string> OsSkip { get; }

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
