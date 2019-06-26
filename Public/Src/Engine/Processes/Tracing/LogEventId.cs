// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;

namespace BuildXL.Processes.Tracing
{
    // disable warning regarding 'missing XML comments on public API'. We don't need docs for these values
#pragma warning disable 1591

    /// <summary>
    /// Defines event IDs corresponding to events in <see cref="Logger" />
    /// </summary>
    public enum LogEventId
    {
        None = 0,

        PipProcessDisallowedFileAccess = 9,
        PipProcessFileAccess = 10,
        PipProcessStartFailed = 11,
        PipProcessFinished = 12,
        PipProcessFinishedFailed = 13,
        // was PipProcessDisallowedFileAccessError = 14,
        PipProcessTookTooLongWarning = 15,
        PipProcessTookTooLongError = 16,
        PipProcessStandardOutput = 17,
        PipProcessStandardError = 18,
        ReadWriteFileAccessConvertedToReadMessage = 19,
        PipProcessDisallowedTempFileAccess = 20,
        ReadWriteFileAccessConvertedToReadWarning = 21,
        PipProcessFileAccessTableEntry = 22,
        PipInvalidDetoursDebugFlag1 = 23,
        PipInvalidDetoursDebugFlag2 = 24,

        PipProcessFinishedDetourFailures = 26,
        PipProcessCommandLineTooLong = 32,
        PipProcessInvalidWarningRegex = 39,
        PipProcessChildrenSurvivedError = 41,
        PipProcessChildrenSurvivedKilled = 42,
        PipProcessChildrenSurvivedTooMany = 43,
        PipProcessMissingExpectedOutputOnCleanExit = 44,
        PipProcessOutputPreparationFailed = 46,


        PipProcessError = 64,
        PipProcessWarning = 65,
        PipProcessOutput = 66,

        PipProcessResponseFileCreationFailed = 74,
        
        PipProcessStartExternalTool = 78,
        PipProcessFinishedExternalTool = 79,
        PipProcessStartExternalVm = 80,
        PipProcessFinishedExternalVm = 81,
        PipProcessExternalExecution = 82,

        RetryStartPipDueToErrorPartialCopyDuringDetours = 85,

        PipProcessStandardInputException = 86,

        PipProcessInvalidErrorRegex = 89,
        //// Reserved  = 90,
        //// Reserved  = 91,
        PipProcessNeedsExecuteExternalButExecuteInternal = 92,

        PipProcessDisallowedFileAccessWhitelistedCacheable = 264,
        PipProcessDisallowedFileAccessWhitelistedNonCacheable = 269,
        FileAccessWhitelistFailedToParsePath = 274,
        
        //// Reserved = 306,
        //// Reserved = 307,
        PipFailSymlinkCreation = 308,
        //// Reserved = 309,

        PipProcessMessageParsingError = 311,

        PipExitedUncleanly = 314,
        PipStandardIOFailed = 316,

        PipRetryDueToExitedWithAzureWatsonExitCode = 317,

        DuplicateWindowsEnvironmentVariableEncountered = 336,
        
        PipProcessDisallowedNtCreateFileAccessWarning = 480,

        PipProcessExpectedMissingOutputs = 504,
        
        //// Additional Process Isolation
        PipProcessIgnoringPathWithWildcardsFileAccess = 800,
        PipProcessIgnoringPathOfSpecialDeviceFileAccess = 801,
        PipProcessFailedToParsePathOfFileAccess = 802,
        Process = 803,

        // Temp files/directory cleanup
        PipTempDirectoryCleanupError = 2201,
        PipTempDirectorySetupError = 2203,
        PipTempSymlinkRedirectionError = 2205,
        PipTempSymlinkRedirection = 2206,

        PipFailedToCreateDumpFile = 2210,

        PipOutputNotAccessed = 2603,

        LogInternalDetoursErrorFileNotEmpty = 2919,
        LogGettingInternalDetoursErrorFile = 2920,

        LogMessageCountSemaphoreExists = 2923,
        LogFailedToCreateDirectoryForInternalDetoursFailureFile = 2925,
        LogMismatchedDetoursVerboseCount = 2927,
        LogDetoursMaxHeapSize = 2928,

        //DominoProcessesStart = 4400,
        PipProcessUncacheableWhitelistNotAllowedInDistributedBuilds = 4401,
        //DominoProcessesEnd = 4499,

        //// Detours
        BrokeredDetoursInjectionFailed = 10100,
        LogDetoursDebugMessage = 10101,
        LogAppleSandboxPolicyGenerated = 10102,
        LogMacKextFailure = 10103,

        //// Container related errors
        FailedToMergeOutputsToOriginalLocation = 12202,
        FailedToCleanUpContainer = 12203,
        WarningSettingUpContainer = 12204,
        
        PipInContainerStarted = 12206,
        PipInContainerStarting = 12207,
        PipSpecifiedToRunInContainerButIsolationIsNotSupported = 12208,
        FailedToCreateHardlinkOnMerge = 12209,
        DoubleWriteAllowedDueToPolicy = 12210,
        DisallowedDoubleWriteOnMerge = 12211,
        
        //// Special tool errors
        PipProcessToolErrorDueToHandleToFileBeingUsed = 14300,
    }
}
