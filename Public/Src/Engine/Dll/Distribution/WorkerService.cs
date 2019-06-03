// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
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

#if FEATURE_MICROSOFT_DIAGNOSTICS_TRACING
using Microsoft.Diagnostics.Tracing;
#else
using System.Diagnostics.Tracing;
# endif

namespace BuildXL.Engine.Distribution
{
    /// <summary>
    /// Defines service run on worker nodes in a distributed build.
    /// </summary>
    /// <remarks>
    /// There are 2 timers to make sure both master and worker are alive:
    /// - The master timer sends heartbeat calls. The worker notes that the master is alive. It also
    /// retries all previously failed calls back to the master.
    /// - The service timer checks for a dead master. If it discovers that the master hasn't called
    /// for EngineEnvironmentSettings.DistributionInactiveTimeout (or WorkerAttachTimeout if Attach is still not called),
    /// the service will shut down.
    /// </remarks>
    public sealed partial class WorkerService : IDistributionService
    {
#region Writer Pool

        private readonly ObjectPool<BuildXLWriter> m_writerPool = new ObjectPool<BuildXLWriter>(CreateWriter, (Action<BuildXLWriter>)CleanupWriter);

        private static void CleanupWriter(BuildXLWriter writer)
        {
            writer.BaseStream.SetLength(0);
        }

        [SuppressMessage("Microsoft.Reliability", "CA2000:DisposeObjectsBeforeLosingScope", Justification = "Disposal is not needed for memory stream")]
        private static BuildXLWriter CreateWriter()
        {
            return new BuildXLWriter(
                debug: false,
                stream: new MemoryStream(),
                leaveOpen: false,
                logStats: false);
        }

#endregion Writer Pool

        private readonly List<ExtendedPipCompletionData> m_executionResultList = new List<ExtendedPipCompletionData>();

        /// <summary>
        /// Gets the build start data from the coordinator passed after the attach operation
        /// </summary>
        public BuildStartData BuildStartData { get; private set; }

        /// <summary>
        /// Returns a task representing the completion of the exit operation
        /// </summary>
        public Task<bool> ExitCompletion => m_exitCompletionSource.Task;

        /// <summary>
        /// Returns a task representing the completion of the attach operation
        /// </summary>
        private Task<bool> AttachCompletion => m_attachCompletionSource.Task;

        // The timer interval and the total inactivity after which we shut down the worker.
        // These paraneters are made internal to allow tests speed up the checks.
        private readonly TaskSourceSlim<bool> m_exitCompletionSource;
        private readonly TaskSourceSlim<bool> m_attachCompletionSource;

        private readonly ConcurrentDictionary<PipId, SinglePipBuildRequest> m_pendingBuildRequests =
            new ConcurrentDictionary<PipId, SinglePipBuildRequest>();

        private readonly ConcurrentDictionary<PipId, ExtendedPipCompletionData> m_pendingPipCompletions =
            new ConcurrentDictionary<PipId, ExtendedPipCompletionData>();

        private readonly ConcurrentBigSet<int> m_handledBuildRequests = new ConcurrentBigSet<int>();

        private TimeSpan m_lastHeartbeatTimestamp = TimeSpan.Zero;
        private Scheduler.Tracing.OperationTracker m_operationTracker;

        /// <summary>
        /// Identifies the worker
        /// </summary>
        public uint WorkerId { get; private set; }

        private bool m_hasFailures = false;

        private LoggingContext m_appLoggingContext;
        private ExecutionResultSerializer m_resultSerializer;
        private PipTable m_pipTable;
        private PipQueue m_pipQueue;
        private Scheduler.Scheduler m_scheduler;
        private IPipExecutionEnvironment m_environment;
        private ForwardingEventListener m_forwardingEventListener;
        private WorkerServicePipStateManager m_workerPipStateManager;
        private readonly ushort m_port;
        private readonly int m_maxProcesses;
        private readonly WorkerRunnablePipObserver m_workerRunnablePipObserver;
        private readonly DistributionServices m_services;
        private NotifyMasterExecutionLogTarget m_notifyMasterExecutionLogTarget;

