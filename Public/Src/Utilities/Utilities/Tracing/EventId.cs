// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;

namespace BuildXL.Utilities.Tracing
{
    // disable warning regarding 'missing XML comments on public API'. We don't need docs for these values
#pragma warning disable 1591

    // TODO: Move other event IDs to this enumeration.

    /// <summary>
    /// Defines event IDs corresponding to events in <see cref="Events" />
    /// </summary>
    public enum EventId
    {
        None = 0,

        // Obsolete = 3,
        // Obsolete = 4,
        // Elsewhere = 5,
        // Elsewhere = 6,
        // Elsewhere = 7,
        // Elsewhere = 8,
        PipProcessDisallowedFileAccess = 9,
        PipProcessFileAccess = 10,
        PipProcessStartFailed = 11,
        PipProcessFinished = 12,
        PipProcessFinishedFailed = 13,
        // elsewhere,
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

        // BuildXL.Engine.dll FilterDetails = 25
        PipProcessFinishedDetourFailures = 26,
        // Elsewhere = 27,

        EngineDidNotFindDefaultQualifierInConfig = 28,
        // AssignProcessToJobObjectFailed = 29,
        // Reserved  = 30,
        EngineDidNotFindRequestedQualifierInConfig = 31,
        PipProcessCommandLineTooLong = 32,
        ObjectCacheStats = 33,
        CacheBulkStatistics = 34,

        
        // Reserved = 37,
        // Reserved = 38,
        PipProcessInvalidWarningRegex = 39,
        
        PipProcessChildrenSurvivedError = 41,
        PipProcessChildrenSurvivedKilled = 42,
        PipProcessChildrenSurvivedTooMany = 43,
        PipProcessMissingExpectedOutputOnCleanExit = 44,
        EngineFailedToInitializeOutputCache = 45,
        PipProcessOutputPreparationFailed = 46,
        // Elsewhere = 47,
        // Elsewhere = 49,
        CacheClientStats = 50,
        // Elsewhere = 51,
        // Elsewhere = 52,
        PipProcessPreserveOutputDirectoryFailedToMakeFilePrivate = 53,
        // Reserved = 54,
        // Reserved = 55,
        // Reserved = 56,
        // Reservd = 57,
        // Elsewhere = 58,
        // Elsewhere = 59,
        
        PipProcessError = 64,
        PipProcessWarning = 65,
        PipProcessOutput = 66,

        // Contains input assertion for the pip
        // Elsewhere = 67,

        // Reserved = 68,
        // Reserved = 69,
        // Reserved = 70,
        // Reserved = 71,
        // Reserved = 72,
        

        // Elsewhere = 77,

        PipProcessStartExternalTool = 78,
        PipProcessFinishedExternalTool = 79,
        PipProcessStartExternalVm = 80,
        PipProcessFinishedExternalVm = 81,
        PipProcessExternalExecution = 82,

        // Elsewhere = 83,
        
        RetryStartPipDueToErrorPartialCopyDuringDetours = 85,

        PipProcessStandardInputException = 86,

        
        PipProcessInvalidErrorRegex = 89,
        // Reserved  = 90,
        // Reserved  = 91,
        PipProcessNeedsExecuteExternalButExecuteInternal = 92,
        // Reserved  = 93,
        
        // CannotHonorLowPriority = 95,
        // Reserved  = 96,
        // Reserved  = 97,
        // Reserved  = 98,
        [SuppressMessage("Microsoft.Naming", "CA1700:IdentifiersShouldBeSpelledCorrectly")]
        // Reserved  = 99,

        // Reserved 100..199
        // Reserved = 110,
        // Reserved = 117,
        // Reserved = 132,
        // Reserved = 133,
        // Reserved = 134,
        // Reserved = 138,
        // Reserved = 139,

        // Free Slots = 141,
        // Reserved = 145,
        // Reserved  = 150,
        // Reserved  = 151,
        // Reserved  = 153,
        // Reserved  = 157,
        // Reserved  = 162,
        // Reserved  = 165,
        // Reserved  = 166,
        // Reserved  = 167,
        // Reserved  = 168,
        // Reserved  = 171,
        // Reserved  = 172,
        // Reserved  = 186,
        // Reserved  = 187,
        // Reserved  = 193,

        // Scheduler, Fingerprinting and pip caching
        // Elsewhere = 200,
        // Elsewhere = 201,
        // Elsewhere = 202,
        // Reserved = 203,
        // Elsewhere = 204,
        // Reserved = 205,

