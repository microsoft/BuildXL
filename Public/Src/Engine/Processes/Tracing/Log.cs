// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Instrumentation.Common;

#pragma warning disable 1591
#nullable enable

namespace BuildXL.Processes.Tracing
{
    /// <summary>
    /// Logging
    /// </summary>
    [EventKeywordsType(typeof(Keywords))]
    [EventTasksType(typeof(Tasks))]
    [LoggingDetails("ProcessesLogger")]
    public abstract partial class Logger
    {
        /// <summary>
        /// Returns the logger instance
        /// </summary>
        public static Logger Log => m_log;

        [GeneratedEvent(
            (int)LogEventId.PipProcessFileAccess,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "File access on '{3}' with {2}")]
        public abstract void PipProcessFileAccess(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string fileAccessDescription,
            string path);

        [GeneratedEvent(
            (int)LogEventId.PipFailSymlinkCreation,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "Process created a symlink at '{2}'. Symlink outputs are not currently supported. This error was introduced by /FailSymlinkCreationflag.")]
        public abstract void PipFailSymlinkCreation(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string path);

        [GeneratedEvent(
            (int)LogEventId.PipInvalidDetoursDebugFlag1,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = "A debug {ShortProductName} is using a non-debug DetoursServices.dll.")]
        public abstract void PipInvalidDetoursDebugFlag1(LoggingContext context);

        [GeneratedEvent(
            (int)LogEventId.PipInvalidDetoursDebugFlag2,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = "A non-debug {ShortProductName} is using a debug DetoursServices.dll.")]
        public abstract void PipInvalidDetoursDebugFlag2(LoggingContext context);

        [GeneratedEvent(
            (int)LogEventId.PipProcessStartFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "Process start failed with error code {2:X8}: {3}. Pip may be retried or failed.")]
        public abstract void PipProcessStartFailed(LoggingContext context, long pipSemiStableHash, string pipDescription, int errorCode, string message);

        [GeneratedEvent(
            (int)LogEventId.PipProcessFileNotFound,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "Process start failed with error code {2:X8}: File '{3}' was not found on disk. The tool is referred to in '{4}({5})'.")]
        public abstract void PipProcessFileNotFound(LoggingContext context, long pipSemiStableHash, string pipDescription, int errorCode, string filename, string specFile, int position);

        [GeneratedEvent(
            (int)LogEventId.PipProcessFinished,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.Diagnostics,
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "Process exited cleanly with exit code {2}")]
        public abstract void PipProcessFinished(LoggingContext context, long pipSemiStableHash, string pipDescription, int exitCode);

        [GeneratedEvent(
            (int)LogEventId.PipProcessFinishedFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.Diagnostics,
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "Process failed with exit code {2}")]
        public abstract void PipProcessFinishedFailed(LoggingContext context, long pipSemiStableHash, string pipDescription, int exitCode);

        [GeneratedEvent(
            (int)LogEventId.PipProcessMessageParsingError,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "Process failed with message parsing error: {2}.")]
        public abstract void PipProcessMessageParsingError(LoggingContext context, long pipSemiStableHash, string pipDescription, string error);

        [GeneratedEvent(
            (int)LogEventId.PipProcessDisallowedTempFileAccess,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message =
                EventConstants.PipPrefix + "Disallowed file access in temp directory was blocked on '{3}' with {2}; declare that this pip needs a temp directory.")]
        public abstract void PipProcessDisallowedTempFileAccess(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string fileAccessDescription,
            string path);

        [GeneratedEvent(
            (int)LogEventId.PipOutputNotAccessed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message =
                EventConstants.PipPrefix + "No file access for output: {2}. Detours discovered inconsistency in detouring some child processes. Information about the inconsistency can be found in the BuildXL log file. Please, restart the build...")]
        public abstract void PipOutputNotAccessed(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string outputFileName);

        [GeneratedEvent(
            (int)LogEventId.PipProcessDisallowedFileAccess,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipSpecPrefix + " - Disallowed file access was detected on '{5}' with {4}.")]
        public abstract void PipProcessDisallowedFileAccess(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string pipSpecPath,
            string pipWorkingDirectory,
            string fileAccessDescription,
            string path);

        [GeneratedEvent(
            (int)LogEventId.PipProcessDisallowedNtCreateFileAccessWarning,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipSpecPrefix + " - Disallowed NtCreateFile access was detected on '{5}' with {4}. " +
                "This warning will become an error if the '/unsafe_ignoreNtCreateFile+' is removed.")]
        public abstract void PipProcessDisallowedNtCreateFileAccessWarning(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string pipSpecPath,
            string pipWorkingDirectory,
            string fileAccessDescription,
            string path);

        [GeneratedEvent(
            (int)LogEventId.PipProcessTookTooLongWarning,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message =
                EventConstants.PipPrefix + "Process ran for {2}, which is longer than the warning timeout of {3}; the process will be terminated if it ever runs longer than {4}")]
        public abstract void PipProcessTookTooLongWarning(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string actual,
            string softMax,
            string hardMax);

        [GeneratedEvent(
            (int)LogEventId.PipProcessTookTooLongError,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "Process terminated because it took too long: {2}; the timeout is set to {3}. {4}\r\nProcess Output:\r\n{5}")]
        public abstract void PipProcessTookTooLongError(LoggingContext context, long pipSemiStableHash, string pipDescription, string actual, string time, string dumpDetails, string outputToLog);

