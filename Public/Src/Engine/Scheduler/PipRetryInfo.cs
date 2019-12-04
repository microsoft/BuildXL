using BuildXL.Pips;
using Logger = BuildXL.Scheduler.Tracing.Logger;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Scheduler
{
    internal class PipRetryInfo
    {
        /// <summary>
        /// The max count of PipIds for telemetry fields recording a list if impact pips
        /// </summary>
        private const int MaxListOfPipIdsForTelemetry = 50; 

        private ConcurrentBigSet<string> m_pipsSucceedingAfterUserRetry;
        private ConcurrentBigSet<string> m_pipsFailingAfterLastUserRetry;

        internal PipRetryInfo()
        {
            m_pipsSucceedingAfterUserRetry = new ConcurrentBigSet<string>();
            m_pipsFailingAfterLastUserRetry = new ConcurrentBigSet<string>();
        }

        internal void LogPipRetryInfo(LoggingContext loggingContext, CounterCollection<PipExecutorCounter> pipExecutionCounters)
        {
            if (pipExecutionCounters.GetCounterValue(PipExecutorCounter.ProcessUserRetries) > 0)
            {
                string pipsSucceedingAfterUserRetry = string.Join(",", m_pipsSucceedingAfterUserRetry.UnsafeGetList());
                string pipsFailingAfterLastUserRetry = string.Join(",", m_pipsFailingAfterLastUserRetry.UnsafeGetList());
                Logger.Log.ProcessRetries(loggingContext, pipsSucceedingAfterUserRetry, pipsFailingAfterLastUserRetry);
            }
        }

        internal void UpdatePipRetryInfo(ProcessRunnablePip processRunnable, ExecutionResult executionResult, CounterCollection<PipExecutorCounter> pipExecutionCounters)
        {
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