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
using BuildXL.Engine.Distribution.Grpc;
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
using static BuildXL.Engine.Distribution.Grpc.ClientConnectionManager;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Engine.Distribution
{
    /// <summary>
    /// Defines service run on worker nodes in a distributed build.
    /// </summary>
    public sealed partial class WorkerService : IDistributionService
    {
        /// <summary>
        /// Gets the build start data from the coordinator passed after the attach operation
        /// </summary>
        public BuildStartData BuildStartData { get; private set; }

        /// <summary>
        /// Returns a task representing the completion of the exit operation
        /// </summary>
        private Task<bool> ExitCompletion => m_exitCompletionSource.Task;

        /// <summary>
        /// Returns a task representing the completion of the attach operation
        /// </summary>
        private Task<bool> AttachCompletion => m_attachCompletionSource.Task;

        // The timer interval and the total inactivity after which we shut down the worker.
        // These paraneters are made internal to allow tests speed up the checks.
        private readonly TaskSourceSlim<bool> m_exitCompletionSource;
        private readonly TaskSourceSlim<bool> m_attachCompletionSource;

        private readonly ConcurrentDictionary<(PipId, PipExecutionStep), SinglePipBuildRequest> m_pendingBuildRequests =
            new ConcurrentDictionary<(PipId, PipExecutionStep), SinglePipBuildRequest>();

        private readonly ConcurrentDictionary<(PipId,PipExecutionStep), ExtendedPipCompletionData> m_pendingPipCompletions =
            new ConcurrentDictionary<(PipId, PipExecutionStep), ExtendedPipCompletionData>();

        private readonly ConcurrentBigSet<int> m_handledBuildRequests = new ConcurrentBigSet<int>();

        private Scheduler.Tracing.OperationTracker m_operationTracker;

        /// <summary>
        /// Identifies the worker
        /// </summary>
        public uint WorkerId { get; private set; }

        private volatile bool m_hasFailures = false;
        private volatile string m_masterFailureMessage;

        /// <summary>
        /// Whether master is done with the worker by sending a message to worker.
        /// </summary>
        private volatile bool m_isMasterExited;

        private LoggingContext m_appLoggingContext;
        private WorkerNotificationManager m_notificationManager;
        private PipTable m_pipTable;
        private PipQueue m_pipQueue;
        private Scheduler.Scheduler m_scheduler;
        private IPipExecutionEnvironment m_environment;
        private WorkerServicePipStateManager m_workerPipStateManager;
        private readonly IConfiguration m_config;
        private readonly ushort m_port;
        private readonly WorkerRunnablePipObserver m_workerRunnablePipObserver;
        private readonly DistributionServices m_services;

        private IMasterClient m_masterClient;
        private readonly IServer m_workerServer;

        /// <summary>
        /// Class constructor
        /// </summary>
        /// <param name="appLoggingContext">Application-level logging context</param>
        /// <param name="config">Build config</param>\
        /// <param name="buildId">the build id</param>
        public WorkerService(LoggingContext appLoggingContext, IConfiguration config, string buildId)
        {
            m_appLoggingContext = appLoggingContext;
            m_config = config;
            m_port = config.Distribution.BuildServicePort;
            m_services = new DistributionServices(buildId);
            m_workerServer = new Grpc.GrpcWorkerServer(this, appLoggingContext, buildId);

            m_attachCompletionSource = TaskSourceSlim.Create<bool>();
            m_exitCompletionSource = TaskSourceSlim.Create<bool>();
            m_workerRunnablePipObserver = new WorkerRunnablePipObserver(this);
        }

        internal void Start(EngineSchedule schedule)
        {
            Contract.Requires(schedule != null);
            Contract.Requires(schedule.Scheduler != null);
            Contract.Requires(schedule.SchedulingQueue != null);
            Contract.Assert(AttachCompletion.IsCompleted && AttachCompletion.GetAwaiter().GetResult(), "ProcessBuildRequests called before finishing attach on worker");

            m_workerPipStateManager = new WorkerServicePipStateManager(this);
            m_pipTable = schedule.PipTable;
            m_pipQueue = schedule.SchedulingQueue;
            m_scheduler = schedule.Scheduler;
            m_operationTracker = m_scheduler.OperationTracker;
            m_environment = m_scheduler;
            m_environment.ContentFingerprinter.FingerprintSalt = BuildStartData.FingerprintSalt;
            m_environment.State.PipEnvironment.MasterEnvironmentVariables = BuildStartData.EnvironmentVariables;
            m_notificationManager = new WorkerNotificationManager(this, schedule, m_environment, m_services);
        }

        /// <summary>
        /// Connects to the master and enables this node to receive build requests
        /// </summary>
        internal async Task<bool> WhenDoneAsync()
        {
            Contract.Assert(AttachCompletion.IsCompleted && AttachCompletion.GetAwaiter().GetResult(), "ProcessBuildRequests called before finishing attach on worker");

            bool success = await SendAttachCompletedAfterProcessBuildRequestStartedAsync();

            // Wait until the build finishes or we discovered that the master is dead
            success &= await ExitCompletion;

            success &= !m_hasFailures;
            if (m_masterFailureMessage != null)
            {
                Logger.Log.DistributionWorkerExitFailure(m_appLoggingContext, m_masterFailureMessage);
            }

            m_pipQueue.SetAsFinalized();

            return success;
        }


        internal void ReportingPipToMaster(ExtendedPipCompletionData data) => m_workerPipStateManager.Transition(data.PipId, WorkerPipState.Reporting);

        internal void PipReportedToMaster(ExtendedPipCompletionData result)
        {
            m_workerPipStateManager.Transition(result.PipId, WorkerPipState.Reported);
            Tracing.Logger.Log.DistributionWorkerFinishedPipRequest(m_appLoggingContext, result.SemiStableHash, ((PipExecutionStep)result.SerializedData.Step).ToString());
        }

        /// <summary>
        /// Waits for the master to attach synchronously
        /// </summary>
        internal bool WaitForMasterAttach()
        {
            Logger.Log.DistributionWaitingForMasterAttached(m_appLoggingContext);

            var timeout = GrpcSettings.WorkerAttachTimeout;
            if (!AttachCompletion.Wait(timeout))
            {
                Exit(failure: "Timed out waiting for attach request from master", isUnexpected: true);
                Logger.Log.DistributionWorkerTimeoutFailure(m_appLoggingContext);
                return false;
            }

            if (!AttachCompletion.Result)
            {
                Logger.Log.DistributionInactiveMaster(m_appLoggingContext, (int)timeout.TotalMinutes);
                return false;
            }

            return true;
        }

        /// <nodoc/>
        public void ExitCallReceivedFromMaster()
        {
            m_isMasterExited = true;
            Logger.Log.DistributionExitReceived(m_appLoggingContext);
        }


        /// <nodoc/>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("AsyncUsage", "AsyncFixer03:FireForgetAsyncVoid")]
        public async void ExitAsync(string failure, bool isUnexpected = false)
        {
            await Task.Yield();
            Exit(failure, isUnexpected);
        }

        /// <nodoc/>
        public void Exit(string failure, bool isUnexpected = false)
        {
            // Can be null if the worker failed to attach to orchestrator
            m_notificationManager?.Exit();
            m_masterClient?.CloseAsync().GetAwaiter().GetResult();

            m_attachCompletionSource.TrySetResult(false);
            bool reportSuccess = string.IsNullOrEmpty(failure);

            if (!reportSuccess)
            {
                m_masterFailureMessage = failure;
                m_hasFailures = true;
            }

            if (isUnexpected && m_isMasterExited)
            {
                // If the worker unexpectedly exits the build after orchestrator exits the build, 
                // we should log a message to keep track of the frequency.
                Logger.Log.DistributionWorkerUnexpectedFailureAfterMasterExits(m_appLoggingContext);
            }

            // Request server shut down before exiting, so the orchestrator stops requests our way
            // gRPC ensures that any pending calls will still be served as part of the shutdown process,
            // so there is no problem if we're executing this as part of an "exit" RPC (which is the normal way of exiting)
            // Do not await, as this call to Exit may have been made as part of the exit RPC and we need to finish it.
            m_workerServer.ShutdownAsync().Forget();
            m_exitCompletionSource.TrySetResult(reportSuccess);
        }

        /// <summary>
        /// Retrieves the cache descriptor containing the build scheduler state from the coordinator node
        /// </summary>
        /// <returns>true if the operation was successful</returns>
        internal bool TryGetBuildScheduleDescriptor(out PipGraphCacheDescriptor descriptor)
        {
            descriptor = null;
            if (!AttachCompletion.Result)
            {
                return false;
            }

            descriptor = BuildStartData.CachedGraphDescriptor;
            return true;
        }

        internal void AttachCore(BuildStartData buildStartData, string masterName)
        {
            Logger.Log.DistributionAttachReceived(m_appLoggingContext, buildStartData.SessionId, masterName);
            BuildStartData = buildStartData;

            // The app-level logging context has a wrong session id. Fix it now that we know the right one.
            m_appLoggingContext = new LoggingContext(
                m_appLoggingContext.ActivityId,
                m_appLoggingContext.LoggerComponentInfo,
                new LoggingContext.SessionInfo(buildStartData.SessionId, m_appLoggingContext.Session.Environment, m_appLoggingContext.Session.RelatedActivityId),
                m_appLoggingContext);

            m_masterClient = new Grpc.GrpcMasterClient(m_appLoggingContext, m_services.BuildId, buildStartData.MasterLocation.IpAddress, buildStartData.MasterLocation.Port, OnConnectionTimeOutAsync);

            WorkerId = BuildStartData.WorkerId;

            m_attachCompletionSource.TrySetResult(true);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("AsyncUsage", "AsyncFixer02:MissingAsyncOpportunity")]
        private async Task<bool> SendAttachCompletedAfterProcessBuildRequestStartedAsync()
        {
            var cacheValidationContent = Guid.NewGuid().ToByteArray();
            var cacheValidationContentHash = ContentHashingUtilities.HashBytes(cacheValidationContent);

            var possiblyStored = await m_environment.Cache.ArtifactContentCache.TryStoreAsync(
                new MemoryStream(cacheValidationContent),
                cacheValidationContentHash);

            if (!possiblyStored.Succeeded)
            {
                Logger.Log.DistributionFailedToStoreValidationContentToWorkerCacheWithException(
                    m_appLoggingContext,
                    cacheValidationContentHash.ToHex(),
                    possiblyStored.Failure.DescribeIncludingInnerFailures());

                Exit("Failed to validate retrieve content from master via cache", isUnexpected: true);
                return false;
            }

            var attachCompletionInfo = new AttachCompletionInfo
            {
                WorkerId = WorkerId,
                MaxProcesses = m_config.Schedule.MaxProcesses,
                MaxMaterialize = m_config.Schedule.MaxMaterialize,
                AvailableRamMb = m_scheduler.LocalWorker.TotalRamMb,
                AvailableCommitMb = m_scheduler.LocalWorker.TotalCommitMb,
                WorkerCacheValidationContentHash = cacheValidationContentHash.ToBondContentHash(),
            };

            Contract.Assert(attachCompletionInfo.WorkerCacheValidationContentHash != null, "worker cache validation content hash is null");

            var attachCompletionResult = await m_masterClient.AttachCompletedAsync(attachCompletionInfo);

            if (!attachCompletionResult.Succeeded)
            {
                Exit($"Failed to attach to master. Duration: {(int)attachCompletionResult.Duration.TotalMinutes}", isUnexpected: true);
                return true;
            }
            else
            {
                m_notificationManager.Start(m_masterClient);
            }

            return true;
        }

        private async void OnConnectionTimeOutAsync(object sender, ConnectionTimeoutEventArgs e)
        {
            Logger.Log.DistributionConnectionTimeout(m_appLoggingContext, e?.Details ?? "");

            // Stop sending messages
            m_notificationManager.Cancel();
           
            // Unblock caller to make it a fire&forget event handler.
            await Task.Yield();
            Logger.Log.DistributionInactiveMaster(m_appLoggingContext, (int)(GrpcSettings.CallTimeout.TotalMinutes * GrpcSettings.MaxRetry));
            ExitAsync("Connection timed out", isUnexpected: true);
        }

        internal void ExecutePipsCore(PipBuildRequest request)
        {
            var reportInputsResult = TryReportInputs(request.Hashes);

            for (int i = 0; i < request.Pips.Count; i++)
            {
                SinglePipBuildRequest pipBuildRequest = request.Pips[i];

                // Start the pip. Handle the case of a retry - the pip may be already started by a previous call.
                if (m_handledBuildRequests.Add(pipBuildRequest.SequenceNumber))
                {

                    var pipId = new PipId(pipBuildRequest.PipIdValue);
                    var pip = m_pipTable.HydratePip(pipId, PipQueryContext.HandlePipStepOnWorker);
                    var pipIdStepTuple = (pipId, (PipExecutionStep)pipBuildRequest.Step);
                    m_pendingBuildRequests[pipIdStepTuple] = pipBuildRequest;
                    var pipCompletionData = new ExtendedPipCompletionData(new PipCompletionData() { PipIdValue = pipId.Value, Step = pipBuildRequest.Step })
                    {
                        SemiStableHash = m_pipTable.GetPipSemiStableHash(pipId)
                    };

                    m_pendingPipCompletions[pipIdStepTuple] = pipCompletionData;

                    HandlePipStepAsync(pip, pipCompletionData, pipBuildRequest, reportInputsResult).Forget((ex)=>
                    {
                        Scheduler.Tracing.Logger.Log.HandlePipStepOnWorkerFailed(
                            m_appLoggingContext,
                            pip.GetDescription(m_environment.Context),
                            ex.ToString());

                        // HandlePipStep might throw an exception after we remove pipCompletionData from m_pendingPipCompletions.
                        // That's why, we check whether the pipCompletionData still exists there.
                        if (m_pendingPipCompletions.ContainsKey(pipIdStepTuple))
                        {
                            ReportResult(
                                pip,
                                ExecutionResult.GetFailureNotRunResult(m_appLoggingContext),
                                (PipExecutionStep)pipBuildRequest.Step);
                        }
                    });
                }
            }
        }

        private async Task HandlePipStepAsync(Pip pip, ExtendedPipCompletionData pipCompletionData, SinglePipBuildRequest pipBuildRequest, Possible<Unit> reportInputsResult)
        {
            // Do not block the caller.
            await Task.Yield();

            var pipId = pip.PipId;
            var pipType = pip.PipType;
            var step = (PipExecutionStep)pipBuildRequest.Step;
            if (!(pipType == PipType.Process || pipType == PipType.Ipc || step == PipExecutionStep.MaterializeOutputs))
            {
                throw Contract.AssertFailure(I($"Workers can only execute process or IPC pips for steps other than MaterializeOutputs: Step={step}, PipId={pipId}, Pip={pip.GetDescription(m_environment.Context)}"));
            }

            using (var operationContext = m_operationTracker.StartOperation(PipExecutorCounter.WorkerServiceHandlePipStepDuration, pipId, pipType, m_appLoggingContext))
            using (operationContext.StartOperation(step))
            {
                var pipInfo = new PipInfo(pip, m_environment.Context);

                if (!reportInputsResult.Succeeded)
                {
                    // Could not report inputs due to input mismatch. Fail the pip
                    Scheduler.Tracing.Logger.Log.PipMaterializeDependenciesFailureDueToVerifySourceFilesFailed(
                        m_appLoggingContext,
                        pipInfo.Description,
                        reportInputsResult.Failure.DescribeIncludingInnerFailures());

                    ReportResult(
                        pip,
                        ExecutionResult.GetFailureNotRunResult(m_appLoggingContext),
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

                ReportResult(
                    pip,
                    executionResult,
                    step);
            }
        }

        private Guid GetActivityId(RunnablePip runnablePip)
        {
            return new Guid(m_pendingBuildRequests[(runnablePip.PipId, runnablePip.Step)].ActivityId);
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
            var completionData = m_pendingPipCompletions[pipIdStepTuple];
            completionData.StepExecutionStarted.SetResult(true);

            switch (runnablePip.Step)
            {
                case PipExecutionStep.ExecuteProcess:
                    if (runnablePip.PipType == PipType.Process)
                    {
                        SinglePipBuildRequest pipBuildRequest;
                        bool found = m_pendingBuildRequests.TryGetValue(pipIdStepTuple, out pipBuildRequest);
                        Contract.Assert(found, "Could not find corresponding build request for executed pip on worker");
                        m_pendingBuildRequests[pipIdStepTuple] = null;

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


            var completionData = m_pendingPipCompletions[(pipId, runnablePip.Step)];
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

        private void ReportResult(
           Pip pip,
           ExecutionResult executionResult,
           PipExecutionStep step)
        {
            var pipId = pip.PipId;
            m_workerPipStateManager.Transition(pipId, WorkerPipState.Recording);

            if (executionResult.Result == PipResultStatus.Failed)
            {
                m_hasFailures = true;
            }

            bool found = m_pendingPipCompletions.TryRemove((pipId, step), out var pipCompletion);
            Contract.Assert(found, "Could not find corresponding build completion data for executed pip on worker");

            pipCompletion.ExecutionResult = executionResult;
            // To preserve the path set casing is an option only available for process pips
            pipCompletion.PreservePathSetCasing = pip.PipType == PipType.Process ? ((Process)pip).PreservePathSetCasing : false;

            if (step == PipExecutionStep.MaterializeOutputs && m_config.Distribution.FireForgetMaterializeOutput)
            {
                // We do not report 'MaterializeOutput' step results back to master.
                Logger.Log.DistributionWorkerFinishedPipRequest(m_appLoggingContext, pipCompletion.SemiStableHash, step.ToString());
                return;
            }

            m_notificationManager.ReportResult(pipCompletion);
        }

        private Possible<Unit> TryReportInputs(List<FileArtifactKeyedHash> hashes)
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
                    // Dynamic outputs can be generated by one worker, but consumed by another, which potentially is not the master. Thus, the two workers do not have the same entries
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
                    m_appLoggingContext,
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

        /// <inheritdoc/>
        public void Dispose()
        {
            m_services.LogStatistics(m_appLoggingContext);

            m_workerPipStateManager?.Dispose();

            m_workerServer.Dispose();
        }

        bool IDistributionService.Initialize()
        {
            // Start listening to the port if we have remote workers
            try
            {
                m_workerServer.Start(m_port);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Log.DistributionServiceInitializationError(m_appLoggingContext, DistributedBuildRole.Worker.ToString(), m_port, ExceptionUtilities.GetLogEventMessage(ex));
                return false;
            }
        }

        private sealed class WorkerRunnablePipObserver : RunnablePipObserver
        {
            private readonly WorkerService m_workerService;
            private readonly ConcurrentDictionary<PipId, ExecutionResult> m_processExecutionResult
                 = new ConcurrentDictionary<PipId, ExecutionResult>();

            public WorkerRunnablePipObserver(WorkerService workerService)
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
                    // For successful/unsuccessful results of ExecuteProcess, store so that when master calls worker for
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
            private readonly WorkerService m_workerService;

            public WorkerServicePipStateManager(WorkerService workerService)
            {
                m_workerService = workerService;
                m_statusReporter = new StatusReporter(m_workerService.m_appLoggingContext, this);
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
                            pipsReported: pipStateSnapshot[WorkerPipState.Reported]);
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
