// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Utilities.Core;

#nullable enable

namespace BuildXL.Pips.Reclassification
{
    /// <summary>
    /// Handles serialization and deserialization of internal reclassification rules.
    /// </summary>
    public static class InternalReclassificationRuleSerialization
    {
        /// <nodoc/>
        public static IInternalReclassificationRule Deserialize(BuildXLReader reader)
        {
            var ruleType = (ReclassificationRuleType)reader.ReadInt32Compact();
            switch (ruleType)
            {
                case ReclassificationRuleType.DScript:
                    return DScriptInternalReclassificationRule.Deserialize(reader);
                case ReclassificationRuleType.JavaScriptPackageStore:
                    return JavaScriptPackageStoreReclassificationRule.Deserialize(reader);
                default:
                    throw new NotSupportedException($"Deserialization of reclassification rule of type '{ruleType}' is not supported.");
            }
        }

        /// <nodoc/>
        public static void Serialize(BuildXLWriter writer, IInternalReclassificationRule rule)
        {
            switch (rule)
            {
                case DScriptInternalReclassificationRule dScriptRule:
                    writer.WriteCompact((int)ReclassificationRuleType.DScript);
                    dScriptRule.Serialize(writer);
                    break;
                case JavaScriptPackageStoreReclassificationRule packageStoreRule:
                    writer.WriteCompact((int)ReclassificationRuleType.JavaScriptPackageStore);
                    packageStoreRule.Serialize(writer);
                    break;
                default:
                    throw new NotSupportedException($"Serialization of reclassification rule of type '{rule.GetType()}' is not supported.");
            }
        }
    }
}
