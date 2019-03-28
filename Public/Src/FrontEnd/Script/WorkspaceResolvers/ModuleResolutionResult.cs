// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using BuildXL.Utilities;
using JetBrains.Annotations;
using BuildXL.FrontEnd.Sdk;

namespace BuildXL.FrontEnd.Script
{
    /// <summary>
    /// Ferries package-related computations that are done by the source workspace resolver.
    /// </summary>
    /// <remarks>
    /// TODO: This is a temporary structure that should be removed when module configuration logic is removed from IResolver
    /// </remarks>
    public readonly struct ModuleResolutionResult : IEquatable<ModuleResolutionResult>
    {
        /// <summary>
        /// Mappings package directories to lists of packages.
        /// </summary>
        /// <remarks>
        /// We allow multiple packages in a single directory, and hence the list of packages. Moreover, by construction, the packages in the same list
        /// must reside in the same directory.
        /// </remarks>
        [NotNull]
        public ConcurrentDictionary<AbsolutePath, List<Package>> PackageDirectories { get; }

        /// <summary>
        /// Mappings from package id's to package locations and descriuptors.
        /// </summary>
        [NotNull]
        public ConcurrentDictionary<PackageId, Package> Packages { get; }

        /// <summary>
        /// Configuration as a package, which is only created for the default source resolver. Null otherwise.
        /// </summary>
        [CanBeNull]
        public Package ConfigAsPackage { get; }

        /// <nodoc/>
        public bool Succeeded { get; }

        private ModuleResolutionResult(ConcurrentDictionary<AbsolutePath, List<Package>> packageDirectories, ConcurrentDictionary<PackageId, Package> packages, Package configAsPackage, bool success)
        {
            PackageDirectories = packageDirectories;
            Packages = packages;
            ConfigAsPackage = configAsPackage;
            Succeeded = success;
        }

        /// <summary>
        /// Creates a failed ModuleResolutionResult
        /// </summary>
        public static ModuleResolutionResult CreateFailure()
        {
            return
                new ModuleResolutionResult(
                    packageDirectories: new ConcurrentDictionary<AbsolutePath, List<Package>>(),
                    packages: new ConcurrentDictionary<PackageId, Package>(), configAsPackage: null, success: false);
        }

        /// <summary>
        /// Creates a successful ModuleResolutionResult
        /// </summary>
        public static ModuleResolutionResult CreateModuleResolutionResult(
            [NotNull] ConcurrentDictionary<AbsolutePath, List<Package>> packageDirectories,
            [NotNull] ConcurrentDictionary<PackageId, Package> packages,
            [CanBeNull] Package configAsPackage)
        {
            return new ModuleResolutionResult(packageDirectories: packageDirectories, packages: packages, configAsPackage: configAsPackage, success: true);
        }

        /// <inheritdoc/>
        public override bool Equals(object other)
        {
            if (ReferenceEquals(other, null))
            {
                return false;
            }

            return other.GetType() == GetType() && Equals((ModuleResolutionResult)other);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(Packages.GetHashCode(), PackageDirectories.GetHashCode(), ConfigAsPackage?.GetHashCode() ?? 0);
        }

        /// <inheritdoc/>
        public bool Equals(ModuleResolutionResult other)
        {
            return Succeeded == other.Succeeded &&
                   ReferenceEquals(ConfigAsPackage, other.ConfigAsPackage) &&
                   ReferenceEquals(Packages, other.Packages) &&
                   ReferenceEquals(PackageDirectories, other.PackageDirectories);
        }

        /// <nodoc/>
        public static bool operator ==(ModuleResolutionResult left, ModuleResolutionResult right)
        {
            return left.Equals(right);
        }

        /// <nodoc/>
        public static bool operator !=(ModuleResolutionResult left, ModuleResolutionResult right)
        {
            return !left.Equals(right);
        }
    }
}
