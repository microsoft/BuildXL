// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Diagnostics.Tracing;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.UtilitiesCore.Internal;
using BuildXL.Distribution.Grpc;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Engine.Distribution.Grpc;
using BuildXL.Pips;
using BuildXL.Pips.Filter;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.Processes;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Distribution;
using BuildXL.Scheduler.Tracing;
using BuildXL.Storage.Fingerprints;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
using Google.Protobuf;
using static BuildXL.Engine.Distribution.Grpc.ClientConnectionManager;
using static BuildXL.Utilities.FormattableStringEx;
using static BuildXL.Utilities.Tasks.TaskUtilities;
using Logger = BuildXL.Engine.Tracing.Logger;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.Extensions;

namespace BuildXL.Engine.Distribution
{
    /// <summary>
    /// Defines a remote worker capable of executing processes on external machines.
    /// </summary>
    internal sealed class RemoteWorker : RemoteWorkerBase
    {
        private readonly List<PipCompletionTask> m_pipCompletionTaskList = new List<PipCompletionTask>();
        private readonly List<SinglePipBuildRequest> m_buildRequestList = new List<SinglePipBuildRequest>();
        private readonly List<FileArtifactKeyedHash> m_hashList = new List<FileArtifactKeyedHash>();

        private readonly ConcurrentDictionary<PipId, PipCompletionTask> m_pipCompletionTasks = new ConcurrentDictionary<PipId, PipCompletionTask>();
        private readonly LoggingContext m_appLoggingContext;
        private readonly OrchestratorService m_orchestratorService;

        private ServiceLocation m_serviceLocation;
        private BondContentHash m_cacheValidationContentHash;
        private int m_nextSequenceNumber;
        private PipGraph m_pipGraph;
        private CancellationTokenRegistration m_cancellationTokenRegistration;

        /// <summary>
        /// Indicates failure which should cause the worker build to fail. NOTE: This may not correspond to the
        /// entire distributed build failing. Namely, connection failures for operations materialize outputs
        /// will not fail the build but will fail the worker to ensure following steps on workers do not continue.
        /// </summary>
        private string m_exitFailure;

        private volatile bool m_isConnectionLost;

        /// <summary>
        /// Possible scenarios of connection failure
        /// </summary>
        public enum ConnectionFailureType { CallDeadlineExceeded, ReconnectionTimeout, UnrecoverableFailure, AttachmentTimeout, RemotePipTimeout }

        private int m_connectionClosedFlag = 0;     // Used to prevent double shutdowns upon connection failures

        /// <inheritdoc />
        public override Task<bool> SetupCompletionTask => m_setupCompletion.Task;
        private readonly TaskSourceSlim<bool> m_setupCompletion;

        // Before we send the build request to the worker, we need to make sure that the worker is attached.
        // For all steps except materializeoutputs, we already send the build requests to available (running) workers;
        // so waiting for this task might seem redundant. However, for distributed metabuild, we materialize outputs 
        // on all workers and send the materializeoutput request to all workers, even the ones that are not available.
        // That's why, the orchestrator now waits for the workers to be available until it is done executing all pips.
        private Task m_beforeSendingToRemoteTask;

        private readonly TaskSourceSlim<bool> m_attachCompletion;
        private readonly CancellationTokenSource m_exitCancellation = new CancellationTokenSource();

        private readonly Thread m_sendThread;
        private readonly BlockingCollection<ValueTuple<PipCompletionTask, SinglePipBuildRequest>> m_buildRequests;

        private readonly IWorkerClient m_workerClient;
        private DateTime m_initializationTime;
        private Task m_schedulerCompletion;
        private readonly object m_hashListLock = new object();

        private int m_status;
        public override WorkerNodeStatus Status => (WorkerNodeStatus)Volatile.Read(ref m_status);

        private bool m_everAvailable;

        /// <inheritdoc/>
        public override bool EverAvailable => Volatile.Read(ref m_everAvailable);

        /// <inheritdoc/>
        public override int WaitingBuildRequestsCount => m_buildRequests.Count;

        /// <inheritdoc/>
        public override int CurrentBatchSize => m_currentBatchSize;

        public ServiceLocation Location
        {
            get => m_serviceLocation;

            set
            {
                Contract.Assert(m_serviceLocation == null);
                Contract.Assert(value != null);

                WorkerIpAddress = value.IpAddress;
                m_serviceLocation = value;
                m_name = I($"#{WorkerId} ({m_serviceLocation.IpAddress}::{m_serviceLocation.Port})");

                m_workerClient.SetWorkerLocation(m_serviceLocation);
                m_serviceLocationTcs.TrySetResult(Unit.Void);
            }
        } 

        private volatile int m_currentBatchSize;

        private readonly TaskCompletionSource<Unit> m_serviceLocationTcs = new();
        private Task WaitForServiceLocationTask => m_serviceLocationTcs.Task;

        private string m_name;
        
        /// <inheritdoc />
        public override string Name => m_name;

        /// <summary>
        /// Whether the worker is available to acquire work items
        /// </summary>
        public override bool IsAvailable => Status == WorkerNodeStatus.Running && !IsEarlyReleaseInitiated;

        private WorkerExecutionLogReader m_buildManifestReader;
        private WorkerExecutionLogReader m_executionLogReader;

        /// <summary>
        /// Constructor
        /// </summary>
        public RemoteWorker(
            LoggingContext appLoggingContext,
            uint workerId,
            OrchestratorService orchestratorService,
            ServiceLocation serviceLocation,
            PipExecutionContext context)
            : base(workerId, context: context)
        {
            m_appLoggingContext = appLoggingContext;
            m_orchestratorService = orchestratorService;
            m_buildRequests = new BlockingCollection<ValueTuple<PipCompletionTask, SinglePipBuildRequest>>();
            m_attachCompletion = TaskSourceSlim.Create<bool>();
            m_setupCompletion = TaskSourceSlim.Create<bool>();
            m_workerClient = new Grpc.GrpcWorkerClient(
                m_appLoggingContext,
                orchestratorService.InvocationId,
                OnConnectionFailureAsync);

            if (serviceLocation != null)
            {
                // It's importat to use the setter,
                // as it will initialize the name and the worker client too 
                Location = serviceLocation;
            }
            else
            {
                m_name = I($"#{workerId} (uninitialized dynamic worker)");
            }

            // Depending on how long send requests take. It might make sense to use the same thread between all workers. 
            m_sendThread = new Thread(SendBuildRequests);
        }

