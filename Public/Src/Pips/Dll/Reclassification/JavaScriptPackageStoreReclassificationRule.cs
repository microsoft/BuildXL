// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Native.IO;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Core;

#nullable enable

namespace BuildXL.Pips.Reclassification
{ 
    /// <summary>
    /// A reclassification rule for codebases using content-addressable package stores (e.g. Yarn strict, pnpm) for package management.
    /// </summary>
    /// <remarks>
    /// The goal of this rule is to reduce the number of accesses BuildXL needs to track. The idea is that when using a content-addressable package store, the directory name containing the package files
    /// univocally determines the package content. E.g. ".store\@babylonjs-core@7.54.3-d93831e7ae9116fa2dd7" should always contain the same files.
    /// Therefore, we can reclassify all accesses under a package directory to a probe on the package directory itself, which reduces the number of tracked accesses significantly.
    /// This assumes no writes happen during the build under the package store location, and that the only writer is the package manager itself.
    /// </remarks>
    public class JavaScriptPackageStoreReclassificationRule : IInternalReclassificationRule
    {
        private readonly AbsolutePath m_packageStoreLocation;
        private readonly string m_ruleName;
        private readonly string m_moduleName;
        private readonly HashSet<PathAtom> m_knownPackages = new HashSet<PathAtom>();
        
        /// <inheritdoc/>
        public string Name() => m_ruleName;

        /// <nodoc/>
        public JavaScriptPackageStoreReclassificationRule(string moduleName, AbsolutePath packageStoreLocation)
        {
            Contract.Requires(!string.IsNullOrEmpty(moduleName));
            Contract.Requires(packageStoreLocation.IsValid);

            m_packageStoreLocation = packageStoreLocation;
            m_ruleName = $"JavaScriptPackageStoreReclassificationRule({moduleName})";
            m_moduleName = moduleName;
        }

        /// <summary>
        /// Bump the descriptor when the implementation changes in a breaking way
        /// </summary>
        public string Descriptor() => "JavaScriptPackageStoreReclassificationRuleV1";

        /// <nodoc/>
        public void Serialize(BuildXLWriter writer)
        {
            writer.Write(m_moduleName);
            writer.Write(m_packageStoreLocation);
        }

        /// <nodoc/>
        public static IInternalReclassificationRule Deserialize(BuildXLReader reader)
        {
            var moduleName = reader.ReadString();
            var packageStoreLocationString = reader.ReadAbsolutePath();
            return new JavaScriptPackageStoreReclassificationRule(moduleName, packageStoreLocationString);
        }

        /// <inheritdoc/>
        public bool TryReclassify(ExpandedAbsolutePath path, PathTable pathTable, ObservationType type, out ReclassificationResult reclassification)
        {
            // Try get the relative path from the package store location to the observed path
            var isRelative = m_packageStoreLocation.TryGetRelative(pathTable, path.Path, out var relativeToPackageStore);
            if (isRelative && !relativeToPackageStore.IsEmpty)
            {
                // The package name is the first segment of the relative path (under a JS package store, each package is stored under its own directory, directly under the package store directory)
                var relativePackageStoreAtoms = relativeToPackageStore.GetAtoms();
                var packageName = relativePackageStoreAtoms[0];
                // We want to reclassify to a probe on the package directory itself, but only the first time we see an access under that package. We can just ignore all other accesses under
                // the same package after that, since the probe serves as a representative for all accesses under that package.
                if (m_knownPackages.Add(packageName))
                {
                    // If the access we get is an absent probe, let's check if the package exists at all (if it is not an absent probe, we know the package exists since the access is a descendant of it)
                    ObservationType reclassifiedType;
                    if (type == ObservationType.AbsentPathProbe)
                    {
                        Possible<PathExistence, NativeFailure> exists;
                        // This is just an optimization: if the relative path has only one segment, it means the access is directly on the package name itself, and since the observation type is absent, we can conclude
                        // that the package does not exist without probing the file system
                        if (relativePackageStoreAtoms.Length == 1)
                        {
                            exists = PathExistence.Nonexistent;
                        }
                        // And this is the general case where the observation came on a descendant of the package directory, so we need to probe the package store to see if the package exists at all
                        else
                        {
                            var packagePath = m_packageStoreLocation.Combine(pathTable, packageName).ToString(pathTable);
                            // The package store is under a shared opaque exclusion, so whether a package exists or not is a permanent state for the duration of the build. If a package is not there, it will never be.
                            exists = FileUtilities.TryProbePathExistence(packagePath, followSymlink: false);
                        }

                        if (!exists.Succeeded)
                        {
                            // If we cannot determine existence, we cannot reclassify
                            reclassification = default;
                            return false;
                        }

                        // This is the most common case: the package exists, so we reclassify to a directory probe on the package directory
                        if (exists.Result == PathExistence.ExistsAsDirectory)
                        {
                            reclassifiedType = ObservationType.ExistingDirectoryProbe;
                        }
                        // A slightly less common case, where there is a file right under the package store (instead of a directory). In this case we map it to a corresponding file probe
                        // Not that we really expect this case, the package store should always contain directories directly under it, but we handle it just in case
                        else if (exists.Result == PathExistence.ExistsAsFile)
                        {
                            reclassifiedType = ObservationType.ExistingFileProbe;
                        }
                        else
                        {
                            Contract.Equals(exists.Result, PathExistence.Nonexistent);
                            reclassifiedType = ObservationType.AbsentPathProbe;
                        }
                    }
                    else 
                    { 
                        reclassifiedType = ObservationType.ExistingDirectoryProbe;
                    }

                    reclassification = new ReclassificationResult(m_ruleName, reclassifiedType, m_packageStoreLocation.Combine(pathTable, packageName));
                }
                else
                {
                    reclassification = new ReclassificationResult(m_ruleName, null, path.Path);
                }

                return true;
            }

            reclassification = default;
            return false;
        }

        /// <summary>
        /// This rule is always valid.
        /// </summary>
        public bool Validate(out string error)
        {
            error = string.Empty;
            return true;
        }

        /// <inheritdoc/>
        public IDictionary<string, object> GetDisplayDescription(PathTable pathTable)
        {
            return new Dictionary<string, object>
            {
                ["Type"] = nameof(JavaScriptPackageStoreReclassificationRule),
                ["Name"] = m_ruleName,
                ["Module Name"] = m_moduleName,
                ["Package Store Location"] = m_packageStoreLocation.ToString(pathTable),
            };
        }
    }
}