        // Pip validation
        // Outputs
        // Elsewhere = 206,
        // Elsewhere= 207,
        // Elsewhere = 208,
        // Elsewhere = 209,
        // Elsewhere = 210,
        // Elsewhere = 211,
        // Elsewhere = 212,

        // Inputs
        // Elsewhere = 213,
        
        // Elsewhere = 215,
        // Elsewhere = 216,
        // Elsewhere = 217,

        // Pips
        // Elsewhere = 218,
        // Elsewhere = 219,
        // Elsewhere = 220,
        // Elsewhere = 221,

        // Input / output hashing
        IgnoringUntrackedSourceFileNotUnderMount = 222,
        // Elsewhere = 223,
        // Elsewhere = 224,

        // Schedule failure
        // Elsewhere = 225,
        // Elsewhere = 226,
        // Elsewhere = 227,
        // Elsewhere = 228,
        // Elsewhere = 229,
        // Elsewhere = 230,
        // Elsewhere = 231,
        // Elsewhere = 232,
        // Elsewehre = 233,
        // Elsewhere = 234,

        // Elsewhere = 235,
        // Elsewhere = 236,

        // Elsewhere = 237,
        // Elsewhere = 238,

        // Elsewhere = 239,
        // Reserved = 240,

        // Resrved = 241,

        // Elsewhere = 242,
        // Elsewhere = 243,

        // Elsewhere = 244,
        InputAssertionMissAfterContentFingerprintCacheDescriptorHit = 245,

        // More pip dependency errors
        // Elsewhere = 246,
        // Elsewhere = 247,
        // Elsewhere = 248,
        // Elsewhere = 14401,
        // Elsewhere = 14402,
        // Elsewhere = 14403,

        // Elsewhere = 14410,

        // Reserved = 250,
        // Reserved = 251,
        // Reserved = 252,

        // Pip performance.
        // Elsewhere = 253,
        // Elsewhere = 254,
        // Elsewhere = 255,
        // Elsewhere = 256,
        // Elsewhere = 257,
        // Elsewhere = 258,

        // was DirectoryPartiallySealed = 259,
        // was DisallowedFileAccessInSealedDirectoryError = 260,

        // Elsewhere = 261,
        // Elsewhere = 262,

        // Elsewhere = 263,

        PipProcessDisallowedFileAccessWhitelistedCacheable = 264,
        // Elsewhere = 265,
        // Reserved = 266,
        // Elsewhere = 267,
        // Elsewhere = 268,
        PipProcessDisallowedFileAccessWhitelistedNonCacheable = 269,
        // Elsewhere = 270,
        // Elsewhere = 271,
        // Elsewhere = 272,
        // Elsewhere = 274,
        // Reserved = 275,
        // Reserved = 276,
        DisallowedFileAccessInSealedDirectory = 277,
        // Elsewhere = 278,

        // Elsewhere = 279,

        // Scheduler continued
        // Elsewhere = 280,
        // Elsewhere = 281,
        // Elsewhere = 282,
        // Elsewhere = 283,
        // Elsewhere = 284,
        // Elsewhere = 285,

        // Elsewhere = 286,
        // Elsewhere 58 = 287,
        // ElseWhere = 288,
        // ElseWhere = 289,
        // Elsewhere = 290,
        // was ScheduleDirectorySourceSealedAllDirectories = 291,
        // was ScheduleDirectorySourceSealedTopDirectoryOnly = 292,
        // Elsewhere = 293,
        // Elsewhere = 294,
        // Elsewhere = 295,
        InvalidPipDueToInvalidServicePipDependency = 296,
        // Elsewhere = 297,
        // Elsewhere = 298,
        // Elsewhere = 299,
        // Elsewhere = 300,

        // Elsewhere = 301,
        // Elsewhere = 302,
        // Elsewhere = 303,

        // Reserved = 306,
        // Reserved = 307,
        PipFailSymlinkCreation = 308,
        // Reserved = 309,

        // Pip validation Inputs (continued)
        // Elsewehre = 310,
        PipProcessMessageParsingError = 311,

        // Elsewhere = 312,
        //CacheMissAnalysisTelemetry = 313,

        // Elsewhere = 314,
        // Elsewhere = 315,
        // Elsewhere = 316,

        // Elsewhere = 317,
        // Reserved = 318,

        // Free slot 319,
        // Reserved = 320,
        // Reserved = 321,
        // Reserved = 322,
        // Reserved = 323,
        // Reserved = 324,

        // Elsewhere = 325,
        // Reserved = 326,