        public override void InitializeForDistribution(IScheduleConfiguration scheduleConfig, PipGraph pipGraph, IExecutionLogTarget executionLogTarget, TaskSourceSlim<bool> schedulerCompletion)
        {
            m_pipGraph = pipGraph;
            m_buildManifestReader = new WorkerExecutionLogReader(m_appLoggingContext, executionLogTarget, m_orchestratorService.Environment, Name);
            m_executionLogReader = new WorkerExecutionLogReader(m_appLoggingContext, executionLogTarget, m_orchestratorService.Environment, Name);

            TimeSpan? minimumWaitForAttachment = EngineEnvironmentSettings.MinimumWaitForRemoteWorker;
            m_initializationTime = DateTime.UtcNow;

            if (minimumWaitForAttachment.HasValue)
            {
                // If there is a minimum waiting time to wait for attachment, we don't want to signal the scheduler is completed 
                // until this waiting period is over.
                m_schedulerCompletion = Task.WhenAll(Task.Delay(minimumWaitForAttachment.Value), schedulerCompletion.Task);
            }
            else
            {
                m_schedulerCompletion = schedulerCompletion.Task;
            }

#pragma warning disable AsyncFixer05 // Downcasting from a nested task to an outer task. This task is only meant to be awaited so we don't need the result
            m_beforeSendingToRemoteTask = Task.WhenAny(m_attachCompletion.Task, m_schedulerCompletion);
#pragma warning restore AsyncFixer05 // Downcasting from a nested task to an outer task.

            base.InitializeForDistribution(scheduleConfig, pipGraph, executionLogTarget, schedulerCompletion);
        }

        private void SendBuildRequests()
        {
            ValueTuple<PipCompletionTask, SinglePipBuildRequest> firstItem;
            while (!m_buildRequests.IsCompleted)
            {
                try
                {
                    firstItem = m_buildRequests.Take();
                }
                catch (InvalidOperationException)
                {
                    // m_buildRequests has drained and been completed.
                    break;
                }

                using (m_orchestratorService.Environment.Counters.StartStopwatch(PipExecutorCounter.RemoteWorker_PrepareAndSendBuildRequestsDuration))
                {
                    m_pipCompletionTaskList.Clear();
                    m_buildRequestList.Clear();
                    m_hashList.Clear();

                    m_pipCompletionTaskList.Add(firstItem.Item1);
                    m_buildRequestList.Add(firstItem.Item2);

                    while (m_buildRequestList.Count < MaxMessagesPerBatch && m_buildRequests.TryTake(out var item, EngineEnvironmentSettings.RemoteWorkerSendBuildRequestTimeoutMs.Value ?? 0))
                    {
                        m_pipCompletionTaskList.Add(item.Item1);
                        m_buildRequestList.Add(item.Item2);
                    }

                    m_currentBatchSize = m_pipCompletionTaskList.Count;

                    if (m_isConnectionLost)
                    {
                        failRemotePips();
                        continue;
                    }

                    using (m_orchestratorService.Environment.Counters.StartStopwatch(PipExecutorCounter.RemoteWorker_ExtractHashesDuration))
                    {
                        Parallel.ForEach(m_pipCompletionTaskList, (task) =>
                        {
                            ExtractHashes(task.RunnablePip, m_hashList);
                        });
                    }

                    var dateTimeBeforeSend = DateTime.UtcNow;
                    TimeSpan sendDuration;
                    RpcCallResult<Unit> callResult;

                    using (var watch = m_orchestratorService.Environment.Counters.StartStopwatch(PipExecutorCounter.RemoteWorker_BuildRequestSendDuration))
                    {
                        var pipRequest = new PipBuildRequest();
                        pipRequest.Pips.AddRange(m_buildRequestList);
                        pipRequest.Hashes.AddRange(m_hashList);

                        callResult = m_workerClient.ExecutePipsAsync(pipRequest,
                            m_pipCompletionTaskList.Select(a => a.Pip.SemiStableHash).ToList()).GetAwaiter().GetResult();

                        sendDuration = watch.Elapsed;
                    }

                    if (callResult.State == RpcCallResultState.Failed)
                    {
                        failRemotePips(callResult);
                    }
                    else
                    {
                        m_orchestratorService.Environment.Counters.IncrementCounter(PipExecutorCounter.BuildRequestBatchesSentToWorkers);
                        m_orchestratorService.Environment.Counters.AddToCounter(PipExecutorCounter.HashesSentToWorkers, m_hashList.Count);

                        foreach (var task in m_pipCompletionTaskList)
                        {
                            task.SetRequestDuration(dateTimeBeforeSend, sendDuration);
                        }
                    }
                }
            }

            void failRemotePips(RpcCallResult<Unit> callResult = null)
            {
                foreach (var task in m_pipCompletionTaskList)
                {
                    FailRemotePip(
                        task,
                        callResult?.LastFailure?.DescribeIncludingInnerFailures() ?? "Connection was lost");
                }

                // TODO: We could not send the hashes; so it is hard to determine what files and directories are added to AvailableHashes.
                // That's why, for correctness, we clear the AvailableHashes all together. 
                // This seems to be very inefficient; but it is so rare that we completely fail to send the build request to the worker after retries.
                ResetAvailableHashes(m_pipGraph);

                m_orchestratorService.Environment.Counters.IncrementCounter(PipExecutorCounter.BuildRequestBatchesFailedSentToWorkers);
            }
        }

        public async Task ReadBuildManifestEventsAsync(ExecutionLogData data)
        {
            using (m_orchestratorService.Environment.Counters[PipExecutorCounter.RemoteWorker_ReadBuildManifestEventsDuration].Start())
            {
                await m_buildManifestReader.ReadEventsAsync(data);
            }
        }

        public async Task ReadExecutionLogAsync(ExecutionLogData data)
        {
            using (m_orchestratorService.Environment.Counters[PipExecutorCounter.RemoteWorker_ReadExecutionLogAsyncDuration].Start())
            {
                await m_executionLogReader.ReadEventsAsync(data);
            }
        }

        private async void OnConnectionFailureAsync(object sender, ConnectionFailureEventArgs e)
        {
            if (Interlocked.Increment(ref m_connectionClosedFlag) != 1)
            {
                // Only go through the failure logic once
                return;
            }

            e?.Log(m_appLoggingContext, machineName: $"Worker#{WorkerId} - {Name}");
            await LostConnectionAsync(e.Type);
        }

        public async Task LostConnectionAsync(ConnectionFailureType failureType)
        {
            // Unblock the caller to make it a fire&forget event handler.
            await Task.Yield();

            CountConnectionFailure(failureType);
            m_isConnectionLost = true;

            // Connection is lost. As the worker might have pending tasks, 
            // Scheduler might decide to release the worker due to insufficient amount of remaining work, which is not related to the connection issue.
            // In that case, we wait for DrainCompletion task source when releasing the worker.
            // We need to finish DrainCompletion task here to prevent hanging.
            DrainCompletion.TrySetResult(false);
            await FinishAsync(null);
        }

        private void CountConnectionFailure(ConnectionFailureType failureType)
        {
            m_orchestratorService.Counters.IncrementCounter(DistributionCounter.LostClientConnections);

            DistributionCounter? specificCounter = failureType switch
            {
                ConnectionFailureType.CallDeadlineExceeded => DistributionCounter.LostClientConnectionsDeadlineExceeded,
                ConnectionFailureType.ReconnectionTimeout => DistributionCounter.LostClientConnectionsReconnectionTimeout,
                ConnectionFailureType.UnrecoverableFailure => DistributionCounter.LostClientUnrecoverableFailure,
                ConnectionFailureType.AttachmentTimeout => DistributionCounter.LostClientAttachmentTimeout,
                ConnectionFailureType.RemotePipTimeout => DistributionCounter.LostClientRemotePipTimeout,
                _ => null
            };

            if (specificCounter.HasValue)
            {
                m_orchestratorService.Counters.IncrementCounter(specificCounter.Value);
            }
        }

