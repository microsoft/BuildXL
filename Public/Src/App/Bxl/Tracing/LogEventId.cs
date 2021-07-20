// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.App.Tracing
{
    // disable warning regarding 'missing XML comments on public API'. We don't need docs for these values
#pragma warning disable 1591

    /// <summary>
    /// Defines event IDs corresponding to events in <see cref="Logger" />
    /// </summary>
    public enum LogEventId
    {
        StartupCurrentDirectory = 27,
        CatastrophicFailureCausedByDiskSpaceExhaustion = 49,
        CatastrophicFailureCausedByCorruptedCache = 51,
        CatastrophicFailure = 59,
        MappedRoot = 83,
        StartupTimestamp = 403,
        DominoCompletion = 406,
        DominoCatastrophicFailure = 407,
        DominoPerformanceSummary = 408,
        FailedToEnumerateLogDirsForCleanup = 454,
        DominoMacOSCrashReport = 412,
        EventWriteFailuresOccurred = 452,
        FailedToCleanupLogDir = 455,
        WaitingCleanupLogDir = 456,
        CoreDumpNoPermissions = 460,
        CrashReportProcessing = 461,
        CancellationRequested = 470,
        TelemetryShutDown = 471,
        TelemetryShutDownException = 474,
        TelemetryShutdownTimeout = 476,

        Channel = 502,
        StorageCatastrophicFailureDriveError = 730,
        CatastrophicFailureMissingRuntimeDependency = 731,
        
        ChangeJournalServiceReady = 2914,
        
        TelemetryEnabledNotifyUser = 4301,
        TelemetryEnabledHideNotification = 4302,
        MemoryLoggingEnabled = 4303,

        EventCount = 6302,

        UsingExistingServer = 8100,
        AppServerBuildStart = 8101,
        AppServerBuildFinish = 8102,
        StartingNewServer = 8103,
        CannotStartServer = 8104,
        DeploymentUpToDateCheckPerformed = 8105,
        DeploymentCacheCreated = 8106,

        ProcessPipsUncacheable = 14001,
        NoCriticalPathTableHits = 14002,
        NoSourceFilesUnchanged = 14003,
        ServerModeDisabled = 14004,
        GraphCacheCheckJournalDisabled = 14005,
        SlowCacheInitialization = 14006,
        BuildHasPerfSmells = 14010,
        LogProcessesEnabled = 14011,
        FrontendIOSlow = 14012,
        // ProblematicWorkerExitError = 14013,

        PerformanceCollectorInitializationFailed = 15000,
        CbTimeoutReached = 15001,
        CbTimeoutTooLow = 15002,
        CbTimeoutInvalid = 15003,
    }
}
