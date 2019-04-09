// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;

namespace BuildXL.Processes
{
    /// <summary>
    /// Result of the execution of a sand-boxed process
    /// </summary>
    public sealed class SandboxedProcessResult
    {
        /// <summary>
        /// Gets the value that the associated process specified when it terminated.
        /// </summary>
        public int ExitCode { get; internal set; }

        /// <summary>
        /// Whether an attempt was made to kill the process (or any nested child process); if this is set, you might want to ignore the <code>ExitCode</code>.
        /// </summary>
        public bool Killed { get; internal set; }

        /// <summary>
        /// Whether the time limit was exceeded; if this is set, you might want to ignore the <code>ExitCode</code>.
        /// </summary>
        /// <remarks>
        /// If true, implies <code>Killed</code>.
        /// </remarks>
        public bool TimedOut { get; internal set; }

        /// <summary>
        /// Whether there are failures in the detouring code.
        /// </summary>
        public bool HasDetoursInjectionFailures { get; internal set; }

        /// <summary>
        /// Optional set (can be null). Paths to a surviving child process; <code>null</code> if there were non, otherwise (a subset of) all remaining
        /// processes; some elements can be null if the process could not be determined
        /// </summary>
        /// <remarks>
        /// If non-empty, implies <code>Killed</code>.
        /// </remarks>
        public IEnumerable<ReportedProcess> SurvivingChildProcesses { get; internal set; }

        /// <summary>
        /// Gets the timings of the primary process (the one started directly). This does not account for any child processes.
        /// </summary>
        public ProcessTimes PrimaryProcessTimes { get; internal set; }

        /// <summary>
        /// If available, gets the accounting information for the job representing the entire process tree that was executed (i.e., including child processes).
        /// </summary>
        public JobObject.AccountingInformation? JobAccountingInformation { get; internal set; }

        /// <summary>
        /// Redirected standard output.
        /// </summary>
        public SandboxedProcessOutput StandardOutput { get; internal set; }

        /// <summary>
        /// Redirected standard error.
        /// </summary>
        public SandboxedProcessOutput StandardError { get; internal set; }

        /// <summary>
        /// Optional set of all file and scope accesses, only non-null when file access monitoring was requested and ReportFileAccesses was specified in manifest
        /// </summary>
        public ISet<ReportedFileAccess> FileAccesses { get; internal set; }

        /// <summary>
        /// Optional set of all file accesses that were reported due to <see cref="FileAccessPolicy.ReportAccess"/> being set, only non-null when file access monitoring was requested
        /// </summary>
        public ISet<ReportedFileAccess> ExplicitlyReportedFileAccesses { get; internal set; }

        /// <summary>
        /// Optional set of all file access violations, only non-null when file access monitoring was requested and ReportUnexpectedFileAccesses was specified in manifest
        /// </summary>
        public ISet<ReportedFileAccess> AllUnexpectedFileAccesses { get; internal set; }

        /// <summary>
        /// Optional list of all launched processes, including nested processes, only non-null when file access monitoring was requested
        /// </summary>
        public IReadOnlyList<ReportedProcess> Processes { get; internal set; }

        /// <summary>
        /// Optional list of all Detouring Status messages received.
        /// </summary>
        public IReadOnlyList<ProcessDetouringStatusData> DetouringStatuses { get; internal set; }

        /// <summary>
        /// Path of the memory dump created if a process times out. This may be null if the process did not time out
        /// or if capturing the dump failed. By default, this will be placed in the process's working directory.
        /// </summary>
        public string DumpFileDirectory { get; internal set; }

        /// <summary>
        /// Exception describing why creating a memory dump may have failed.
        /// </summary>
        public Exception DumpCreationException { get; internal set; }

        /// <summary>
        /// Exception describing why writing standard input may have failed.
        /// </summary>
        public Exception StandardInputException { get; internal set; }

        /// <summary>
        /// Number of retries to execute this pip.
        /// </summary>
        public int NumberOfProcessLaunchRetries { get; internal set; }

        /// <summary>
        /// Whether there were ReadWrite access requests changed to Read access requests.
        /// </summary>
        public bool HasReadWriteToReadFileAccessRequest { get; internal set; }

        /// <summary>
        /// Indicates if there was a failure in parsing of the message coming throught the async pipe.
        /// This could happen if the child process is killed while writing a message in the pipe.
        /// If null there is no error, otherwise the Faiulure object contains string, describing the error.
        /// </summary>
        public Utilities.Failure<string> MessageProcessingFailure { get; internal set; }

        /// <summary>
        /// Time (in ms.) spent for startiing the process.
        /// </summary>
        public long ProcessStartTime { get; internal set; }
    }
}