        // Reserved = 327,
        // Reserved = 328,
        // Reserved = 329,
        // Reserved = 330,

        // Free slot 331,

        // Free slot 333,
        // Reserved = 334,
        // Reserved = 335,
        DuplicateWindowsEnvironmentVariableEncountered = 336,

        // Free slot 337,
        // Free slot 338,
        // Free slot 339,
        // Free slot 340,
        // Free slot 341,
        // Free slot 342,
        // Reserved = 343,
        // Reserved = 344,
        // Reserved = 345,

        // Free slot 346,
        // Reserved = 347,
        // Reserved = 348,
        // Reserved = 349,
        // Reserved = 350,
        // Reserved = 351,
        // Reserved = 352,
        // Reserved = 353,
        // Reserved = 354,
        // Reserved = 355,
        // Reserved = 356,
        // Reserved = 357,
        // Reserved = 358,

        // Free slot 359,

        // Scheduler: Directory fingerprinting
        // Elsewhere = 360,
        // Elsewhere = 361,
        // Elsewhere = 363,
        // Elsewhere = 364,
        // Elsewhere = 365,
        // Elsewhere = 366,
        // Elsewhere = 367,
        // Elsewhere = 368,
        // Elsewhere = 369,

        // Dynamic Module Activity
        // DEPRECATED 370,
        // DEPRECATED 371,
        // DEPRECATED 372,
        // DEPRECATED 373,
        // DEPRECATED 374,
        // DEPRECATED 375,
        // DEPRECATED 376,
        // was DisallowedFileAccessInTopOnlySourceSealedDirectoryError = 377,
        // Elsewhere = 378,
        // elsewhere = 379,

        // Environment
        EnvUnresolvableIdentifier = 400,
        EnvRequestedIdentifierIsANamespace = 401,
        // EnvFreezing = 402,
        // Elsewhere = 403,
        EnvAmbiguousReferenceDeclaration = 404,
        // Elsewhere = 405,
        DominoCompletion = 406,
        // Elsewhere = 407,
        // Elsewhere = 408,
        // Elsewhere 409,
        // DEPRECATED = 410,
        // DEPRECATED = 411,
        // Elsewhere = 412,
        // Elsewhere = 413,

        // Tracing
        TextLogEtwOnly = 450,
        CacheFileLog = 451,

        // Elsewhere = 452,
        // Elsewhere = 453,
        // Elsewhere = 454,
        // Elsewhere = 455,
        // Elsewhere = 456,
        // Elsewhere = 457,
        //was: DisplayHelpLink = 458,
        StatsPerformanceLog = 459,
        // Elsewhere = 460,
        // Elsewhere = 461,

        // Cancellation
        // Elsewhere = 470,

        // Elsewhere = 471,
        UnexpectedConditionLocal = 472,
        UnexpectedConditionTelemetry = 473,
        // Elsewhere = 474,
        // was ServerDeploymentDirectoryHashMismatch = 475,
        // Elsewhere = 476,

        PipProcessDisallowedNtCreateFileAccessWarning = 480,

        // was PipProcessChildrenSurvivedWarning = 499,
        // Elsewhere = 500,
        // Elsewhere = 501,

        // Elsewhere = 502,
        // Elsewhere = 503,
        // Elsewhere = 504,
        // was PipProcessAllowedMissingOutputs = 505,

        // USN/Change Journal usage (FileChangeTracker)
        StartLoadingChangeTracker = 680,
        StartScanningJournal = 681,
        ScanningJournal = 682, // was EndSavingChangeTracker = 682,
        // was ChangeTrackerNotLoaded = 683,
        // was ScanningJournalError = 684,
        // Elsewhere  = 685,
        EndScanningJournal = 686,
        // Elsewhere  = 687,
        DisableChangeTracker = 688,

        // Storage
        // Elsewhere  = 698,
        // Elsewhere  = 699,
        // Elsewhere  = 700, // was StorageFileContentTableIgnoringFileSinceUsnJournalDisabled
        // Elsewhere  = 701,
        // Elsewhere = 702,
        // Elsewhere = 703,
        // Elsewhere  = 704,
        // Elsewhere  = 705,
        // Elsewhere = 706,
        // Reserved = 707,
        // Elsewhere = 708,
        // Reserved = 709,
        // Reserved = 710,
        // Elsewhere = 711,
        // Elsewhere = 712,

