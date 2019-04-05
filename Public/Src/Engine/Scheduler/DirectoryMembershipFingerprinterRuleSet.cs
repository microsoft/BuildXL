// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;

#pragma warning disable 1591 // disabling warning about missing API documentation; TODO: Remove this line and write documentation!

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Encapsulates a set of <see cref="DirectoryMembershipFingerprinterRule"/>s.
    /// </summary>
    public sealed class DirectoryMembershipFingerprinterRuleSet
    {
        private readonly Dictionary<AbsolutePath, DirectoryMembershipFingerprinterRule> m_rules;

        /// <summary>
        /// The set of tools using search path enumeration
        /// </summary>
        private readonly List<string> m_searchPathEnumerationToolFragments;

        /// <summary>
        /// Cache of the result of computing whether tool paths match a path fragment in <see cref="m_searchPathEnumerationToolFragments"/>
        /// Match is defined as a case-insensitive suffix match
        /// The size of the cache is chosen arbitrarily to be 1103, but it should be large enough since there shouldn't be
        /// a huge number of tools active during a build
        /// </summary>
        private readonly ObjectCache<string, bool> m_searchPathEnumerationToolMatchCache = new ObjectCache<string, bool>(1103);

        /// <summary>
        /// The parent whitelist
        /// If this is a module specific directory membership fingerprint rule set, parent is the root directory membership fingerprint rule set
        /// Otherwise, if this is the root directory membership fingerprint rule set, this is null
        /// This field is mutually exclusive with the <see cref="m_moduleRuleSets"/> field.
        /// </summary>
        private readonly DirectoryMembershipFingerprinterRuleSet m_parent;

        /// <summary>
        /// The module specific directory membership fingerprint rule sets (null if this is a module specific directory membership fingerprint rule set)
        /// This field is mutually exclusive with the <see cref="m_parent"/> field.
        /// </summary>
        private readonly Dictionary<ModuleId, DirectoryMembershipFingerprinterRuleSet> m_moduleRuleSets;

        /// <summary>
        /// Gets the module specific directory membership fingerprint rule sets. (null if this is a module specific rule set)
        /// </summary>
        public IReadOnlyDictionary<ModuleId, DirectoryMembershipFingerprinterRuleSet> ModuleRuleSets => m_moduleRuleSets;

        /// <summary>
        /// For testing
        /// </summary>
        internal void AddModuleRuleSet(ModuleId moduleId, DirectoryMembershipFingerprinterRuleSet ruleSet)
        {
            m_moduleRuleSets.Add(moduleId, ruleSet);
        }

        public DirectoryMembershipFingerprinterRuleSet(
            IRootModuleConfiguration rootConfiguration,
            StringTable stringTable)
            : this(rootConfiguration, stringTable, parent: null)
        {
            m_moduleRuleSets = new Dictionary<ModuleId, DirectoryMembershipFingerprinterRuleSet>();
            m_searchPathEnumerationToolFragments = new List<string>(
                rootConfiguration.SearchPathEnumerationTools?.Select(toolSuffix =>

                    // Append leading separator to ensure suffix only matches valid relative path fragments
                    Path.DirectorySeparatorChar + toolSuffix.ToString(stringTable)) ??
                Enumerable.Empty<string>());

            foreach (var module in rootConfiguration.ModulePolicies.Values)
            {
                if (module.DirectoryMembershipFingerprinterRules.Count != 0)
                {
                    m_moduleRuleSets.Add(module.ModuleId, new DirectoryMembershipFingerprinterRuleSet(module, stringTable, this));
                }
            }
        }

        /// <summary>
        /// Gets module specific rules
        /// </summary>
        public DirectoryMembershipFingerprinterRuleSet GetModuleRule(ModuleId module)
        {
            Contract.Assert(m_parent == null, "Only root whitelist can be queried for module whitelists");

            DirectoryMembershipFingerprinterRuleSet rs;
            if (m_moduleRuleSets.TryGetValue(module, out rs))
            {
                return rs;
            }

            return this;
        }

        /// <nodoc/>
        private DirectoryMembershipFingerprinterRuleSet(
            IModuleConfiguration moduleConfiguration,
            StringTable stringTable,
            DirectoryMembershipFingerprinterRuleSet parent = null)
            : this(CreateFromConfig(moduleConfiguration, stringTable), parent)
        {
        }

        /// <nodoc/>
        private DirectoryMembershipFingerprinterRuleSet(
            IEnumerable<DirectoryMembershipFingerprinterRule> rules,
            Dictionary<ModuleId, DirectoryMembershipFingerprinterRuleSet> moduleRuleSets,
            List<string> searchPathEnumerationTools,
            DirectoryMembershipFingerprinterRuleSet parent = null)
            : this(rules, parent)
        {
            m_moduleRuleSets = moduleRuleSets;
            m_searchPathEnumerationToolFragments = searchPathEnumerationTools;
        }

        /// <nodoc/>
        public DirectoryMembershipFingerprinterRuleSet(
            IEnumerable<DirectoryMembershipFingerprinterRule> rules,
            DirectoryMembershipFingerprinterRuleSet parent = null)
        {
            m_rules = new Dictionary<AbsolutePath, DirectoryMembershipFingerprinterRule>();
            m_parent = parent;

            if (rules != null)
            {
                foreach (var rule in rules)
                {
                    DirectoryMembershipFingerprinterRule existingException;
                    if (m_rules.TryGetValue(rule.Root, out existingException))
                    {
                        Contract.Assert(
                            false,
                            "Encountered duplicate DirectoryMembershipFingerprinterRule. Duplicates are supposed to be detected when the user provided configuration is validated.");
                    }
                    else
                    {
                        m_rules.Add(rule.Root, rule);
                    }
                }
            }
            else
            {
                m_rules = new Dictionary<AbsolutePath, DirectoryMembershipFingerprinterRule>();
            }
        }

        /// <nodoc/>
        private static IEnumerable<DirectoryMembershipFingerprinterRule> CreateFromConfig(
            IModuleConfiguration moduleConfiguration,
            StringTable stringTable)
        {
            foreach (var rule in moduleConfiguration.DirectoryMembershipFingerprinterRules)
            {
                yield return new DirectoryMembershipFingerprinterRule(
                    rule.Name,
                    rule.Root,
                    rule.DisableFilesystemEnumeration,
                    rule.FileIgnoreWildcards.Select(wildCard => wildCard.ToString(stringTable)).ToList());
            }
        }

        /// <summary>
        /// Computes the hash of all the specified search path tool suffixes
        /// </summary>
        public ContentHash? ComputeSearchPathToolsHash()
        {
            if (m_searchPathEnumerationToolFragments == null || m_searchPathEnumerationToolFragments.Count == 0)
            {
                return null;
            }

            var hash = ContentHashingUtilities.ZeroHash;
            foreach (var searchPathToolSuffix in m_searchPathEnumerationToolFragments)
            {
                hash = ContentHashingUtilities.CombineOrderIndependent(
                    hash,
                    ContentHashingUtilities.HashString(searchPathToolSuffix.ToUpperInvariant()));
            }

            return hash;
        }

        public bool IsSearchPathEnumerationTool(string toolPath)
        {
            // Get the parent module (root config) if applicable. Otherwise, this is the root config so use it.
            var rootRuleSet = m_parent ?? this;

            bool result;
            if (m_searchPathEnumerationToolMatchCache.TryGetValue(toolPath, out result))
            {
                return result;
            }

            result = false;
            foreach (var searchPathToolSuffix in rootRuleSet.m_searchPathEnumerationToolFragments)
            {
                if (toolPath.EndsWith(searchPathToolSuffix, System.StringComparison.OrdinalIgnoreCase))
                {
                    result = true;
                    break;
                }
            }

            // Cache the result
            m_searchPathEnumerationToolMatchCache.AddItem(toolPath, result);

            return result;
        }

        /// <summary>
        /// Attempts to get a rule corresponding to the path
        /// </summary>
        public bool TryGetRule(AbsolutePath path, out DirectoryMembershipFingerprinterRule rule)
        {
            if (!m_rules.TryGetValue(path, out rule))
            {
                if (m_parent != null)
                {
                    return m_parent.TryGetRule(path, out rule);
                }

                return false;
            }

            return true;
        }

        /// <nodoc/>
        public void Serialize(BuildXLWriter writer)
        {
            writer.WriteCompact(m_rules.Count);
            foreach (var rule in m_rules)
            {
                rule.Value.Serialize(writer);
            }

            if (m_searchPathEnumerationToolFragments != null)
            {
                writer.Write(true);
                writer.WriteCompact(m_searchPathEnumerationToolFragments.Count);
                foreach (var tool in m_searchPathEnumerationToolFragments)
                {
                    writer.Write(tool);
                }
            }
            else
            {
                writer.Write(false);
            }

            if (m_moduleRuleSets != null)
            {
                writer.Write(true);
                writer.WriteCompact(m_moduleRuleSets.Count);
                foreach (var moduleRuleSetEntry in m_moduleRuleSets)
                {
                    ModuleId moduleId = moduleRuleSetEntry.Key;
                    DirectoryMembershipFingerprinterRuleSet moduleRuleSet = moduleRuleSetEntry.Value;
                    writer.Write(moduleId);
                    moduleRuleSet.Serialize(writer);
                }
            }
            else
            {
                writer.Write(false);
            }
        }

        /// <nodoc/>
        public static DirectoryMembershipFingerprinterRuleSet Deserialize(BuildXLReader reader)
        {
            return Deserialize(reader, parent: null);
        }

        private static DirectoryMembershipFingerprinterRuleSet Deserialize(BuildXLReader reader, DirectoryMembershipFingerprinterRuleSet parent)
        {
            int ruleCount = reader.ReadInt32Compact();
            List<DirectoryMembershipFingerprinterRule> rules = new List<DirectoryMembershipFingerprinterRule>(ruleCount);
            for (int i = 0; i < ruleCount; i++)
            {
                rules.Add(DirectoryMembershipFingerprinterRule.Deserialize(reader));
            }

            List<string> searchPathEnumerationTools = null;
            if (reader.ReadBoolean())
            {
                var count = reader.ReadInt32Compact();
                searchPathEnumerationTools = new List<string>();
                for (int i = 0; i < count; i++)
                {
                    var tool = reader.ReadString();
                    searchPathEnumerationTools.Add(tool);
                }
            }

            DirectoryMembershipFingerprinterRuleSet result;
            Dictionary<ModuleId, DirectoryMembershipFingerprinterRuleSet> moduleRuleSets = null;
            if (reader.ReadBoolean())
            {
                moduleRuleSets = new Dictionary<ModuleId, DirectoryMembershipFingerprinterRuleSet>();
                result = new DirectoryMembershipFingerprinterRuleSet(rules, moduleRuleSets, searchPathEnumerationTools, parent);
                var count = reader.ReadInt32Compact();
                for (int i = 0; i < count; i++)
                {
                    var moduleId = reader.ReadModuleId();
                    var moduleRuleSet = Deserialize(reader, result);
                    result.m_moduleRuleSets.Add(moduleId, moduleRuleSet);
                }
            }
            else
            {
                result = new DirectoryMembershipFingerprinterRuleSet(rules, moduleRuleSets, searchPathEnumerationTools, parent);
            }

            return result;
        }
    }
}
