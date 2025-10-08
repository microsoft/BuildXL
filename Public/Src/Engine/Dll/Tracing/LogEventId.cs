// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Engine.Tracing
{
    // disable warning regarding 'missing XML comments on public API'. We don't need docs for these values
#pragma warning disable 1591

    /// <summary>
    /// Defines event IDs corresponding to events in <see cref="Logger" />
    /// </summary>
    public enum LogEventId : ushort
    {
        None = 0,

        FilterDetails = 25,
        AssignProcessToJobObjectFailed = 29,
        ObjectCacheStats = 33,
        StartParseConfig = 35,
        EndParseConfig = 36,
        BusyOrUnavailableOutputDirectories = 40,
        StatsBanner = 60,
        GCStats = 61,
        ObjectPoolStats = 62,

        InterningStats = 63,
        FrontEndStatsBanner = 73,
        PipProcessResponseFileCreationFailed = 74,
        PipTableStats = 75,
        PipWriterStats = 76,
        PipTableDeserializationContext = 84,
        EnvironmentValueForTempDisallowed = 94,
        ErrorRelatedLocation = 110,
        BusyOrUnavailableOutputDirectoriesRetry = 214,
        FileAccessAllowlistCouldNotCreateIdentifier = 270,
        FileAccessAllowlistFailedToParsePath = 274,
        AllowlistFileAccess = 278,
        FileAccessManifestSummary = 279,
        FileAccessAllowlistEntryHasInvalidRegex = 287,
        EnvFreezing = 402,

        StorageCacheStartupError = 712,
        // Scrubbing/Cleaning
        ScrubbingExternalFileOrDirectoryFailed = 850,
        ScrubbingFailedToEnumerateDirectory = 851,
        ScrubbingStarted = 852,
        ScrubbingFinished = 853,
        ScrubbableMountsMayOnlyContainScrubbableMounts = 854,
        ScrubbingFailedBecauseDirectoryIsNotScrubbable = 855,
        ScrubbingDirectory = 856,
        ScrubbingDeleteDirectoryContents = 857,
        ScrubbingFile = 858,
        ScrubbingStatus = 859,

        CleaningStarted = 860,
        CleaningFinished = 861,
        CleaningFileFailed = 862,
        CleaningOutputFile = 863,
        CleaningDirectoryFailed = 864,

        ScrubbingFailedToEnumerateMissingDirectory = 865,
        ConfigUnsafeSharedOpaqueEmptyDirectoryScrubbingDisabled = 866,

        DeletingOutputsFromSharedOpaqueSidebandFilesStarted = 867,
        DeletingSharedOpaqueSidebandFilesStarted = 868,
        ScrubbingProgress = 869,
        SidebandFileIntegrityCheckThrewException = 870,
        SidebandIntegrityCheckForProcessFailed = 871,
        PostponingDeletionOfSharedOpaqueOutputs = 872,
        DeletingOutputsFromExtraneousSidebandFilesStarted = 873,

        ScrubbingCancelled = 880,

        ConfigUnsafeDisabledFileAccessMonitoring = 900,
        ConfigUnsafeIgnoringChangeJournal = 901,
        ConfigUnsafeUnexpectedFileAccessesAsWarnings = 902,
        // was ConfigUnsafeMonitorNtCreateFileOff = 903,
        ConfigIgnoreReparsePoints = 905,
        JournalRequiredOnVolumeError = 906,
        ConfigFailedParsingCommandLinePipFilter = 907,
        ConfigFailedParsingDefaultPipFilter = 908,
        ConfigUsingExperimentalOptions = 909,
        // was ConfigIgnoreZwRenameFileInformation = 910,
        ConfigArtificialCacheMissOptions = 911,
        // was ConfigExportGraphRequiresScheduling = 912,
        ConfigUsingPipFilter = 913,
        ConfigFilterAndPathImplicitNotSupported = 914,
        ConfigIgnoreDynamicWritesOnAbsentProbes = 916,
        ConfigIgnoreSetFileInformationByHandle = 917,
        ConfigPreserveOutputs = 918,
        // was ConfigUnsafeLazySymlinkCreation = 919,
        ConfigDisableDetours = 920,
        ConfigDebuggingAndProfilingCannotBeSpecifiedSimultaneously = 921,
        ConfigUnsafeRunPathTranslationForGetFinalPathNameByHandle = 922,
        ConfigUnsafeRunPathTranslationForDeviceIoControlGetReparsePoint = 923,
        // was ConfigUnsafeMonitorZwCreateOpenQueryFileOff = 924,
        // was ConfigIgnoreNonCreateFileReparsePoints = 925,
        ConfigUnsafeDisableCycleDetection = 926,
        ConfigUnsafeExistingDirectoryProbesAsEnumerations = 927,
        ConfigUnsafeAllowMissingOutput = 928,
        // was ConfigIgnoreValidateExistingFileAccessesForOutputs = 929,
        // was ConfigUnsafeIgnoreUndeclaredAccessesUnderSharedOpaques = 930,
        ConfigUnsafeOptimizedAstConversion = 931,
        ConfigIncompatibleIncrementalSchedulingDisabled = 933,
        ConfigIncompatibleOptionWithDistributedBuildError = 934,
        //was ConfigIgnorePreloadedDlls = 935,
        ConfigIncompatibleOptionWithDistributedBuildWarn = 936,

        WarnToNotUsePackagesButModules = 937,
        WarnToNotUseProjectsField = 938,

        // was ConfigIgnoreCreateProcessReport = 939,
        // was ConfigProbeDirectorySymlinkAsDirectory = 940,
        ConfigUnsafeAllowDuplicateTemporaryDirectory = 941,
        ConfigIgnoreFullReparsePointResolving = 942,
        ConfigUnsafeSkipFlaggingSharedOpaqueOutputs = 943,
        ConfigUnsafeIgnorePreserveOutputsPrivatization = 944,
        ConfigIncompatibleOptionIgnorePreserveOutputsPrivatization = 945,
        ConfigIncompatibleOptionBuildTimeoutMinsAndCbTimeout = 7142,
        PipTimedOutRemotely = 946,
        ConfigAssumeCleanOutputs = 947,
        ConfigIgnoreUntrackedPathsInFullReparsePointResolving = 948,

        StartInitializingCache = 1502,
        EndInitializingCache = 1503,
        SynchronouslyWaitedForCache = 1504,


        // Scheduler Pip Validation
        /// Elsewhere = 2000,
        // Elsewhere = 2001,
        CannotAddCreatePipsDuringConfigOrModuleEvaluation = 2002,
        SpecCacheDisabledForNoSeekPenalty = 2105,
        ErrorSavingSnapshot = 2501,
        EngineErrorSavingFileContentTable = 2503,
        GenericSnapshotError = 2507,
        ErrorCaseSensitiveFileSystemDetected = 2508,
        // RESERVED TO [2800, 2899] (BuildXL.Engine.dll)

        // Graph caching
        StartCheckingForPipGraphReuse = 2800,
        EndCheckingForPipGraphReuse = 2801,
        StartDeserializingPipGraph = 2802,
        EndDeserializingEngineState = 2803,
        StartSerializingPipGraph = 2804,
        EndSerializingPipGraph = 2805,
        FailedToDeserializePreviousInputs = 2806,
        FailedToDeserializePipGraph = 2807,
        FailedToSerializePipGraph = 2808,
        GraphNotReusedDueToChangedInput = 2809,
        SerializedFile = 2810,
        DeserializedFile = 2811,
        EngineCachePrefersLoadingInMemoryForSeekPenalty = 2812,
        FailedToSaveGraphToCache = 2813,
        FailedToFetchGraphDescriptorFromCache = 2814,
        FailedToFetchSerializedGraphFromCache = 2815,
        FailedToComputeGraphFingerprint = 2816,
        MatchedCompatibleGraphFingerprint = 2817,
        MatchedExactGraphFingerprint = 2818,
        PipGraphIdentfier = 2819,
        PipGraphByPathFailure = 2820,
        PipGraphByIdFailure = 2821,
        CacheShutdownFailed = 2822,
        CouldNotCreateSystemMount = 2823,
        DirectoryMembershipFingerprinterRuleError = 2824,
        FailedToDuplicateGraphFile = 2825,

        // Phases
        // Reserved in BuildXL.FrontEnd.Core for LoadConfigPhaseStart = 2826,
        // Reserved in BuildXL.FrontEnd.Core for LoadConfigPhaseComplete = 2827,
        // Reserved in BuildXL.FrontEnd.Core for InitializeResolversPhaseStart = 2828,
        // Reserved in BuildXL.FrontEnd.Core for InitializeResolversPhaseComplete = 2829,
        ParsePhaseStart = 2830,
        ParsePhaseComplete = 2831,
        StartEvaluateValues = 2832,
        EndEvaluateValues = 2833,
        ScheduleConstructedWithConfiguration = 2845,
        StartExecute = 2834,
        EndExecute = 2835,

        ReusedEngineState = 2836,
        DisposedEngineStateDueToGraphId = 2837,

        FileAccessErrorsExist = 2838,

        CacheSessionCloseFailed = 2839,

        FailedToComputeHashFromDeploymentManifest = 2840,
        VirusScanEnabledForPath = 2841,
        FailedToComputeHashFromDeploymentManifestReason = 2842,
        ElementsOfConfigurationFingerprint = 2843,

        InputTrackerUnableToDetectChangeInEnumeratedDirectory = 2844,

        NonReadableConfigMountsMayNotContainReadableModuleMounts = 2850,
        NonWritableConfigMountsMayNotContainWritableModuleMounts = 2851,
        ModuleMountsWithSameNameAsConfigMountsMustHaveSamePath = 2852,
        ModuleMountsWithSamePathAsConfigMountsMustHaveSameName = 2853,
        MountHasInvalidName = 2854,
        MountHasInvalidPath = 2855,

        InputTrackerHasMismatchedGraphFingerprint = 2856,
        InputTrackerHasUnaccountedDirectoryEnumeration = 2857,
        InputTrackerDetectedEnvironmentVariableChanged = 2858,
        InputTrackerUnableToDetectChangedInputFileByCheckingContentHash = 2859,
        InputTrackerDetectedChangedInputFileByCheckingContentHash = 2860,
        InputTrackerDetectedChangeInEnumeratedDirectory = 2861,
        StartVisitingSpecFiles = 2862,
        EndVisitingSpecFiles = 2863,
        JournalDetectedNoInputChanges = 2864,
        JournalDetectedInputChanges = 2865,
        JournalProcessingStatisticsForGraphReuseCheck = 2866,

        // Reserved = 2867,
        CheckingForPipGraphReuseStatus = 2868,
        CacheInitialized = 2869,
        // Reserved = 2870,

        PreserveOutputsNotAllowedInDistributedBuild = 2871,
        PreserveOutputsWithNewSalt = 2872,
        PreserveOutputsWithExistingSalt = 2873,
        PreserveOutputsFailedToInitializeSalt = 2874,
        // was PreserveOutputsRequiresTwoPhaseFingerprinting = 2875,

        FailedToDeserializeDueToFileNotFound = 2876,
        FailedToInitalizeFileAccessAllowlist = 2877,
        FailedToCreateEngineOutputDirectories = 2878,

        FetchedSerializedGraphFromCache = 2879,
        DuplicateDirectoryMembershipFingerprinterRule = 2880,
        FinishedCopyingGraphToLogDir = 2881,
        InvalidDirectoryTranslation = 2882,

        UsingPatchableGraphBuilder = 2883,
        StartDeserializingEngineState = 2884,
        FailedCheckingDirectJournalAccess = 2885,

        CacheRecoverableError = 2886,
        DirectoryTranslationsDoNotPassJunctionTest = 2887,

        JournalProcessingStatisticsForGraphReuseCheckTelemetry = 2888,
        GraphInputArtifactChangesTokensMismatch = 2889,
        JournalDetectedGvfsProjectionChanges = 2890,

        // Reserved = 2891,

        WrittenBuildInvocationToUserFolder = 2892,
        FailedToWriteBuildInvocationToUserFolder = 2893,
        FailedToReadBuildInvocationToUserFolder = 2894,

        // was FailureLaunchingBuildExplorerFileNotFound = 2895,
        // was FailureLaunchingBuildExplorerException = 2896,

        FailedToResolveHistoricMetadataCacheFileName = 2940,
        LoadingHistoricMetadataCacheFailed = 2941,
        SavingHistoricMetadataCacheFailed = 2942,
        HistoricMetadataCacheLoaded = 2943,
        HistoricMetadataCacheSaved = 2944,
        HistoricMetadataCacheModeInvoked = 14541,

        FailedReloadPipGraph = 2986,
        InputTrackerDetectedMountChanged = 2987,


        // Critical Path Suggestions
        StartLoadingHistoricPerfData = 3100,
        EndLoadingHistoricPerfData = 3101,
        StartSavingHistoricPerfData = 3102,
        EndSavingHistoricPerfData = 3103,
        FailedToResolveHistoricDataFileName = 3104,

        // FREE 3105
        LoadingHistoricPerfDataFailed = 3106,
        SavingHistoricPerfDataFailed = 3107,
        HistoricPerfDataLoaded = 3108,
        HistoricPerfDataSaved = 3109,
        StartRehydratingConfigurationWithNewPathTable = 3117,
        EndRehydratingConfigurationWithNewPathTable = 3118,



        ErrorUnableToCacheGraphDistributedBuild = 3200,

        ErrorCacheDisabledDistributedBuild = 3201,
        NonDeterministicPipOutput = 3204,
        NonDeterministicPipResult = 3205,
        EnvironmentVariablesImpactingBuild = 3207,
        SchedulerExportFailedSchedulerNotInitialized = 3212,
        MountsImpactingBuild = 3214,
        DominoEngineEnd = 3599,

        // RESERVED TO [2800, 2899] (BuildXL.Engine.dll)

        // Distribution [7000, 7050]
        DistributionConnectedToWorker = 7000,
        DistributionWorkerChangedState = 7001,
        // Deprecated = 7002,
        DistributionFailedToCallOrchestrator = 7003,
        DistributionInactiveOrchestrator = 7004,
        DistributionStatistics = 7005,
        // DistributionExecutePipFailedNetworkFailure = 7006,
        DistributionWorkerExitFailure = 7007,
        DistributionSuccessfulRetryCallToWorker = 7008,
        DistributionSuccessfulRetryCallToOrchestrator = 7009,
        DistributionWorkerAttachTooSlow = 7010,
        DistributionAttachReceived = 7011,
        DistributionExitReceived = 7012,
        DistributionTryMaterializeInputsFailedRetry = 7013,
        DistributionTryMaterializeInputsSuccessfulRetry = 7014,

        DistributionWorkerUnexpectedFailureAfterOrchestratorExits = 7017,
        DistributionWorkerFinish = 7018,
        //DistributionWorkerExecutePipRequest = 7019,
        //DistributionWorkerFinishedPipRequest = 7020,
        DistributionWorkerCouldNotLoadGraph = 7021,
        //DistributionFailedToRetrieveValidationContentFromWorkerCache = 7022,
        //DistributionFailedToRetrieveValidationContentFromWorkerCacheWithException = 7023,
        DistributionWorkerPipOutputContent = 7027,
        //DistributionPipFailedOnWorker = 7028,
        GrpcTrace = 7029,
        //DistributionFailedToRetrieveValidationContentFromWorkerCacheWithException = 7030,
        DistributionDisableServiceProxyInactive = 7031,
        DistributionWaitingForOrchestratorAttached = 7032,
        DistributionCallWorkerCodeException = 7033,
        DistributionCallOrchestratorCodeException = 7034,
        Custom = 7035,
        DistributionHostLog = 7036,
        DistributionOrchestratorStatus = 7037,
        //DistributionWorkerStatus = 7038,

        DistributionExecutePipFailedNetworkFailureWarning = 7039,
        DistributionWorkerTimeoutFailure = 7040,

        DistributionDebugMessage = 7042,
        DistributionServiceInitializationError = 7043,

        //RemoteWorkerProcessedExecutionBlob = 7045,
        // 7046 in use by SharedLogEventId

        DistributionConnectionTimeout = 7047,
        DistributionConnectionFailure = 7048,
        AttachmentFailureAfterOrchestratorExit = 7049,
        DistributionWorkerOrphanMessage = 7050,

        // Scheduling
        ForceSkipDependenciesOrDistributedBuildOverrideIncrementalScheduling = 7051,
        ForceSkipDependenciesEnabled = 7052,

        // More graph caching
        EngineContextHeuristicOutcomeReuse = 7053,
        EngineContextHeuristicOutcomeSkip = 7054,
        GetPipGraphDescriptorFromCache = 7055,
        StorePipGraphCacheDescriptorToCache = 7056,
        MismatchInputInGraphInputDescriptor = 7057,
        // was MismatchEnvironmentInGraphInputDescriptor = 7058,
        FailedHashingGraphFileInput = 7059,
        FailedComputingFingerprintGraphDirectoryInput = 7060,
        // was MismatchMountInGraphInputDescriptor = 7061,

        DistributionHelloReceived = 7071,
        DistributionHelloNoSlot = 7072,
        DistributionSayingHelloToOrchestrator = 7073,

        FallingBackOnGraphFileCopy = 7080,
        FailedLoadIncrementalSchedulingState = 7081,
        FailedToDuplicateOptionalGraphFile = 7082,

        // Symlink file.
        FailedStoreSymlinkFileToCache = 7100,
        FailedLoadSymlinkFileFromCache = 7101,
        FailedMaterializeSymlinkFileFromCache = 7102,

        // Failure recovery
        FailedToRecoverFailure = 7110,
        FailedToMarkFailure = 7111,
        SuccessfulFailureRecovery = 7112,
        SuccessfulMarkFailure = 7113,

        ScrubbingSharedOpaquesStarted = 7114,
        // was: EmitSpotlightIndexingWarning = 7115,
        FailedToAcquireDirectoryLock = 7116,
        UsingRedirectedUserProfile = 7117,
        FailedToRedirectUserProfile = 7118,
        FailedToAuthorizeVSTSCache = 7119,
        BusyOrUnavailableOutputDirectoriesException = 7120,
        GrpcSettings = 7121,

        FailedToGetJournalAccessor = 7122,

        StartInitializingVm = 7123,
        EndInitializingVm = 7124,
        InitializingVm = 7125,

        ChosenABTesting = 7126,

        ExitOnNewGraph = 7128,

        EngineLoadedFileContentTable = 7150,
        GrpcTraceWarning = 7151,

        DistributionWorkerPendingMessageQueues = 7153,
        DistributionOrchestratorExitBeforeAttachment = 7154,

        LogAndRemoveEngineStateOnBuildFailure = 10011,
        CacheIsStillBeingInitialized = 13200,

        //was: StringTableConfiguration = 7127,

        GrpcServerTraceWarning = 7130,
        GrpcServerTrace = 7131,

        CacheInitializationTakingTooLong = 7132,

        RequiredToolsNotInstalled = 7133,

        DistributionStreamingNetworkFailure = 7134,

        JournalNotEnabledOnVolumeWarning = 7135,

        ErrorCacheInitializationForEngineScheduleConstruction = 7136,
        DistributionExecutePipFailedDistributionFailureWarning = 7137,

        DistributionEventStatsNotMatch = 7138,
        DistributionCompareEventStats = 7139,
        DistributionEventStatsNotFound = 7140,
        DistributionReportExecutionLogFailed = 7141,

        ErrorEBPFCannotStart = 7143,
        ErrorEBPFFailedUnexpectedly = 7144,
        InvalidEBPFRingBufferSizeMultiplier = 7145,

        EBPFCapabilitiesSudoPrompt = 7146,
        CannotSetEBPFCapabilities = 7147,

        // max 7200
    }
}
