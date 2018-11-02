// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;

#pragma warning disable 1591

namespace BuildXL.Processes.Tracing
{
    /// <summary>
    /// Logging for Processes
    /// </summary>
    internal class Logger
    {
        /// <summary>
        /// Returns the logger instance
        /// </summary>
        public static Logger Log { get; } = new Logger();

        public void PipProcessFileAccess(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string fileAccessDescription,
            string path)
        {
            context.SpecifyVerboseWasLogged((int)EventId.PipProcessFileAccess);
            if (LogEventLevel.Verbose <= context.MaximumLevelToLog)
            {
                Console.WriteLine(
                    Events.PipPrefix + "File access on '{3}' with {2}",
                    pipSemiStableHash,
                    pipDescription,
                    fileAccessDescription,
                    path);
            }
        }

        public void PipInvalidDetoursDebugFlag1(LoggingContext context)
        {
            context.SpecifyErrorWasLogged((int)EventId.PipInvalidDetoursDebugFlag1);
            if (LogEventLevel.Error <= context.MaximumLevelToLog)
            {
                Console.Error.WriteLine($"A debug {Branding.ShortProductName} is using a non-debug DetoursServices.dll.");
            }
        }

        public void PipInvalidDetoursDebugFlag2(LoggingContext context)
        {
            context.SpecifyErrorWasLogged((int)EventId.PipInvalidDetoursDebugFlag1);
            if (LogEventLevel.Error <= context.MaximumLevelToLog)
            {
                Console.Error.WriteLine($"A non-debug {Branding.ShortProductName} is using a debug DetoursServices.dll.");
            }
        }

        public void PipProcessStartFailed(LoggingContext context, long pipSemiStableHash, string pipDescription, int errorCode, string message)
        {
            context.SpecifyErrorWasLogged((int)EventId.PipProcessStartFailed);
            if (LogEventLevel.Error <= context.MaximumLevelToLog)
            {
                Console.Error.WriteLine(Events.PipPrefix + "Process start failed with error code {2:X8}: {3}", pipSemiStableHash, pipDescription, errorCode, message);
            }
        }

        public void PipProcessFinished(LoggingContext context, long pipSemiStableHash, string pipDescription, int exitCode)
        {
            context.SpecifyVerboseWasLogged((int)EventId.PipProcessFinished);
            if (LogEventLevel.Verbose <= context.MaximumLevelToLog)
            {
                Console.WriteLine(Events.PipPrefix + "Process exited cleanly with exit code {2}", pipSemiStableHash, pipDescription, exitCode);
            }
        }

        public void PipProcessFinishedFailed(LoggingContext context, long pipSemiStableHash, string pipDescription, int exitCode)
        {
            context.SpecifyVerboseWasLogged((int)EventId.PipProcessFinishedFailed);
            if (LogEventLevel.Verbose <= context.MaximumLevelToLog)
            {
                Console.WriteLine(Events.PipPrefix + "Process failed with exit code {2}", pipSemiStableHash, pipDescription, exitCode);
            }
        }

        public void PipProcessMessageParsingError(LoggingContext context, long pipSemiStableHash, string pipDescription, string error)
        {
            context.SpecifyErrorWasLogged((int)EventId.PipProcessMessageParsingError);
            if (LogEventLevel.Error <= context.MaximumLevelToLog)
            {
                Console.Error.WriteLine(Events.PipPrefix + "Process failed with message parsing error: {2}.", pipSemiStableHash, pipDescription, error);
            }
        }

        public void PipProcessFinishedDetourFailures(LoggingContext context, long pipSemiStableHash, string pipDescription)
        {
            context.SpecifyErrorWasLogged((int)EventId.PipProcessFinishedDetourFailures);
            if (LogEventLevel.Error <= context.MaximumLevelToLog)
            {
                Console.Error.WriteLine(Events.PipPrefix + "Failed to instrument one or more processes", pipSemiStableHash, pipDescription);
            }
        }
    