        // USNs
        // Elsewhere  = 713,
        // Elsewhere  = 714,
        // Elsewhere  = 715,
        // Elsewhere  = 716,
        // Elsewhere  = 717,
        // Elsewhere  = 718,
        // Elsewhere  = 719, // StorageJournalDisabledMiss

        // Elsewhere  = 720,
        // Elsewhere  = 721,
        // Elsewhere  = 722,
        // Elsewhere  = 723,
        // Elsewhere  = 724,

        // Elsewhere  = 725,
        // Elsewhere = 726,
        // Elsewhere = 727,

        // Elsewhere = 728,

        // Elsewhere  = 729,

        // Elsewhere  = 730,
        // Elsewhere = 731,
        // Elsewhere  = 732,
        // Elsewhere  = 733,
        // Elsewhere  = 734,
        // Elsewhere  = 735,
        // Elsewhere  = 736,
        // Elsewhere = 737,
        // Elsewhere = 738,
        // Elsewhere = 739,

        // Elsewhere  = 740,
        // Elsewhere  = 741,

        // Elsewhere  = 742,
        // Elsewhere  = 743,

        RetryOnFailureException = 744,

        // Elsewhere = 745,

        // Elsewhere  = 746,
        // was IncorrectExistsAsFileThroughPathMappings = 747,
        // Elsewhere  = 748,

        // Additional Process Isolation
        // Elsewhere  = 800,
        // Elsewhere  = 801,
        // Elsewhere  = 802,
        Process = 803,

        
        // Elsewhere = 874
        // Elsewhere = 875
        // Elsewhere = 876
        // Elsewhere = 877
        // Elsewhere = 878

        // Config
        // ConfigUnsafeDisabledFileAccessMonitoring = 900,
        // ConfigUnsafeIgnoringChangeJournal = 901,
        // ConfigUnsafeUnexpectedFileAccessesAsWarnings = 902,
        // ConfigUnsafeMonitorNtCreateFileOff = 903,
        // // Elsewhere  = 904,
        // ConfigIgnoreReparsePoints = 905,
        // JournalRequiredOnVolumeError = 906,
        // ConfigFailedParsingCommandLinePipFilter = 907,
        // ConfigFailedParsingDefaultPipFilter = 908,
        // ConfigUsingExperimentalOptions = 909,
        // ConfigIgnoreZwRenameFileInformation = 910,
        // ConfigArtificialCacheMissOptions = 911,
        // ConfigExportGraphRequiresScheduling = 912,
        // ConfigUsingPipFilter = 913,
        // ConfigFilterAndPathImplicitNotSupported = 914,
        // // Elsewhere  = 915,
        // ConfigIgnoreDynamicWritesOnAbsentProbes = 916,
        // ConfigIgnoreSetFileInformationByHandle = 917,
        // ConfigPreserveOutputs = 918,
        // ConfigUnsafeLazySymlinkCreation = 919,
        // ConfigDisableDetours = 920,
        // ConfigDebuggingAndProfilingCannotBeSpecifiedSimultaneously = 921,
        // ConfigIgnoreGetFinalPathNameByHandle = 922,
        // ConfigIgnoreZwOtherFileInformation = 923,
        // ConfigUnsafeMonitorZwCreateOpenQueryFileOff = 924,
        // ConfigIgnoreNonCreateFileReparsePoints = 925,
        // ConfigUnsafeDisableCycleDetection = 926,
        // ConfigUnsafeExistingDirectoryProbesAsEnumerations = 927,
        // ConfigUnsafeAllowMissingOutput = 928,
        // ConfigIgnoreValidateExistingFileAccessesForOutputs = 929,
        // ConfigUnsafeIgnoreUndeclaredAccessesUnderSharedOpaques = 930,
        // ConfigUnsafeOptimizedAstConversion = 931,

        // // Elsewhere  = 932,

        // ConfigIncompatibleIncrementalSchedulingDisabled = 933,
        // ConfigIncompatibleOptionWithDistributedBuildError = 934,
        // ConfigIgnorePreloadedDlls = 935,
        // ConfigIncompatibleOptionWithDistributedBuildWarn = 936,

        // WarnToNotUsePackagesButModules = 937,
        // WarnToNotUseProjectsField = 938,

        // ConfigIgnoreCreateProcessReport = 939,

        // RESERVED TO [950, 960] (BuildXL.Frontend.Sdk)

        // Reserved = 1005,
        // Reserved = 1006,

        // Perf instrumentation
        // FREE SLOT = 1500,
        // FREE SLOT = 1501,
        

