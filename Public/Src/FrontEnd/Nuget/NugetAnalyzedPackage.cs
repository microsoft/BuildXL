// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using BuildXL.FrontEnd.Nuget.Tracing;
using BuildXL.FrontEnd.Sdk;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using NuGet.Versioning;
using Moniker = BuildXL.Utilities.PathAtom;

namespace BuildXL.FrontEnd.Nuget
{
    /// <summary>
    /// Contains information about the analyzed package contents.
    /// </summary>
    public sealed class NugetAnalyzedPackage
    {
        /// <summary>
        /// Source of the package: disk, cache, nuget.
        /// </summary>
        public PackageSource Source => PackageOnDisk.PackageDownloadResult.Source;

        /// <summary>
        /// Nuget package dependencies
        /// </summary>
        public IReadOnlyList<INugetPackage> Dependencies { get; private set; }

        /// <nodoc />
        public MultiValueDictionary<Moniker, INugetPackage> DependenciesPerFramework { get; private set; }

        /// <nodoc />
        public bool IsManagedPackage { get; set; }

        /// <summary>
        /// Supported target frameworks of the package.
        /// </summary>
        public List<Moniker> TargetFrameworks { get; }

        /// <summary>
        /// Package assembly references per framework
        /// </summary>
        public MultiValueDictionary<NugetTargetFramework, RelativePath> References { get; private set; }

        /// <summary>
        /// Package libraries per framework
        /// </summary>
        public MultiValueDictionary<NugetTargetFramework, RelativePath> Libraries { get; private set; }

        /// <summary>
        /// Assembly name to target framework. An assembly may be available in multiple frameworks
        /// </summary>
        public MultiValueDictionary<PathAtom, NugetTargetFramework> AssemblyToTargetFramework { get; private set; }

        /// <nodoc />
        public PackageOnDisk PackageOnDisk { get; }

        /// <nodoc />
        public string Id => PackageOnDisk.Package.Id;

        /// <nodoc />
        public string Alias => PackageOnDisk.Package.Alias;

        /// <nodoc/>
        public string ActualId => string.IsNullOrEmpty(Alias) ? Id : Alias;

        /// <nodoc />
        public string Version => PackageOnDisk.Package.Version;

        /// <nodoc />
        public string Tfm => PackageOnDisk.Package.Tfm;

        /// <nodoc />
        public List<string> DependentPackageIdsToSkip => PackageOnDisk.Package.DependentPackageIdsToSkip ?? new List<string>() { };

        /// <nodoc />
        public List<string> DependentPackageIdsToIgnore => PackageOnDisk.Package.DependentPackageIdsToIgnore ?? new List<string>() { };

        /// <nodoc />
        public bool ForceFullFrameworkQualifiersOnly => PackageOnDisk.Package.ForceFullFrameworkQualifiersOnly;

        /// <summary>
        /// A compound framework is a target framework that contains '+' or '-' (e.g 'portable-net45+win8+wpa81'). This means that different
        /// target framework folders can be compatible with the same known moniker (e.g. 'portable-net45+win8+wpa81' and 'net45' are both compatible with
        /// the known moniker 'net45'). Given a set of framework folders that map to the same known moniker, we want to pick always the same one, so
        /// we avoid mixing artifacts
        /// This dictionary maps known monikers to framework folders, to make sure that the first framework folder that is mapped to a known moniker is always
        /// used across the package.
        /// </summary>
        private readonly Dictionary<Moniker, PathAtom> m_monikerToTargetFramework = new Dictionary<Moniker, PathAtom>();

        private readonly FrontEndContext m_context;

        /// <nodoc />
        public NugetFrameworkMonikers NugetFrameworkMonikers { get; }

        private readonly XDocument m_nuSpec;
        private readonly Dictionary<string, INugetPackage> m_packagesOnConfig;
        private readonly bool m_doNotEnforceDependencyVersions;

        /// <nodoc/>
        private NugetAnalyzedPackage(
            FrontEndContext context,
            NugetFrameworkMonikers nugetFrameworkMonikers,
            XDocument nuSpec,
            PackageOnDisk packageOnDisk,
            Dictionary<string, INugetPackage> packagesOnConfig,
            bool doNotEnforceDependencyVersions)
        {
            m_context = context;
            m_nuSpec = nuSpec;
            PackageOnDisk = packageOnDisk;
            NugetFrameworkMonikers = nugetFrameworkMonikers;
            m_packagesOnConfig = packagesOnConfig;
            m_doNotEnforceDependencyVersions = doNotEnforceDependencyVersions;
            TargetFrameworks = new List<Moniker>();
        }

