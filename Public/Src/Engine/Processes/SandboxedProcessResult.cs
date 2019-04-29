// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.Utilities;

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
        public Failure<string> MessageProcessingFailure { get; internal set; }

        /// <summary>
        /// Time (in ms.) spent for startiing the process.
        /// </summary>
        public long ProcessStartTime { get; internal set; }

        /// <summary>
        /// Number of warnings.
        /// </summary>
        public int WarningCount { get; set; }

        /// <summary>
        /// Maximum heap size of the sandboxed process.
        /// </summary>
        public long DetoursMaxHeapSize { get; set; }

        /// <summary>
        /// Differences in sent and received messages from sandboxed process.
        /// </summary>
        public int LastMessageCount { get; set; }

        /// <summary>
        /// Flag indicating if a semaphore is created for Detours message.
        /// </summary>
        public bool MessageCountSemaphoreCreated { get; set; }

        /// <summary>
        /// Serializes this instance to a given <paramref name="stream"/>.
        /// </summary>
        public void Serialize(Stream stream)
        {
            using (var writer = new BuildXLWriter(false, stream, true, true))
            {
                Serialize(writer);
            }
        }

        /// <summary>
        /// Serializes this instance to a given <paramref name="writer"/>.
        /// </summary>
        public void Serialize(BuildXLWriter writer)
        {
            writer.Write(ExitCode);
            writer.Write(Killed);
            writer.Write(TimedOut);
            writer.Write(HasDetoursInjectionFailures);

            var processMap = CreateAndSerializeProcessMap(writer);

            writer.Write(SurvivingChildProcesses, (w, v) => w.WriteReadOnlyList(v.ToList(), (w2, v2) => w2.Write(processMap[v2])));
            writer.Write(PrimaryProcessTimes, (w, v) => v.Serialize(w));
            writer.Write(JobAccountingInformation, (w, v) => v.Serialize(w));
            writer.Write(StandardOutput, (w, v) => v.Serialize(w));
            writer.Write(StandardError, (w, v) => v.Serialize(w));
            writer.Write(FileAccesses, (w, v) => w.WriteReadOnlyList(v.ToList(), (w2, v2) => v2.Serialize(writer, processMap, writePath: null)));
            writer.Write(ExplicitlyReportedFileAccesses, (w, v) => w.WriteReadOnlyList(v.ToList(), (w2, v2) => v2.Serialize(writer, processMap, writePath: null)));
            writer.Write(AllUnexpectedFileAccesses, (w, v) => w.WriteReadOnlyList(v.ToList(), (w2, v2) => v2.Serialize(writer, processMap, writePath: null)));
            writer.Write(Processes, (w, v) => w.WriteReadOnlyList(v, (w2, v2) => w2.Write(processMap[v2])));
            writer.Write(DetouringStatuses, (w, v) => w.WriteReadOnlyList(v, (w2, v2) => v2.Serialize(w2)));
            writer.WriteNullableString(DumpFileDirectory);
            writer.WriteNullableString(DumpCreationException?.Message);
            writer.WriteNullableString(StandardInputException?.Message);
            writer.Write(NumberOfProcessLaunchRetries);
            writer.Write(HasReadWriteToReadFileAccessRequest);
            writer.WriteNullableString(MessageProcessingFailure?.Describe());
            writer.Write(ProcessStartTime);
            writer.Write(WarningCount);
            writer.Write(DetoursMaxHeapSize);
            writer.Write(LastMessageCount);
            writer.Write(MessageCountSemaphoreCreated);
        }

        /// <summary>
        /// Deserializes an instance of <see cref="SandboxedProcessResult"/>.
        /// </summary>
        public static SandboxedProcessResult Deserialize(Stream stream)
        {
            using (var reader = new BuildXLReader(false, stream, true))
            {
                return Deserialize(reader);
            }
        }

        /// <summary>
        /// Deserializes an instance of <see cref="SandboxedProcessResult"/>.
        /// </summary>
        /// <param name="reader"></param>
        /// <returns></returns>
        public static SandboxedProcessResult Deserialize(BuildXLReader reader)
        {
            int exitCode = reader.ReadInt32();
            bool killed = reader.ReadBoolean();
            bool timedOut = reader.ReadBoolean();
            bool hasDetoursInjectionFailures = reader.ReadBoolean();

            IReadOnlyList<ReportedProcess> allReportedProcesses = reader.ReadReadOnlyList(r => ReportedProcess.Deserialize(r));
            IReadOnlyList<ReportedProcess> survivingChildProcesses = reader.ReadNullable(r => r.ReadReadOnlyList(r2 => allReportedProcesses[r2.ReadInt32()]));
            ProcessTimes primaryProcessTimes = reader.ReadNullable(r => ProcessTimes.Deserialize(r));
            JobObject.AccountingInformation? jobAccountingInformation = reader.ReadNullableStruct(r => JobObject.AccountingInformation.Deserialize(r));
            SandboxedProcessOutput standardOutput = reader.ReadNullable(r => SandboxedProcessOutput.Deserialize(r));
            SandboxedProcessOutput standardError = reader.ReadNullable(r => SandboxedProcessOutput.Deserialize(r));
            IReadOnlyList<ReportedFileAccess> fileAccesses = reader.ReadNullable(r => r.ReadReadOnlyList(r2 => ReportedFileAccess.Deserialize(r2, allReportedProcesses, readPath: null)));
            IReadOnlyList<ReportedFileAccess> explicitlyReportedFileAccesses = reader.ReadNullable(r => r.ReadReadOnlyList(r2 => ReportedFileAccess.Deserialize(r2, allReportedProcesses, readPath: null)));
            IReadOnlyList<ReportedFileAccess> allUnexpectedFileAccesses = reader.ReadNullable(r => r.ReadReadOnlyList(r2 => ReportedFileAccess.Deserialize(r2, allReportedProcesses, readPath: null)));
            IReadOnlyList<ReportedProcess> processes = reader.ReadNullable(r => r.ReadReadOnlyList(r2 => allReportedProcesses[r2.ReadInt32()]));
            IReadOnlyList<ProcessDetouringStatusData> detouringStatuses = reader.ReadNullable(r => r.ReadReadOnlyList(r2 => ProcessDetouringStatusData.Deserialize(r2)));
            string dumpFileDirectory = reader.ReadNullableString();
            string dumpCreationExceptionMessage = reader.ReadNullableString();
            string standardInputExceptionMessage = reader.ReadNullableString();
            int numberOfPRocessLaunchRetries = reader.ReadInt32();
            bool hasReadWriteToReadFileAccessRequest = reader.ReadBoolean();
            string messageProcessingFailureMessage = reader.ReadNullableString();
            long processStartTime = reader.ReadInt64();
            int warningCount = reader.ReadInt32();
            long detoursMaxHeapSize = reader.ReadInt64();
            int lastMessageCount = reader.ReadInt32();
            bool messageCountSemaphoreCreated = reader.ReadBoolean();

            return new SandboxedProcessResult()
            {
                ExitCode = exitCode,
                Killed = killed,
                TimedOut = timedOut,
                HasDetoursInjectionFailures = hasDetoursInjectionFailures,
                SurvivingChildProcesses = survivingChildProcesses,
                PrimaryProcessTimes = primaryProcessTimes,
                JobAccountingInformation = jobAccountingInformation,
                StandardOutput = standardOutput,
                StandardError = standardError,
                FileAccesses = fileAccesses != null ? new HashSet<ReportedFileAccess>(fileAccesses) : null,
                ExplicitlyReportedFileAccesses = explicitlyReportedFileAccesses != null ? new HashSet<ReportedFileAccess>(explicitlyReportedFileAccesses) : null,
                AllUnexpectedFileAccesses = allUnexpectedFileAccesses != null ? new HashSet<ReportedFileAccess>(allUnexpectedFileAccesses) : null,
                Processes = processes,
                DetouringStatuses = detouringStatuses,
                DumpFileDirectory = dumpFileDirectory,
                DumpCreationException = dumpCreationExceptionMessage != null ? new Exception(dumpCreationExceptionMessage) : null,
                StandardInputException = standardInputExceptionMessage != null ? new Exception(standardInputExceptionMessage) : null,
                NumberOfProcessLaunchRetries = numberOfPRocessLaunchRetries,
                HasReadWriteToReadFileAccessRequest = hasReadWriteToReadFileAccessRequest,
                MessageProcessingFailure = messageProcessingFailureMessage != null ? new Failure<string>(messageProcessingFailureMessage) : null,
                ProcessStartTime = processStartTime,
                WarningCount = warningCount,
                DetoursMaxHeapSize = detoursMaxHeapSize,
                LastMessageCount = lastMessageCount,
                MessageCountSemaphoreCreated = messageCountSemaphoreCreated
            };
        }

        private Dictionary<ReportedProcess, int> CreateAndSerializeProcessMap(BuildXLWriter writer)
        {
            var processMap = new Dictionary<ReportedProcess, int>();

            PopulateProcesses(Processes);
            PopulateProcesses(SurvivingChildProcesses);
            PopulateProcesses(FileAccesses?.Select(f => f.Process));
            PopulateProcesses(ExplicitlyReportedFileAccesses?.Select(f => f.Process));
            PopulateProcesses(AllUnexpectedFileAccesses?.Select(f => f.Process));

            ReportedProcess[] processes = new ReportedProcess[processMap.Count];
            foreach (var process in processMap)
            {
                processes[process.Value] = process.Key;
            }

            writer.WriteReadOnlyList(processes, (w, v) => v.Serialize(w));

            return processMap;

            void PopulateProcesses(IEnumerable<ReportedProcess> processesToPopulate)
            {
                if (processesToPopulate != null)
                {
                    foreach (var process in processesToPopulate)
                    {
                        if (!processMap.ContainsKey(process))
                        {
                            processMap.Add(process, processMap.Count);
                        }
                    }
                }
            }
        }
    }
}
