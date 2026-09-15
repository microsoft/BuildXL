// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Core;

namespace BuildXL.Engine.Distribution
{
    /// <summary>
    /// Counters related to distributed builds
    /// </summary>
    public enum DistributionCounter : ushort
    {
        /// <nodoc/>
        [CounterType(CounterType.Stopwatch)]
        ReportPipsCompletedDuration,

        /// <nodoc/>
        [CounterType(CounterType.Stopwatch)]
        SendEventMessagesDuration,

        /// <summary>
        /// Amount of lost workers
        /// </summary>
        LostClientConnections,

        /// <summary>
        /// Lost workers due to call deadline exceeded
        /// </summary>
        LostClientConnectionsDeadlineExceeded,

        /// <summary>
        /// Lost workers after failed reconnection attempt
        /// </summary>
        LostClientConnectionsReconnectionTimeout,

        /// <summary>
        /// Lost workers due to unrecoverable failure in communication
        /// </summary>
        /// <remarks>
        /// As of now, this amounts to mismatch in build ids
        /// </remarks>
        LostClientUnrecoverableFailure,

        /// <summary>
        /// Lost workers due to timeout before attachment
        /// </summary>
        LostClientAttachmentTimeout,

        /// <summary>
        /// Lost workers due to timing out waiting a pip result from the remote worker 
        /// </summary>
        LostClientRemotePipTimeout,

        /// <summary>
        /// The size of the ExecutionResult sent over the network for process pips
        /// </summary>
        ProcessExecutionResultSize,

        /// <summary>
        /// The size of the ExecutionResult sent over the network for ipc pips
        /// </summary>
        IpcExecutionResultSize,

        /// <summary>
        /// The total size of messages that are received
        /// </summary>
        ReceivedMessageSizeBytes,

        /// <summary>
        /// The total size of messages that are sent
        /// </summary>
        SentMessageSizeBytes,

        /// <summary>
        /// The number of build request messages that fail to be sent to worker.
        /// </summary>
        FailedSendPipBuildRequestCount,

        /// <summary>
        /// Time spent serializing the execution result of a pip request on worker
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        WorkerServiceResultSerializationDuration,

        /// <summary>
        /// Time spent flushing the execution log before sending pip results
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        WorkerFlushExecutionLogDuration,

        /// <summary>
        /// Time spent building the messages to be sent to the orchestrator
        /// </summary>
        [CounterType(CounterType.Stopwatch)]
        WorkerOutgoingMessageProcessingDuration,

        /// <nodoc/>
        BuildResultBatchesSentToOrchestrator,

        /// <nodoc/>
        BuildResultsSentToOrchestrator,

        /// <nodoc/>
        [CounterType(CounterType.Stopwatch)]
        ReportExecutionLogDuration,

        /// <nodoc/>
        FailedSendPipBuildRequestCallDurationMs,

        /// <nodoc/>
        SendPipBuildRequestCallDurationMs,

        /// <nodoc/>
        [CounterType(CounterType.Stopwatch)]
        ReportPipResultsDuration,

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
        RemoteWorker_EarlyReleaseDrainDurationMs,

        /// <nodoc/>
        [CounterType(CounterType.Stopwatch)]
        RemoteWorker_DeserializeFromBlobDuration,

        /// <nodoc/>
        [CounterType(CounterType.Stopwatch)]
        RemoteWorker_ReadBuildManifestEventsDuration,

        /// <nodoc/>
        [CounterType(CounterType.Stopwatch)]
        RemoteWorker_ReadExecutionLogAsyncDuration,

        /// <nodoc/>
        TotalGrpcDurationMs,

        /// <nodoc/>
        BuildRequestBatchesSentToWorkers,

        /// <nodoc/>
        BuildRequestBatchesFailedSentToWorkers,

        /// <nodoc/>
        BuildRequestBatchesExceededSizeLimit,

        /// <nodoc/>
        PipsForcedToRunOnOrchestratorDueToBuildRequestSize,

        /// <nodoc/>
        HashesSentToWorkers,

        /// <nodoc/>
        HashesForStringPathsSentToWorkers,

        /// <nodoc/>
        [CounterType(CounterType.Stopwatch)]
        PrintFinishedLogsDuration,

        /// <nodoc/>
        [CounterType(CounterType.Stopwatch)]
        GetPipResultsDescriptionDuration,

        /// <nodoc/>
        ExecutionLogSentSize,

        /// <summary>
        /// Number of reusable distribution buffers reset with a capacity of at most 64 KiB.
        /// </summary>
        DistributionBufferResetCapacity64KBOrLess,

        /// <summary>
        /// Number of reusable distribution buffers reset with a capacity greater than 64 KiB and at most 256 KiB.
        /// </summary>
        DistributionBufferResetCapacity256KBOrLess,

        /// <summary>
        /// Number of reusable distribution buffers reset with a capacity greater than 256 KiB and at most 1 MiB.
        /// </summary>
        DistributionBufferResetCapacity1MBOrLess,

        /// <summary>
        /// Number of reusable distribution buffers reset with a capacity greater than 1 MiB and at most 4 MiB.
        /// </summary>
        DistributionBufferResetCapacity4MBOrLess,

        /// <summary>
        /// Number of reusable distribution buffers reset with a capacity greater than 4 MiB.
        /// </summary>
        DistributionBufferResetCapacityGreaterThan4MB,

        /// <summary>
        /// Number of reusable distribution buffers replaced because their capacity exceeded the retention limit.
        /// </summary>
        DistributionBufferReplacementCount,

        /// <summary>
        /// Total backing-array capacity discarded when oversized distribution buffers were replaced.
        /// </summary>
        DistributionBufferDiscardedCapacityBytes,

        /// <summary>
        /// Number of oversized execution-log event buffers replaced.
        /// </summary>
        ExecutionLogEventBufferReplacementCount,

        /// <summary>
        /// Total capacity discarded from oversized execution-log event buffers.
        /// </summary>
        ExecutionLogEventBufferDiscardedCapacityBytes,

        /// <summary>
        /// Number of oversized manifest event buffers replaced.
        /// </summary>
        ManifestEventBufferReplacementCount,

        /// <summary>
        /// Total capacity discarded from oversized manifest event buffers.
        /// </summary>
        ManifestEventBufferDiscardedCapacityBytes,

        /// <summary>
        /// Number of oversized flushed manifest event buffers replaced.
        /// </summary>
        FlushedManifestEventBufferReplacementCount,

        /// <summary>
        /// Total capacity discarded from oversized flushed manifest event buffers.
        /// </summary>
        FlushedManifestEventBufferDiscardedCapacityBytes,

        /// <summary>
        /// Number of oversized flushed execution-log buffers replaced.
        /// </summary>
        FlushedExecutionLogBufferReplacementCount,

        /// <summary>
        /// Total capacity discarded from oversized flushed execution-log buffers.
        /// </summary>
        FlushedExecutionLogBufferDiscardedCapacityBytes,

        /// <nodoc/>
        ConnectionManagerTimeout,

        /// <nodoc/>
        ConnectionManagerIdle,

        /// <nodoc/>
        ConnectionManagerFailedCalls,

        /// <nodoc/>
        LostClientHeartbeatFailure,

        /// <nodoc/>
        ConnectionManagerFailedHeartbeats,

        /// <nodoc/>
        [CounterType(CounterType.Stopwatch)]
        ReportInputsDuration,

        /// <nodoc/>
        [CounterType(CounterType.Stopwatch)]
        StartPipStepDuration,

        /// <nodoc/>
        NumProblematicWorkers,
    }
}
