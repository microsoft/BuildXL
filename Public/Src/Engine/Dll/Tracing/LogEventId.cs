// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
        ErrorRelatedLocation = 110,

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
        FailedToInitalizeFileAccessWhitelist = 2877,
        FailedToAcquireDirectoryDeletionLock = 2878,

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

        // Reserved  = 2890,
        // Reserved = 2891,

        WrittenBuildInvocationToUserFolder = 2892,
        FailedToWriteBuildInvocationToUserFolder = 2893,
        FailedToReadBuildInvocationToUserFolder = 2894,

        FailureLaunchingBuildExplorerFileNotFound = 2895,
        FailureLaunchingBuildExplorerException = 2896,

        InputTrackerDetectedMountChanged = 2987,

        // RESERVED TO [2800, 2899] (BuildXL.Engine.dll)

        // Distribution [7000, 7050]
        DistributionConnectedToWorker = 7000,
        DistributionWorkerChangedState = 7001,
        DistributionFailedToCallWorker = 7002,
        DistributionFailedToCallMaster = 7003,
        DistributionInactiveMaster = 7004,
        DistributionStatistics = 7005,
        DistributionExecutePipFailedNetworkFailure = 7006,
        DistributionWorkerExitFailure = 7007,
        DistributionSuccessfulRetryCallToWorker = 7008,
        DistributionSuccessfulRetryCallToMaster = 7009,
        DistributionWorkerAttachTooSlow = 7010,
        DistributionAttachReceived = 7011,
        DistributionExitReceived = 7012,
        DistributionTryMaterializeInputsFailedRetry = 7013,
        DistributionTryMaterializeInputsSuccessfulRetry = 7014,
        // Double defined in EventId.cs
        //DistributionWorkerForwardedError = 7015,
        DistributionWorkerForwardedWarning = 7016,

        DistributionWorkerExecutePipRequest = 7019,
        DistributionWorkerFinishedPipRequest = 7020,
        DistributionWorkerCouldNotLoadGraph = 7021,
        DistributionFailedToRetrieveValidationContentFromWorkerCache = 7022,
        DistributionFailedToRetrieveValidationContentFromWorkerCacheWithException = 7023,
        DistributionWorkerPipOutputContent = 7027,
        DistributionPipFailedOnWorker = 7028,
        GrpcTrace = 7029,
        DistributionFailedToStoreValidationContentToWorkerCacheWithException = 7030,
        DistributionDisableServiceProxyInactive = 7031,
        DistributionWaitingForMasterAttached = 7032,
        DistributionCallWorkerCodeException = 7033,
        DistributionCallMasterCodeException = 7034,
        //DistributionPipRemoteResultReceived = 7035,
        DistributionHostLog = 7036,
        DistributionMasterStatus = 7037,
        DistributionWorkerStatus = 7038,

        DistributionExecutePipFailedNetworkFailureWarning = 7039,
        // UNUSED 7040

        DistributionBondCall = 7041,
        DistributionDebugMessage = 7042,
        DistributionServiceInitializationError = 7043,

        // Scheduling
        ForceSkipDependenciesOrDistributedBuildOverrideIncrementalScheduling = 7051,
        ForceSkipDependenciesEnabled = 7052,

        // More graph caching
        EngineContextHeuristicOutcomeReuse = 7053,
        EngineContextHeuristicOutcomeSkip = 7054,
        GetPipGraphDescriptorFromCache = 7055,
        StorePipGraphCacheDescriptorToCache = 7056,
        MismatchPathInGraphInputDescriptor = 7057,
        MismatchEnvironmentInGraphInputDescriptor = 7058,
        FailedHashingGraphFileInput = 7059,
        FailedComputingFingerprintGraphDirectoryInput = 7060,
        MismatchMountInGraphInputDescriptor = 7061,

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
        EmitSpotlightIndexingWarning = 7115,
        FailedToAcquireDirectoryLock = 7116,
        UsingRedirectedUserProfile = 7117,
        FailedToRedirectUserProfile = 7118,
        ResourceBasedCancellationIsEnabledWithSharedOpaquesPresent = 7119,
        BusyOrUnavailableOutputDirectoriesException = 7120,
        GrpcSettings = 7121,

        FailedToGetJournalAccessor = 7122,

        StartInitializingVm = 7123,
        EndInitializingVm = 7124,
        InitializingVm = 7125

        // max 7200
    }
}