        public void PipProcessDisallowedTempFileAccess(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string fileAccessDescription,
            string path)
        {
            context.SpecifyVerboseWasLogged((int)EventId.PipProcessDisallowedTempFileAccess);
            if (LogEventLevel.Verbose <= context.MaximumLevelToLog)
            {
                Console.WriteLine(Events.PipPrefix + "Disallowed file access in temp directory was blocked on '{3}' with {2}; call PipBuilder.PrepareTempDirectory in the transformer code to enable access to the temp directory.",
                pipSemiStableHash,
                pipDescription,
                fileAccessDescription,
                path);
            }
        }

        public void PipProcessTempDirectoryTooLong(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string directory)
        {
            context.SpecifyErrorWasLogged((int)EventId.PipProcessTempDirectoryTooLong);
            if (LogEventLevel.Error <= context.MaximumLevelToLog)
            {
                Console.Error.WriteLine(Events.PipPrefix + "Temp directory too long: '{2}'", 
                pipSemiStableHash,
                pipDescription,
                directory);
            }
        }

        public void PipOutputNotAccessed(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string outputFileName)
        {
            context.SpecifyVerboseWasLogged((int)EventId.PipOutputNotAccessed);
            if (LogEventLevel.Verbose <= context.MaximumLevelToLog)
            {
                Console.WriteLine(Events.PipPrefix + "No file access for output: {2}. Detours discovered inconsistency in detouring some child processes. Information about the inconsistency can be found in the log file. Please, restart the build...",
                pipSemiStableHash,
                pipDescription,
                outputFileName);
            }
        }

        public void PipProcessDisallowedFileAccess(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string pipSpecPath,
            string pipWorkingDirectory,
            string fileAccessDescription,
            string path)
        {
            context.SpecifyVerboseWasLogged((int)EventId.PipProcessDisallowedFileAccess);
            if (LogEventLevel.Verbose <= context.MaximumLevelToLog)
            {
                Console.WriteLine(Events.PipSpecPrefix + " - Disallowed file access was detected on '{5}' with {4}.", 
                pipSemiStableHash,
                pipDescription,
                pipSpecPath,
                pipWorkingDirectory,
                fileAccessDescription,
                path);
            }
        }

        public void PipProcessDisallowedNtCreateFileAccessWarning(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string pipSpecPath,
            string pipWorkingDirectory,
            string fileAccessDescription,
            string path)
        {
            context.SpecifyWarningWasLogged((int)EventId.PipProcessDisallowedNtCreateFileAccessWarning);
            if (LogEventLevel.Warning <= context.MaximumLevelToLog)
            {
                Console.WriteLine(Events.PipSpecPrefix + " - Disallowed NtCreateFile access was detected on '{5}' with {4}. " +
                "This warning will become an error if the '/unsafe_ignoreNtCreateFile+' is removed.",
                pipSemiStableHash,
                pipDescription,
                pipSpecPath,
                pipWorkingDirectory,
                fileAccessDescription,
                path);
            }
        }

        public void PipProcessTookTooLongWarning(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            long actual,
            long softMax,
            long hardMax)
        {
            context.SpecifyWarningWasLogged((int)EventId.PipProcessTookTooLongWarning);
            if (LogEventLevel.Warning <= context.MaximumLevelToLog)
            {
                Console.WriteLine(Events.PipPrefix + "Process ran for {2}ms, which is longer than the warning timeout of {3}ms; the process will be terminated if it ever runs longer than {4}ms",
                pipSemiStableHash,
                pipDescription,
                actual,
                softMax,
                hardMax);
            }
        }

        public void PipProcessTookTooLongError(LoggingContext context, long pipSemiStableHash, string pipDescription, long actual, long time, string dumpDetails)
        {
            context.SpecifyErrorWasLogged((int)EventId.PipProcessTookTooLongError);
            if (LogEventLevel.Error <= context.MaximumLevelToLog)
            {
                Console.Error.WriteLine(
                    Events.PipPrefix + "Process terminated because it took too long: {2}ms; the timeout is set to {3}ms. {4}",
                    pipSemiStableHash,
                    pipDescription,
                    actual,
                    time,
                    dumpDetails);
            }
        }

