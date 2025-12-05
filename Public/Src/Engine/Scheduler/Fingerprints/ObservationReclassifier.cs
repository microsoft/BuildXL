// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Pips.Reclassification;
using BuildXL.Storage;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Core;

namespace BuildXL.Scheduler.Fingerprints
{
    /// <nodoc />
    public readonly record struct FingerprintReclassificationResult(string AppliedRuleName, ObservedInputType? ReclassifyToType, AbsolutePath ReclassifyToPath);

    /// <inheritdoc />
    public class ObservationReclassifier
    {
        /// <nodoc />
        public ObservationReclassifier() : this(Array.Empty<IInternalReclassificationRule>()) { }

        private ObservationReclassifier(IInternalReclassificationRule[] rules)
        {
            m_rules = rules;
        }

        private CounterCollection<PipExecutorCounter> m_counters;
        private IInternalReclassificationRule[] m_rules;

        /// <summary>
        /// Initializes the rules given a user provided configuration. 
        /// This method throws <see cref="BuildXLException"/> if there is some error while initializing the rules, 
        /// such as forbidden reclassifications.
        /// </summary>
        public void Initialize(IReadOnlyCollection<IInternalReclassificationRule> rules, CounterCollection<PipExecutorCounter> counters = null)
        {
            if (!ValidateRules(rules, out var errors))
            {
                throw new BuildXLException($"Failure inizializing the observation reclassification rules. Errors: {errors}");
            }

            m_counters = counters ?? new();
            m_rules = rules.ToArray();
        }

        private bool ValidateRules(IReadOnlyCollection<IInternalReclassificationRule> rules, out string errors)
        {
            var errorBuilder = new StringBuilder();
            bool hasErrors = false;
            var ruleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rule in rules)
            {
                // 1. Rule name can't be repeated
                if (!string.IsNullOrEmpty(rule.Name()) && ruleNames.Contains(rule.Name()))
                {
                    errorBuilder.AppendLine($"Duplicate rule name found: '{rule.Name()}'");
                    hasErrors = true;
                }

                ruleNames.Add(rule.Name());

                if (!rule.Validate(out var error))
                {
                    errorBuilder.AppendLine(error);
                    hasErrors = true;
                }
            }

            errors = errorBuilder.ToString();
            return !hasErrors;
        }

        /// <summary>
        /// If true, the given path must be reclassified from its observed type to the one returned in the out parameter.
        /// The out parameter is a <see cref="FingerprintReclassificationResult"/> which also includes the name of the rule that applied,
        /// for tracing purposes.
        /// </summary>
        public bool TryReclassify(AbsolutePath path, PathTable pathTable, ObservedInputType type, out FingerprintReclassificationResult reclassification)
        {
            reclassification = default;

            if (m_rules.Length == 0)
            {
                return false;
            }
            
            ExpandedAbsolutePath expandedPath = path.Expand(pathTable);

            for (var i = 0; i < m_rules.Length; i++)
            {
                var rule = m_rules[i];

                using (m_counters.StartStopwatch(PipExecutorCounter.ReclassificationRulesDuration))
                {
                    if (rule.TryReclassify(expandedPath, pathTable, FromObservedInputType(type), out var result))
                    {
                        m_counters.IncrementCounter(PipExecutorCounter.NumReclassifiedObservations);
                        reclassification = new(result.AppliedRuleName, FromObservationType(result.ReclassifyToType), result.ReclassifyToPath);
                        return true;
                    }
                }
            }

            return false;
        }

        // We need to cast from the 'configuration' enums to the 'fingerprinting' enums
        private static ObservedInputType? FromObservationType(ObservationType? from) => from switch
        {
            ObservationType.AbsentPathProbe => ObservedInputType.AbsentPathProbe,
            ObservationType.FileContentRead => ObservedInputType.FileContentRead,
            ObservationType.DirectoryEnumeration => ObservedInputType.DirectoryEnumeration,
            ObservationType.ExistingDirectoryProbe => ObservedInputType.ExistingDirectoryProbe,
            ObservationType.ExistingFileProbe => ObservedInputType.ExistingFileProbe,
            null => null,
            // Should never happen as these values come from a union type in DScript
            _ => throw new ArgumentException($"Unknown ObservationType {from}")
        };

        // We need to cast from the 'fingerprinting' enums to the 'configuration' enums
        private static ObservationType FromObservedInputType(ObservedInputType from) => from switch
        {
            ObservedInputType.AbsentPathProbe => ObservationType.AbsentPathProbe,
            ObservedInputType.FileContentRead => ObservationType.FileContentRead,
            ObservedInputType.DirectoryEnumeration => ObservationType.DirectoryEnumeration,
            ObservedInputType.ExistingDirectoryProbe => ObservationType.ExistingDirectoryProbe,
            ObservedInputType.ExistingFileProbe => ObservationType.ExistingFileProbe,
            // Should never happen as these values come from a union type in DScript
            _ => throw new ArgumentException($"Unknown ObservedInputType {from}")
        };


        /// <summary>
        /// Serialize
        /// </summary>
        public void Serialize(BuildXLWriter writer)
        {
            Contract.Requires(writer != null);
            writer.Write(m_rules.Length);
            foreach (var ruleSet in m_rules)
            {
                InternalReclassificationRuleSerialization.Serialize(writer, ruleSet);
            }
        }

        /// <summary>
        /// Deserializes
        /// </summary>
        public static ObservationReclassifier Deserialize(BuildXLReader reader)
        {
            Contract.Requires(reader != null);
            var count = reader.ReadInt32();
            var ruleSets = new IInternalReclassificationRule[count];
            for (var i = 0; i < count; i++)
            {
                ruleSets[i] = InternalReclassificationRuleSerialization.Deserialize(reader);
            }

            return new ObservationReclassifier(ruleSets);
        }

        /// <summary>
        /// We generate a hash for a set of defined rules. We take into account if they change order in the definition (because priority changes in that case)
        /// If any of this changes, we need to rerun all the pips, because new rules might apply for them,
        /// so this needs to act as a fingerprint salt.
        /// </summary>
        public static ContentHash? ComputeObservationReclassificationRulesHash(IConfiguration configuration)
        {
            return ComputeObservationReclassificationRulesHash(configuration.GlobalReclassificationRules);
        }

        private static ContentHash? ComputeObservationReclassificationRulesHash(IReadOnlyList<IReclassificationRuleConfig> reclassificationRules)
        {
            if (reclassificationRules.Count == 0)
            {
                return null;
            }

            var hash = ContentHashingUtilities.ZeroHash;
            int i = 0;
            foreach (var ruleConfig in reclassificationRules)
            {
                var rule = ruleConfig.GetRule();
                var ruleDescriptorWithOrder = ContentHashingUtilities.HashString($"{i++}|{rule.Descriptor()}");
                hash = ContentHashingUtilities.CombineOrderIndependent(hash, ruleDescriptorWithOrder);
            }

            return hash;
        }
    }
}