        [GeneratedEvent(
            (int)LogEventId.PipProcessStandardOutput,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "Process standard output at '{2}'")]
        public abstract void PipProcessStandardOutput(LoggingContext context, long pipSemiStableHash, string pipDescription, string path);

        [GeneratedEvent(
            (int)LogEventId.PipProcessStandardError,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "Process standard error at '{2}'")]
        public abstract void PipProcessStandardError(LoggingContext context, long pipSemiStableHash, string pipDescription, string path);

        [GeneratedEvent(
            (int)LogEventId.PipProcessFileAccessTableEntry,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "File access table entry '{2}'")]
        public abstract void PipProcessFileAccessTableEntry(LoggingContext context, long pipSemiStableHash, string pipDescription, string value);

        [GeneratedEvent(
            (int)LogEventId.PipProcessFailedToParsePathOfFileAccess,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "Access to the following path will be ignored, since the path could not be parsed: '{3}' (Accessed via {2})")]
        public abstract void PipProcessFailedToParsePathOfFileAccess(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string operation,
            string path);

        [GeneratedEvent(
            (int)LogEventId.PipProcessIgnoringPathOfSpecialDeviceFileAccess,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "Access to the following path will be ignored, since the path is a path to a device: '{3}' (Accessed via {2})")]
        public abstract void PipProcessIgnoringPathOfSpecialDeviceFileAccess(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string operation,
            string path);

        [GeneratedEvent(
            (int)LogEventId.PipProcessIgnoringPathWithWildcardsFileAccess,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "Access to the following path will be ignored, since the path contains wildcard characters: '{3}' (Accessed via {2})")]
        public abstract void PipProcessIgnoringPathWithWildcardsFileAccess(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string operation,
            string path);

        [GeneratedEvent(
            (int)LogEventId.PipProcessDisallowedFileAccessAllowlistedNonCacheable,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message =
                EventConstants.PipPrefix +
                "Disallowed file access (non-cacheable) was detected on '{3}' with {2}. This message will become an error if the allowlist entry (in a top-level configuration file) allowing this access is removed.")]
        public abstract void PipProcessDisallowedFileAccessAllowlistedNonCacheable(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string processPath,
            string path);

        [GeneratedEvent(
            (int)LogEventId.PipProcessDisallowedFileAccessAllowlistedCacheable,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message =
                EventConstants.PipPrefix +
                "Disallowed file access (cacheable) was detected on '{3}' with {2}. This message will become an error if the allowlist entry (in a top-level configuration file) allowing this access is removed.")]
        public abstract void PipProcessDisallowedFileAccessAllowlistedCacheable(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string processPath,
            string path);

        [GeneratedEvent(
            (int)LogEventId.FileAccessAllowlistFailedToParsePath,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message =
                EventConstants.PipPrefix +
                "Failed to parse tool path '{3}' at character '{4}' that accessed '{2}'. File access allowlist entries matching on tool paths will not be checked for this access.")]
        public abstract void FileAccessAllowlistFailedToParsePath(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string path,
            string processPath,
            int characterWithError);

        [GeneratedEvent(
            (int)LogEventId.PipProcessUncacheableAllowlistNotAllowedInDistributedBuilds,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.PipExecutor,
            Message =
                EventConstants.PipPrefix +
                "Disallowed file access (non-cacheable) was detected on '{3}' with {2}. This message is an error because non-cacheable allowlist matches are not allowed in distributed builds.")]
        public abstract void PipProcessUncacheableAllowlistNotAllowedInDistributedBuilds(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string processPath,
            string path);

        [GeneratedEvent(
            (int)LogEventId.Process,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "Process with id {2} at '{3}'")]
        public abstract void PipProcess(LoggingContext context, long pipSemiStableHash, string pipDescription, uint id, string path);

        [GeneratedEvent(
            (int)LogEventId.BrokeredDetoursInjectionFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = "Failed to instrument process ID {0} with path '{1}' for file monitoring on behalf of an existing instrumented process, error: {2}")]
        public abstract void BrokeredDetoursInjectionFailed(LoggingContext context, uint processId, string processPath, string error);

        [GeneratedEvent(
            (int)LogEventId.LogDetoursDebugMessage,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = "[Pip{pipSemiStableHash:X16}] Detours Debug Message: {message}")]
        public abstract void LogDetoursDebugMessage(
            LoggingContext context,
            long pipSemiStableHash,
            string message);

        [GeneratedEvent(
            (int)LogEventId.LogSandboxInfoMessage,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = "[Pip{pipSemiStableHash:X16}] {message}")]
        public abstract void LogSandboxInfoMessage(
            LoggingContext context,
            long pipSemiStableHash,
            string message);

        [GeneratedEvent(
            (int)LogEventId.FindAnyBuildClient,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = "Find AnyBuild client for process remoting at '{anyBuildInstallDir}'")]
        public abstract void FindAnyBuildClient(LoggingContext context, string anyBuildInstallDir);

        [GeneratedEvent(
            (int)LogEventId.FindOrStartAnyBuildDaemon,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = "Find or start AnyBuild daemon manager for process remoting with arguments '{args}' (log directory: '{logDir}')")]
        public abstract void FindOrStartAnyBuildDaemon(LoggingContext context, string args, string logDir);

        [GeneratedEvent(
            (int)LogEventId.ExceptionOnFindOrStartAnyBuildDaemon,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = "Exception on finding or starting AnyBuild daemon: {exception}")]
        public abstract void ExceptionOnFindOrStartAnyBuildDaemon(LoggingContext context, string exception);

