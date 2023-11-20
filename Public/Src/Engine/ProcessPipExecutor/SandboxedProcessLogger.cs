// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.ProcessPipExecutor
{
    /// <summary>
    /// Logs reported processes and file accesses for a pip
    /// </summary>
    public class SandboxedProcessLogger : ISandboxedProcessLogger
    {
        /// <summary>
        /// The logging context used for logging
        /// </summary>
        protected readonly LoggingContext LoggingContext;

        /// <summary>
        /// The process run in sandbox
        /// </summary>
        protected readonly Process Process;

        /// <summary>
        /// The execution contexet
        /// </summary>
        protected readonly PipExecutionContext Context;

        /// <summary>
        /// Class constructor
        /// </summary>
        public SandboxedProcessLogger(LoggingContext loggingContext, Process pip, PipExecutionContext context)
        {
            LoggingContext = loggingContext;
            Process = pip;
            Context = context;
        }

        /// <summary>
        /// Logs the reported processes and file accesses
        /// </summary>
        public virtual void LogProcessObservation(
            IReadOnlyCollection<ReportedProcess> processes,
            IReadOnlyCollection<ReportedFileAccess> fileAccesses,
            IReadOnlyList<ProcessDetouringStatusData> detouringStatuses)
        {
            if (fileAccesses != null)
            {
                foreach (ReportedFileAccess reportedFile in fileAccesses)
                {
                    Processes.Tracing.Logger.Log.PipProcessFileAccess(
                        LoggingContext,
                        Process.SemiStableHash,
                        Process.FormattedSemiStableHash,
                        reportedFile.Describe(),
                        reportedFile.GetPath(Context.PathTable));
                }
            }

            if (processes != null)
            {
                foreach (var reportedProcess in processes)
                {
                    Processes.Tracing.Logger.Log.PipProcess(
                        LoggingContext,
                        Process.SemiStableHash,
                        Process.FormattedSemiStableHash,
                        reportedProcess.ProcessId,
                        reportedProcess.Path);
                }
            }
        }
    }
}
