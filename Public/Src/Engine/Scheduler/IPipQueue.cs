// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using BuildXL.Scheduler.WorkDispatcher;
using BuildXL.Utilities;
using NotNullAttribute=JetBrains.Annotations.NotNullAttribute;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// A dispatcher queue which processes work items from several priority queues inside.
    /// </summary>
    public interface IPipQueue : IDisposable
    {
        /// <summary>
        /// Gets the max degree of parallelism for the CPU queue
        /// </summary>
        [System.Diagnostics.Contracts.Pure]
        int MaxProcesses { get; }

        /// <summary>
        /// Whether the queue is now being drained
        /// </summary>
        [System.Diagnostics.Contracts.Pure]
        bool IsDraining { get; }

        /// <summary>
        /// Whether the queue has been completely drained
        /// </summary>
        [System.Diagnostics.Contracts.Pure]
        bool IsFinished { get; }

        /// <summary>
        /// Whether the queue has been disposed
        /// </summary>
        [System.Diagnostics.Contracts.Pure]
        bool IsDisposed { get; }

        /// <summary>
        /// The number of pips waiting for semaphore resources
        /// </summary>
        int NumSemaphoreQueued { get; }

        /// <summary>
        /// How many work items there are in the dispatcher as pending or actively running.
        /// </summary>
        long NumRunningOrQueued { get; }

        /// <summary>
        /// Gets the number of running pips in the given queue
        /// </summary>
        int GetNumRunningPipsByKind(DispatcherKind queueKind);

        /// <summary>
        /// Gets the number of acquired slots in the given queue
        /// </summary>
        int GetNumAcquiredSlotsByKind(DispatcherKind queueKind);

        /// <summary>
        /// Gets the number of queued (pending) pips in the given queue
        /// </summary>
        int GetNumQueuedByKind(DispatcherKind queueKind);

        /// <summary>
        /// Gets the number of running pips in the given queue
        /// </summary>
        int GetMaxParallelDegreeByKind(DispatcherKind queueKind);

        /// <summary>
        /// Check whether the given dispatcher kind uses the weight when acquiring slots
        /// </summary>
        bool IsUseWeightByKind(DispatcherKind kind);

        /// <summary>
        /// Sets the number of running pips in the given queue
        /// </summary>
        void SetMaxParallelDegreeByKind(DispatcherKind queueKind, int maxParallelDegree);

        /// <summary>
        /// Drains the queues
        /// </summary>
        void DrainQueues();

        /// <summary>
        /// Enqueues the given <see cref="RunnablePip"/>
        /// </summary>
        void Enqueue([NotNull]RunnablePip runnablePip);

        /// <summary>
        /// Finalizes the dispatcher so that external work will not be scheduled
        /// </summary>
        void SetAsFinalized();

        /// <summary>
        /// Cancels draining the queues
        /// </summary>
        void Cancel();

        /// <summary>
        /// Adjusts the concurrency limit for the IO queue
        /// </summary>
        void AdjustIOParallelDegree(PerformanceCollector.MachinePerfInfo machinePerfInfo);
        
        /// <summary>
        /// Sets the total number of process slots in the build.
        /// </summary>
        void SetTotalProcessSlots(int totalProcessSlots);
    }
}
