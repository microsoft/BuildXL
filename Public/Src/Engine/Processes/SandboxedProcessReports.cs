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
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Instrumentation.Common;
using static BuildXL.Processes.IDetoursEventListener;
using static BuildXL.Utilities.Core.FormattableStringEx;

namespace BuildXL.Processes
{
    /// <summary>
    /// Parser and aggregator for incoming file access reports
    /// </summary>
    /// <remarks>
    /// Instance members of this class are not thread-safe.
    ///
    /// On Windows, BuildXL uses two semaphores for keeping track of the number of messages (or file access reports) sent by Detours
    /// and the number of messages received by the sandbox. The first semaphore, <see cref="FileAccessManifest.MessageCountSemaphore"/>,
    /// is incremented before Detours sends a message, and is decremented by the sandbox upon receiving a message. Thus,
    /// for correctness, the value of this semaphore should be zero at the end of the detoured process execution.
    ///
    /// The second semaphore, <see cref="FileAccessManifest.MessageSentCountSemaphore"/> is incremented by Detours after successfully sending a message.
    /// This semaphore is never decremented by the sandbox. When the sandbox receives a message, it increments an internal counter, <see cref="m_receivedReportCount"/>.
    /// Thus, for correctness, the value of the second semaphore should be equal to the value of <see cref="m_receivedReportCount"/> at the end of the detoured process execution.
    /// If the value of the second semaphore is greater than the value of <see cref="m_receivedReportCount"/>, then there are messages that were successfully sent but not received,
    /// meaning that there are lost messages. Because BuildXL relies on these messages for caching, having lost messages can result in incorrect caching, which can lead
    /// to underbuild.
    /// 
    /// For example, if a process read file A and B, then the sandbox expects Read(A) and Read(B) messages. Suppose that the sandbox only received Read(A). Then, BuildXL
    /// will only use the content of A as the cache key. However, if the content of B changes, then we expect the process to re-run (instead of using the cached result).
    /// But since Read(B) was lost, and the content of A does not change, then BuildXL will use the cached result, which is incorrect (or underbuild).
    ///
    /// Thus, when there is a lost message, BuildXL should fail the corresponding process pip.
    ///
    /// The logic in sending a message in Detours looks like the following (see Public\Src\Sandbox\Windows\DetoursServices\SendReport.cpp):
    /// <code>
    /// void SendReportString(message) {
    ///   A: ReleaseSemaphore(MessageCountSemaphore, 1, NULL);
    ///   B:
    ///   C: bool success = WriteFile(ReportHandle, message);
    ///   D:
    ///   E: if (success) { ReleaseSemaphore(MessageSentCountSemaphore, 1, NULL); }
    ///   F:
    /// }
    /// </code>
    /// During the process execution, the process (or its threads) can terminate at any of these points, A, B, C, D, E, or F. Because this happen during
    /// the call to a detoured API (e.g., CreateFile for opening a handle), this indicates that (1) the executed tool could have a non-deterministic file-access
    /// behavior, and (2) the file access may be irrelevant for caching, and can be ignored.
    /// 
    /// Assume that <code>ReleaseSemaphore</code> is atomic. If the termination happens at B or during the call to <code>WriteFile</code> at C, then
    /// <see cref="FileAccessManifest.MessageCountSemaphore"/> has been released, but the message may not have been sent, and the sandbox would not receive
    /// the message. Thus, at the end of the execution, the value of <see cref="FileAccessManifest.MessageCountSemaphore"/> is greater than 0. This scenario
    /// is not considered a lost message because the message has not been sent, and BuildXL should not fail the process pip. Note that, by construction, the value
    /// of <see cref="FileAccessManifest.MessageCountSemaphore"/> cannot be less than 0.
    /// 
    /// If the termination happens at D, then the message has been sent and the sandbox received the message, but <see cref="FileAccessManifest.MessageSentCountSemaphore"/>
    /// has not been released. Thus, at the end of the execution, the value of <see cref="FileAccessManifest.MessageSentCountSemaphore"/> is less than the value
    /// of <see cref="m_receivedReportCount"/>. This scenario is also not considered a lost message because the sandbox received the message.
    /// 
    /// The only error case is if <see cref="FileAccessManifest.MessageSentCountSemaphore"/> has been released, indicating that a message has been successfully sent, but
    /// the message is not received by the sandbox (message is lost), causing the value of <see cref="m_receivedReportCount"/> to be less than the value
    /// of <see cref="FileAccessManifest.MessageSentCountSemaphore"/>. In this case, BuildXL should fail the process pip.
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
        private readonly ConcurrentDictionary<uint, ReportedProcess> m_activeProcesses = new();
        private readonly ConcurrentDictionary<uint, ReportedProcess> m_processesExits = new();