        public void PipProcessStandardOutput(LoggingContext context, long pipSemiStableHash, string pipDescription, string path)
        {
            context.SpecifyVerboseWasLogged((int)EventId.PipProcessStandardOutput);
            if (LogEventLevel.Verbose <= context.MaximumLevelToLog)
            {
                Console.WriteLine(Events.PipPrefix + "Process standard output at '{2}'", pipSemiStableHash, pipDescription, path);
            }
        }

        public void PipProcessStandardError(LoggingContext context, long pipSemiStableHash, string pipDescription, string path)
        {
            context.SpecifyVerboseWasLogged((int)EventId.PipProcessStandardError);
            if (LogEventLevel.Verbose <= context.MaximumLevelToLog)
            {
                Console.WriteLine(Events.PipPrefix + "Process standard error at '{2}'", pipSemiStableHash, pipDescription, path);
            }
        }

        public void PipProcessFileAccessTableEntry(LoggingContext context, long pipSemiStableHash, string pipDescription, string value)
        {
            context.SpecifyVerboseWasLogged((int)EventId.PipProcessFileAccessTableEntry);
            if (LogEventLevel.Verbose <= context.MaximumLevelToLog)
            {
                Console.WriteLine(Events.PipPrefix + "File access table entry '{2}'", pipSemiStableHash, pipDescription, value);
            }
        }

        public void PipProcessFailedToParsePathOfFileAccess(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string operation,
            string path)
        {
            context.SpecifyWarningWasLogged((int)EventId.PipProcessFailedToParsePathOfFileAccess);
            if (LogEventLevel.Warning <= context.MaximumLevelToLog)
            {
                Console.WriteLine(Events.PipPrefix + "Access to the following path will be ignored, since the path could not be parsed: '{3}' (Accessed via {2})", 
                pipSemiStableHash,
                pipDescription,
                operation,
                path);
            }
        }

        public void PipProcessIgnoringPathOfSpecialDeviceFileAccess(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string operation,
            string path)
        {
            context.SpecifyVerboseWasLogged((int)EventId.PipProcessIgnoringPathOfSpecialDeviceFileAccess);
            if (LogEventLevel.Verbose <= context.MaximumLevelToLog)
            {
                Console.WriteLine(Events.PipPrefix + "Access to the following path will be ignored, since the path is a path to a device: '{3}' (Accessed via {2})",
                pipSemiStableHash,
                pipDescription,
                operation,
                path);
            }
        }

        public void PipProcessIgnoringPathWithWildcardsFileAccess(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string operation,
            string path)
        {
            context.SpecifyVerboseWasLogged((int)EventId.PipProcessIgnoringPathWithWildcardsFileAccess);
            if (LogEventLevel.Verbose <= context.MaximumLevelToLog)
            {
                Console.WriteLine(Events.PipPrefix + "Access to the following path will be ignored, since the path contains wildcard characters: '{3}' (Accessed via {2})",
                pipSemiStableHash,
                pipDescription,
                operation,
                path);
            }
        }

        public void PipProcessDisallowedFileAccessWhitelistedNonCacheable(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string fileAccessDescription,
            string path)
        {
            context.SpecifyVerboseWasLogged((int)EventId.PipProcessDisallowedFileAccessWhitelistedNonCacheable);
            if (LogEventLevel.Verbose <= context.MaximumLevelToLog)
            {
                Console.WriteLine(Events.PipPrefix +
                "Disallowed file access (non-cacheable) was detected on '{3}' with {2}. This message will become an error if the whitelist entry (in a top-level configuration file) allowing this access is removed.",
                pipSemiStableHash,
                pipDescription,
                fileAccessDescription,
                path);
            }
        }

        public void PipProcessDisallowedFileAccessWhitelistedCacheable(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string fileAccessDescription,
            string path)
        {
            context.SpecifyVerboseWasLogged((int)EventId.PipProcessDisallowedFileAccessWhitelistedCacheable);
            if (LogEventLevel.Verbose <= context.MaximumLevelToLog)
            {
                Console.WriteLine(Events.PipPrefix +
                "Disallowed file access (cacheable) was detected on '{3}' with {2}. This message will become an error if the whitelist entry (in a top-level configuration file) allowing this access is removed.",
                pipSemiStableHash,
                pipDescription,
                fileAccessDescription,
                path);
            }
        }

