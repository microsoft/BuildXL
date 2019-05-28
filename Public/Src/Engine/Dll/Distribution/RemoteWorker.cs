// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Engine.Cache.Artifacts;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Engine.Distribution.OpenBond;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Distribution;
using BuildXL.Scheduler.Graph;
using BuildXL.Scheduler.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
using static BuildXL.Utilities.FormattableStringEx;
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
        private readonly MasterService m_masterService;

        private readonly ServiceLocation m_serviceLocation;

        private int m_status;
        private BondContentHash m_cacheValidationContentHash;
        private int m_nextSequenceNumber;
        private bool m_everAvailable;
        private PipGraph m_pipGraph;

        /// <summary>
        /// Indicates failure which should cause the worker build to fail. NOTE: This may not correspond to the
        /// entire distributed build failing. Namely, connection failures for operations materialize outputs
        /// will not fail the build but will fail the worker to ensure following steps on workers do not continue.
        /// </summary>
        private string m_exitFailure;

        public override WorkerNodeStatus Status => (WorkerNodeStatus)m_status;

        public override bool EverAvailable => Volatile.Read(ref m_everAvailable);

        private readonly TaskSourceSlim<bool> m_attachCompletion;
        private readonly TaskSourceSlim<bool> m_executionBlobCompletion;
        private BlockingCollection<WorkerNotificationArgs> m_executionBlobQueue = new BlockingCollection<WorkerNotificationArgs>(new ConcurrentQueue<WorkerNotificationArgs>());

        private readonly Thread m_sendThread;
        private readonly BlockingCollection<ValueTuple<PipCompletionTask, SinglePipBuildRequest>> m_buildRequests;
        private readonly int m_maxMessagesPerBatch = EngineEnvironmentSettings.MaxMessagesPerBatch.Value;

        private readonly bool m_isGrpcEnabled;
        private readonly IWorkerClient m_workerClient;

        #region Distributed execution log state

        private IExecutionLogTarget m_workerExecutionLogTarget;
        private BinaryLogReader m_executionLogBinaryReader;
        private MemoryStream m_executionLogBufferStream;
        private ExecutionLogFileReader m_executionLogReader;
        private readonly SemaphoreSlim m_logBlobMutex = TaskUtilities.CreateMutex();
        private readonly object m_hashListLock = new object();

        private int m_lastBlobSeqNumber = -1;

        #endregion Distributed execution log state

        /// <summary>
        /// Constructor
        /// </summary>
        public RemoteWorker(
            bool isGrpcEnabled,
            LoggingContext appLoggingContext,
            uint workerId,
            MasterService masterService,
            ServiceLocation serviceLocation)
            : base(workerId, name: I($"#{workerId} ({serviceLocation.IpAddress}::{serviceLocation.Port})"))
        {
            m_isGrpcEnabled = isGrpcEnabled;
            m_appLoggingContext = appLoggingContext;
            m_masterService = masterService;
            m_buildRequests = new BlockingCollection<ValueTuple<PipCompletionTask, SinglePipBuildRequest>>();
            m_attachCompletion = TaskSourceSlim.Create<bool>();
            m_executionBlobCompletion = TaskSourceSlim.Create<bool>();

            m_serviceLocation = serviceLocation;

            if (isGrpcEnabled)
            {
                m_workerClient = new Grpc.GrpcWorkerClient(m_appLoggingContext, masterService.DistributionServices.BuildId, serviceLocation.IpAddress, serviceLocation.Port, OnConnectionTimeOutAsync);
            }
            else
            {
#if !DISABLE_FEATURE_BOND_RPC
                m_workerClient = new InternalBond.BondWorkerClient(m_appLoggingContext, Name, serviceLocation.IpAddress, serviceLocation.Port, masterService.DistributionServices, OnActivateConnection, OnDeactivateConnection, OnConnectionTimeOutAsync);
#endif
            }

            // Depending on how long send requests take. It might make sense to use the same thread between all workers. 
            m_sendThread = new Thread(SendBuildRequests);
        }

        public override void Initialize(PipGraph pipGraph, IExecutionLogTarget executionLogTarget)
        {
            m_pipGraph = pipGraph;
            m_workerExecutionLogTarget = executionLogTarget;
            base.Initialize(pipGraph, executionLogTarget);
        }

        public override int WaitingBuildRequestsCount => m_buildRequests.Count;

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

                using (m_masterService.Environment.Counters.StartStopwatch(PipExecutorCounter.RemoteWorker_PrepareAndSendBuildRequestsDuration))
                {
                    m_pipCompletionTaskList.Clear();
                    m_buildRequestList.Clear();
                    m_hashList.Clear();

                    m_pipCompletionTaskList.Add(firstItem.Item1);
                    m_buildRequestList.Add(firstItem.Item2);

                    while (m_buildRequestList.Count < m_maxMessagesPerBatch && m_buildRequests.TryTake(out var item))
                    {
                        m_pipCompletionTaskList.Add(item.Item1);
                        m_buildRequestList.Add(item.Item2);
                    }

                    using (m_masterService.Environment.Counters.StartStopwatch(PipExecutorCounter.RemoteWorker_ExtractHashesDuration))
                    {
                        Parallel.ForEach(m_pipCompletionTaskList, (task) =>
                        {
                            ExtractHashes(task.RunnablePip, m_hashList);
                        });
                    }

                    var dateTimeBeforeSend = DateTime.UtcNow;
                    TimeSpan sendDuration;
                    RpcCallResult<Unit> callResult;

                    using (var watch = m_masterService.Environment.Counters.StartStopwatch(PipExecutorCounter.RemoteWorker_BuildRequestSendDuration))
                    {
                        callResult = m_workerClient.ExecutePipsAsync(new PipBuildRequest
                        {
                            Pips = m_buildRequestList,
                            Hashes = m_hashList
                        },
                        m_pipCompletionTaskList.Select(a => a.Pip.SemiStableHash).ToList()).GetAwaiter().GetResult();

                        sendDuration = watch.Elapsed;
                    }

                    if (callResult.State == RpcCallResultState.Failed)
                    {
                        foreach (var task in m_pipCompletionTaskList)
                        {
                            FailRemotePip(
                                task,
                                callResult.LastFailure?.DescribeIncludingInnerFailures() ?? "Unknown");
                        }

                        // TODO: We could not send the hashes; so it is hard to determine what files and directories are added to AvailableHashes.
                        // That's why, for correctness, we clear the AvailableHashes all together. 
                        // This seems to be very inefficient; but it is so rare that we completely fail to send the build request to the worker after retries.
                        ResetAvailableHashes(m_pipGraph);

                        // Change status on connection failure
                        // Try to pause so next pips will skip this worker.
                        ChangeStatus(WorkerNodeStatus.Running, WorkerNodeStatus.Paused);
                        m_masterService.Environment.Counters.IncrementCounter(PipExecutorCounter.BuildRequestBatchesFailedSentToWorkers);
                    }
                    else
                    {
                        m_masterService.Environment.Counters.IncrementCounter(PipExecutorCounter.BuildRequestBatchesSentToWorkers);
                        m_masterService.Environment.Counters.AddToCounter(PipExecutorCounter.HashesSentToWorkers, m_hashList.Count);

                        foreach (var task in m_pipCompletionTaskList)
                        {
                            task.SetRequestDuration(dateTimeBeforeSend, sendDuration);
                        }
                    }
                }
            }
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("AsyncUsage", "AsyncFixer02:awaitinsteadofwait")]
        public async Task LogExecutionBlobAsync(WorkerNotificationArgs notification)
        {
            Contract.Requires(notification.ExecutionLogData != null || notification.ExecutionLogData.Count != 0);

            m_executionBlobQueue.Add(notification);

            // After we put the executionBlob in a queue, we can unblock the caller and give an ACK to the worker.
            await Task.Yield();

            // Execution log events cannot be logged by multiple threads concurrently since they must be ordered
            using (await m_logBlobMutex.AcquireAsync())
            {
                // We need to dequeue and process the blobs in order. 
                // Here, we do not necessarily process the blob that is just added to the queue above.
                // There might be another thread that adds the next blob to the queue after the current thread, 
                // and that thread might acquire the lock earlier. 

                WorkerNotificationArgs executionBlobNotification = null;
                Contract.Assert(m_executionBlobQueue.TryTake(out executionBlobNotification), "The executionBlob queue cannot be empty");
                
                int blobSequenceNumber = executionBlobNotification.ExecutionLogBlobSequenceNumber;
                ArraySegment<byte> executionLogBlob = executionBlobNotification.ExecutionLogData;

                if (m_workerExecutionLogTarget == null)
                {
                    return;
                }

                try
                {
                    // Workers send execution log blobs one-at-a-time, waiting for a response from the master between each message.
                    // A sequence number higher than the last logged blob sequence number indicates a worker sent a subsequent blob without waiting for a response.
                    Contract.Assert(blobSequenceNumber <= m_lastBlobSeqNumber + 1, "Workers should not send a new execution log blob until receiving a response from the master for all previous blobs.");

                    // Due to network latency and retries, it's possible to receive a message multiple times.
                    // Ignore any low numbered blobs since they should have already been logged and ack'd at some point before
                    // the worker could send a higher numbered blob.
                    if (blobSequenceNumber != m_lastBlobSeqNumber + 1)
                    {
                        return;
                    }

                    if (m_executionLogBufferStream == null)
                    {
                        // Create the stream on demand, because we need to pass the BinaryLogReader stream with the header bytes in order
                        // to correctly deserialize events
                        m_executionLogBufferStream = new MemoryStream();
                    }

                    // Write the new execution log event content into buffer starting at beginning of buffer stream
                    m_executionLogBufferStream.SetLength(0);
                    m_executionLogBufferStream.Write(executionLogBlob.Array, executionLogBlob.Offset, executionLogBlob.Count);

                    // Reset the buffer stream to beginning and reset reader to ensure it reads events starting from beginning
                    m_executionLogBufferStream.Position = 0;

                    if (m_executionLogBinaryReader == null)
                    {
                        m_executionLogBinaryReader = new BinaryLogReader(m_executionLogBufferStream, m_masterService.Environment.Context);
                        m_executionLogReader = new ExecutionLogFileReader(m_executionLogBinaryReader, m_workerExecutionLogTarget);
                    }

                    m_executionLogBinaryReader.Reset();

                    // Read all events into worker execution log target
                    if (!m_executionLogReader.ReadAllEvents())
                    {
                        Logger.Log.DistributionCallMasterCodeException(m_appLoggingContext, nameof(LogExecutionBlobAsync), "Failed to read all worker events");
                        // Disable further processing of execution log since an error was encountered during processing
                        m_workerExecutionLogTarget = null;
                    }
                    else
                    {
                        m_lastBlobSeqNumber = blobSequenceNumber;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log.DistributionCallMasterCodeException(m_appLoggingContext, nameof(LogExecutionBlobAsync), ex.ToStringDemystified() + Environment.NewLine
                                                                    + "Message sequence number: " + blobSequenceNumber
                                                                    + " Last sequence number logged: " + m_lastBlobSeqNumber);
                    // Disable further processing of execution log since an exception was encountered during processing
                    m_workerExecutionLogTarget = null;
                }
            }

            if (m_executionBlobQueue.IsCompleted)
            {
                m_executionBlobCompletion.TrySetResult(true);
            }
        }

        private async void OnConnectionTimeOutAsync(object sender, EventArgs e)
        {           
            // Unblock caller to make it a fire&forget event handler.
            await Task.Yield();

            // Stop using the worker if the connect times out
            while (!ChangeStatus(Status, WorkerNodeStatus.Stopped) && Status != WorkerNodeStatus.Stopped)
            {
            }
        }

        private void OnDeactivateConnection(object sender, EventArgs e)
        {
            ChangeStatus(WorkerNodeStatus.Running, WorkerNodeStatus.Paused);
        }

        private void OnActivateConnection(object sender, EventArgs e)
        {
            ChangeStatus(WorkerNodeStatus.Paused, WorkerNodeStatus.Running);
        }

        /// <inheritdoc/>
        public override void Dispose()
        {
            m_executionLogBinaryReader?.Dispose();
            m_workerClient?.Dispose();
            
            base.Dispose();
        }

        /// <inheritdoc />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("AsyncUsage", "AsyncFixer03:FireForgetAsyncVoid")]
        public override async void Start()
        {
            if (ChangeStatus(WorkerNodeStatus.NotStarted, WorkerNodeStatus.Starting))
            {
                var startData = new BuildStartData
                {
                    SessionId = m_appLoggingContext.Session.Id,
                    WorkerId = WorkerId,
                    CachedGraphDescriptor = m_masterService.CachedGraphDescriptor,
                    SymlinkFileContentHash = m_masterService.SymlinkFileContentHash.ToBondContentHash(),
                    FingerprintSalt = m_masterService.Environment.ContentFingerprinter.FingerprintSalt,
                    MasterLocation = new ServiceLocation
                    {
                        IpAddress = Dns.GetHostName(),
                        Port = m_masterService.Port,
                    },
                    EnvironmentVariables = m_masterService.Environment.State.PipEnvironment
                        .FullEnvironmentVariables.ToDictionary().ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                };

                var callResult = await m_workerClient.AttachAsync(startData);

                if (callResult.State != RpcCallResultState.Succeeded)
                {
                    // Change to stopped state since we failed to attach
                    ChangeStatus(WorkerNodeStatus.Starting, WorkerNodeStatus.Stopped);
                }
                else
                {
                    // Change to started state so we know the worker is connected
                    ChangeStatus(WorkerNodeStatus.Starting, WorkerNodeStatus.Started);
                }
            }
        }

        /// <inheritdoc />
        public override async Task FinishAsync(string buildFailure)
        {
            m_buildRequests.CompleteAdding();
            if (m_sendThread.IsAlive)
            {
                m_sendThread.Join();
            }

            bool initiatedStop = false;
            while (true)
            {
                WorkerNodeStatus status = Status;
                if (status == WorkerNodeStatus.Stopping || status == WorkerNodeStatus.Stopped)
                {
                    break;
                }

                if (ChangeStatus(status, WorkerNodeStatus.Stopping))
                {
                    initiatedStop = true;
                    break;
                }
            }

            if (initiatedStop)
            {
                CancellationTokenSource exitCancellation = new CancellationTokenSource();

                // Only wait a short amount of time for exit (15 seconds) if worker is not successfully attached.
                if (m_attachCompletion.Task.Status != TaskStatus.RanToCompletion || !await m_attachCompletion.Task)
                {
                    exitCancellation.CancelAfter(TimeSpan.FromSeconds(15));
                }

                var buildEndData = new BuildEndData()
                {
                    Failure = buildFailure ?? m_exitFailure
                };

                await m_workerClient.ExitAsync(buildEndData, exitCancellation.Token);

                m_executionBlobQueue.CompleteAdding();

                using (m_masterService.Environment.Counters.StartStopwatch(PipExecutorCounter.RemoteWorker_AwaitExecutionBlobCompletionDuration))
                {
                    if (!m_executionBlobQueue.IsCompleted)
                    {
                        // Wait for execution blobs to be processed.
                        await m_executionBlobCompletion.Task;
                    }
                }

                ChangeStatus(WorkerNodeStatus.Stopping, WorkerNodeStatus.Stopped);
            }
        }

        /// <inheritdoc />
        public override async Task<PipResultStatus> MaterializeInputsAsync(RunnablePip runnablePip)
        {
            var result = await ExecutePipRemotely(runnablePip);
            return result.Result;
        }

        /// <inheritdoc />
        public override async Task<PipResultStatus> MaterializeOutputsAsync(RunnablePip runnablePip)
        {
            var result = await ExecutePipRemotely(runnablePip);
            return result.Result;
        }

        /// <inheritdoc />
        public override async Task<ExecutionResult> ExecuteProcessAsync(ProcessRunnablePip runnablePip)
        {
            var result = await ExecutePipRemotely(runnablePip);
            return result;
        }

        /// <inheritdoc />
        public override async Task<PipResult> ExecuteIpcAsync(RunnablePip runnable)
        {
            var result = await ExecutePipRemotely(runnable);
            return runnable.CreatePipResult(result.Result);
        }

        /// <inheritdoc />
        public override async Task<RunnableFromCacheResult> CacheLookupAsync(
            ProcessRunnablePip runnablePip,
            PipExecutionState.PipScopeState state,
            CacheableProcess cacheableProcess)
        {
            ExecutionResult result = await ExecutePipRemotely(runnablePip);
            if (result.Result.IndicatesFailure())
            {
                return null;
            }

            return PipExecutor.TryConvertToRunnableFromCacheResult(
                runnablePip.OperationContext,
                runnablePip.Environment,
                state,
                cacheableProcess,
                result);
        }

        /// <inheritdoc />
        public override async Task<ExecutionResult> PostProcessAsync(ProcessRunnablePip runnablePip)
        {
            var result = await ExecutePipRemotely(runnablePip);
            return result;
        }

        /// <summary>
        /// Executes a pip remotely
        /// </summary>
        private async Task<ExecutionResult> ExecutePipRemotely(RunnablePip runnablePip)
        {
            using (var operationContext = runnablePip.OperationContext.StartAsyncOperation(PipExecutorCounter.ExecutePipRemotelyDuration))
            using (OnPipExecutionStarted(runnablePip, operationContext))
            {
                // Send the pip to the remote machine
                await SendToRemote(operationContext, runnablePip);

                // Wait for result from remote matchine
                ExecutionResult result = await AwaitRemoteResult(operationContext, runnablePip);

                using (operationContext.StartOperation(PipExecutorCounter.HandleRemoteResultDuration))
                {
                    // Process the remote result
                    HandleRemoteResult(runnablePip, result);
                }

                return result;
            }
        }

        public async Task SendToRemote(OperationContext operationContext, RunnablePip runnable)
        {
            Contract.Assert(m_workerClient != null, "Calling SendToRemote before the worker is initialized");
            Contract.Assert(m_attachCompletion.IsValid, "Remote worker not started");

            var attachCompletionResult = await m_attachCompletion.Task;

            var environment = runnable.Environment;
            var pipId = runnable.PipId;
            var description = runnable.Description;
            var pip = runnable.Pip;
            var processRunnable = runnable as ProcessRunnablePip;
            var fingerprint = processRunnable?.CacheResult?.Fingerprint ?? ContentFingerprint.Zero;

            var pipCompletionTask = new PipCompletionTask(runnable.OperationContext, runnable);

            m_pipCompletionTasks.Add(pipId, pipCompletionTask);

            if (!attachCompletionResult)
            {
                FailRemotePip(
                    pipCompletionTask,
                    "Worker did not attach");
                return;
            }

            var pipBuildRequest = new SinglePipBuildRequest
            {
                ActivityId = operationContext.LoggingContext.ActivityId.ToString(),
                PipIdValue = pipId.Value,
                Fingerprint = fingerprint.Hash.ToBondFingerprint(),
                Priority = runnable.Priority,
                Step = (int)runnable.Step,
                ExpectedRamUsageMb = processRunnable?.ExpectedRamUsageMb,
                SequenceNumber = Interlocked.Increment(ref m_nextSequenceNumber),
            };

            m_buildRequests.Add(ValueTuple.Create(pipCompletionTask, pipBuildRequest));
        }

        private void ExtractHashes(RunnablePip runnable, List<FileArtifactKeyedHash> hashes)
        {
            var step = runnable.Step;
            bool requiresHashes = step == PipExecutionStep.MaterializeInputs
                || step == PipExecutionStep.MaterializeOutputs
                || step == PipExecutionStep.CacheLookup;
            if (!requiresHashes)
            {
                return;
            }

            var environment = runnable.Environment;
            bool materializingOutputs = step == PipExecutionStep.MaterializeOutputs;

            // The block below collects process input file artifacts and hashes
            // Currently there is no logic to keep from sending the same hashes twice
            // Consider a model where hashes for files are requested by worker
            using (var pooledFileSet = Pools.GetFileArtifactSet())
            using (var pooledDynamicFileMultiDirectoryMap = Pools.GetFileMultiDirectoryMap())
            {
                var pathTable = environment.Context.PathTable;
                var files = pooledFileSet.Instance;
                var dynamicFiles = pooledDynamicFileMultiDirectoryMap.Instance;

                using (m_masterService.Environment.Counters.StartStopwatch(PipExecutorCounter.RemoteWorker_CollectPipFilesToMaterializeDuration))
                {
                    environment.State.FileContentManager.CollectPipFilesToMaterialize(
                        isMaterializingInputs: !materializingOutputs,
                        pipTable: environment.PipTable,
                        pip: runnable.Pip,
                        files: files,
                        dynamicFileMap: dynamicFiles,

                        // Only send content which is not already on the worker.
                        // TryAddAvailableHash can return null if the artifact is dynamic file in which case
                        // the file cannot be added to the set due to missing index (note that we're using ContentTrackingSet as the underlying set).
                        // In such a case we decide to include the artifact during the collection.
                        shouldInclude: artifact => TryAddAvailableHash(artifact) ?? true,
                        shouldIncludeServiceFiles: servicePipId => TryAddAvailableHash(servicePipId) ?? true);
                }

                using (m_masterService.Environment.Counters.StartStopwatch(PipExecutorCounter.RemoteWorker_CreateFileArtifactKeyedHashDuration))
                {
                    // Now we have to consider both dynamicFiles map and files set so we union into the files set. If we only rely on files, then the following incorrect build can happen.
                    // Suppose that we have pip P that specifies D as an opaque output directory and D\f as an output file. Pip Q consumes D\f directly (not via directory dependency on D).
                    // Pip R consumes D. Suppose that the cache lookup's for Q and R happen on the same machine. Suppose that Q is processed first, TryAddAvailableHash(D/f)
                    // returns true because it's a declared output and it's added into the files set. Now, on processing R later, particularly in
                    // collecting the files of D to materialize, D\f is not included in the files set because TryAddAvailableHash(D/f) returns false, i.e., 
                    // it's a declared output and it's been added when processing Q. However, D/f is still populated to the dynamicFiles map.
                    files.UnionWith(dynamicFiles.Keys);

                    foreach (var file in files)
                    {
                        var fileMaterializationInfo = environment.State.FileContentManager.GetInputContent(file);
                        bool isDynamicFile = dynamicFiles.TryGetValue(file, out var dynamicDirectories) && dynamicDirectories.Count != 0;

                        var hash = new FileArtifactKeyedHash
                        {
                            RewriteCount = file.RewriteCount,
                            PathValue = file.Path.Value.Value,
                            PathString = isDynamicFile ? file.Path.ToString(pathTable) : null,
                        }.SetFileMaterializationInfo(pathTable, fileMaterializationInfo);

                        if (isDynamicFile)
                        {
                            hash.AssociatedDirectories = new List<BondDirectoryArtifact>();

                            foreach (var dynamicDirectory in dynamicDirectories)
                            {
                                hash.AssociatedDirectories.Add(new BondDirectoryArtifact
                                {
                                    // Path id of dynamic directory input can be sent to the remote worker because it appears in the pip graph, and thus in path table.
                                    DirectoryPathValue = dynamicDirectory.Path.RawValue,
                                    DirectorySealId = dynamicDirectory.PartialSealId,
                                    IsDirectorySharedOpaque = dynamicDirectory.IsSharedOpaque
                                });
                            }
                        }

                        lock (m_hashListLock)
                        {
                            hashes.Add(hash);
                        }
                    }
                }
            }
        }

        private void FailRemotePip(PipCompletionTask pipCompletionTask, string errorMessage)
        {
            var result = LogAndGetNetworkFailureResult(pipCompletionTask, errorMessage);

            pipCompletionTask.Set(result);
        }

        private ExecutionResult LogAndGetNetworkFailureResult(PipCompletionTask pipCompletionTask, string errorMessage)
        {
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
                    step: runnablePip.Step.AsString());

                // Return success result
                result = new ExecutionResult();
                result.SetResult(operationContext, PipResultStatus.NotMaterialized);
                result.Seal();
                pipCompletionTask.Set(result);
            }
            else
            {
                Logger.Log.DistributionExecutePipFailedNetworkFailure(
                    operationContext,
                    runnablePip.Description,
                    Name,
                    errorMessage: errorMessage,
                    step: runnablePip.Step.AsString());

                result = ExecutionResult.GetFailureNotRunResult(operationContext);
            }

            return result;
        }

        public async Task<ExecutionResult> AwaitRemoteResult(OperationContext operationContext, RunnablePip runnable)
        {
            var pipId = runnable.PipId;
            var pipType = runnable.PipType;

            Contract.Assert(m_pipCompletionTasks.ContainsKey(pipId), "RemoteWorker tried to await the result of a pip which it did not start itself");

            ExecutionResult executionResult = null;

            using (operationContext.StartOperation(PipExecutorCounter.AwaitRemoteResultDuration))
            {
                executionResult = (await m_pipCompletionTasks[pipId].Completion.Task).Value;
            }

            PipCompletionTask pipCompletionTask;
            m_pipCompletionTasks.TryRemove(pipId, out pipCompletionTask);

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
            var description = runnable.Description;
            var pip = runnable.Pip;
            var pipType = runnable.PipType;
            bool isExecuteStep = runnable.Step == PipExecutionStep.ExecuteProcess || runnable.Step == PipExecutionStep.ExecuteNonProcessPip;

            if (runnable.Step == PipExecutionStep.CacheLookup && executionResult.CacheLookupPerfInfo != null)
            {
                var perfInfo = executionResult.CacheLookupPerfInfo;
                runnable.Performance.SetCacheLookupPerfInfo(perfInfo);
                if (perfInfo.CacheMissType != PipCacheMissType.Invalid)
                {
                    environment.Counters.IncrementCounter((PipExecutorCounter)perfInfo.CacheMissType);
                }
            }

            if (isExecuteStep)
            {
                runnable.SetExecutionResult(executionResult);
            }

            if (executionResult.Result == PipResultStatus.Failed)
            {
                // Failure
                m_masterService.Environment.Counters.IncrementCounter(pip.PipType == PipType.Process ? PipExecutorCounter.ProcessPipsFailedRemotely : PipExecutorCounter.IpcPipsFailedRemotely);
                return;
            }

            if (!isExecuteStep)
            {
                return;
            }

            // Success
            if (pipType == PipType.Process)
            {
                m_masterService.Environment.Counters.IncrementCounter(PipExecutorCounter.ProcessPipsSucceededRemotely);

                // NOTE: Process outputs will be reported later during the PostProcess step.
            }
            else
            {
                Contract.Assert(pipType == PipType.Ipc);

                m_masterService.Environment.Counters.IncrementCounter(PipExecutorCounter.IpcPipsSucceededRemotely);

                // NOTE: Output content is reported for IPC but not Process because Process outputs will be reported
                // later during PostProcess because cache convergence can change which outputs for a process are used

                // Report the payload file of the IPC pip
                foreach (var (fileArtifact, fileInfo, pipOutputOrigin) in executionResult.OutputContent)
                {
                    environment.State.FileContentManager.ReportOutputContent(
                        operationContext,
                        description,
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
                // Or the worker might have been stopped due to the master termination.
                return;
            }

            m_cacheValidationContentHash = attachCompletionInfo.WorkerCacheValidationContentHash;
            TotalProcessSlots = attachCompletionInfo.MaxConcurrency;
            TotalMemoryMb = attachCompletionInfo.AvailableRamMb;

            var validateCacheSuccess = await ValidateCacheConnection();

            if (validateCacheSuccess)
            {
                ChangeStatus(WorkerNodeStatus.Attached, WorkerNodeStatus.Running);
                Volatile.Write(ref m_everAvailable, true);
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

                var pip = pipCompletionTask.Pip;
                var operationContext = pipCompletionTask.OperationContext;
                pipCompletionTask.SetDuration(pipCompletionData.ExecuteStepTicks, pipCompletionData.QueueTicks);

                var description = pipCompletionTask.RunnablePip.Description;
                int dataSize = pipCompletionData.ResultBlob != null ? (int)pipCompletionData.ResultBlob.Count : 0;
                m_masterService.Environment.Counters.AddToCounter(pip.PipType == PipType.Process ? PipExecutorCounter.ProcessExecutionResultSize : PipExecutorCounter.IpcExecutionResultSize, dataSize);

                ExecutionResult result = m_masterService.ResultSerializer.DeserializeFromBlob(
                    pipCompletionData.ResultBlob,
                    WorkerId);

                pipCompletionTask.Set(result);
            }
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

            if (toStatus == WorkerNodeStatus.Stopped || toStatus == WorkerNodeStatus.Stopping)
            {
                m_attachCompletion.TrySetResult(false);
            }
            else if (toStatus == WorkerNodeStatus.Attached)
            {
                m_attachCompletion.TrySetResult(true);
            }

            Logger.Log.DistributionWorkerChangedState(
                m_appLoggingContext,
                m_serviceLocation.IpAddress,
                m_serviceLocation.Port,
                fromStatus.ToString(),
                toStatus.ToString(),
                callerName);

            // In the case of a stop fail all pending pips and stop the timer.
            if (toStatus == WorkerNodeStatus.Stopped)
            {
                // The worker goes in a stopped state either by a scheduler request or because it lost connection with the
                // remote machine. Check which one applies.
                bool isLostConnection = fromStatus != WorkerNodeStatus.Stopping;
                Stop(isLostConnection);
            }

            OnStatusChanged();
            return true;
        }

        private void Stop(bool isLostConnection)
        {
            Analysis.IgnoreResult(m_workerClient.CloseAsync(), justification: "Okay to ignore close");

            // The worker goes in a stopped state either by a scheduler request or because it lost connection with the
            // remote machine. Check which one applies.

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

                var context = m_masterService.Environment.Context;

                foreach (PipCompletionTask pipCompletionTask in pipCompletionTasks)
                {
                    Func<ExecutionResult> executionResultFactory;

                    if (isLostConnection)
                    {
                        // Remember that we have an infrastructure error so we can report it at the end.
                        // Do not set the flag if the lost connection doesn't affect any pip execution.
                        m_masterService.HasInfrastructureFailures = true;
                        executionResultFactory = () =>
                        {
                            return LogAndGetNetworkFailureResult(pipCompletionTask, "No result was received and connection was lost");
                        };
                    }
                    else
                    {
                        // This one can happen if the user presses CTRL+C
                        executionResultFactory = () =>
                        {
                            var result = new ExecutionResult();
                            result.SetResult(m_appLoggingContext, PipResultStatus.Canceled);
                            result.Seal();
                            return result;
                        };
                    }

                    pipCompletionTask.Set(executionResultFactory);
                }
            }
        }

        private async Task<bool> ValidateCacheConnection()
        {
            var cacheValidationContentHash = m_cacheValidationContentHash.ToContentHash();

            IArtifactContentCache contentCache = m_masterService.Environment.Cache.ArtifactContentCache;
            var cacheValidationContentRetrieval = await contentCache.TryLoadAvailableContentAsync(new[]
            {
                cacheValidationContentHash,
            });

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
            public readonly TaskSourceSlim<Lazy<ExecutionResult>> Completion;
            public readonly DateTime QueuedTime;
            public TimeSpan SendRequestDuration { get; private set; }
            public TimeSpan QueueRequestDuration { get; private set; }

            public long? ExecuteStepTicks { get; private set; }

            public long? QueueTicks { get; private set; }

            public PipCompletionTask(OperationContext operationContext, RunnablePip pip)
            {
                RunnablePip = pip;
                OperationContext = operationContext;
                Completion = TaskSourceSlim.Create<Lazy<ExecutionResult>>();
                QueuedTime = DateTime.UtcNow;
            }

            public void SetDuration(long? executeStepTicks, long? queueTicks)
            {
                ExecuteStepTicks = executeStepTicks;
                QueueTicks = queueTicks;
            }

            public bool Set(ExecutionResult executionResult)
            {
                return Set(() => executionResult);
            }

            public bool Set(Func<ExecutionResult> executionResultFactory)
            {
                return Completion.TrySetResult(new Lazy<ExecutionResult>(executionResultFactory));
            }

            internal void SetRequestDuration(DateTime dateTimeBeforeSend, TimeSpan sendDuration)
            {
                QueueRequestDuration = dateTimeBeforeSend - QueuedTime;
                SendRequestDuration = sendDuration;
            }
        }
    }
}