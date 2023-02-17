// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
        public static GlobalProperties CreateQualifierAsGlobalProperties(QualifierId qualifierId, FrontEndContext context)
        {
            var stringTable = context.StringTable;
            var qualifier = context.QualifierTable.GetQualifier(qualifierId);
            return new GlobalProperties(
                qualifier.Keys.ToDictionary(
                    key => key.ToString(stringTable),
                    key =>
                    {
                        qualifier.TryGetValue(stringTable, key, out StringId value);
                        return value.ToString(stringTable);
                    }));
        }
    }
}
