// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.Linq;
using System.Threading;
using BuildXL.Interop.MacOS;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using JetBrains.Annotations;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Processes
{
    /// <summary>
    /// Parser and aggregator for incoming file access reports
    /// </summary>
    /// <remarks>
    /// Instance members of this class are not thread-safe.
    /// </remarks>
    internal sealed class SandboxedProcessReports
    {
        private static readonly Dictionary<string, ReportType> s_reportTypes = Enum
            .GetValues(typeof(ReportType))
            .Cast<ReportType>()
            .ToDictionary(reportType => ((int)reportType).ToString(), reportType => reportType);

        private readonly PathTable m_pathTable;
        private readonly ConcurrentDictionary<uint, ReportedProcess> m_activeProcesses = new ConcurrentDictionary<uint, ReportedProcess>();
        private readonly ConcurrentDictionary<uint, ReportedProcess> m_processesExits = new ConcurrentDictionary<uint, ReportedProcess>();

        private readonly Dictionary<string, string> m_pathCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<AbsolutePath, bool> m_overrideAllowedWritePaths = new Dictionary<AbsolutePath, bool>();

        [CanBeNull]
        private readonly IDetoursEventListener m_detoursEventListener;

        [CanBeNull]
        private readonly SidebandWriter m_sharedOpaqueOutputLogger;

        public readonly List<ReportedProcess> Processes = new List<ReportedProcess>();
        public readonly HashSet<ReportedFileAccess> FileUnexpectedAccesses;
        public readonly HashSet<ReportedFileAccess> FileAccesses;
        public readonly HashSet<ReportedFileAccess> ExplicitlyReportedFileAccesses = new HashSet<ReportedFileAccess>();

        public readonly List<ProcessDetouringStatusData> ProcessDetoursStatuses = new List<ProcessDetouringStatusData>();

        /// <summary>
        /// The last message count in the semaphore.
        /// </summary>
        public int GetLastMessageCount()
        {
            return m_manifest.MessageCountSemaphore?.Release() ?? 0;
        }

        private bool m_isFrozen;

        /// <summary>
        /// Gets whether the report is frozen for modification
        /// </summary>
        private bool IsFrozen => Volatile.Read(ref m_isFrozen);

        /// <summary>
        /// Keeps track of whether there were any ReadWrite file access attempts converted to Read file access.
        /// </summary>
        public bool HasReadWriteToReadFileAccessRequest { get; internal set; }

        /// <summary>
        /// Accessor to the PipSemiStableHash for logging.
        /// </summary>
        public long PipSemiStableHash { get; }

        /// <summary>
        /// Accessor to the PipDescription for logging.
        /// </summary>
        public string PipDescription { get; }

        private readonly FileAccessManifest m_manifest;

        private readonly LoggingContext m_loggingContext;

        /// <summary>
        /// The max Detours heap size for processes of this pip.
        /// </summary>
        public long MaxDetoursHeapSize { get; private set; }

        /// <summary>
        /// Indicates if there was a failure in parsing of the message coming throught the async pipe.
        /// This could happen if the child process is killed while writing a message in the pipe.
        /// If null there is no error, otherwise the Failure object contains string, describing the error.
        /// </summary>
        public Failure<string> MessageProcessingFailure { get; internal set; }

        public SandboxedProcessReports(
            FileAccessManifest manifest,
            PathTable pathTable,
            long pipSemiStableHash,
            string pipDescription,
            LoggingContext loggingContext,
            [CanBeNull] IDetoursEventListener detoursEventListener,
            [CanBeNull] SidebandWriter sharedOpaqueOutputLogger)
        {
            Contract.Requires(manifest != null);
            Contract.Requires(pathTable != null);
            Contract.Requires(pipDescription != null);

            PipSemiStableHash = pipSemiStableHash;
            PipDescription = pipDescription;
            m_pathTable = pathTable;
            FileAccesses = manifest.ReportFileAccesses ? new HashSet<ReportedFileAccess>() : null;
            FileUnexpectedAccesses = new HashSet<ReportedFileAccess>();
            m_manifest = manifest;
            m_detoursEventListener = detoursEventListener;
            m_sharedOpaqueOutputLogger = sharedOpaqueOutputLogger;

            // For tests we need the StaticContext
            m_loggingContext = loggingContext ?? BuildXL.Utilities.Tracing.Events.StaticContext;
        }

        /// <summary>
        /// Freezes the report disallowing further modification
        /// </summary>
        internal void Freeze()
        {
            Volatile.Write(ref m_isFrozen, true);
        }

        /// <summary>
        /// Returns a list of still active child processes for which we only received a ProcessCreate but no
        /// ProcessExit event.
        /// </summary>
        internal IReadOnlyList<ReportedProcess> GetActiveProcesses()
        {
            var matches = new HashSet<uint>(m_processesExits.Select(entry => entry.Key));
            return m_activeProcesses.Where(entry => !matches.Contains(entry.Key)).Select(entry => entry.Value).ToList();
        }

        /// <summary>
        /// Delegate for parsing generic access reports.
        /// </summary>
        public delegate bool FileAccessReportProvider<T>(
            ref T data,
            out uint processId,
            out ReportedFileOperation operation,
            out RequestedAccess requestedAccess,
            out FileAccessStatus status,
            out bool explicitlyReported,
            out uint error,
            out Usn usn,
            out DesiredAccess desiredAccess,
            out ShareMode shareMode,
            out CreationDisposition creationDisposition,
            out FlagsAndAttributes flagsAndAttributes,
            out AbsolutePath manifestPath,
            out string path,
            out string enumeratePattern,
            out string processArgs,
            out string errorMessage);

        /// <summary>
        /// An alternative to <see cref="ReportLineReceived(string)"/> for reporting file accesses
        /// </summary>
        public bool ReportFileAccess<T>(ref T accessReport, FileAccessReportProvider<T> parser)
        {
            var result = FileAccessReportLineReceived(ref accessReport, parser, out var errorMessage);
            if (!result)
            {
                MessageProcessingFailure = CreateMessageProcessingFailure(errorMessage);
            }

            return result;
        }

        /// <summary>
        /// Callback invoked when a new report item is received from the native monitoring code
        /// <returns>true if the processing should continue. Otherwise false, which should cause exiting of the processing of data.</returns>
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly")]
        public bool ReportLineReceived(string data)
        {
            if (data == null)
            {
                // EOF
                return true;
            }

            if (m_manifest.MessageCountSemaphore != null)
            {
                try
                {
                    m_manifest.MessageCountSemaphore.WaitOne(0);
                }
                catch (Exception ex)
                {
                    MessageProcessingFailure = CreateMessageProcessingFailure(data, I($"Wait error on semaphore for counting Detours messages: {ex.GetLogEventMessage()}."));
                    return false;
                }
            }

            int splitIndex = data.IndexOf(',');

            if (splitIndex <= 0)
            {
                MessageProcessingFailure = CreateMessageProcessingFailure(data, "Unexpected message content. Comma expected.");
                return false;
            }

            string reportTypeString = data.Substring(0, splitIndex);

            ReportType reportType;
            bool success = s_reportTypes.TryGetValue(reportTypeString, out reportType);
            if (!success)
            {
                MessageProcessingFailure = CreateMessageProcessingFailure(data, "Unexpected message content. Failed parsing the reportType.");
                return false;
            }

            if (reportType <= ReportType.None || reportType >= ReportType.Max)
            {
                MessageProcessingFailure = CreateMessageProcessingFailure(data, "Unexpected message content. ReportType out to range.");
                return false;
            }

            data = data.Substring(splitIndex + 1);
            if (data.Length <= 0)
            {
                MessageProcessingFailure = CreateMessageProcessingFailure(data, "Unexpected message content. Data length must be bigger than 0.");
                return false;
            }

            string errorMessage = string.Empty;

            switch (reportType)
            {
                case ReportType.FileAccess:
                    if (!FileAccessReportLineReceived(ref data, FileAccessReportLine.TryParse, out errorMessage))
                    {
                        MessageProcessingFailure = CreateMessageProcessingFailure(data, errorMessage);
                        return false;
                    }
                    break;
                case ReportType.DebugMessage:
                    if (m_detoursEventListener != null && (m_detoursEventListener.GetMessageHandlingFlags() & MessageHandlingFlags.DebugMessageNotify) != 0)
                    {
                        m_detoursEventListener.HandleDebugMessage(PipSemiStableHash, PipDescription, data);
                    }

                    Tracing.Logger.Log.LogDetoursDebugMessage(m_loggingContext, PipSemiStableHash, PipDescription, data);
                    break;
                case ReportType.WindowsCall:
                    throw new NotImplementedException(I($"{ReportType.WindowsCall.ToString()} report type is not supported."));
                case ReportType.ProcessData:
                    if (!ProcessDataReportLineReceived(data, out errorMessage))
                    {
                        MessageProcessingFailure = CreateMessageProcessingFailure(data, errorMessage);
                        return false;
                    }
                    break;
                case ReportType.ProcessDetouringStatus:
                    if (!ProcessDetouringStatusReceived(data, out errorMessage))
                    {
                        MessageProcessingFailure = CreateMessageProcessingFailure(data, errorMessage);
                        return false;
                    }
                    break;
                default:
                    Contract.Assume(false);
                    break;
            }

            return true;
        }

        private static Failure<string> CreateMessageProcessingFailure(string message) => new Failure<string>(I($"Error message: {message}"));
        private static Failure<string> CreateMessageProcessingFailure(string rawData, string message) => CreateMessageProcessingFailure(I($"{message} | Raw data: {rawData}"));

        private bool ProcessDetouringStatusReceived(string data, out string errorMessage)
        {
            if (!ProcessDetouringStatusReportLine.TryParse(
                data,
                out var processId,
                out var reportStatus,
                out var processName,
                out var startApplicationName,
                out var startCommandLine,
                out var needsInjection,
                out var hJob,
                out var disableDetours,
                out var creationFlags,
                out var detoured,
                out var error,
                out var createProcessStatusReturn,
                out errorMessage))
            {
                return false;
            }

            // If there is a listener registered and not a process message and notifications allowed, notify over the interface.
            if (m_detoursEventListener != null && (m_detoursEventListener.GetMessageHandlingFlags() & MessageHandlingFlags.ProcessDetoursStatusNotify) != 0)
            {
                m_detoursEventListener.HandleProcessDetouringStatus(
                processId,
                reportStatus,
                processName,
                startApplicationName,
                startCommandLine,
                needsInjection,
                hJob,
                disableDetours,
                creationFlags,
                detoured,
                error,
                createProcessStatusReturn);
            }

            // If there is a listener registered that disables the collection of data in the collections, just exit.
            if (m_detoursEventListener != null && (m_detoursEventListener.GetMessageHandlingFlags() & MessageHandlingFlags.ProcessDetoursStatusCollect) == 0)
            {
                return true;
            }

            ProcessDetoursStatuses.Add(new ProcessDetouringStatusData(
                processId,
                reportStatus,
                processName,
                startApplicationName,
                startCommandLine,
                needsInjection,
                hJob,
                disableDetours,
                creationFlags,
                detoured,
                error,
                createProcessStatusReturn));

            return true;
        }

        private static class ProcessDetouringStatusReportLine
        {
            public static bool TryParse(
                string line,
                out ulong processId,
                out uint reportStatus,
                out string processName,
                out string startApplicationName,
                out string startCommandLine,
                out bool needsInjection,
                out ulong hJob,
                out bool disableDetours,
                out uint creationFlags,
                out bool detoured,
                out uint error,
                out uint createProcessStatusReturn,
                out string errorMessage)
            {
                reportStatus = 0;
                needsInjection = false;
                disableDetours = false;
                detoured = false;
                createProcessStatusReturn = 0;
                error = 0;
                creationFlags = 0;
                hJob = 0L;
                processName = default;
                processId = 0;
                startApplicationName = default;
                startCommandLine = default;
                errorMessage = string.Empty;

                var items = line.Split('|');

                // A "process data" report is expected to have exactly 14 items. 1 for the process id,
                // 1 for the command line (last item) and 12 numbers indicating the various counters and
                // execution times.
                // If this assert fires, it indicates that we could not successfully parse (split) the data being
                // sent from the detour (SendReport.cpp).
                // Make sure the strings are formatted only when the condition is false.
                if (items.Length < 12)
                {
                    errorMessage = I($"Unexpected message items (potentially due to pipe corruption). Message '{line}'. Expected >= 12 items, Received {items.Length} items");
                    return false;
                }

                if (items.Length == 12)
                {
                    startCommandLine = items[11];
                }
                else
                {
                    System.Text.StringBuilder builder = Pools.GetStringBuilder().Instance;
                    for (int i = 11; i < items.Length; i++)
                    {
                        if (i > 11)
                        {
                            builder.Append("|");
                        }

                        builder.Append(items[i]);
                    }

                    startCommandLine = builder.ToString();
                }

                processName = items[2];
                startApplicationName = items[3];

                uint uintNeedsInjection;
                uint uintDisableDetours;
                uint uintDetoured;

                if (ulong.TryParse(items[0], NumberStyles.None, CultureInfo.InvariantCulture, out processId) &&
                    uint.TryParse(items[1], NumberStyles.None, CultureInfo.InvariantCulture, out reportStatus) &&
                    uint.TryParse(items[4], NumberStyles.None, CultureInfo.InvariantCulture, out uintNeedsInjection) &&
                    ulong.TryParse(items[5], NumberStyles.None, CultureInfo.InvariantCulture, out hJob) &&
                    uint.TryParse(items[6], NumberStyles.None, CultureInfo.InvariantCulture, out uintDisableDetours) &&
                    uint.TryParse(items[7], NumberStyles.None, CultureInfo.InvariantCulture, out creationFlags) &&
                    uint.TryParse(items[8], NumberStyles.None, CultureInfo.InvariantCulture, out uintDetoured) &&
                    uint.TryParse(items[9], NumberStyles.None, CultureInfo.InvariantCulture, out error) &&
                    uint.TryParse(items[10], NumberStyles.None, CultureInfo.InvariantCulture, out createProcessStatusReturn))
                {
                    needsInjection = uintNeedsInjection == 0 ? false : true;
                    disableDetours = uintDisableDetours == 0 ? false : true;
                    detoured = uintDetoured == 0 ? false : true;
                    return true;
                }

                return false;
            }
        }

        private bool ProcessDataReportLineReceived(string data, out string errorMessage)
        {
            if (!ProcessDataReportLine.TryParse(
                data,
                out var processId,
                out var processName,
                out var ioCounters,
                out var creationDateTime,
                out var exitDateTime,
                out var kernelTime,
                out var userTime,
                out var exitCode,
                out var parentProcessId,
                out var detoursMaxMemHeapSizeInBytes,
                out var manifestSizeInBytes,
                out var finalDetoursHeapSizeInBytes,
                out var allocatedPoolEntries,
                out var maxHandleMapEntries,
                out var handleMapEntries,
                out errorMessage))
            {
                return false;
            }

            Tracing.Logger.Log.LogDetoursMaxHeapSize(
                m_loggingContext,
                PipSemiStableHash,
                PipDescription,
                detoursMaxMemHeapSizeInBytes,
                processName,
                processId,
                manifestSizeInBytes,
                finalDetoursHeapSizeInBytes,
                allocatedPoolEntries,
                maxHandleMapEntries,
                handleMapEntries);

            if (MaxDetoursHeapSize < unchecked((long)detoursMaxMemHeapSizeInBytes))
            {
                MaxDetoursHeapSize = unchecked((long)detoursMaxMemHeapSizeInBytes);
            }

            // If there is a listener registered and not a process message and notifications allowed, notify over the interface.
            if (m_detoursEventListener != null && (m_detoursEventListener.GetMessageHandlingFlags() & MessageHandlingFlags.ProcessDataNotify) != 0)
            {
                m_detoursEventListener.HandleProcessData(
                    PipSemiStableHash,
                    PipDescription,
                    processName,
                    processId,
                    creationDateTime,
                    exitDateTime,
                    kernelTime,
                    userTime,
                    exitCode,
                    ioCounters,
                    parentProcessId);
            }

            // In order to store the ProcessData information, the processId has to be added to
            // collection. This happens in the handler of FileAccess message with operation == Process.
            if (m_detoursEventListener != null && (m_detoursEventListener.GetMessageHandlingFlags() & MessageHandlingFlags.FileAccessCollect) == 0)
            {
                // We are told not to collect the FileAccess events, so the ProcessData cannot be stored either.
                return true;
            }

            bool foundProcess = m_activeProcesses.TryGetValue(processId, out var process);
            Contract.Assert(foundProcess, "Should have found a process before receiving its exit data");

            process.CreationTime = creationDateTime;
            process.ExitTime = exitDateTime;
            process.KernelTime = kernelTime;
            process.UserTime = userTime;
            process.IOCounters = ioCounters;
            process.ExitCode = exitCode;
            process.ParentProcessId = parentProcessId;

            return true;
        }

        private void AddLookupEntryForProcessExit(uint processId, ReportedProcess reportedProcess)
        {
            m_processesExits[processId] = reportedProcess;
        }

        private bool FileAccessReportLineReceived<T>(ref T data, FileAccessReportProvider<T> parser, out string errorMessage)
        {
            Contract.Assume(!IsFrozen, "FileAccessReportLineReceived: !IsFrozen");

            if (!parser(
                ref data,
                out var processId,
                out var operation,
                out var requestedAccess,
                out var status,
                out var explicitlyReported,
                out var error,
                out var usn,
                out var desiredAccess,
                out var shareMode,
                out var creationDisposition,
                out var flagsAndAttributes,
                out var manifestPath,
                out var path,
                out var enumeratePattern,
                out var processArgs,
                out errorMessage))
            {
                return false;
            }

            // Special case seen with vstest.console.exe
            if (string.IsNullOrEmpty(path))
            {
                return true;
            }

            // If there is a listener registered and notifications allowed, notify over the interface.
            if (m_detoursEventListener != null && (m_detoursEventListener.GetMessageHandlingFlags() & MessageHandlingFlags.FileAccessNotify) != 0)
            {
                m_detoursEventListener.HandleFileAccess(
                    PipSemiStableHash,
                    PipDescription,
                    operation,
                    requestedAccess,
                    status,
                    explicitlyReported,
                    processId,
                    error,
                    desiredAccess,
                    shareMode,
                    creationDisposition,
                    flagsAndAttributes,
                    path,
                    processArgs);
            }

            // If there is a listener registered that disables the collection of data in the collections, just exit.
            if (m_detoursEventListener != null && (m_detoursEventListener.GetMessageHandlingFlags() & MessageHandlingFlags.FileAccessCollect) == 0)
            {
                return true;
            }

            if (m_manifest.DirectoryTranslator != null)
            {
                path = m_manifest.DirectoryTranslator.Translate(path);
            }

            // If we are getting a message for ChangedReadWriteToReadAccess operation,
            // just log it as a warning and return
            if (operation == ReportedFileOperation.ChangedReadWriteToReadAccess)
            {
                Tracing.Logger.Log.ReadWriteFileAccessConvertedToReadMessage(m_loggingContext, PipSemiStableHash, PipDescription, processId, path);
                HasReadWriteToReadFileAccessRequest = true;
                return true;
            }

            // A process id is only unique during the lifetime of the process (so there may be duplicates reported),
            // but the ID is at least consistent with other tracing tools including procmon.
            // For the purposes of event correlation, m_activeProcesses keeps track which process id maps to which process a the current time.
            // We also record all processes in a list. Because process create and exit messages can arrive out of order on macOS when multiple queues
            // are used, we have to keep track of the reported exits using the m_processesExits dictionary.
            ReportedProcess process;
            if (operation == ReportedFileOperation.Process)
            {
                process = new ReportedProcess(processId, path, processArgs);
                m_activeProcesses[processId] = process;
                Processes.Add(process);
            }
            else
            {
                m_activeProcesses.TryGetValue(processId, out process);

                if (operation == ReportedFileOperation.ProcessExit)
                {
                    AddLookupEntryForProcessExit(processId, process);
                    if (process != null)
                    {
                        m_activeProcesses.TryRemove(processId, out _);
                        path = process.Path;
                    }
                    else
                    {
                        // no process to remove;
                        return true;
                    }
                }
            }

            // This assertion doesn't have to hold when using /sandboxKind:macOsKext because some messages may come out of order
            Contract.Assert(OperatingSystemHelper.IsUnixOS || process != null, "Should see a process creation before its accesses (malformed report)");

            // If no active ReportedProcess is found (e.g., because it already completed but we are still processing its access reports),
            // try to see the latest exiting process with the same process id. Otherwise, it's ok to just create an unnamed one since ReportedProcess is used for descriptive purposes only
            if (process == null && (!m_processesExits.TryGetValue(processId, out process) || process == null))
            {
                process = new ReportedProcess(processId, string.Empty, string.Empty);
            }

            // For exact matches (i.e., not a scope rule), the manifest path is the same as the full path.
            // In that case we don't want to keep carrying around the giant string.
            if (AbsolutePath.TryGet(m_pathTable, path, out AbsolutePath finalPath) && finalPath == manifestPath)
            {
                path = null;
            }

            var pathAsAbsolutePath = finalPath;
            if (!pathAsAbsolutePath.IsValid)
            {
                AbsolutePath.TryCreate(m_pathTable, path, out pathAsAbsolutePath);
            }

            if (pathAsAbsolutePath.IsValid && m_sharedOpaqueOutputLogger != null && (requestedAccess & RequestedAccess.Write) != 0)
            {
                m_sharedOpaqueOutputLogger.RecordFileWrite(m_pathTable, pathAsAbsolutePath);
            }

            Contract.Assume(manifestPath.IsValid || !string.IsNullOrEmpty(path));

            if (path != null)
            {
                if (m_pathCache.TryGetValue(path, out var cachedPath))
                {
                    path = cachedPath;
                }
                else
                {
                    m_pathCache[path] = path;
                }
            }

            if (operation == ReportedFileOperation.FirstAllowWriteCheckInProcess)
            {
                // This operation represents that a given path was checked for write access for the first time
                // within the scope of a process. The status of the operation represents whether that access should
                // have been allowed/denied, based on the existence of the file.
                // However, we need to determine whether to deny the access based on the first time the path was
                // checked for writes across the whole process tree. This means checking the first time this operation
                // is reported for a given path, and ignore subsequent reports.
                // Races are ignored: a race means two child processes are racing to create or delete the same file
                // - something that is not a good build behavior anyway - and the outcome will be that we will
                // non-deterministically deny the access
                // We store the path as an absolute path in order to guarantee canonicalization: e.g. prefixes like \\?\
                // are not canonicalized in detours
                if (path != null && pathAsAbsolutePath.IsValid && !m_overrideAllowedWritePaths.ContainsKey(pathAsAbsolutePath))
                {
                    // We should override write allowed accesses for this path if the status of the special operation was 'denied'
                    m_overrideAllowedWritePaths[pathAsAbsolutePath] = (status == FileAccessStatus.Denied);
                }

                return true;
            }

            FileAccessStatusMethod method = FileAccessStatusMethod.PolicyBased;

            // If we are processing an allowed write, but this should be overridden based on file existence,
            // we change the status here
            if (path != null &&
                (requestedAccess & RequestedAccess.Write) != 0 &&
                status == FileAccessStatus.Allowed &&
                m_overrideAllowedWritePaths.Count > 0 && // Avoid creating the absolute path if the override allowed writes flag is off
                pathAsAbsolutePath.IsValid &&
                m_overrideAllowedWritePaths.TryGetValue(pathAsAbsolutePath, out bool shouldOverrideAllowedAccess) &&
                shouldOverrideAllowedAccess)
            {
                status = FileAccessStatus.Denied;
                method = FileAccessStatusMethod.FileExistenceBased;
            }

            var reportedAccess =
                new ReportedFileAccess(
                    operation,
                    process,
                    requestedAccess,
                    status,
                    explicitlyReported,
                    error,
                    usn,
                    desiredAccess,
                    shareMode,
                    creationDisposition,
                    flagsAndAttributes,
                    manifestPath,
                    path,
                    enumeratePattern,
                    method);

            HandleReportedAccess(reportedAccess);
            return true;
        }

        private void HandleReportedAccess(ReportedFileAccess access)
        {
            Contract.Assume(!IsFrozen, "HandleReportedAccess: !IsFrozen");

            if (access.Status == FileAccessStatus.Allowed)
            {
                if (access.ExplicitlyReported)
                {
                    // Note that this set does not contain denied accesses even if they have ExplicitlyReported set.
                    // Let's say that we have some directory D\ with the policy AllowRead|ReportAccess.
                    // A denied write - despite being under a report scope - isn't really what we are looking for.
                    // Presumably since a denied access should also be in the 'denied' set and thus emitted as a warning, error, etc.
                    // Note that this results in FileAccessWarnings, FileUnexpectedAccesses, and ExplicitlyReportedFileAccesses being
                    // disjoint, which is a handy property for not double-reporting things.
                    ExplicitlyReportedFileAccesses.Add(access);
                }
            }
            else
            {
                Contract.Assume(access.Status == FileAccessStatus.Denied || access.Status == FileAccessStatus.CannotDeterminePolicy);
                FileUnexpectedAccesses.Add(access);
            }

            FileAccesses?.Add(access);
        }

        private static class ProcessDataReportLine
        {
            public static bool TryParse(
                string line,
                out uint processId,
                out string processName,
                out IOCounters ioCounters,
                out DateTime creationDateTime,
                out DateTime exitDateTime,
                out TimeSpan kernelTime,
                out TimeSpan userTime,
                out uint exitCode,
                out uint parentProcessId,
                out ulong detoursMaxHeapSizeInBytes,
                out uint manifestSizeInBytes,
                out ulong finalDetoursHeapSizeInBytes,
                out uint allocatedPoolEntries,
                out ulong maxHandleMapEntries,
                out ulong handleMapEntries,
                out string errorMessage)
            {
                processName = default;
                parentProcessId = 0;
                processId = 0;
                ioCounters = default;
                creationDateTime = default;
                exitDateTime = default;
                kernelTime = default;
                userTime = default;
                exitCode = ExitCodes.UninitializedProcessExitCode;
                detoursMaxHeapSizeInBytes = 0;
                errorMessage = string.Empty;

                manifestSizeInBytes = 0;
                finalDetoursHeapSizeInBytes = 0L;
                allocatedPoolEntries = 0;
                maxHandleMapEntries = 0L;
                handleMapEntries = 0L;

                const int NumberOfEntriesInMessage = 24;

                var items = line.Split('|');

                // A "process data" report is expected to have exactly 15 items. 1 for the process id,
                // 1 for the command line (last item) and 12 numbers indicating the various counters and
                // execution times and 1 number for the parent process Id.
                // If this assert fires, it indicates that we could not successfully parse (split) the data being
                // sent from the detour (SendReport.cpp).
                // Make sure the strings are formatted only when the condition is false.
                if (items.Length != NumberOfEntriesInMessage)
                {
                    errorMessage = I($"Unexpected message items. Message'{line}'. Expected {NumberOfEntriesInMessage} items, Received {items.Length} items");
                    return false;
                }

                processName = items[15];

                if (uint.TryParse(items[0], NumberStyles.None, CultureInfo.InvariantCulture, out processId) &&
                    ulong.TryParse(items[1], NumberStyles.None, CultureInfo.InvariantCulture, out var readOperationCount) &&
                    ulong.TryParse(items[2], NumberStyles.None, CultureInfo.InvariantCulture, out var writeOperationCount) &&
                    ulong.TryParse(items[3], NumberStyles.None, CultureInfo.InvariantCulture, out var otherOperationCount) &&
                    ulong.TryParse(items[4], NumberStyles.None, CultureInfo.InvariantCulture, out var readTransferCount) &&
                    ulong.TryParse(items[5], NumberStyles.None, CultureInfo.InvariantCulture, out var writeTransferCount) &&
                    ulong.TryParse(items[6], NumberStyles.None, CultureInfo.InvariantCulture, out var otherTransferCount) &&
                    uint.TryParse(items[7], NumberStyles.None, CultureInfo.InvariantCulture, out var creationHighDateTime) &&
                    uint.TryParse(items[8], NumberStyles.None, CultureInfo.InvariantCulture, out var creationLowDateTime) &&
                    uint.TryParse(items[9], NumberStyles.None, CultureInfo.InvariantCulture, out var exitHighDateTime) &&
                    uint.TryParse(items[10], NumberStyles.None, CultureInfo.InvariantCulture, out var exitLowDateTime) &&
                    uint.TryParse(items[11], NumberStyles.None, CultureInfo.InvariantCulture, out var kernelHighDateTime) &&
                    uint.TryParse(items[12], NumberStyles.None, CultureInfo.InvariantCulture, out var kernelLowDateTime) &&
                    uint.TryParse(items[13], NumberStyles.None, CultureInfo.InvariantCulture, out var userHighDateTime) &&
                    uint.TryParse(items[14], NumberStyles.None, CultureInfo.InvariantCulture, out var userLowDateTime) &&
                    uint.TryParse(items[16], NumberStyles.None, CultureInfo.InvariantCulture, out exitCode) &&
                    uint.TryParse(items[17], NumberStyles.None, CultureInfo.InvariantCulture, out parentProcessId) &&
                    ulong.TryParse(items[18], NumberStyles.None, CultureInfo.InvariantCulture, out detoursMaxHeapSizeInBytes) &&
                    uint.TryParse(items[19], NumberStyles.None, CultureInfo.InvariantCulture, out manifestSizeInBytes) &&
                    ulong.TryParse(items[20], NumberStyles.None, CultureInfo.InvariantCulture, out finalDetoursHeapSizeInBytes) &&
                    uint.TryParse(items[21], NumberStyles.None, CultureInfo.InvariantCulture, out allocatedPoolEntries) &&
                    ulong.TryParse(items[22], NumberStyles.None, CultureInfo.InvariantCulture, out maxHandleMapEntries) &&
                    ulong.TryParse(items[23], NumberStyles.None, CultureInfo.InvariantCulture, out handleMapEntries))
                {
                    long fileTime = creationHighDateTime;
                    fileTime = fileTime << 32;
                    creationDateTime = DateTime.FromFileTimeUtc(fileTime + creationLowDateTime);

                    fileTime = exitHighDateTime;
                    fileTime = fileTime << 32;
                    exitDateTime = DateTime.FromFileTimeUtc(fileTime + exitLowDateTime);

                    fileTime = kernelHighDateTime;
                    fileTime = fileTime << 32;
                    fileTime += kernelLowDateTime;
                    kernelTime = TimeSpan.FromTicks(fileTime);

                    fileTime = userHighDateTime;
                    fileTime = fileTime << 32;
                    fileTime += userLowDateTime;
                    userTime = TimeSpan.FromTicks(fileTime);

                    ioCounters = new IOCounters(
                        new IOTypeCounters(readOperationCount, readTransferCount),
                        new IOTypeCounters(writeOperationCount, writeTransferCount),
                        new IOTypeCounters(otherOperationCount, otherTransferCount));
                    return true;
                }

                return false;
            }
        }

        /// <summary>
        /// Low-level parser for file access report lines
        /// </summary>
        internal class FileAccessReportLine
        {
            /// <summary>
            /// Mapping of "operation name" to <see cref="ReportedFileOperation"/>
            /// </summary>
            internal static readonly Dictionary<string, ReportedFileOperation> Operations =
                new Dictionary<string, ReportedFileOperation>(StringComparer.Ordinal)
                {
                    { "CreateFile", ReportedFileOperation.CreateFile },
                    { "CreateDirectory", ReportedFileOperation.CreateDirectory },
                    { "RemoveDirectory", ReportedFileOperation.RemoveDirectory },
                    { "GetFileAttributes", ReportedFileOperation.GetFileAttributes },
                    { "GetFileAttributesEx", ReportedFileOperation.GetFileAttributesEx },
                    { "FindFirstFileEx", ReportedFileOperation.FindFirstFileEx },
                    { "FindNextFile", ReportedFileOperation.FindNextFile },
                    { "CopyFile_Source", ReportedFileOperation.CopyFileSource },
                    { "CopyFile_Dest", ReportedFileOperation.CopyFileDestination },
                    { "CreateHardLink_Source", ReportedFileOperation.CreateHardLinkSource },
                    { "CreateHardLink_Dest", ReportedFileOperation.CreateHardLinkDestination },
                    { "MoveFile_Source", ReportedFileOperation.MoveFileSource },
                    { "MoveFile_Dest", ReportedFileOperation.MoveFileDestination },
                    { "ZwSetRenameInformationFile_Source", ReportedFileOperation.ZwSetRenameInformationFileSource },
                    { "ZwSetRenameInformationFile_Dest", ReportedFileOperation.ZwSetRenameInformationFileDest },
                    { "ZwSetLinkInformationFile", ReportedFileOperation.ZwSetLinkInformationFile },
                    { "ZwSetDispositionInformationFile", ReportedFileOperation.ZwSetDispositionInformationFile },
                    { "ZwSetModeInformationFile", ReportedFileOperation.ZwSetModeInformationFile },
                    { "ZwSetFileNameInformationFile_Source", ReportedFileOperation.ZwSetFileNameInformationFileSource },
                    { "ZwSetFileNameInformationFile_Dest", ReportedFileOperation.ZwSetFileNameInformationFileDest },
                    { "SetFileInformationByHandle_Source", ReportedFileOperation.SetFileInformationByHandleSource },
                    { "SetFileInformationByHandle_Dest", ReportedFileOperation.SetFileInformationByHandleDest },
                    { "DeleteFile", ReportedFileOperation.DeleteFile },
                    { "Process", ReportedFileOperation.Process },
                    { "ProcessExit", ReportedFileOperation.ProcessExit },
                    { "NtQueryDirectoryFile", ReportedFileOperation.NtQueryDirectoryFile },
                    { "ZwQueryDirectoryFile", ReportedFileOperation.ZwQueryDirectoryFile },
                    { "NtCreateFile", ReportedFileOperation.NtCreateFile },
                    { "ZwCreateFile", ReportedFileOperation.ZwCreateFile },
                    { "ZwOpenFile", ReportedFileOperation.ZwOpenFile },
                    { "CreateSymbolicLink_Source", ReportedFileOperation.CreateSymbolicLinkSource },
                    { "ReparsePointTarget", ReportedFileOperation.ReparsePointTarget },
                    { "ChangedReadWriteToReadAccess", ReportedFileOperation.ChangedReadWriteToReadAccess },
                    { "FirstAllowWriteCheckInProcess", ReportedFileOperation.FirstAllowWriteCheckInProcess },
                    { "MoveFileWithProgress_Source", ReportedFileOperation.MoveFileWithProgressSource },
                    { "MoveFileWithProgress_Dest", ReportedFileOperation.MoveFileWithProgressDest },
                    { "MultipleOperations", ReportedFileOperation.MultipleOperations },
                    { FileOperation.OpMacLookup.GetName(), ReportedFileOperation.MacLookup },
                    { FileOperation.OpMacReadlink.GetName(), ReportedFileOperation.MacReadlink },
                    { FileOperation.OpMacVNodeCreate.GetName(), ReportedFileOperation.MacVNodeCreate },
                    { FileOperation.OpMacVNodeWrite.GetName(), ReportedFileOperation.MacVNodeWrite },
                    { FileOperation.OpMacVNodeCloneSource.GetName(), ReportedFileOperation.MacVNodeCloneSource },
                    { FileOperation.OpMacVNodeCloneDest.GetName(), ReportedFileOperation.MacVNodeCloneDest },
                    { FileOperation.OpKAuthMoveSource.GetName(), ReportedFileOperation.KAuthMoveSource },
                    { FileOperation.OpKAuthMoveDest.GetName(), ReportedFileOperation.KAuthMoveDest },
                    { FileOperation.OpKAuthCreateHardlinkSource.GetName(), ReportedFileOperation.KAuthCreateHardlinkSource },
                    { FileOperation.OpKAuthCreateHardlinkDest.GetName(), ReportedFileOperation.KAuthCreateHardlinkDest },
                    { FileOperation.OpKAuthCopySource.GetName(), ReportedFileOperation.KAuthCopySource },
                    { FileOperation.OpKAuthCopyDest.GetName(), ReportedFileOperation.KAuthCopyDest },
                    { FileOperation.OpKAuthDeleteDir.GetName(), ReportedFileOperation.KAuthDeleteDir },
                    { FileOperation.OpKAuthDeleteFile.GetName(), ReportedFileOperation.KAuthDeleteFile },
                    { FileOperation.OpKAuthOpenDir.GetName(), ReportedFileOperation.KAuthOpenDir },
                    { FileOperation.OpKAuthReadFile.GetName(), ReportedFileOperation.KAuthReadFile },
                    { FileOperation.OpKAuthCreateDir.GetName(), ReportedFileOperation.KAuthCreateDir },
                    { FileOperation.OpKAuthWriteFile.GetName(), ReportedFileOperation.KAuthWriteFile },
                    { FileOperation.OpKAuthClose.GetName(), ReportedFileOperation.KAuthClose },
                    { FileOperation.OpKAuthCloseModified.GetName(), ReportedFileOperation.KAuthCloseModified },
                    { FileOperation.OpKAuthVNodeExecute.GetName(), ReportedFileOperation.KAuthVNodeExecute },
                    { FileOperation.OpKAuthVNodeWrite.GetName(), ReportedFileOperation.KAuthVNodeWrite },
                    { FileOperation.OpKAuthVNodeRead.GetName(), ReportedFileOperation.KAuthVNodeRead },
                    { FileOperation.OpKAuthVNodeProbe.GetName(), ReportedFileOperation.KAuthVNodeProbe },
                };

            [SuppressMessage("Microsoft.Globalization", "CA1305:CultureInfo.InvariantCulture")]
            public static bool TryParse(
                ref string line,
                out uint processId,
                out ReportedFileOperation operation,
                out RequestedAccess requestedAccess,
                out FileAccessStatus status,
                out bool explicitlyReported,
                out uint error,
                out Usn usn,
                out DesiredAccess desiredAccess,
                out ShareMode shareMode,
                out CreationDisposition creationDisposition,
                out FlagsAndAttributes flagsAndAttributes,
                out AbsolutePath absolutePath,
                out string path,
                out string enumeratePattern,
                out string processArgs,
                out string errorMessage)
            {
                // TODO: Task 138817: Refactor passing and parsing of report data from native to managed code

                operation = ReportedFileOperation.Unknown;
                requestedAccess = RequestedAccess.None;
                status = FileAccessStatus.None;
                processId = error = 0;
                usn = default;
                explicitlyReported = false;
                desiredAccess = 0;
                shareMode = ShareMode.FILE_SHARE_NONE;
                creationDisposition = 0;
                flagsAndAttributes = 0;
                absolutePath = AbsolutePath.Invalid;
                path = null;
                enumeratePattern = null;
                processArgs = null;
                errorMessage = string.Empty;

                var i = line.IndexOf(':');
                var index = 0;

                if (i > 0)
                {
                    var items = line.Substring(i + 1).Split('|');

                    if (!Operations.TryGetValue(line.Substring(0, i), out operation))
                    {
                        // We could consider the report line malformed in this case; but in practice it is easy to forget to update this parser
                        // after adding a new call. So let's be conservative about throwing the line out so long as we can parse the important bits to follow.
                        operation = ReportedFileOperation.Unknown;
                    }

                    // When the command line arguments of the process are not reported there will be 12 fields
                    // When command line arguments are included, everything after the 12th field is the command line argument
                    // Command line arguments are only reported when the reported file operation is Process
                    if (operation == ReportedFileOperation.Process)
                    {
                        // Make sure the formatting happens only if the condition is false.
                        if (items.Length < 12)
                        {
                            errorMessage = I($"Unexpected message items (potentially due to pipe corruption) for {operation.ToString()} operation. Message '{line}'. Expected >= 12 items, Received {items.Length} items");
                            return false;
                        }
                    }
                    else
                    {
                        // An ill behaved tool can try to do GetFileAttribute on a file with '|' char. This will result in a failure of the API, but we get a report for the access.
                        // Allow that by handling such case.
                        // In Office build there is a call to GetFileAttribute with a small xml document as a file name.
                        if (items.Length < 12)
                        {
                            errorMessage = I($"Unexpected message items (potentially due to pipe corruption) for {operation.ToString()} operation. Message '{line}'. Expected >= 12 items, Received {items.Length} items");
                            return false;
                        }
                    }

                    if (
                        uint.TryParse(items[index++], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out processId) &&
                        uint.TryParse(items[index++], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var requestedAccessValue) &&
                        uint.TryParse(items[index++], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var statusValue) &&
                        uint.TryParse(items[index++], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var explicitlyReportedValue) &&
                        uint.TryParse(items[index++], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out error) &&
                        ulong.TryParse(items[index++], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var usnValue) &&
                        uint.TryParse(items[index++], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var desiredAccessValue) &&
                        uint.TryParse(items[index++], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var shareModeValue) &&
                        uint.TryParse(items[index++], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var creationDispositionValue) &&
                        uint.TryParse(items[index++], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var flagsAndAttributesValue) &&
                        uint.TryParse(items[index++], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var absolutePathValue))
                    {
                        if (statusValue > (uint)FileAccessStatus.CannotDeterminePolicy)
                        {
                            errorMessage = I($"Unknown file access status '{statusValue}'");
                            return false;
                        }

                        if (requestedAccessValue > (uint)RequestedAccess.All)
                        {
                            errorMessage = I($"Unknown requested access '{requestedAccessValue}'");
                            return false;
                        }

                        requestedAccess = (RequestedAccess)requestedAccessValue;
                        status = (FileAccessStatus)statusValue;
                        explicitlyReported = explicitlyReportedValue != 0;
                        desiredAccess = (DesiredAccess)desiredAccessValue;
                        shareMode = (ShareMode)shareModeValue;
                        creationDisposition = (CreationDisposition)creationDispositionValue;
                        flagsAndAttributes = (FlagsAndAttributes)flagsAndAttributesValue;
                        absolutePath = new AbsolutePath(unchecked((int)absolutePathValue));
                        path = items[index++];
                        // Detours is only guaranteed to sent at least 12 items, so here (since we are at index 12), we must check if this item is included
                        enumeratePattern = index < items.Length ? items[index++] : null;

                        if (requestedAccess != RequestedAccess.Enumerate)
                        {
                            // If the requested access is not enumeration, enumeratePattern does not matter.
                            enumeratePattern = null;
                        }

                        if ((operation == ReportedFileOperation.Process) && (items.Length > index))
                        {
                            processArgs = items[index++];
                            while (index < items.Length)
                            {
                                processArgs += "|";
                                processArgs += items[index++];
                            }
                        }
                        else
                        {
                            processArgs = string.Empty;
                        }

                        usn = new Usn(usnValue);
                        Contract.Assert(index <= items.Length);
                        return true;
                    }
                }

                return false;
            }
        }
    }
}