        // FREE SLOT = 1505,
        StartViewer = 1506,
        UnableToStartViewer = 1507,
        Memory = 1508,
        // Reserved = 1509,
        // Elsewhere = 1510,
        UnableToLaunchViewer = 1511,
        // Elsewhere = 1512,
        // Elsewhere = 1513,
        // Elsewhere = 1514,
        // Elsewhere = 1515,

        // Scheduler Pip Validation
        /// Elsewhere = 2000,
        // Elsewhere = 2001,
        // Elsewhere = 2002,
        // Elsewhere = 2003,
        // Elsewhere = 2004,
        // Elsewhere = 2005,

        // FREE SLOT = 2005,

        // Free slot 2100,
        // Reserved = 2101,

        // Elsewhere = 2102,

        FileCombinerVersionIncremented = 2103,
        // Elsewhere  = 2104,
        // SpecCacheDisabledForNoSeekPenalty = 2105,
        // Elsewhere  = 2106,
        SpecCache = 2107,
        // Elsewhere  = 2108,

        // Temp files/directory cleanup
        // Elsewhere = 2200,
        // Elsewhere  = 2201,
        // Elsewhere = 2202,
        // Elsewhere  = 2203,
        // Elsewhere = 2204,
        // Elsewhere  = 2205,
        // Elsewhere  = 2206,

        // Elsewhere  = 2210,

        // Engine Errors
        // was: EngineRunErrorDuplicateMountPaths = 2500,
        // ErrorSavingSnapshot = 2501,
        // Reserved = 2502,
        // EngineErrorSavingFileContentTable = 2503,
        // Elsewhere  = 2504,
        // was: EngineRunErrorDuplicateMountNames = 2506,
        // GenericSnapshotError = 2507,
        // ErrorCaseSensitiveFileSystemDetected = 2508,

        // Dealing with MAX_PATH issues
        PathHashed = 2600,
        // was: PipProcessTempDirectoryTooLong = 2601,
        // Elsewhere = 2602,
        // Elsewhere  = 2603,
        // Elsewhere = 2604,

        // Elsewhere = 2610,

        // MLAM
        // Elsewhere = 2700,
        // Elsewhere = 2701,
        // Elsewhere = 2702,
        // Elsewhere = 2703,
        // Elsewhere = 2704,
        // Elsewhere = 2705,
        // Elsewhere = 2706,
        // Elsewhere = 2707,
        // Elsewhere = 2708,
        // Elsewhere = 2709,
        // Elsewhere = 2710,
        // Elsewhere = 2711,
        // Elsewhere = 2712,
        // Elsewhere = 2713,
        // Elsewhere = 2714,

        // Two-phase fingerprinting
        // Elsewhere = 2715,
        // Elsewhere = 2716,
        // Elsewhere = 2717,
        // Elsewhere = 2718,
        // Elsewhere = 2719,
        // Elsewhere = 2720,
        // Elsewhere = 2721,
        // Elsewhere = 2722,
        // Elsewhere = 2723,
        // Elsewhere = 2724,
        // Elsewhere = 2725,
        // Elsewhere = 2726,
        // Elsewhere = 2727,
        // Elsewhere = 2728,
        // Elsewhere = 2729,
        // Elsewhere = 2730,
        // Elsewhere = 2731,
        // Elsewhere = 2732,
        // Elsewhere  = 2733,

        // RESERVED TO [2800, 2899] (BuildXL.Engine.dll)

        // RESERVED TO [2900, 2999] (BuildXL.Engine.dll)

        // Engine phase markers for journal service init
        // Elsewhere  = 2900,
        // Elsewhere  = 2901,
        JournalServiceNotInstalled = 2902,
        // Elsewhere  = 2903,
        // Elsewhere  = 2904,
        // Elsewhere  = 2905,
        // Elsewhere  = 2906,
        // Elsewhere  = 2907,
        UserRefusedElevation = 2908,
        // was FailedCheckingDirectJournalAccess = 2909,
        // Elsewhere  = 2910,
        // Elsewhere  = 2911,
        // Elsewhere  = 2912,
        // Elsewhere  = 2913,
        // Elsewhere = 2914,
        // Elsewhere = 2915,
        // Elsewhere = 2916,

        MaterializingFileToFileDepdencyMap = 2917,
        ErrorMaterializingFileToFileDepdencyMap = 2918,

        // Elsewhere  = 2919,
        // Elsewhere  = 2920,

        // Elsewhere = 2922,
        // Elsewhere  = 2923,
        // Elsewhere = 2924,
        // Elsewhere  = 2925,
        // RESERVED 2926
        // Elsewhere  = 2927,
        // Elsewhere  = 2928,
        // Elsewhere = 2929,


