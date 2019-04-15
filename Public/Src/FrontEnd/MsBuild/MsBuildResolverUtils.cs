// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Linq;
using BuildXL.FrontEnd.MsBuild.Serialization;
using BuildXL.FrontEnd.Sdk;
using BuildXL.Utilities;

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