        /// <summary>
        /// Synthetic processes that were created when an access arrived with a process id and a parent process id that were
        /// unknown to the sandbox.
        /// </summary>
        /// <remarks>
        /// The dictionary is indexed by parent id.  
        /// When the parent process is determined (based on subsequent accesses, as the callstack unfolds), children information is completed based on it
        /// and the element is moved out from this dictionary. Once the pip has exited, this dictionary should be empty.
        /// Observe this collection is not exposed outside of this class. Reports are received sequentially, so there is no need for synchronization.
        /// </remarks>
        private readonly Dictionary<uint, HashSet<ReportedProcess>> m_unknownProcessesByParent = new();

        private readonly Dictionary<string, string> m_pathCache = new(OperatingSystemHelper.PathComparer);
        private readonly Dictionary<AbsolutePath, bool> m_overrideAllowedWritePaths = new();

        private readonly Dictionary<AbsolutePath, RequestedAccess> m_fileAccessesBeforeFirstUndeclaredReWrite = new();

        /// <summary>
        /// The requested accesses per path before the first undeclared rewrite.
        /// </summary>
        /// <remarks>
        /// Only populated when <see cref="m_allowUndeclaredFileReads"/> is true, since only under that case we allow writes to undeclared sources.
        /// We are interested in knowing whether the pip is using this path as an input before an undeclared rewrite. At this point just for telemetry purposes: If the path 
        /// was accessed before the rewrite, BuildXL may not have had the opportunity to hash the original content, and we want to understand how often this happens.
        /// Observe that if a given path never had an undeclared rewrite, this dictionary will just contain all the input accesses to that path.
        /// </remarks>
        public IReadOnlyDictionary<AbsolutePath, RequestedAccess> FileAccessesBeforeFirstUndeclaredReWrite => m_fileAccessesBeforeFirstUndeclaredReWrite;

        [MaybeNull]
        private readonly IDetoursEventListener m_detoursEventListener;

        [MaybeNull]
        private readonly SidebandWriter m_sharedOpaqueOutputLogger;

        public readonly List<ReportedProcess> Processes = new();
        public readonly HashSet<ReportedFileAccess> FileUnexpectedAccesses;
        public readonly HashSet<ReportedFileAccess> FileAccesses;
        public readonly HashSet<ReportedFileAccess> ExplicitlyReportedFileAccesses = new();

        public readonly List<ProcessDetouringStatusData> ProcessDetoursStatuses = new();
        
        /// <summary>
        /// The last message count of messages sent and received.
        /// </summary>
        /// <remarks>
        /// If the returned value is greater than 0, it means that there are messages that were sent (or were about to be sent) but not received.
        /// This can happen if the process/thread terminates before sending the messages or while sending the messages. By design, the value
        /// returned by this method should not be less than 0. For details, see remarks of this class.
        /// </remarks>
        public int GetLastMessageCount() => m_manifest.MessageCountSemaphore?.Release() ?? 0;

        /// <summary>
        /// The difference between the number of messages successfully sent by Detours, and the number of messages received by this instance of sandboxed process reports.
        /// </summary>
        /// <remarks>
        /// If the returned value is greater than 0, then there are lost messages, i.e., there are messages that were successfully sent but not received.
        /// The returned value can be less than 0 if there are messages successfully sent by Detours, and are received by this instance, but Detours did not
        /// have a chance to release the counting semaphore. For details, see remarks of this class.
        /// </remarks>
        public int GetLastConfirmedMessageCount() => (m_manifest.MessageSentCountSemaphore?.Release() ?? m_receivedReportCount) - m_receivedReportCount;