        [GeneratedEvent(
            (int)LogEventId.ExceptionOnGetAnyBuildRemoteProcessFactory,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = "Exception on getting AnyBuild remote process factory: {exception}")]
        public abstract void ExceptionOnGetAnyBuildRemoteProcessFactory(LoggingContext context, string exception);

        [GeneratedEvent(
            (int)LogEventId.ExceptionOnFindingAnyBuildClient,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = "Exception on finding AnyBuild client: {exception}")]
        public abstract void ExceptionOnFindingAnyBuildClient(LoggingContext context, string exception);

        [GeneratedEvent(
            (int)LogEventId.AnyBuildRepoConfigOverrides,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = "AnyBuild repo config overrides: {config}")]
        public abstract void AnyBuildRepoConfigOverrides(LoggingContext context, string config);

        [GeneratedEvent(
            (int)LogEventId.InstallAnyBuildClient,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = "Installing AnyBuild client from '{source}' (ring: {ring})")]
        public abstract void InstallAnyBuildClient(LoggingContext context, string source, string ring);

        [GeneratedEvent(
            (int)LogEventId.InstallAnyBuildClientDetails,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = "Installing AnyBuild client from '{source}' (ring: {ring}): {reason}")]
        public abstract void InstallAnyBuildClientDetails(LoggingContext context, string source, string ring, string reason);

        [GeneratedEvent(
            (int)LogEventId.FailedDownloadingAnyBuildClient,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = "Failed downloading AnyBuild client: {message}")]
        public abstract void FailedDownloadingAnyBuildClient(LoggingContext context, string message);

        [GeneratedEvent(
            (int)LogEventId.FailedInstallingAnyBuildClient,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = "Failed installing AnyBuild client: {message}")]
        public abstract void FailedInstallingAnyBuildClient(LoggingContext context, string message);

        [GeneratedEvent(
            (int)LogEventId.FinishedInstallAnyBuild,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = "Finished installing AnyBuild client: {message}")]
        public abstract void FinishedInstallAnyBuild(LoggingContext context, string message);

        [GeneratedEvent(
            (int)LogEventId.ExecuteAnyBuildBootstrapper,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = "Execute AnyBuild bootstrapper: {command}")]
        public abstract void ExecuteAnyBuildBootstrapper(LoggingContext context, string command);

        [GeneratedEvent(
            (int)LogEventId.LogAppleSandboxPolicyGenerated,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "Apple sandbox-exec policy for pip generated: {policyFilePath}")]
        public abstract void LogAppleSandboxPolicyGenerated(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string policyFilePath);

        [GeneratedEvent(
            (int)LogEventId.LogDetoursMaxHeapSize,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.Diagnostics,
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "Maximum detours heap size for process in the pip is {maxDetoursHeapSizeInBytes} bytes. The processName '{processName}'. The processId is: {processId}. The manifestSize in bytes is: {manifestSizeInBytes}. The finalDetoursHeapSize in bytes is: {finalDetoursHeapSizeInBytes}. The allocatedPoolEntries is: {allocatedPoolEntries}. The maxHandleMapEntries is: {maxHandleMapEntries}. The handleMapEntries is: {handleMapEntries}.")]
        public abstract void LogDetoursMaxHeapSize(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            ulong maxDetoursHeapSizeInBytes,
            string processName,
            uint processId,
            uint manifestSizeInBytes,
            ulong finalDetoursHeapSizeInBytes,
            uint allocatedPoolEntries,
            ulong maxHandleMapEntries,
            ulong handleMapEntries);

        [GeneratedEvent(
            (int)LogEventId.LogInternalDetoursErrorFileNotEmpty,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "Detoured process emitted failure information that could not be transmitted back to {ShortProductName}. Diagnostic file content: {2}")]
        public abstract void LogInternalDetoursErrorFileNotEmpty(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string message);

        [GeneratedEvent(
            (int)LogEventId.LogFailedToCreateDirectoryForInternalDetoursFailureFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "Failed to create directory for the internal Detours error file. Path: {path}. Error: {message}")]
        public abstract void LogFailedToCreateDirectoryForInternalDetoursFailureFile(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string path,
            string message);

        [GeneratedEvent(
            (int)LogEventId.LogGettingInternalDetoursErrorFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "Failed checking for detours backup communication file existence. Pip will be treated as a failure. Error: {message}.")]
        public abstract void LogGettingInternalDetoursErrorFile(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string message);

        [GeneratedEvent(
            (int)LogEventId.LogMismatchedDetoursCountLostMessages,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "The number of messages sent successfully by detoured processes did not match the number received by the {MainExecutableName} process, which indicates lost messages. {MainExecutableName} cannot reliably use the file accesses reported by Detours for caching the process pip. LostMessageCount: {lastConfirmedMessageCount}")]
        public abstract void LogMismatchedDetoursCountLostMessages(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            int lastMessageCount,
            int lastConfirmedMessageCount);

        [GeneratedEvent(
            (int)LogEventId.LogMismatchedDetoursCount,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "The number of messages sent by detoured processes did not match the number received by the {MainExecutableName} process, which indicates that calls to detoured APIs terminated abruptly, and indicates that the detoured processes could have a non-deterministic file access behavior. This can cause the process pip to be cached with different sets of file accesses as cache keys. DiffBetweenSendAttemptsAndReceived: {lastMessageCount}. LostMessageCount: {lastConfirmedMessageCount} (<= 0 means no lost message).")]
        public abstract void LogMismatchedDetoursCount(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            int lastMessageCount,
            int lastConfirmedMessageCount);