        private readonly Thread m_sendThread;
        private readonly BlockingCollection<ExtendedPipCompletionData> m_buildResults = new BlockingCollection<ExtendedPipCompletionData> ();
        private readonly int m_maxMessagesPerBatch = EngineEnvironmentSettings.MaxMessagesPerBatch.Value;

        private readonly bool m_isGrpcEnabled;
        private IMasterClient m_masterClient;
        private readonly IServer m_workerServer;

#if !DISABLE_FEATURE_BOND_RPC
        private InternalBond.BondMasterClient m_bondMasterClient;
        private readonly InternalBond.BondWorkerServer m_bondWorkerService;
#endif

        /// <summary>
        /// Class constructor
        /// </summary>
        /// <param name="appLoggingContext">Application-level logging context</param>
        /// <param name="maxProcesses">the maximum number of concurrent pips on the worker</param>
        /// <param name="config">Distribution config</param>\
        /// <param name="buildId">the build id</param>
        public WorkerService(LoggingContext appLoggingContext, int maxProcesses, IDistributionConfiguration config, string buildId)
        {
            m_isGrpcEnabled = config.IsGrpcEnabled;

            m_appLoggingContext = appLoggingContext;
            m_maxProcesses = maxProcesses;
            m_port = config.BuildServicePort;
            m_services = new DistributionServices(buildId);
            if (m_isGrpcEnabled)
            {
                m_workerServer = new Grpc.GrpcWorkerServer(this, appLoggingContext, buildId);
            }
            else
            {
#if !DISABLE_FEATURE_BOND_RPC
                m_bondWorkerService = new InternalBond.BondWorkerServer(appLoggingContext, this, m_port, m_services);
                m_workerServer = m_bondWorkerService;
#endif
            }

            m_attachCompletionSource = TaskSourceSlim.Create<bool>();
            m_exitCompletionSource = TaskSourceSlim.Create<bool>();
            m_workerRunnablePipObserver = new WorkerRunnablePipObserver(this);
            m_sendThread = new Thread(SendBuildResults);
        }

        private void SendBuildResults()
        {
            int numBatchesSent = 0;

            ExtendedPipCompletionData firstItem;
            while (!m_buildResults.IsCompleted)
            {
                try
                {
                    firstItem = m_buildResults.Take();
                }
                catch (InvalidOperationException)
                {
                    // m_buildResults has drained and been completed.
                    break;
                }

                m_executionResultList.Clear();
                m_executionResultList.Add(firstItem);

                while (m_executionResultList.Count < m_maxMessagesPerBatch && m_buildResults.TryTake(out var item))
                {
                    m_executionResultList.Add(item);
                }

                using (m_services.Counters.StartStopwatch(DistributionCounter.WorkerServiceResultSerializationDuration))
                {
                    Parallel.ForEach(m_executionResultList, a => SerializeExecutionResult(a));
                }

                using (m_services.Counters.StartStopwatch(DistributionCounter.ReportPipsCompletedDuration))
                {
                    var callResult = m_masterClient.NotifyAsync(new WorkerNotificationArgs
                    {
                        WorkerId = WorkerId,
                        CompletedPips = m_executionResultList.Select(a => a.SerializedData).ToList()
                    },
                    m_executionResultList.Select(a => a.SemiStableHash).ToList()).GetAwaiter().GetResult();

                    if (callResult.Succeeded)
                    {
                        foreach (var result in m_executionResultList)
                        {
                            m_workerPipStateManager.Transition(result.PipId, WorkerPipState.Reported);
                            Tracing.Logger.Log.DistributionWorkerFinishedPipRequest(m_appLoggingContext, result.SemiStableHash, ((PipExecutionStep)result.SerializedData.Step).ToString());
                        }

                        numBatchesSent++;
                    }
                    else
                    {
                        // Fire-forget exit call with failure.
                        // If we fail to send notification to master, the worker should fail.
                        ExitAsync(timedOut: false, failure: "Notify event failed to send to master");
                        break;
                    }
                }
            }

            m_services.Counters.AddToCounter(DistributionCounter.BuildResultBatchesSentToMaster, numBatchesSent);
        }

