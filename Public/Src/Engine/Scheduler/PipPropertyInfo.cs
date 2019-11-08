using System.Collections.Concurrent;
using System.Collections.Generic;
using Logger = BuildXL.Scheduler.Tracing.Logger;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Scheduler
{
    internal class PipPropertyInfo
    {
        /// <summary>
        /// The max count of PipIds for telemetry fields recording a list if impact pips
        /// </summary>
        private const int MaxListOfPipIdsForTelemetry = 50; 

        private ConcurrentDictionary<string, long> m_aggregatedPipPropertiesCount;
        private ConcurrentDictionary<string, HashSet<string>> m_addgregatepipsPerPipProperty;

        internal PipPropertyInfo()
        {
            m_aggregatedPipPropertiesCount = new ConcurrentDictionary<string, long>();
            m_addgregatepipsPerPipProperty = new ConcurrentDictionary<string, HashSet<string>>();
        }

        internal void LogPipPropertyInfo(LoggingContext loggingContext)
        {
            if (m_aggregatedPipPropertiesCount.Count > 0)
            {
                // Log out one message with containing a string of pip properties with their impacted pips
                string pipPropertiesPips = string.Empty;
                foreach (var item in m_aggregatedPipPropertiesCount)
                {
                    var impactedPips = m_addgregatepipsPerPipProperty[item.Key];
                    if (impactedPips.Count > 0)
                    {
                        pipPropertiesPips = pipPropertiesPips + item.Key + "Pips: " + string.Join(",", m_addgregatepipsPerPipProperty[item.Key]) + "; ";
                    }
                }
                Logger.Log.ProcessPattern(loggingContext, pipPropertiesPips, m_aggregatedPipPropertiesCount);
            }
        }

        internal void UpdatePipPropertyInfo(ProcessRunnablePip processRunnable, ExecutionResult executionResult)
        {
            if (executionResult.PipProperties != null && executionResult.PipProperties.Count > 0)
            {
                foreach (var kvp in executionResult.PipProperties)
                {
                    m_aggregatedPipPropertiesCount.AddOrUpdate(kvp.Key, kvp.Value, (key, oldValue) => oldValue + kvp.Value);

                    var impactedPips = m_addgregatepipsPerPipProperty.GetOrAdd(kvp.Key, new HashSet<string>());
                    lock (m_addgregatepipsPerPipProperty)
                    {
                        if (impactedPips.Count < MaxListOfPipIdsForTelemetry)
                        {
                            impactedPips.Add(processRunnable.Process.FormattedSemiStableHash);
                        }
                    }
                }
            }
        }
    }
}