        public void FileAccessWhitelistFailedToParsePath(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string fileAccessDescription,
            string path,
            int characterWithError)
        {
            context.SpecifyWarningWasLogged((int)EventId.FileAccessWhitelistFailedToParsePath);
            if (LogEventLevel.Warning <= context.MaximumLevelToLog)
            {
                Console.WriteLine(Events.PipPrefix +
                "Tool path '{3}' failed to parse at character '{4}' could not be parsed. File access whitelist entries matching on tool paths will not be checked for this access. (Accessed via {2})",
                pipSemiStableHash,
                pipDescription,
                fileAccessDescription,
                path,
                characterWithError);
            }
        }

        public void PipProcessUncacheableWhitelistNotAllowedInDistributedBuilds(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string fileAccessDescription,
            string path)
        {
            context.SpecifyErrorWasLogged((int)EventId.PipProcessUncacheableWhitelistNotAllowedInDistributedBuilds);
            if (LogEventLevel.Error <= context.MaximumLevelToLog)
            {
                Console.Error.WriteLine(Events.PipPrefix +
                "Disallowed file access (non-cacheable) was detected on '{3}' with {2}. This message is an error because non-cacheable whitelist matches are not allowed in distributed builds.",
                pipSemiStableHash,
                pipDescription,
                fileAccessDescription,
                path);
            }
        }

        public void PipProcess(LoggingContext context, long pipSemiStableHash, string pipDescription, uint id, string path)
        {
            context.SpecifyVerboseWasLogged((int)EventId.Process);
            if (LogEventLevel.Verbose <= context.MaximumLevelToLog)
            {
                Console.WriteLine(Events.PipPrefix + "Process with id {2} at '{3}'", pipSemiStableHash, pipDescription, id, path);
            }
        }

        public void BrokeredDetoursInjectionFailed(LoggingContext context, uint processId, string error)
        {
            context.SpecifyErrorWasLogged((int)EventId.BrokeredDetoursInjectionFailed);
            if (LogEventLevel.Error <= context.MaximumLevelToLog)
            {
                Console.Error.WriteLine("Failed to instrument process ID {0} for file monitoring on behalf of an existing instrumented process, error: {1}. Most likely reason for this error is the run time for the process exceeded the allowed timeout for the process to complete.",
                processId, error);
            }
        }

        public void LogDetoursDebugMessage(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string message)
        {
            context.SpecifyVerboseWasLogged((int)EventId.LogDetoursDebugMessage);
            if (LogEventLevel.Verbose <= context.MaximumLevelToLog)
            {
                Console.WriteLine(Events.PipPrefix + "Detours Debug Message: {2}", pipSemiStableHash, pipDescription, message);
            }
        }

        public void LogMacKextFailure(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string message)
        {
            context.SpecifyErrorWasLogged((int)EventId.LogMacKextFailure);
            if (LogEventLevel.Error <= context.MaximumLevelToLog)
            {
                Console.Error.WriteLine(Events.PipPrefix + "{2}", pipSemiStableHash, pipDescription, message);
            }
        }

        public void LogAppleSandboxPolicyGenerated(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string policyFilePath)
        {
            context.SpecifyVerboseWasLogged((int)EventId.LogAppleSandboxPolicyGenerated);
            if (LogEventLevel.Verbose <= context.MaximumLevelToLog)
            {
                Console.WriteLine(Events.PipPrefix + "Apple sandbox-exec policy for pip generated: {2}", 
                pipSemiStableHash,
                pipDescription,
                policyFilePath);
            }
        }