        public INamedSemaphore GetMessageCountSemaphore() => m_manifest.MessageCountSemaphore;

        private bool m_isFrozen;
        
        /// <summary>
        /// A denied access based on existence is not actually final until we have processed all accesses. This is because augmented file accesses coming from trusted tools
        /// always override file existence denials. When accesses come in order, we can always count for trusted tools accesses to prevent the creation of file extistence denials.
        /// But many times trusted tool accesses come out of order, since approaches like the VBCSCompilerLogger sends them all together when the project is done building. This forces
        /// us to reconsider denials we have already created. This is fine since all reported file access collections this class manages are considered in flux until the instance is frozen
        /// <see cref="Freeze"/>.
        /// In this dictionary we store the aforementioned denials to be able to 'retract' them if needed.
        /// </summary>
        private readonly MultiValueDictionary<AbsolutePath, ReportedFileAccess> m_deniedAccessesBasedOnExistence = new();

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

        private readonly SandboxedProcessTraceBuilder m_traceBuilder;

        private readonly List<AbsolutePath> m_processesRequiringPTrace;
        private readonly string m_fileName;
        private readonly bool m_allowUndeclaredFileReads;
        private int m_receivedReportCount = 0;

        public SandboxedProcessReports(
            FileAccessManifest manifest,
            PathTable pathTable,
            long pipSemiStableHash,
            string pipDescription,
            LoggingContext loggingContext,
            string fileName,
            bool allowUndeclaredFileReads,
            [MaybeNull] IDetoursEventListener detoursEventListener,
            [MaybeNull] SidebandWriter sharedOpaqueOutputLogger,
            [MaybeNull] ISandboxFileSystemView fileSystemView,
            [MaybeNull] SandboxedProcessTraceBuilder traceBuilder = null)
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
            m_traceBuilder = traceBuilder;
            m_processesRequiringPTrace = OperatingSystemHelper.IsLinuxOS ? new List<AbsolutePath>() : null;
            m_fileName = fileName;
            m_allowUndeclaredFileReads = allowUndeclaredFileReads;
        }

        /// <summary>ReportArgsMismatch
        /// Freezes the report disallowing further modification
        /// </summary>
        internal void Freeze()
        {
            Volatile.Write(ref m_isFrozen, true);

            // Dump any detected processes requiring ptrace for this pip
            if (m_processesRequiringPTrace?.Any() == true)
            {
                var exePath = string.Join(", ", m_processesRequiringPTrace.Select(p => p.ToString(m_pathTable)));

                // If the ptrace sandbox is enabled, just log this as a verbose message to facilitate debugging scenarios. Otherwise, print a warning, since this is a case where we could
                // be missing accesses
                if (m_manifest.EnableLinuxPTraceSandbox)
                {
                    Tracing.Logger.Log.PTraceSandboxLaunchedForPip(m_loggingContext, PipDescription, exePath);
                }
                else
                {
                    Tracing.Logger.Log.LinuxSandboxReportedBinaryRequiringPTrace(m_loggingContext, PipDescription, exePath);
                }
            }

            if (m_unknownProcessesByParent.Count > 0)
            {
                // We shouldn't hit this case. We should always have the parent process tracked.
                // This is just an info message for debugging purposes. We are interested in spotting reports without an associated process
                // because it means that the process creation message was not received.
                // The practical consequence of not having this process properly registered is that downstream allowlists may try to use the image name for matching. So accesses
                // associated to this untracked process may cause a DFA. All considerations given, this is not a critical issue so we allow the build to move on.
                foreach (var process in m_unknownProcessesByParent.Values.SelectMany(reportedProcess => reportedProcess))
                {
                    Tracing.Logger.Log.ReceivedReportFromUnknownPid(m_loggingContext, PipDescription, process.ProcessId.ToString());
                }
            }
        }

        /// <summary>
        /// Returns a list of still active child processes for which we only received a ProcessCreate but no
        /// ProcessExit event.
        /// </summary>
        internal IEnumerable<ReportedProcess> GetActiveProcesses()
        {
            var matches = new HashSet<uint>(m_processesExits.Select(entry => entry.Key));
            return m_activeProcesses.Where(entry => !matches.Contains(entry.Key)).Select(entry => entry.Value);
        }

        /// <summary>
        /// Delegate for parsing generic access reports.
        /// </summary>
        public delegate bool FileAccessReportProvider<T>(
            ref T data,
            out uint processId,
            out uint parentProcessId,
            out uint id,
            out uint correlationId,
            out ReportedFileOperation operation,
            out RequestedAccess requestedAccess,
            out FileAccessStatus status,
            out bool explicitlyReported,
            out uint error,
            out uint rawError,
            out Usn usn,
            out DesiredAccess desiredAccess,
            out ShareMode shareMode,
            out CreationDisposition creationDisposition,
            out FlagsAndAttributes flagsAndAttributes,
            out FlagsAndAttributes openedFileOrDirectoryAttributes,
            out AbsolutePath manifestPath,
            out string path,
            out bool isPathTruncated,
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
        /// Entry point for reporting sandbox infrastructure messages. See <see cref="ExtendedDetoursEventListener"/>
        /// </summary>
        /// <remarks>
        /// The message is reported to the detours listener if one is registered.
        /// </remarks>
        public bool ReportSandboxInfraMessage(ExtendedDetoursEventListener.SandboxInfraMessage sandboxInfraMessage)
        {
            if (m_detoursEventListener != null &&
                m_detoursEventListener is ExtendedDetoursEventListener extendedDetoursEventListener &&
                (m_detoursEventListener.GetMessageHandlingFlags() & MessageHandlingFlags.DebugMessageNotify) != 0)
            {
                extendedDetoursEventListener.HandleSandboxInfraMessage(sandboxInfraMessage);
            }

            return true;
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
                    Interlocked.Increment(ref m_receivedReportCount);
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
            out uint parentProcessId,
            out uint id,
            out uint correlationId,
            out ReportedFileOperation operation,
            out RequestedAccess requestedAccess,
            out FileAccessStatus status,
            out bool explicitlyReported,
            out uint error,
            out uint rawError,
            out Usn usn,
            out DesiredAccess desiredAccess,
            out ShareMode shareMode,
            out CreationDisposition creationDisposition,
            out FlagsAndAttributes flagsAndAttributes,
            out FlagsAndAttributes openedFileOrDirectoryAttributes,
            out AbsolutePath manifestPath,
            out string path,
            out bool isPathTruncated,
            out string enumeratePattern,
            out string processArgs,
            out string errorMessage)
        {
            // An augmented file access has the same structure as a regular one, so let's call the usual parser.
            var result = FileAccessReportLine.TryParse(
                ref data,
                out processId,
                out parentProcessId,
                out id,
                out correlationId,
                out operation,
                out requestedAccess,
                out status,
                out explicitlyReported,
                out error,
                out rawError,
                out usn,
                out desiredAccess,
                out shareMode,
                out creationDisposition,
                out flagsAndAttributes,
                out openedFileOrDirectoryAttributes,
                out manifestPath,
                out path,
                out isPathTruncated,
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
                out var parentProcessId,
                out var id,
                out var correlationId,
                out var operation,
                out var requestedAccess,
                out var status,
                out var explicitlyReported,
                out var error,
                out var rawError,
                out var usn,
                out var desiredAccess,
                out var shareMode,
                out var creationDisposition,
                out var flagsAndAttributes,
                out var openedFileOrDirectoryAttributes,
                out var manifestPath,
                out var path,
                out var isPathTruncated,
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

            // If the path was truncated we print a warning and ignore the access. Observe that a path
            // can only be truncated by the Linux sandbox.
            // A truncated path means that the path exceeded 4k. Even though the Linux OS does not set a
            // bound to a path length, most APIs work with paths shorter than that. Which means that
            // it is very unlikely that such path can point to a real artifact on disk. The most likely
            // cause of getting a path exceeding 4k is that a tool put together an incorrect/malformed one.
            if (isPathTruncated)
            {
                Tracing.Logger.Log.PathTooLongIsIgnored(m_loggingContext, PipDescription, path);
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
                    RawError = rawError,
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
            m_activeProcesses.TryGetValue(processId, out var process);

            // For the case of Linux, we send 2 process start reports for each clone/fork (on the parent and on the child process). So this check will also
            // make sure that we only create one instance of a reported process for it. This avoids duplicated processes, but also makes sure that
            // whenever an 'exec' for this process comes, we update the single instance for it wrt path and arguments. Additionally, for Linux there is
            // always a process exit event, which guarantees that in case of process id reuse, we will always see a process exit in between.
            // For the case of Windows, we do not send process exit events, but at the same time we don't need to avoid duplicated processes because
            // we only send one process start, not two. This means that for the case of Windows, when a process start arrives with the same ID than an existing
            // active process has, we always want to replace it.
            if (operation == ReportedFileOperation.Process && (OperatingSystemHelper.IsWindowsOS || process == null))
            {
                process = new ReportedProcess(processId, parentProcessId, path, processArgs);

                TrackProcessCreated(process);
            }

            // The associated process can be null for Unix systems. E.g. clone3 does not have a libc implementation and we will miss it.
            Contract.Assert(OperatingSystemHelper.IsUnixOS || process != null, "Should see a process creation before its accesses (malformed report)");

            // If no active ReportedProcess is found (e.g., because it already completed but we are still processing its access reports),
            // try to see the latest exiting process with the same process id.
            if (operation != ReportedFileOperation.ProcessRequiresPTrace &&
                operation != ReportedFileOperation.ProcessBreakaway &&
                operation != ReportedFileOperation.FirstAllowWriteCheckInProcess &&
                process == null && (!m_processesExits.TryGetValue(processId, out process) || process == null))
            {
                Tracing.Logger.Log.LogDetoursDebugMessage(m_loggingContext, PipSemiStableHash, $"No process start found for {processId}");
                // This is a case where we could have missed the process start event (e.g. check clone3 comment above)
                // On Unix systems, a process start event is associated with a fork/clone. This means that we can reuse the parent process information regarding paths, arguments, etc for the current
                // process, as long as we preserve the process id.
                if (!TryGetProcessStartedOrExited(parentProcessId, out var parentProcess))
                {
                    // If we hit this case, it means that we have a process that has a parent process we don't know about. The known case that causes this is when nesting multiple clone3 calls.
                    // However, we should be able to reconstruct the process tree as the callstack unfolds (at the very least we should eventually see the corresponding process exit events). 
                    // We create for now a synthetic process using the pip main executable as the path for this process. And we will update it whenever we get the missing information.
                    // If for any reason we never see this missing piece of info, the pip main executable will remain as the process path. We don't really have a better option here,
                    // and not setting a path is problematic downstream. We will log a message for debugging purposes later if this happens to be the case.
                    process = new ReportedProcess(processId, parentProcessId, m_fileName, string.Empty);
                    
                    // Let's keep track of this process so that we can update it later if we get the missing information
                    if (m_unknownProcessesByParent.TryGetValue(parentProcessId, out var processes))
                    {
                        processes.Add(process);
                    }
                    else
                    {
                        m_unknownProcessesByParent[parentProcessId] = new HashSet<ReportedProcess>() {process};
                    }
                }
                else
                {
                    // We found the parent process. Let's create a synthetic process for the current process id based on the parent process information
                    // This is accurate on Linux: we missed a fork/clone, so the parent process shares executable path and arguments with its child
                    process = new ReportedProcess(processId, parentProcessId, parentProcess.Path, parentProcess.ProcessArgs);
                }

                // Let's treat it as if the process was just created
                // This allows potential children of this process (for which their process start event might be missing) to be associated with the correct process.
                TrackProcessCreated(process);
            }

            // Now that we have compensated for a potential missing process start, the associated process shouldn't be null, even for the Linux case
            // The only exception are the special internal messages for which we don't care about compensating for missing process starts
            if (operation != ReportedFileOperation.ProcessRequiresPTrace &&
                operation != ReportedFileOperation.FirstAllowWriteCheckInProcess &&
                operation != ReportedFileOperation.ProcessBreakaway &&
                process == null)
            {
                Contract.Assert(false, $"Process shouldn't be null for access [{processId}]:{operation}:'{path}'");
            }

            // This is a special Linux-specific report, it contains a path and the command line arguments passed to an exec call on a process ID that already exists in our table.
            // If the error value was not 0, that means the exec failed and therefore we should not update the path/args
            if (operation == ReportedFileOperation.ProcessExec && error == 0)
            {
                process.UpdateOnPathAndArgsOnExec(path, processArgs);
                m_traceBuilder?.UpdateProcessArgs(process, path, processArgs);

                // if this process exec has an associated report with missing information, we can already populate it and we don't need to wait for an ancestor with that info (and it would be 
                // wrong to do so)
                if (m_unknownProcessesByParent.TryGetValue(process.ParentProcessId, out var processes))
                {
                    processes.Remove(process);
                }
            }

            // Now take care of the case where the report was an exit
            if (operation == ReportedFileOperation.ProcessExit)
            {
                AddLookupEntryForProcessExit(processId, process);

                m_activeProcesses.TryRemove(processId, out _);
                path = process.Path;
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

            m_traceBuilder?.ReportFileAccess(processId, operation, requestedAccess, finalPath, error, isAnAugmentedFileAccess, enumeratePattern, id, correlationId);

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
                if (finalPath.IsValid && !m_overrideAllowedWritePaths.ContainsKey(finalPath))
                {
                    // We should override write allowed accesses for this path if the status of the special operation was 'denied'
                    m_overrideAllowedWritePaths[finalPath] = (status == FileAccessStatus.Denied);
                }

                return true;
            }

            if (operation == ReportedFileOperation.ProcessRequiresPTrace)
            {
                // The sandbox should automatically filter out duplicate process names
                m_processesRequiringPTrace.Add(finalPath);
                return true;
            }

            if (operation == ReportedFileOperation.ProcessBreakaway)
            {
                Tracing.Logger.Log.ProcessBreakaway(m_loggingContext, PipDescription, finalPath.ToString(m_pathTable), process.ProcessId);

                // We'll never see the process exit for a breakaway process, so remove it
                // from the active processes
                AddLookupEntryForProcessExit(processId, process);
                m_activeProcesses.TryRemove(processId, out _);

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
                    rawError,
                    usn,
                    desiredAccess,
                    shareMode,
                    creationDisposition,
                    flagsAndAttributes,
                    manifestPath,
                    path,
                    enumeratePattern,
                    openedFileOrDirectoryAttributes,
                    method);

            // We need to track directories effectively created by pips. In order to do that, we need to interpret access reports in order,
            // since we want to understand if the first thing that happened to a directory was a creation or a deletion. We cannot
            // do this post-pip execution, since structures at that point contain sets (unordered collections) of accesses. So we do it here, as soon
            // as accesses arrive.
            if (finalPath.IsValid && m_fileSystemView != null)
            {
                // We want to be sure this is the first time the directory is created (to rule out cases where a pip removes a directory
                // and the directory is later re-created)
                if (reportedAccess.IsDirectoryEffectivelyCreated() && !m_fileSystemView.ExistRemovedDirectoryInOutputFileSystem(finalPath))
                {
                    m_fileSystemView.ReportOutputFileSystemDirectoryCreated(finalPath);
                }

                // Same here, we want to make sure that we only report removal if the directory was not created before by the build
                // Observe directory removals are only reported on execution. On cache replay, the cache is unaware of directories and the
                // removal operation won't occur (the cache only creates a directory when there is a file under that directory that needs to be replayed). 
                // Downstream pips may therefore behave differently (e.g. a pip probes the existence of a directory and changes behavior based on the result). 
                // And that means downstream pips may behave differently depending on whether the upstream was a cache hit or a miss, 
                // but that's a bigger problem to solve, where the cache needs to start treating directories as first class citizens.
                if (reportedAccess.IsDirectoryEffectivelyRemoved() && !m_fileSystemView.ExistCreatedDirectoryInOutputFileSystem(finalPath))
                {
                    m_fileSystemView.ReportOutputFileSystemDirectoryRemoved(finalPath);
                }
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

        /// <summary>
        /// Updates all the structures that track active/created processes
        /// </summary>
        /// <remarks>
        /// It also makes sure that if there is any unknown process <see cref="m_unknownProcessesByParent"/> that has the just created process as a parent,
        /// it get properly updated
        /// </remarks>
        private void TrackProcessCreated(ReportedProcess process)
        {
            m_activeProcesses[process.ProcessId] = process;
            Processes.Add(process);
            m_traceBuilder?.ReportProcess(process);

            // We may have unknown processes that are children of this process. Let's update them with the correct path and args
            UpdateUnkownProcessesWithParent(process);
        }

        /// <summary>
        /// Updates all the process tree that belong to <see cref="m_unknownProcessesByParent"/> that has the given process as ancestor 
        /// </summary>
        /// <remarks>
        /// After updating a process with the ancestor information, the process is not unknown anymore and therefore it is removed from the collection
        /// </remarks>
        private void UpdateUnkownProcessesWithParent(ReportedProcess parentProcess)
        {
            // No unknown processes for this parent
            if (!m_unknownProcessesByParent.TryGetValue(parentProcess.ProcessId, out var unknownProcesses))
            {
                return;
            }

            // Update the path and args for all unknown processes (recursively)
            foreach (var unknownProcess in unknownProcesses)
            {
                UpdateUnkownProcessesWithParent(unknownProcess);
                unknownProcess.UpdateOnPathAndArgsOnExec(parentProcess.Path, parentProcess.ProcessArgs);
                m_traceBuilder?.UpdateProcessArgs(unknownProcess, parentProcess.Path, parentProcess.ProcessArgs);
            }
            
            // We updated all unknown processes for this parent, so we can remove them from the dictionary
            m_unknownProcessesByParent.Remove(parentProcess.ProcessId);
        }

        /// <summary>
        /// Tries to find the given process id within the active processes or the processes that have exited.
        /// </summary>
        /// <remarks>
        /// Consider that a process id may be reused after a process has exited. This method looks first for an active
        /// process with that id, and if it doesn't find it, it looks for a process that has exited. We only keep the last
        /// process that has exited with a given id.
        /// </remarks>
        private bool TryGetProcessStartedOrExited(uint processId, out ReportedProcess reportedProcess)
        {
            if (m_activeProcesses.TryGetValue(processId, out reportedProcess) || 
                m_processesExits.TryGetValue(processId, out reportedProcess)
            )
            {
                return true;
            }
            
            reportedProcess = null;
            return false;
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

            // If allowed undeclared reads is enabled, we might get file existence based writes, which represent a rewrite.
            // In that case, accumulate all the non-write observations to paths until the first file-existence based write is reported.
            // These observations represent all the input access types that we got before the first rewrite to the undeclared source.
            // This is for now just for telemetry purposes, since this is a situation where BuildXL might not be able to determine the content of the file
            // before it was rewritten. Observe we don't actually care about the reported access, just the requested access type.
            if (m_allowUndeclaredFileReads && !m_deniedAccessesBasedOnExistence.ContainsKey(finalPath) && (access.RequestedAccess & RequestedAccess.Write) == 0)
            {
                var finalRequestedAccess = access.RequestedAccess;
                if (m_fileAccessesBeforeFirstUndeclaredReWrite.TryGetValue(finalPath, out var observations))
                {
                    finalRequestedAccess |= observations;
                }

                m_fileAccessesBeforeFirstUndeclaredReWrite[finalPath] = finalRequestedAccess;
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
