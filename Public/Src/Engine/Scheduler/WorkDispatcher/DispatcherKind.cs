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
        /// <remarks>
        /// The queue where service client pips run.
        /// </remarks>
        Light,

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
        /// Choose worker queue for cachelookup queue
        /// </summary>
        ChooseWorkerCacheLookup,

        /// <summary>
        /// The queue where sealdirectory pips are processed.
        /// </summary>
        SealDirs,

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
}