        // Elsewhere  = 2945,
        // Elsewhere  = 2946,
        // Elsewhere  = 2947,

        // Free: 3000..3053

        
        // Elsewhere = 3110,
        // Elsewhere = 3111,
        // Elsewhere = 3112,
        // Elsewhere = 3113,
        // Elsewhere = 3114,

        // Pip state initialization
        // Elsewhere = 3115,
        // Elsewhere = 3116,
        

        #region ASSEMBLY RESERVED (3200-3599): BuildXL.Engine.dll

        // DominoEngineStart = 3200,

        // TODO: Move to BuildXL.Engine
        // Distributed Build
        // ErrorUnableToCacheGraphDistributedBuild = DominoEngineStart, // 3200
        // ErrorCacheDisabledDistributedBuild = 3201,
        // Was ErrorUsingCacheServiceDistributedBuild = 3202,
        // Elsewhere  = 3203,
        // NonDeterministicPipOutput = 3204,
        // NonDeterministicPipResult = 3205,
        // Elsewhere  = 3206,
        // EnvironmentVariablesImpactingBuild = 3207,
        // Reserved = 3208,
        // Reserved = 3209,
        // Reserved = 3210,
        // Was ErrorUsingNewCacheWithDistributedBuild = 3211,
        // SchedulerExportFailedSchedulerNotInitialized = 3212,
        // Was ErrorUsingTwoPhaseFingerprintingWithDistributedBuild = 3213,
        // MountsImpactingBuild = 3214,

        // DominoEngineEnd = 3599,

        #endregion ASSEMBLY RESERVED (3200-3599): BuildXL.Engine.dll

        #region ASSEMBLY RESERVED (3600-3999): BuildXL.Scheduler.dll

        // Elsewhere  = 3600,
        // Elsewhere  = 3999,

        #endregion ASSEMBLY RESERVED (3600-3999): BuildXL.Scheduler.dll

        // Change journal service
        // Elsewhere  = 4000,
        // Elsewhere  = 4001,
        // Elsewhere  = 4002,
        // Elsewhere  = 4003,
        // Elsewhere  = 4004,
        // Elsewhere  = 4005,
        // Elsewhere  = 4006,
        // Elsewhere  = 4007,
        // Elsewhere  = 4008,

        #region Assembly-level reserved ranges (4100-5000)

        #region ASSEMBLY RESERVED (4100-4199): BuildXL.Utilities.dll

        DominoUtilitiesStart = 4100,
        DominoUtilitiesEnd = 4199,

        #endregion ASSEMBLY RESERVED (4100-4199): BuildXL.Utilities.dll

        #region ASSEMBLY RESERVED (4200-4299): BuildXL.Storage.dll

        DominoStorageStart = 4200,
        // was StorageFileContentTableIncorrectPathMapping = 4201,
        ValidateJunctionRoot = 4202,
        // was IncorrectExistenceCheckThroughJournal = 4203,
        // was StorageFileContentTableIncorrectPathMappingContentMismatch = 4204,
        ConflictDirectoryMembershipFingerprint = 4205,
        DominoStorageEnd = 4299,

        #endregion ASSEMBLY RESERVED (4200-4299): BuildXL.Storage.dll

        #region ASSEMBLY RESERVED (4300-4399): bxl.exe

        DominoApplicationStart = 4300,
        // Elswhere = 4301,
        // Elsewhere = 4302,
        // Elsewhere = 4303,

        DominoApplicationEnd = 4399,

        #endregion ASSEMBLY RESERVED (4300-4399): bxl.exe

        #region ASSEMBLY RESERVED (4400-4499): BuildXL.Processes.dll

        DominoProcessesStart = 4400,
        PipProcessUncacheableWhitelistNotAllowedInDistributedBuilds = 4401,
        DominoProcessesEnd = 4499,

        #endregion ASSEMBLY RESERVED (4400-4499): BuildXL.Processes.dll

        #region ASSEMBLY RESERVED (4500-4499): BuildXL.Pips.dll

        DominoPipsStart = 4500,
        DominoPipsEnd = 4599,

        #endregion Assembly-level reserved ranges (4100-5000)

        #endregion Assembly-level reserved ranges (4100-5000)

        #region ASSEMBLY RESERVED (5000-5050): BuildXL.Scheduler.dll

        // RESERVED TO [5000, 5050] (BuildXL.Scheduler.dll)
        #endregion ASSEMBLY RESERVED (5000-5050): BuildXL.Scheduler.dll

