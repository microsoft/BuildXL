// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Tracing;

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
        FinalReportExecutionLogDuration,

        /// <nodoc/>
        [CounterType(CounterType.Stopwatch)]
        FinalReportPipResultsDuration,

        /// <nodoc/>
        ForAllPipsGrpcDurationMs,

        /// <nodoc/>
        [CounterType(CounterType.Stopwatch)]
        PrintFinishedLogsDuration,

        /// <nodoc/>
        [CounterType(CounterType.Stopwatch)]
        GetPipResultsDescriptionDuration,

        /// <nodoc/>
        ExecutionLogSentSize
    }
}
