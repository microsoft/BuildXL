// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Tracing;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;

#pragma warning disable 1591

namespace BuildXL.Processes.Tracing
{
    /// <summary>
    /// Logging
    /// </summary>
    [EventKeywordsType(typeof(Events.Keywords))]
    [EventTasksType(typeof(Events.Tasks))]
    internal abstract partial class Logger
    {
        /// <summary>
        /// Returns the logger instance
        /// </summary>
        public static Logger Log => m_log;

        [GeneratedEvent(
            (int)EventId.PipProcessFileAccess,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipPrefix + "File access on '{3}' with {2}")]
        public abstract void PipProcessFileAccess(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string fileAccessDescription,
            string path);

        [GeneratedEvent(
            (int)EventId.PipFailSymlinkCreation,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipPrefix + "Process created a symlink at '{2}'. Symlink outputs are not currently supported. This error was introduced by /FailSymlinkCreationflag.")]
        public abstract void PipFailSymlinkCreation(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string path);

        [GeneratedEvent(
            (int)EventId.PipInvalidDetoursDebugFlag1,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = "A debug {ShortProductName} is using a non-debug DetoursServices.dll.")]
        public abstract void PipInvalidDetoursDebugFlag1(LoggingContext context);

        [GeneratedEvent(
            (int)EventId.PipInvalidDetoursDebugFlag2,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = "A non-debug {ShortProductName} is using a debug DetoursServices.dll.")]
        public abstract void PipInvalidDetoursDebugFlag2(LoggingContext context);

        [GeneratedEvent(
            (int)EventId.PipProcessStartFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipPrefix + "Process start failed with error code {2:X8}: {3}")]
        public abstract void PipProcessStartFailed(LoggingContext context, long pipSemiStableHash, string pipDescription, int errorCode, string message);

        [GeneratedEvent(
            (int)EventId.PipProcessFinished,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.Diagnostics,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipPrefix + "Process exited cleanly with exit code {2}")]
        public abstract void PipProcessFinished(LoggingContext context, long pipSemiStableHash, string pipDescription, int exitCode);

        [GeneratedEvent(
            (int)EventId.PipProcessFinishedFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.Diagnostics,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipPrefix + "Process failed with exit code {2}")]
        public abstract void PipProcessFinishedFailed(LoggingContext context, long pipSemiStableHash, string pipDescription, int exitCode);

        [GeneratedEvent(
            (int)EventId.PipProcessMessageParsingError,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipPrefix + "Process failed with message parsing error: {2}.")]
        public abstract void PipProcessMessageParsingError(LoggingContext context, long pipSemiStableHash, string pipDescription, string error);

        [GeneratedEvent(
            (int)EventId.PipProcessFinishedDetourFailures,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipPrefix + "Failed to instrument one or more processes")]
        public abstract void PipProcessFinishedDetourFailures(LoggingContext context, long pipSemiStableHash, string pipDescription);

        [GeneratedEvent(
            (int)EventId.PipProcessDisallowedTempFileAccess,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message =
                Events.PipPrefix + "Disallowed file access in temp directory was blocked on '{3}' with {2}; declare that this pip needs a temp directory.")]
        public abstract void PipProcessDisallowedTempFileAccess(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string fileAccessDescription,
            string path);

        [GeneratedEvent(
            (int)EventId.PipProcessTempDirectoryTooLong,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message =
                Events.PipPrefix + "Temp directory too long: '{2}'")]
        public abstract void PipProcessTempDirectoryTooLong(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string directory);

        [GeneratedEvent(
            (int)EventId.PipOutputNotAccessed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message =
                Events.PipPrefix + "No file access for output: {2}. Detours discovered inconsistency in detouring some child processes. Information about the inconsistency can be found in the BuildXL log file. Please, restart the build...")]
        public abstract void PipOutputNotAccessed(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string outputFileName);

        [GeneratedEvent(
            (int)EventId.PipProcessDisallowedFileAccess,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipSpecPrefix + " - Disallowed file access was detected on '{5}' with {4}.")]
        public abstract void PipProcessDisallowedFileAccess(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string pipSpecPath,
            string pipWorkingDirectory,
            string fileAccessDescription,
            string path);