        /// <summary>
        /// Constructs a new NugetAnalyzed Package.
        /// </summary>
        /// <remarks>
        /// In case of failure it will log a detailed message and return null.
        /// </remarks>
        public static NugetAnalyzedPackage TryAnalyzeNugetPackage(
            FrontEndContext context,
            NugetFrameworkMonikers nugetFrameworkMonikers,
            XDocument nuSpec,
            PackageOnDisk packageOnDisk,
            Dictionary<string, INugetPackage> packagesOnConfig,
            bool doNotEnforceDependencyVersions)
        {
            Contract.Requires(context != null);
            Contract.Requires(nuSpec != null);
            Contract.Requires(packageOnDisk != null);
            Contract.Requires(packageOnDisk.NuSpecFile.IsValid);

            var analyzedPackage = new NugetAnalyzedPackage(context, nugetFrameworkMonikers, nuSpec, packageOnDisk,
                packagesOnConfig, doNotEnforceDependencyVersions);

            analyzedPackage.ParseManagedSemantics();
            if (!analyzedPackage.TryParseDependenciesFromNuSpec())
            {
                return null;
            }

            return analyzedPackage;
        }

        private void ParseManagedSemantics()
        {
            var stringTable = m_context.PathTable.StringTable;
            var magicNugetMarker = PathAtom.Create(stringTable, "_._");
            var dllExtension = PathAtom.Create(stringTable, ".dll");

            var refs = new MultiValueDictionary<NugetTargetFramework, RelativePath>();
            var libs = new MultiValueDictionary<NugetTargetFramework, RelativePath>();
            var assemblyToTargetFramework = new MultiValueDictionary<PathAtom, NugetTargetFramework>();

            foreach (var relativePath in PackageOnDisk.Contents)
            {
                // This is a dll. Check if it is in a lib folder or ref folder.
                var atoms = relativePath.GetAtoms();
                if (atoms.Length == 3)
                {
                    var libOrRef = atoms[0];
                    var targetFrameworkFolder = atoms[1];
                    var fileName = atoms[2];

                    var isLib = NugetFrameworkMonikers.LibFolderName.CaseInsensitiveEquals(stringTable, libOrRef);
                    var isRef = NugetFrameworkMonikers.RefFolderName.CaseInsensitiveEquals(stringTable, libOrRef);

                    if (isLib || isRef)
                    {
                        if (!TryGetKnownTargetFramework(targetFrameworkFolder, out NugetTargetFramework targetFramework))
                        {
                            // We skip unknown frameworks, packages are not necessarily well constructed. We log this
                            // as a verbose message (i.e., this is not an error).
                            Logger.Log.NugetUnknownFramework(m_context.LoggingContext, PackageOnDisk.Package.Id,
                                targetFrameworkFolder.ToString(stringTable), relativePath.ToString(stringTable));
                            continue;
                        }

                        var isManagedEntry = false;
                        var ext = fileName.GetExtension(stringTable);
                        if (dllExtension.CaseInsensitiveEquals(stringTable, ext))
                        {
                            isManagedEntry = true;
                            if (isRef)
                            {
                                refs.Add(targetFramework, relativePath);
                            }

                            if (isLib)
                            {
                                libs.Add(targetFramework, relativePath);
                            }
                        }
                        else if (fileName == magicNugetMarker)
                        {
                            isManagedEntry = true;
                        }

                        if (isManagedEntry)
                        {
                            IsManagedPackage = true;

                            if (!TargetFrameworks.Contains(targetFramework.Moniker))
                            {
                                TargetFrameworks.Add(targetFramework.Moniker);
                            }

                            // The magic marker is there so the framework is declared as supported, but no actual files are listed
                            // So we don't want to add a magic marker as a real artifact that can be referenced.
                            if (fileName != magicNugetMarker)
                            {
                                assemblyToTargetFramework.Add(fileName, targetFramework);
                            }
                        }
                    }
                }
            }

            if (TargetFrameworks.Count == 0)
            {
                var history = ForceFullFrameworkQualifiersOnly ?
                    NugetFrameworkMonikers.FullFrameworkVersionHistory :
                    NugetFrameworkMonikers.WellknownMonikers.ToList();

                // TODO: Remove this once we have a LKG with ForceFullFrameworkQualifiersOnly on spec generation
                if (Id.Equals("Bond.NET"))
                {
                    history = NugetFrameworkMonikers.FullFrameworkVersionHistory;
                }

                foreach (var moniker in history)
                {
                    TargetFrameworks.Add(moniker);
                }
            }

            // For the refs without lib, copy them to refs.
            foreach (var kv in libs)
            {
                if (!refs.ContainsKey(kv.Key))
                {
                    refs.Add(kv.Key, kv.Value.ToArray());
                }
            }

            References = refs;
            Libraries = libs;
            AssemblyToTargetFramework = assemblyToTargetFramework;
        }

