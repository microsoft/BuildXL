// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.ContentStore.Distributed.MetadataService;

/// <summary>
/// This interface represents the event stream for GCS. We use this to persist events to the event stream during normal
/// operation and to read events during crash recovery.
/// </summary>
public interface IContentMetadataEventStream: IStartupShutdownSlim
{
    /// <summary>
    /// Pause and resume logging.
    /// </summary>
    public void Toggle(bool active);

    /// <summary>
    /// Forces logs to be written into persistent storage.
    /// </summary>
    public Task<CheckpointLogId> CompleteOrChangeLogAsync(OperationContext context, CheckpointLogId? newLogId = null, bool moveToNextLog = false);

    /// <summary>
    /// Writes a single event to the log.
    /// </summary>
    public Task<bool> WriteEventAsync(OperationContext context, ServiceRequestBase request);

    /// <summary>
    /// Reads events from the log starting at the given log ID and calls the given handleEvent function for each event
    /// in the log. Calls will be performed and waited for in order as we sequentially read the log. This is used to
    /// allow recovery to be performed.
    /// </summary>
    public Task<Result<CheckpointLogId>> ReadEventsAsync(
        OperationContext context,
        CheckpointLogId logId,
        Func<ServiceRequestBase, ValueTask> handleEvent);

    /// <summary>
    /// Ensures the currently running block is commited to the log, moves forward the log, and then returns the old log
    /// ID for persistence during checkpointing.
    /// </summary>
    public Task<Result<CheckpointLogId>> BeforeCheckpointAsync(OperationContext context);

    /// <summary>
    /// Garbage collects all logs before the given log ID. This is done because we assume that the given log ID has
    /// been safely checkpointed.
    /// </summary>
    public Task<BoolResult> AfterCheckpointAsync(OperationContext context, CheckpointLogId logId);

    /// <summary>
    /// Removes all information in the log. This is used only in the case in which we start the service after a very
    /// long time of it not running and we want to start fresh.
    /// </summary>
    public Task<BoolResult> ClearAsync(OperationContext context);
}
