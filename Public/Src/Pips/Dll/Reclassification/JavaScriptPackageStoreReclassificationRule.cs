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
    /// Non-DFA causing observation types (absent probes and enumerations) under the package store are ignored. The main goal with this is to reduce cache candidate churn for tools that very liberally enumerate
    /// packages across the store that in the end are not read. In theory this is not completely safe, but in practice is unlikely for a tool to make decisions solely based on the result of enumerating/absent probing 
    /// packages without actually reading content from them.
    /// This assumes no writes happen during the build under the package store location, and that the only writer is the package manager itself.
    /// </remarks>
    public class JavaScriptPackageStoreReclassificationRule : IInternalReclassificationRule
    {
        private readonly AbsolutePath m_packageStoreLocation;
        private readonly string m_ruleName;
        private readonly string m_moduleName;
        
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
        public string Descriptor() => "JavaScriptPackageStoreReclassificationRuleV3";

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
                // Absent probes and enumerations never cause DFAs. Just ignore them if they happen under the store folder and limit the actual reclassifications to present probes and reads.
                // Rationale: on one hand we observed there is a lot of churn in cache candidates caused by accesses on the package store, even with this rule on. So just considering reads has a good impact on cache lookup performance
                // just by reducing the amount of candidates. On the reliability side, this might open a small gap where a tool makes a decision purely based on an enumeration/absent probe. But for the case of the package store this is deemed
                // safe enough to ignore.
                if (type == ObservationType.AbsentPathProbe || type == ObservationType.DirectoryEnumeration)
                {
                    reclassification = new ReclassificationResult(m_ruleName, null, path.Path);
                    return true;
                }

                // The package name is the first segment of the relative path (under a JS package store, each package is stored under its own directory, directly under the package store directory)
                var relativePackageStoreAtoms = relativeToPackageStore.GetAtoms();
                var packageName = relativePackageStoreAtoms[0];
                // We want to reclassify to a probe on the package directory itself, but only the first time we see an access under that package. All other accesses under
                // the same package can be ignored, since the probe serves as a representative for all accesses under that package.
                // We mark the result as cacheable so the (per-pip) ObservationReclassifier collapses the duplicate probes (i.e. only the first access to a given package
                // produces a probe, and the rest are ignored).
                reclassification = new ReclassificationResult(m_ruleName, ObservationType.ExistingDirectoryProbe, m_packageStoreLocation.Combine(pathTable, packageName), CanBeCached: true);

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
