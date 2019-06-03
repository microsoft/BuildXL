// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities.Tracing;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Counters for <see cref="PipExecutor" />.
    /// Primarily one would retrieve these counters from <see cref="Scheduler.PipExecutionCounters"/>.
    /// </summary>
    public enum PipExecutorCounter
    {
        // These counters are ordered intentionally for ease of skimmability in the stats log file. Don't rearrange
        // arbitrarily.

        // ============================================================================================================
        // 1. First are high level stats for how long each pip type took
        // ============================================================================================================

        /// <summary>
        /// The amount of time it took to execute all CopyFile pips.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        CopyFileDuration,

        /// <summary>
        /// The amount of time it took to execute all WriteFile pips.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        WriteFileDuration,

        /// <summary>
        /// The amount of time it took to run all process pips. This includes all time pre and post processing as well.
        /// It may be nonzero even for a 100% cache hit build
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        ProcessDuration,

        // ============================================================================================================
        // 2. After the process pip we break that down into how long was spent doing components since it is generally the
        // most expensive pip type to process.
        // These are currently ordered by what is generally the most expensive operation first. And no time is double
        // counted in this section
        // ============================================================================================================

        /// <summary>
        /// The amount of time it took to execute all process pips. This only includes the time spent executing the process.
        /// It does not include any pre or post processing
        /// </summary>
        /// <remarks>
        /// In distributed builds, this counter only includes processes executed on one machine, even if the machine
        /// was acting as master for that build
        /// </remarks>
        [CounterType(CounterType.Stopwatch)]
        ExecuteProcessDuration,

        /// <summary>
        /// The time spent storing the process results to the cache
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        ProcessOutputsDuration,

        /// <summary>
        /// The amount of time spent in TryCheckProcessRunnableFromCacheAsync
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        CheckProcessRunnableFromCacheDuration,

        /// <summary>
        /// The amount of time spent in HashProcessDependenciesDuration
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        HashProcessDependenciesDuration,

        /// <summary>
        /// The amount of time spent in TryHashSourceFileDependenciesAsync
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        HashSourceFileDependenciesDuration,

        /// <summary>
        /// The time spent in ProcessSandboxedProcessResult. This includes verifying existence of ouputs, handling
        /// output streams, and other tasks
        /// </summary>
        SandboxedProcessProcessResultDurationMs,

        /// <summary>
        /// The time spent preparing the process sandbox to execute a process pip
        /// </summary>
        SandboxedProcessPrepDurationMs,

        /// <summary>
        /// The amount of time spent in OnPipCompleted
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        OnPipCompletedDuration,

        /// <summary>
        /// The time spent replaying the outputs from cache
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        RunProcessFromCacheDuration,

        // ============================================================================================================
        // 3. These are deeper breakdowns of the times from above. Durations here may overlap times from above
        // ============================================================================================================

        /// <summary>
        /// The amount of time it took to matrerialize the inputs for all run pips.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        InputMaterializationDuration,

        /// <summary>
        /// The amount of time it took to compute strong fingerprints from prior path sets. This is part
        /// of cache lookup for prior process executions.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        PriorPathSetEvaluationToProduceStrongFingerprintDuration,

        /// <summary>
        /// Amount of time spent computing strong fingerprints
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        ComputeStrongFingerprintDuration,

        /// <summary>
        /// The amount of time spent in SchedulePip
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        SchedulePipDuration,

        /// <summary>
        /// The amount of time spent in SchedulePip
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        InitialSchedulePipWallTime,

        /// <summary>
        /// The amount of time spent in InitSchedulerRuntimeState
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        InitSchedulerRuntimeStateDuration,

        /// <summary>
        /// The amount of time spent in FileSystemView.Create
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        CreateFileSystemViewDuration,

        /// <summary>
        /// The amount of time it took to execute process pips included in <see cref="ProcessPipsUncacheableImpacted"/>.
        /// </summary>
        ProcessPipsUncacheableImpactedDurationMs,

        /// <nodoc/>
        [CounterType(CounterType.Stopwatch)]
        CheckProcessRunnableFromCacheChapter1DetermineStrongFingerprintDuration,

        /// <nodoc/>
        [CounterType(CounterType.Stopwatch)]
        CheckProcessRunnableFromCacheChapter2RetrieveCacheEntryDuration,

        /// <nodoc/>
        [CounterType(CounterType.Stopwatch)]
        CheckProcessRunnableFromCacheChapter3RetrieveAndParseMetadataDuration,

        /// <nodoc/>
        [CounterType(CounterType.Stopwatch)]
        CheckProcessRunnableFromCacheChapter3LoadAndDeserializeDuration,

        /// <nodoc/>
        [CounterType(CounterType.Stopwatch)]
        CheckProcessRunnableFromCacheChapter4CheckContentAvailabilityDuration,

        /// <nodoc/>
        [CounterType(CounterType.Stopwatch)]
        CheckProcessRunnableFromCacheExecutionLogDuration,

        /// <nodoc/>
        [CounterType(CounterType.Stopwatch)]
        TryLoadPathSetFromContentCacheDuration,

        /// <nodoc/>
        [CounterType(CounterType.Stopwatch)]
        TryLoadPathSetFromContentCacheDeserializeDuration,

        /// <summary>
        /// The amount of time spent in cache-querying weak fingerprint
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        CacheQueryingWeakFingerprintDuration,

        /// <summary>
        /// The amount of time spent in <see cref="ComputeWeakFingerprintDuration"/>
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        ComputeWeakFingerprintDuration,

        /// <summary>
        /// The amount of time ObservedInputProcessor spent computing directory dependencies
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        ObservedInputProcessorPreProcessDuration,

        /// <summary>
        /// The amount of time ObservedInputProcessor spent listing contents of directory dependencies
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        ObservedInputProcessorPreProcessListDirectoriesDuration,

        /// <summary>
        /// The amount of time ObservedInputProcessor validating whether paths are under seal source directories
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        ObservedInputProcessorPreProcessValidateSealSourceDuration,

        /// <summary>
        /// The amount of time ObservedInputProcessor spent in Pass1
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        ObservedInputProcessorPass1InitializeObservationInfosDuration,

        /// <summary>
        /// The amount of time ObservedInputProcessor spent in Pass2
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        ObservedInputProcessorPass2ProcessObservationInfosDuration,

        /// <summary>
        /// The amount of time ObservedInputProcessor spent in ComputeSearchPathsAndFilter
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        ObservedInputProcessorComputeSearchPathsAndFilterDuration,

        /// <summary>
        /// The amount of time ObservedInputProcessor spent probing for file existence
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        ObservedInputProcessorTryProbeForExistenceDuration,

        /// <summary>
        /// The amount of time ObservedInputProcessor spent querying input content hashes
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        ObservedInputProcessorTryQuerySealedInputContentDuration,

        /// <summary>
        /// The amount of time ObservedInputProcessor spent querying directory fingerprints
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        ObservedInputProcessorTryQueryDirectoryFingerprintDuration,

        /// <summary>
        /// The amount of time ObservedInputProcessor spent in ProcessInternal
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        ObservedInputProcessorProcessInternalDuration,

        /// <summary>
        /// The amount of time it took to determine the set of dependencies which need to build
        /// as a result of missing inputs to the set of explicitly scheduled pips
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        ForceSkipDependenciesScheduleDependenciesUntilInputsPresentDuration,

        /// <summary>
        /// The time spent storing the process results to the cache
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        StoreProcessToCacheDurationMs,

        /// <summary>
        /// The amount of time it took to compute the build cone (dependencies and dependents).
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        BuildSetCalculatorComputeBuildCone,

        /// <summary>
        /// The amount of time it took to get nodes to schedule.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        BuildSetCalculatorGetNodesToSchedule,

        /// <summary>
        /// The amount of time it took to determine the set of dependencies which need to build for incremental scheduling.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        BuildSetCalculatorScheduleDependenciesUntilCleanAndMaterialized,

        /// <summary>
        /// The amount of time it took to compute the meta pips affected after the scheduled nodes are identified.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        BuildSetCalculatorComputeAffectedMetaPips,

        /// <summary>
        /// The amount of time it took during outputs processing to validate observed inputs.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        ProcessOutputsObservedInputValidationDuration,

        /// <summary>
        /// The amount of time it took during outputs processing to store content and create cache entry.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        ProcessOutputsStoreContentForProcessAndCreateCacheEntryDuration,

        /// <summary>
        /// The amount of time it took during outputs processing to hash/serialize and store a pip's output content.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        SerializeAndStorePipOutputDuration,

        /// <summary>
        /// The amount of time it took during outputs processing to hash/serialize and store pip metadata content.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        SerializeAndStorePipMetadataDuration,

        /// <summary>
        /// The amount of time it took during choosing a worker.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        ChooseWorkerCpuDuration,

        // ============================================================================================================
        // 4. These are count aggregates for how many times various operations happened
        // ============================================================================================================

        /// <summary>
        /// Number of lookups for process pip cache descriptors that succeeded (descriptor was found and was usable).
        /// A descriptor for which content was not found (but was needed) is still considered a hit.
        /// </summary>
        CacheHitsForProcessPipDescriptors,

        /// <summary>
        /// Number of process pips that were executed due to a cache miss / disabled cache.
        /// </summary>
        ProcessPipsExecutedDueToCacheMiss,

        #region components of ProcessPipsExecutedDueToCacheMiss

        /// <summary>
        /// Number of times a process pip cache descriptor was not usable due to mismatched strong fingerprints
        /// </summary>
        CacheMissesForDescriptorsDueToStrongFingerprints,

        /// <summary>
        /// Number of times a process pip cache entry was not found (no prior execution information).
        /// </summary>
        CacheMissesForDescriptorsDueToWeakFingerprints,

        /// <summary>
        /// Number of times a process pip was forced to be a cache miss (despite finding a descriptor) due to artifial cache miss injection.
        /// </summary>
        CacheMissesForDescriptorsDueToArtificialMissOptions,

        /// <summary>
        /// Numter of times strong fingerprint match was found but the corresponding <see cref="BuildXL.Engine.Cache.Fingerprints.CacheEntry"/> was not retrievable
        /// </summary>
        CacheMissesForCacheEntry,

        /// <summary>
        /// Number of times a process pip cache descriptor was found, but was invalid
        /// </summary>
        CacheMissesDueToInvalidDescriptors,
        
        /// <summary>
        /// Number of times a process pip cache descriptor was found but the metadata was not retrievable
        /// </summary>
        CacheMissesForProcessMetadata,

        /// <summary>
        /// Number of times a process pip cache descriptor was found from historicmetadatacache
        /// </summary>
        CacheMissesForProcessMetadataFromHistoricMetadata,

        /// <summary>
        /// Number of times a process pip cache descriptor was found, but the referenced output content was not available when needed.
        /// The cache descriptor has been counted as a part of <see cref="CacheHitsForProcessPipDescriptors"/>.
        /// </summary>
        CacheMissesForProcessOutputContent,

        /// <summary>
        /// Number of times a process pip was a miss due to being configured to always miss on cache lookup.
        /// </summary>
        CacheMissesForProcessConfiguredUncacheable,

        #endregion components of ProcessPipsExecutedDueToCacheMiss

        /// <summary>
        /// Aggregate size in bytes of content downloaded from a remote (shared) cache.
        /// </summary>
        RemoteContentDownloadedBytes,

        /// <summary>
        /// Number of processes pips that were satisfied via a remote cache (a descriptor was found remotely,
        /// and all content was found; though some or all content may have been available from a local cache).
        /// </summary>
        RemoteCacheHitsForProcessPipDescriptorAndContent,

        /// <summary>
        /// Number of times a process pip executed, but its result was not saved to cache (due to e.g. file monitoring warnings).
        /// </summary>
        ProcessPipsExecutedButUncacheable,

        /// <summary>
        /// Number of evaluations of prior path sets to derive a strong fingerprint. A single pip may evaluate multiple distinct path sets.
        /// This is part of cache-lookup for prior process executions.
        /// </summary>
        PriorPathSetsEvaluatedToProduceStrongFingerprint,

        /// <summary>
        /// Count of pips that are possibly impacted by uncacheability. This either means they were uncacheable themselves or
        /// are the downstream consumers of uncacheable pips and had to be executed due to a cache miss. Note that this
        /// could overspecify in the case a pip consumes an uncacheable pip would have run anyway because its direct input
        /// also changed.
        /// </summary>
        ProcessPipsUncacheableImpacted,

        /// <summary>
        /// How many times the SandboxedProcess failed with PreparationFailure
        /// </summary>
        PreparationFailureCount,

        /// <summary>
        /// How many times the SandboxedProcess failed with PreparationFailure with ErrorPartialCopy
        /// This is expected to be seen when we retry the pip because of Detours injection failure.
        /// </summary>
        PreparationFailurePartialCopyCount,

        /// <summary>
        /// Counts the number of instances where a pip that was a cache miss
        /// later turned out to be runnable from the cache, and the work that was
        /// done to build it is dropped.
        /// </summary>
        ProcessPipDeterminismRecoveredFromCache,

        /// <summary>
        /// Counts the time taken to start all the pip processes.
        /// </summary>
        ProcessStartTimeMs,

        /// <summary>
        /// Counts the number of processes not runnable from cache
        /// </summary>
        ProcessPipDeterminismProbeProcessCannotRunFromCache,

        /// <summary>
        /// In a given cache entry that BuildXL is attempting to publish, counts the number of files
        /// that match its cached counterpart. Valid only when the determinism probe is enabled.
        /// </summary>
        ProcessPipDeterminismProbeSameFiles,

        /// <summary>
        /// In a given cache entry that BuildXL is attempting to publish, counts the number of files
        /// that differ from its cached counterpart. Valid only when the determinism probe is enabled.
        /// </summary>
        ProcessPipDeterminismProbeDifferentFiles,

        /// <summary>
        /// In a given cache entry that BuildXL is attempting to publish, counts the number of output
        /// directories that are the same.  Valid only when the determinism probe is enabled.
        /// </summary>
        ProcessPipDeterminismProbeSameDirectories,

        /// <summary>
        /// In a given cache entry that BuildXL is attempting to publish, the number of files whose
        /// presence are non-deterministic.
        /// Valid only when the determinism probe is enabled.
        /// </summary>
        ProcessPipDeterminismProbeDifferentDirectories,

        /// <summary>
        /// The amount of time FileContentManager spent pinning content
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        FileContentManagerTryLoadAvailableContentDuration,

        /// <summary>
        /// The amount of time FileContentManager spent materializing content(excluding waiting
        /// on materialization semaphore)
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        FileContentManagerTryMaterializeDuration,

        /// <summary>
        /// The amount of time FileContentManager host spent materializing content (excluding waiting
        /// on materialization semaphore)
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        FileContentManagerHostTryMaterializeDuration,

        /// <summary>
        /// The amount of time FileContentManager spent materializing content (including waiting
        /// on materialization semaphore)
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        FileContentManagerTryMaterializeOuterDuration,

        /// <summary>
        /// The amount of time FileContentManager spent querying sealed input content
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        FileContentManagerTryQuerySealedInputContentDuration,

        /// <summary>
        /// The amount of time FileContentManager spent materializing a single file (for IPC pips)
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        FileContentManagerTryMaterializeFileDuration,

        /// <summary>
        /// The amount of time FileContentManager spent deleting directories
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        FileContentManagerDeleteDirectoriesDuration,

        /// <summary>
        /// The amount of time FileContentManager spent parsing paths for delete directories
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        FileContentManagerDeleteDirectoriesPathParsingDuration,

        /// <summary>
        /// The amount of time FileContentManager spent materializing content (excluding pinning content)
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        FileContentManagerPlaceFilesDuration,

        /// <summary>
        /// The time pips spent in the running state
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        PipRunningStateDuration,

        /// <summary>
        /// The time pips spent in <see cref="Scheduler.ExecutePipStep"/>
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        ExecutePipStepDuration,

        /// <summary>
        /// The active time of the <see cref="Tracing.OperationTracker"/>
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        OperationTrackerActiveDuration,

        /// <summary>
        /// The total time of service execution
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        ExecuteServiceDuration,

        /// <summary>
        /// The total time of service start
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        ExecuteServiceStartupLaunchDuration,

        /// <summary>
        /// The total time spent materializing inputs for services
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        ServiceInputMaterializationDuration,

        /// <summary>
        /// The total time to launch service shutdown
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        ExecuteServiceShutdownLaunchDuration,

        /// <summary>
        /// The total time of service shutdown
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        ExecuteServiceShutdownDuration,

        /// <summary>
        /// The total time to run service dependencies
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        RunServiceDependenciesDuration,

        /// <summary>
        /// The total duration of the critical path
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        CriticalPathDuration,

        /// <summary>
        /// The total count of process pip two phase cache entries which were added
        /// </summary>
        ProcessPipTwoPhaseCacheEntriesAdded,

        /// <summary>
        /// The total count of process pip two phase cache entries which had a conflicting
        /// entry which was converged
        /// </summary>
        ProcessPipTwoPhaseCacheEntriesConverged,

        /// <summary>
        /// Counts the number of externally running process pips
        /// </summary>
        ExternalProcessCount,

        /// <summary>
        /// Counts the number of retries for pips because of unobserved file accesses for outputs
        /// </summary>
        OutputsWithNoFileAccessRetriesCount,

        /// <summary>
        /// Counts the number of retries for pips because of mismatches of detours message count.
        /// </summary>
        MismatchMessageRetriesCount,

        /// <summary>
        /// Counts the number of retries for pips because of Azure Watson's 0xDEAD exit code.
        /// </summary>
        AzureWatsonExitCodeRetriesCount,

        /// <summary>
        /// Counts the number of retries for pips because users allow them to be retried, e.g., based on their exit codes.
        /// </summary>
        ProcessUserRetries,

        /// <summary>
        /// Counts the number of process pips executed on remote workers
        /// </summary>
        ProcessesExecutedRemotely,

        /// <summary>
        /// Counts the number of remote workers
        /// </summary>
        RemoteWorkerCount,

        /// <summary>
        /// Counts the number of available workers at the end of build
        /// </summary>
        AvailableWorkerCountAtEnd,

        /// <summary>
        /// Counts the number of workers who become available at any time
        /// </summary>
        EverAvailableWorkerCount,

        /// <summary>
        /// Average duration for worker to be available (containing the following states: started, starting, attached)
        /// </summary>
        WorkerAveragePendingDurationMs,

        /// <summary>
        /// Average running duration for workers that became available at any time
        /// </summary>
        WorkerAverageRunningDurationMs,
         
        /// <summary>
        /// The time spent scheduling dependent pips
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        ScheduleDependentsDuration,

        /// <summary>
        /// The time spent scheduling a dependent pip
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        ScheduleDependentDuration,

        /// <summary>
        /// The time spent scheduling a dependent pip
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        ScheduledByDependencyDuration,

        /// <summary>
        /// The time spent updating incremental scheduling state
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        UpdateIncrementalSchedulingStateDuration,

        /// <summary>
        /// The time spent updating incremental scheduling state
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        RecordDynamicObservationsDuration,

        /// <summary>
        /// Counts the number of Ipc pips executed on remote workers
        /// </summary>
        IpcPipsExecutedRemotely,

        /// <summary>
        /// The size of the ExecutionResult sent over Bond for process pips
        /// </summary>
        ProcessExecutionResultSize,

        /// <summary>
        /// The size of the ExecutionResult sent over Bond for ipc pips
        /// </summary>
        IpcExecutionResultSize,

        /// <summary>
        /// Counts the number of process pips failed on remote workers
        /// </summary>
        ProcessPipsFailedRemotely,

        /// <summary>
        /// Counts the number of process pips succeeded on remote workers
        /// </summary>
        ProcessPipsSucceededRemotely,

        /// <summary>
        /// Counts the number of ipc pips failed on remote workers
        /// </summary>
        IpcPipsFailedRemotely,

        /// <summary>
        /// Counts the number of ipc pips succeeded on remote workers
        /// </summary>
        IpcPipsSucceededRemotely,

        /// <summary>
        /// Counts the number of times processes were killed and retried due to resource limits
        /// </summary>
        ProcessRetriesDueToResourceLimits,

        /// <summary>
        /// The end-to-end time spent running a process, including any possible retries
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        ProcessPossibleRetryWallClockDuration,
        
        /// <summary>
        /// Time spent awaiting the result from the remote worker
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        AwaitRemoteResultDuration,

        /// <summary>
        /// Time spent reported for the remote operation.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        RemoteWorkerReportedExecutionDuration,

        /// <summary>
        /// Time spent processing the result from the remote worker
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        HandleRemoteResultDuration,

        /// <summary>
        /// Time spent processing the result from the remote worker
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        ExecutePipRemotelyDuration,

        /// <summary>
        /// Time spent executing the pip step on all workers
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        ExecuteStepOnAllRemotesDuration,

        /// <summary>
        /// Time spent executing the pip step on local worker
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        ExecuteStepLocallyDuration,

        /// <summary>
        /// The time spent querying RAM usage for process pips
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        QueryRamUsageDuration,

        /// <summary>
        /// The time spent cancelling processes due exceeding resource limits
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        ResourceLimitCancelProcessDuration,

        /// <summary>
        /// The time spent for processes that were canceled
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        CanceledProcessExecuteDuration,

        /// <summary>
        /// The amount of time FileContentManager spent hashing file content (including hashing semaphore waiting time) or getting usn number
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        FileContentManagerGetAndRecordFileContentHashDuration,

        /// <summary>
        /// The amount of time for pip graph post validation.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        PipGraphBuilderPostGraphValidation,

        /// <summary>
        /// Time spent handling a pip request on worker
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        WorkerServiceHandlePipStepDuration,

        /// <summary>
        /// Time spent executing a pip request on worker
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        WorkerServiceExecutePipStepDuration,

        /// <summary>
        /// Time spent reporting result of a pip request on worker
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        WorkerServiceReportPipStepDuration,

        /// <summary>
        /// Time a pip request spent queue on worker
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        WorkerServiceQueuedPipStepDuration,

        /// <summary>
        /// The maximum detours heap used in Bytes
        /// </summary>
        MaxDetoursHeapInBytes,

        /// <summary>
        /// Time a pip request spent acquiring resources
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        AcquireResourcesDuration,

        /// <summary>
        /// The time spent computing incremental scheduling state
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        ComputeIncrementalSchedulingStateDuration,

        /// <summary>
        /// The number of pips skipped due to clean and materialized.
        /// </summary>
        IncrementalSkipPipDueToCleanMaterialized,

        /// <summary>
        /// The number of processes skipped due to clean and materialized.
        /// </summary>
        IncrementalSkipProcessDueToCleanMaterialized,

        /// <summary>
        /// The number of pips marked clean.
        /// </summary>
        PipMarkClean,

        /// <summary>
        /// The number of pips marked materialized.
        /// </summary>
        PipMarkMaterialized,

        /// <summary>
        /// The number of pips marked perpetually dirty.
        /// </summary>
        PipMarkPerpetuallyDirty,

        /// <summary>
        /// The number of absent paths that are eliminated with the optimizations.
        /// </summary>
        NumAbsentPathsEliminated,

        /// <summary>
        /// The time materializing content to verify cache lookup outputs
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        TryLoadAvailableOutputContent_VerifyCacheLookupPinDuration,

        /// <summary>
        /// The time doing normal pinning content to verify cache lookup outputs
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        TryLoadAvailableOutputContent_PinDuration,

        /// <summary>
        /// The time spent in WhenDone after the queues are drained.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        AfterDrainingWhenDoneDuration,

        /// <summary>
        /// The time spent to create symlink including waiting for semaphore.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        TryCreateSymlinkOuterDuration,

        /// <summary>
        /// The time spent to create symlink.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        TryCreateSymlinkDuration,

        /// <summary>
        /// The time spent to create symlink for file materialization.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        TryMaterializeSymlinkDuration,

        /// <summary>
        /// The number of real filesystem directory enumerations.
        /// </summary>
        RealFilesystemDirectoryEnumerations,

        /// <summary>
        /// The time spent to enumerate the directories via real filesystem
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        RealFilesystemDirectoryEnumerationsDuration,

        /// <summary>
        /// The number of minimal graph directory enumerations.
        /// </summary>
        MinimalGraphDirectoryEnumerations,

        /// <summary>
        /// The time spent to enumerate the directories via minimal graph
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        MinimalGraphDirectoryEnumerationsDuration,

        /// <summary>
        /// The number of full graph directory enumerations.
        /// </summary>
        FullGraphDirectoryEnumerations,

        /// <summary>
        /// The time spent to enumerate the directories via full graph 
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        FullGraphDirectoryEnumerationsDuration,

        /// <summary>
        /// The number of existing directory probes in pathsets
        /// </summary>
        ExistingDirectoryProbes,

        /// <summary>
        /// The number of existing file probes in pathsets
        /// </summary>
        ExistingFileProbes,

        /// <summary>
        /// The number of absent path probes in pathsets
        /// </summary>
        AbsentPathProbes,

        /// <summary>
        /// The number of file content read in pathsets
        /// </summary>
        FileContentReads,

        /// <summary>
        /// The number of directory enumeration in pathsets
        /// </summary>
        DirectoryEnumerations,

        /// <nodoc/>
        UniqueDirectoriesAllowAllFilter,

        /// <nodoc/>
        UniqueDirectoriesSearchPathFilter,

        /// <nodoc/>
        UniqueDirectoriesUnionFilter,

        /// <nodoc/>
        UniqueDirectoriesRegexFilter,

        /// <summary>
        /// The time spent to filter the contents of the directories
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        DirectoryEnumerationFilterDuration,

        /// <summary>
        /// The time spent to hash the contents of a write file pip.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        WriteFileHashingDuration,

        /// <summary>
        /// The number of output file handles created with sequential scan.
        /// </summary>
        CreateOutputFileHandleWithSequentialScan,

        /// <summary>
        /// The time spent to analyze the file access violations.
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        AnalyzeFileAccessViolationsDuration,

        /// <summary>
        /// The time spent to report metadata and pathset on master
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        ReportRemoteMetadataAndPathSetDuration,

        /// <nodoc/>
        [CounterType(CounterType.Stopwatch)]
        RemoteWorker_PrepareAndSendBuildRequestsDuration,

        /// <nodoc/>
        [CounterType(CounterType.Stopwatch)]
        RemoteWorker_ExtractHashesDuration,

        /// <nodoc/>
        [CounterType(CounterType.Stopwatch)]
        RemoteWorker_CollectPipFilesToMaterializeDuration,

        /// <nodoc/>
        [CounterType(CounterType.Stopwatch)]
        RemoteWorker_CreateFileArtifactKeyedHashDuration,

        /// <nodoc/>
        [CounterType(CounterType.Stopwatch)]
        RemoteWorker_BuildRequestSendDuration,

        /// <nodoc/>
        [CounterType(CounterType.Stopwatch)]
        RemoteWorker_AwaitExecutionBlobCompletionDuration,

        /// <nodoc/>
        RemoteWorker_EarlyReleaseDrainDurationMs,

        /// <nodoc/>
        RemoteWorker_EarlyReleaseSavingDurationMs,

        /// <nodoc/>
        BuildRequestBatchesSentToWorkers,

        /// <nodoc/>
        BuildRequestBatchesFailedSentToWorkers,

        /// <nodoc/>
        HashesSentToWorkers,

        /// <nodoc/>
        [CounterType(CounterType.Stopwatch)]
        IpcSendAndHandleDuration,

        /// <nodoc/>
        Ipc_RequestQueueDurationMs,

        /// <nodoc/>
        Ipc_RequestSendDurationMs,

        /// <nodoc/>
        Ipc_RequestServerAckDurationMs,

        /// <nodoc/>
        Ipc_ResponseDurationMs,

        /// <nodoc/>
        Ipc_ResponseDeserializeDurationMs,

        /// <nodoc/>
        Ipc_ResponseQueueSetDurationMs,

        /// <nodoc/>
        Ipc_ResponseSetDurationMs,

        /// <nodoc/>
        Ipc_ResponseAfterSetTaskDurationMs,

        /// <nodoc/>
        ObservedInputProcessorComputePipFileSystemPaths,

        /// <nodoc/>
        [CounterType(CounterType.Stopwatch)]
        ObservedInputProcessorReportUnexpectedAccess,

        /// <nodoc/>
        [CounterType(CounterType.Stopwatch)]
        RegisterStaticDirectory,

        /// <nodoc/>
        MaxPathSetsDownloadedForHit,

        /// <nodoc/>
        MaxCacheEntriesVisitedForHit,

        /// <nodoc/>
        MinPathSetsDownloadedForHit,

        /// <nodoc/>
        MinCacheEntriesVisitedForHit,

        /// <nodoc/>
        MaxPathSetsDownloadedForMiss,

        /// <nodoc/>
        MaxCacheEntriesVisitedForMiss,

        /// <nodoc/>
        MinPathSetsDownloadedForMiss,

        /// <nodoc/>
        MinCacheEntriesVisitedForMiss,

        /// <nodoc/>
        NumPipsUsingMinimalGraphFileSystem
    }

    /// <summary>
    /// Select counters that are aggregated for a group of pips that match a specified criteria.
    /// </summary>
    /// <remarks>
    /// In distributed builds, these counters on the master will include pips executed on workers.
    /// </remarks>
    public enum PipCountersByGroup
    {
        /// <summary>
        /// The number of pips that match the group criteria.
        /// </summary>
        Count,

        /// <summary>
        /// The number of pips that were cache misses.
        /// </summary>
        CacheMiss,

        /// <summary>
        /// The number of pips that were cache hits.
        /// </summary>
        CacheHit,

        /// <summary>
        /// The number of pips that were failed.
        /// </summary>
        Failed,

        /// <summary>
        /// The number of pips that were skipped due to failed dependencies.
        /// </summary>
        Skipped,

        /// <summary>
        /// The amount of time it took to run all process pips. This includes all time pre and post processing as well.
        /// It may be nonzero even for a 100% cache hit build
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        ProcessDuration,

        /// <summary>
        /// The amount of time it took to execute all process pips. This only includes the time spent executing the process.
        /// It does not include any pre or post processing
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        ExecuteProcessDuration,

        /// <summary>
        /// The number of bytes read by pips in the group.
        /// </summary>
        IOReadBytes,

        /// <summary>
        /// The number of bytes written by pips in the group.
        /// </summary>
        IOWriteBytes
    }
}