        /// <inheritdoc/>
        public override void Dispose()
        {
            Contract.Requires(!m_sendThread.IsAlive);
            m_buildManifestReader?.Dispose();
            m_executionLogReader?.Dispose();
            m_workerClient?.Dispose();
            m_cancellationTokenRegistration.Dispose();

            base.Dispose();
        }

        /// <inheritdoc />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("AsyncUsage", "AsyncFixer03:FireForgetAsyncVoid")]
        public override async void Start()
        {
            m_cancellationTokenRegistration = m_orchestratorService.Environment.Context.CancellationToken.Register(() => m_setupCompletion.TrySetResult(false));

            if (ChangeStatus(WorkerNodeStatus.NotStarted, WorkerNodeStatus.Starting))
            {
                if (await TryAttachAsync())
                {
                    // Change to started state so we know the worker is connected
                    ChangeStatus(WorkerNodeStatus.Starting, WorkerNodeStatus.Started);
                }
                else
                {
                    await LostConnectionAsync(ConnectionFailureType.AttachmentTimeout);
                }
            }
        }

        private async Task<bool> TryAttachAsync()
        {
            await WaitForServiceLocationTask;

            var startData = new BuildStartData
            {
                SessionId = m_appLoggingContext.Session.Id,
                WorkerId = WorkerId,
                CachedGraphDescriptor = m_orchestratorService.CachedGraphDescriptor.ToGrpc(),
                SymlinkFileContentHash = m_orchestratorService.SymlinkFileContentHash.ToByteString(),
                FingerprintSalt = m_orchestratorService.Environment.ContentFingerprinter.FingerprintSalt,
                OrchestratorLocation = new ServiceLocation
                {
                    IpAddress = Dns.GetHostName(),
                    Port = m_orchestratorService.Port,
                },
            };

            startData.EnvironmentVariables.Add(m_orchestratorService.Environment.State.PipEnvironment
                       .FullEnvironmentVariables.ToDictionary());

            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.Elapsed < EngineEnvironmentSettings.WorkerAttachTimeout && !m_schedulerCompletion.IsCompleted)
            {
                WorkerNodeStatus status = Status;
                if (status == WorkerNodeStatus.Stopping || status == WorkerNodeStatus.Stopped)
                {
                    // Stop initiated: stop trying to attach
                    return false;
                }

                var callResult = await m_workerClient.AttachAsync(startData, m_exitCancellation.Token);
                if (callResult.State == RpcCallResultState.Succeeded)
                {
                    // Successfully attached
                    return true;
                }
                else if (m_exitCancellation.IsCancellationRequested)
                {
                    // We manually cancelled the operation: don't retry
                    return false;
                }

                try
                {
                    // We failed: let's try again after a while (as long as we don't exceed WorkerAttachTimeout)
                    await Task.Delay(TimeSpan.FromSeconds(30), m_exitCancellation.Token);
                }
                catch (TaskCanceledException)
                {
                    return false;
                }
            }

            // Timed out
            return false;
        }

        private bool TryInitiateStop(bool isEarlyRelease, [CallerMemberName] string callerName = null)
        {
            // This is called even if this call is not the one initiating the stop (as checked below)
            // to possibly override a longer waiting period before cancellation that may be active from
            // an early release. Note the reentrancy of SignalExitCancellation.
            SignalExitCancellation(isEarlyRelease);

            while (true)
            {
                WorkerNodeStatus status = Status;
                if (status == WorkerNodeStatus.Stopping || status == WorkerNodeStatus.Stopped)
                {
                    // We already initiated the stop for the worker.
                    return false;
                }

                if (ChangeStatus(status, WorkerNodeStatus.Stopping, callerName))
                {
                    break;
                }
            }

            return true;
        }

        public override async Task FinishAsync(string buildFailure = null, [CallerMemberName] string callerName = null)
        {
            if (TryInitiateStop(isEarlyRelease: false))
            {
                Logger.Log.DistributionWorkerFinish(
                    m_appLoggingContext,
                    Name,
                    callerName);

                await DisconnectAsync(buildFailure, callerName);
            }
        }

        /// <summary>
        /// Called by any codepath that is shutting communication from the worker (regular exit, disconnections, early release)
        /// Should be reentrant: note that signalling the cancellation token multiple times is not a problem and the first one wins.
        /// </summary>
        private void SignalExitCancellation(bool isEarlyRelease)
        {
            if (m_attachCompletion.Task.Status != TaskStatus.RanToCompletion || !m_attachCompletion.Task.GetAwaiter().GetResult())
            {
                // Normally we only want to wait a short amount of time for exit (15 seconds) if worker is not successfully attached.
                // If we are early releasing it is possible that we are really early in the build and so we give the worker
                // some more time (up to 5min since initialization) to attach so we don't unnecesarily make workers be idle
                // until their timeout and ultimately fail with "Orchestrator didn't attach".
                var timeSinceInitialization = DateTime.UtcNow - m_initializationTime;
                TimeSpan attachmentTolerance;
                if (isEarlyRelease && timeSinceInitialization < TimeSpan.FromMinutes(5) && timeSinceInitialization > TimeSpan.Zero)
                {
                    attachmentTolerance = TimeSpan.FromMinutes(5) - timeSinceInitialization;
                }
                else
                {
                    attachmentTolerance = TimeSpan.FromSeconds(15);
                }

                // Give some extra time for the worker to attach so it can exit gracefully
                Task.Delay(attachmentTolerance).ContinueWith(_ =>
                {
                    m_attachCompletion.TrySetResult(false);
                    m_setupCompletion.TrySetResult(false);
                });

                m_exitCancellation.CancelAfter(attachmentTolerance);
            }
        }

        /// <inheritdoc />
        public override async Task EarlyReleaseAsync()
        {
            IsEarlyReleaseInitiated = true;
            
            // Unblock scheduler
            await Task.Yield();

            if (!TryInitiateStop(isEarlyRelease: true))
            {
                // Already stopped, no need to continue.
                return;
            }
            
            var drainStopwatch = new StopwatchVar();
            bool isDrainedWithSuccess = true;

            using (drainStopwatch.Start())
            {
                // We only await DrainCompletion if the total acquired slots is not zero
                // because the worker can acquire a slot after we decide to release it but before we attempt to stop it. 
                // We only set DrainCompletion in case of Stopping state.
                if (AcquiredSlots != 0)
                {
                    isDrainedWithSuccess = await DrainCompletion.Task;
                }
            }

            // "There cannot be pending completion tasks when we drain the pending tasks successfully"
            while (!m_pipCompletionTasks.IsEmpty && isDrainedWithSuccess)
            {
                await Task.Delay(1000);
            }

            m_orchestratorService.Environment.Counters.AddToCounter(PipExecutorCounter.RemoteWorker_EarlyReleaseDrainDurationMs, (long)drainStopwatch.TotalElapsed.TotalMilliseconds);

            var disconnectStopwatch = new StopwatchVar();
            using (disconnectStopwatch.Start())
            {
                Logger.Log.DistributionWorkerFinish(
                    m_appLoggingContext,
                    Name,
                    "EarlyReleaseAsync");

                await DisconnectAsync();
            }

            WorkerEarlyReleasedTime = DateTime.UtcNow;
            Scheduler.Tracing.Logger.Log.WorkerReleasedEarly(
                m_appLoggingContext,
                Name,
                (long)drainStopwatch.TotalElapsed.TotalMilliseconds,
                (long)disconnectStopwatch.TotalElapsed.TotalMilliseconds,
                isDrainedWithSuccess);
        }