        [GeneratedEvent(
            (int)LogEventId.LogMessageCountSemaphoreExists,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "Can't open semaphore for counting Detours messages. Full failure message: {2}")]
        public abstract void LogMessageCountSemaphoreOpenFailure(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string failureMessage);

        [GeneratedEvent(
            (int)LogEventId.PipProcessCommandLineTooLong,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "Process command line is longer than {3} characters: {2}")]
        public abstract void PipProcessCommandLineTooLong(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string commandLine,
            int maxLength);

        [GeneratedEvent(
            (int)LogEventId.PipProcessInvalidWarningRegex,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "Process warning regular expression is invalid, pattern is {2}, options are {3}")]
        public abstract void PipProcessInvalidWarningRegex(LoggingContext context, long pipSemiStableHash, string pipDescription, string pattern, string options);

        [GeneratedEvent(
            (int)LogEventId.PipProcessInvalidErrorRegex,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "Process error regular expression is invalid, pattern is {2}, options are {3}")]
        public abstract void PipProcessInvalidErrorRegex(LoggingContext context, long pipSemiStableHash, string pipDescription, string pattern, string options);

        [GeneratedEvent(
            (int)LogEventId.PipProcessChildrenSurvivedError,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "Unexpected child processes survived: {2} process(es){3}")]
        public abstract void PipProcessChildrenSurvivedError(LoggingContext context, long pipSemiStableHash, string pipDescription, int count, string paths);

        [GeneratedEvent(
            (int)LogEventId.PipProcessChildrenSurvivedTooMany,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "Too many child processes survived: {2} process(es){3}")]
        public abstract void PipProcessChildrenSurvivedTooMany(LoggingContext context, long pipSemiStableHash, string pipDescription, int count, string paths);

        [GeneratedEvent(
            (int)LogEventId.PipProcessChildrenSurvivedKilled,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "Process and/or job containing child processes killed")]
        public abstract void PipProcessChildrenSurvivedKilled(LoggingContext context, long pipSemiStableHash, string pipDescription);

        [GeneratedEvent(
            (int)LogEventId.PipProcessMissingExpectedOutputOnCleanExit,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipSpecPrefix + "Process was expected to write an output file at '{4}', but that file is not present.")]
        public abstract void PipProcessMissingExpectedOutputOnCleanExit(LoggingContext context, long pipSemiStableHash, string pipDescription, string pipSpecPath, string pipWorkingDirectory, string path);

        [GeneratedEvent(
            (int)LogEventId.PipProcessWroteToStandardErrorOnCleanExit,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipSpecPrefix + "Process exited succesfully but wrote to standard error. The process is configured to fail in this case, even if the exit code was successful.")]
        public abstract void PipProcessWroteToStandardErrorOnCleanExit(LoggingContext context, long pipSemiStableHash, string pipDescription, string pipSpecPath, string pipWorkingDirectory);

        [GeneratedEvent(
          (int)LogEventId.PipProcessExpectedMissingOutputs,
          EventGenerators = EventGenerators.LocalOnly,
          EventLevel = Level.Error,
          Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
          EventTask = (int)Tasks.PipExecutor,
          Message = EventConstants.PipPrefix + "- Process was expected to write the following output files, but those files are not present.:\r\n{2}")]
        public abstract void PipProcessExpectedMissingOutputs(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string paths);

        [GeneratedEvent(
            (int)LogEventId.PipProcessOutputPreparationFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.InfrastructureIssue),
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipSpecPrefix + "Process output directories could not be prepared, path '{2}', error code {3:X8}: {4}")]
        public abstract void PipProcessOutputPreparationFailed(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string path,
            int errorCode,
            string message);