        private void SerializeExecutionResult(ExtendedPipCompletionData completionData)
        {
            using (var pooledWriter = m_writerPool.GetInstance())
            {
                var writer = pooledWriter.Instance;
                PipId pipId = completionData.PipId;

                m_resultSerializer.Serialize(writer, completionData.ExecutionResult);

                // TODO: ToArray is expensive here. Think about alternatives.
                var dataByte = ((MemoryStream)writer.BaseStream).ToArray();
                completionData.SerializedData.ResultBlob = new ArraySegment<byte>(dataByte);
                m_workerPipStateManager.Transition(pipId, WorkerPipState.Reporting);
                m_environment.Counters.AddToCounter(m_pipTable.GetPipType(pipId) == PipType.Process ? PipExecutorCounter.ProcessExecutionResultSize : PipExecutorCounter.IpcExecutionResultSize, dataByte.Length);
            }
        }

        /// <summary>
        /// Connects to the master and enables this node to receive build requests
        /// </summary>
        /// <param name="schedule">the engine schedule</param>
        /// <returns>true if the operation was successful</returns>
        internal async Task<bool> ConnectToMasterAsync(EngineSchedule schedule)
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
            m_resultSerializer = new ExecutionResultSerializer(maxSerializableAbsolutePathIndex: schedule.MaxSerializedAbsolutePath, executionContext: m_scheduler.Context);
            m_forwardingEventListener = new ForwardingEventListener(this);

            bool success = await SendAttachCompletedAfterProcessBuildRequestStartedAsync();

            // Wait until the build finishes or we discovered that the master is dead
            success &= await ExitCompletion;

            success &= !m_hasFailures;

            m_pipQueue.SetAsFinalized();

            return success;
        }

        /// <summary>
        /// Waits for the master to attach synchronously
        /// </summary>
        internal bool WaitForMasterAttach()
        {
            Logger.Log.DistributionWaitingForMasterAttached(m_appLoggingContext);

            while (!AttachCompletion.Wait(EngineEnvironmentSettings.WorkerAttachTimeout))
            {
                if ((TimestampUtilities.Timestamp - m_lastHeartbeatTimestamp) > EngineEnvironmentSettings.WorkerAttachTimeout)
                {
                    Exit(timedOut: true, failure: "Timed out waiting for attach request from master");
                    return false;
                }
            }

            if (!AttachCompletion.Result)
            {
                Logger.Log.DistributionInactiveMaster(m_appLoggingContext, (int)EngineEnvironmentSettings.WorkerAttachTimeout.Value.TotalMinutes);
                return false;
            }

            return true;
        }

        /// <nodoc/>
        public void BeforeExit()
        {
            Logger.Log.DistributionExitReceived(m_appLoggingContext);

            // Dispose the notify master execution log target to ensure all message are sent to master.
            m_notifyMasterExecutionLogTarget?.Dispose();
        }


        /// <nodoc/>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("AsyncUsage", "AsyncFixer03:FireForgetAsyncVoid")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("AsyncUsage", "AsyncFixer02:MissingAsyncOpportunity")]
        public async void ExitAsync(bool timedOut, string failure)
        {
            await Task.Yield();
            Exit(timedOut, failure);
        }

