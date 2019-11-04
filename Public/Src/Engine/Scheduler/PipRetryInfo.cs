using System.Collections.Concurrent;
using System.Collections.Generic;
using BuildXL.Pips;
using Logger = BuildXL.Scheduler.Tracing.Logger;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Scheduler
{
    public class PipRetryInfo
    {
        /// <summary>
        /// The max count of PipIds for telemetry fields recording a list if impact pips
        /// </summary>
        private const int MaxListOfPipIdsForTelemetry = 50; 

        private ConcurrentDictionary<string, long> m_aggregatedPipPropertiesCount;
        private ConcurrentDictionary<string, HashSet<string>> m_addgregatepipsPerPipProperty;
        private ConcurrentBigSet<string> m_pipsSucceedingAfterUserRetry;
        private ConcurrentBigSet<string> m_pipsFailingAfterLastUserRetry;

        public PipRetryInfo()
        {
            m_aggregatedPipPropertiesCount = new ConcurrentDictionary<string, long>();
            m_addgregatepipsPerPipProperty = new ConcurrentDictionary<string, HashSet<string>>();
            m_pipsSucceedingAfterUserRetry = new ConcurrentBigSet<string>();
            m_pipsFailingAfterLastUserRetry = new ConcurrentBigSet<string>();
        }

        public void LogPipRetryInfo(LoggingContext loggingContext, CounterCollection<PipExecutorCounter> pipExecutionCounters)
        {
            if (pipExecutionCounters.GetCounterValue(PipExecutorCounter.ProcessUserRetries) > 0)
            {
                string pipsSucceedingAfterUserRetry = string.Join(",", m_pipsSucceedingAfterUserRetry);
                string pipsFailingAfterLastUserRetry = string.Join(",", m_pipsFailingAfterLastUserRetry);
                Logger.Log.ProcessRetries(loggingContext, pipsSucceedingAfterUserRetry, pipsFailingAfterLastUserRetry);
            }

            if (m_aggregatedPipPropertiesCount.Count > 0)
            {
                // Log out one message with containing a string of pip properties with their impacted pips
                int countOfLoggedProperties = 0;
                string pipPropertiesPips = string.Empty;
                foreach (var item in m_aggregatedPipPropertiesCount)
                {
                    pipPropertiesPips = item.Key + "Pips: " + string.Join(",", item.Value);
                    countOfLoggedProperties++;
                    if (countOfLoggedProperties == MaxListOfPipIdsForTelemetry)
                    {
                        break;
                    }
                }
                Logger.Log.ProcessPattern(loggingContext, pipPropertiesPips, m_aggregatedPipPropertiesCount);
            }
        }

        public void UpdatePipRetrInfo(ProcessRunnablePip processRunnable, ExecutionResult executionResult, CounterCollection<PipExecutorCounter> pipExecutionCounters)
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

            if (executionResult.HasUserRetries)
            {
                if (executionResult.Result == PipResultStatus.Succeeded)
                {
                    pipExecutionCounters.IncrementCounter(PipExecutorCounter.ProcessUserRetriesSucceededPipsCount);
                    if (m_pipsSucceedingAfterUserRetry.Count < MaxListOfPipIdsForTelemetry)
                    {
                        m_pipsSucceedingAfterUserRetry.Add(processRunnable.Process.FormattedSemiStableHash);
                    }
                }
                else if (executionResult.Result == PipResultStatus.Failed)
                {
                    pipExecutionCounters.IncrementCounter(PipExecutorCounter.ProcessUserRetriesFailedPipsCount);
                    if (m_pipsFailingAfterLastUserRetry.Count < MaxListOfPipIdsForTelemetry)
                    {
                        m_pipsFailingAfterLastUserRetry.Add(processRunnable.Process.FormattedSemiStableHash);
                    }
                }
            }
        }
    }
}