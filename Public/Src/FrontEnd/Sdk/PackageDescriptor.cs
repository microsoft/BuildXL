// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.FrontEnd.Sdk
{
    /// <summary>
    /// Package descriptor (or also called package configuration).
    /// </summary>
    public interface IPackageDescriptor
    {
        /// <summary>
        /// Package name.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Package display name.
        /// </summary>
        string DisplayName { get; }

        /// <summary>
        /// Package version.
        /// </summary>
        string Version { get; }

        /// <summary>
        /// Publisher of package.
        /// </summary>
        string Publisher { get; }

        /// <summary>
        /// Main entry path.
        /// </summary>
        AbsolutePath Main { get; }

        /// <summary>
        /// Projects that are owned by this package.
        /// </summary>
        /// <remarks>
        /// If this field is not specified, then all projects in the cone of this package are owned by the package.
        /// (The cone of a package covers the directory where the package resides, including all sub-directories underneath
        /// except those that contain other packages.) This field can be use to restrict the projects that are owned
        /// by the package. When this field is specified, and the user evaluate or build the package, and the evaluation requires a project
        /// not in this list, then the evaluation will result in an error.
        /// </remarks>
        IReadOnlyList<AbsolutePath> Projects { get; }

        /// <summary>
        /// The resolution semantics for this package
        /// </summary>
        NameResolutionSemantics? NameResolutionSemantics { get; set; }

        /// <summary>
        /// Modules that are allowed as dependencies of this module
        /// </summary>
        IReadOnlyList<string> AllowedDependencies { get; }

        /// <summary>
        /// Dependent modules that are allowed to be part of a module-to-module dependency cycle
        /// </summary>
        IReadOnlyList<string> CyclicalFriendModules { get; }
    }

    /// <summary>
    /// Set of extension methods for <see cref="IPackageDescriptor"/>.
    /// </summary>
    public static class PackageDescriptorExtensions
    {
        /// <nodoc />
        [Pure]
        public static NameResolutionSemantics NameResolutionSemantics(this IPackageDescriptor packageDescriptor)
        {
            return packageDescriptor.NameResolutionSemantics ?? BuildXL.Utilities.Configuration.NameResolutionSemantics.ExplicitProjectReferences;
        }
    }
}