        [GeneratedEvent(
            (int)EventId.PipProcessDisallowedNtCreateFileAccessWarning,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipSpecPrefix + " - Disallowed NtCreateFile access was detected on '{5}' with {4}. " +
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
            (int)EventId.PipProcessTookTooLongWarning,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message =
                Events.PipPrefix + "Process ran for {2}ms, which is longer than the warning timeout of {3}ms; the process will be terminated if it ever runs longer than {4}ms")]
        public abstract void PipProcessTookTooLongWarning(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            long actual,
            long softMax,
            long hardMax);

        [GeneratedEvent(
            (int)EventId.PipProcessTookTooLongError,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipPrefix + "Process terminated because it took too long: {2}ms; the timeout is set to {3}ms. {4}")]
        public abstract void PipProcessTookTooLongError(LoggingContext context, long pipSemiStableHash, string pipDescription, long actual, long time, string dumpDetails);

        [GeneratedEvent(
            (int)EventId.PipProcessStandardOutput,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipPrefix + "Process standard output at '{2}'")]
        public abstract void PipProcessStandardOutput(LoggingContext context, long pipSemiStableHash, string pipDescription, string path);

        [GeneratedEvent(
            (int)EventId.PipProcessStandardError,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipPrefix + "Process standard error at '{2}'")]
        public abstract void PipProcessStandardError(LoggingContext context, long pipSemiStableHash, string pipDescription, string path);

        [GeneratedEvent(
            (int)EventId.PipProcessFileAccessTableEntry,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipPrefix + "File access table entry '{2}'")]
        public abstract void PipProcessFileAccessTableEntry(LoggingContext context, long pipSemiStableHash, string pipDescription, string value);

        [GeneratedEvent(
            (int)EventId.PipProcessFailedToParsePathOfFileAccess,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipPrefix + "Access to the following path will be ignored, since the path could not be parsed: '{3}' (Accessed via {2})")]
        public abstract void PipProcessFailedToParsePathOfFileAccess(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string operation,
            string path);

        [GeneratedEvent(
            (int)EventId.PipProcessIgnoringPathOfSpecialDeviceFileAccess,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipPrefix + "Access to the following path will be ignored, since the path is a path to a device: '{3}' (Accessed via {2})")]
        public abstract void PipProcessIgnoringPathOfSpecialDeviceFileAccess(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string operation,
            string path);

        [GeneratedEvent(
            (int)EventId.PipProcessIgnoringPathWithWildcardsFileAccess,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipPrefix + "Access to the following path will be ignored, since the path contains wildcard characters: '{3}' (Accessed via {2})")]
        public abstract void PipProcessIgnoringPathWithWildcardsFileAccess(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string operation,
            string path);

        [GeneratedEvent(
            (int)EventId.PipProcessDisallowedFileAccessWhitelistedNonCacheable,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message =
                Events.PipPrefix +
                "Disallowed file access (non-cacheable) was detected on '{3}' with {2}. This message will become an error if the whitelist entry (in a top-level configuration file) allowing this access is removed.")]
        public abstract void PipProcessDisallowedFileAccessWhitelistedNonCacheable(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string fileAccessDescription,
            string path);

        [GeneratedEvent(
            (int)EventId.PipProcessDisallowedFileAccessWhitelistedCacheable,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message =
                Events.PipPrefix +
                "Disallowed file access (cacheable) was detected on '{3}' with {2}. This message will become an error if the whitelist entry (in a top-level configuration file) allowing this access is removed.")]
        public abstract void PipProcessDisallowedFileAccessWhitelistedCacheable(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string fileAccessDescription,
            string path);

        [GeneratedEvent(
            (int)EventId.FileAccessWhitelistFailedToParsePath,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message =
                Events.PipPrefix +
                "Tool path '{3}' failed to parse at character '{4}' could not be parsed. File access whitelist entries matching on tool paths will not be checked for this access. (Accessed via {2})")]
        public abstract void FileAccessWhitelistFailedToParsePath(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string fileAccessDescription,
            string path,
            int characterWithError);