        /// <summary>
        /// Deals with cases like 'lib/portable-net45+win8+wpa81/System.Threading.Tasks.Dataflow.dll'. Splits the compound target framework
        /// directory using '-' and '+' as separators and tries to find the first fragment that matches a known target framework.
        /// </summary>
        /// <remarks>
        /// To avoid ambiguity the first framework that gets evaluated and contains a known moniker succeeds, but subsequent
        /// compound frameworks that would resolve to the same moniker fail. In that way, the first known framework is mapped to the same moniker and other
        /// candidates are ignored.
        /// </remarks>
        private bool TryGetKnownTargetFramework(PathAtom targetFrameworkFolder, out NugetTargetFramework targetFramework)
        {
            Contract.Assert(targetFrameworkFolder.IsValid);

            var targetFrameworkFragments = targetFrameworkFolder.ToString(m_context.StringTable).Split('+', '-');

            // If there are no + or -, then the moniker and the target framework folder are equivalent
            // This is the most common case
            if (targetFrameworkFragments.Length == 1)
            {
                if (NugetFrameworkMonikers.WellknownMonikers.Contains(targetFrameworkFolder))
                {
                    // If this is the first time we see this known moniker, we record it
                    // so no other (compound) target frameworks are used for the same folder
                    if (!m_monikerToTargetFramework.ContainsKey(targetFrameworkFolder))
                    {
                        m_monikerToTargetFramework.Add(targetFrameworkFolder, targetFrameworkFolder);
                    }

                    targetFramework = new NugetTargetFramework(targetFrameworkFolder);
                    return true;
                }
            }

            foreach (var target in targetFrameworkFragments)
            {
                if (!PathAtom.TryCreate(m_context.StringTable, target, out Moniker moniker))
                {
                    targetFramework = default(NugetTargetFramework);
                    return false;
                }

                // Check if we saw a compound framework before mapped to the same moniker
                if (m_monikerToTargetFramework.ContainsKey(moniker))
                {
                    // We saw it and it's the same target framework folder
                    if (m_monikerToTargetFramework[moniker] == targetFrameworkFolder)
                    {
                        targetFramework = new NugetTargetFramework(moniker, targetFrameworkFolder);
                        return true;
                    }

                    // We saw it, but the folder is different, so we make it fail
                    targetFramework = default(NugetTargetFramework);
                    return false;
                }

                // We didn't see this compound framework, so we check if it maps to a known moniker and if
                // that's the case we update the compound monikers seen so far and we return it
                if (NugetFrameworkMonikers.WellknownMonikers.Contains(moniker))
                {
                    m_monikerToTargetFramework.Add(moniker, targetFrameworkFolder);

                    targetFramework = new NugetTargetFramework(moniker, targetFrameworkFolder);
                    return true;
                }
            }

            targetFramework = default(NugetTargetFramework);
            return false;
        }

