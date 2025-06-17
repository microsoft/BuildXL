// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Service.Grpc;
using BuildXL.Cache.ContentStore.UtilitiesCore.Internal;
using BuildXL.Distribution.Grpc;
using BuildXL.Engine.Distribution.Grpc;
using BuildXL.Pips;
using BuildXL.Pips.Filter;
using BuildXL.Pips.Graph;
using BuildXL.Pips.Operations;
using BuildXL.ProcessPipExecutor;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Distribution;
using BuildXL.Scheduler.Tracing;
using BuildXL.Storage.Fingerprints;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Core.Tasks;
using BuildXL.Utilities.Core.Tracing;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tracing;
using Google.Protobuf;
using static BuildXL.Engine.Distribution.Grpc.ClientConnectionManager;
using static BuildXL.Utilities.Core.FormattableStringEx;
using static BuildXL.Utilities.Core.Tasks.TaskUtilities;
using Logger = BuildXL.Engine.Tracing.Logger;

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
        private int m_nextSequenceNumber;
        private PipGraph m_pipGraph;
        private CancellationTokenRegistration m_cancellationTokenRegistration;
        private bool m_isInitialized;
        private volatile bool m_isConnectionLost;
        private bool m_isEarlyReleaseInitiated;
        private string m_infraFailure;

        /// <inheritdoc />
        public override bool IsEarlyReleaseInitiated => Volatile.Read(ref m_isEarlyReleaseInitiated);

        /// <summary>
        /// Possible scenarios of connection failure
        /// </summary>
        public enum ConnectionFailureType { CallDeadlineExceeded, ReconnectionTimeout, UnrecoverableFailure, RemotePipTimeout, HeartbeatFailure }

        private int m_connectionClosedFlag = 0;     // Used to prevent double shutdowns upon connection failures

        /// <inheritdoc />
        public override Task<bool> AttachCompletionTask => m_attachCompletion.Task;

        /// <inheritdoc />
        public override bool IsUnknownDynamic => Location == null;

        /// <summary>
        /// The completion source to be completed after the first successful gRPC call to the worker.
        /// This task can be used to indicate whether there is a connection with the worker.
        /// </summary>
        private readonly TaskSourceSlim<bool> m_firstCallCompletion = TaskSourceSlim.Create<bool>();
        private readonly TaskSourceSlim<bool> m_attachCompletion = TaskSourceSlim.Create<bool>();
        private readonly CancellationTokenSource m_attachCancellation = new CancellationTokenSource();
        private readonly BlockingCollection<ValueTuple<PipCompletionTask, SinglePipBuildRequest>> m_buildRequests = new BlockingCollection<ValueTuple<PipCompletionTask, SinglePipBuildRequest>>();
        private readonly Thread m_sendThread;
        private readonly IWorkerClient m_workerClient;
        private readonly object m_hashListLock = new object();
        private Task m_attachOrSchedulerCompletionTask;

        private int m_status;
        public override WorkerNodeStatus Status => (WorkerNodeStatus)Volatile.Read(ref m_status);

        /// <inheritdoc/>
        public override bool EverAvailable => AttachCompletionTask.Status == TaskStatus.RanToCompletion && AttachCompletionTask.GetAwaiter().GetResult();

        /// <inheritdoc/>
        public override bool EverConnected => m_firstCallCompletion.Task.Status == TaskStatus.RanToCompletion && m_firstCallCompletion.Task.GetAwaiter().GetResult();

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
                Name = I($"#{WorkerId} ({m_serviceLocation.IpAddress}::{m_serviceLocation.Port})");

                m_workerClient.SetWorkerLocation(m_serviceLocation);
                m_serviceLocationTcs.TrySetResult(Unit.Void);
            }
        }

        private volatile int m_currentBatchSize;

        private readonly TaskCompletionSource<Unit> m_serviceLocationTcs = new();
        private Task WaitForServiceLocationTask => m_serviceLocationTcs.Task;

        private IPipExecutionEnvironment Environment => m_orchestratorService.Environment;

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
            PipExecutionContext context,
            IScheduleConfiguration scheduleConfig)
            : base(workerId, context, scheduleConfig)
        {
            m_appLoggingContext = appLoggingContext;
            m_orchestratorService = orchestratorService;
            m_workerClient = new Grpc.GrpcWorkerClient(
                m_appLoggingContext,
                orchestratorService.InvocationId,
                OnConnectionFailureAsync,
                m_orchestratorService.Counters);

            if (serviceLocation != null)
            {
                // It's importat to use the setter,
                // as it will initialize the name and the worker client too 
                Location = serviceLocation;
            }
            else
            {
                Name = I($"#{workerId} (uninitialized dynamic worker)");
            }

            // Initialize the thread that handles sending build requests as a background thread. This ensures that the presence of this thread 
            // does not prevent the termination of the build execution (bxl) process. There are scenarios, particularly during early termination 
            // due to failures, where workers may be disposed after being initiated but before the scheduler is activated. In such cases, making 
            // this thread a background thread allows the bxl process to exit cleanly without waiting for this thread to complete, avoiding potential hang-ups.
            m_sendThread = new Thread(SendBuildRequests);
            m_sendThread.IsBackground = true;
        }

        private void SendBuildRequests()
        {
            // Before we send the build request to the worker, we need to make sure that the worker is attached.
            // For all steps except materializeoutputs, we already send the build requests to available (running) workers;
            // so waiting for this task might seem redundant. However, for distributed metabuild, we materialize outputs 
            // on all workers and send the materializeoutput request to all workers, even the ones that are not available.
            // That's why, the orchestrator waits for the workers to be available until it is done executing all pips.
            m_attachOrSchedulerCompletionTask.Wait();

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

                using (m_orchestratorService.Counters.StartStopwatch(DistributionCounter.RemoteWorker_PrepareAndSendBuildRequestsDuration))
                {
                    m_pipCompletionTaskList.Clear();
                    m_buildRequestList.Clear();
                    m_hashList.Clear();

                    m_pipCompletionTaskList.Add(firstItem.Item1);
                    m_buildRequestList.Add(firstItem.Item2);

                    try
                    {
                        while (m_buildRequestList.Count < MaxMessagesPerBatch && m_buildRequests.TryTake(out var item, EngineEnvironmentSettings.RemoteWorkerSendBuildRequestTimeoutMs.Value ?? 0))
                        {
                            m_pipCompletionTaskList.Add(item.Item1);
                            m_buildRequestList.Add(item.Item2);
                        }
                    }
                    catch (Exception e)
                    {
                        // We might have disconnected the worker. We should check the loop condition (buildRequests.IsCompleted) again.
                        failRemotePips($"Exception occurred when sending the pip build request: {e}");
                        continue;
                    }

                    if (!EverAvailable || m_isConnectionLost)
                    {
                        failRemotePips($"No connection to the worker. Ever available: {EverAvailable}");
                        continue;
                    }

                    using (m_orchestratorService.Counters.StartStopwatch(DistributionCounter.RemoteWorker_ExtractHashesDuration))
                    {
                        Parallel.ForEach(m_pipCompletionTaskList, (task) =>
                        {
                            ExtractHashes(task.RunnablePip, m_hashList);
                        });
                    }

                    m_currentBatchSize = m_pipCompletionTaskList.Count;

                    var dateTimeBeforeSend = DateTime.UtcNow;
                    TimeSpan sendDuration;

                    var pipRequest = new PipBuildRequest();
                    pipRequest.Pips.AddRange(m_buildRequestList);
                    pipRequest.Hashes.AddRange(m_hashList);
                    string description = getExecuteDescription();

                    RpcCallResult<Unit> callResult;

                    using (var watch = m_orchestratorService.Counters.StartStopwatch(DistributionCounter.RemoteWorker_BuildRequestSendDuration))
                    {
                        callResult = m_workerClient.ExecutePipsAsync(pipRequest, description).GetAwaiter().GetResult();
                        sendDuration = watch.Elapsed;
                    }

                    if (!callResult.Succeeded)
                    {
                        failRemotePips(callResult?.LastFailure?.DescribeIncludingInnerFailures() ?? "gRPC call failed");
                    }
                    else
                    {
                        m_orchestratorService.Counters.IncrementCounter(DistributionCounter.BuildRequestBatchesSentToWorkers);
                        m_orchestratorService.Counters.AddToCounter(DistributionCounter.HashesSentToWorkers, m_hashList.Count);
                        m_orchestratorService.Counters.AddToCounter(DistributionCounter.TotalGrpcDurationMs, (long)sendDuration.TotalMilliseconds);

                        foreach (var task in m_pipCompletionTaskList)
                        {
                            task.SetRequestDuration(dateTimeBeforeSend, sendDuration);
                        }
                    }
                }
            }

            void failRemotePips(string failureMessage)
            {
                foreach (var task in m_pipCompletionTaskList)
                {
                    FailRemotePip(task, failureMessage);
                }

                // TODO: We could not send the hashes; so it is hard to determine what files and directories are added to AvailableHashes.
                // That's why, for correctness, we clear the AvailableHashes all together. 
                // This seems to be very inefficient; but it is so rare that we completely fail to send the build request to the worker after retries.
                ResetAvailableHashes(m_pipGraph);

                m_orchestratorService.Counters.IncrementCounter(DistributionCounter.BuildRequestBatchesFailedSentToWorkers);
            }

            string getExecuteDescription()
            {
                using (var sbPool = Pools.GetStringBuilder())
                {
                    var sb = sbPool.Instance;

                    sb.Append("ExecutePips: ");
                    sb.Append($"{m_pipCompletionTaskList.Count} pips, {m_hashList.Count} file hashes, ");
                    foreach (var pipCompletionTask in m_pipCompletionTaskList)
                    {
                        sb.AppendFormat(CultureInfo.InvariantCulture, "{0:X16} ", pipCompletionTask.Pip.SemiStableHash);
                    }

                    return sb.ToString();
                }
            }
        }

        public async Task ReadBuildManifestEventsAsync(ExecutionLogData data)
        {
            using (m_orchestratorService.Counters[DistributionCounter.RemoteWorker_ReadBuildManifestEventsDuration].Start())
            {
                await m_buildManifestReader.ReadEventsAsync(data);
            }
        }

        public async Task ReadExecutionLogAsync(ExecutionLogData data)
        {
            using (m_orchestratorService.Counters[DistributionCounter.RemoteWorker_ReadExecutionLogAsyncDuration].Start())
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

            CountConnectionFailure(e.Type);
            m_isConnectionLost = true;
            m_infraFailure = $"The connection got lost with the worker. Is ever connected: {EverConnected}. Is early-release initiated: {IsEarlyReleaseInitiated}";

            // Connection is lost. As the worker might have pending tasks, 
            // Scheduler might decide to release the worker due to insufficient amount of remaining work, which is not related to the connection issue.
            // In that case, we wait for DrainCompletion task source when releasing the worker.
            // We need to finish DrainCompletion task here to prevent hanging.
            DrainCompletion.TrySetResult(false);

            // Unblock the caller to make it a fire&forget event handler.
            await Task.Yield();

            await FinishAsync();
        }

        private void CountConnectionFailure(ConnectionFailureType failureType)
        {
            m_orchestratorService.Counters.IncrementCounter(DistributionCounter.LostClientConnections);

            DistributionCounter? specificCounter = failureType switch
            {
                ConnectionFailureType.CallDeadlineExceeded => DistributionCounter.LostClientConnectionsDeadlineExceeded,
                ConnectionFailureType.ReconnectionTimeout => DistributionCounter.LostClientConnectionsReconnectionTimeout,
                ConnectionFailureType.UnrecoverableFailure => DistributionCounter.LostClientUnrecoverableFailure,
                ConnectionFailureType.RemotePipTimeout => DistributionCounter.LostClientRemotePipTimeout,
                ConnectionFailureType.HeartbeatFailure => DistributionCounter.LostClientHeartbeatFailure,
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
            m_buildManifestReader?.Dispose();
            m_executionLogReader?.Dispose();
            m_workerClient?.Dispose();
            m_cancellationTokenRegistration.Dispose();

            base.Dispose();
        }

        public override void InitializeForDistribution(
            OperationContext parent,
            IConfiguration config,
            PipGraph pipGraph,
            IExecutionLogTarget executionLogTarget,
            Task schedulerCompletion,
            Action<Scheduler.Distribution.Worker> statusChangedAction)
        {
            m_pipGraph = pipGraph;

            // For manifest events, we wait for the messages to be processed before we send an ack to the worker. 
            m_buildManifestReader = new WorkerExecutionLogReader(m_appLoggingContext, executionLogTarget, Environment, Name, asyncProcessing: false);

            // For xlg/non-manifest events, we wait for the messages to be added to the queue before we send an ack to the worker. 
            m_executionLogReader = new WorkerExecutionLogReader(m_appLoggingContext, executionLogTarget, Environment, Name, asyncProcessing: true);

            // If there is a minimum waiting time to wait for attachment, we don't want to signal the scheduler is completed until this waiting period is over.
            // Minimum waiting time is only for builds where we replicate outputs to workers such as metabuilds.
            // This is to prevent workers from getting dropped from very fast metabuilds taking less than 1 minute. 
            var schedulerCompletionWithExtraWait = Task.WhenAll(Task.Delay(config.Distribution.ReplicateOutputsToWorkers() ? (int)EngineEnvironmentSettings.MinimumWaitForRemoteWorker.Value.TotalMilliseconds : 0),
                                                                                    schedulerCompletion);

            // This task is awaited for two purposes: (i) before sending build requests to the worker, and (ii) before stopping the worker.
            // The condition for proceeding is either the completion of the scheduler (with an extra wait for metabuild), or the completion of the attachment process.
            m_attachOrSchedulerCompletionTask = Task.WhenAny(schedulerCompletionWithExtraWait, AttachCompletionTask).Unwrap();

            m_cancellationTokenRegistration = Environment.Context.CancellationToken.Register(() => m_attachCompletion.TrySetResult(false));

            base.InitializeForDistribution(parent, config, pipGraph, executionLogTarget, schedulerCompletion, statusChangedAction);
            m_isInitialized = true;
        }

        /// <inheritdoc />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("AsyncUsage", "AsyncFixer03:FireForgetAsyncVoid")]
        public override async void Start()
        {
            Contract.Requires(m_isInitialized, "The remote worker needs to be initialized before getting started.");

            await Task.Yield(); // Unblock the scheduler.

            m_sendThread.Start();

            if (ChangeStatus(WorkerNodeStatus.NotStarted, WorkerNodeStatus.Starting))
            {
                if (await TryAttachAsync())
                {
                    // We successfully sent the attachment request to the worker. 
                    // It means that the worker is connected. 
                    m_firstCallCompletion.TrySetResult(true);

                    // Change to started state so we know the worker is connected
                    ChangeStatus(WorkerNodeStatus.Starting, WorkerNodeStatus.Started);
                    return;
                }

                // The attach call was unsuccessful. 
                m_attachCompletion.TrySetResult(false);
                m_firstCallCompletion.TrySetResult(false);
            }
        }

        private async Task<bool> TryAttachAsync()
        {
            if (Environment.Configuration.Distribution.ReplicateOutputsToWorkers() && !Environment.Configuration.Distribution.FireForgetMaterializeOutput())
            {
                // This combination of flags is unfortunate: with ReplicateOutputsToWorkers we try to materialize outputs in all workers,
                // even 'unavailable' ones. If FireForgetMaterializeOutput is disabled, this might block the scheduler completion
                // waiting for that worker to attach. In this case, we want to trigger the cancellation explicitly after WorkerAttachTimeout has elapsed:
                // this will make the RemoteWorker abandon the build, and the scheduler will be able to continue.
                //
                // We should not allow this situation to come to pass: see work item #2290359.
                // As of writing this comment, some customers might be running this mode explicitly, so to to be safe in the transition,
                // we introduce this special handling before removing that scenario altogether.
                m_attachCancellation.CancelAfter(EngineEnvironmentSettings.WorkerAttachTimeout);
            }

            try
            {
#if NET_FRAMEWORK
                WaitForServiceLocationTask.Wait(m_attachCancellation.Token);
#else
                await WaitForServiceLocationTask.WaitAsync(m_attachCancellation.Token);
#endif
            }
            catch (OperationCanceledException)
            {
                return false;
            }

            var startData = new BuildStartData
            {
                SessionId = m_appLoggingContext.Session.Id,
                WorkerId = WorkerId,
                CachedGraphDescriptor = m_orchestratorService.CachedGraphDescriptor.ToGrpc(),
                SymlinkFileContentHash = m_orchestratorService.SymlinkFileContentHash.ToByteString(),
                FingerprintSalt = Environment.ContentFingerprinter.FingerprintSalt,
                OrchestratorLocation = new ServiceLocation
                {
                    IpAddress = m_orchestratorService.Hostname,
                    Port = m_orchestratorService.Port,
                },
            };

            startData.EnvironmentVariables.Add(Environment.State.PipEnvironment
                       .FullEnvironmentVariables.ToDictionary());

            startData.PipSpecificPropertiesAndValues.AddRange(Environment.Configuration.Engine.PipSpecificPropertyAndValues.Select(p => p.ToGrpc()));

            while (!m_attachOrSchedulerCompletionTask.IsCompleted)
            {
                WorkerNodeStatus status = Status;
                if (status.IsStoppingOrStopped())
                {
                    // Stop initiated: stop trying to attach
                    return false;
                }

                var callResult = await m_workerClient.AttachAsync(startData, m_attachCancellation.Token);
                if (callResult.State == RpcCallResultState.Succeeded)
                {
                    // Successfully attached
                    return true;
                }
                else if (m_attachCancellation.IsCancellationRequested)
                {
                    // We might cancel Attach call due to the followings:
                    // (i) Scheduler early-released the worker.
                    // (ii) The scheduler has already finished all the work. 
                    // (iii) The attachment timeout was hit
                    return false;
                }

                try
                {
                    // We failed: let's try again after a minute.
                    await Task.Delay(TimeSpan.FromSeconds(60), m_attachCancellation.Token);
                }
                catch (TaskCanceledException)
                {
                    return false;
                }
            }

            return false;
        }

        private async Task<bool> TryInitiateStop([CallerMemberName] string callerName = null)
        {
            if (IsUnknownDynamic && IsEarlyReleaseInitiated)
            {
                // For the dynamic workers whose locations are still unknown, we should not wait for the attachment period when they early-released.
                // Those workers can already terminate after they get 'Released' result from their 'Hello' call. 
                await m_attachCancellation.CancelTokenAsyncIfSupported(); // This will complete m_firstCallCompletion
            }
            else if (!m_isConnectionLost)
            {
                // If we haven't completed the attachment with the worker, we want to give the worker more time to attach 
                // so we don't unnecesarily make workers be idle until their timeout and ultimately fail with "Orchestrator didn't attach".
                // That's why, we wait till the scheduler completion or the attachment.
                // If the connection is lost due to an unrecoverable failure, there is no need to give a chance to the worker for attachment.
                await m_attachOrSchedulerCompletionTask;
            }

            if (m_firstCallCompletion.Task.Status != TaskStatus.RanToCompletion)
            {
                // If the scheduler is completed before Attach call gets completed, we cancel it and we do not wait more.
                // In CloudBuild, when the orchestrator is terminated, all workers will be terminated as well even if their bxl process is still alive.
                await m_attachCancellation.CancelTokenAsyncIfSupported();
            }

            while (true)
            {
                WorkerNodeStatus status = Status;
                if (status.IsStoppingOrStopped())
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

        public override async Task FinishAsync([CallerMemberName] string callerName = null)
        {
            if (await TryInitiateStop())
            {
                if (IsEarlyReleaseInitiated)
                {
                    await DrainRemainingPips();
                }

                Logger.Log.DistributionWorkerFinish(
                    m_appLoggingContext,
                    Name,
                    callerName);

                await DisconnectAsync(callerName);
            }
        }

        private async Task DrainRemainingPips()
        {
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

            m_orchestratorService.Counters.AddToCounter(DistributionCounter.RemoteWorker_EarlyReleaseDrainDurationMs, (long)drainStopwatch.TotalElapsed.TotalMilliseconds);
        }

        /// <inheritdoc />
        public override async Task EarlyReleaseAsync()
        {
            m_isEarlyReleaseInitiated = true;

            // Unblock scheduler
            await Task.Yield();

            await FinishAsync();

            WorkerEarlyReleasedTime = DateTime.UtcNow;
            Scheduler.Tracing.Logger.Log.WorkerReleasedEarly(m_appLoggingContext, Name);
        }

        private async Task DisconnectAsync([CallerMemberName] string callerName = null)
        {
            Contract.Requires(AttachCompletionTask.IsCompleted, "AttachCompletionTask needs to be completed before we disconnect the worker");
            Contract.Requires(Status == WorkerNodeStatus.Stopping, $"Disconnect cannot be called for {Status} status");

            // Before we disconnect the worker, we mark it as 'stopping'; 
            // so it does not accept any requests. We can safely close the 
            // thread which is responsible for sending build requests to the
            // remote worker.
            m_buildRequests.CompleteAdding();
            if (m_sendThread.IsAlive)
            {
                m_sendThread.Join();
            }

            if (!m_workerClient.TryFinalizeStreaming())
            {
                Logger.Log.DistributionStreamingNetworkFailure(m_appLoggingContext, Name);
            }

            if (!EverAvailable && !IsEarlyReleaseInitiated)
            {
                m_infraFailure = $"{Name} failed to connect to the orchestrator (minimum wait: {EngineEnvironmentSettings.MinimumWaitForRemoteWorker.Value.TotalMinutes} min, timeout: {EngineEnvironmentSettings.WorkerAttachTimeout.Value.TotalMinutes} min), typically due to a delay between orchestrator startup and worker bxl process initiation. Such delays can be expected in certain multi-stage distributed builds.";
            }

            WorkerExitResponse workerExitResponse = null;
            // If we still have a connection with the worker, we should send a message to worker to make it exit. 
            if (!m_isConnectionLost && EverConnected)
            {
                var buildEndData = new BuildEndData();

                // The infrastructure failures should be given higher priority when forwarding them to workers.
                buildEndData.Failure = m_infraFailure ?? (Environment.HasFailed ? "Distributed build failed. See errors on orchestrator." : string.Empty);
                var exitCallResult = await m_workerClient.ExitAsync(buildEndData);
                if (!exitCallResult.Succeeded)
                {
                    m_infraFailure += $" Exit call failed with {exitCallResult.LastFailure?.DescribeIncludingInnerFailures() ?? "gRPC call failed"}.";
                }
                else
                {
                    workerExitResponse = exitCallResult.Value;
                }
            }

            if (!string.IsNullOrEmpty(m_infraFailure))
            {
                // We log the following message for each worker if any of these occurs:
                // (i) The worker has not been ever attached to the orchestrator at any part of the build and the early release has not been initiated for this worker.
                // (ii) The worker has been connected to the orchestrator at one point, but then we lost the connection with the worker.
                // (iii) The worker failed to materialize all outputs.
                // (iv) The exit call fails to be sent.

                Scheduler.Tracing.Logger.Log.ProblematicWorkerExit(m_appLoggingContext, m_infraFailure);
                m_orchestratorService.Counters.IncrementCounter(DistributionCounter.NumProblematicWorkers);
                Environment.ReportProblematicWorker();
            }

            if (EverConnected)
            {
                await m_buildManifestReader.FinalizeAsync();
                await m_executionLogReader.FinalizeAsync();
            }

            if (workerExitResponse != null)
            {
                CompareEventStats(workerExitResponse);
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
                SendToRemote(operationContext, runnablePip);

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
            return Environment.Configuration.Distribution.FireForgetMaterializeOutput()
                && runnablePip.Step == PipExecutionStep.MaterializeOutputs;
        }

        public void SendToRemote(OperationContext operationContext, RunnablePip runnable)
        {
            Contract.Assert(runnable.Step == PipExecutionStep.MaterializeOutputs || Status == WorkerNodeStatus.Running || EverAvailable, $"All steps except MaterializeOutput step require available workers: {Name}");

            var pipId = runnable.PipId;
            var processRunnable = runnable as ProcessRunnablePip;
            var fingerprint = processRunnable?.CacheResult?.Fingerprint ?? ContentFingerprint.Zero;
            var pipCompletionTask = new PipCompletionTask(runnable.OperationContext, runnable);
            m_pipCompletionTasks.Add(pipId, pipCompletionTask);

            var pip = runnable.Pip;
            if (pip.PipType == PipType.Process &&
                ((Process)pip).Priority == Process.IntegrationTestPriority &&
                pip.Tags.Any(a => a.ToString(Environment.Context.StringTable) == TagFilter.TriggerWorkerConnectionTimeout) &&
                runnable.Performance.RetryCountOnRemoteWorkers == 0)
            {
                // We execute a pip which has 'buildxl.internal:triggerWorkerConnectionTimeout' in the integration tests for distributed build. 
                // It is expected to lose the connection with the worker, so that we force the pips 
                // assigned to that worker to retry on different workers.
                OnConnectionFailureAsync(null,
                    new ConnectionFailureEventArgs(ConnectionFailureType.ReconnectionTimeout,
                    "Triggered connection timeout for integration tests"));
            }

            var pipBuildRequest = new SinglePipBuildRequest
            {
                ActivityId = operationContext.LoggingContext.ActivityId.ToString(),
                PipIdValue = pipId.Value,
                Fingerprint = ByteString.CopyFrom(fingerprint.Hash.ToByteArray()),
                Priority = runnable.Priority,
                Step = (int)runnable.Step,
                ExpectedPeakWorkingSetMb = processRunnable?.ExpectedMemoryCounters?.PeakWorkingSetMb ?? 0,
                ExpectedAverageWorkingSetMb = processRunnable?.ExpectedMemoryCounters?.AverageWorkingSetMb ?? 0,
                SequenceNumber = Interlocked.Increment(ref m_nextSequenceNumber),
            };

            try
            {
                var isAdded = m_buildRequests.TryAdd(ValueTuple.Create(pipCompletionTask, pipBuildRequest));
                if (!isAdded)
                {
                    FailRemotePip(pipCompletionTask, $"The pip build request could not be added to the send list.");
                }
            }
            catch (Exception e)
            {
                // We cannot send the pip build request as the connection has been lost with the worker. 

                // When connection has been lost, the worker gets stopped and scheduler stops choosing that worker. 
                // However, if the connection is lost after the worker is chosen 
                // and before we add those build requests to blocking collection(m_buildRequests), 
                // we will try to add the build request to the blocking collection which is marked as completed 
                // It will throw InvalidOperationException. 
                FailRemotePip(pipCompletionTask, $"Connection was lost. Was the worker ever available: {EverAvailable}. The exception: {e}");
            }
        }

        private void ExtractHashes(RunnablePip runnable, List<FileArtifactKeyedHash> hashes)
        {
            var step = runnable.Step;

            bool enableDistributedSourceHashing = Environment.Configuration.EnableDistributedSourceHashing();

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
                var pathTable = Environment.Context.PathTable;
                var files = pooledFileSet.Instance;
                var dynamicFiles = pooledDynamicFileMultiDirectoryMap.Instance;

                using (m_orchestratorService.Counters.StartStopwatch(DistributionCounter.RemoteWorker_CollectPipFilesToMaterializeDuration))
                {
                    Environment.State.FileContentManager.CollectPipFilesToMaterialize(
                        isMaterializingInputs: !materializingOutputs,
                        pipTable: Environment.PipTable,
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

                using (m_orchestratorService.Counters.StartStopwatch(DistributionCounter.RemoteWorker_CreateFileArtifactKeyedHashDuration))
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
                        var fileMaterializationInfo = Environment.State.FileContentManager.GetInputContent(file);
                        bool isDynamicFile = dynamicFiles.TryGetValue(file, out var dynamicDirectories) && dynamicDirectories.Count != 0;

                        bool sendStringPath = m_pipGraph.MaxAbsolutePathIndex < file.Path.Value.Index;

                        var keyedHash = new FileArtifactKeyedHash
                        {
                            IsSourceAffected = Environment.State.FileContentManager.SourceChangeAffectedInputs.IsSourceChangedAffectedFile(file),
                            RewriteCount = file.RewriteCount,
                            PathValue = sendStringPath ? AbsolutePath.Invalid.RawValue : file.Path.RawValue,
                            IsAllowedFileRewrite = Environment.State.FileContentManager.IsAllowedFileRewriteOutput(file.Path)
                        }.SetFileMaterializationInfo(pathTable, fileMaterializationInfo);

                        // Never set a gRPC field to null
                        if (sendStringPath)
                        {
                            var expandedPath = new ExpandedAbsolutePath(file.Path, pathTable);

                            // Honor casing if specified
                            expandedPath = fileMaterializationInfo.GetPathWithProperCasingIfAvailable(pathTable, expandedPath);

                            keyedHash.PathString = expandedPath.ExpandedPath;
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

                    m_orchestratorService.Counters.AddToCounter(DistributionCounter.HashesForStringPathsSentToWorkers, numStringPathFiles);
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

            ExecutionResult result;
            if (pipCompletionTask.Step == PipExecutionStep.MaterializeOutputs)
            {
                // Output replication failures on workers due to connection issues do not fail the distributed build. 
                // Setting the exit failure above ensures that the worker will fail its build and not proceed.
                m_infraFailure = "Some materialize output requests could not be sent.";

                result = new ExecutionResult();
                result.SetResult(operationContext, PipResultStatus.NotMaterialized);
                result.Seal();

                pipCompletionTask.TrySet(result);
                return;
            }

            var maxRetryLimitOnRemoteWorkers = Environment.Configuration.Distribution.MaxRetryLimitOnRemoteWorkers;
            if (maxRetryLimitOnRemoteWorkers > runnablePip.Performance.RetryCountOnRemoteWorkers)
            {
                Logger.Log.DistributionExecutePipFailedNetworkFailureWarning(
                    operationContext,
                    runnablePip.Description,
                    Name,
                    errorMessage: errorMessage + " Retry Number: " + runnablePip.Performance.RetryCountOnRemoteWorkers + " out of " + maxRetryLimitOnRemoteWorkers,
                    step: pipCompletionTask.Step.AsString(),
                    callerName: callerName);

                result = ExecutionResult.GetRetryableNotRunResult(operationContext, RetryInfo.GetDefault(RetryReason.RemoteWorkerFailure));

                pipCompletionTask.TrySet(result);
                return;
            }

            Logger.Log.DistributionExecutePipFailedDistributionFailureWarning(
                    operationContext,
                    runnablePip.Description,
                    Name,
                    errorMessage: errorMessage,
                    maxRetryLimit: maxRetryLimitOnRemoteWorkers,
                    step: pipCompletionTask.Step.AsString(),
                    callerName: callerName);

            // Return 'DistributionFailure' so it can be retried only on the orchestrator machine next time.
            result = ExecutionResult.GetRetryableNotRunResult(operationContext, RetryInfo.GetDefault(RetryReason.DistributionFailure));

            pipCompletionTask.TrySet(result);
        }

        public async Task<ExecutionResult> AwaitRemoteResult(OperationContext operationContext, RunnablePip runnable)
        {
            var pipId = runnable.PipId;
            var pipType = runnable.PipType;

            Contract.Assert(m_pipCompletionTasks.ContainsKey(pipId), "RemoteWorker tried to await the result of a pip which it did not start itself");

            ExecutionResult executionResult = null;
            var operationTimedOut = false;

            using (operationContext.StartOperation(PipExecutorCounter.AwaitRemoteResultDuration))
            {
                if (!EngineEnvironmentSettings.RemotePipTimeout.Value.HasValue)
                {
                    executionResult = await m_pipCompletionTasks[pipId].Completion.Task;
                }
                else
                {
                    var completionTask = m_pipCompletionTasks[pipId].Completion.Task;

                    var timeoutWatch = new StopwatchVar();
                    using (timeoutWatch.Start())
                    {
                        if (await Task.WhenAny(completionTask, Task.Delay(EngineEnvironmentSettings.RemotePipTimeout.Value.Value)) != completionTask)
                        {
                            // Delay task completed first
                            operationTimedOut = true;
                            Logger.Log.PipTimedOutRemotely(m_appLoggingContext, runnable.Pip.FormattedSemiStableHash, runnable.Step.AsString(), Name, timeoutWatch.TotalElapsed.Minutes, (int)EngineEnvironmentSettings.RemotePipTimeout.Value.Value.TotalMinutes);
                        }
                        else
                        {
                            // Task already completed
                            executionResult = await completionTask;
                        }
                    }
                }
            }

            m_pipCompletionTasks.TryRemove(runnable.PipId, out var pipCompletionTask);

            if (operationTimedOut
                // For integration tests, simulate a timeout on the first try
                || runnable.Pip is Process processPip && processPip.Priority == Process.IntegrationTestPriority &&
                   runnable.Pip.Tags.Any(a => a.ToString(Environment.Context.StringTable) == TagFilter.TriggerWorkerRemotePipTimeout)
                    && runnable.Performance.RetryCountOnRemoteWorkers == 0)
            {
                Environment.Counters.IncrementCounter(PipExecutorCounter.PipsTimedOutRemotely);

                // We assume the worker is in a bad state and abandon it so we can schedule pips elsewhere
                OnConnectionFailureAsync(null, new ConnectionFailureEventArgs(ConnectionFailureType.RemotePipTimeout, $"Pip {runnable.Pip.FormattedSemiStableHash} timed out remotely on step {pipCompletionTask.Step.AsString()}."));

                // We consider the pip not run so we can retry it elsewhere
                return ExecutionResult.GetRetryableNotRunResult(m_appLoggingContext, RetryInfo.GetDefault(RetryReason.RemoteWorkerFailure));
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
                    // If error still was not logged (worker may have crashed or lost connectivity), retry the pip.
                    return ExecutionResult.GetRetryableNotRunResult(m_appLoggingContext, RetryInfo.GetDefault(RetryReason.RemoteWorkerFailure));
                }
            }

            return executionResult;
        }

        private void ReportRemoteExecutionStepDuration(OperationContext operationContext, RunnablePip runnablePip, PipCompletionTask completionTask)
        {
            var remoteStepDuration = TimeSpan.FromTicks(completionTask.ExecuteStepTicks ?? 0);
            var remoteQueueDuration = TimeSpan.FromTicks(completionTask.QueueTicks ?? 0);

            operationContext.ReportExternalOperation(PipExecutorCounter.RemoteWorkerReportedExecutionDuration, remoteStepDuration);

            runnablePip.LogRemoteExecutionStepPerformance(WorkerId, completionTask.Step, remoteStepDuration, remoteQueueDuration, completionTask.QueueRequestDuration, completionTask.GrpcDuration);
        }

        public void HandleRemoteResult(RunnablePip runnable, ExecutionResult executionResult)
        {
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
                Environment.Counters.IncrementCounter(pip.PipType == PipType.Process ? PipExecutorCounter.ProcessPipsFailedRemotely : PipExecutorCounter.IpcPipsFailedRemotely);
                return;
            }

            if (!isExecuteStep)
            {
                return;
            }

            // Success
            if (pipType == PipType.Process)
            {
                Environment.Counters.IncrementCounter(PipExecutorCounter.ProcessPipsSucceededRemotely);

                // NOTE: Process outputs will be reported later during the PostProcess step.
            }
            else
            {
                Contract.Assert(pipType == PipType.Ipc);

                Environment.Counters.IncrementCounter(PipExecutorCounter.IpcPipsSucceededRemotely);

                // NOTE: Output content is reported for IPC but not Process because Process outputs will be reported
                // later during PostProcess because cache convergence can change which outputs for a process are used

                // Report the payload file of the IPC pip
                foreach (var (fileArtifact, fileInfo, pipOutputOrigin) in executionResult.OutputContent)
                {
                    Environment.State.FileContentManager.ReportOutputContent(
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
        public void AttachCompleted(AttachCompletionInfo attachCompletionInfo)
        {
            Contract.Requires(attachCompletionInfo != null);

            TotalProcessSlots = attachCompletionInfo.MaxProcesses;
            TotalCacheLookupSlots = attachCompletionInfo.MaxCacheLookup;
            TotalMaterializeInputSlots = attachCompletionInfo.MaxMaterialize;
            TotalLightProcessSlots = attachCompletionInfo.MaxLightProcesses;
            TotalIpcSlots = attachCompletionInfo.MaxLightProcesses;

            int? availableRamMb = attachCompletionInfo.AvailableRamMb;
            int? totalRamMb = attachCompletionInfo.TotalRamMb;

            if (totalRamMb == 0 || availableRamMb == 0)
            {
                // If the worker could not measure the ram size, we should use the orchestrator's ram counters.
                totalRamMb = Environment.LocalWorker.TotalRamMb;
                availableRamMb = Environment.LocalWorker.AvailableRamMb;
            }

            // When the remote worker is first attached, we just need to know the ram counters, so we can set up the ram semaphores.
            // The cpu usage information will be sent as a part of the heartbeat messages.
            UpdatePerfInfo(m_appLoggingContext,
                currentTotalRamMb: totalRamMb,
                machineAvailableRamMb: availableRamMb,
                engineRamMb: attachCompletionInfo.EngineRamMb,
                engineCpuUsage: null,
                machineCpuUsage: null);

            // There is a nearly impossible race condition where the node may still be
            // in the Starting state (i.e. waiting for ACK of Attach call) so we try to transition
            // from Starting AND Started
            ChangeStatus(WorkerNodeStatus.Starting, WorkerNodeStatus.Running);
            ChangeStatus(WorkerNodeStatus.Started, WorkerNodeStatus.Running);
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

                var serializationDuration = TimeSpan.FromTicks(pipCompletionData.SerializationTicks);
                ExecutionResult result = null;
                using (var counter = m_orchestratorService.Counters.StartStopwatch(DistributionCounter.RemoteWorker_DeserializeFromBlobDuration))
                {
                    result = m_orchestratorService.ResultSerializer.DeserializeFromBlob(
                        pipCompletionData.ResultBlob.Memory.Span,
                        WorkerId);
                    serializationDuration += counter.Elapsed;
                }

                pipCompletionTask.SetDuration(pipCompletionData.ExecuteStepTicks, pipCompletionData.QueueTicks);

                pipCompletionTask.RunnablePip.ThreadId = pipCompletionData.ThreadId;
                pipCompletionTask.RunnablePip.StepStartTime = new DateTime(pipCompletionData.StartTimeTicks);
                pipCompletionTask.RunnablePip.StepDuration = new TimeSpan(pipCompletionData.ExecuteStepTicks);

                var receiveResultDuration = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - pipCompletionData.BeforeSendTicks);
                pipCompletionTask.AddGrpcDuration(receiveResultDuration);
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
                Environment.MaterializeOutputsInBackground)
            {
                isInfraError = true;
            }

            if (isInfraError)
            {
                m_infraFailure = forwardedEvent.Text;
                await FinishAsync();
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

            switch (toStatus)
            {
                case WorkerNodeStatus.Stopping:
                case WorkerNodeStatus.Stopped:
                    // If the worker hasn't attached yet, cancel it.
                    m_attachCompletion.TrySetResult(false);
                    break;
                case WorkerNodeStatus.Running:
                    m_attachCompletion.TrySetResult(true);
                    break;
            }

            if (toStatus == WorkerNodeStatus.Stopped)
            {
                // In the case of a stop fail all pending pips and stop the timer.
                Stop();
            }

            Logger.Log.DistributionWorkerChangedState(
                m_appLoggingContext,
                Name,
                fromStatus.ToString(),
                toStatus.ToString(),
                callerName);

            OnStatusChanged();
            return true;
        }

        private void CompareEventStats(WorkerExitResponse workerExitResponse)
        {
            // Only call this function when workerExitResponse is not null 
            Contract.AssertNotNull(workerExitResponse);
            Logger.Log.DistributionComparingEventStats(m_appLoggingContext, Name);

            // Event stats not found on worker when exit requested.
            if (workerExitResponse.EventCounts.Count == 0)
            {
                Logger.Log.DistributionEventStatsNotFound(m_appLoggingContext, $"Worker {Name}");
                return;
            }

            long[] eventStatsOnOrchestrator = null;
            if (ExecutionLogTarget != null)
            {
                var logTargets = ((MultiExecutionLogTarget)ExecutionLogTarget).LogTargets;
                var eventStatsLogTarget = logTargets.Where(t => t is EventStatsExecutionLogTarget).FirstOrDefault();
                eventStatsOnOrchestrator = ((EventStatsExecutionLogTarget)eventStatsLogTarget)?.EventCounts;
            }

            if (eventStatsOnOrchestrator == null)
            {
                Logger.Log.DistributionEventStatsNotFound(m_appLoggingContext, $"Orchestrator for {Name}");
                return;
            }

            // Event stats count length on worker and orchestrator should be same
            Contract.Assert(workerExitResponse.EventCounts.Count == eventStatsOnOrchestrator.Length,
                "Event stats count length on worker and orchestrator are different");
            // Length must be larger than ExecutionEventId.MaxValue to avoid index out of range
            Contract.Assert(workerExitResponse.EventCounts.Count > (int)EnumTraits<ExecutionEventId>.MaxValue);

            var mismatchBuilder = new StringBuilder();
            foreach (ExecutionEventId eventId in Enum.GetValues(typeof(ExecutionEventId)))
            {
                var workerCount = workerExitResponse.EventCounts[(int)eventId];
                var orchestratorCount = eventStatsOnOrchestrator[(int)eventId];
                if (workerCount != orchestratorCount)
                {
                    mismatchBuilder.AppendLine($"{eventId}: workerCount = {workerCount}, orchestratorCount = {orchestratorCount}");
                }
            }

            if (mismatchBuilder.Length > 0)
            {
                Logger.Log.DistributionEventStatsNotMatch(m_appLoggingContext, Name, mismatchBuilder.ToString());
            }
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

        private sealed class PipCompletionTask
        {
            public readonly RunnablePip RunnablePip;
            public readonly PipExecutionStep Step;

            public Pip Pip => RunnablePip.Pip;

            public readonly OperationContext OperationContext;
            public readonly TaskSourceSlim<ExecutionResult> Completion;
            public readonly DateTime QueuedTime;
            public TimeSpan GrpcDuration { get; private set; }
            public TimeSpan QueueRequestDuration { get; private set; }

            public long? ExecuteStepTicks { get; private set; }

            public long? QueueTicks { get; private set; }

            public PipCompletionTask(OperationContext operationContext, RunnablePip pip)
            {
                Step = pip.Step;
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
                AddGrpcDuration(sendDuration);
            }

            internal void AddGrpcDuration(TimeSpan grpcDuration)
            {
                GrpcDuration += grpcDuration;
            }
        }
    }
}