        private async Task DisconnectAsync(string buildFailure = null, [CallerMemberName] string callerName = null)
        {
            Contract.Assert(Status == WorkerNodeStatus.Stopping, $"Disconnect cannot be called for {Status} status");

            // Before we disconnect the worker, we mark it as 'stopping'; 
            // so it does not accept any requests. We can safely close the 
            // thread which is responsible for sending build requests to the
            // remote worker.
            m_buildRequests.CompleteAdding();
            if (m_sendThread.IsAlive)
            {
                m_sendThread.Join();
            }

            // If we still have a connection with the worker, we should send a message to worker to make it exit. 
            // We might be releasing a worker that didn't say Hello and so m_serviceLocation can be null, don't try to call exit
            // in that case.
            if (m_serviceLocation != null && !m_isConnectionLost)
            {
                // We wait until this task is completed to give the worker
                // a chance to attach and then respond gracefully to the exit.
                // Note that by virtue of transitioning to Stopping via TryInitiateStop
                // this task will be completed eventually (see SignalExitCancellation)
                await m_attachCompletion.Task;

                var buildEndData = new BuildEndData();

                var failure = buildFailure ?? m_exitFailure; 
                if (failure is not null)    // Don't set the protobuf field to null
                {
                    buildEndData.Failure = failure;
                }

                var callResult = await m_workerClient.ExitAsync(buildEndData, m_exitCancellation.Token);
                m_isConnectionLost = !callResult.Succeeded;
            }

            if (EverAvailable)
            {
                // If the worker was available at some point of the build and the connection is lost, 
                // we should log an warning.
                if (m_isConnectionLost)
                {
                    Scheduler.Tracing.Logger.Log.ProblematicWorkerExit(m_appLoggingContext, Name);
                }

                await m_buildManifestReader.FinalizeAsync();
                await m_executionLogReader.FinalizeAsync();
            }

            ChangeStatus(WorkerNodeStatus.Stopping, WorkerNodeStatus.Stopped, callerName);
        }

        /// <inheritdoc />
        public override async Task<PipResultStatus> MaterializeInputsAsync(ProcessRunnablePip runnablePip)
        {
            var result = await ExecutePipRemotelyAsync(runnablePip);
            return result.Result;
        }

        /// <inheritdoc />
        public override async Task<PipResultStatus> MaterializeOutputsAsync(RunnablePip runnablePip)
        {
            var result = await ExecutePipRemotelyAsync(runnablePip);
            return result.Result;
        }

        /// <inheritdoc />
        public override async Task<ExecutionResult> ExecuteProcessAsync(ProcessRunnablePip runnablePip)
        {
            var result = await ExecutePipRemotelyAsync(runnablePip);
            return result;
        }

        /// <inheritdoc />
        public override async Task<PipResult> ExecuteIpcAsync(RunnablePip runnable)
        {
            var result = await ExecutePipRemotelyAsync(runnable);
            return runnable.CreatePipResult(result.Result);
        }

        /// <inheritdoc />
        public override async Task<(RunnableFromCacheResult, PipResultStatus)> CacheLookupAsync(
            ProcessRunnablePip runnablePip,
            PipExecutionState.PipScopeState state,
            CacheableProcess cacheableProcess,
            bool shouldSkipRemoteCache = false)
        {
            ExecutionResult result = await ExecutePipRemotelyAsync(runnablePip);
            if (result.Result.IndicatesFailure())
            {
                return ValueTuple.Create<RunnableFromCacheResult, PipResultStatus>(null, result.Result);
            }

            return ValueTuple.Create<RunnableFromCacheResult, PipResultStatus>(
                PipExecutor.TryConvertToRunnableFromCacheResult(
                    runnablePip.OperationContext,
                    runnablePip.Environment,
                    state,
                    cacheableProcess,
                    result),
                result.Result);
        }

        /// <inheritdoc />
        public override async Task<ExecutionResult> PostProcessAsync(ProcessRunnablePip runnablePip)
        {
            var result = await ExecutePipRemotelyAsync(runnablePip);
            return result;
        }

        /// <summary>
        /// Executes a pip remotely
        /// </summary>
        private async Task<ExecutionResult> ExecutePipRemotelyAsync(RunnablePip runnablePip)
        {
            using (var operationContext = runnablePip.OperationContext.StartAsyncOperation(PipExecutorCounter.ExecutePipRemotelyDuration))
            using (OnPipExecutionStarted(runnablePip, operationContext))
            {
                // Send the pip to the remote machine
                await SendToRemoteAsync(operationContext, runnablePip);

                ExecutionResult result;
                if (IsFireAndForget(runnablePip))
                {
                    // We will not wait for the result of MaterializeOutputs steps on the workers,
                    // so we don't need the task in the pip completion tasks. We have just fired
                    // the request (by virtue of adding it to the send queue) and we can forget about it.
                    // We fired & forgot this pip step - we handle an empty success result instead
                    if (m_pipCompletionTasks.TryRemove(runnablePip.PipId, out var completionTask)
                        && completionTask.Completion.Task.IsCompleted)
                    {
                        // The task may have been completed in case of error (see FailRemotePip)
                        // Try to use that result for consistency
                        result = completionTask.Completion.Task.GetAwaiter().GetResult();
                    }
                    else
                    {
                        result = ExecutionResult.GetEmptySuccessResult(m_appLoggingContext);
                    }
                }
                else
                {
                    // Wait for result from remote machine
                    result = await AwaitRemoteResult(operationContext, runnablePip);
                }

                using (operationContext.StartOperation(PipExecutorCounter.HandleRemoteResultDuration))
                {
                    // Process the remote result
                    HandleRemoteResult(runnablePip, result);
                }

                return result;
            }
        }

        private bool IsFireAndForget(RunnablePip runnablePip)
        {
            return m_orchestratorService.Environment.Configuration.Distribution.FireForgetMaterializeOutput()
                && runnablePip.Step == PipExecutionStep.MaterializeOutputs;
        }