        [GeneratedEvent(
            (int)EventId.PipProcessUncacheableWhitelistNotAllowedInDistributedBuilds,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.PipExecutor,
            Message =
                Events.PipPrefix +
                "Disallowed file access (non-cacheable) was detected on '{3}' with {2}. This message is an error because non-cacheable whitelist matches are not allowed in distributed builds.")]
        public abstract void PipProcessUncacheableWhitelistNotAllowedInDistributedBuilds(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string fileAccessDescription,
            string path);

        [GeneratedEvent(
            (int)EventId.Process,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipPrefix + "Process with id {2} at '{3}'")]
        public abstract void PipProcess(LoggingContext context, long pipSemiStableHash, string pipDescription, uint id, string path);

        [GeneratedEvent(
            (int)EventId.BrokeredDetoursInjectionFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = "Failed to instrument process ID {0} for file monitoring on behalf of an existing instrumented process, error: {1}. Most likely reason for this error is the run time for the process exceeded the allowed timeout for the process to complete.")]
        public abstract void BrokeredDetoursInjectionFailed(LoggingContext context, uint processId, string error);

        [GeneratedEvent(
            (int)EventId.LogDetoursDebugMessage,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipPrefix + "Detours Debug Message: {2}")]
        public abstract void LogDetoursDebugMessage(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string message);

        [GeneratedEvent(
            (int)EventId.LogMacKextFailure,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipPrefix + "{message}")]
        public abstract void LogMacKextFailure(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string message);

        [GeneratedEvent(
            (int)EventId.LogAppleSandboxPolicyGenerated,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipPrefix + "Apple sandbox-exec policy for pip generated: {policyFilePath}")]
        public abstract void LogAppleSandboxPolicyGenerated(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string policyFilePath);

