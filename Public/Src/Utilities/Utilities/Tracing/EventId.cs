
using System.Diagnostics.CodeAnalysis;
namespace BuildXL.Utilities.Tracing
{
#pragma warning disable 1591
    public enum EventId
    {
        None = 0,
        PipProcessDisallowedFileAccess = 9,
        PipProcessFileAccess = 10,
        PipProcessFinished = 12,
        PipProcessFinishedFailed = 13,
        PipProcessTookTooLongError = 16,
        PipProcessDisallowedTempFileAccess = 20,
        PipProcessFileAccessTableEntry = 22,
        ObjectCacheStats = 33,
        CacheBulkStatistics = 34,
        PipProcessChildrenSurvivedError = 41,
        PipProcessMissingExpectedOutputOnCleanExit = 44,
        PipProcessOutputPreparationFailed = 46,
        CacheClientStats = 50,
        PipProcessError = 64,
        PipProcessWarning = 65,
        PipProcessOutput = 66,
        RetryStartPipDueToErrorPartialCopyDuringDetours = 85,
        PipProcessInvalidErrorRegex = 89,
        IgnoringUntrackedSourceFileNotUnderMount = 222,
        PipProcessDisallowedFileAccessWhitelistedCacheable = 264,
        PipProcessDisallowedFileAccessWhitelistedNonCacheable = 269,
        DisallowedFileAccessInSealedDirectory = 277,
        PipProcessMessageParsingError = 311,
        DominoCompletion = 406,
        TextLogEtwOnly = 450,
        CacheFileLog = 451,
       
        StatsPerformanceLog = 459,
        UnexpectedConditionLocal = 472,
        UnexpectedConditionTelemetry = 473,
        PipProcessDisallowedNtCreateFileAccessWarning = 480,
        StartScanningJournal = 681,
        EndScanningJournal = 686,
        DisableChangeTracker = 688,
        RetryOnFailureException = 744,
        Process = 803,
        
        
        StartViewer = 1506,
        UnableToStartViewer = 1507,
        Memory = 1508,
        UnableToLaunchViewer = 1511,
        FileCombinerVersionIncremented = 2103,
        SpecCache = 2107,
        PathHashed = 2600,
        JournalServiceNotInstalled = 2902,
        UserRefusedElevation = 2908,
      
        DominoStorageStart = 4200,
        ValidateJunctionRoot = 4202,
        ConflictDirectoryMembershipFingerprint = 4205,
        Statistic = 6300,
        StatisticWithoutTelemetry = 6304,
        BulkStatistic = 6305,
        FinalStatistics = 6306,
        LoggerStatistics = 6307,
        PipCounters = 6308,
        ChangeDetectionCreateResult = 8007,
        VerboseEvent = 10000,
        InfoEvent = 10001,
        WarningEvent = 10002,
        ErrorEvent = 10003,
        CriticalEvent = 10004,
        AlwaysEvent = 10005,
        VerboseEventWithProvenance = 10006,
        DiagnosticEventInOtherTask = 10007,
        DiagnosticEvent = 10008,
        InfrastructureErrorEvent = 10009,
        UserErrorEvent = 10010,
        
        DominoCompletedEvent = 11500,
        TargetAddedEvent = 11501,
        TargetRunningEvent = 11502,
        TargetFailedEvent = 11503,
        TargetFinishedEvent = 11504,
        DominoInvocationEvent = 11505,
        DropCreationEvent = 11506,
        DropFinalizationEvent = 11507,
        DominoContinuousStatisticsEvent = 11508,
        ServicePipFailed = 12003,
        IpcClientFailed = 12006,
        Status = 12400,
        StatusSnapshot = 12401,
        StatusHeader = 12402,
        StatusCallbacksDelayed = 12403,

        /*
         *********************************************
         * README:
         *********************************************
         *
         * Please do not add any new events in this class. 
         *
         * New events should be added to LogEvent.cs next to the Log.cs file that
         * uses the identifier. 
         *
         * The events are here only when we started small. This causes too much of the
         * build graph to be reevaluated if one updates a single file at the bottom of the stack.
         * It is more effecient to keep the constants near the usagage.
         *
         * A unittest is guaranteeing global eventid uniqueness so you don't have to worry about that.
         *
         *********************************************
         */
    }
}
