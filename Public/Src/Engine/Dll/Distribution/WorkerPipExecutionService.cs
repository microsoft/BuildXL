// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Distribution.Grpc;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Artifacts;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Core.Tasks;
using BuildXL.Utilities.Instrumentation.Common;
using static BuildXL.Tracing.Diagnostics;
using static BuildXL.Utilities.Core.FormattableStringEx;
using static BuildXL.Utilities.PipSpecificPropertiesConfig;

namespace BuildXL.Engine.Distribution
{

    /// <summary>
    /// Back end of the worker service, i.e. core methods that interact with the engine.
    /// </summary>
    internal interface IWorkerPipExecutionService
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
        AttachCompletionInfo ConstructAttachCompletionInfo();

        /// <summary>
        /// Called when the build is done
        /// </summary>
        void WhenDone();

        /// <summary>
        /// Report inputs needed to execute pip steps to the file content manager
        /// </summary>
        Possible<Unit> TryReportInputs(IEnumerable<FileArtifactKeyedHash> hashes);
        
        /// <summary>
        /// Starts executing the requested pip step
        /// </summary>
        Task StartPipStepAsync(PipId pipId, ExtendedPipCompletionData pipCompletionData, SinglePipBuildRequest pipBuildRequest, Possible<Unit> reportInputsResult);

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
           
            /// <summary>
            /// Active pip steps: elements are added when receiving a build request, and removed when we are ready to report a result
            /// </summary>
            private ConcurrentDictionary<(PipId, PipExecutionStep), ExtendedPipCompletionData> PendingPipCompletions => m_workerService.m_pendingPipCompletions;
           
            /// <summary>
            /// Requested pip steps that were already processed but might still not have been scheduled
            /// </summary>
            private ConcurrentDictionary<(PipId, PipExecutionStep), bool> PendingScheduleRequests => m_workerService.PendingScheduleRequests;

            // Scheduler & engine state
            private IConfiguration Config => m_workerService.m_config;
            private PipTable m_pipTable;
            private PipQueue m_pipQueue;
            private Scheduler.Scheduler m_scheduler;
            private IPipExecutionEnvironment m_environment;
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