        [GeneratedEvent(
            (int)LogEventId.PipProcessOutputPreparationToBeRetriedInVM,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)(Keywords.UserMessage),
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "Process output '{2}' could not be prepared. Attempting to delete it from within the VM on the next retry.")]
        public abstract void PipProcessOutputPreparationToBeRetriedInVM(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string path);

        [GeneratedEvent(
            (int)LogEventId.PipStandardIOFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "Process standard I/O failure at path '{2}', error code {3:X8}: {4}")]
        public abstract void PipStandardIOFailed(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string path,
            int errorCode,
            string message);

        [GeneratedEvent(
            (int)LogEventId.PipExitedUncleanly,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "Pip had unclean exit. Canceled: {canceled}, Error code {errorCode}, Killed: {killed}, # Surviving child errors: {numSurvivingChildErrors}")]
        public abstract void PipExitedUncleanly(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            bool canceled,
            int errorCode,
            bool killed,
            int numSurvivingChildErrors);

        [GeneratedEvent(
            (int)LogEventId.PipRetryDueToExitedWithAzureWatsonExitCode,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "Pip will be retried because its reported process '{process}' with pid '{processId}' exited with Azure Watson's 0xDEAD exit code")]
        public abstract void PipRetryDueToExitedWithAzureWatsonExitCode(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string process,
            uint processId);

        [GeneratedEvent(
            (int)LogEventId.PipFinishedWithSomeProcessExitedWithAzureWatsonExitCode,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix
            + "Pip finished with some process (can be a child process) '{process} {args}' with pid '{processId}' exited with Azure Watson's 0xDEAD exit code. "
            + "Pip will not be cached if warning is treated as an error.")]
        public abstract void PipFinishedWithSomeProcessExitedWithAzureWatsonExitCode(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string process,
            string args,
            uint processId);

        [GeneratedEvent(
            (int)LogEventId.PipProcessStandardInputException,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipSpecPrefix + " Unexpected standard input exception: {4}")]
        public abstract void PipProcessStandardInputException(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string pipSpecPath,
            string pipWorkingDirectory,
            string exceptionMessage);

        [GeneratedEvent(
            (int)LogEventId.PipProcessToolErrorDueToHandleToFileBeingUsed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipSpecPrefix + "The tool '{toolName}' cannot access the file '{file}' because it is being used by another process: \r\n{reason}")]
        public abstract void PipProcessToolErrorDueToHandleToFileBeingUsed(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string pipSpecPath,
            string pipWorkingDirectory,
            string toolName,
            string file,
            string reason);

        [GeneratedEvent(
            (int)LogEventId.PipProcessError,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Keywords.UserMessage | Keywords.UserError),
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipSpecPrefix + " - failed with exit code {exitCode}{optionalMessage}\r\n{outputToLog}\r\n{messageAboutPathsToLog}\r\n{pathsToLog}")]
        public abstract void PipProcessError(
            LoggingContext context,

            // CAUTION!!!
            // Refer PipProcessErrorEventFields.cs if any of these fields or the order is changed.
            // A reference to a field still remains in ConsoleEventListener.cs please refer to that when the fields or the order is changed.
            long pipSemiStableHash,
            string pipDescription,
            string pipSpecPath,
            string pipWorkingDirectory,
            string pipExe,
            string outputToLog,
            string messageAboutPathsToLog,
            string pathsToLog,
            int exitCode,
            string optionalMessage,
            string shortPipDescription,
            long pipExecutionTimeMs);

        [GeneratedEvent(
            (int)LogEventId.PipProcessWarning,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipSpecPrefix + " - warnings\r\n{outputToLog}\r\n{messageAboutPathsToLog}\r\n{pathsToLog}")]
        public abstract void PipProcessWarning(
            LoggingContext context,

            // CAUTION!!!
            // ConsoleEventListener opens up the payload array to pluck off various members. It must be updated
            // if the order or type of these parameters change
            long pipSemiStableHash,
            string pipDescription,
            string pipSpecPath,
            string pipWorkingDirectory,
            string pipExe,
            string outputToLog,
            string messageAboutPathsToLog,
            string pathsToLog);

        [GeneratedEvent(
            (int)LogEventId.PipProcessOutput,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipSpecPrefix + "\r\n{4}")]
        public abstract void PipProcessOutput(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string pipSpecPath,
            string pipWorkingDirectory,
            string outputToLog);

        [GeneratedEvent(
            (ushort)LogEventId.PipTempDirectoryCleanupWarning,
            EventLevel = Level.Warning,
            EventGenerators = EventGenerators.LocalOnly,
            Keywords = (int)(Keywords.UserMessage | Keywords.InfrastructureIssue),
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "Failed to clean temp directory at '{directory}'. Pip may be retried or failed. {exceptionMessage}")]
        public abstract void PipTempDirectoryCleanupFailure(LoggingContext context, long pipSemiStableHash, string pipDescription, string directory, string exceptionMessage);

        [GeneratedEvent(
            (ushort)LogEventId.PipTempDirectorySetupWarning,
            EventLevel = Level.Warning,
            EventGenerators = EventGenerators.LocalOnly,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "Failed to create temp directory at '{directory}'. Pip may be retried or failed. {exceptionMessage}")]
        public abstract void PipTempDirectorySetupFailure(LoggingContext context, long pipSemiStableHash, string pipDescription, string directory, string exceptionMessage);

        [GeneratedEvent(
            (ushort)LogEventId.PipTempSymlinkRedirectionError,
            EventLevel = Level.Error,
            EventGenerators = EventGenerators.LocalOnly,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "Failed to create directory symlink '{directorySymlink}' as a redirection for temp directory '{tempDirectory}'. {exceptionMessage}")]
        public abstract void PipTempSymlinkRedirectionError(LoggingContext context, long pipSemiStableHash, string pipDescription, string directorySymlink, string tempDirectory, string exceptionMessage);

        [GeneratedEvent(
            (ushort)LogEventId.PipTempSymlinkRedirection,
            EventLevel = Level.Verbose,
            EventGenerators = EventGenerators.LocalOnly,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "Create directory symlink '{directorySymlink}' as a redirection for temp directory '{tempDirectory}'")]
        public abstract void PipTempSymlinkRedirection(LoggingContext context, long pipSemiStableHash, string pipDescription, string directorySymlink, string tempDirectory);

        [GeneratedEvent(
            (ushort)LogEventId.PipFailedToCreateDumpFile,
            EventLevel = Level.Warning,
            EventGenerators = EventGenerators.LocalOnly,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "Failed to create dump for timed out process. {exceptionMessage}")]
        public abstract void PipFailedToCreateDumpFile(LoggingContext context, long pipSemiStableHash, string pipDescription, string exceptionMessage);

        [GeneratedEvent(
            (ushort)LogEventId.RetryStartPipDueToErrorPartialCopyDuringDetours,
            EventLevel = Level.Verbose,
            EventGenerators = EventGenerators.LocalOnly,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipSpecPrefix + "Retry to start pip for {retryNumber} time(s) due to the following error: {error}")]
        public abstract void RetryStartPipDueToErrorPartialCopyDuringDetours(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            int error,
            int retryNumber);

        [GeneratedEvent(
            (int)LogEventId.DuplicateWindowsEnvironmentVariableEncountered,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Unexpected duplicate environment variable encountered. Variable '{0}' has already been defined with value '{1}'. The other occurrence with value '{2}' will be ignored.")]
        public abstract void DuplicateWindowsEnvironmentVariableEncountered(
            LoggingContext context,
            string key,
            string existingValue,
            string ignoredValue);

        [GeneratedEvent(
            (int)LogEventId.ReadWriteFileAccessConvertedToReadMessage,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "File access on file '{3}' requested with Read/Write but granted for Read only by process with ID: {2}.")]
        public abstract void ReadWriteFileAccessConvertedToReadMessage(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            uint processId,
            string path);

        [GeneratedEvent(
            (int)LogEventId.ReadWriteFileAccessConvertedToReadWarning,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "This pip might have failed because of converting Read/Write file access to a Read file access. Examine the execution log for information on which files the Read/Write access request was converted to Read access request.")]
        public abstract void ReadWriteFileAccessConvertedToReadWarning(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription);

        [GeneratedEvent(
            (int)LogEventId.PipProcessResponseFileCreationFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipSpecPrefix + "Process response file could not be prepared, path '{2}', error code {3:X8}: {4}")]
        public abstract void PipProcessResponseFileCreationFailed(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string path,
            int errorCode,
            string message);

        [GeneratedEvent(
            (int)LogEventId.PipProcessPreserveOutputDirectoryFailedToMakeFilePrivate,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "Failed to preserve output directory '{directory}' because '{file}' cannot be made private, contents of the directory will be deleted")]
        public abstract void PipProcessPreserveOutputDirectoryFailedToMakeFilePrivate(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string directory,
            string file);

        [GeneratedEvent(
            (int)LogEventId.PipProcessPreserveOutputDirectorySkipMakeFilesPrivate,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "Output directory '{directory}' is not preserved because /unsafe_IgnorePreserveOutputsPrivatization. This can cause failure in pip execution.")]
        public abstract void PipProcessPreserveOutputDirectorySkipMakeFilesPrivate(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string directory);

        [GeneratedEvent(
            (int)LogEventId.PipProcessChangeAffectedInputsWrittenFileCreationFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "File containing change affected inputs could not be prepared, path '{path}', error code {errorCode:X8}: {message}")]
        public abstract void PipProcessChangeAffectedInputsWrittenFileCreationFailed(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string path,
            int errorCode,
            string message);

        [GeneratedEvent(
            (ushort)LogEventId.FailedToCreateHardlinkOnMerge,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Storage,
            Message = EventConstants.PipPrefix + "Cannot create a hardlink from '{sourceFile}' to '{destinationFile}' when merging outputs to their original location: {failedStatus}")]
        internal abstract void FailedToCreateHardlinkOnMerge(LoggingContext loggingContext, long pipSemiStableHash, string pipDescription, string destinationFile, string sourceFile, string failedStatus);

        [GeneratedEvent(
            (ushort)LogEventId.DisallowedDoubleWriteOnMerge,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Storage,
            Message = EventConstants.PipPrefix + "A disallowed double write was detected when merging '{sourceFile}' to '{destinationFile}'.")]
        internal abstract void DisallowedDoubleWriteOnMerge(LoggingContext loggingContext, long pipSemiStableHash, string pipDescription, string destinationFile, string sourceFile);

        [GeneratedEvent(
            (ushort)LogEventId.DoubleWriteAllowedDueToPolicy,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Storage,
            Message = EventConstants.PipPrefix + "Detected double write in '{destinationFile}' when merging outputs to their original location. The double write is allowed due to configured policy.")]
        internal abstract void DoubleWriteAllowedDueToPolicy(LoggingContext loggingContext, long pipSemiStableHash, string pipDescription, string destinationFile);
        [GeneratedEvent(
            (int)LogEventId.PipProcessStartExternalTool,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "Process execution via external tool '{tool}' starts")]
        public abstract void PipProcessStartExternalTool(LoggingContext context, long pipSemiStableHash, string pipDescription, string tool);

        [GeneratedEvent(
            (int)LogEventId.PipProcessFinishedExternalTool,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "Process execution via external tool finished with the tool's exit code {exitCode}:{stdOut}{stdErr}")]
        public abstract void PipProcessFinishedExternalTool(LoggingContext context, long pipSemiStableHash, string pipDescription, int exitCode, string stdOut, string stdErr);

        [GeneratedEvent(
            (int)LogEventId.PipProcessStartRemoteExecution,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "Remoting process execution via '{tool}' starts")]
        public abstract void PipProcessStartRemoteExecution(LoggingContext context, long pipSemiStableHash, string pipDescription, string tool);

        [GeneratedEvent(
            (int)LogEventId.PipProcessFinishedRemoteExecution,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "Remoting process execution via external tool finished with the tool's exit code {exitCode}:{stdOut}{stdErr}")]
        public abstract void PipProcessFinishedRemoteExecution(LoggingContext context, long pipSemiStableHash, string pipDescription, int exitCode, string stdOut, string stdErr);

        [GeneratedEvent(
            (int)LogEventId.PipProcessStartExternalVm,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "Process execution in VM starts")]
        public abstract void PipProcessStartExternalVm(LoggingContext context, long pipSemiStableHash, string pipDescription);

        [GeneratedEvent(
            (int)LogEventId.PipProcessFinishedExternalVm,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "Process execution in VM finished with VM's command proxy exit code {exitCode}:{stdOut}{stdErr}")]
        public abstract void PipProcessFinishedExternalVm(LoggingContext context, long pipSemiStableHash, string pipDescription, int exitCode, string stdOut, string stdErr);

        [GeneratedEvent(
            (int)LogEventId.PipProcessExternalExecution,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "External execution: {message}")]
        public abstract void PipProcessExternalExecution(LoggingContext context, long pipSemiStableHash, string pipDescription, string message);

        [GeneratedEvent(
            (int)LogEventId.PipProcessNeedsExecuteExternalButExecuteInternal,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "Process needs to be executed externally because (require admin privilege: {requiredAdminPrivilege} | execution mode: {executionMode}), but instead it executes internally because (Win OS: {isWinOS} | listener existence: {existsListener})")]
        public abstract void PipProcessNeedsExecuteExternalButExecuteInternal(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            bool requiredAdminPrivilege,
            string executionMode,
            bool isWinOS,
            bool existsListener);

        [GeneratedEvent(
            (ushort)LogEventId.LogPhaseDuration,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.Diagnostics,
            EventTask = (ushort)Tasks.PipExecutor,
            Message = "[{pipSemiStableHash}] Done with phase '{phaseName}' in {duration}.  {extraInfo}")]
        public abstract void LogPhaseDuration(LoggingContext context, string pipSemiStableHash, string phaseName, string duration, string extraInfo);

        [GeneratedEvent(
            (ushort)LogEventId.CannotDeleteSharedOpaqueOutputFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "[{pipDescription}] Failed to delete shared opaque output files recorded in '{sidebandFile}':{files}.  Reason: {failure}")]
        public abstract void CannotDeleteSharedOpaqueOutputFile(LoggingContext context, string pipDescription, string sidebandFile, string files, string failure);

        [GeneratedEvent(
            (ushort)LogEventId.SharedOpaqueOutputsDeletedLazily,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "[{pipDescription}] Lazily deleted {count} shared opaque output files recorded in '{sidebandFile}':{files}.")]
        public abstract void SharedOpaqueOutputsDeletedLazily(LoggingContext context, string pipDescription, string sidebandFile, string files, int count);

        [GeneratedEvent(
            (ushort)LogEventId.CannotReadSidebandFileError,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Cannot read sideband file '{fileName}': {error}")]
        public abstract void CannotReadSidebandFileError(LoggingContext context, string fileName, string error);

        [GeneratedEvent(
            (ushort)LogEventId.CannotReadSidebandFileWarning,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Cannot read sideband file '{fileName}': {error}")]
        public abstract void CannotReadSidebandFileWarning(LoggingContext context, string fileName, string error);

        [GeneratedEvent(
            (ushort)LogEventId.ResumeOrSuspendProcessError,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "[{pipSemiStableHash}] occurred an error for {failedOperation}: {errorCode}")]
        public abstract void ResumeOrSuspendProcessError(LoggingContext context, string pipSemiStableHash, string failedOperation, int errorCode);

        [GeneratedEvent(
            (int)LogEventId.ResumeOrSuspendException,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            EventTask = (ushort)Tasks.SandboxedProcessExecutor,
            Keywords = (int)((Keywords.UserMessage) | Keywords.Diagnostics),
            Message = "{operation} attempt failed with exception. {exception}")]
        public abstract void ResumeOrSuspendException(LoggingContext context, string operation, string exception);

        [GeneratedEvent(
            (ushort)LogEventId.CannotProbeOutputUnderSharedOpaque,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "[{pipDescription}] Failed to probe '{path}' under a shared opaque directory : {details}")]
        public abstract void CannotProbeOutputUnderSharedOpaque(LoggingContext context, string pipDescription, string path, string details);

        [GeneratedEvent(
            (int)LogEventId.DumpSurvivingPipProcessChildrenStatus,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "Dumping unexpected surviving child processes for Process: '{processName}'. Status: {status}")]
        public abstract void DumpSurvivingPipProcessChildrenStatus(LoggingContext context, string processName, string status);

        [GeneratedEvent(
            (int)LogEventId.ExistenceAssertionUnderOutputDirectoryFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = "[{pipDescription}] The output file '{assertedOutput}' existence was asserted under output directory root '{outputDirectoryRoot}' but the file was not produced by the pip.")]
        public abstract void ExistenceAssertionUnderOutputDirectoryFailed(LoggingContext context, string pipDescription, string assertedOutput, string outputDirectoryRoot);

        [GeneratedEvent(
            (int)LogEventId.SandboxedProcessResultLogOutputTimeout,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = "[{pipSemiStableHash}] Logging process StandardOutput/StandardError timed out after exceeding '{timeoutInMinutes}' minutes. This may be caused by the default terminal being Windows Terminal, workaround this by switching the default terminal to 'Windows Console Host' in Windows settings or Windows Terminal settings. Output streams may be incomplete due to this error.")]
        public abstract void SandboxedProcessResultLogOutputTimeout(LoggingContext context, string pipSemiStableHash, int timeoutInMinutes);

        [GeneratedEvent(
            (int)LogEventId.LinuxSandboxReportedBinaryRequiringPTrace,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.PipExecutor,
            Message = "[{pipDescription}] The following processes '{exePath}' require ptrace and their file accesses may not be reported by the sandbox.")]
        public abstract void LinuxSandboxReportedBinaryRequiringPTrace(LoggingContext context, string pipDescription, string exePath);

        [GeneratedEvent(
            (int)LogEventId.PTraceSandboxLaunchedForPip,
            EventLevel = Level.Verbose,
            EventGenerators = EventGenerators.LocalOnly,
            Keywords = (int)((Keywords.UserMessage) | Keywords.Diagnostics),
            EventTask = (int)Tasks.PipExecutor,
            Message = "[{pipDescription}] Ptrace sandbox was launched for the following processes '{exePath}'.")]
        public abstract void PTraceSandboxLaunchedForPip(LoggingContext context, string pipDescription, string exePath);

        [GeneratedEvent(
            (int)LogEventId.ProcessBreakaway,
            EventLevel = Level.Verbose,
            EventGenerators = EventGenerators.LocalOnly,
            Keywords = (int)((Keywords.UserMessage) | Keywords.Diagnostics),
            EventTask = (int)Tasks.PipExecutor,
            Message = "[{pipDescription}] Process {pid} with path '{exePath}' breaks away from the sandbox.")]
        public abstract void ProcessBreakaway(LoggingContext context, string pipDescription, string exePath, uint pid);

        [GeneratedEvent(
            (ushort)LogEventId.PTraceRunnerError,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Scheduler,
            Message = "[{pipDescription}] PTraceRunner logged the following error: {content}")]
        internal abstract void PTraceRunnerError(LoggingContext loggingContext, string pipDescription, string content);

        [GeneratedEvent(
            (ushort)LogEventId.SandboxInternalError,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)(Keywords.UserMessage | Keywords.InfrastructureIssue),
            EventTask = (ushort)Tasks.PipExecutor,
            Message = EventConstants.PipPrefix + "Pip failed with an internal sandbox error and may be retried. See {ShortProductName} log for more details.")]
        internal abstract void SandboxInternalError(LoggingContext loggingContext, long pipSemiStableHash, string pipDescription);

        [GeneratedEvent(
            (ushort)LogEventId.SandboxInternalWarningMessage,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.PipExecutor,
            Message = "[{pipDescription}] BuildXL sandbox warning: {content}.")]
        internal abstract void SandboxInternalWarningMessage(LoggingContext loggingContext, string pipDescription, string content);

        [GeneratedEvent(
            (ushort)LogEventId.FullSandboxInternalErrorMessage,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.PipExecutor,
            Message = "[{pipDescription}] BuildXL sandbox error: {content}")]
        internal abstract void FullSandboxInternalErrorMessage(LoggingContext loggingContext, string pipDescription, string content);

        [GeneratedEvent(
            (ushort)LogEventId.ReportArgsMismatch,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Scheduler,
            Message = "[{pipDescription}] Received ProcessCommandLine report without a matching ProcessStart report for pid '{pid}'.")]
        internal abstract void ReportArgsMismatch(LoggingContext loggingContext, string pipDescription, string pid);

        [GeneratedEvent(
            (ushort)LogEventId.ReceivedReportFromUnknownPid,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Scheduler,
            Message = "[{pipDescription}] Received report from unknown pid: {pid}")]
        internal abstract void ReceivedReportFromUnknownPid(LoggingContext loggingContext, string pipDescription, string pid);

        [GeneratedEvent(
            (ushort)LogEventId.ReceivedFileAccessReportBeforeSemaphoreInit,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Scheduler,
            Message = "The following file access occurred before the BxlObserver was able to complete initialization '{path}'")]
        internal abstract void ReceivedFileAccessReportBeforeSemaphoreInit(LoggingContext loggingContext, string path);

        [GeneratedEvent(
            (ushort)LogEventId.EnvironmentPreparationError,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Plugin,
            Message = "Could not prepare environment variables. Error: {error}")]
        public abstract void EnvironmentPreparationFailed(LoggingContext logging, string error);

        [GeneratedEvent(
            (ushort)LogEventId.PathTooLongIsIgnored,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (ushort)Tasks.Scheduler,
            Message = "[{pipDescription}] The pip tried to access a path longer than 4096 bytes, which exceeds the limit supported by BuildXL. The access will be ignored. Path: '{path}'")]
        public abstract void PathTooLongIsIgnored(LoggingContext logging, string pipDescription, string path);

        [GeneratedEvent(
            (ushort)LogEventId.EBPFIsStillBeingInitialized,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)(Keywords.UserMessage | Keywords.Overwritable),
            EventTask = (int)Tasks.Engine,
            Message = "EBPF is still being initialized..")]
        public abstract void EBPFIsStillBeingInitialized(LoggingContext context);

        [GeneratedEvent(
            (int)LogEventId.SynchronouslyWaitedForEBPF,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "Synchronously waited {0}ms for EBPF to finish initializing. {1}ms of EBPF initialization overlapped other processing")]
        public abstract void SynchronouslyWaitedForEBPF(LoggingContext context, int waitTimeMs, int overlappedTimeMs);

        [GeneratedEvent(
            (int)LogEventId.EBPFDisposed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Keywords.UserMessage,
            EventTask = (int)Tasks.Engine,
            Message = "EBPF disposed: {info}")]
        public abstract void EBPFDisposed(LoggingContext context, string info);
    }
}
