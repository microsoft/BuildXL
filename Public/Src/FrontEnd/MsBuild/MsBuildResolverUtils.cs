// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using BuildXL.FrontEnd.MsBuild.Serialization;
using BuildXL.FrontEnd.Sdk;
using BuildXL.Utilities.Core;

namespace BuildXL.FrontEnd.MsBuild
{
    /// <nodoc/>
    public class MsBuildResolverUtils
    {
        /// <summary>
        /// Encodes a qualifier as MSBuild global properties
        /// </summary>
        public static GlobalProperties CreateQualifierAsGlobalProperties(QualifierId qualifierId, FrontEndContext context, bool injectGraphBuildProperty = false)
        {
            var stringTable = context.StringTable;
            var qualifier = context.QualifierTable.GetQualifier(qualifierId);
            var dictionary = qualifier.Keys.ToDictionary(
                    key => key.ToString(stringTable),
                    key =>
                    {
                        qualifier.TryGetValue(stringTable, key, out StringId value);
                        return value.ToString(stringTable);
                    });

            if (injectGraphBuildProperty)
            {
                dictionary[PipConstructor.s_isGraphBuildProperty] = "true";
            }

            return new GlobalProperties(dictionary);
        }

        /// <summary>
        /// Creates a qualifier ID from the given global properties.
        /// </summary>
        public static QualifierId CreateQualifierIdFromGlobalProperties(GlobalProperties globalProperties, FrontEndContext context, bool removeGraphBuildProperty = false)
        {
            var qualifierTable = context.QualifierTable;
            return qualifierTable.CreateQualifier(
                globalProperties
                    .Where(kv => !removeGraphBuildProperty || kv.Key != PipConstructor.s_isGraphBuildProperty)
                    .Select(kvp => new Tuple<string, string>(kvp.Key, kvp.Value))
                    .ToArray());
        }
    }
}