        public void LogDetoursMaxHeapSize(
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
            ulong handleMapEntries)
        {
            context.SpecifyVerboseWasLogged((int)EventId.LogDetoursMaxHeapSize);
            if (LogEventLevel.Verbose <= context.MaximumLevelToLog)
            {
                Console.WriteLine(Events.PipPrefix + "Maximum detours heap size for process in the pip is {2} bytes. The processName '{3}'. The processId is: {4}. The manifestSize in bytes is: {5}. The finalDetoursHeapSize in bytes is: {6}. The allocatedPoolEntries is: {7}. The maxHandleMapEntries is: {8}. The handleMapEntries is: {9}.",
                pipSemiStableHash,
                pipDescription,
                maxDetoursHeapSizeInBytes,
                processName,
                processId,
                manifestSizeInBytes,
                finalDetoursHeapSizeInBytes,
                allocatedPoolEntries,
                maxHandleMapEntries,
                handleMapEntries);
            }
        }

        public void LogInternalDetoursErrorFileNotEmpty(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string message)
        {
            context.SpecifyErrorWasLogged((int)EventId.LogInternalDetoursErrorFileNotEmpty);
            if (LogEventLevel.Error <= context.MaximumLevelToLog)
            {
                Console.Error.WriteLine(Events.PipPrefix + "Detoured process emitted failure information that could not be transmitted back to {ShortProductName}. Diagnostic file content: {2}",
                pipSemiStableHash,
                pipDescription,
                message);
            }
        }

        public void LogFailedToCreateDirectoryForInternalDetoursFailureFile(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string path,
            string message)
        {
            context.SpecifyErrorWasLogged((int)EventId.LogFailedToCreateDirectoryForInternalDetoursFailureFile);
            if (LogEventLevel.Error <= context.MaximumLevelToLog)
            {
                Console.Error.WriteLine(Events.PipPrefix + "Failed to create directory for the internal Detours error file. Path: {2}. Error: {3}",
                pipSemiStableHash,
                pipDescription,
                path,
                message);
            }
        }

        public void LogGettingInternalDetoursErrorFile(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string message)
        {
            context.SpecifyErrorWasLogged((int)EventId.LogGettingInternalDetoursErrorFile);
            if (LogEventLevel.Error <= context.MaximumLevelToLog)
            {
                Console.Error.WriteLine(Events.PipPrefix + "Failed checking for detours backup communication file existence. Pip will be treated as a failure. Error: {2}.", 
                pipSemiStableHash,
                pipDescription,
                message);
            }
        }

        public void LogMismatchedDetoursVerboseCount(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            int lastMessageCount)
        {
            context.SpecifyVerboseWasLogged((int)EventId.LogMismatchedDetoursVerboseCount);
            if (LogEventLevel.Verbose <= context.MaximumLevelToLog)
            {
                Console.WriteLine(Events.PipPrefix + "The number of messages sent by detoured processes did not match the number received by the {MainExecutableName} process. LastMessageCount:{2}",
                pipSemiStableHash,
                pipDescription,
                lastMessageCount);
            }
        }

        public void LogMessageCountSemaphoreExists(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription)
        {
            context.SpecifyErrorWasLogged((int)EventId.LogMessageCountSemaphoreExists);
            if (LogEventLevel.Error <= context.MaximumLevelToLog)
            {
                Console.Error.WriteLine(
                    Events.PipPrefix + "Semaphore for counting Detours messages is already opened.",
                    pipSemiStableHash,
                    pipDescription);
            }
        }

        public void PipProcessCommandLineTooLong(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string commandLine,
            int maxLength)
        {
            context.SpecifyErrorWasLogged((int)EventId.PipProcessCommandLineTooLong);
            if (LogEventLevel.Error <= context.MaximumLevelToLog)
            {
                Console.Error.WriteLine(Events.PipPrefix + "Process command line is longer than {3} characters: {2}", 
                pipSemiStableHash,
                pipDescription,
                commandLine,
                maxLength);
            }
        }

        public void PipProcessInvalidWarningRegex(LoggingContext context, long pipSemiStableHash, string pipDescription, string pattern, string options)
        {
            context.SpecifyErrorWasLogged((int)EventId.PipProcessInvalidWarningRegex);
            if (LogEventLevel.Error <= context.MaximumLevelToLog)
            {
                Console.Error.WriteLine(Events.PipPrefix + "Process warning regular expression is invalid, pattern is {2}, options are {3}", pipSemiStableHash, pipDescription, pattern, options);
            }
        }

