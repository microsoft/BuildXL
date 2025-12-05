// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Core;

#nullable enable

namespace BuildXL.Pips.Reclassification
{
    /// <summary>
    /// Represents a DScript-configured reclassification rule.
    /// </summary>
    /// <remarks>
    /// This class is essentially a decorator over <see cref="IReclassificationRule"/> that compiles the regex and handles the classification logic
    /// </remarks>
    public class DScriptInternalReclassificationRule : IInternalReclassificationRule
    {
        private readonly int m_ruleIndex;
        private readonly IReclassificationRule m_reclassificationRule;
        private readonly SerializableRegex m_pathRegex;
        private readonly HashSet<ObservationType>? m_fromTypes; // null means all types
        private readonly ObservationType? m_toType;
        private readonly bool m_ignore;
        private readonly string m_ruleName;

        /// <nodoc/>
        public DScriptInternalReclassificationRule(int ruleIndex, IReclassificationRule reclassificationRule)
        {
            m_ruleIndex = ruleIndex;
            m_reclassificationRule = reclassificationRule;
            var baseFlag = OperatingSystemHelper.IsPathComparisonCaseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
#if NET7_0_OR_GREATER
            baseFlag |= RegexOptions.NonBacktracking;
#endif
            m_pathRegex = new SerializableRegex(m_reclassificationRule.PathRegex, baseFlag | RegexOptions.Compiled | RegexOptions.CultureInvariant);

            m_fromTypes = null;
            // If the rule contains 'All' as a mapped type, we will match every observation
            if (!m_reclassificationRule.ResolvedObservationTypes.Contains(ObservationType.All))
            {
                m_fromTypes = new HashSet<ObservationType>(m_reclassificationRule.ResolvedObservationTypes);
            }

            m_ignore = false;
            m_toType = null;
            if (m_reclassificationRule.ReclassifyTo != null)
            {
                if (m_reclassificationRule.ReclassifyTo.GetValue() is ObservationType t)
                {
                    m_toType = t;
                }
                else
                {
                    // The DiscriminatingUnion has type unit, it means ignore the observation
                    m_ignore = true;
                }
            }

            m_ruleName = m_reclassificationRule.Name ?? $"<untitled rule, index {ruleIndex}>";
        }

        /// <inheritdoc/>
        public string Name() => m_ruleName;

        /// <inheritdoc/>
        public string Descriptor() => m_reclassificationRule.Descriptor();

        /// <nodoc/>
        public void Serialize(BuildXLWriter writer)
        {
            writer.WriteCompact(m_ruleIndex);
            m_reclassificationRule.Serialize(writer);
        }

        /// <nodoc/>
        public static IInternalReclassificationRule Deserialize(BuildXLReader reader)
        {
            int ruleIndex = reader.ReadInt32Compact();
            var rule = ReclassificationRule.Deserialize(reader);

            return new DScriptInternalReclassificationRule(ruleIndex, rule);
        }

        /// <inheritdoc/>
        public bool TryReclassify(ExpandedAbsolutePath path, PathTable pathTable, ObservationType type, out ReclassificationResult reclassification)
        {
            // if From is null we match against any type
            if (m_fromTypes == null || m_fromTypes.Contains(type))
            {
                if (m_pathRegex.Regex.IsMatch(path.ExpandedPath))
                {
                    // If the rule says 'ignore', return null. Else, the rule specifies a type or 'null' means 'don't reclassify'.
                    ObservationType? reclassifyTo = m_ignore ? null : m_toType ?? type;
                    reclassification = new(m_ruleName, reclassifyTo, path.Path);

                    return true;
                }
            }

            reclassification = default;
            return false;
        }

        /// <inheritdoc/>
        public bool Validate(out string error)
        {
            if (m_reclassificationRule.ReclassifyTo.GetValue() is ObservationType t)
            {
                if (t == ObservationType.All)
                {
                    error = "'All' is not a valid target for a reclassification";
                    return false;
                }
                else if (t == ObservationType.FileContentRead && m_reclassificationRule.ResolvedObservationTypes.Any(s => s != ObservationType.FileContentRead && s != ObservationType.ExistingFileProbe))
                {
                    error = $"'{ObservationType.FileContentRead}' can only be reclassified from '{ObservationType.FileContentRead}' or '{ObservationType.ExistingFileProbe}'";
                    return false;
                }
                else if (t == ObservationType.DirectoryEnumeration && m_reclassificationRule.ResolvedObservationTypes.Any(s => s != ObservationType.DirectoryEnumeration && s != ObservationType.ExistingDirectoryProbe))
                {
                    error = $"'{ObservationType.DirectoryEnumeration}' can only be reclassified from '{ObservationType.DirectoryEnumeration}' or '{ObservationType.ExistingDirectoryProbe}'";
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }

        /// <inheritdoc/>
        public IDictionary<string, object> GetDisplayDescription(PathTable pathTable)
        {
            return new Dictionary<string, object>
            {
                ["Type"] = nameof(DScriptInternalReclassificationRule),
                ["Name"] = m_ruleName,
                ["Path Regex"] = m_reclassificationRule.PathRegex,
                ["Resolved Input Types"] = m_reclassificationRule.ResolvedObservationTypes.OrderBy(r => (int)r).Select(s => s.ToString()).ToList(),
                ["Reclassify To"] = GetReclassifyValue(m_reclassificationRule.ReclassifyTo)
            };
        }

        private static string GetReclassifyValue(DiscriminatingUnion<ObservationType, UnitValue> reclassifyTo)
        {
            if (reclassifyTo == null)
            {
                return "<NO RECLASSIFICATION>";
            }

            if (reclassifyTo.GetValue() is ObservationType t)
            {
                return t.ToString();
            }

            // UnitValue means ignore
            return "<IGNORE>";
        }
    }
}
