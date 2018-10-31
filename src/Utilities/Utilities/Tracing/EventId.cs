// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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

        LogicRegisteringDuplicateElementName = 3,
        LogicRegisteringDuplicateType = 4,
        PipIpcFailed = 5,
        PipWriteFileFailed = 6,
        PipCopyFileFromUntrackableDir = 7,
        PipCopyFileFailed = 8,
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

        // BuildXL.Engine.dll FilterDetails = 25
        PipProcessFinishedDetourFailures = 26,
        StartupCurrentDirectory = 27,

        EngineDidNotFindDefaultQualifierInConfig = 28,
        AssignProcessToJobObjectFailed = 29,
        EngineDidNotFindASingleValueOfTypeModuleInConfig = 30,
        EngineDidNotFindRequestedQualifierInConfig = 31,
        PipProcessCommandLineTooLong = 32,
        ObjectCacheStats = 33,
        CacheBulkStatistics = 34,

        StartParseConfig = 35,
        EndParseConfig = 36,
        StartParseSpecs = 37,
        EndParseSpecs = 38,
        PipProcessInvalidWarningRegex = 39,
        BusyOrUnavailableOutputDirectories = 40,
        PipProcessChildrenSurvivedError = 41,
        PipProcessChildrenSurvivedKilled = 42,
        PipProcessMspdbsrv = 43,
        PipProcessMissingExpectedOutputOnCleanExit = 44,
        EngineFailedToInitializeOutputCache = 45,
        PipProcessOutputPreparationFailed = 46,
        CacheFingerprintHitSources = 47,
        CatastrophicFailureCausedByDiskSpaceExhaustion = 49,
        CacheClientStats = 50,
        CatastrophicFailureCausedByCorruptedCache = 51,
        ProcessingPipOutputFileFailed = 52,
        EvaluationStatus = 53,
        EvaluationContextUserSpecificationError = 54,
        EvaluationContextUserSpecificationWarning = 55,
        UserTransformerSpecificationError = 56,
        UserTransformerSpecificationWarning = 57,
        PipStatus = 58,
        CatastrophicFailure = 59,
        StatsBanner = 60,
        GCStats = 61,
        ObjectPoolStats = 62,
        InterningStats = 63,
        PipProcessError = 64,
        PipProcessWarning = 65,
        PipProcessOutput = 66,

        // Contains input assertion for the pip
        PipInputAssertion = 67,

        MissingRegisterMethodFromType = 68,
        ErrorLoadingLogicAssembly = 69,
        ErrorLoadingRegistrationAttributes = 70,
        ErrorGettingRegistrationMethod = 71,
        ErrorInvokingRegistrationMethod = 72,
        FrontEndStatsBanner = 73,
        PipProcessResponseFileCreationFailed = 74,
        PipTableStats = 75,
        PipWriterStats = 76,
        UserTransformerTryResolveTransformerType = 77,
        UserTransformerTryResolveValueType = 78,
        UserTransformerTryResolveEnumerationType = 79,
        UserTransformerTryResolveTemplateType = 80,
        UserTransformerTryResolveNotFound = 81,
        UserTransformerTryResolveInvalidType = 82,
        MappedRoot = 83,
        PipTableDeserializationContext = 84,
        RetryStartPipDueToErrorPartialCopyDuringDetours = 85,

        PipProcessStandardInputException = 86,

        StartEngineRun = 87,
        EndEngineRun = 88,
        PipProcessInvalidErrorRegex = 89,
        EnvErrorEvaluationOverflow = 90,
        EnvEvaluationOverflowFull = 91,
        TransformerLogicUnexpectedException = 92,
        EnvAmbiguousReferenceDetected = 93,
        EnvironmentValueForTempDisallowed = 94,
        CannotHonorLowPriority = 95,
        ConfigErrorDuplicateModuleIdentifier = 96,
        ConfigErrorNonExistingDependencyError = 97,
        ConfigSingletonSpecifiedMultipleTimes = 98,
        [SuppressMessage("Microsoft.Naming", "CA1700:IdentifiersShouldBeSpelledCorrectly")]
        LogicRegisteringValueTypeWithReservedMember = 99,

        // XML Parser
        // Reserved 100..199 See XMlLogger
        XmlParseErrorRelatedLocation = 110,
        XmlParseErrorUnexpectedNodeExpectedTextNode = 117,
        XmlParseNotificationUserDefinedError = 132,
        XmlParseNotificationUserDefinedWarning = 133,
        XmlParseNotificationUserDefinedMessage = 134,
        ParseSpecFilesStatus = 138,
        XmlParseErrorInnerTemplateMustBeTransformerTemplate = 139,

        // Free Slots = 141,
        XmlParseErrorInvalidImportPath = 145,
        EvaluatorIncompatibleType = 150,
        EvaluatorEncounteredNamespaceExpectedValue = 151,
        EvaluatorCyclicReference = 153,
        EvaluationNullReferenceException = 157,
        EvaluatorNonAbsoluteInterpolationCannotContainAbsolutePath = 162,
        EvaluatorInvalidDottedIdentifierUnexpectedDot = 165,
        EvaluatorInvalidDottedIdentifierUnexpectedCharacter = 166,
        EvaluatorUnresolvedValueInNestedEnvironment = 167,
        EvaluationNotALeaf = 168,
        EvaluatorAbsolutePathInterpolationCannotContainAbsolutePathInSuffix = 171,
        EvaluatorInterpolationPartUnexpectedType = 172,
        EvaluatorCouldNotFindTemplate = 186,
        EvaluatorTemplateOfWrongType = 187,
        EvaluatorExpressionCharacterMustBeEscaped = 193,

        // Scheduler, Fingerprinting and pip caching
        CacheDescriptorHitForContentFingerprint = 200,
        CacheDescriptorMissForContentFingerprint = 201,
        ContentMissAfterContentFingerprintCacheDescriptorHit = 202,
        CopyingAllPipOutputsFromCache = 203,
        PipOutputDeployedFromCache = 204,
        PipOutputsDifferentForDifferentCacheLevels = 205,

        // Pip validation
        // Outputs
        InvalidOutputSinceOutputIsSource = 206,
        InvalidOutputSincePreviousVersionUsedAsInput = 207,
        InvalidOutputSinceOutputHasUnexpectedlyHighWriteCount = 208,
        InvalidOutputSinceRewritingOldVersion = 209,
        InvalidOutputSinceRewrittenOutputMismatchedWithInput = 210,
        InvalidOutputDueToSimpleDoubleWrite = 211,
        InvalidOutputDueToMultipleConflictingRewriteCounts = 212,

        // Inputs
        InvalidInputSincePathIsWrittenAndThusNotSource = 213,
        BusyOrUnavailableOutputDirectoriesRetry = 214,
        InvalidInputSinceInputIsRewritten = 215,
        InvalidInputDueToMultipleConflictingRewriteCounts = 216,

        XmlParseErrorTemplateNameNotAllowedOnInnerTemplate = 217,

        // Pips
        InvalidProcessPipDueToNoOutputArtifacts = 218,
        InvalidProcessPipDueToExplicitArtifactsInOpaqueDirectory = 219,
        InvalidCopyFilePipDueToSameSourceAndDestinationPath = 220,
        InvalidWriteFilePipSinceOutputIsRewritten = 221,

        // Input / output hashing
        IgnoringUntrackedSourceFileNotUnderMount = 222,
        PipOutputProduced = 223,
        HashedSourceFile = 224,

        // Schedule failure
        TerminatingDueToPipFailure = 225,
        IgnoringPipSinceScheduleIsTerminating = 226,
        PipsSucceededStats = 227,
        PipsFailedStats = 228,
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
        UpdatingCacheWithReplacementDescriptor = 240,

        DirectoryMembershipAssertionMissAfterContentFingerprintCacheDescriptorHit = 241,

        PipOutputUpToDate = 242,
        OutputFileStats = 243,

        DirectorySealed = 244,
        InputAssertionMissAfterContentFingerprintCacheDescriptorHit = 245,

        // More pip dependency errors
        InvalidOutputSinceDirectoryHasBeenSealed = 246,
        InvalidSealDirectoryContentSinceNotUnderRoot = 247,
        InvalidOutputSinceFileHasBeenPartiallySealed = 248,
        InvalidSharedOpaqueDirectoryDueToOverlap = 14401,
        ScheduleFailAddPipInvalidComposedSealDirectoryNotUnderRoot = 14402,
        ScheduleFailAddPipInvalidComposedSealDirectoryIsNotSharedOpaque = 14403,

        PipStaticFingerprint = 14410,

        // Reserved for XML
        // Template resolution
        // Reserved for Xml Parsing
        InnerTemplateOfSameTypeNotAllowed = 250,
        DuplicateInnerTemplateDefined = 251,
        UserTransformerResolveTemplateTypeDoesNotAgree = 252,

        // Pip performance.
        ProcessStart = 253,
        ProcessEnd = 254,
        CopyFileStart = 255,
        CopyFileEnd = 256,
        WriteFileStart = 257,
        WriteFileEnd = 258,

        DirectoryPartiallySealed = 259,
        // was DisallowedFileAccessInSealedDirectoryError = 260,

        FailedToHashInputFileDueToFailedExistenceCheck = 261,
        FailedToHashInputFileBecauseTheFileIsDirectory = 262,

        UnableToCreateExecutionLogFile = 263,

        PipProcessDisallowedFileAccessWhitelistedCacheable = 264,
        IgnoringUntrackedSourceFileUnderMountWithHashingDisabled = 265,
        InputAssertionTypeMismatchAfterContentFingerprintCacheDescriptorHit = 266,
        ProcessDescendantOfUncacheable = 267,
        ProcessNotStoredToCacheDueToFileMonitoringViolations = 268,
        PipProcessDisallowedFileAccessWhitelistedNonCacheable = 269,
        FileAccessWhitelistCouldNotCreateIdentifier = 270,
        WarningStats = 271,
        PipWarningsFromCache = 272,
        FileAccessWhitelistFailedToParsePath = 274,
        StartWaitingForScheduleExportCompletion = 275,
        EndtWaitingForScheduleExportCompletion = 276,
        DisallowedFileAccessInSealedDirectory = 277,
        WhitelistFileAccess = 278,

        FileAccessManifestSummary = 279,

        // Scheduler continued
        StartSchedulingPipsWithFilter = 280,
        EndSchedulingPipsWithFilter = 281,
        StartFilterApplyTraversal = 282,
        EndFilterApplyTraversal = 283,

        InvalidSealDirectorySourceNotUnderMount = 284,
        InvalidSealDirectorySourceNotUnderReadableMount = 285,

        ProcessNotStoredToCachedDueToItsInherentUncacheability = 286,
        FileAccessWhitelistEntryHasInvalidRegex = 287,
        PipSemaphoreQueued = 288,
        PipSemaphoreDequeued = 289,
        ProcessesSemaphoreQueuedStats = 290,
        ScheduleDirectorySourceSealedAllDirectories = 291,
        ScheduleDirectorySourceSealedTopDirectoryOnly = 292,
        ScheduleArtificialCacheMiss = 293,
        CopyingPipInputToLocalStorage = 294,
        NoPipsMatchedFilter = 295,
        InvalidPipDueToInvalidServicePipDependency = 296,
        FileAccessCheckProbeFailed = 297,
        PipQueueConcurrency = 298,
        InvalidInputSinceSourceFileCannotBeInsideOutputDirectory = 299,

        // XML Evaluator (continued)
        EvaluatorFunctionCallWarning = 306,
        EvaluatorFunctionCallError = 307,
        PipFailSymlinkCreation = 308,
        EvaluatorUnexpectedCharacterAtEndOfExpressionMissingEnd = 309,

        // Pip validation Inputs (continued)
        InvalidInputSinceCorrespondingOutputIsTemporary = 310,
        PipProcessMessageParsingError = 311,

        CacheMissAnalysis = 312,
        CacheMissAnalysisTelemetry = 313,

        PipExitedUncleanly = 314,
        // Free slot 315,
        PipStandardIOFailed = 316,

        // Free slot 317,
        EvaluatorAliasDeclarationMustContainExpression = 318,

        // Free slot 319,
        EvaluatorIdentifierLookupSuggestionCaseMismatch = 320,
        EvaluatorIdentifierLookupSuggestionMissingModuleDependency = 321,
        EvaluatorIdentifierLookupSuggestionMissingNamespacePrefix = 322,
        EvaluatorIdentifierLookupSuggestionInternalModuleDependency = 323,
        EvaluatorIdentifierLookupSuggestionPrivateModuleDeclaration = 324,

        // Free slot 325,
        EvaluationCouldNotFindQualifier = 326,

        // EvaluationErrorQualifierIsNotAnIQualifier = 327,
        EvaluatorInvalidDottedIdentifierCannotBeEmpty = 328,
        EvaluatorUnexpectedCharacterAtStartOfIdentifier = 329,
        EvaluatorUnexpectedEmptyIdentifier = 330,

        // Free slot 331,

        // Free slot 333,
        EvaluatorIdentifierLookupSuggestionNonpublicModuleValueForNamespace = 334,
        EvaluatorIdentifierLookupSuggestionMissingModuleDependencyForNamespace = 335,
        DuplicateWindowsEnvironmentVariableEncountered = 336,

        // Free slot 337,
        // Free slot 338,
        // Free slot 339,
        // Free slot 340,
        // Free slot 341,
        // Free slot 342,
        StartParseModules = 343,
        EndParseModules = 344,
        XmlParseErrorInvalidExplicitVisibleToAttributeValue = 345,

        // Free slot 346,
        ConfigErrorModuleDependencyNotVisible = 347,
        ConfigErrorModuleDependencyNotVisibleResolution = 348,
        ConfigErrorInconsistentPartialModuleVisibility = 349,
        ConfigErrorInconsistentPartialModuleParent = 350,
        EvaluatorTemplateBlockedByCondition = 351,
        EvaluatorQualifierDefinitionBlockedByCondition = 352,
        EvaluatorConditionBlockedByCondition = 353,
        ConfigErrorNonexistentParentModule = 354,
        ConfigErrorParentModuleNotPartial = 355,
        ConfigErrorNestedModuleCycle = 356,
        ConfigErrorInlineNestedModuleDeclaresParent = 357,
        EvaluatorIdentifierBlockedByCondition = 358,

        // Free slot 359,

        // Scheduler: Directory fingerprinting
        PipDirectoryMembershipAssertion = 360,
        DirectoryFingerprintingFilesystemEnumerationFailed = 361,
        PipDirectoryMembershipFingerprintingError = 363,
        DirectoryFingerprintComputedFromFilesystem = 364,
        DirectoryFingerprintComputedFromGraph = 365,
        DirectoryFingerprintExercisedRule = 366,
        PathSetValidationTargetFailedAccessCheck = 367,
        // was DirectoryFingerprintUsedSearchPathEnumeration = 368,

        // Dynamic Module Activity
        // DEPRECATED 370,
        // DEPRECATED 371,
        // DEPRECATED 372,
        // DEPRECATED 373,
        // DEPRECATED 374,
        // DEPRECATED 375,
        // DEPRECATED 376,
        // was DisallowedFileAccessInTopOnlySourceSealedDirectoryError = 377,
        DisallowedFileAccessInTopOnlySourceSealedDirectory = 378,
        ProcessingPipOutputDirectoryFailed = 379,

        // Environment
        EnvUnresolvableIdentifier = 400,
        EnvRequestedIdentifierIsANamespace = 401,
        EnvFreezing = 402,
        StartupTimestamp = 403,
        EnvAmbiguousReferenceDeclaration = 404,
        BuildXLInvocation = 405,
        BuildXLCompletion = 406,
        BuildXLCatastrophicFailure = 407,
        BuildXLPerformanceSummary = 408,
        BuildXLInvocationForLocalLog = 409,
        ViewerUsage = 410,
        ViewerWasAccessed = 411,

        // Tracing
        TextLogEtwOnly = 450,
        CacheFileLog = 451,

        EventWriteFailuresOccurred = 452,
        FailedToFetchPerformanceCounter = 453,
        FailedToEnumerateLogDirsForCleanup = 454,
        FailedToCleanupLogDir = 455,
        WaitingCleanupLogDir = 456,
        WaitingClientDebugger = 457,
        DisplayHelpLink = 458,
        StatsPerformanceLog = 459,

        // Cancellation
        CancellationRequested = 470,

        TelemetryShutDown = 471,
        UnexpectedCondition = 472,
        // was TelemetryRecoverableException = 473,
        TelemetryShutDownException = 474,
        ServerDeploymentDirectoryHashMismatch = 475,
        TelemetryShutdownTimeout = 476,

        PipProcessDisallowedNtCreateFileAccessWarning = 480,

        // was PipProcessChildrenSurvivedWarning = 499,
        FileMonitoringError = 500,
        FileMonitoringWarning = 501,

        Channel = 502,
        StorageCacheContentHitSources = 503,
        PipProcessExpectedMissingOutputs = 504,
        // was PipProcessAllowedMissingOutputs = 505,

        // USN/Change Journal usage (FileChangeTracker)
        StartLoadingChangeTracker = 680,
        StartScanningJournal = 681,
        ScanningJournal = 682, // was EndSavingChangeTracker = 682,
        // was ChangeTrackerNotLoaded = 683,
        // was ScanningJournalError = 684,
        EndLoadingChangeTracker = 685,
        EndScanningJournal = 686,
        LoadingChangeTracker = 687,
        DisableChangeTracker = 688,

        // Storage
        FileUtilitiesDiagnostic = 699,
        StorageFileContentTableIgnoringFileSinceUsnJournalDisabled = 700,
        StorageLoadFileContentTable = 701,
        StorageHashedSourceFile = 702,
        StorageUsingKnownHashForSourceFile = 703,
        SettingOwnershipAndAcl = 704,
        SettingOwnershipAndAclFailed = 705,
        StorageCacheCopyLocalError = 706,
        StorageCacheGetContentBagError = 707,
        StorageCacheGetContentError = 708,
        StorageCacheAddContentBagError = 709,
        StorageCacheReplaceContentBagError = 710,
        StorageCachePutContentFailed = 711,
        StorageCacheStartupError = 712,

        // USNs
        StorageReadUsn = 713,
        StorageKnownUsnHit = 714,
        StorageUnknownUsnMiss = 715,
        StorageCheckpointUsn = 716,
        StorageRecordNewKnownUsn = 717,
        StorageUnknownFileMiss = 718,
        StorageJournalDisabledMiss = 719,

        StorageTryOpenDirectoryFailure = 720,
        StorageFoundVolume = 721,
        StorageTryOpenFileByIdFailure = 722,
        StorageVolumeCollision = 723,
        StorageTryOpenOrCreateFileFailure = 724,

        StorageCacheContentPinned = 725,
        StorageCacheIngressFallbackContentToMakePrivateError = 726,
        StorageCacheGetContentUsingFallback = 727,

        StorageBringProcessContentLocalWarning = 728,

        StorageFailureToOpenFileForFlushOnIngress = 729,

        StorageCatastrophicFailureDriveError = 730,
        CatastrophicFailureMissingRuntimeDependency = 731,
        SharingViolationHandleResult = 732,
        HashedSymlinkAsTargetPath = 733,
        StorageFailureToFlushFileOnIngress = 734,
        ClosingFileStreamAfterHashingFailed = 735,
        StorageFailureToFlushFileOnDisk = 736,
        StorageCacheGetContentWarning = 737,
        FailedToMaterializeFileWarning = 738,
        MaterializeFilePipProducerNotFound = 739,

        StoreSymlinkWarning = 740,
        // was StoreWarnDueToUnableToDetermineReparsePoint = 741,

        SerializingToPipFingerprintEntryResultInCorruptedData = 742,
        DeserializingCorruptedPipFingerprintEntry = 743,

        RetryOnFailureException = 744,

        StorageTrackOutputFailed = 745,

        RetryOnLoadingAndDeserializingMetadata = 746,
        // was IncorrectExistsAsFileThroughPathMappings = 747,
        TimeoutOpeningFileForHashing = 748,

        // Additional Process Isolation
        PipProcessIgnoringPathWithWildcardsFileAccess = 800,
        PipProcessIgnoringPathOfSpecialDeviceFileAccess = 801,
        PipProcessFailedToParsePathOfFileAccess = 802,
        Process = 803,

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

        // Config
        ConfigUnsafeDisabledFileAccessMonitoring = 900,
        ConfigUnsafeIgnoringChangeJournal = 901,
        ConfigUnsafeUnexpectedFileAccessesAsWarnings = 902,
        ConfigUnsafeMonitorNtCreateFileOff = 903,
        ConfigErrorMissingAliasDefinition = 904,
        ConfigIgnoreReparsePoints = 905,
        JournalRequiredOnVolumeError = 906,
        ConfigFailedParsingCommandLinePipFilter = 907,
        ConfigFailedParsingDefaultPipFilter = 908,
        ConfigUsingExperimentalOptions = 909,
        ConfigIgnoreZwRenameFileInformation = 910,
        ConfigArtificialCacheMissOptions = 911,
        ConfigExportGraphRequiresScheduling = 912,
        ConfigUsingPipFilter = 913,
        ConfigFilterAndPathImplicitNotSupported = 914,
        ConfigExportFingerprintsRequiresScheduling = 915,
        ConfigIgnoreDynamicWritesOnAbsentProbes = 916,
        ConfigIgnoreSetFileInformationByHandle = 917,
        ConfigPreserveOutputs = 918,
        ConfigUnsafeLazySymlinkCreation = 919,
        ConfigDisableDetours = 920,
        ConfigDebuggingAndProfilingCannotBeSpecifiedSimultaneously = 921,
        ConfigIgnoreGetFinalPathNameByHandle = 922,
        ConfigIgnoreZwOtherFileInformation = 923,
        ConfigUnsafeMonitorZwCreateOpenQueryFileOff = 924,
        ConfigIgnoreNonCreateFileReparsePoints = 925,
        ConfigUnsafeDisableCycleDetection = 926,
        ConfigUnsafeExistingDirectoryProbesAsEnumerations = 927,
        ConfigUnsafeAllowMissingOutput = 928,
        ConfigIgnoreValidateExistingFileAccessesForOutputs = 929,

        StorageUsnMismatchButContentMatch = 932,

        ConfigIncompatibleIncrementalSchedulingDisabled = 933,
        ConfigIncompatibleOptionWithDistributedBuildError = 934,
        ConfigIgnorePreloadedDlls = 935,
        ConfigIncompatibleOptionWithDistributedBuildWarn = 936,

        WarnToNotUsePacakgesButModules = 937,
        WarnToNotUseProjectsField = 938,

        // Schedule observer events
        // FREE SLOT
        // FREE SLOT
        // FREE SLOT
        // reserved 1003, 1004 for xml
        UserScheduleObserverWarning = 1005,
        UserScheduleObserverError = 1006,

        // Perf instrumentation
        StartLoadingTransformers = 1500,
        EndLoadingTransformers = 1501,
        StartInitializingCache = 1502,
        EndInitializingCache = 1503,
        SynchronouslyWaitedForCache = 1504,

        // FREE SLOT = 1505,
        StartViewer = 1506,
        UnableToStartViewer = 1507,
        Memory = 1508,
        ParsingStats = 1509,
        PipDetailedStats = 1510,
        UnableToLaunchViewer = 1511,
        IncrementalBuildSavingsSummary = 1512,
        IncrementalBuildSharedCacheSavingsSummary = 1513,
        RemoteCacheHitsGreaterThanTotalCacheHits = 1514,
        SchedulerDidNotConverge = 1515,

        // Scheduler Pip Validation
        InvalidOutputUnderNonWritableRoot = 2000,
        InvalidInputUnderNonReadableRoot = 2001,
        CannotAddCreatePipsDuringConfigOrModuleEvaluation = 2002,
        InvalidTempDirectoryUnderNonWritableRoot = 2003,
        InvalidTempDirectoryInvalidPath = 2004,
        RewritingPreservedOutput = 2005,

        // FREE SLOT = 2005,

        // XML Parser continue
        // Free slot 2100,
        SpecifiedTypeAttributeHasNoCorrespondingRegisteredType = 2101,

        // Free slot 2102,
        FileCombinerVersionIncremented = 2103,
        FileCombinerFailedToInitialize = 2104,
        SpecCacheDisabledForNoSeekPenalty = 2105,
        FileCombinerFailedToCreate = 2106,
        SpecCache = 2107,
        IncrementalFrontendCache = 2108,

        // Temp files/directory cleanup
        PipTempDirectoryCleanupWarning = 2200,
        PipTempDirectoryCleanupError = 2201,
        PipTempCleanerThreadSummary = 2202,
        PipTempDirectorySetupError = 2203,
        PipTempFileCleanupWarning = 2204,

        PipFailedToCreateDumpFile = 2210,

        // Engine Errors
        EngineRunErrorDuplicateMountPaths = 2500,
        ErrorSavingSnapshot = 2501,
        ErrorSavingValues = 2502,
        EngineErrorSavingFileContentTable = 2503,
        EngineFailedToConnectToChangeJournalService = 2504,
        EngineRunErrorDuplicateMountNames = 2506,
        GenericSnapshotError = 2507,
        ErrorCaseSensitiveFileSystemDetected = 2508,

        // Dealing with MAX_PATH issues
        PathHashed = 2600,
        PipProcessTempDirectoryTooLong = 2601,
        FailPipOutputWithNoAccessed = 2602,
        PipOutputNotAccessed = 2603,
        PipWillBeRetriedDueToExitCode = 2604,

        // MLAM
        FileArtifactContentMismatch = 2700,
        PipOutputNotMaterialized = 2701,
        PipFailedDueToDependenciesCannotBeMaterialized = 2702,
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

        // Two-phase fingerprinting
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
        TemporalCacheEntryTrace = 2733,

        // RESERVED TO [2800, 2899] (BuildXL.Engine.dll)

        // RESERVED TO [2900, 2999] (BuildXL.Engine.dll)

        // Engine phase markers for journal service init
        StartConnectToJournalService = 2900,
        EndConnectToJournalService = 2901,
        JournalServiceNotInstalled = 2902,
        FailedToInstallJournalService = 2903,
        JournalServiceNotRunning = 2904,
        FailedToRunJournalService = 2905,
        IncompatibleJournalService = 2906,
        FailedToUpgradeJournalService = 2907,
        UserRefusedElevation = 2908,
        FailedCheckingDirectJournalAccess = 2909,
        RunBuildXLWithSetupJournal = 2910,
        EndInstallJournalService = 2911,
        EndRunJournalService = 2912,
        EndUpgradeJournalService = 2913,
        ChangeJournalServiceReady = 2914,
        MaterializingProfilerReport = 2915,
        ErrorMaterializingProfilerReport = 2916,

        MaterializingFileToFileDepdencyMap = 2917,
        ErrorMaterializingFileToFileDepdencyMap = 2918,

        LogInternalDetoursErrorFileNotEmpty = 2919,
        LogGettingInternalDetoursErrorFile = 2920,

        LogMismatchedDetoursErrorCount = 2922,
        LogMessageCountSemaphoreExists = 2923,
        // RESERVED 2924
        LogFailedToCreateDirectoryForInternalDetoursFailureFile = 2925,
        // RESERVED 2926
        LogMismatchedDetoursVerboseCount = 2927,
        LogDetoursMaxHeapSize = 2928,
        OutputFileHashingStats = 2929,

        FailedToResolveHistoricMetadataCacheFileName = 2940,
        LoadingHistoricMetadataCacheFailed = 2941,
        SavingHistoricMetadataCacheFailed = 2942,
        HistoricMetadataCacheLoaded = 2943,
        HistoricMetadataCacheSaved = 2944,
        HistoricMetadataCacheStats = 2945,
        HistoricMetadataCacheAdded = 2946,
        HistoricMetadataCacheUpdated = 2947,

        // Free: 3000..3053

        // Critical Path Suggestions
        StartLoadingRunningTimes = 3100,
        EndLoadingRunningTimes = 3101,
        StartSavingRunningTimes = 3102,
        EndSavingRunningTimes = 3103,
        FailedToResolveHistoricDataFileName = 3104,

        // FREE 3105
        LoadingRunningTimesFailed = 3106,
        SavingRunningTimesFailed = 3107,
        RunningTimesLoaded = 3108,
        RunningTimesSaved = 3109,
        RunningTimeStats = 3110,
        RunningTimeAdded = 3111,
        RunningTimeUpdated = 3112,
        StartAssigningPriorities = 3113,
        EndAssigningPriorities = 3114,

        // Pip state initialization
        StartSettingPipStates = 3115,
        EndSettingPipStates = 3116,
        StartRehydratingConfigurationWithNewPathTable = 3117,
        EndRehydratingConfigurationWithNewPathTable = 3118,

        #region ASSEMBLY RESERVED (3200-3599): BuildXL.Engine.dll

        BuildXLEngineStart = 3200,

        // TODO: Move to BuildXL.Engine
        // Distributed Build
        ErrorUnableToCacheGraphDistributedBuild = BuildXLEngineStart, // 3200
        ErrorCacheDisabledDistributedBuild = 3201,
        // Was ErrorUsingCacheServiceDistributedBuild = 3202,
        ErrorWorkerForwardedError = 3203,
        NonDeterministicPipOutput = 3204,
        NonDeterministicPipResult = 3205,
        ErrorVerifyDeterminismWorkerAttachTimeOut = 3206,
        EnvironmentVariablesImpactingBuild = 3207,
        StartWaitingForFingerprintExportCompletion = 3208,
        EndWaitingForFingerprintExportCompletion = 3209,
        FingerprintExportProgress = 3210,
        // Was ErrorUsingNewCacheWithDistributedBuild = 3211,
        SchedulerExportFailedSchedulerNotInitialized = 3212,
        // Was ErrorUsingTwoPhaseFingerprintingWithDistributedBuild = 3213,
        MountsImpactingBuild = 3214,

        BuildXLEngineEnd = 3599,

        #endregion ASSEMBLY RESERVED (3200-3599): BuildXL.Engine.dll

        #region ASSEMBLY RESERVED (3600-3999): BuildXL.Scheduler.dll

        BuildXLSchedulerStart = 3600,
        BuildXLSchedulerEnd = 3999,

        #endregion ASSEMBLY RESERVED (3600-3999): BuildXL.Scheduler.dll

        // Change journal service
        ChangeJournalServiceRequestStart = 4000,
        ChangeJournalServiceRequestStop = 4001,
        ChangeJournalPipeServerInstanceThreadCrash = 4002,
        ChangeJournalServiceRequestIOError = 4003,
        ChangeJournalServiceProtocolError = 4004,
        ChangeJournalServiceReadJournalRequest = 4005,
        ChangeJournalServiceQueryJournalRequest = 4006,
        ChangeJournalServiceQueryServiceVersionRequest = 4007,
        ChangeJournalServiceUnsupportedProtocolVersion = 4008,

        #region Assembly-level reserved ranges (4100-5000)

        #region ASSEMBLY RESERVED (4100-4199): BuildXL.Utilities.dll

        BuildXLUtilitiesStart = 4100,
        BuildXLUtilitiesEnd = 4199,

        #endregion ASSEMBLY RESERVED (4100-4199): BuildXL.Utilities.dll

        #region ASSEMBLY RESERVED (4200-4299): BuildXL.Storage.dll

        BuildXLStorageStart = 4200,
        // was StorageFileContentTableIncorrectPathMapping = 4201,
        ValidateJunctionRoot = 4202,
        // was IncorrectExistenceCheckThroughJournal = 4203,
        // was StorageFileContentTableIncorrectPathMappingContentMismatch = 4204,
        ConflictDirectoryMembershipFingerprint = 4205,
        BuildXLStorageEnd = 4299,

        #endregion ASSEMBLY RESERVED (4200-4299): BuildXL.Storage.dll

        #region ASSEMBLY RESERVED (4300-4399): BuildXL.exe

        BuildXLApplicationStart = 4300,
        TelemetryEnabledNotifyUser = 4301,
        TelemetryEnabledHideNotification = 4302,
        MemoryLoggingEnabled = 4303,

        BuildXLApplicationEnd = 4399,

        #endregion ASSEMBLY RESERVED (4300-4399): BuildXL.exe

        #region ASSEMBLY RESERVED (4400-4499): BuildXL.Processes.dll

        BuildXLProcessesStart = 4400,
        PipProcessUncacheableWhitelistNotAllowedInDistributedBuilds = 4401,
        BuildXLProcessesEnd = 4499,

        #endregion ASSEMBLY RESERVED (4400-4499): BuildXL.Processes.dll

        #region ASSEMBLY RESERVED (4500-4499): BuildXL.Pips.dll

        BuildXLPipsStart = 4500,
        BuildXLPipsEnd = 4599,

        #endregion Assembly-level reserved ranges (4100-5000)

        #endregion Assembly-level reserved ranges (4100-5000)

        #region ASSEMBLY RESERVED (5000-5050): BuildXL.Scheduler.dll

        // RESERVED TO [5000, 5050] (BuildXL.Scheduler.dll)
        #endregion ASSEMBLY RESERVED (5000-5050): BuildXL.Scheduler.dll

        // was SchedulerAskedToWaitForUnknownValue = 6213,
        // was SchedulerAskedToWaitForUnscheduledValue = 6215,
        Statistic = 6300,
        // was PerformanceSnapshot = 6301,
        EventCount = 6302,
        PerformanceSample = 6303,
        StatisticWithoutTelemetry = 6304,
        BulkStatistic = 6305,
        FinalStatistics = 6306,
        LoggerStatistics = 6307,

        // DEPRECATED SlowestElementsStatistic = 6307,
        #region ASSEMBLY RESERVED (7000 - 7050): BuildXL.Engine.dll (distribution)

        // Moved to BuildXL.Engine
        // NOTE: Do not add more events here. Events should be added to BuildXL.Engine.Tracing.LogEventId

        // This event is needed by TrackingEventListener. It is defined here instead of in BuildXL.Engine.Tracing.LogEventId
        // in order to avoid taking a dependency we don't want
        DistributionWorkerForwardedError = 7015,
        #endregion


        // Change detection (FileChangeTrackingSet)
        ChangeDetectionGeneralVerbose = 8001,
        ChangeDetectionProbeSnapshotInconsistent = 8002,
        ChangeDetectionComputedDirectoryMembershipTrackingFingerprint = 8003,
        ChangeDetectionDueToPerpetualDirtyNode = 8004,
        // was ChangeDetectionSaveTrackingSet = 8005,

        ChangeDetectionFailCreateTrackingSetDueToJournalQueryError = 8006,
        ChangeDetectionCreateResult = 8007,

        ChangeDetectionSingleVolumeScanJournalResult = 8008,
        ChangeDetectionScanJournalFailedSinceJournalGotOverwritten = 8009,
        ChangeDetectionScanJournalFailedSinceTimeout = 8010,

        AntiDependencyValidationPotentiallyAddedButVerifiedAbsent = 8011,
        AntiDependencyValidationFailedRetrackPathAsNonExistent = 8012,
        AntiDependencyValidationFailedProbePathToVerifyNonExistent = 8013,
        AntiDependencyValidationStats = 8014,

        EnumerationDependencyValidationPotentiallyAddedOrRemovedDirectChildrenButVerifiedUnchanged = 8015,
        EnumerationDependencyValidationFailedRetrackUnchangedDirectoryForMembershipChanges = 8016,
        EnumerationDependencyValidationFailedToOpenOrEnumerateDirectoryForMembershipChanges = 8017,
        EnumerationDependencyValidationStats = 8018,

        HardLinkValidationPotentiallyChangedButVerifiedUnchanged = 8019,
        HardLinkValidationHardLinkChangedBecauseFileIdChanged = 8020,
        HardLinkValidationFailedRetrackUnchangedHardLink = 8021,
        HardLinkValidationFailedToOpenHardLinkDueToNonExistent = 8022,
        HardLinkValidationFailedToOpenOrTrackHardLink = 8023,
        HardLinkValidationStats = 8024,

        ChangeDetectionSingleVolumeScanJournalResultTelemetry = 8025,
        ChangedPathsDetectedByJournalScanning = 8026,
        ChangeDetectionParentPathIsUntrackedOnTrackingAbsentRelativePath = 8027,
        IgnoredRecordsDueToUnchangedJunctionRootCount = 8028,
        TrackChangesToFileDiagnostic = 8029,

        StartSavingChangeTracker = 8030,
        EndSavingChangeTracker = 8031,
        SavingChangeTracker = 8032,

        // Incremental scheduling
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

        // FREE SLOT 8062
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

        // Server mode
        UsingExistingServer = 8100,
        AppServerBuildStart = 8101,
        AppServerBuildFinish = 8102,
        StartingNewServer = 8103,
        CannotStartServer = 8104,
        DeploymentUpToDateCheckPerformed = 8105,
        DeploymentCacheCreated = 8106,

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

        // Detours
        BrokeredDetoursInjectionFailed = 10100,
        LogDetoursDebugMessage = 10101,
        LogAppleSandboxPolicyGenerated = 10102,
        LogMacKextFailure = 10103,

        // reserved 11200 .. 11300 for the FrontEndHost
        // reserved 11300 .. 11400 for the Nuget FrontEnd
        // reserved 11400 .. 11500 for the VPack FrontEnd

        // CloudBuild events
        BuildXLCompletedEvent = 11500,
        TargetAddedEvent = 11501,
        TargetRunningEvent = 11502,
        TargetFailedEvent = 11503,
        TargetFinishedEvent = 11504,
        BuildXLInvocationEvent = 11505,
        DropCreationEvent = 11506,
        DropFinalizationEvent = 11507,
        BuildXLContinuousStatisticsEvent = 11508,

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
        ApiServerMaterializeFileExecuted = 12103,
        ApiServerReportStatisticsExecuted = 12104,
        ApiServerGetSealedDirectoryContentExecuted = 12105,

        // Copy file cont'd.
        PipCopyFileSourceFileDoesNotExist = 12201,

        // Container related errors
        FailedToMoveOutputsToOriginalLocation = 12202,
        FailedToCleanUpContainer = 12203,
        WarningSettingUpContainer = 12204,
        VirtualizationFilterDetachError = 12205,
        PipInContainerStarted = 12206,
        PipInContainerStarting = 12207,
        PipSpecifiedToRunInContainerButIsolationIsNotSupported = 12208,

        // Status logging
        Status = 12400,
        StatusSnapshot = 12401,
        StatusHeader = 12402,
        StatusCallbacksDelayed = 12403,

        // Determinism probe to detect nondeterministic PIPs
        DeterminismProbeEncounteredNondeterministicOutput = 13000,
        DeterminismProbeEncounteredProcessThatCannotRunFromCache = 13001,
        DeterminismProbeEncounteredUnexpectedStrongFingerprintMismatch = 13002,
        DeterminismProbeEncounteredPipFailure = 13003,
        DeterminismProbeDetectedUnexpectedMismatch = 13004,
        DeterminismProbeEncounteredUncacheablePip = 13005,
        DeterminismProbeEncounteredOutputDirectoryDifferentFiles = 13006,
        DeterminismProbeEncounteredNondeterministicDirectoryOutput = 13007,

        // Pip validation continued.
        InvalidOutputSinceDirectoryHasBeenProducedByAnotherPip = 13100,
        InvalidOutputSinceOutputIsBothSpecifiedAsFileAndDirectory = 13101,
        SourceDirectoryUsedAsDependency = 13102,

        // Cache initialization
        CacheIsStillBeingInitialized = 13200,

        // FingerprintStore saving
        MissingKeyWhenSavingFingerprintStore = 13300,
        FingerprintStoreSavingFailed = 13301,
        FingerprintStoreToCompareTrace = 13302,
        SuccessLoadFingerprintStoreToCompare = 13303,

        // Smell events
        ProcessPipsUncacheable = 14001,
        NoCriticalPathTableHits = 14002,
        NoSourceFilesUnchanged = 14003,
        ServerModeDisabled = 14004,
        GraphCacheCheckJournalDisabled = 14005,
        SlowCacheInitialization = 14006,
        LowMemory = 14007,
        ExportFingerprintsEnabled = 14008,
        ExportGraphEnabled = 14009,
        BuildHasPerfSmells = 14010,
        LogProcessesEnabled = 14011,
        FrontendIOSlow = 14012,

        // Graph validation.
        InvalidGraphSinceOutputDirectoryContainsSourceFile = 14100,
        InvalidGraphSinceOutputDirectoryContainsOutputFile = 14101,
        InvalidGraphSinceOutputDirectoryContainsSealedDirectory = 14102,
        InvalidGraphSinceFullySealedDirectoryIncomplete = 14103,
        InvalidGraphSinceSourceSealedDirectoryContainsOutputFile = 14104,
        InvalidGraphSinceSourceSealedDirectoryContainsOutputDirectory = 14105,
        InvalidGraphSinceSharedOpaqueDirectoryContainsExclusiveOpaqueDirectory = 14106,
        InvalidGraphSinceOutputDirectoryCoincidesSourceFile = 14107,
        InvalidGraphSinceOutputDirectoryCoincidesOutputFile = 14108,
        InvalidGraphSinceOutputDirectoryCoincidesSealedDirectory = 14109,
        InvalidGraphSinceSourceSealedDirectoryCoincidesSourceFile = 14110,
        InvalidGraphSinceSourceSealedDirectoryCoincidesOutputFile = 14111,
        PreserveOutputsDoNotApplyToSharedOpaques = 14112,

        // Dirty build
        DirtyBuildExplicitlyRequestedModules = 14200,
        DirtyBuildProcessNotSkippedDueToMissingOutput = 14201,
        DirtyBuildProcessNotSkipped = 14202,
        DirtyBuildStats = 14203,
        MinimumWorkersNotSatisfied = 14204,

        // Build set calculator
        BuildSetCalculatorStats = 14210,
        BuildSetCalculatorProcessStats = 14211,
        BuildSetCalculatorScheduleDependenciesUntilCleanAndMaterializedStats = 14212,

        // Special tool errors
        PipProcessToolErrorDueToHandleToFileBeingUsed = 14300,

        FailedToDuplicateSchedulerFile = 14400,

        // Sandbox kernel extension connection manger errors
        KextFailedToInitializeConnectionManager = 14500,
        KextFailureNotificationReceived = 14501
    }
}