        public void PipProcessInvalidErrorRegex(LoggingContext context, long pipSemiStableHash, string pipDescription, string pattern, string options)
        {
            context.SpecifyErrorWasLogged((int)EventId.PipProcessInvalidErrorRegex);
            if (LogEventLevel.Error <= context.MaximumLevelToLog)
            {
                Console.Error.WriteLine(Events.PipPrefix + "Process error regular expression is invalid, pattern is {2}, options are {3}", pipDescription, pattern, options);
            }
        }

        public void PipProcessChildrenSurvivedError(LoggingContext context, long pipSemiStableHash, string pipDescription, string path)
        {
            context.SpecifyErrorWasLogged((int)EventId.PipProcessChildrenSurvivedError);
            if (LogEventLevel.Error <= context.MaximumLevelToLog)
            {
                Console.Error.WriteLine(Events.PipPrefix + "Process child survived: '{2}'", pipSemiStableHash, pipDescription, path);
            }
        }

        public void PipProcessChildrenSurvivedKilled(LoggingContext context, long pipSemiStableHash, string pipDescription)
        {
            context.SpecifyVerboseWasLogged((int)EventId.PipProcessChildrenSurvivedKilled);
            if (LogEventLevel.Verbose <= context.MaximumLevelToLog)
            {
                Console.WriteLine(Events.PipPrefix + "Process and/or job containing child processes killed", pipSemiStableHash, pipDescription);
            }
        }

        public void PipProcessMspdbsrv(LoggingContext context, long pipSemiStableHash, string pipDescription)
        {
            context.SpecifyWarningWasLogged((int)EventId.PipProcessMspdbsrv);
            if (LogEventLevel.Warning <= context.MaximumLevelToLog)
            {
                Console.WriteLine(
                    Events.PipPrefix +
                    "Hint: By default, link.exe is a badly behaving build tool, as it spawns a detached child process. Run [Root]/Shared/Scripts/DisableMspdbsrv.cmd to disable this behavior.",
                    pipSemiStableHash,
                    pipDescription);
            }
        }

        public void PipProcessMissingExpectedOutputOnCleanExit(LoggingContext context, long pipSemiStableHash, string pipDescription, string pipSpecPath, string pipWorkingDirectory, string path)
        {
            context.SpecifyVerboseWasLogged((int)EventId.PipProcessMissingExpectedOutputOnCleanExit);
            if (LogEventLevel.Verbose <= context.MaximumLevelToLog)
            {
                Console.WriteLine(Events.PipSpecPrefix + "Process was expected to write an output file at '{4}', but that file is not present.", pipSemiStableHash, pipDescription, pipSpecPath, pipWorkingDirectory, path);
            }
        }

        public void PipProcessExpectedMissingOutputs(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string paths)
        {
            context.SpecifyErrorWasLogged((int)EventId.PipProcessExpectedMissingOutputs);
            if (LogEventLevel.Error <= context.MaximumLevelToLog)
            {
                Console.Error.WriteLine(Events.PipPrefix + "- Process was expected to write the following output files, but those files are not present.:\r\n{2}", 
                pipSemiStableHash,
                pipDescription,
                paths);
            }
        }

        public void PipProcessOutputPreparationFailed(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string path,
            int errorCode,
            string message,
            string exception)
        {
            context.SpecifyErrorWasLogged((int)EventId.PipProcessOutputPreparationFailed);
            if (LogEventLevel.Error <= context.MaximumLevelToLog)
            {
                Console.Error.WriteLine(Events.PipSpecPrefix + "Process output directories could not be prepared, path '{2}', error code {3:X8}: {4}", 
                pipSemiStableHash,
                pipDescription,
                path,
                errorCode,
                message,
                exception);
            }
        }

        public void PipStandardIOFailed(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string path,
            int errorCode,
            string message)
        {
            context.SpecifyErrorWasLogged((int)EventId.PipStandardIOFailed);
            if (LogEventLevel.Error <= context.MaximumLevelToLog)
            {
                Console.Error.WriteLine(Events.PipPrefix + "Process standard I/O failure at path '{2}', error code {3:X8}: {4}", 
                pipSemiStableHash,
                pipDescription,
                path,
                errorCode,
                message);
            }
        }

