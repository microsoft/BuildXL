// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Diagnostics.Tracing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Engine.Distribution.OpenBond;
using BuildXL.Engine.Tracing;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Artifacts;
using BuildXL.Scheduler.Distribution;
using BuildXL.Storage;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Engine.Distribution
{

    /// <summary>
    /// Back end of the worker service, i.e. core methods that interact with the engine.
    /// </summary>
    internal interface IWorkerPipExecutionService : IDisposable
    {
        /// <summary>
        /// Id of the worker in the distributed build.
        /// </summary>
        uint WorkerId { get; }

        /// <summary>
        /// Starts the execution service with the BuildStartData received from the orchestrator.
        /// </summary>
        void Start(EngineSchedule schedule, BuildStartData buildStartData);

        /// <summary>
        /// Creates and returns the data to be sent to the orchestrator for validating the cache connection
        /// </summary>
        Task<Possible<AttachCompletionInfo>> ConstructAttachCompletionInfo();

        /// <summary>
        /// Called when the build is done
        /// </summary>
        void WhenDone();

        /// <summary>
        /// Report inputs needed to execute pip steps to the file content manager
        /// </summary>
        Possible<Unit> TryReportInputs(List<FileArtifactKeyedHash> hashes);
        
        /// <summary>
        /// Executes the requested pip step and reports the result to the worker service
        /// </summary>
        Task HandlePipStepAsync(PipId pipId, ExtendedPipCompletionData pipCompletionData, SinglePipBuildRequest pipBuildRequest, Possible<Unit> reportInputsResult);
        
        /// <nodoc />
        void Transition(PipId pipId, WorkerPipState state);

        /// <summary>
        /// Gets the description for a pip from a PipId. For logging purposes.
        /// </summary>
        string GetPipDescription(PipId pipId);
    }

    /// <summary>
    /// Defines service run on worker nodes in a distributed build.
    /// </summary>
    public sealed partial class WorkerService {

        /// <remarks>
        /// Abstracted from the WorkerService (which is the "front end" for communications) for testing purposes.
        /// </remarks>
        private sealed class WorkerPipExecutionService : IWorkerPipExecutionService
        {
            // State shared with the front end of the service
            private readonly WorkerService m_workerService;
            private LoggingContext LoggingContext => m_workerService.m_appLoggingContext;
            private ConcurrentDictionary<(PipId, PipExecutionStep), SinglePipBuildRequest> PendingBuildRequests => m_workerService.m_pendingBuildRequests;
            private ConcurrentDictionary<(PipId, PipExecutionStep), ExtendedPipCompletionData> PendingPipCompletions => m_workerService.m_pendingPipCompletions;

            // Scheduler & engine state
            private IConfiguration Config => m_workerService.m_config;
            private PipTable m_pipTable;
            private PipQueue m_pipQueue;
            private Scheduler.Scheduler m_scheduler;
            private IPipExecutionEnvironment m_environment;
            private WorkerServicePipStateManager m_workerPipStateManager;
            private readonly WorkerRunnablePipObserver m_workerRunnablePipObserver;
            private Scheduler.Tracing.OperationTracker m_operationTracker;

            /// <summary>
            /// Class constructor
            /// </summary>
            public WorkerPipExecutionService(WorkerService workerService)
            {
                m_workerService = workerService;
                m_workerRunnablePipObserver = new WorkerRunnablePipObserver(this);
            }
            
            /// <inheritdoc/>
            void IWorkerPipExecutionService.Start(EngineSchedule schedule, BuildStartData buildStartData)
            {
                Contract.Requires(schedule != null);
                Contract.Requires(schedule.Scheduler != null);
                Contract.Requires(schedule.SchedulingQueue != null);

                m_workerPipStateManager = new WorkerServicePipStateManager(this);
                m_pipTable = schedule.PipTable;
                m_pipQueue = schedule.SchedulingQueue;
                m_scheduler = schedule.Scheduler;
                m_operationTracker = m_scheduler.OperationTracker;
                m_environment = m_scheduler;
                m_environment.ContentFingerprinter.FingerprintSalt = buildStartData.FingerprintSalt;
                m_environment.State.PipEnvironment.OrchestratorEnvironmentVariables = buildStartData.EnvironmentVariables;
            }

            /// <inheritdoc/>
            public uint WorkerId => m_workerService.WorkerId;

            /// <inheritdoc/>
            void IWorkerPipExecutionService.WhenDone() => m_pipQueue.SetAsFinalized();

            /// <inheritdoc/>
            [System.Diagnostics.CodeAnalysis.SuppressMessage("AsyncUsage", "AsyncFixer02:MissingAsyncOpportunity")]
            async Task<Possible<AttachCompletionInfo>> IWorkerPipExecutionService.ConstructAttachCompletionInfo()
            {
                var cacheValidationContent = Guid.NewGuid().ToByteArray();
                var cacheValidationContentHash = ContentHashingUtilities.HashBytes(cacheValidationContent);

                var possiblyStored = await m_environment.Cache.ArtifactContentCache.TryStoreAsync(
                    new MemoryStream(cacheValidationContent),
                    cacheValidationContentHash);

                if (!possiblyStored.Succeeded)
                {
                    Logger.Log.DistributionFailedToStoreValidationContentToWorkerCacheWithException(
                        LoggingContext,
                        cacheValidationContentHash.ToHex(),
                        possiblyStored.Failure.DescribeIncludingInnerFailures());

                    return new Possible<AttachCompletionInfo>(possiblyStored.Failure);
                }

                var attachCompletionInfo = new AttachCompletionInfo
                {
                    WorkerId = WorkerId,
                    MaxProcesses = Config.Schedule.MaxProcesses,
                    MaxMaterialize = Config.Schedule.MaxMaterialize,
                    AvailableRamMb = m_scheduler.LocalWorker.TotalRamMb,
                    AvailableCommitMb = m_scheduler.LocalWorker.TotalCommitMb,
                    WorkerCacheValidationContentHash = cacheValidationContentHash.ToBondContentHash(),
                };

                Contract.Assert(attachCompletionInfo.WorkerCacheValidationContentHash != null, "worker cache validation content hash is null");

                return new Possible<AttachCompletionInfo>(attachCompletionInfo);
            }

            /// <inheritdoc/>
            async Task IWorkerPipExecutionService.HandlePipStepAsync(PipId pipId, ExtendedPipCompletionData pipCompletionData, SinglePipBuildRequest pipBuildRequest, Possible<Unit> reportInputsResult)
            {
                // Do not block the caller.
                await Task.Yield();

                var pip = m_pipTable.HydratePip(pipId, PipQueryContext.HandlePipStepOnWorker);
                var pipType = pip.PipType;
                var step = (PipExecutionStep)pipBuildRequest.Step;

                pipCompletionData.SemiStableHash = m_pipTable.GetPipSemiStableHash(pipId);
                pipCompletionData.PipType = m_pipTable.GetPipType(pipId);

                // To preserve the path set casing is an option only available for process pips
                pipCompletionData.PreservePathSetCasing = pip.PipType == PipType.Process && ((Process)pip).PreservePathSetCasing;

                if (!(pipType == PipType.Process || pipType == PipType.Ipc || step == PipExecutionStep.MaterializeOutputs))
                {
                    throw Contract.AssertFailure(I($"Workers can only execute process or IPC pips for steps other than MaterializeOutputs: Step={step}, PipId={pipId}, Pip={pip.GetDescription(m_environment.Context)}"));
                }

                using (var operationContext = m_operationTracker.StartOperation(PipExecutorCounter.WorkerServiceHandlePipStepDuration, pipId, pipType, LoggingContext))
                using (operationContext.StartOperation(step))
                {
                    var pipInfo = new PipInfo(pip, m_environment.Context);

                    if (!reportInputsResult.Succeeded)
                    {
                        // Could not report inputs due to input mismatch. Fail the pip
                        Scheduler.Tracing.Logger.Log.PipMaterializeDependenciesFailureDueToVerifySourceFilesFailed(
                            LoggingContext,
                            pipInfo.Description,
                            reportInputsResult.Failure.DescribeIncludingInnerFailures());

                        m_workerService.ReportResult(
                            pip.PipId,
                            ExecutionResult.GetFailureNotRunResult(LoggingContext),
                            step);

                        return;
                    }

                    if (step == PipExecutionStep.CacheLookup)
                    {
                        // Directory dependencies need to be registered for cache lookup.
                        // For process execution, the input materialization guarantees the registration.
                        m_environment.State.FileContentManager.RegisterDirectoryDependencies(pipInfo.UnderlyingPip);
                    }

                    m_workerPipStateManager.Transition(pipId, WorkerPipState.Queued);
                    m_scheduler.HandlePipRequest(pipId, m_workerRunnablePipObserver, step, pipBuildRequest.Priority);

                    // Track how much time the request spent queued
                    using (var op = operationContext.StartOperation(PipExecutorCounter.WorkerServiceQueuedPipStepDuration))
                    {
                        await pipCompletionData.StepExecutionStarted.Task;
                        pipCompletionData.SerializedData.QueueTicks = op.Duration.Value.Ticks;

                    }

                    ExecutionResult executionResult;

                    // Track how much time the request spent executing
                    using (operationContext.StartOperation(PipExecutorCounter.WorkerServiceExecutePipStepDuration))
                    {
                        executionResult = await pipCompletionData.StepExecutionCompleted.Task;
                    }

                    m_workerService.ReportResult(
                        pip.PipId,
                        executionResult,
                        step);
                }
            }

            /// <inheritdoc />
            void IWorkerPipExecutionService.Transition(PipId pipId, WorkerPipState state) => m_workerPipStateManager.Transition(pipId, state);

            /// <inheritdoc />
            Possible<Unit> IWorkerPipExecutionService.TryReportInputs(List<FileArtifactKeyedHash> hashes)
            {
                var dynamicDirectoryMap = new Dictionary<DirectoryArtifact, List<FileArtifactWithAttributes>>();
                var failedFiles = new List<(FileArtifact file, ContentHash hash)>();
                var fileContentManager = m_environment.State.FileContentManager;

                foreach (FileArtifactKeyedHash fileArtifactKeyedHash in hashes)
                {
                    FileArtifactWithAttributes fileWithAttributes;
                    FileArtifact file;
                    if (fileArtifactKeyedHash.AssociatedDirectories != null && fileArtifactKeyedHash.AssociatedDirectories.Count != 0)
                    {
                        // All workers have the same entries in the path table up to the schedule phase. Dynamic outputs add entries to the path table of the worker that generates them.
                        // Dynamic outputs can be generated by one worker, but consumed by another, which potentially is not the orchestrator. Thus, the two workers do not have the same entries
                        // in the path table.
                        // The approach we have here may be sub-optimal(the worker may have had the paths already). But it ensures correctness.
                        // Need to create absolute path because the path is potentially not in the path table.
                        file = new FileArtifact(
                            AbsolutePath.Create(m_environment.Context.PathTable, fileArtifactKeyedHash.PathString),
                            fileArtifactKeyedHash.RewriteCount);

                        fileWithAttributes = FileArtifactWithAttributes.Create(
                            file,
                            FileExistence.Required,
                            isUndeclaredFileRewrite: fileArtifactKeyedHash.IsAllowedFileRewrite);

                        foreach (var bondDirectoryArtifact in fileArtifactKeyedHash.AssociatedDirectories)
                        {
                            var directory = new DirectoryArtifact(
                                new AbsolutePath(bondDirectoryArtifact.DirectoryPathValue),
                                bondDirectoryArtifact.DirectorySealId,
                                bondDirectoryArtifact.IsDirectorySharedOpaque);

                            if (!dynamicDirectoryMap.TryGetValue(directory, out var files))
                            {
                                files = new List<FileArtifactWithAttributes>();
                                dynamicDirectoryMap.Add(directory, files);
                            }

                            files.Add(fileWithAttributes);
                        }
                    }
                    else
                    {
                        file = fileArtifactKeyedHash.File;
                    }

                    if (fileArtifactKeyedHash.IsSourceAffected)
                    {
                        fileContentManager.SourceChangeAffectedInputs.ReportSourceChangedAffectedFile(file.Path);
                    }

                    var materializationInfo = fileArtifactKeyedHash.GetFileMaterializationInfo(m_environment.Context.PathTable);
                    if (!fileContentManager.ReportWorkerPipInputContent(
                        LoggingContext,
                        file,
                        materializationInfo))
                    {
                        failedFiles.Add((file, materializationInfo.Hash));
                    }
                }

                foreach (var directoryAndContents in dynamicDirectoryMap)
                {
                    fileContentManager.ReportDynamicDirectoryContents(directoryAndContents.Key, directoryAndContents.Value, PipOutputOrigin.NotMaterialized);
                }

                if (failedFiles.Count != 0)
                {
                    return new ArtifactMaterializationFailure(failedFiles.ToReadOnlyArray(), m_environment.Context.PathTable);
                }

                return Unit.Void;
            }

            /// <inheritdoc />
            string IWorkerPipExecutionService.GetPipDescription(PipId pipId)
            {
                var pip = m_pipTable.HydratePip(pipId, PipQueryContext.LoggingPipFailedOnWorker);
                return pip.GetDescription(m_environment.Context);
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                m_workerService.LogStatistics(LoggingContext);
                m_workerPipStateManager?.Dispose();
            }

            private void StartStep(RunnablePip runnablePip)
            {
                var pipId = runnablePip.PipId;
                var processRunnable = runnablePip as ProcessRunnablePip;

                Tracing.Logger.Log.DistributionWorkerExecutePipRequest(
                    runnablePip.LoggingContext,
                    runnablePip.Pip.SemiStableHash,
                    runnablePip.Description,
                    runnablePip.Step.AsString());

                var pipIdStepTuple = (pipId, runnablePip.Step);
                var completionData = PendingPipCompletions[pipIdStepTuple];
                completionData.StepExecutionStarted.SetResult(true);

                switch (runnablePip.Step)
                {
                    case PipExecutionStep.ExecuteProcess:
                        if (runnablePip.PipType == PipType.Process)
                        {
                            SinglePipBuildRequest pipBuildRequest;
                            bool found = PendingBuildRequests.TryGetValue(pipIdStepTuple, out pipBuildRequest);
                            Contract.Assert(found, "Could not find corresponding build request for executed pip on worker");
                            PendingBuildRequests[pipIdStepTuple] = null;

                            // Set the cache miss result with fingerprint so ExecuteProcess step can use it
                            var fingerprint = pipBuildRequest.Fingerprint.ToFingerprint();
                            processRunnable.SetCacheResult(RunnableFromCacheResult.CreateForMiss(new WeakContentFingerprint(fingerprint)));

                            processRunnable.ExpectedMemoryCounters = ProcessMemoryCounters.CreateFromMb(
                                peakWorkingSetMb: pipBuildRequest.ExpectedPeakWorkingSetMb,
                                averageWorkingSetMb: pipBuildRequest.ExpectedAverageWorkingSetMb,
                                peakCommitSizeMb: pipBuildRequest.ExpectedPeakCommitSizeMb,
                                averageCommitSizeMb: pipBuildRequest.ExpectedAverageCommitSizeMb);
                        }

                        break;
                }
            }

            private void EndStep(RunnablePip runnablePip)
            {
                var pipId = runnablePip.PipId;
                var loggingContext = runnablePip.LoggingContext;
                var pip = runnablePip.Pip;
                var description = runnablePip.Description;
                var executionResult = runnablePip.ExecutionResult;


                var completionData = PendingPipCompletions[(pipId, runnablePip.Step)];
                completionData.SerializedData.ExecuteStepTicks = runnablePip.StepDuration.Ticks;
                completionData.SerializedData.ThreadId = runnablePip.ThreadId;
                completionData.SerializedData.StartTimeTicks = runnablePip.StepStartTime.Ticks;

                switch (runnablePip.Step)
                {
                    case PipExecutionStep.MaterializeInputs:
                        if (!runnablePip.Result.HasValue ||
                            !runnablePip.Result.Value.Status.IndicatesFailure())
                        {
                            m_workerPipStateManager.Transition(pipId, WorkerPipState.Prepped);
                        }

                        break;
                    case PipExecutionStep.ExecuteProcess:
                    case PipExecutionStep.ExecuteNonProcessPip:
                        executionResult.Seal();
                        m_workerPipStateManager.Transition(pipId, WorkerPipState.Executed);

                        if (!executionResult.Result.IndicatesFailure())
                        {
                            foreach (var outputContent in executionResult.OutputContent)
                            {
                                Tracing.Logger.Log.DistributionWorkerPipOutputContent(
                                    loggingContext,
                                    pip.SemiStableHash,
                                    description,
                                    outputContent.fileArtifact.Path.ToString(m_environment.Context.PathTable),
                                    outputContent.fileInfo.Hash.ToHex());
                            }
                        }

                        break;
                    case PipExecutionStep.CacheLookup:
                        var runnableProcess = (ProcessRunnablePip)runnablePip;

                        executionResult = new ExecutionResult();
                        var cacheResult = runnableProcess.CacheResult;

                        executionResult.SetResult(
                            loggingContext,
                            status: cacheResult == null ? PipResultStatus.Failed : PipResultStatus.Succeeded);

                        if (cacheResult != null)
                        {
                            executionResult.PopulateCacheInfoFromCacheResult(cacheResult);
                        }

                        executionResult.CacheLookupPerfInfo = runnableProcess.CacheLookupPerfInfo;
                        executionResult.Seal();

                        break;
                    case PipExecutionStep.PostProcess:
                        // Execution result is already computed during ExecuteProcess.
                        Contract.Assert(executionResult != null);
                        break;
                }

                if (executionResult == null)
                {
                    executionResult = new ExecutionResult();

                    // If no result is set, the step succeeded
                    executionResult.SetResult(loggingContext, runnablePip.Result?.Status ?? PipResultStatus.Succeeded);
                    executionResult.Seal();
                }

                completionData.StepExecutionCompleted.SetResult(executionResult);
            }

            private Guid GetActivityId(RunnablePip runnablePip)
            {
                return new Guid(PendingBuildRequests[(runnablePip.PipId, runnablePip.Step)].ActivityId);
            }

            private sealed class WorkerRunnablePipObserver : RunnablePipObserver
            {
                private readonly WorkerPipExecutionService m_workerService;
                private readonly ConcurrentDictionary<PipId, ExecutionResult> m_processExecutionResult
                     = new ConcurrentDictionary<PipId, ExecutionResult>();

                public WorkerRunnablePipObserver(WorkerPipExecutionService workerService)
                {
                    m_workerService = workerService;
                }

                public override Guid? GetActivityId(RunnablePip runnablePip)
                {
                    return m_workerService.GetActivityId(runnablePip);
                }

                public override void StartStep(RunnablePip runnablePip)
                {
                    if (runnablePip.Step == PipExecutionStep.PostProcess)
                    {
                        ExecutionResult executionResult;
                        var removed = m_processExecutionResult.TryRemove(runnablePip.PipId, out executionResult);
                        Contract.Assert(removed, "Execution result must be stored from ExecuteProcess step for PostProcess");
                        runnablePip.SetExecutionResult(executionResult);
                    }

                    m_workerService.StartStep(runnablePip);
                }

                public override void EndStep(RunnablePip runnablePip)
                {
                    if (runnablePip.Step == PipExecutionStep.ExecuteProcess)
                    {
                        // For successful/unsuccessful results of ExecuteProcess, store so that when orchestrator calls worker for
                        // PostProcess it can reuse the result rather than sending it unnecessarily
                        // The unsuccessful results are stored as well to preserve the existing behavior where PostProcess is also done for such results.
                        // TODO: Should we skipped PostProcess when Process failed? In such a case then PipExecutor.ReportExecutionResultOutputContent should not be in PostProcess.
                        m_processExecutionResult[runnablePip.PipId] = runnablePip.ExecutionResult;
                    }

                    m_workerService.EndStep(runnablePip);
                }
            }

            private sealed class WorkerServicePipStateManager : WorkerPipStateManager, IDisposable
            {
                private readonly StatusReporter m_statusReporter;
                private readonly WorkerPipExecutionService m_workerService;

                public WorkerServicePipStateManager(WorkerPipExecutionService workerService)
                {
                    m_workerService = workerService;
                    m_statusReporter = new StatusReporter(m_workerService.LoggingContext, this);
                }

                private sealed class StatusReporter : BaseEventListener
                {
                    private readonly LoggingContext m_loggingContext;
                    private Snapshot m_pipStateSnapshot;

                    public StatusReporter(LoggingContext loggingContext, WorkerServicePipStateManager stateManager)
                        : base(Events.Log, warningMapper: null, eventMask: new EventMask(enabledEvents: new int[] { (int)SharedLogEventId.PipStatus }, disabledEvents: null))
                    {
                        m_loggingContext = loggingContext;
                        m_pipStateSnapshot = stateManager.GetSnapshot();
                    }

                    private void ReportStatus()
                    {
                        var pipStateSnapshot = Interlocked.Exchange(ref m_pipStateSnapshot, null);
                        if (pipStateSnapshot != null)
                        {
                            pipStateSnapshot.Update();
                            Logger.Log.DistributionWorkerStatus(
                                m_loggingContext,
                                pipsQueued: pipStateSnapshot[WorkerPipState.Queued],
                                pipsPrepping: pipStateSnapshot[WorkerPipState.Prepping],
                                pipsPrepped: pipStateSnapshot[WorkerPipState.Prepped],
                                pipsRecording: pipStateSnapshot[WorkerPipState.Recording],
                                pipsReporting: pipStateSnapshot[WorkerPipState.Reporting],
                                pipsDone: pipStateSnapshot[WorkerPipState.Done]);
                            m_pipStateSnapshot = pipStateSnapshot;
                        }
                    }

                    protected override void OnEventWritten(EventWrittenEventArgs eventData)
                    {
                        if (eventData.EventId == (int)SharedLogEventId.PipStatus)
                        {
                            ReportStatus();
                        }
                    }

                    protected override void OnCritical(EventWrittenEventArgs eventData)
                    {
                    }

                    protected override void OnWarning(EventWrittenEventArgs eventData)
                    {
                    }

                    protected override void OnError(EventWrittenEventArgs eventData)
                    {
                    }

                    protected override void OnInformational(EventWrittenEventArgs eventData)
                    {
                    }

                    protected override void OnVerbose(EventWrittenEventArgs eventData)
                    {
                    }

                    protected override void OnAlways(EventWrittenEventArgs eventData)
                    {
                    }
                }

                #region IDisposable Members

                public void Dispose()
                {
                    m_statusReporter.Dispose();
                }

                #endregion
            }
        }
    }
}