        // was SchedulerAskedToWaitForUnknownValue = 6213,
        // was SchedulerAskedToWaitForUnscheduledValue = 6215,
        Statistic = 6300,
        // was PerformanceSnapshot = 6301,
        // Elsewhere = 6302,
        // PerformanceSample = 6303,
        StatisticWithoutTelemetry = 6304,
        BulkStatistic = 6305,
        FinalStatistics = 6306,
        LoggerStatistics = 6307,
        PipCounters = 6308,

        // DEPRECATED SlowestElementsStatistic = 6307,
        #region ASSEMBLY RESERVED (7000 - 7050): BuildXL.Engine.dll (distribution)

        // Moved to BuildXL.Engine
        // NOTE: Do not add more events here. Events should be added to BuildXL.Engine.Tracing.LogEventId

        // This event is needed by TrackingEventListener. It is defined here instead of in BuildXL.Engine.Tracing.LogEventId
        // in order to avoid taking a dependency we don't want
        // DistributionWorkerForwardedError = 7015,
        #endregion

        #region ASSEMBLY RESERVED (7500-8000): DScript.Ast.dll
        #endregion

        // Change detection (FileChangeTrackingSet)
        // Elsewhere  = 8001,
        // Elsewhere  = 8002,
        // Elsewhere  = 8003,
        // Elsewhere  = 8004,
        // was ChangeDetectionSaveTrackingSet = 8005,

        // Elsewhere  = 8006,
        ChangeDetectionCreateResult = 8007,

        // Elsewhere  = 8008,
        // Elsewhere  = 8009,
        // Elsewhere  = 8010,

        // Elsewhere  = 8011,
        // Elsewhere  = 8012,
        // Elsewhere  = 8013,
        // Elsewhere  = 8014,

        // Elsewhere  = 8015,
        // Elsewhere  = 8016,
        // Elsewhere  = 8017,
        // Elsewhere  = 8018,

        // Elsewhere  = 8019,
        // Elsewhere  = 8020,
        // Elsewhere  = 8021,
        // Elsewhere  = 8022,
        // Elsewhere  = 8023,
        // Elsewhere  = 8024,

        // Elsewhere  = 8025,
        // Elsewhere  = 8026,
        // Elsewhere  = 8027,
        // Elsewhere  = 8028,
        // Elsewhere  = 8029,

        // Elsewhere  = 8030,
        // Elsewhere  = 8031,
        // Elsewhere  = 8032,

        // Incremental scheduling
        // Elsewhere = 8050,
        // Elsewhere = 8051,
        // Elsewhere = 8052,
        // Elsewhere = 8053,
        // Elsewhere = 8054,
        // Elsewhere = 8055,
        // Elsewhere = 8056,
        // Elsewhere = 8057,
        // Elsewhere = 8058,
        // Elsewhere = 8059,
        // Elsewhere = 8060,
        // Elsewhere = 8061,
        // Elsewhere FREE SLOT 8062
        // Elsewhere = 8063,
        // Elsewhere = 8064,
        // Elsewhere = 8065,
        // Elsewhere = 8066,
        // Elsewhere = 8067,
        // Elsewhere = 8068,
        // Elsewhere = 8069,
        // Elsewhere = 8070,
        // Elsewhere = 8071,
        // Elsewhere = 8072,
        // Elsewhere = 8073,
        // Elsewhere = 8074,
        // Elsewhere = 8075,
        // Elsewhere = 8076,
        // Elsewhere = 8077,
        // Elsewhere = 8078,
        // Elsewhere = 8079,
        // Elsewhere = 8080,
        // Elsewhere = 8081,
        // Elsewhere = 8082,

        // Server mode
        // Elsewhere = 8100,
        // Elsewhere = 8101,
        // Elsewhere = 8102,
        // Elsewhere = 8103,
        // Elsewhere = 8104,
        // Elsewhere = 8105,
        // Elsewhere = 8106,

        // Next free section
        #region ASSEMBLY RESERVED (9000-9899): BuildXL.FrontEnd.Script.dll
        #endregion

        #region ASSEMBLY RESERVED (9900-9999): BuildXL.FrontEnd.Script.Debugger.dll
        #endregion

        // Testing
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

        // Engine state logging
        

        // Detours
        // Elsewhere  = 10100,
        // Elsewhere  = 10101,
        // Elsewhere  = 10102,
        LogMacKextFailure = 10103,