        public async Task SendToRemoteAsync(OperationContext operationContext, RunnablePip runnable)
        {
            Contract.Assert(m_workerClient != null, "Calling SendToRemote before the worker is initialized");
            Contract.Assert(m_beforeSendingToRemoteTask != null, "Remote worker not started");
            
            // Retrieve the step to be executed before the next await statement.
            var step = runnable.Step;

            await m_beforeSendingToRemoteTask;

            var pipId = runnable.PipId;
            var processRunnable = runnable as ProcessRunnablePip;
            var fingerprint = processRunnable?.CacheResult?.Fingerprint ?? ContentFingerprint.Zero;
            var pipCompletionTask = new PipCompletionTask(runnable.OperationContext, runnable);

            m_pipCompletionTasks.Add(pipId, pipCompletionTask);

            var pip = runnable.Pip;
            if (pip.PipType == PipType.Process && 
                ((Process)pip).Priority == Process.IntegrationTestPriority &&
                pip.Tags.Any(a => a.ToString(runnable.Environment.Context.StringTable) == TagFilter.TriggerWorkerConnectionTimeout) &&
                runnable.Performance.RetryCountDueToStoppedWorker == 0)
            {
                // We execute a pip which has 'buildxl.internal:triggerWorkerConnectionTimeout' in the integration tests for distributed build. 
                // It is expected to lose the connection with the worker, so that we force the pips 
                // assigned to that worker to retry on different workers.
                OnConnectionFailureAsync(null, 
                    new ConnectionFailureEventArgs(ConnectionFailureType.ReconnectionTimeout, 
                    "Triggered connection timeout for integration tests"));
            }

            if (m_attachCompletion.Task.Status == TaskStatus.RanToCompletion)
            {
                if (!m_attachCompletion.Task.GetAwaiter().GetResult())
                {
                    FailRemotePip(pipCompletionTask, "Worker did not attach.");
                    return;
                }
            }
            else
            {
                Contract.Assert(m_schedulerCompletion.Status == TaskStatus.RanToCompletion);
                // the scheduler is done with all pips except materializeoutput steps, then we fail to send the build request to the worker. 
                FailRemotePip(pipCompletionTask, "Worker did not attach until scheduler has been completed.");
                return;
            }

            var pipBuildRequest = new SinglePipBuildRequest
            {
                ActivityId = operationContext.LoggingContext.ActivityId.ToString(),
                PipIdValue = pipId.Value,
                Fingerprint = ByteString.CopyFrom(fingerprint.Hash.ToByteArray()),
                Priority = runnable.Priority,
                Step = (int)step,
                ExpectedPeakWorkingSetMb = processRunnable?.ExpectedMemoryCounters?.PeakWorkingSetMb ?? 0,
                ExpectedAverageWorkingSetMb = processRunnable?.ExpectedMemoryCounters?.AverageWorkingSetMb ?? 0,
                ExpectedPeakCommitSizeMb = processRunnable?.ExpectedMemoryCounters?.PeakCommitSizeMb ?? 0,
                ExpectedAverageCommitSizeMb = processRunnable?.ExpectedMemoryCounters?.AverageCommitSizeMb ?? 0,
                SequenceNumber = Interlocked.Increment(ref m_nextSequenceNumber),
            };

            try
            {
                m_buildRequests.Add(ValueTuple.Create(pipCompletionTask, pipBuildRequest));
            }
            catch (InvalidOperationException)
            {
                // We cannot send the pip build request as the connection has been lost with the worker. 

                // When connection has been lost, the worker gets stopped and scheduler stops choosing that worker. 
                // However, if the connection is lost after the worker is chosen 
                // and before we add those build requests to blocking collection(m_buildRequests), 
                // we will try to add the build request to the blocking collection which is marked as completed 
                // It will throw InvalidOperationException. 
                FailRemotePip(pipCompletionTask, $"Connection was lost. Was the worker ever available: {EverAvailable}");
                return;
            }
        }

        private void ExtractHashes(RunnablePip runnable, List<FileArtifactKeyedHash> hashes)
        {
            var step = runnable.Step;
            var environment = runnable.Environment;

            bool enableDistributedSourceHashing = environment.Configuration.EnableDistributedSourceHashing();

            // In the case of fire-and-forget MaterializeOutputs the pip can transition to HandleResult or Done
            // before it is actually sent to the worker, so we can observe these steps here
            bool materializingOutputs = step == PipExecutionStep.MaterializeOutputs
                || step == PipExecutionStep.HandleResult
                || step == PipExecutionStep.Done;
                
            bool requiresHashes = materializingOutputs
                || step == PipExecutionStep.MaterializeInputs
                || step == PipExecutionStep.CacheLookup
                || step == PipExecutionStep.ExecuteNonProcessPip;

            if (!requiresHashes)
            {
                return;
            }

            // The block below collects process input file artifacts and hashes
            // Currently there is no logic to keep from sending the same hashes twice
            // Consider a model where hashes for files are requested by worker
            using (var pooledFileSet = Pools.GetFileArtifactSet())
            using (var pooledDynamicFileMultiDirectoryMap = Pools.GetFileMultiDirectoryMap())
            {
                var pathTable = environment.Context.PathTable;
                var files = pooledFileSet.Instance;
                var dynamicFiles = pooledDynamicFileMultiDirectoryMap.Instance;

                using (m_orchestratorService.Environment.Counters.StartStopwatch(PipExecutorCounter.RemoteWorker_CollectPipFilesToMaterializeDuration))
                {
                    environment.State.FileContentManager.CollectPipFilesToMaterialize(
                        isMaterializingInputs: !materializingOutputs,
                        pipTable: environment.PipTable,
                        pip: runnable.Pip,
                        files: files,
                        dynamicFileMap: dynamicFiles,
                        excludeSourceFiles: enableDistributedSourceHashing,
                        // Only send content which is not already on the worker.
                        // TryAddAvailableHash can return null if the artifact is dynamic file in which case
                        // the file cannot be added to the set due to missing index (note that we're using ContentTrackingSet as the underlying set).
                        // In such a case we decide to include the artifact during the collection.
                        shouldInclude: artifact => TryAddAvailableHash(artifact) ?? true,
                        shouldIncludeServiceFiles: servicePipId => TryAddAvailableHash(servicePipId) ?? true);
                }

                using (m_orchestratorService.Environment.Counters.StartStopwatch(PipExecutorCounter.RemoteWorker_CreateFileArtifactKeyedHashDuration))
                {
                    // Now we have to consider both dynamicFiles map and files set so we union into the files set. If we only rely on files, then the following incorrect build can happen.
                    // Suppose that we have pip P that specifies D as an opaque output directory and D\f as an output file. Pip Q consumes D\f directly (not via directory dependency on D).
                    // Pip R consumes D. Suppose that the cache lookup's for Q and R happen on the same machine. Suppose that Q is processed first, TryAddAvailableHash(D/f)
                    // returns true because it's a declared output and it's added into the files set. Now, on processing R later, particularly in
                    // collecting the files of D to materialize, D\f is not included in the files set because TryAddAvailableHash(D/f) returns false, i.e., 
                    // it's a declared output and it's been added when processing Q. However, D/f is still populated to the dynamicFiles map.
                    files.UnionWith(dynamicFiles.Keys);

                    int numStringPathFiles = 0;

#if NET6_0_OR_GREATER // EnsureCapacity is only available starting from .net6
                    lock (m_hashListLock)
                    {
                        // Making sure the target collection is large enough to avoid excessive allocations when the content is added to it.
                        hashes.EnsureCapacity(hashes.Count + files.Count);
                    }
#endif
                    foreach (var file in files)
                    {
                        var fileMaterializationInfo = environment.State.FileContentManager.GetInputContent(file);
                        bool isDynamicFile = dynamicFiles.TryGetValue(file, out var dynamicDirectories) && dynamicDirectories.Count != 0;

                        bool sendStringPath = m_pipGraph.MaxAbsolutePathIndex < file.Path.Value.Index;

                        var keyedHash = new FileArtifactKeyedHash
                        {
                            IsSourceAffected = environment.State.FileContentManager.SourceChangeAffectedInputs.IsSourceChangedAffectedFile(file),
                            RewriteCount = file.RewriteCount,
                            PathValue = sendStringPath ? AbsolutePath.Invalid.RawValue : file.Path.RawValue,
                            IsAllowedFileRewrite = environment.State.FileContentManager.IsAllowedFileRewriteOutput(file.Path)
                        }.SetFileMaterializationInfo(pathTable, fileMaterializationInfo);

                        // Never set a gRPC field to null
                        if (sendStringPath)
                        {
                            keyedHash.PathString = file.Path.ToString(pathTable);
                        }

                        if (isDynamicFile)
                        {
                            foreach (var dynamicDirectory in dynamicDirectories.AsStructEnumerable())
                            {
                                keyedHash.AssociatedDirectories.Add(new GrpcDirectoryArtifact
                                {
                                    // Path id of dynamic directory input can be sent to the remote worker because it appears in the pip graph, and thus in path table.
                                    DirectoryPathValue = dynamicDirectory.Path.RawValue,
                                    DirectorySealId = dynamicDirectory.PartialSealId,
                                    IsDirectorySharedOpaque = dynamicDirectory.IsSharedOpaque
                                });
                            }
                        }

                        if (sendStringPath)
                        {
                            numStringPathFiles++;
                        }

                        lock (m_hashListLock)
                        {
                            hashes.Add(keyedHash);
                        }
                    }

                    m_orchestratorService.Environment.Counters.AddToCounter(PipExecutorCounter.HashesForStringPathsSentToWorkers, numStringPathFiles);
                }
            }
        }

