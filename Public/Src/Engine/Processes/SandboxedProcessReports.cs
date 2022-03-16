// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.Linq;
using System.Threading;
using BuildXL.Native.IO;
using BuildXL.Processes.Sideband;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;
using JetBrains.Annotations;
using static BuildXL.Processes.IDetoursEventListener;
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
        // CODESYNC: Public\Src\Sandbox\Windows\DetoursServices\FileAccessHelpers.h
        public const uint FileAccessNoId = 0;

        private static readonly Dictionary<string, ReportType> s_reportTypes = Enum
            .GetValues(typeof(ReportType))
            .Cast<ReportType>()
            .ToDictionary(reportType => ((int)reportType).ToString(), reportType => reportType);

        private readonly PathTable m_pathTable;
        private readonly ConcurrentDictionary<uint, ReportedProcess> m_activeProcesses = new ConcurrentDictionary<uint, ReportedProcess>();
        private readonly ConcurrentDictionary<uint, ReportedProcess> m_processesExits = new ConcurrentDictionary<uint, ReportedProcess>();

        private readonly Dictionary<string, string> m_pathCache = new Dictionary<string, string>(OperatingSystemHelper.PathComparer);
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
        /// A denied access based on existence is not actually final until we have processed all accesses. This is because augmented file accesses coming from trusted tools
        /// always override file existence denials. When accesses come in order, we can always count for trusted tools accesses to prevent the creation of file extistence denials.
        /// But many times trusted tool accesses come out of order, since approaches like the VBCSCompilerLogger sends them all together when the project is done building. This forces
        /// us to reconsider denials we have already created. This is fine since all reported file access collections this class manages are considered in flux until the instance is frozen
        /// <see cref="Freeze"/>.
        /// In this dictionary we store the aforementioned denials to be able to 'retract' them if needed.
        /// </summary>
        private readonly MultiValueDictionary<AbsolutePath, ReportedFileAccess> m_deniedAccessesBasedOnExistence = new MultiValueDictionary<AbsolutePath, ReportedFileAccess>();

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
        
        private readonly ISandboxFileSystemView m_fileSystemView;

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
            [CanBeNull] SidebandWriter sharedOpaqueOutputLogger,
            [CanBeNull] ISandboxFileSystemView fileSystemView)
        {
            Contract.RequiresNotNull(manifest);
            Contract.RequiresNotNull(pathTable);
            Contract.RequiresNotNull(pipDescription);
            Contract.RequiresNotNull(loggingContext);

            PipSemiStableHash = pipSemiStableHash;
            PipDescription = pipDescription;
            m_pathTable = pathTable;
            FileAccesses = manifest.ReportFileAccesses ? new HashSet<ReportedFileAccess>() : null;
            FileUnexpectedAccesses = new HashSet<ReportedFileAccess>();
            m_manifest = manifest;
            m_detoursEventListener = detoursEventListener;
            m_sharedOpaqueOutputLogger = sharedOpaqueOutputLogger;
            m_loggingContext = loggingContext;
            m_fileSystemView = fileSystemView;
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
            out uint id,
            out uint correlationId,
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
            out FlagsAndAttributes openedFileOrDirectoryAttributes,
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
            var result = FileAccessReportLineReceived(ref accessReport, parser, isAnAugmentedFileAccess: false, out var errorMessage);
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

            if (m_manifest.MessageCountSemaphore != null && reportType.ShouldCountReportType())
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

            string errorMessage = string.Empty;

            switch (reportType)
            {
                case ReportType.FileAccess:
                    if (!FileAccessReportLineReceived(ref data, FileAccessReportLine.TryParse, isAnAugmentedFileAccess: false, out errorMessage))
                    {
                        MessageProcessingFailure = CreateMessageProcessingFailure(data, errorMessage);
                        return false;
                    }

                    break;

                case ReportType.DebugMessage:
                    if (m_detoursEventListener != null && (m_detoursEventListener.GetMessageHandlingFlags() & MessageHandlingFlags.DebugMessageNotify) != 0)
                    {
                        m_detoursEventListener.HandleDebugMessage(new DebugData { PipId = PipSemiStableHash, PipDescription = PipDescription, DebugMessage = data });
                    }

                    Tracing.Logger.Log.LogDetoursDebugMessage(m_loggingContext, PipSemiStableHash, data);
                    break;

                case ReportType.WindowsCall:
                    throw new NotImplementedException(I($"{ReportType.WindowsCall} report type is not supported."));

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

                case ReportType.AugmentedFileAccess:
                    if (!FileAccessReportLineReceived(ref data, TryParseAugmentedFileAccess, isAnAugmentedFileAccess: true, out errorMessage))
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
                out var isCurrent64BitProcess,
                out var isCurrentWow64Process,
                out var isProcessWow64,
                out var needsRemoteInjection,
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

            var detouringStatusData = new ProcessDetouringStatusData(
                processId,
                reportStatus,
                processName,
                startApplicationName,
                startCommandLine,
                needsInjection,
                isCurrent64BitProcess,
                isCurrentWow64Process,
                isProcessWow64,
                needsRemoteInjection,
                hJob,
                disableDetours,
                creationFlags,
                detoured,
                error,
                createProcessStatusReturn);

            // If there is a listener registered and not a process message and notifications allowed, notify over the interface.
            if (m_detoursEventListener != null && (m_detoursEventListener.GetMessageHandlingFlags() & MessageHandlingFlags.ProcessDetoursStatusNotify) != 0)
            {
                m_detoursEventListener.HandleProcessDetouringStatus(detouringStatusData);
            }

            // If there is a listener registered that disables the collection of data in the collections, just exit.
            if (m_detoursEventListener != null && (m_detoursEventListener.GetMessageHandlingFlags() & MessageHandlingFlags.ProcessDetoursStatusCollect) == 0)
            {
                return true;
            }

            ProcessDetoursStatuses.Add(detouringStatusData);

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
                out bool isCurrent64BitProcess,
                out bool isCurrentWow64Process,
                out bool isProcessWow64,
                out bool needsRemoteInjection,
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
                isCurrent64BitProcess = false;
                isCurrentWow64Process = false;
                isProcessWow64 = false;
                needsRemoteInjection = false;
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
                if (items.Length < 16)
                {
                    errorMessage = I($"Unexpected message items (potentially due to pipe corruption). Message '{line}'. Expected >= 12 items, Received {items.Length} items");
                    return false;
                }

                if (items.Length == 16)
                {
                    startCommandLine = items[15];
                }
                else
                {
                    System.Text.StringBuilder builder = Pools.GetStringBuilder().Instance;
                    for (int i = 15; i < items.Length; i++)
                    {
                        if (i > 15)
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
                uint uintIsCurrent64BitProcess;
                uint uintIsCurrentWow64Process;
                uint uintIsProcessWow64;
                uint uintNeedsRemoteInjection;
                uint uintDisableDetours;
                uint uintDetoured;

                if (ulong.TryParse(items[0], NumberStyles.None, CultureInfo.InvariantCulture, out processId) &&
                    uint.TryParse(items[1], NumberStyles.None, CultureInfo.InvariantCulture, out reportStatus) &&
                    uint.TryParse(items[4], NumberStyles.None, CultureInfo.InvariantCulture, out uintNeedsInjection) &&
                    uint.TryParse(items[5], NumberStyles.None, CultureInfo.InvariantCulture, out uintIsCurrent64BitProcess) &&
                    uint.TryParse(items[6], NumberStyles.None, CultureInfo.InvariantCulture, out uintIsCurrentWow64Process) &&
                    uint.TryParse(items[7], NumberStyles.None, CultureInfo.InvariantCulture, out uintIsProcessWow64) &&
                    uint.TryParse(items[8], NumberStyles.None, CultureInfo.InvariantCulture, out uintNeedsRemoteInjection) &&
                    ulong.TryParse(items[9], NumberStyles.None, CultureInfo.InvariantCulture, out hJob) &&
                    uint.TryParse(items[10], NumberStyles.None, CultureInfo.InvariantCulture, out uintDisableDetours) &&
                    uint.TryParse(items[11], NumberStyles.None, CultureInfo.InvariantCulture, out creationFlags) &&
                    uint.TryParse(items[12], NumberStyles.None, CultureInfo.InvariantCulture, out uintDetoured) &&
                    uint.TryParse(items[13], NumberStyles.None, CultureInfo.InvariantCulture, out error) &&
                    uint.TryParse(items[14], NumberStyles.None, CultureInfo.InvariantCulture, out createProcessStatusReturn))
                {
                    needsInjection = uintNeedsInjection != 0;
                    isCurrent64BitProcess = uintIsCurrent64BitProcess != 0;
                    isCurrentWow64Process = uintIsCurrentWow64Process != 0;
                    isProcessWow64 = uintIsProcessWow64 != 0;
                    needsRemoteInjection = uintNeedsRemoteInjection != 0;
                    disableDetours = uintDisableDetours != 0;
                    detoured = uintDetoured != 0;
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
                m_detoursEventListener.HandleProcessData(new IDetoursEventListener.ProcessData
                {
                    PipId = PipSemiStableHash,
                    PipDescription = PipDescription,
                    ProcessName = processName,
                    ProcessId = processId,
                    ParentProcessId = parentProcessId,
                    CreationDateTime = creationDateTime,
                    ExitDateTime = exitDateTime,
                    KernelTime = kernelTime,
                    UserTime = userTime,
                    ExitCode = exitCode,
                    IoCounters = ioCounters
                });
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

        private bool TryParseAugmentedFileAccess(
            ref string data,
            out uint processId,
            out uint id,
            out uint correlationId,
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
            out FlagsAndAttributes openedFileOrDirectoryAttributes,
            out AbsolutePath manifestPath,
            out string path,
            out string enumeratePattern,
            out string processArgs,
            out string errorMessage)
        {
            // An augmented file access has the same structure as a regular one, so let's call
            // the usual parser
            var result = FileAccessReportLine.TryParse(
                ref data,
                out processId,
                out id,
                out correlationId,
                out operation,
                out requestedAccess,
                out status,
                out explicitlyReported,
                out error,
                out usn,
                out desiredAccess,
                out shareMode,
                out creationDisposition,
                out flagsAndAttributes,
                out openedFileOrDirectoryAttributes,
                out manifestPath,
                out path,
                out enumeratePattern,
                out processArgs,
                out errorMessage);

            // Augmented file accesses never have the manifest path set, since there is no easy access to the manifest for 
            // processes to use.
            // Let's recreate the manifest path based on the current path and manifest
            // The manifest may have its own path table after deserialization, so make sure we use the right one
            if (string.IsNullOrEmpty(path) || !AbsolutePath.TryCreate(m_manifest.PathTable, path, out var absolutePath))
            {
                return result;
            }
            
            var success = m_manifest.TryFindManifestPathFor(absolutePath, out AbsolutePath computedManifestPath, out FileAccessPolicy policy);

            // If there is no explicit policy for this path, just keep the manifest path and explicitlyReported flag as it came from the report
            if (!success)
            {
                return result;
            }

            manifestPath = computedManifestPath;

            // We override the explicitly reported flag according to the manifest policy.
            // We could impose trusted tools the responsibility of knowing this, but this type of coordination is hard to achieve
            explicitlyReported = (policy & FileAccessPolicy.ReportAccess) != 0;

            // If the access is not explicitly reported, and the global manifest flag is not asking for all accesses to be reported, we ignore
            // this line
            if (!explicitlyReported && !m_manifest.ReportFileAccesses)
            {
                path = null;
                return true;
            }
            
            return result;
        }

        private bool FileAccessReportLineReceived<T>(ref T data, FileAccessReportProvider<T> parser, bool isAnAugmentedFileAccess, out string errorMessage)
        {
            Contract.Assume(!IsFrozen, "FileAccessReportLineReceived: !IsFrozen");

            if (!parser(
                ref data,
                out var processId,
                out var id,
                out var correlationId,
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
                out var openedFileOrDirectoryAttributes,
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

            if (OperatingSystemHelper.IsWindowsOS)
            {
                // CODESYNC: Public/Src/Sandbox/Windows/DetoursServices/SendReport.cpp
                // Handle escaped \r\n characters in path
                path = path.Replace("/\\r", "\r").Replace("/\\n", "\n");
            }

            // If there is a listener registered and notifications allowed, notify over the interface.
            if (m_detoursEventListener != null && (m_detoursEventListener.GetMessageHandlingFlags() & MessageHandlingFlags.FileAccessNotify) != 0)
            {
                m_detoursEventListener.HandleFileAccess(new IDetoursEventListener.FileAccessData
                {
                    PipId = PipSemiStableHash,
                    PipDescription = PipDescription,
                    Operation = operation,
                    RequestedAccess = requestedAccess,
                    Status = status,
                    ExplicitlyReported = explicitlyReported,
                    ProcessId = processId,
                    Id = id,
                    CorrelationId = correlationId,
                    Error = error,
                    DesiredAccess = desiredAccess,
                    ShareMode = shareMode,
                    CreationDisposition = creationDisposition,
                    FlagsAndAttributes = flagsAndAttributes,
                    OpenedFileOrDirectoryAttributes = openedFileOrDirectoryAttributes,
                    Path = m_manifest.DirectoryTranslator?.Translate(path) ?? path,
                    ProcessArgs = processArgs,
                    IsAnAugmentedFileAccess = isAnAugmentedFileAccess
                });
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

            if (!finalPath.IsValid)
            {
                AbsolutePath.TryCreate(m_pathTable, path, out finalPath);
            }

            if (finalPath.IsValid && m_sharedOpaqueOutputLogger != null && (requestedAccess & RequestedAccess.Write) != 0)
            {
                // flushing immediately to ensure the write is recorded as soon as possible
                // (and so that we are more likely to have a record of it if bxl crashes)
                m_sharedOpaqueOutputLogger.RecordFileWrite(m_pathTable, finalPath, flushImmediately: true);
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
                if (path != null && finalPath.IsValid && !m_overrideAllowedWritePaths.ContainsKey(finalPath))
                {
                    // We should override write allowed accesses for this path if the status of the special operation was 'denied'
                    m_overrideAllowedWritePaths[finalPath] = (status == FileAccessStatus.Denied);
                }

                return true;
            }

            if (operation == ReportedFileOperation.CreateProcess)
            {
                // Ensure that when the operation is CreateProcess, the desire access includes execute.
                // This will prevent CreateProcess from being collapsed into Process operation because
                // reported file access does not include operation type in the hash code.
                desiredAccess = desiredAccess | DesiredAccess.GENERIC_EXECUTE;
            }

            // If this is an augmented file access, the method was not based on policy, but a trusted tool reported the access
            FileAccessStatusMethod method = isAnAugmentedFileAccess
                ? FileAccessStatusMethod.TrustedTool
                : FileAccessStatusMethod.PolicyBased;

            // If we are processing an allowed write, but this should be overridden based on file existence,
            // we change the status here
            // Observe that if the access is coming from a trusted tool, that trumps file existence and we don't deny the access
            if (method != FileAccessStatusMethod.TrustedTool
                && path != null
                && (requestedAccess & RequestedAccess.Write) != 0
                && status == FileAccessStatus.Allowed
                && m_overrideAllowedWritePaths.Count > 0 // Avoid creating the absolute path if the override allowed writes flag is off
                && finalPath.IsValid
                && m_overrideAllowedWritePaths.TryGetValue(finalPath, out bool shouldOverrideAllowedAccess)
                && shouldOverrideAllowedAccess)
            {
                status = FileAccessStatus.Denied;
                method = FileAccessStatusMethod.FileExistenceBased;
            }

            // Note that when the operation is Process, then the reported process <code>process</code> is the process itself.
            // This can be interpreted as follows: the process starts and it's accessing the file path of the executable.
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
                    openedFileOrDirectoryAttributes,
                    manifestPath,
                    path,
                    enumeratePattern,
                    method);

            // By reporting created directories as soon as possible we minimize the chance of a race for
            // long running processes, where a produced directory is already created but the output file
            // system is notified of this only when the pip finishes
            // The output file system is eventually notified of this as part of the regular flow under PipExecutor,
            // this is an optimization to minimize cache misses when MinimalGraphWithAlienFiles is used to
            // compute the directory enumeration fingerprint, since created directories matter for it.
            if (finalPath.IsValid && m_fileSystemView != null && reportedAccess.IsDirectoryEffectivelyCreated())
            {
                m_fileSystemView.ReportOutputFileSystemDirectoryCreated(finalPath);
            }

            // The access was denied based on file existence. Store it since we can change our minds later if
            // a trusted access arrives for the same path
            if (status == FileAccessStatus.Denied && method == FileAccessStatusMethod.FileExistenceBased)
            {
                Contract.Assert(finalPath.IsValid);
                m_deniedAccessesBasedOnExistence.Add(finalPath, reportedAccess);
            }

            HandleReportedAccess(finalPath, reportedAccess);

            return true;
        }

        private void HandleReportedAccess(AbsolutePath finalPath, ReportedFileAccess access)
        {
            Contract.Assume(!IsFrozen, "HandleReportedAccess: !IsFrozen");

            // If there is an allowed trusted tool access, it overrides any denials based on file existence that we may have found so far 
            // for that path. We remove those accesses and replace them with allowed ones.
            if (access.Method == FileAccessStatusMethod.TrustedTool && 
                access.Status == FileAccessStatus.Allowed && 
                finalPath.IsValid &&
                m_deniedAccessesBasedOnExistence.TryGetValue(finalPath, out IReadOnlyList<ReportedFileAccess> deniedAccesses))
            {
                foreach (var deniedAccess in deniedAccesses)
                {
                    // Remove the denied access and add the corresponding allowed ones
                    FileUnexpectedAccesses.Remove(deniedAccess);
                    FileAccesses?.Remove(deniedAccess);
                    ExplicitlyReportedFileAccesses.Remove(deniedAccess);
                    HandleReportedAccess(finalPath, deniedAccess.CreateWithStatus(FileAccessStatus.Allowed));
                    // Remove the denied accesses from the dictionary
                    m_deniedAccessesBasedOnExistence.Remove(finalPath);
                    // Block further file existence based denials on that path
                    m_overrideAllowedWritePaths[finalPath] = false;
                }
            }

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

                // There might be relaxing policies in place that may allow a denied access based on file existence
                // But in order to process it we need to explicitly report the access
                if (access.Method == FileAccessStatusMethod.FileExistenceBased && access.ExplicitlyReported)
                {
                    ExplicitlyReportedFileAccesses.Add(access);
                }
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
    }
}
