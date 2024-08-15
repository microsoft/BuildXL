// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Processes;
using BuildXL.Storage;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Core;

namespace BuildXL.Scheduler.Fingerprints
{
    /// <nodoc />
    public readonly record struct ReclassificationResult(string AppliedRuleName, ObservedInputType? ReclassifyTo);

    /// <inheritdoc />
    public class ObservationReclassifier
    {
        /// <summary>
        /// Encapsulates the mappings for a given 'rule set' in the configuration
        /// </summary>
        internal class ReclassificationRuleInternal
        {
            /// <nodoc />
            public string Name { get; }

            /// <summary>
            /// The underlying Regex
            /// </summary>
            public SerializableRegex PathRegex { get; }

            /// <nodoc />
            public HashSet<ObservedInputType> From { get; }

            /// <nodoc />
            public ObservedInputType? To { get; }

            public bool Ignore { get; }

            /// <nodoc />
            public static ReclassificationRuleInternal CreateFromConfig(IReclassificationRule rule)
            {
                var baseFlag = OperatingSystemHelper.IsPathComparisonCaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
#if NET7_0_OR_GREATER
                baseFlag |= RegexOptions.NonBacktracking;
#endif
                var regex = new SerializableRegex(rule.PathRegex, baseFlag | RegexOptions.Compiled | RegexOptions.CultureInvariant);

                HashSet<ObservedInputType> from = null;
                // If the rule contains 'All' as a mapped type, we will match every observation
                if (!rule.ResolvedObservationTypes.Contains(ObservationType.All))
                {
                    from = rule.ResolvedObservationTypes.Select(cast).ToHashSet();
                }

                // If ReclassifyTo is unit
                bool ignore = false;
                ObservedInputType? to = null;
                if (rule.ReclassifyTo != null)
                {
                    if (rule.ReclassifyTo.GetValue() is ObservationType t)
                    {
                        to = cast(t);
                    }
                    else
                    {
                        // The DiscriminatingUnion has type unit, it means ignore the observation
                        ignore = true;
                    }
                }

                return new ReclassificationRuleInternal(rule.Name, regex, from, ignore, to);

                // We need to cast from the 'configuration' enums to the 'fingerprinting' enums
                ObservedInputType cast(ObservationType from) => from switch
                {
                    ObservationType.AbsentPathProbe => ObservedInputType.AbsentPathProbe,
                    ObservationType.FileContentRead => ObservedInputType.FileContentRead,
                    ObservationType.DirectoryEnumeration => ObservedInputType.DirectoryEnumeration,
                    ObservationType.ExistingDirectoryProbe => ObservedInputType.ExistingDirectoryProbe,
                    ObservationType.ExistingFileProbe => ObservedInputType.ExistingFileProbe,
                    // Should never happen as these values come from a union type in DScript
                    _ => throw new ArgumentException($"Unknown ObservationType {from}")
                };
            }

            private ReclassificationRuleInternal(string name, SerializableRegex pathRegex, HashSet<ObservedInputType> from, bool ignore, ObservedInputType? to)
            {
                Name = name;
                PathRegex = pathRegex;
                From = from;
                Ignore = ignore;
                To = to;
            }

            /// <nodoc />
            public void Serialize(BuildXLWriter writer)
            {
                Contract.Requires(writer != null);
                PathRegex.Write(writer);

                writer.WriteNullableString(Name);
                
                writer.Write(From != null);
                if (From != null)
                {
                    writer.Write(From.ToReadOnlyArray(), (w, v) => w.Write((int)v));
                }

                writer.Write(Ignore);

                if (!Ignore)
                {
                    writer.Write(To.HasValue);
                    if (To.HasValue)
                    {
                        writer.Write((int)To.Value);
                    }
                }
            }

            /// <nodoc />
            public static ReclassificationRuleInternal Deserialize(BuildXLReader reader)
            {
                var regex = SerializableRegex.Read(reader);

                string name = reader.ReadNullableString();
                
                HashSet<ObservedInputType> from = null;
                if (reader.ReadBoolean())
                {
                    from = new HashSet<ObservedInputType>(reader.ReadReadOnlyArray(r => (ObservedInputType)r.ReadInt32()));
                }

                var ignore = reader.ReadBoolean();

                ObservedInputType? to = null;
                if (!ignore)
                {
                    if (reader.ReadBoolean())
                    {
                        to = (ObservedInputType)reader.ReadInt32();
                    }
                }

                return new ReclassificationRuleInternal(name, regex, from, ignore, to);
            }
        }

        /// <nodoc />
        public ObservationReclassifier() : this(Array.Empty<ReclassificationRuleInternal>()) { }

        private ObservationReclassifier(ReclassificationRuleInternal[] rules)
        {
            m_rules = rules;
        }