        /// <summary>
        /// Fail the pip due to connection issues.
        /// </summary>
        private void FailRemotePip(PipCompletionTask pipCompletionTask, string errorMessage, [CallerMemberName] string callerName = null)
        {
            Contract.Requires(errorMessage != null);

            if (pipCompletionTask.Completion.Task.IsCompleted)
            {
                // If we already set the result for the given completionTask, do nothing.
                return;
            }

            var runnablePip = pipCompletionTask.RunnablePip;
            var operationContext = runnablePip.OperationContext;

            if (m_exitFailure == null)
            {
                m_exitFailure = errorMessage;
            }

            ExecutionResult result;
            if (runnablePip.Step == PipExecutionStep.MaterializeOutputs)
            {
                // Output replication failures on workers due to connection issues do not fail the distributed build. 
                // Setting the exit failure above ensures that the worker will fail its build and not proceed.
                Logger.Log.DistributionExecutePipFailedNetworkFailureWarning(
                    operationContext,
                    runnablePip.Description,
                    Name,
                    errorMessage: errorMessage,
                    step: runnablePip.Step.AsString(),
                    callerName: callerName);

                // Return success result
                result = new ExecutionResult();
                result.SetResult(operationContext, PipResultStatus.NotMaterialized);
                result.Seal();

                pipCompletionTask.TrySet(result);
                return;
            }

            if (runnablePip.ShouldRetryDueToStoppedWorker())
            {
                Logger.Log.DistributionExecutePipFailedNetworkFailureWarning(
                    operationContext,
                    runnablePip.Description,
                    Name,
                    errorMessage: errorMessage + " Retry Number: " + runnablePip.Performance.RetryCountDueToStoppedWorker + " out of " + runnablePip.MaxRetryLimitForStoppedWorker,
                    step: runnablePip.Step.AsString(),
                    callerName: callerName);

                result = ExecutionResult.GetRetryableNotRunResult(operationContext, Processes.RetryInfo.GetDefault(Processes.RetryReason.StoppedWorker));

                pipCompletionTask.TrySet(result);
                return;
            }

            Logger.Log.DistributionExecutePipFailedNetworkFailure(
                    operationContext,
                    runnablePip.Description,
                    Name,
                    errorMessage: errorMessage + " Retry Number: " + runnablePip.Performance.RetryCountDueToStoppedWorker + " out of " + runnablePip.MaxRetryLimitForStoppedWorker,
                    step: runnablePip.Step.AsString(),
                    callerName: callerName);

            result = ExecutionResult.GetFailureNotRunResult(operationContext);
            pipCompletionTask.TrySet(result);
        }

        public async Task<ExecutionResult> AwaitRemoteResult(OperationContext operationContext, RunnablePip runnable)
        {
            var pipId = runnable.PipId;
            var pipType = runnable.PipType;
            var environment = runnable.Environment;

            Contract.Assert(m_pipCompletionTasks.ContainsKey(pipId), "RemoteWorker tried to await the result of a pip which it did not start itself");

            ExecutionResult executionResult = null;
            var operationTimedOut = false;

            using (operationContext.StartOperation(PipExecutorCounter.AwaitRemoteResultDuration))
            {
                executionResult = await m_pipCompletionTasks[pipId].Completion.Task;

                // Remote Pip Timeout logic: disable until we fix bug #1860596
                //
                //var completionTask = m_pipCompletionTasks[pipId].Completion.Task;
                //if (await Task.WhenAny(completionTask, Task.Delay(EngineEnvironmentSettings.RemotePipTimeout)) != completionTask)
                //{
                //    // Delay task completed first
                //    operationTimedOut = true;
                //    Logger.Log.PipTimedOutRemotely(m_appLoggingContext, runnable.Pip.FormattedSemiStableHash, runnable.Step.AsString(), Name);
                //    environment.Counters.IncrementCounter(PipExecutorCounter.PipsTimedOutRemotely);
                //}
                //else
                //{
                //    // Task already completed
                //    executionResult = await completionTask;
                //}
            }

            m_pipCompletionTasks.TryRemove(runnable.PipId, out var pipCompletionTask);

            if (operationTimedOut
                // For integration tests, simulate a timeout on the first try
                || runnable.Pip is Process processPip && processPip.Priority == Process.IntegrationTestPriority &&
                   runnable.Pip.Tags.Any(a => a.ToString(environment.Context.StringTable) == TagFilter.TriggerWorkerRemotePipTimeout) 
                    && runnable.Performance.RetryCountDueToStoppedWorker == 0)
            {
                environment.Counters.IncrementCounter(PipExecutorCounter.PipsTimedOutRemotely);

                // We assume the worker is in a bad state and abandon it so we can schedule pips elsewhere
                OnConnectionFailureAsync(null, new ConnectionFailureEventArgs(ConnectionFailureType.RemotePipTimeout, $"Pip {runnable.Pip.FormattedSemiStableHash} timed out remotely on step {runnable.Step.AsString()}. Timeout: {EngineEnvironmentSettings.RemotePipTimeout.Value.TotalMilliseconds} ms"));

                // We consider the pip not run so we can retry it elsewhere
                return ExecutionResult.GetRetryableNotRunResult(m_appLoggingContext, RetryInfo.GetDefault(RetryReason.StoppedWorker));
            }

            // TODO: Make worker reported time nested under AwaitRemoteResult operation
            ReportRemoteExecutionStepDuration(operationContext, runnable, pipCompletionTask);

            if (executionResult.Result == PipResultStatus.Failed
                && !m_appLoggingContext.ErrorWasLogged)
            {
                // No error has been logged but we received a failure result.
                // Wait for a while to receive errors and then continue
                // It is ok to wait here because its expected to be a long running operation
                // and should run on a unlimited dispatcher queue
                await Task.Delay(EngineEnvironmentSettings.DistributionConnectTimeout);

                if (!m_appLoggingContext.ErrorWasLogged)
                {
                    // If error still was not logged (worker may have crashed or lost connectivity), log generic error
                    Logger.Log.DistributionPipFailedOnWorker(operationContext, runnable.Pip.SemiStableHash, runnable.Description, runnable.Step.AsString(), Name);
                }
            }

            return executionResult;
        }

