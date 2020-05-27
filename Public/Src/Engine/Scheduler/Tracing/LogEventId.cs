// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Scheduler.Tracing
{
    // disable warning regarding 'missing XML comments on public API'. We don't need docs for these values
#pragma warning disable 1591

    /// <summary>
    /// Defines event IDs corresponding to events in <see cref="Logger" />
    /// </summary>
    public enum LogEventId : ushort
    {
        None = 0,

        PipIpcFailed = 5,
        PipWriteFileFailed = 6,
        PipCopyFileFromUntrackableDir = 7,
        PipCopyFileFailed = 8,
        CacheFingerprintHitSources = 47,
        ProcessingPipOutputFileFailed = 52,

        PipInputAssertion = 67,
        PipIpcFailedDueToInvalidInput = 77,

        DeleteFullySealDirectoryUnsealedContents = 244,
        PipsSucceededStats = 227,
        PipsFailedStats = 228,
        StatsPerformanceLog = 459,
        StorageTrackOutputFailed = 745,

        CacheDescriptorHitForContentFingerprint = 200,
        CacheDescriptorMissForContentFingerprint = 201,
        ContentMissAfterContentFingerprintCacheDescriptorHit = 202,
        PipOutputDeployedFromCache = 204,


        InvalidProcessPipDueToExplicitArtifactsInOpaqueDirectory = 219,
        IgnoringUntrackedSourceFileNotUnderMount = 222,
        PipOutputProduced = 223,
        HashedSourceFile = 224,
        TerminatingDueToPipFailure = 225,
        IgnoringPipSinceScheduleIsTerminating = 226,
        FailedToHashInputFile = 229,
        CancelingPipSinceScheduleIsTerminating = 230,
        ProcessesCacheMissStats = 231,
        ProcessesCacheHitStats = 232,
        InvalidCacheDescriptorForContentFingerprint = 233,
        SourceFileHashingStats = 234,
        ProcessPipCacheMiss = 235,
        ProcessPipCacheHit = 236,
        PipFailedDueToFailedPrerequisite = 237,
        CopyingPipOutputToLocalStorage = 238,

        UpdatingCacheWithNewDescriptor = 239,
        PipOutputUpToDate = 242,

        OutputFileStats = 243,

        ProcessStart = 253,
        ProcessEnd = 254,
        CopyFileStart = 255,
        CopyFileEnd = 256,
        WriteFileStart = 257,
        WriteFileEnd = 258,

        FailedToHashInputFileDueToFailedExistenceCheck = 261,
        FailedToHashInputFileBecauseTheFileIsDirectory = 262,

        UnableToCreateExecutionLogFile = 263,
        IgnoringUntrackedSourceFileUnderMountWithHashingDisabled = 265,

        WarningStats = 271,
        PipWarningsFromCache = 272,

        DisallowedFileAccessInSealedDirectory = 277,
        StartSchedulingPipsWithFilter = 280,
        EndSchedulingPipsWithFilter = 281,

        PipSemaphoreQueued = 288,
        PipSemaphoreDequeued = 289,
        CopyingPipInputToLocalStorage = 294,
        ProcessDescendantOfUncacheable = 267,
        ProcessNotStoredToCacheDueToFileMonitoringViolations = 268,
        StorageCacheIngressFallbackContentToMakePrivateError = 726,
        ProcessNotStoredToCachedDueToItsInherentUncacheability = 286,

        ProcessesSemaphoreQueuedStats = 290,
        ScheduleArtificialCacheMiss = 293,
        FileAccessCheckProbeFailed = 297,
        PipQueueConcurrency = 298,
        InvalidInputSinceSourceFileCannotBeInsideOutputDirectory = 299,
        ScheduleProcessConfiguredUncacheable = 300,
        ProcessPipProcessWeight = 301,
        // Shared: CacheMissAnalysis = 312,
        PipExitedUncleanly = 314,
        CacheMissAnalysisException = 315,
        PipStandardIOFailed = 316,

        PipRetryDueToExitedWithAzureWatsonExitCode = 317,

        // Shared: CacheMissAnalysisBatchResults = 325,
        DisallowedFileAccessInTopOnlySourceSealedDirectory = 378,
        ProcessingPipOutputDirectoryFailed = 379,

        PipDirectoryMembershipAssertion = 360,
        DirectoryFingerprintingFilesystemEnumerationFailed = 361,
        PipDirectoryMembershipFingerprintingError = 363,
        DirectoryFingerprintComputedFromFilesystem = 364,
        DirectoryFingerprintComputedFromGraph = 365,
        DirectoryFingerprintExercisedRule = 366,
        PathSetValidationTargetFailedAccessCheck = 367,
        InvalidMetadataStaticOutputNotFound = 368,
        InvalidMetadataRequiredOutputIsAbsent = 369,


        FileMonitoringError = 500,
        FileMonitoringWarning = 501,
        StorageCacheContentHitSources = 503,

        StorageHashedSourceFile = 702,
        StorageUsingKnownHashForSourceFile = 703,
        StorageCacheGetContentError = 708,
        StorageCachePutContentFailed = 711,
        StorageCacheGetContentUsingFallback = 727,
        StorageBringProcessContentLocalWarning = 728,
        StorageCacheGetContentWarning = 737,
        FailedToMaterializeFileWarning = 738,
        MaterializeFilePipProducerNotFound = 739,


        PipDetailedStats = 1510,
        IncrementalBuildSavingsSummary = 1512,
        IncrementalBuildSharedCacheSavingsSummary = 1513,
        RemoteCacheHitsGreaterThanTotalCacheHits = 1514,
        SchedulerDidNotConverge = 1515,

        PipMaterializeDependenciesFailureUnrelatedToCache = 2102,

        PipFailedTempDirectoryCleanup = 2200,
        PipTempCleanerThreadSummary = 2202,
        PipFailedTempFileCleanup = 2204,

        FailPipOutputWithNoAccessed = 2602,
        PipWillBeRetriedDueToExitCode = 2604,
        DetailedPipMaterializeDependenciesFromCacheFailure = 2610,

        FileArtifactContentMismatch = 2700,
        PipOutputNotMaterialized = 2701,
        PipMaterializeDependenciesFromCacheFailure = 2702,
        PipFailedToMaterializeItsOutputs = 2703,
        PipFailedDueToServicesFailedToRun = 2704,
        StartComputingPipFingerprints = 2705,
        StartMaterializingPipOutputs = 2706,
        StartExecutingPips = 2707,
        StartMarkingInvalidPipOutputs = 2708,
        TopDownPipForMaterializingOutputs = 2709,
        BottomUpPipForPipExecutions = 2710,
        TryBringContentToLocalCache = 2711,
        CacheTransferStats = 2712,
        InvalidatedDoneMaterializingOutputPip = 2713,
        PossiblyInvalidatingPip = 2714,

        TwoPhaseCacheDescriptorMissDueToStrongFingerprints = 2715,
        TwoPhaseFailureQueryingWeakFingerprint = 2716,
        TwoPhaseStrongFingerprintComputedForPathSet = 2717,
        TwoPhaseStrongFingerprintMatched = 2718,
        TwoPhaseStrongFingerprintRejected = 2719,
        TwoPhaseStrongFingerprintUnavailableForPathSet = 2720,
        TwoPhaseCacheEntryMissing = 2721,
        TwoPhaseFetchingCacheEntryFailed = 2722,
        TwoPhaseMissingMetadataForCacheEntry = 2723,
        TwoPhaseFetchingMetadataForCacheEntryFailed = 2724,
        TwoPhaseLoadingPathSetFailed = 2725,
        TwoPhasePathSetInvalid = 2726,
        TwoPhaseFailedToStoreMetadataForCacheEntry = 2727,
        TwoPhaseCacheEntryConflict = 2728,
        TwoPhasePublishingCacheEntryFailedWarning = 2729,
        TwoPhaseCacheEntryPublished = 2730,
        ConvertToRunnableFromCacheFailed = 2731,
        TwoPhasePublishingCacheEntryFailedError = 2732,

        LogMismatchedDetoursErrorCount = 2922,
        PipExitedWithAzureWatsonExitCode = 2924,
        OutputFileHashingStats = 2929,

        HistoricPerfDataStats = 3110,
        HistoricPerfDataAdded = 3111,
        HistoricPerfDataUpdated = 3112,
        StartAssigningPriorities = 3113,
        EndAssigningPriorities = 3114,
        StartSettingPipStates = 3115,
        EndSettingPipStates = 3116,

        // RESERVED TO [3600, 3999] (BuildXL.Scheduler.dll)
        ProcessStatus = 3600,
        AbortObservedInputProcessorBecauseFileUntracked = 3601,
        PipFailedDueToDependenciesCannotBeHashed = 3602,
        ScheduleHashedOutputFile = 3603,
        PipFailedDueToOutputsCannotBeHashed = 3604,
        PreserveOutputsFailedToMakeOutputPrivate = 3605,
        PipStatusNonOverwriteable = 3606,
        StoppingProcessExecutionDueToMemory = 3607,
        ResumingProcessExecutionAfterSufficientResources = 3608,
        PipFailedOnRemoteWorker = 3609,

        PipInputVerificationMismatch = 3610,
        PipInputVerificationMismatchExpectedExistence = 3611,
        PipInputVerificationMismatchExpectedNonExistence = 3612,
        PipInputVerificationUntrackedInput = 3613,
        StorageRemoveAbsentFileOutputWarning = 3614,
        StorageCacheCleanDirectoryOutputError = 3615,
        StorageSymlinkDirInOutputDirectoryWarning = 3616,

        PipInputVerificationMismatchRecovery = 3617,
        PipInputVerificationMismatchRecoveryExpectedExistence = 3618,
        PipInputVerificationMismatchRecoveryExpectedNonExistence = 3619,
        UnexpectedlySmallObservedInputCount = 3620,
        HistoricPerfDataCacheTrace = 3621,
        CancellingProcessPipExecutionDueToResourceExhaustion = 3622,
        StartCancellingProcessPipExecutionDueToResourceExhaustion = 3623,

        PipFailedDueToSourceDependenciesCannotBeHashed = 3624,

        // Reserved = 3625,
        PipIsMarkedClean = 3626,
        PipIsMarkedMaterialized = 3627,
        PipIsPerpetuallyDirty = 3628,

        PipFingerprintData = 3629,
        HistoricMetadataCacheTrace = 3630,

        PipIsIncrementallySkippedDueToCleanMaterialized = 3631,

        // Symlink file.
        FailedToCreateSymlinkFromSymlinkMap = 3632,
        FailedLoadSymlinkFile = 3633,
        CreateSymlinkFromSymlinkMap = 3634,
        SymlinkFileTraceMessage = 3635,
        UnexpectedAccessOnSymlinkPath = 3636,

        // Preserved outputs tracker.
        // Reserved = 3640,
        SavePreservedOutputsTracker = 3641,

        AddAugmentingPathSet = 3651,
        AugmentedWeakFingerprint = 3652,
        PipTwoPhaseCacheGetCacheEntry = 3653,
        PipTwoPhaseCachePublishCacheEntry = 3654,
        ScheduleProcessNotStoredToWarningsUnderWarnAsError = 3655,
        // was ScheduleProcessNotStoredDueToMissingOutputs = 3656,
        PipInputVerificationMismatchForSourceFile = 3657,

        // Historic metadata cache warnings
        HistoricMetadataCacheCreateFailed = 3660,
        HistoricMetadataCacheOperationFailed = 3661,
        HistoricMetadataCacheSaveFailed = 3662,
        HistoricMetadataCacheCloseCalled = 3663,
        HistoricMetadataCacheLoadFailed = 3664,

        UnableToGetMemoryPressureLevel = 3665,

        PipCacheMetadataBelongToAnotherPip = 3700,

        PipIpcFailedDueToInfrastructureError = 3701,
        PipTimedOutDueToSuspend = 3702,

        // RESERVED TO [5000, 5050] (BuildXL.Scheduler.dll)

        // Dependency violations / analysis
        DependencyViolationGenericWithRelatedPip = 5000,
        DependencyViolationGeneric = 5001,
        DependencyViolationDoubleWrite = 5002,
        DependencyViolationReadRace = 5003,
        DependencyViolationUndeclaredOrderedRead = 5004,
        DependencyViolationMissingSourceDependencyWithValueSuggestion = 5005,
        DependencyViolationMissingSourceDependency = 5006,
        DependencyViolationUndeclaredReadCycle = 5007,
        DependencyViolationUndeclaredOutput = 5008,
        DependencyViolationReadUndeclaredOutput = 5009,

        // Reserved = 5010,

        DistributionExecutePipRequest = 5011,
        DistributionFinishedPipRequest = 5012,
        DistributionMasterWorkerProcessOutputContent = 5013,
        // DistributionStartDownThrottleMasterLocal = 5014,
        // DistributionStopDownThrottleMasterLocal = 5015,

        CriticalPathPipRecord = 5016,
        CriticalPathChain = 5017,
        LimitingResourceStatistics = 5018,

        // Fingerprint store [5019, 5022]
        FingerprintStoreUnableToCreateDirectory = 5019,
        FingerprintStoreUnableToHardLinkLogFile = 5020,
        FingerprintStoreSnapshotException = 5021,
        FingerprintStoreFailure = 5022,

        DependencyViolationWriteInSourceSealDirectory = 5023,

        // Fingerprint store [5024]
        FingerprintStoreGarbageCollectCanceled = 5024,

        DependencyViolationWriteInUndeclaredSourceRead = 5025,
        DependencyViolationWriteOnAbsentPathProbe = 5026,
        DependencyViolationAbsentPathProbeInsideUndeclaredOpaqueDirectory = 5027,
        RocksDbException = 5028,
        DependencyViolationSharedOpaqueWriteInTempDirectory = 5029,

        // Fingerprint store [5030, 5039]
        FingerprintStoreUnableToOpen = 5030,
        FingerprintStoreUnableToCopyOnWriteLogFile = 5031, // was FingerprintStoreFormatVersionChangeDetected = 5031,

        MovingCorruptFile = 5040,
        FailedToMoveCorruptFile = 5041,
        FailedToDeleteCorruptFile = 5042,
        AbsentPathProbeInsideUndeclaredOpaqueDirectory = 5043,

        AllowedSameContentDoubleWrite = 5044,

        InitiateWorkerRelease = 5045,
        WorkerReleasedEarly = 5046,
        DependencyViolationWriteOnExistingFile = 5047,
        FailedToAddFragmentPipToGraph = 5048,
        ExceptionOnAddingFragmentPipToGraph = 5049,
        ExceptionOnDeserializingPipGraphFragment = 5050,
        DeserializationStatsPipGraphFragment = 5051,
        DebugFragment = 5052,

        PipSourceDependencyCannotBeHashed = 5053,
        WorkerFailedDueToLowDiskSpace = 5054,

        ProblematicWorkerExit = 5070,
        ProcessPipExecutionInfo = 5071,
        ProcessPipExecutionInfoOverflowFailure = 5072,
        CacheOnlyStatistics = 5073,

        PipMaterializeDependenciesFailureDueToVerifySourceFilesFailed = 5080,
        SuspiciousPathsInAugmentedPathSet = 5081,
        PipMaterializeDependenciesFromCacheFailureDueToFileDeletionFailure = 5082,

        JournalProcessingStatisticsForScheduler = 8050,

        IncrementalSchedulingNewlyPresentFile = 8051,
        IncrementalSchedulingNewlyPresentDirectory = 8052,
        IncrementalSchedulingSourceFileIsDirty = 8053,
        IncrementalSchedulingPipIsDirty = 8054,
        IncrementalSchedulingPipIsPerpetuallyDirty = 8055,
        IncrementalSchedulingReadDirtyNodeState = 8056,

        IncrementalSchedulingArtifactChangesCounters = 8057,
        IncrementalSchedulingAssumeAllPipsDirtyDueToFailedJournalScan = 8058,
        IncrementalSchedulingAssumeAllPipsDirtyDueToAntiDependency = 8059,
        IncrementalSchedulingDirtyPipChanges = 8060,
        IncrementalSchedulingProcessGraphChange = 8061,

        IncrementalSchedulingDisabledDueToGvfsProjectionChanges = 8062,
        JournalProcessingStatisticsForSchedulerTelemetry = 8063,

        IncrementalSchedulingPreciseChange = 8064,
        IncrementalSchedulingArtifactChangeSample = 8065,
        IncrementalSchedulingIdsMismatch = 8066,
        IncrementalSchedulingTokensMismatch = 8067,

        IncrementalSchedulingLoadState = 8068,
        IncrementalSchedulingReuseState = 8069,

        IncrementalSchedulingSaveState = 8070,

        IncrementalSchedulingProcessGraphChangeGraphId = 8071,
        IncrementalSchedulingProcessGraphChangeProducerChange = 8072,
        IncrementalSchedulingProcessGraphChangePathNoLongerSourceFile = 8073,
        IncrementalSchedulingPipDirtyAcrossGraphBecauseSourceIsDirty = 8074,
        IncrementalSchedulingPipDirtyAcrossGraphBecauseDependencyIsDirty = 8075,
        IncrementalSchedulingSourceFileOfOtherGraphIsDirtyDuringScan = 8076,
        IncrementalSchedulingPipOfOtherGraphIsDirtyDuringScan = 8077,
        IncrementalSchedulingPipDirtyDueToChangesInDynamicObservationAfterScan = 8078,
        IncrementalSchedulingPipsOfOtherPipGraphsGetDirtiedAfterScan = 8079,

        IncrementalSchedulingStateStatsAfterLoad = 8080,
        IncrementalSchedulingStateStatsAfterScan = 8081,
        IncrementalSchedulingStateStatsEnd = 8082,

        // Service pip scheduling
        ServicePipStarting = 12000,
        ServicePipShuttingDown = 12001,
        ServicePipTerminatedBeforeStartupWasSignaled = 12002,
        ServicePipFailed = 12003,
        ServicePipShuttingDownFailed = 12004,
        IpcClientForwardedMessage = 12005,
        IpcClientFailed = 12006,

        // BuildXL API server
        ApiServerForwarderIpcServerMessage = 12100,
        ApiServerInvalidOperation = 12101,
        ApiServerOperationReceived = 12102,
        ApiServerMaterializeFileSucceeded = 12103,
        ApiServerReportStatisticsExecuted = 12104,
        ApiServerGetSealedDirectoryContentExecuted = 12105,
        ErrorApiServerMaterializeFileFailed = 12106,
        ApiServerReceivedMessage = 12107,
        ApiServerReceivedWarningMessage = 12108,

        // Copy file cont'd.
        // Elsewhere = 12201,

        // Container related errors
        // Elsewhere  = 12202,
        // Elsewhere  = 12203,
        // Elsewhere  = 12204,
        VirtualizationFilterDetachError = 12205,
        PipInContainerStarted = 12206,
        PipInContainerStarting = 12207,

        PipCopyFileSourceFileDoesNotExist = 12201,
        AllowSameContentPolicyNotAvailableForStaticallyDeclaredOutputs = 12212,

        // Determinism probe to detect nondeterministic PIPs
        DeterminismProbeEncounteredNondeterministicOutput = 13000,
        DeterminismProbeEncounteredProcessThatCannotRunFromCache = 13001,
        DeterminismProbeEncounteredUnexpectedStrongFingerprintMismatch = 13002,
        DeterminismProbeEncounteredPipFailure = 13003,
        DeterminismProbeDetectedUnexpectedMismatch = 13004,
        DeterminismProbeEncounteredUncacheablePip = 13005,
        DeterminismProbeEncounteredOutputDirectoryDifferentFiles = 13006,
        DeterminismProbeEncounteredNondeterministicDirectoryOutput = 13007,

        MissingKeyWhenSavingFingerprintStore = 13300,
        FingerprintStoreSavingFailed = 13301,
        FingerprintStoreToCompareTrace = 13302,
        SuccessLoadFingerprintStoreToCompare = 13303,

        LowRamMemory = 14007,
        LowCommitMemory = 14014,
        HitLowMemorySmell = 14015,

        DirtyBuildExplicitlyRequestedModules = 14200,
        DirtyBuildProcessNotSkippedDueToMissingOutput = 14201,
        DirtyBuildProcessNotSkipped = 14202,
        DirtyBuildStats = 14203,
        MinimumWorkersNotSatisfied = 14204,
        BuildSetCalculatorStats = 14210,
        BuildSetCalculatorProcessStats = 14211,
        BuildSetCalculatorScheduleDependenciesUntilCleanAndMaterializedStats = 14212,

        FailedToDuplicateSchedulerFile = 14400,

        // Sandbox kernel extension connection manger errors
        KextFailedToInitializeConnectionManager = 14500,
        KextFailureNotificationReceived = 14501,

        FailedToLoadPipGraphFragment = 14502,
        PipCacheLookupStats = 14503,

        ProcessRetries = 14504,
        ProcessPattern = 14505,
        OperationTrackerAssert = 14506,

        ExcessivePipRetriesDueToLowMemory = 14507,
        TopPipsPerformanceInfo = 14508,

        CompositeSharedOpaqueContentDetermined = 14509,
        PipRetryDueToLowMemory = 14510,
        EmptyWorkingSet = 14511,
        ResumeProcess = 14512,

        // was DependencyViolationGenericWithRelatedPip_AsError = 25000,
        // was DependencyViolationGeneric_AsError = 25001,
        // was DependencyViolationDoubleWrite_AsError = 25002,
        // was DependencyViolationReadRace_AsError = 25003,
        // was DependencyViolationUndeclaredOrderedRead_AsError = 25004,
        // was DependencyViolationMissingSourceDependencyWithValueSuggestion_AsError = 25005,
        // was DependencyViolationMissingSourceDependency_AsError = 25006,
        // was DependencyViolationUndeclaredReadCycle_AsError = 25007,
        // was DependencyViolationUndeclaredOutput_AsError = 25008,
        // was DependencyViolationReadUndeclaredOutput_AsError = 25009,
    }
}