        /// <nodoc/>
        public void Exit(bool timedOut, string failure)
        {
            Analysis.IgnoreArgument(timedOut);

            m_buildResults.CompleteAdding();
            if (m_sendThread.IsAlive)
            {
                m_sendThread.Join();
            }

            // The execution log target can be null if the worker failed to attach to master
            if (m_notifyMasterExecutionLogTarget != null)
            {
                // Remove the notify master target to ensure no further events are sent to it
                m_scheduler.RemoveExecutionLogTarget(m_notifyMasterExecutionLogTarget);
                // Dispose the execution log target to ensure all events are flushed and sent to master
                m_notifyMasterExecutionLogTarget.Dispose();
            }

            m_masterClient?.CloseAsync().GetAwaiter().GetResult();

            m_attachCompletionSource.TrySetResult(false);
            bool reportSuccess = string.IsNullOrEmpty(failure);

            if (!reportSuccess)
            {
                // Only log the error, if this thread set exit response
                Logger.Log.DistributionWorkerExitFailure(m_appLoggingContext, failure);
                m_hasFailures = true;
            }

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


            if (m_isGrpcEnabled)
            {
                m_masterClient = new Grpc.GrpcMasterClient(m_appLoggingContext, m_services.BuildId, buildStartData.MasterLocation.IpAddress, buildStartData.MasterLocation.Port, OnConnectionTimeOutAsync);
            }
            else
            {
#if !DISABLE_FEATURE_BOND_RPC
                m_bondWorkerService.UpdateLoggingContext(m_appLoggingContext);
                m_bondMasterClient = new InternalBond.BondMasterClient(m_appLoggingContext, buildStartData.MasterLocation.IpAddress, buildStartData.MasterLocation.Port);
                m_masterClient = m_bondMasterClient;
#endif
            }

            WorkerId = BuildStartData.WorkerId;

            m_attachCompletionSource.TrySetResult(true);
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("AsyncUsage", "AsyncFixer02:MissingAsyncOpportunity")]
        private async Task<bool> SendAttachCompletedAfterProcessBuildRequestStartedAsync()
        {
            if (!m_isGrpcEnabled)
            {
#if !DISABLE_FEATURE_BOND_RPC
                m_bondMasterClient.Start(m_services, OnConnectionTimeOutAsync);
#endif
            }

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

                Exit(timedOut: true, "Failed to validate retrieve content from master via cache");
                return false;
            }

            var attachCompletionInfo = new AttachCompletionInfo
            {
                WorkerId = WorkerId,
                MaxConcurrency = m_maxProcesses,
                AvailableRamMb = m_scheduler.LocalWorker.TotalMemoryMb,
                WorkerCacheValidationContentHash = cacheValidationContentHash.ToBondContentHash(),
            };

            Contract.Assert(attachCompletionInfo.WorkerCacheValidationContentHash != null, "worker cache validation content hash is null");

            var attachCompletionResult = await m_masterClient.AttachCompletedAsync(attachCompletionInfo);

            if (!attachCompletionResult.Succeeded)
            {
                Logger.Log.DistributionInactiveMaster(m_appLoggingContext, (int)attachCompletionResult.Duration.TotalMinutes);
                Exit(timedOut: true, "Failed to attach to master");
                return true;
            }
            else
            {
                m_notifyMasterExecutionLogTarget = new NotifyMasterExecutionLogTarget(WorkerId, m_masterClient, m_environment.Context, m_scheduler.PipGraph.GraphId, m_scheduler.PipGraph.MaxAbsolutePathIndex, m_services);
                m_scheduler.AddExecutionLogTarget(m_notifyMasterExecutionLogTarget);
                m_sendThread.Start();
            }

