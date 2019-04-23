// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.Nuget.Tracing;
using BuildXL.FrontEnd.Sdk;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Tasks;

namespace BuildXL.FrontEnd.Nuget
{
    /// <summary>
    /// The registry that contains all the nuget packages with their configuration.
    /// </summary>
    internal sealed class PackageRegistry
    {
        private readonly FrontEndContext m_context;
        private readonly IReadOnlyList<INugetPackage> m_packages;

        private Dictionary<string, INugetPackage> m_packagesById;
        private Dictionary<string, INugetPackage> m_packagesByIdPlusVersion;

        private readonly Lazy<Possible<Unit>> m_validationResult;

        public PackageRegistry(FrontEndContext context, IReadOnlyList<INugetPackage> packages)
        {
            Contract.Requires(context != null);
            Contract.Requires(packages != null);

            m_context = context;
            m_packages = packages;
            m_validationResult = new Lazy<Possible<Unit>>(() => DoValidate());
        }

        public static Possible<PackageRegistry> Create(FrontEndContext context, IReadOnlyList<INugetPackage> packages)
        {
            var registry = new PackageRegistry(context, packages);
            var result = registry.Validate();
            if (!result.Succeeded)
            {
                return result.Failure;
            }

            return registry;
        }

        public bool IsValid => m_packagesById != null;

        public Dictionary<string, INugetPackage> AllPackagesById
        {
            get
            {
                Contract.Requires(IsValid);
                return m_packagesById;
            }
        }

        public Possible<Unit> Validate() => m_validationResult.Value;

        private Possible<Unit> DoValidate()
        {
            // The flow is:
            // 1. Validate configuration
            // 2. Validate uniqueness and return all the nuget packages.
            var validationResult = TryValidatePackagesConfiguration();
            if (!validationResult.Succeeded)
            {
                return validationResult.Failure;
            }

            // out vars are initialized only when the result is successful.
            return ValidateUniquenes(out m_packagesById, out m_packagesByIdPlusVersion);
        }

        private Possible<Unit> TryValidatePackagesConfiguration()
        {
            var invalidPackages = m_packages.Where(pkg => !ValidatePackageConfiguration(pkg)).Select(pkg => pkg.Alias ?? pkg.Id).ToList();

            return invalidPackages.Count == 0 ? (Possible<Unit>)Unit.Void : NugetFailure.InvalidConfiguration(invalidPackages);
        }

        private Possible<Unit> ValidateUniquenes(out Dictionary<string, INugetPackage> idToPackageMap, out Dictionary<string, INugetPackage> idPlusVersionToPackageMap)
        {
            // TODO: the overall uniquness/aliasing thing should be improved.
            // Today the following case will lead to weird results:
            // package1: id: n1, alias: n2
            // package2: id: n2, alias: n1
            // This will work, but it is not clear what the expected semantic should be.
            // When the user requests 'n1' which package should be used?

            idToPackageMap = idPlusVersionToPackageMap = null;

            // Packages should have unique (id or alias)
            // And (id + version) -- regardless of the alias.
            var duplicateNames = new HashSet<string>();
            var duplicateIdPlusVersion = new HashSet<string>();

            var localIdToPackageMap = new Dictionary<string, INugetPackage>(m_packages.Count);
            var localIdPlusVersionToPackageMap = new Dictionary<string, INugetPackage>(m_packages.Count);
            foreach (var package in m_packages)
            {
                // Track id+version first
                string idPlusVersion = package.Id + "." + package.Version;
                if (!localIdPlusVersionToPackageMap.TryAdd(idPlusVersion, package))
                {
                    duplicateIdPlusVersion.Add(package.Id);
                }

                // Then track id or alias
                if (!localIdToPackageMap.TryAdd(package.Id, package))
                {
                    // A package with the same id is already in the map.
                    if (string.IsNullOrEmpty(package.Alias) || localIdToPackageMap.ContainsKey(package.Alias))
                    {
                        duplicateNames.Add(package.Alias ?? package.Id);
                    }
                    else
                    {
                        // This allows us to have the same package id coexist in our workspace as long as it defines a unique alias.
                        localIdToPackageMap.Add(package.Alias, package);
                    }
                }
            }

            if (duplicateNames.Count != 0)
            {
                return NugetFailure.DuplicatedPackagesWithTheSameIdOrAlias(duplicateNames.ToList());
            }

            if (duplicateIdPlusVersion.Count != 0)
            {
                return NugetFailure.DuplicatedPackagesWithTheSameIdAndVersion(duplicateIdPlusVersion.ToList());
            }

            idToPackageMap = localIdToPackageMap;
            idPlusVersionToPackageMap = localIdPlusVersionToPackageMap;
            return Unit.Void;
        }

        private bool ValidatePackageConfiguration(INugetPackage packageConfiguration)
        {
            if (!PathAtom.TryCreate(m_context.StringTable, packageConfiguration.Version, out _))
            {
                Logger.Log.ConfigNugetPackageVersionIsInvalid(m_context.LoggingContext, packageConfiguration.Version, packageConfiguration.Id);
                return false;
            }

            return true;
        }
    }
}