        // reserved 11200 .. 11300 for the FrontEndHost
        // reserved 11300 .. 11400 for the Nuget FrontEnd
        // reserved 11400 .. 11500 for the MsBuild FrontEnd

        // CloudBuild events
        DominoCompletedEvent = 11500,
        TargetAddedEvent = 11501,
        TargetRunningEvent = 11502,
        TargetFailedEvent = 11503,
        TargetFinishedEvent = 11504,
        DominoInvocationEvent = 11505,
        DropCreationEvent = 11506,
        DropFinalizationEvent = 11507,
        DominoContinuousStatisticsEvent = 11508,

        // reserved 11550 .. 11600 for ninja
        // reserved 11600 .. 11700 for the Download FrontEnd

        // Service pip scheduling
        ServicePipStarting = 12000,
        ServicePipShuttingDown = 12001,
        ServicePipTerminatedBeforeStartupWasSignaled = 12002,
        ServicePipFailed = 12003,
        ServicePipShuttingDownFailed = 12004,
        IpcClientForwardedMessage = 12005,
        IpcClientFailed = 12006,

        // BuildXL API server
        // Elsewhere = 12100,
        // Elsewhere = 12101,
        // Elsewhere = 12102,
        // Elsewhere = 12103,
        // Elsewhere = 12104,
        // Elsewhere = 12105,
        // Elsewhere = 12106,
        // Elsewhere = 12107,
        // Elsewhere = 12108,

        // Copy file cont'd.
        // Elsewhere = 12201,

        // Container related errors
        // Elsewhere  = 12202,
        // Elsewhere  = 12203,
        // Elsewhere  = 12204,
        // Elsewhere = 12205,
        // Elsewhere = 12206,
        // Elsewhere = 12207,
        // Elsewhere = 12208,
        // Elsewhere  = 12209,
        // Elsewhere  = 12210,
        // Elsewhere  = 12211,
        // Elsewhere = 12212,

        // Status logging
        Status = 12400,
        StatusSnapshot = 12401,
        StatusHeader = 12402,
        StatusCallbacksDelayed = 12403,

        // Determinism probe to detect nondeterministic PIPs
        // Elsewhere = 13000,
        // Elsewhere = 13001,
        // Elsewhere = 13002,
        // Elsewhere = 13003,
        // Elsewhere = 13004,
        // Elsewhere = 13005,
        // Elsewhere = 13006,
        // Elsewhere = 13007,

        // Pip validation continued.
        // Elsewhere = 13100,
        // Elsewhere = 13101,
        // Elsewhere = 13102,

        // Cache initialization
        // CacheIsStillBeingInitialized = 13200,

        // FingerprintStore saving
        // Elsewhere = 13300,
        // Elsewhere = 13301,
        // Elsewhere = 13302,
        // Elsewhere = 13303,

        // Smell events
        // Elsewhere = 14001,
        // Elsewhere = 14002,
        // Elsewhere = 14003,
        // Elsewhere = 14004,
        // Elsewhere = 14005,
        // Elsewhere = 14006,
        // Elsewhere = 14007,
        // Elsewhere  = 14008,
        // Elsewhere  = 14009,
        // Elsewhere = 14010,
        // Elsewhere = 14011,
        // Elsewhere = 14012,
        // Elsewhere = 14013,
        // Elsewhere = 14014,
        // Elsewhere = 14015,

        // Graph validation.
        // Elsewhere = 14100,
        // Elsewhere = 14101,
        // Elsewhere = 14102,
        // Elsewhere = 14103,
        // Elsewhere = 14104,
        // Elsewhere = 14105,
        // Elsewhere = 14106,
        // Elsewhere = 14107,
        // Elsewhere = 14108,
        // Elsewhere = 14109,
        // Elsewhere = 14110,
        // Elsewhere = 14111,
        // Elsewhere = 14112,
        // Elsewhere = 14113,

        // Dirty build
        // Elsewhere = 14200,
        // Elsewhere = 14201,
        // Elsewhere = 14202,
        // Elsewhere = 14203,
        // Elsewhere = 14204,

        // Build set calculator
        // Elsewhere = 14210,
        // Elsewhere = 14211,
        // Elsewhere = 14212,

        // Special tool errors
        // Elsewhere  = 14300,

        // Elsewhere = 14400,
        // Sandbox kernel extension connection manger errors
        // Elsewhere = 14500,
        // Elsewhere = 14501,
        // Elsewhere = 14502,
        // Elsewhere = 14503,
        // Elsewhere = 14504,
        // Elsewhere = 14505,

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
