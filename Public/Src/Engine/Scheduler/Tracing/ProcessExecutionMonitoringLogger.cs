// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Scheduler.Tracing
{
    /// <summary>
    /// Logs reported processes and file accesses for a pip to the execution log if present. Otherwise,
    /// reverts to logging to standard log.
    /// </summary>
    public class ProcessExecutionMonitoringLogger : SandboxedProcessLogger
    {
        private readonly IExecutionLogTarget m_executionLog;
        private ProcessExecutionMonitoringReportedEventData m_eventData;

        /// <summary>
        /// Class constructor
        /// </summary>
        public ProcessExecutionMonitoringLogger(
            LoggingContext loggingContext,
            Process pip,
            PipExecutionContext context,
            IExecutionLogTarget executionLog)
            : base(loggingContext, pip, context)
        {
            m_executionLog = executionLog;
            m_eventData.PipId = pip.PipId;
        }

        /// <inheritdoc />
        public override void LogProcessObservation(
            IReadOnlyCollection<ReportedProcess> processes,
            IReadOnlyCollection<ReportedFileAccess> fileAccesses,
            IReadOnlyList<ProcessDetouringStatusData> detouringStatuses)
        {
            if (m_executionLog != null && m_executionLog.CanHandleEvent(ExecutionEventId.ProcessExecutionMonitoringReported, Distribution.DistributionConstants.LocalWorkerId, 0, 0))
            {
                if (processes != null || fileAccesses != null || detouringStatuses != null)
                {
                    m_eventData.ReportedProcesses = processes;
                    m_eventData.ReportedFileAccesses = fileAccesses;
                    m_eventData.ProcessDetouringStatuses = detouringStatuses;
                    m_executionLog.ProcessExecutionMonitoringReported(m_eventData);
                }
            }
            else
            {
                // Log to standard log
                base.LogProcessObservation(processes, fileAccesses, detouringStatuses);
            }
        }
    }
}