            return true;
        }

        private async void OnConnectionTimeOutAsync(object sender, EventArgs e)
        {
            // Unblock caller to make it a fire&forget event handler.
            await Task.Yield();
            Logger.Log.DistributionInactiveMaster(m_appLoggingContext, (int)EngineEnvironmentSettings.DistributionInactiveTimeout.Value.TotalMinutes);
            ExitAsync(timedOut: true, "Connection timed out");
        }

        internal void SetLastHeartbeatTimestamp()
        {
            m_lastHeartbeatTimestamp = TimestampUtilities.Timestamp;
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
                    HandlePipStepAsync(pipBuildRequest, reportInputsResult).Forget();
                }
            }
        }

        private async Task HandlePipStepAsync(SinglePipBuildRequest pipBuildRequest, Possible<Unit> reportInputsResult)
        {
            // Do not block the caller.
            await Task.Yield();

            var pipId = new PipId(pipBuildRequest.PipIdValue);
            var pip = m_pipTable.HydratePip(pipId, PipQueryContext.LoggingPipFailedOnWorker);
            var pipType = pip.PipType;
            var step = (PipExecutionStep)pipBuildRequest.Step;
            if (!(pipType == PipType.Process || pipType == PipType.Ipc || step == PipExecutionStep.MaterializeOutputs))
            {
                throw Contract.AssertFailure(I($"Workers can only execute process or IPC pips for steps other than MaterializeOutputs: Step={step}, PipId={pipId}, Pip={pip.GetDescription(m_environment.Context)}"));
            }

            m_pendingBuildRequests[pipId] = pipBuildRequest;
            var pipCompletionData = new ExtendedPipCompletionData(new PipCompletionData() { PipIdValue = pipId.Value, Step = (int)step })
            {
                SemiStableHash = m_pipTable.GetPipSemiStableHash(pipId)
            };

            m_pendingPipCompletions[pipId] = pipCompletionData;

            using (var operationContext = m_operationTracker.StartOperation(PipExecutorCounter.WorkerServiceHandlePipStepDuration, pipId, pipType, m_appLoggingContext))
            using (operationContext.StartOperation(step))
            {
                var pipInfo = new PipInfo(pip, m_environment.Context);

                if (!reportInputsResult.Succeeded)
                {
                    // Could not report inputs due to input mismatch. Fail the pip
                    BuildXL.Scheduler.Tracing.Logger.Log.PipMaterializeDependenciesFailureUnrelatedToCache(
                        m_appLoggingContext,
                        pipInfo.Description,
                        ArtifactMaterializationResult.VerifySourceFilesFailed.ToString(),
                        reportInputsResult.Failure.DescribeIncludingInnerFailures());

                    ReportResult(
                        operationContext,
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
                    operationContext,
                    pip,
                    executionResult,
                    step);
            }
        }

        private Guid GetActivityId(PipId pipId)
        {
            return new Guid(m_pendingBuildRequests[pipId].ActivityId);
        }

        private void StartStep(RunnablePip runnablePip, PipExecutionStep step)
        {
            var pipId = runnablePip.PipId;
            var processRunnable = runnablePip as ProcessRunnablePip;

            Tracing.Logger.Log.DistributionWorkerExecutePipRequest(
                runnablePip.LoggingContext,
                runnablePip.Pip.SemiStableHash,
                runnablePip.Description,
                runnablePip.Step.AsString());

            var completionData = m_pendingPipCompletions[pipId];
            completionData.StepExecutionStarted.SetResult(true);

            switch (step)
            {
                case PipExecutionStep.ExecuteProcess:
                    if (runnablePip.PipType == PipType.Process)
                    {
                        SinglePipBuildRequest pipBuildRequest;
                        bool found = m_pendingBuildRequests.TryGetValue(pipId, out pipBuildRequest);
                        Contract.Assert(found, "Could not find corresponding build request for executed pip on worker");
                        m_pendingBuildRequests[pipId] = null;

                        // Set the cache miss result with fingerprint so ExecuteProcess step can use it
                        var fingerprint = pipBuildRequest.Fingerprint.ToFingerprint();
                        processRunnable.SetCacheResult(RunnableFromCacheResult.CreateForMiss(new WeakContentFingerprint(fingerprint)));

                        processRunnable.ExpectedRamUsageMb = pipBuildRequest.ExpectedRamUsageMb;
                    }

                    break;
            }
        }

        private void EndStep(RunnablePip runnablePip, PipExecutionStep step, TimeSpan duration)
        {
            var pipId = runnablePip.PipId;
            var loggingContext = runnablePip.LoggingContext;
            var pip = runnablePip.Pip;
            var description = runnablePip.Description;
            var executionResult = runnablePip.ExecutionResult;

            var completionData = m_pendingPipCompletions[pipId];
            completionData.SerializedData.ExecuteStepTicks = duration.Ticks;

            switch (step)
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
                        executionResult.WeakFingerprint = cacheResult.WeakFingerprint;

                        if (cacheResult.CanRunFromCache)
                        {
                            var cacheHitData = cacheResult.GetCacheHitData();
                            if (m_environment.State.Cache.IsNewlyAdded(cacheHitData.PathSetHash))
                            {
                                executionResult.PathSet = cacheHitData.PathSet;
                            }

                            executionResult.PipCacheDescriptorV2Metadata = cacheHitData.Metadata;
                            executionResult.TwoPhaseCachingInfo = new TwoPhaseCachingInfo(
                                weakFingerprint: cacheResult.WeakFingerprint,
                                pathSetHash: cacheHitData.PathSetHash,
                                strongFingerprint: cacheHitData.StrongFingerprint,

                                // NOTE: This should not be used so we set it to default values except the metadata hash (it is used for HistoricMetadataCache).
                                cacheEntry: new CacheEntry(cacheHitData.MetadataHash, "unused", ArrayView<ContentHash>.Empty));
                        }
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
           OperationContext operationContext,
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

            bool found = m_pendingPipCompletions.TryRemove(pipId, out var pipCompletion);
            Contract.Assert(found, "Could not find corresponding build completion data for executed pip on worker");

            pipCompletion.ExecutionResult = executionResult;
            m_buildResults.Add(pipCompletion);
        }

        private Possible<Unit> TryReportInputs(List<FileArtifactKeyedHash> hashes)
        {
            var dynamicDirectoryMap = new Dictionary<DirectoryArtifact, List<FileArtifact>>();
            var failedFiles = new List<(FileArtifact file, ContentHash hash)>();
            var fileContentManager = m_environment.State.FileContentManager;

            foreach (FileArtifactKeyedHash fileArtifactKeyedHash in hashes)
            {
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

                    foreach (var bondDirectoryArtifact in fileArtifactKeyedHash.AssociatedDirectories)
                    {
                        var directory = new DirectoryArtifact(
                            new AbsolutePath(bondDirectoryArtifact.DirectoryPathValue),
                            bondDirectoryArtifact.DirectorySealId,
                            bondDirectoryArtifact.IsDirectorySharedOpaque);
                        
                        if (!dynamicDirectoryMap.TryGetValue(directory, out var files))
                        {
                            files = new List<FileArtifact>();
                            dynamicDirectoryMap.Add(directory, files);
                        }

                        files.Add(file);
                    }
                }
                else
                {
                    file = fileArtifactKeyedHash.File;
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

        private async Task SendEventMessagesAsync(List<EventMessage> forwardedEvents)
        {
            if (m_masterClient == null)
            {
                return;
            }

            using (m_services.Counters.StartStopwatch(DistributionCounter.SendEventMessagesDuration))
            {
                await m_masterClient.NotifyAsync(new WorkerNotificationArgs
                {
                    ForwardedEvents = forwardedEvents,
                    WorkerId = WorkerId
                },
                null);
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            m_services.LogStatistics(m_appLoggingContext);

            m_workerPipStateManager?.Dispose();

            m_workerServer.Dispose();
            m_forwardingEventListener?.Dispose();

#if !DISABLE_FEATURE_BOND_RPC
            m_bondMasterClient?.Dispose();
#endif
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

            public override Guid? GetActivityId(PipId pipId)
            {
                return m_workerService.GetActivityId(pipId);
            }

            public override void StartStep(RunnablePip runnablePip, PipExecutionStep step)
            {
                if (step == PipExecutionStep.PostProcess)
                {
                    ExecutionResult executionResult;
                    var removed = m_processExecutionResult.TryRemove(runnablePip.PipId, out executionResult);
                    Contract.Assert(removed, "Execution result must be stored from ExecuteProcess step for PostProcess");
                    runnablePip.SetExecutionResult(executionResult);
                }

                m_workerService.StartStep(runnablePip, step);
            }

            public override void EndStep(RunnablePip runnablePip, PipExecutionStep step, TimeSpan duration)
            {
                if (step == PipExecutionStep.ExecuteProcess)
                {
                    // For successful/unsuccessful results of ExecuteProcess, store so that when master calls worker for
                    // PostProcess it can reuse the result rather than sending it unnecessarily
                    // The unsuccessful results are stored as well to preserve the existing behavior where PostProcess is also done for such results.
                    // TODO: Should we skipped PostProcess when Process failed? In such a case then PipExecutor.ReportExecutionResultOutputContent should not be in PostProcess.
                    m_processExecutionResult[runnablePip.PipId] = runnablePip.ExecutionResult;
                }

                m_workerService.EndStep(runnablePip, step, duration);
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
                    : base(Events.Log, warningMapper: null, eventMask: new EventMask(enabledEvents: new int[] { (int)EventId.PipStatus }, disabledEvents: null))
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
                    if (eventData.EventId == (int)EventId.PipStatus)
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