        /// <nodoc />
        private bool TryParseDependenciesFromNuSpec()
        {
            var dependencyNodes = m_nuSpec
                .Elements()
                .Where(el => string.Equals(el.Name.LocalName, "package", StringComparison.Ordinal))
                .Elements()
                .Where(el => string.Equals(el.Name.LocalName, "metadata", StringComparison.Ordinal))
                .Elements()
                .Where(el => string.Equals(el.Name.LocalName, "dependencies", StringComparison.Ordinal))
                .Elements();

            // Namespace independent query, nuget has about 6 different namespaces as of may 2016.
            var dependencies = new List<INugetPackage>();
            var skipIdLookupTable = new HashSet<string>(this.DependentPackageIdsToSkip);
            var ignoreIdLookupTable = new HashSet<string>(this.DependentPackageIdsToIgnore);
            bool skipAllDependencies = skipIdLookupTable.Contains("*");
            bool ignoreAllDependencies = ignoreIdLookupTable.Contains("*");

            foreach (var dependency in dependencyNodes.Where(el => string.Equals(el.Name.LocalName, "dependency", StringComparison.Ordinal)))
            {
                var genericDependency = ReadDependencyElement(dependency);
                if (genericDependency == null && !(ignoreAllDependencies || ignoreIdLookupTable.Contains(dependency.Attribute("id")?.Value?.Trim())))
                {
                    return false;
                }

                if (genericDependency != null && !skipAllDependencies && !skipIdLookupTable.Contains(genericDependency.GetPackageIdentity()))
                {
                    dependencies.Add(genericDependency);
                }
            }

            var dependenciesPerFramework = new MultiValueDictionary<PathAtom, INugetPackage>();
            var groups = dependencyNodes.Where(el => string.Equals(el.Name.LocalName, "group", StringComparison.Ordinal));

            foreach (var group in groups)
            {
                if (group.Attribute("targetFramework") != null && NugetFrameworkMonikers.TargetFrameworkNameToMoniker.TryGetValue(group.Attribute("targetFramework").Value, out Moniker targetFramework))
                {
                    if (group.Elements().Any())
                    {
                        // If there is at least one valid dependency for a known framework, then the package is defined as managed
                        IsManagedPackage = true;

                        // Only add the group dependency target framework if the nuget package itself also contains specific assemblies of the same version
                        if (!TargetFrameworks.Contains(targetFramework) && (References.Keys.Any(tfm => tfm.Moniker == targetFramework) || Libraries.Keys.Any(tfm => tfm.Moniker == targetFramework)))
                        {
                            TargetFrameworks.Add(targetFramework);
                        }

                        // If the package has a pinned tfm and the groups tfm does not match, skip the groups dependency resolution
                        if (!string.IsNullOrEmpty(this.Tfm) && NugetFrameworkMonikers.TargetFrameworkNameToMoniker.TryGetValue(this.Tfm, out Moniker pinnedTfm) && !PathAtom.Equals(pinnedTfm, targetFramework))
                        {
                            continue;
                        }

                        foreach (
                            var dependency in
                                group.Elements().Where(
                                    el => string.Equals(el.Name.LocalName, "dependency", StringComparison.Ordinal)))
                        {
                            var grouppedDependency = ReadDependencyElement(dependency);
                            if (grouppedDependency == null && !(ignoreAllDependencies || ignoreIdLookupTable.Contains(dependency.Attribute("id")?.Value?.Trim())))
                            {
                                return false;
                            }

                            if (grouppedDependency != null && !skipAllDependencies && !skipIdLookupTable.Contains(grouppedDependency.GetPackageIdentity()))
                            {
                                dependenciesPerFramework.Add(targetFramework, grouppedDependency);
                            }
                        }
                    }
                }
            }

            Dependencies = dependencies;
            DependenciesPerFramework = dependenciesPerFramework;

            return true;
        }