        public void PipExitedUncleanly(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            bool canceled,
            int errorCode,
            bool killed,
            int numSurvivingChildErrors)
        {
            context.SpecifyVerboseWasLogged((int)EventId.PipExitedUncleanly);
            if (LogEventLevel.Verbose <= context.MaximumLevelToLog)
            {
                Console.WriteLine(Events.PipPrefix + "Pip had unclean exit. Canceled: {2}, Error code {3}, Killed: {4}, # Surviving child error: {5}",
                pipSemiStableHash,
                pipDescription,
                canceled,
                errorCode,
                killed,
                numSurvivingChildErrors);
            }
        }

        public void PipProcessStandardInputException(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string pipSpecPath,
            string pipWorkingDirectory,
            string exceptionMessage)
        {
            context.SpecifyVerboseWasLogged((int)EventId.PipProcessStandardInputException);
            if (LogEventLevel.Verbose <= context.MaximumLevelToLog)
            {
                Console.WriteLine(Events.PipSpecPrefix + " Unexpected standard input exception: {4}", 
                pipSemiStableHash,
                pipDescription,
                pipSpecPath,
                pipWorkingDirectory,
                exceptionMessage);
            }
        }

        public void PipProcessToolErrorDueToHandleToFileBeingUsed(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string pipSpecPath,
            string pipWorkingDirectory,
            string toolName,
            string file,
            string reason)
        {
            context.SpecifyErrorWasLogged((int)EventId.PipProcessToolErrorDueToHandleToFileBeingUsed);
            if (LogEventLevel.Error <= context.MaximumLevelToLog)
            {
                Console.Error.WriteLine(Events.PipSpecPrefix + "The tool '{4}' cannot access the file '{5}' because it is being used by another process: \r\n{6}",
                pipSemiStableHash,
                pipDescription,
                pipSpecPath,
                pipWorkingDirectory,
                toolName,
                file,
                reason);
            }
        }

        public void PipProcessError(
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
            string optionalMessage)
        {
            context.SpecifyErrorWasLogged((int)EventId.PipProcessError);
            if (LogEventLevel.Error <= context.MaximumLevelToLog)
            {
                Console.Error.WriteLine(Events.PipSpecPrefix + " - failed with exit code {7}{8}\r\n{5}\r\n{6}", 
                pipSemiStableHash,
                pipDescription,
                pipSpecPath,
                pipWorkingDirectory,
                pipExe,
                outputToLog,
                pathsToLog,
                exitCode,
                optionalMessage);
            }
        }

        public void PipProcessWarning(
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
            string pathsToLog)
        {
            context.SpecifyWarningWasLogged((int)EventId.PipProcessWarning);
            if (LogEventLevel.Warning <= context.MaximumLevelToLog)
            {
                Console.WriteLine(Events.PipSpecPrefix + " - warnings\r\n{5}\r\n{6}", 
                pipSemiStableHash,
                pipDescription,
                pipSpecPath,
                pipWorkingDirectory,
                pipExe,
                outputToLog,
                pathsToLog);
            }
        }

        public void PipProcessOutput(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string pipSpecPath,
            string pipWorkingDirectory,
            string outputToLog)
        {
            context.SpecifyVerboseWasLogged((int)EventId.PipProcessOutput);
            if (LogEventLevel.Verbose <= context.MaximumLevelToLog)
            {
                Console.WriteLine(Events.PipSpecPrefix + "\r\n{4}", 
                pipSemiStableHash,
                pipDescription,
                pipSpecPath,
                pipWorkingDirectory,
                outputToLog);
            }
        }

        public void PipTempDirectoryCleanupError(LoggingContext context, long pipSemiStableHash, string pipDescription, string directory, string exceptionMessage)
        {
            context.SpecifyErrorWasLogged((int)EventId.PipTempDirectoryCleanupError);
            if (LogEventLevel.Error <= context.MaximumLevelToLog)
            {
                Console.Error.WriteLine(Events.PipPrefix + "Failed to clean temp directory at '{2}'. Pip will not be executed. Reason: {3}", pipSemiStableHash, pipDescription, directory, exceptionMessage);
            }
        }