        private void ReportRemoteExecutionStepDuration(OperationContext operationContext, RunnablePip runnablePip, PipCompletionTask completionTask)
        {
            var remoteStepDuration = TimeSpan.FromTicks(completionTask.ExecuteStepTicks ?? 0);
            var remoteQueueDuration = TimeSpan.FromTicks(completionTask.QueueTicks ?? 0);

            var queueRequestDuration = completionTask.QueueRequestDuration;
            var sendRequestDuration = completionTask.SendRequestDuration;

            operationContext.ReportExternalOperation(PipExecutorCounter.RemoteWorkerReportedExecutionDuration, remoteStepDuration);

            runnablePip.LogRemoteExecutionStepPerformance(WorkerId, runnablePip.Step, remoteStepDuration, remoteQueueDuration, queueRequestDuration, sendRequestDuration);
        }

        public void HandleRemoteResult(RunnablePip runnable, ExecutionResult executionResult)
        {
            var environment = runnable.Environment;
            var operationContext = runnable.OperationContext;
            var pip = runnable.Pip;
            var pipType = runnable.PipType;
            bool isExecuteStep = runnable.Step == PipExecutionStep.ExecuteProcess || runnable.Step == PipExecutionStep.ExecuteNonProcessPip;

            if (isExecuteStep)
            {
                runnable.SetExecutionResult(executionResult);
            }

            if (executionResult.Result == PipResultStatus.Canceled)
            {
                return;
            }

            if (runnable.Step == PipExecutionStep.CacheLookup && executionResult.CacheLookupPerfInfo != null)
            {
                var perfInfo = executionResult.CacheLookupPerfInfo;
                runnable.Performance.SetCacheLookupPerfInfo(perfInfo);
            }

            if (executionResult.Result == PipResultStatus.Failed)
            {
                // Failure
                m_orchestratorService.Environment.Counters.IncrementCounter(pip.PipType == PipType.Process ? PipExecutorCounter.ProcessPipsFailedRemotely : PipExecutorCounter.IpcPipsFailedRemotely);
                return;
            }

            if (!isExecuteStep)
            {
                return;
            }

            // Success
            if (pipType == PipType.Process)
            {
                m_orchestratorService.Environment.Counters.IncrementCounter(PipExecutorCounter.ProcessPipsSucceededRemotely);

                // NOTE: Process outputs will be reported later during the PostProcess step.
            }
            else
            {
                Contract.Assert(pipType == PipType.Ipc);

                m_orchestratorService.Environment.Counters.IncrementCounter(PipExecutorCounter.IpcPipsSucceededRemotely);

                // NOTE: Output content is reported for IPC but not Process because Process outputs will be reported
                // later during PostProcess because cache convergence can change which outputs for a process are used

                // Report the payload file of the IPC pip
                foreach (var (fileArtifact, fileInfo, pipOutputOrigin) in executionResult.OutputContent)
                {
                    environment.State.FileContentManager.ReportOutputContent(
                        operationContext,
                        pip.SemiStableHash,
                        artifact: fileArtifact,
                        info: fileInfo,
                        origin: pipOutputOrigin);
                }
            }
        }

