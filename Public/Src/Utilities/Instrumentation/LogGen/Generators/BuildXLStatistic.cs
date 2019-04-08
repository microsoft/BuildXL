// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using Microsoft.CodeAnalysis;

namespace BuildXL.LogGen.Generators
{
    /// <summary>
    /// Logs all numeric members of the payload as BuildXL StatisticWithoutTelemetry events
    /// </summary>
    internal sealed class BuildXLStatistic : GeneratorBase
    {
        /// <inheritdoc/>
        public override void GenerateLogMethodBody(LoggingSite site, Func<string> getMessageExpression)
        {
            foreach (var item in site.FlattenedPayload)
            {
                if (IsCompatibleNumeric(item.Type))
                {
                    m_codeGenerator.Ln("BuildXL.Tracing.ETWLogger.Log.Statistic({0}.Session.RelatedActivityId, \"{1}_{2}\", (long){3});", site.LoggingContextParameterName, site.Method.Name, item.AddressForTelemetryString, item.Address);
                }
                else if (item.Type.SpecialType == SpecialType.System_UInt64)
                {
                    m_errorReport.ReportError(site.Method, "Numeric payload member {0} not being logged as a statistic because it may overflow when cast to a long.", item.Address);
                }
                else
                {
                    IPropertySymbol key;
                    IPropertySymbol value;
                    if (AriaV2.TryGetEnumerableKeyValuePair(item, out key, out value) &&
                        key.Type.SpecialType == SpecialType.System_String &&
                        IsCompatibleNumeric(value.Type))
                    {
                        m_codeGenerator.Ln("foreach (var item in {0})", item.Address);
                        using (m_codeGenerator.Br)
                        {
                            // This directly calls the ETWLogger to make sure a Statistic event is logged to ensure all
                            // statistics, whether they are logged with the bulk call or not, get to the same ETW event.
                            m_codeGenerator.Ln("BuildXL.Tracing.ETWLogger.Log.Statistic({0}.Session.RelatedActivityId, \"{1}\" + item.Key, (long)item.Value);", site.LoggingContextParameterName, ComputeKeyPrefix(site));
                        }
                    }
                }
            }
        }

        public static string ComputeKeyPrefix(LoggingSite site)
        {
            if (!site.Method.Name.Equals("BulkStatistic", StringComparison.OrdinalIgnoreCase))
            {
                return site.Method.Name + "_";
            }

            return string.Empty;
        }
    }
}