        public void PipTempDirectorySetupError(LoggingContext context, string directory, string exceptionMessage)
        {
            context.SpecifyErrorWasLogged((int)EventId.PipTempDirectorySetupError);
            if (LogEventLevel.Error <= context.MaximumLevelToLog)
            {
                Console.Error.WriteLine("Failed to create temp directory at '{0}'. Reason: {1}", directory, exceptionMessage);
            }
        }

        public void PipFailedToCreateDumpFile(LoggingContext context, long pipSemiStableHash, string pipDescription, string exceptionMessage)
        {
            context.SpecifyWarningWasLogged((int)EventId.PipFailedToCreateDumpFile);
            if (LogEventLevel.Warning <= context.MaximumLevelToLog)
            {
                Console.WriteLine(Events.PipPrefix + "Failed to create dump for timed out process. {2}", pipSemiStableHash, pipDescription, exceptionMessage);
            }
        }

        public void RetryStartPipDueToErrorPartialCopyDuringDetours(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            int error,
            int retryNumber)
        {
            context.SpecifyVerboseWasLogged((int)EventId.RetryStartPipDueToErrorPartialCopyDuringDetours);
            if (LogEventLevel.Verbose <= context.MaximumLevelToLog)
            {
                Console.WriteLine(Events.PipSpecPrefix + "Retry to start pip for {3} time(s) due to the following error: {2}", 
                pipSemiStableHash,
                pipDescription,
                error,
                retryNumber);
            }
        }

        public void DuplicateWindowsEnvironmentVariableEncountered(
            LoggingContext context,
            string key,
            string existingValue,
            string ignoredValue)
        {
            context.SpecifyWarningWasLogged((int)EventId.DuplicateWindowsEnvironmentVariableEncountered);
            if (LogEventLevel.Warning <= context.MaximumLevelToLog)
            {
                Console.WriteLine("Unexpected duplicate environment variable encountered. Variable '{0}' has already been defined with value '{1}'. The other occurrence with value '{2}' will be ignored.", 
                key,
                existingValue,
                ignoredValue);
            }
        }

        public void ReadWriteFileAccessConvertedToReadMessage(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            uint processId,
            string path)
        {
            context.SpecifyVerboseWasLogged((int)EventId.ReadWriteFileAccessConvertedToReadMessage);
            if (LogEventLevel.Verbose <= context.MaximumLevelToLog)
            {
                Console.WriteLine(Events.PipPrefix + "File access on file '{3}' requested with Read/Write but granted for Read only by process with ID: {2}.", 
                pipSemiStableHash,
                pipDescription,
                processId,
                path);
            }
        }

        public void ReadWriteFileAccessConvertedToReadWarning(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription)
        {
            context.SpecifyWarningWasLogged((int)EventId.ReadWriteFileAccessConvertedToReadWarning);
            if (LogEventLevel.Warning <= context.MaximumLevelToLog)
            {
                Console.WriteLine(
                    Events.PipPrefix +
                    "This pip might have failed because of converting Read/Write file access to a Read file access. Examine the execution log for information on which files the Read/Write access request was converted to Read access request.",
                    pipSemiStableHash,
                    pipDescription);
            }
        }

        public void PipProcessResponseFileCreationFailed(
            LoggingContext context,
            long pipSemiStableHash,
            string pipDescription,
            string path,
            int errorCode,
            string message)
        {
            context.SpecifyErrorWasLogged((int)EventId.PipProcessResponseFileCreationFailed);
            if (LogEventLevel.Error <= context.MaximumLevelToLog)
            {
                Console.Error.WriteLine(Events.PipSpecPrefix + "Process response file could not be prepared, path '{2}', error code {3:X8}: {4}", 
                pipSemiStableHash,
                pipDescription,
                path,
                errorCode,
                message);
            }
        }
    }
}
