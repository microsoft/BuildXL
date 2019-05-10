// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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

        // RESERVED TO [3600, 3999] (BuildXL.Scheduler.dll)
        ProcessStatus = 3600,
        AbortObservedInputProcessorBecauseFileUntracked = 3601,
        PipFailedDueToDependenciesCannotBeHashed = 3602,
        ScheduleHashedOutputFile = 3603,
        PipFailedDueToOutputsCannotBeHashed = 3604,
        PreserveOutputsFailedToMakeOutputPrivate = 3605,
        PipStatusNonOverwriteable = 3606,
        StoppingProcessExecutionDueToResourceExhaustion = 3607,
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
        PerformanceDataCacheTrace = 3621,
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

        PipTwoPhaseCacheGetCacheEntry = 3653,
        PipTwoPhaseCachePublishCacheEntry = 3654,
        ScheduleProcessNotStoredToWarningsUnderWarnAsError = 3655,
        // was ScheduleProcessNotStoredDueToMissingOutputs = 3656,


        // Historic metadata cache warnings
        HistoricMetadataCacheCreateFailed = 3660,
        HistoricMetadataCacheOperationFailed = 3661,
        HistoricMetadataCacheSaveFailed = 3662,
        HistoricMetadataCacheCloseCalled = 3663,
        HistoricMetadataCacheLoadFailed = 3664,

        PipCacheMetadataBelongToAnotherPip = 3700,

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