        [SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly")]
        private INugetPackage ReadDependencyElement(XElement dependency)
        {
            var idAttr = dependency.Attribute("id");

            if (idAttr == null)
            {
                Logger.Log.NugetFailedToReadNuSpecFile(
                    m_context.LoggingContext,
                    PackageOnDisk.Package.Id,
                    PackageOnDisk.Package.Version,
                    PackageOnDisk.NuSpecFile.ToString(m_context.PathTable),
                    "Malformed NuGet dependency. 'id' is a required attribute.");

                return null;
            }

            var versionAttr = dependency.Attribute("version");

            var version = versionAttr?.Value?.Trim();
            if (TryResolveNugetPackageVersion(m_packagesOnConfig, PackageOnDisk.Package, idAttr.Value.Trim(), version,
                m_doNotEnforceDependencyVersions, out INugetPackage nugetPackageDependency, out string errorMessage))
            {
                return nugetPackageDependency;
            }

            Logger.Log.NugetFailedToReadNuSpecFile(
                m_context.LoggingContext,
                PackageOnDisk.Package.Id,
                PackageOnDisk.Package.Version,
                PackageOnDisk.NuSpecFile.ToString(m_context.PathTable),
                errorMessage);

            return null;
        }

        /// <summary>
        ///     Given the list of packages specified in the config file, tries to find a candidate in that list such that it
        ///     matches
        ///     a requested dependency on the nuspec being interpreted.
        /// </summary>
        /// <remarks>
        ///     This behavior works under the assumption the user has to specify all transitive dependencies in the config file,
        ///     and each dependency only supports one version.
        /// </remarks>
        /// <returns>
        ///     Whether a candidate was found. Upon success, nugetPackage contains the candidate. Otherwise, errorMessage contains
        ///     an explanation of what happened
        /// </returns>
        [SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly", MessageId = "NuGet")]
        private bool TryResolveNugetPackageVersion(
            Dictionary<string, INugetPackage> packagesOnConfig,
            INugetPackage requestorPackage,
            string id,
            string version,
            bool doNotEnforceDependencyVersions,
            out INugetPackage nugetPackage,
            out string errorMessage)
        {
            Contract.Assert(id != null);

            // First, the requestedId must exist in the list specified in the config file
            if (!packagesOnConfig.ContainsKey(id))
            {
                nugetPackage = null;
                if (requestorPackage.DependentPackageIdsToIgnore.Contains(id) || requestorPackage.DependentPackageIdsToIgnore.Contains("*")) {
                    errorMessage = null;
                    return true;
                }

                errorMessage = string.Format(
                    CultureInfo.InvariantCulture,
                    "The requested dependency with id '{0}' and version '{1}' is not explicitly listed in the configuration file.", id, version);
                return false;
            }

            // Now we deal with the version. The candidate package is what we found above, but we have to validate the version is valid wrt the request
            var candidatePackage = packagesOnConfig[id];

            // If the version is not specified, we use the one listed in the config
            if (version == null)
            {
                nugetPackage = candidatePackage;
                errorMessage = null;

                // This is just informative. We succeeded already.
                Logger.Log.NugetDependencyVersionWasNotSpecifiedButConfigOneWasChosen(
                    m_context.LoggingContext,
                    nugetPackage.Id,
                    nugetPackage.Version);

                return true;
            }

            // Now we parse the requested version to validate it is compatible with the one specified in the config
            if (!NuGetVersion.TryParse(candidatePackage.Version, out var packageOnConfigVersion))
            {
                nugetPackage = null;
                errorMessage = string.Format(
                    CultureInfo.InvariantCulture,
                    "Version '{1}' on package '{0}' is malformed.", candidatePackage.Id, packagesOnConfig[id].Version);
                return false;
            }

            if (VersionRange.TryParse(version, out var versionRange))
            {
                if (versionRange.Satisfies(packageOnConfigVersion))
                {
                    nugetPackage = candidatePackage;
                    errorMessage = null;

                    // This is just informative. We succeeded already.
                    Logger.Log.NugetDependencyVersionWasPickedWithinRange(
                        m_context.LoggingContext,
                        nugetPackage.Id,
                        nugetPackage.Version,
                        version);

                    return true;
                }

                if (doNotEnforceDependencyVersions)
                {
                    nugetPackage = candidatePackage;
                    errorMessage = null;

                    // This is a warning, but we suceeded since versions are configured to not be enforced
                    Logger.Log.NugetDependencyVersionDoesNotMatch(
                        m_context.LoggingContext,
                        requestorPackage.Id,
                        requestorPackage.Version,
                        nugetPackage.Id,
                        nugetPackage.Version,
                        version);

                    return true;
                }

                nugetPackage = null;
                errorMessage = string.Format(
                    CultureInfo.InvariantCulture,
                    "Package '{0}' is specified with version '{1}', but that is not contained in the interval '{2}'.",
                    id, candidatePackage.Version, version);
                return false;
            }

            nugetPackage = null;
            errorMessage = string.Format(CultureInfo.InvariantCulture, "Could not parse version '{0}'.", version);
            return false;
        }
    }
}
