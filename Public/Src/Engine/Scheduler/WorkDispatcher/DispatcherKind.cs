// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Scheduler.WorkDispatcher
{
    /// <summary>
    /// Dispatcher queue kinds
    /// </summary>
    public enum DispatcherKind
    {
        /// <summary>
        /// None as a default kind
        /// </summary>
        None,

        /// <summary>
        /// IO queue for IO-bound operations
        /// </summary>
        /// <remarks>
        /// The queue where CopyFile and WriteFile pips run
        /// </remarks>
        IO,

        /// <summary>
        /// CPU queue for CPU-bound operations
        /// </summary>
        /// <remarks>
        /// The queue where SealDirectory, Process (if pipelining disabled), meta pips, process executions (if pipelining enabled) run.
        /// </remarks>
        CPU,

        /// <summary>
        /// Light queue for lightweight operations
        /// </summary>
        Light,

        /// <summary>
        /// Ipc queue for Ipc pips
        /// </summary>
        IpcPips,

        /// <summary>
        /// Cache lookup queue
        /// </summary>
        /// <remarks>
        /// The queue where process pips do cache lookup.
        /// </remarks>
        CacheLookup,

        /// <summary>
        /// Choose worker queue for CPU queue
        /// </summary>
        ChooseWorkerCpu,

        /// <summary>
        /// Choose worker queue for CacheLookup queue
        /// </summary>
        ChooseWorkerCacheLookup,

        /// <summary>
        /// Choose worker queue for IPC pips
        /// </summary>
        ChooseWorkerIpc,

        /// <summary>
        /// Choose worker queue to find a worker to execute light process pips
        /// </summary>
        ChooseWorkerLight,

        /// <summary>
        /// Delayed cache lookup queue
        /// </summary>
        /// <remarks>
        /// If the delayed cache lookup feature is enabled, pips are parked in this queue
        /// if ChooseWorkerCpu already contains enough pips waiting.
        /// </remarks>        
        DelayedCacheLookup,

        /// <summary>
        /// Queue for input/output materialization
        /// </summary>
        Materialize,
    }

    /// <summary>
    /// Extension methods for <see cref="DispatcherKind"/>
    /// </summary>
    public static class DispatcherKindHelpers
    {
        /// <summary>
        /// Whether this dispatcher represents a logic for choosing a worker.
        /// </summary>
        public static bool IsChooseWorker(this DispatcherKind kind)
        {
            return kind == DispatcherKind.ChooseWorkerLight || kind == DispatcherKind.ChooseWorkerCacheLookup || kind == DispatcherKind.ChooseWorkerCpu || kind == DispatcherKind.ChooseWorkerIpc;
        }
    
    }
}