        [GeneratedEvent(
            (int)EventId.LogDetoursMaxHeapSize,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.Diagnostics),
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipPrefix + "Maximum detours heap size for process in the pip is {maxDetoursHeapSizeInBytes} bytes. The processName '{processName}'. The processId is: {processId}. The manifestSize in bytes is: {manifestSizeInBytes}. The finalDetoursHeapSize in bytes is: {finalDetoursHeapSizeInBytes}. The allocatedPoolEntries is: {allocatedPoolEntries}. The maxHandleMapEntries is: {maxHandleMapEntries}. The handleMapEntries is: {handleMapEntries}.")]
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
            (int)EventId.LogInternalDetoursErrorFileNotEmpty,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipPrefix + "Detoured process emitted failure information that could not be transmitted back to {ShortProductName}. Diagnostic file content: {2}")]
        public abstract void LogInternalDetoursErrorFileNotEmpty(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string message);

        [GeneratedEvent(
            (int)EventId.LogFailedToCreateDirectoryForInternalDetoursFailureFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipPrefix + "Failed to create directory for the internal Detours error file. Path: {path}. Error: {message}")]
        public abstract void LogFailedToCreateDirectoryForInternalDetoursFailureFile(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string path,
            string message);

        [GeneratedEvent(
            (int)EventId.LogGettingInternalDetoursErrorFile,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipPrefix + "Failed checking for detours backup communication file existence. Pip will be treated as a failure. Error: {message}.")]
        public abstract void LogGettingInternalDetoursErrorFile(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string message);

        [GeneratedEvent(
            (int)EventId.LogMismatchedDetoursVerboseCount,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipPrefix + "The number of messages sent by detoured processes did not match the number received by the {MainExecutableName} process. LastMessageCount:{lastMessageCount}")]
        public abstract void LogMismatchedDetoursVerboseCount(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            int lastMessageCount);

        [GeneratedEvent(
            (int)EventId.LogMessageCountSemaphoreExists,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipPrefix + "Semaphore for counting Detours messages is already opened.")]
        public abstract void LogMessageCountSemaphoreExists(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription);

        [GeneratedEvent(
            (int)EventId.PipProcessCommandLineTooLong,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipPrefix + "Process command line is longer than {3} characters: {2}")]
        public abstract void PipProcessCommandLineTooLong(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string commandLine,
            int maxLength);

        [GeneratedEvent(
            (int)EventId.PipProcessInvalidWarningRegex,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipPrefix + "Process warning regular expression is invalid, pattern is {2}, options are {3}")]
        public abstract void PipProcessInvalidWarningRegex(LoggingContext context, long pipSemiStableHash, string pipDescription, string pattern, string options);

        [GeneratedEvent(
            (int)EventId.PipProcessInvalidErrorRegex,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipPrefix + "Process error regular expression is invalid, pattern is {2}, options are {3}")]
        public abstract void PipProcessInvalidErrorRegex(LoggingContext context, long pipSemiStableHash, string pipDescription, string pattern, string options);

        [GeneratedEvent(
            (int)EventId.PipProcessChildrenSurvivedError,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipPrefix + "Unexpected child processes survived: {2} process(es){3}")]
        public abstract void PipProcessChildrenSurvivedError(LoggingContext context, long pipSemiStableHash, string pipDescription, int count,  string paths);

        [GeneratedEvent(
            (int)EventId.PipProcessChildrenSurvivedTooMany,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipPrefix + "Too many child processes survived: {2} process(es){3}")]
        public abstract void PipProcessChildrenSurvivedTooMany(LoggingContext context, long pipSemiStableHash, string pipDescription, int count, string paths);

        [GeneratedEvent(
            (int)EventId.PipProcessChildrenSurvivedKilled,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipPrefix + "Process and/or job containing child processes killed")]
        public abstract void PipProcessChildrenSurvivedKilled(LoggingContext context, long pipSemiStableHash, string pipDescription);

        [GeneratedEvent(
            (int)EventId.PipProcessMissingExpectedOutputOnCleanExit,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipSpecPrefix + "Process was expected to write an output file at '{4}', but that file is not present.")]
        public abstract void PipProcessMissingExpectedOutputOnCleanExit(LoggingContext context, long pipSemiStableHash, string pipDescription, string pipSpecPath, string pipWorkingDirectory, string path);

        [GeneratedEvent(
          (int)EventId.PipProcessExpectedMissingOutputs,
          EventGenerators = EventGenerators.LocalOnly,
          EventLevel = Level.Error,
          Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
          EventTask = (int)Events.Tasks.PipExecutor,
          Message = Events.PipPrefix + "- Process was expected to write the following output files, but those files are not present.:\r\n{2}")]
        public abstract void PipProcessExpectedMissingOutputs(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string paths);

        [GeneratedEvent(
            (int)EventId.PipProcessOutputPreparationFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.InfrastructureError),
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipSpecPrefix + "Process output directories could not be prepared, path '{2}', error code {3:X8}: {4}")]
        public abstract void PipProcessOutputPreparationFailed(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string path,
            int errorCode,
            string message,
            string exception);

        [GeneratedEvent(
            (int)EventId.PipStandardIOFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipPrefix + "Process standard I/O failure at path '{2}', error code {3:X8}: {4}")]
        public abstract void PipStandardIOFailed(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string path,
            int errorCode,
            string message);

        [GeneratedEvent(
            (int)EventId.PipExitedUncleanly,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipPrefix + "Pip had unclean exit. Canceled: {canceled}, Error code {errorCode}, Killed: {killed}, # Surviving child errors: {numSurvivingChildErrors}")]
        public abstract void PipExitedUncleanly(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            bool canceled,
            int errorCode,
            bool killed,
            int numSurvivingChildErrors);

        [GeneratedEvent(
            (int)EventId.PipProcessStandardInputException,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipSpecPrefix + " Unexpected standard input exception: {4}")]
        public abstract void PipProcessStandardInputException(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string pipSpecPath,
            string pipWorkingDirectory,
            string exceptionMessage);

        [GeneratedEvent(
            (int)EventId.PipProcessToolErrorDueToHandleToFileBeingUsed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipSpecPrefix + "The tool '{toolName}' cannot access the file '{file}' because it is being used by another process: \r\n{reason}")]
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
            (int)EventId.PipProcessError,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)(Events.Keywords.UserMessage | Events.Keywords.UserError),
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipSpecPrefix + " - failed with exit code {7}{8}\r\n{5}\r\n{6}")]
        public abstract void PipProcessError(
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
            string pathsToLog,
            int exitCode,
            string optionalMessage);

        [GeneratedEvent(
            (int)EventId.PipProcessWarning,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipSpecPrefix + " - warnings\r\n{5}\r\n{6}")]
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
            string pathsToLog);

        [GeneratedEvent(
            (int)EventId.PipProcessOutput,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipSpecPrefix + "\r\n{4}")]
        public abstract void PipProcessOutput(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string pipSpecPath,
            string pipWorkingDirectory,
            string outputToLog);

        [GeneratedEvent(
            (ushort)EventId.PipTempDirectoryCleanupError,
            EventLevel = Level.Error,
            EventGenerators = EventGenerators.LocalOnly,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipPrefix + "Failed to clean temp directory at '{2}'. Pip will not be executed. Reason: {3}")]
        public abstract void PipTempDirectoryCleanupError(LoggingContext context, long pipSemiStableHash, string pipDescription, string directory, string exceptionMessage);

        [GeneratedEvent(
            (ushort)EventId.PipTempDirectorySetupError,
            EventLevel = Level.Error,
            EventGenerators = EventGenerators.LocalOnly,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = "Failed to create temp directory at '{0}'. Reason: {1}")]
        public abstract void PipTempDirectorySetupError(LoggingContext context, string directory, string exceptionMessage);

        [GeneratedEvent(
            (ushort)EventId.PipFailedToCreateDumpFile,
            EventLevel = Level.Warning,
            EventGenerators = EventGenerators.LocalOnly,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipPrefix + "Failed to create dump for timed out process. {exceptionMessage}")]
        public abstract void PipFailedToCreateDumpFile(LoggingContext context, long pipSemiStableHash, string pipDescription, string exceptionMessage);

        [GeneratedEvent(
            (ushort)EventId.RetryStartPipDueToErrorPartialCopyDuringDetours,
            EventLevel = Level.Verbose,
            EventGenerators = EventGenerators.LocalOnly,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipSpecPrefix + "Retry to start pip for {retryNumber} time(s) due to the following error: {error}")]
        public abstract void RetryStartPipDueToErrorPartialCopyDuringDetours(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            int error,
            int retryNumber);

        [GeneratedEvent(
            (int)EventId.DuplicateWindowsEnvironmentVariableEncountered,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.Engine,
            Message = "Unexpected duplicate environment variable encountered. Variable '{0}' has already been defined with value '{1}'. The other occurrence with value '{2}' will be ignored.")]
        public abstract void DuplicateWindowsEnvironmentVariableEncountered(
            LoggingContext context,
            string key,
            string existingValue,
            string ignoredValue);

        [GeneratedEvent(
            (int)EventId.ReadWriteFileAccessConvertedToReadMessage,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipPrefix + "File access on file '{3}' requested with Read/Write but granted for Read only by process with ID: {2}.")]
        public abstract void ReadWriteFileAccessConvertedToReadMessage(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            uint processId,
            string path);

        [GeneratedEvent(
            (int)EventId.ReadWriteFileAccessConvertedToReadWarning,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipPrefix + "This pip might have failed because of converting Read/Write file access to a Read file access. Examine the execution log for information on which files the Read/Write access request was converted to Read access request.")]
        public abstract void ReadWriteFileAccessConvertedToReadWarning(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription);

        [GeneratedEvent(
            (int)EventId.PipProcessResponseFileCreationFailed,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipSpecPrefix + "Process response file could not be prepared, path '{2}', error code {3:X8}: {4}")]
        public abstract void PipProcessResponseFileCreationFailed(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string path,
            int errorCode,
            string message);

        [GeneratedEvent(
            (ushort)EventId.FailedToMergeOutputsToOriginalLocation,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Storage,
            Message = Events.PipPrefix + "Pip completed successfully, but it ran in a container and its outputs could not be merged back to their original locations. {details}")]
        internal abstract void FailedToMergeOutputsToOriginalLocation(LoggingContext loggingContext, long pipSemiStableHash, string pipDescription, string details);

        [GeneratedEvent(
            (ushort)EventId.FailedToCreateHardlinkOnMerge,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Storage,
            Message = Events.PipPrefix + "Cannot create a hardlink from '{sourceFile}' to '{destinationFile}' when merging outputs to their original location: {failedStatus}")]
        internal abstract void FailedToCreateHardlinkOnMerge(LoggingContext loggingContext, long pipSemiStableHash, string pipDescription, string destinationFile, string sourceFile, string failedStatus);

        [GeneratedEvent(
            (ushort)EventId.DisallowedDoubleWriteOnMerge,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Storage,
            Message = Events.PipPrefix + "A disallowed double write was detected when merging '{sourceFile}' to '{destinationFile}'.")]
        internal abstract void DisallowedDoubleWriteOnMerge(LoggingContext loggingContext, long pipSemiStableHash, string pipDescription, string destinationFile, string sourceFile);

        [GeneratedEvent(
            (ushort)EventId.DoubleWriteAllowedDueToPolicy,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Storage,
            Message = Events.PipPrefix + "Detected double write in '{destinationFile}' when merging outputs to their original location. The double write is allowed due to configured policy.")]
        internal abstract void DoubleWriteAllowedDueToPolicy(LoggingContext loggingContext, long pipSemiStableHash, string pipDescription, string destinationFile);

        [GeneratedEvent(
            (ushort)EventId.FailedToCleanUpContainer,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Warning,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Storage,
            Message = "Cleaning up container for job object {jobObject} failed. {details}")]
        internal abstract void FailedToCleanUpContainer(LoggingContext loggingContext, string jobObject, string details);

        [GeneratedEvent(
            (ushort)EventId.WarningSettingUpContainer,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Informational,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (ushort)Events.Tasks.Storage,
            Message = "A warning occurred when setting up a container for job object {jobObject}: {warning}")]
        internal abstract void WarningSettingUpContainer(LoggingContext loggingContext, string jobObject, string warning);

        [GeneratedEvent(
            (int)EventId.PipInContainerStarted,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipPrefix + "Process started to run in a container succesfully.")]
        public abstract void PipInContainerStarted(LoggingContext context, long pipSemiStableHash, string pipDescription);

        [GeneratedEvent(
            (int)EventId.PipInContainerStarting,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipPrefix + "Process is about to run in a container. Remapping information: \n {remappingInfo}")]
        public abstract void PipInContainerStarting(LoggingContext context, long pipSemiStableHash, string pipDescription, string remappingInfo);

        [GeneratedEvent(
            (int)EventId.PipSpecifiedToRunInContainerButIsolationIsNotSupported,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Error,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipPrefix + "Process was specified to run in a container, but this capability is not available on this machine.")]
        public abstract void PipSpecifiedToRunInContainerButIsolationIsNotSupported(LoggingContext context, long pipSemiStableHash, string pipDescription);

        [GeneratedEvent(
            (int) EventId.PipProcessStartExternalTool,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipPrefix + "Process execution via external tool '{tool}' starts")]
        public abstract void PipProcessStartExternalTool(LoggingContext context, long pipSemiStableHash, string pipDescription, string tool);

        [GeneratedEvent(
            (int)EventId.PipProcessFinishedExternalTool,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipPrefix + "Process execution via external tool finished with the tool's exit code {exitCode}:{stdOut}{stdErr}")]
        public abstract void PipProcessFinishedExternalTool(LoggingContext context, long pipSemiStableHash, string pipDescription, int exitCode, string stdOut, string stdErr);

        [GeneratedEvent(
            (int)EventId.PipProcessStartExternalVm,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipPrefix + "Process execution in VM starts")]
        public abstract void PipProcessStartExternalVm(LoggingContext context, long pipSemiStableHash, string pipDescription);

        [GeneratedEvent(
            (int)EventId.PipProcessFinishedExternalVm,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipPrefix + "Process execution in VM finished with VM's command proxy exit code {exitCode}:{stdOut}{stdErr}")]
        public abstract void PipProcessFinishedExternalVm(LoggingContext context, long pipSemiStableHash, string pipDescription, int exitCode, string stdOut, string stdErr);

        [GeneratedEvent(
            (int)EventId.PipProcessExternalExecution,
            EventGenerators = EventGenerators.LocalOnly,
            EventLevel = Level.Verbose,
            Keywords = (int)Events.Keywords.UserMessage,
            EventTask = (int)Events.Tasks.PipExecutor,
            Message = Events.PipPrefix + "External execution: {message}")]
        public abstract void PipProcessExternalExecution(LoggingContext context, long pipSemiStableHash, string pipDescription, string message);
    }
}