                m_pipTable = schedule.PipTable;
                m_pipQueue = schedule.SchedulingQueue;
                m_scheduler = schedule.Scheduler;
                m_operationTracker = m_scheduler.OperationTracker;
                m_environment = m_scheduler;
                m_environment.ContentFingerprinter.FingerprintSalt = buildStartData.FingerprintSalt;
                m_environment.State.PipEnvironment.OrchestratorEnvironmentVariables = buildStartData.EnvironmentVariables;
                GetPipSpecificPropertiesConfigObject(buildStartData.PipSpecificPropertiesAndValues);
            }

            /// <inheritdoc/>
            public uint WorkerId => m_workerService.WorkerId;

            /// <inheritdoc/>
            void IWorkerPipExecutionService.WhenDone() => m_pipQueue.SetAsFinalized();

            /// <summary>
            /// Refresh the mappings of pipSpecificPropertiesAndValues on the workers based on the buildStartData from the orchestrator
            /// </summary>
            public void GetPipSpecificPropertiesConfigObject(IEnumerable<PipSpecificPropertyAndValue> pipPropertiesAndValuesFromOrchestrator)
            {
                m_environment.PipSpecificPropertiesConfig = new Utilities.PipSpecificPropertiesConfig(pipPropertiesAndValuesFromOrchestrator.Select(pipSpecificPropertyAndValueEntry => new Utilities.PipSpecificPropertyAndValue(
                                                                                                                                            (PipSpecificProperty)(pipSpecificPropertyAndValueEntry.PipSpecificProperty),
                                                                                                                                            pipSpecificPropertyAndValueEntry.PipSemiStableHash,
                                                                                                                                            pipSpecificPropertyAndValueEntry.PropertyValue)).ToList().AsReadOnlyList());
            }

            /// <inheritdoc/>
            AttachCompletionInfo IWorkerPipExecutionService.ConstructAttachCompletionInfo() 
                => new AttachCompletionInfo
                    {
                        WorkerId = WorkerId,
                        MaxProcesses = Config.Schedule.MaxProcesses,
                        MaxMaterialize = Config.Schedule.MaxMaterialize,
                        MaxCacheLookup = Config.Schedule.MaxCacheLookup,
                        MaxLightProcesses = Config.Schedule.MaxLight,
                        AvailableRamMb = m_scheduler.LocalWorker.TotalRamMb ?? 0,
                        AvailableCommitMb = m_scheduler.LocalWorker.TotalCommitMb ?? 0,
                    };

            /// <inheritdoc/>
            async Task IWorkerPipExecutionService.StartPipStepAsync(PipId pipId, ExtendedPipCompletionData pipCompletionData, SinglePipBuildRequest pipBuildRequest, Possible<Unit> reportInputsResult)
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

                try
                {
                    using (var operationContext = m_operationTracker.StartOperation(PipExecutorCounter.WorkerServiceHandlePipStepDuration, pipId, pipType, LoggingContext))
                    using (operationContext.StartOperation(step))
                    {
                        if (step == PipExecutionStep.MaterializeOutputs && Config.Distribution.FireForgetMaterializeOutput())
                        {
                            // We do not report 'MaterializeOutput' step results back to orchestrator
                            // so the notification manager is not made aware of the pip being processed.
                        }
                        else
                        {
                            m_workerService.m_notificationManager.MarkPipProcessingStarted(pip.SemiStableHash);
                        }

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

                        m_scheduler.StartPipStep(pipId, m_workerRunnablePipObserver, step, pipBuildRequest.Priority);
                        var removed = PendingScheduleRequests.TryRemove((pipId, step), out _);
                        Contract.Assert(removed, $"This pip step ({pip.FormattedSemiStableHash} : {step}) should be marked as pending scheduling");
                    }
                }
                catch (Exception ex)
                {
                    Scheduler.Tracing.Logger.Log.HandlePipStepOnWorkerFailed(
                                   LoggingContext,
                                   pip.GetDescription(m_environment.Context),
                                   ex.ToString());

                    // HandlePipStep might throw an exception before we send the pipbuildrequest to Scheduler. 
                    // In that case, we will fail to report the result to the orchestrator. That's why, in the case
                    // of an exception, we send the result to the orchestrator to avoid infinite waiting over there. 
                    m_workerService.ReportResult(
                        pip.PipId,
                        ExecutionResult.GetFailureNotRunResult(LoggingContext),
                        step);
                }
            }

            /// <inheritdoc />
            Possible<Unit> IWorkerPipExecutionService.TryReportInputs(IEnumerable<FileArtifactKeyedHash> hashes)
            {
                var dynamicDirectoryMap = new Dictionary<DirectoryArtifact, List<FileArtifactWithAttributes>>();
                var failedFiles = new List<(FileArtifact file, ContentHash hash)>();
                var fileContentManager = m_environment.State.FileContentManager;
                var lockObj = new object();

                // Concurrently report inputs
                Parallel.ForEach(hashes, (fileArtifactKeyedHash) =>
                {
                    if (fileArtifactKeyedHash.PathValue == AbsolutePath.Invalid.RawValue)
                    {
                        // All workers have the same entries in the path table up to the schedule phase. Dynamic outputs add entries to the path table of the worker that generates them.
                        // Dynamic outputs can be generated by one worker, but consumed by another, which potentially is not the orchestrator. Thus, the two workers do not have the same entries
                        // in the path table.
                        // The approach we have here may be sub-optimal(the worker may have had the paths already). But it ensures correctness.
                        // Need to create absolute path because the path is potentially not in the path table.

                        fileArtifactKeyedHash.PathValue = AbsolutePath.Create(m_environment.Context.PathTable, fileArtifactKeyedHash.PathString).RawValue;
                    }

                    var file = fileArtifactKeyedHash.GetFileArtifact();

                    if (fileArtifactKeyedHash.IsSourceAffected)
                    {
                        fileContentManager.SourceChangeAffectedInputs.ReportSourceChangedAffectedFile(file.Path);
                    }

                    AbsolutePath outputDirectoryRoot = AbsolutePath.Invalid;
                    // Let's try to identify whether this file artifact is a dynamic one. Output directories only allow rewrite count 1, therefore there can be at most one
                    if (fileArtifactKeyedHash.AssociatedDirectories?.Count == 1)
                    {
                        var associatedDirectory = fileArtifactKeyedHash.AssociatedDirectories[0];
                        var directoryArtifact = new DirectoryArtifact(
                                new AbsolutePath(associatedDirectory.DirectoryPathValue),
                                associatedDirectory.DirectorySealId,
                                associatedDirectory.IsDirectorySharedOpaque);
                        
                        if (m_environment.GetSealDirectoryKind(directoryArtifact).IsDynamicKind())
                        {
                            outputDirectoryRoot = directoryArtifact.Path;
                        }
                    }
                    
                    var materializationInfo = fileArtifactKeyedHash.GetFileMaterializationInfo(m_environment.Context.PathTable, outputDirectoryRoot);
                    if (!fileContentManager.ReportWorkerPipInputContent(
                        LoggingContext,
                        file,
                        materializationInfo))
                    {
                        lock (lockObj)
                        {
                            failedFiles.Add((file, materializationInfo.Hash));
                        }
                    }
                });

                // Populate dynamicDirectoryMap
                foreach (FileArtifactKeyedHash fileArtifactKeyedHash in hashes)
                {
                    if (fileArtifactKeyedHash.AssociatedDirectories?.Count > 0)
                    {
                        var fileWithAttributes = FileArtifactWithAttributes.Create(
                            fileArtifactKeyedHash.GetFileArtifact(),
                            FileExistence.Required,
                            isUndeclaredFileRewrite: fileArtifactKeyedHash.IsAllowedFileRewrite);

                        foreach (var directoryArtifact in fileArtifactKeyedHash.AssociatedDirectories)
                        {
                            var directory = new DirectoryArtifact(
                                new AbsolutePath(directoryArtifact.DirectoryPathValue),
                                directoryArtifact.DirectorySealId,
                                directoryArtifact.IsDirectorySharedOpaque);

                            if (!dynamicDirectoryMap.TryGetValue(directory, out var files))
                            {
                                files = new List<FileArtifactWithAttributes>();
                                dynamicDirectoryMap.Add(directory, files);
                            }

                            files.Add(fileWithAttributes);
                        }
                    }
                }

                // Report directory contents from dynamicDirectoryMap
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

            private void StartStep(RunnablePip runnablePip)
            {
                var pipId = runnablePip.PipId;
                var processRunnable = runnablePip as ProcessRunnablePip;

                var pipIdStepTuple = (pipId, runnablePip.Step);

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
                            // We don't know the miss reason here, but we don't really need it, so set it to Invalid.
                            var fingerprint = new BuildXL.Cache.MemoizationStore.Interfaces.Sessions.Fingerprint(new ReadOnlyFixedBytes(pipBuildRequest.Fingerprint.Span), pipBuildRequest.Fingerprint.Length);
                            processRunnable.SetCacheResult(RunnableFromCacheResult.CreateForMiss(new WeakContentFingerprint(fingerprint), PipCacheMissType.Invalid));

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
                runnablePip.ReleaseDispatcher();

                var pipId = runnablePip.PipId;
                var loggingContext = runnablePip.LoggingContext;
                var pip = runnablePip.Pip;
                var description = runnablePip.Description;
                var executionResult = runnablePip.ExecutionResult;

                var completionData = PendingPipCompletions[(pipId, runnablePip.Step)];
                completionData.SerializedData.ExecuteStepTicks = runnablePip.StepDuration.Ticks;
                completionData.SerializedData.ThreadId = runnablePip.ThreadId;
                completionData.SerializedData.StartTimeTicks = runnablePip.StepStartTime.Ticks;
                completionData.SerializedData.QueueTicks = runnablePip.Performance.QueueDurations.Values.FirstOrDefault().Ticks;

                switch (runnablePip.Step)
                {
                    case PipExecutionStep.ExecuteProcess:
                    case PipExecutionStep.ExecuteNonProcessPip:
                        executionResult.Seal();

                        if (!executionResult.Result.IndicatesFailure() &&
                            ETWLogger.Log.IsEnabled(EventLevel.Verbose, Keywords.Diagnostics))
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

                m_workerService.ReportResult(
                        runnablePip.PipId,
                        executionResult,
                        runnablePip.Step);
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
        }
    }
}
