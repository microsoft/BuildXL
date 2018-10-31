// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Processes
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
                    BuildXL.Processes.Tracing.Logger.Log.PipProcessFileAccess(
                        LoggingContext,
                        Process.SemiStableHash,
                        Process.GetDescription(Context),
                        reportedFile.Describe(),
                        reportedFile.GetPath(Context.PathTable));
                }
            }

            if (processes != null)
            {
                foreach (var reportedProcess in processes)
                {
                    BuildXL.Processes.Tracing.Logger.Log.PipProcess(
                        LoggingContext,
                        Process.SemiStableHash,
                        Process.GetDescription(Context),
                        reportedProcess.ProcessId,
                        reportedProcess.Path);
                }
            }
        }
    }
}