        private CounterCollection<PipExecutorCounter> m_counters;
        private ReclassificationRuleInternal[] m_rules;

        /// <summary>
        /// Initializes the rules given a user provided configuration. 
        /// This method throws <see cref="BuildXLException"/> if there is some error while initializing the rules, 
        /// such as forbidden reclassifications.
        /// </summary>
        public void Initialize(IReadOnlyCollection<IReclassificationRule> rules, CounterCollection<PipExecutorCounter> counters = null)
        {
            if (!ValidateRules(rules, out var errors))
            {
                throw new BuildXLException($"Failure inizializing the observation reclassification rules. Errors: {errors}");
            }

            m_counters = counters ?? new();
            m_rules = rules.Select(static r => ReclassificationRuleInternal.CreateFromConfig(r)).ToArray();
        }

        private bool ValidateRules(IReadOnlyCollection<IReclassificationRule> rules, out string errors)
        {
            var errorBuilder = new StringBuilder();
            bool hasErrors = false;
            var ruleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rule in rules)
            {
                // 1. Rule name can't be repeated
                if (!string.IsNullOrEmpty(rule.Name) && ruleNames.Contains(rule.Name))
                {
                    errorBuilder.AppendLine($"Duplicate rule name found: '{rule.Name}'");
                    hasErrors = true;
                }

                ruleNames.Add(rule.Name);

                // 2. Error on illegal transitions
                if (rule.ReclassifyTo.GetValue() is ObservationType t)
                {
                    if (t == ObservationType.All)
                    {
                        errorBuilder.AppendLine("'All' is not a valid target for a reclassification");
                        hasErrors = true;
                    }
                    else if (t == ObservationType.FileContentRead && rule.ResolvedObservationTypes.Any(s => s != ObservationType.FileContentRead || s != ObservationType.ExistingFileProbe))
                    {
                        errorBuilder.AppendLine($"'{ObservationType.FileContentRead}' can only be reclassified from '{ObservationType.FileContentRead} or '{ObservationType.ExistingFileProbe}'");
                        hasErrors = true;
                    }
                    else if (t == ObservationType.DirectoryEnumeration && rule.ResolvedObservationTypes.Any(s => s != ObservationType.DirectoryEnumeration || s != ObservationType.ExistingDirectoryProbe))
                    {
                        errorBuilder.AppendLine($"'{ObservationType.DirectoryEnumeration}' can only be reclassified from '{ObservationType.DirectoryEnumeration} or '{ObservationType.ExistingDirectoryProbe}'");
                        hasErrors = true;
                    }
                }
            }

            errors = errorBuilder.ToString();
            return !hasErrors;
        }

        /// <summary>
        /// If true, the given path must be reclassified from its observed type to the one returned in the out parameter.
        /// The out parameter is a <see cref="ReclassificationResult"/> which also includes the name of the rule that applied,
        /// for tracing purposes.
        /// </summary>
        public bool TryReclassify(AbsolutePath path, PathTable pathTable, ObservedInputType type, out ReclassificationResult reclassification)
        {
            reclassification = default;
            string pathString = null;

            for (var i = 0; i < m_rules.Length; i++)
            {
                var rule = m_rules[i];
                // if From is null we match against any type
                if (rule.From == null || rule.From.Contains(type))
                {
                    pathString ??= path.ToString(pathTable);
                    m_counters.IncrementCounter(PipExecutorCounter.NumRegexMatchForReclassificationRules);
                    using (m_counters.StartStopwatch(PipExecutorCounter.RegexMatchForReclassificationRulesDuration))
                    {
                        if (rule.PathRegex.Regex.IsMatch(pathString))
                        {
                            var ruleName = rule.Name ?? $"<untitled rule, index {i}>";
                            // If the rule says 'ignore', return null. Else, the rule specifies a type or 'null' means 'don't reclassify'.
                            ObservedInputType? reclassifyTo = rule.Ignore ? null : (rule.To ?? type);
                            reclassification = new(ruleName, reclassifyTo);
                            m_counters.IncrementCounter(PipExecutorCounter.NumReclassifiedObservations);
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Serialize
        /// </summary>
        public void Serialize(BuildXLWriter writer)
        {
            Contract.Requires(writer != null);
            writer.Write(m_rules.Length);
            foreach (var ruleSet in m_rules)
            {
                ruleSet.Serialize(writer);
            }
        }

        /// <summary>
        /// Deserializes
        /// </summary>
        public static ObservationReclassifier Deserialize(BuildXLReader reader)
        {
            Contract.Requires(reader != null);
            var count = reader.ReadInt32();
            var ruleSets = new ReclassificationRuleInternal[count];
            for (var i = 0; i < count; i++)
            {
                ruleSets[i] = ReclassificationRuleInternal.Deserialize(reader);
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