        /// <summary>
        /// Signals the completion of the Attach call
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("AsyncUsage", "AsyncFixer03:FireForgetAsyncVoid")]
        public async void AttachCompletedAsync(AttachCompletionInfo attachCompletionInfo)
        {
            Contract.Requires(attachCompletionInfo != null);

            // Complete this task regardless of the success in the transition below:
            // we want to signal that the handshake with the worker was sucessful.
            m_attachCompletion.TrySetResult(true);

            // There is a nearly impossible race condition where the node may still be
            // in the Starting state (i.e. waiting for ACK of Attach call) so we try to transition
            // from Starting AND Started
            var isStatusUpdated = ChangeStatus(WorkerNodeStatus.Starting, WorkerNodeStatus.Attached);
            isStatusUpdated |= ChangeStatus(WorkerNodeStatus.Started, WorkerNodeStatus.Attached);

            if (!isStatusUpdated || m_workerClient == null)
            {
                // If the status is not changed to Attached due to the current status,
                // then no need to validate cache connection.
                // The worker might have already validated the cache and it is running.
                // Or the worker might have been stopped due to the orchestrator termination.
                return;
            }

            m_cacheValidationContentHash = attachCompletionInfo.WorkerCacheValidationContentHash.ToBondContentHash();
            TotalProcessSlots = attachCompletionInfo.MaxProcesses;
            TotalCacheLookupSlots = attachCompletionInfo.MaxCacheLookup;
            TotalMaterializeInputSlots = attachCompletionInfo.MaxMaterialize;
            TotalLightProcessSlots = attachCompletionInfo.MaxLightProcesses;
            TotalIpcSlots = attachCompletionInfo.MaxLightProcesses;
            TotalRamMb = attachCompletionInfo.AvailableRamMb;
            TotalCommitMb = attachCompletionInfo.AvailableCommitMb;

            if (TotalRamMb == 0)
            {
                // If BuildXL did not properly retrieve the available ram, then we use the default: 100gb
                TotalRamMb = 100000;
                Logger.Log.WorkerTotalRamMb(m_appLoggingContext, Name, TotalRamMb.Value, TotalCommitMb.Value);
            }

            if (TotalCommitMb == 0)
            {
                TotalCommitMb = (int)(TotalRamMb * 1.5);
            }

            var validateCacheSuccess = await ValidateCacheConnection();

            if (validateCacheSuccess)
            {
                ChangeStatus(WorkerNodeStatus.Attached, WorkerNodeStatus.Running);
                m_sendThread.Start();
            }
            else
            {
                await FinishAsync("ValidateCacheConnection failed");
            }
        }

        /// <summary>
        /// Notify the node that a pip execution completes.
        /// </summary>
        /// <param name="pipCompletionData">The pip completion data</param>
        public void NotifyPipCompletion(PipCompletionData pipCompletionData)
        {
            Contract.Requires(pipCompletionData != null);

            var pipId = new PipId(pipCompletionData.PipIdValue);

            // We might receive two notifications for the completion so that's why, we do not fail if there is no pipCompletionTask for the given pipId.
            PipCompletionTask pipCompletionTask;
            if (m_pipCompletionTasks.TryGetValue(pipId, out pipCompletionTask))
            {
                var reportedStep = (PipExecutionStep)pipCompletionData.Step;
                if (reportedStep != pipCompletionTask.RunnablePip.Step)
                {
                    // Step does not match the current step of the pip
                    // This can happen in the case of RPC retries for completion notification
                    return;
                }

                if (pipCompletionTask.Completion.Task.IsCompleted)
                {
                    // If we already set the result for the given completionTask, do nothing.
                    return;
                }

                var pip = pipCompletionTask.Pip;
                pipCompletionTask.SetDuration(pipCompletionData.ExecuteStepTicks, pipCompletionData.QueueTicks);

                int dataSize = pipCompletionData.ResultBlob.Length;
                m_orchestratorService.Counters.AddToCounter(pip.PipType == PipType.Process ? DistributionCounter.ProcessExecutionResultSize : DistributionCounter.IpcExecutionResultSize, dataSize);

                ExecutionResult result = null;
                using (m_orchestratorService.Environment.Counters.StartStopwatch(PipExecutorCounter.RemoteWorker_DeserializeFromBlobDuration))
                {
                    result = m_orchestratorService.ResultSerializer.DeserializeFromBlob(
                        pipCompletionData.ResultBlob.Memory.Span,
                        WorkerId);
                }

                pipCompletionTask.RunnablePip.ThreadId = pipCompletionData.ThreadId;
                pipCompletionTask.RunnablePip.StepStartTime = new DateTime(pipCompletionData.StartTimeTicks);
                pipCompletionTask.RunnablePip.StepDuration = new TimeSpan(pipCompletionData.ExecuteStepTicks);
                pipCompletionTask.TrySet(result);
            }
        }

        /// <summary>
        /// Worker logged an error and disconnect worker in case of specific errors
        /// </summary>
        public async Task<bool> NotifyInfrastructureErrorAsync(EventMessage forwardedEvent)
        {
            if ((EventLevel)forwardedEvent.Level != EventLevel.Error)
            {
                return false;
            }

            bool isInfraError = false;
            int eventId = forwardedEvent.EventId;

            if (eventId == (int)LogEventId.WorkerFailedDueToLowDiskSpace)
            {
                isInfraError = true;
            }

            if (eventId == (int)LogEventId.PipFailedToMaterializeItsOutputs &&
                m_orchestratorService.Environment.MaterializeOutputsInBackground)
            {
                isInfraError = true;
            }

            if (isInfraError)
            {
                await FinishAsync(forwardedEvent.Text);
                return true;
            }

            return false;
        }

        private bool ChangeStatus(WorkerNodeStatus fromStatus, WorkerNodeStatus toStatus, [CallerMemberName] string callerName = null)
        {
            if (fromStatus == toStatus)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref m_status, (int)toStatus, (int)fromStatus) != (int)fromStatus)
            {
                return false;
            }

            if (toStatus == WorkerNodeStatus.Stopped)
            {
                m_attachCompletion.TrySetResult(false);
                m_setupCompletion.TrySetResult(false);
            }
            else if (toStatus == WorkerNodeStatus.Running)
            {
                Volatile.Write(ref m_everAvailable, true);
                m_setupCompletion.TrySetResult(true);
            }

            Logger.Log.DistributionWorkerChangedState(
                m_appLoggingContext,
                Name,
                fromStatus.ToString(),
                toStatus.ToString(),
                callerName);

            // In the case of a stop fail all pending pips and stop the timer.
            if (toStatus == WorkerNodeStatus.Stopped)
            {
                Stop();
            }

            OnStatusChanged();
            return true;
        }

        private void Stop()
        {
            Analysis.IgnoreResult(m_workerClient.CloseAsync(), justification: "Okay to ignore close");

            // Fail all pending pips. Do it repeatedly in case there are other threads adding pips at the same time.
            // Note that the state never goes directly from Running to Stopped, so it is not expected to have
            // other threads trying to insert new pips.
            while (true)
            {
                PipCompletionTask[] pipCompletionTasks = m_pipCompletionTasks.Values.ToArray();
                if (pipCompletionTasks.Length == 0)
                {
                    break;
                }

                // Here, we fail the pips which we sent the build request to the worker but did not hear back and receive the result.
                foreach (PipCompletionTask pipCompletionTask in pipCompletionTasks)
                {
                    FailRemotePip(pipCompletionTask, "No result was received because connection was lost");
                }
            }
        }

        private async Task<bool> ValidateCacheConnection()
        {
            var cacheValidationContentHash = m_cacheValidationContentHash.ToContentHash();

            IArtifactContentCache contentCache = m_orchestratorService.Environment.Cache.ArtifactContentCache;
            var cacheValidationContentRetrieval = await contentCache.TryLoadAvailableContentAsync(new[]
            {
                cacheValidationContentHash,
            },
            PipExecutionContext.CancellationToken);

            if (!cacheValidationContentRetrieval.Succeeded)
            {
                Logger.Log.DistributionFailedToRetrieveValidationContentFromWorkerCacheWithException(
                    m_appLoggingContext,
                    Name,
                    cacheValidationContentHash.ToHex(),
                    cacheValidationContentRetrieval.Failure.DescribeIncludingInnerFailures());

                return false;
            }

            if (!cacheValidationContentRetrieval.Result.AllContentAvailable)
            {
                Logger.Log.DistributionFailedToRetrieveValidationContentFromWorkerCache(
                    m_appLoggingContext,
                    Name,
                    cacheValidationContentHash.ToHex());

                return false;
            }

            return true;
        }

        private sealed class PipCompletionTask
        {
            public readonly RunnablePip RunnablePip;

            public Pip Pip => RunnablePip.Pip;

            public readonly OperationContext OperationContext;
            public readonly TaskSourceSlim<ExecutionResult> Completion;
            public readonly DateTime QueuedTime;
            public TimeSpan SendRequestDuration { get; private set; }
            public TimeSpan QueueRequestDuration { get; private set; }

            public long? ExecuteStepTicks { get; private set; }

            public long? QueueTicks { get; private set; }

            public PipCompletionTask(OperationContext operationContext, RunnablePip pip)
            {
                RunnablePip = pip;
                OperationContext = operationContext;
                Completion = TaskSourceSlim.Create<ExecutionResult>();
                QueuedTime = DateTime.UtcNow;
            }

            public void SetDuration(long? executeStepTicks, long? queueTicks)
            {
                ExecuteStepTicks = executeStepTicks;
                QueueTicks = queueTicks;
            }

            public bool TrySet(ExecutionResult executionResult)
            {
                return Completion.TrySetResult(executionResult);
            }

            internal void SetRequestDuration(DateTime dateTimeBeforeSend, TimeSpan sendDuration)
            {
                QueueRequestDuration = dateTimeBeforeSend - QueuedTime;
                SendRequestDuration = sendDuration;
            }
        }
    }
}