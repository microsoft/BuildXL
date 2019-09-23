// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using BuildXL.FrontEnd.Sdk;
using BuildXL.Utilities;
using JetBrains.Annotations;

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

        private ModuleResolutionResult(ConcurrentDictionary<PackageId, Package> packages, Package configAsPackage, bool success)
        {
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
            return new ModuleResolutionResult(packages: packages, configAsPackage: configAsPackage, success: true);
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
            return HashCodeHelper.Combine(Packages.GetHashCode(), ConfigAsPackage?.GetHashCode() ?? 0);
        }

        /// <inheritdoc/>
        public bool Equals(ModuleResolutionResult other)
        {
            return Succeeded == other.Succeeded &&
                   ReferenceEquals(ConfigAsPackage, other.ConfigAsPackage) &&
                   ReferenceEquals(Packages, other.Packages);
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
