// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Scheduler.WorkDispatcher;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// A dispatcher queue which processes work items from several priority queues inside.
    /// </summary>
    [SuppressMessage("Microsoft.Naming", "CA1711:IdentifiersShouldNotHaveIncorrectSuffix", Justification = "But it is a queue...")]
    public sealed class PipQueue : IPipQueue
    {
        /// <summary>
        /// Priority queues by kind
        /// </summary>
        private readonly Dictionary<DispatcherKind, DispatcherQueue> m_queuesByKind;

        private readonly ChooseWorkerQueue m_chooseWorkerCpuQueue;
        private readonly ChooseWorkerQueue m_chooseWorkerCacheLookupQueue;

        /// <summary>
        /// Task completion source that completes whenever there are applicable changes which require another dispatching iteration.
        /// </summary>
        private readonly ManualResetEventSlim m_hasAnyChange;

        /// <summary>
        /// Task completion source that completes if the cancellation is requested and there are no running tasks.
        /// </summary>
        private TaskCompletionSource<bool> m_hasAnyRunning;

        /// <summary>
        /// How many work items there are in the dispatcher as pending or actively running.
        /// </summary>
        private long m_numRunningOrQueued;

        /// <summary>
        /// Whether the queue can accept new external work items.
        /// </summary>
        /// <remarks>
        /// In distributed builds, new work items can come from external requests after draining is started (i.e., workers get requests from the master)
        /// In single machine builds, after draining is started, new work items are only scheduled from the items that are being executed, not external requests.
        /// </remarks>
        private bool m_isFinalized;

        /// <summary>
        /// Whether the queue is cancelled via Ctrl-C
        /// </summary>
        private bool m_isCancelled;

        private bool IsCancelled
        {
            get
            {
                return Volatile.Read(ref m_isCancelled);
            }
            set
            {
                Volatile.Write(ref m_isCancelled, value);
            }
        }

        /// <inheritdoc/>
        public int MaxProcesses => m_queuesByKind[DispatcherKind.CPU].MaxParallelDegree;

        /// <inheritdoc/>
        public int NumSemaphoreQueued => m_queuesByKind[DispatcherKind.ChooseWorkerCpu].NumRunning + m_queuesByKind[DispatcherKind.ChooseWorkerCpu].NumQueued;

        /// <inheritdoc/>
        public int TotalNumSemaphoreQueued => m_queuesByKind[DispatcherKind.ChooseWorkerCpu].NumRunning + m_queuesByKind[DispatcherKind.ChooseWorkerCpu].NumQueued;

        /// <inheritdoc/>
        public bool IsDraining { get; private set; }

        /// <summary>
        /// Whether the queue has been completely drained
        /// </summary>
        /// <returns>
        /// If there are no items running or pending in the queues, we need to check whether this pipqueue can accept new external work.
        /// If this is a worker, we cannot finish dispatcher because master can still send new work items to the worker.
        /// </returns>
        public bool IsFinished => IsCancelled || (Volatile.Read(ref m_numRunningOrQueued) == 0 && m_isFinalized);

        /// <inheritdoc/>
        public bool IsDisposed { get; private set; }

        /// <summary>
        /// See <see cref="ChooseWorkerQueue.FastChooseNextCount"/>
        /// </summary>
        internal long ChooseQueueFastNextCount => m_chooseWorkerCpuQueue.FastChooseNextCount;

        /// <summary>
        /// Run time of tasks in choose worker queue
        /// </summary>
        internal TimeSpan ChooseQueueRunTime => m_chooseWorkerCpuQueue.RunTime;

        private long m_triggerDispatcherCount;
        private long m_dispatcherLoopCount;
        private TimeSpan m_dispatcherLoopTime;

        /// <summary>
        /// Time spent in dispatcher loop
        /// </summary>
        public TimeSpan DispatcherLoopTime
        {
            get
            {
                return m_dispatcherLoopTime;
            }
        }

        /// <summary>
        /// Number of dispatcher loop iterations
        /// </summary>
        public long DispatcherIterations
        {
            get
            {
                return m_dispatcherLoopCount;
            }
        }

        /// <summary>
        /// Number of times dispatcher loop was triggered
        /// </summary>
        public long TriggerDispatcherCount
        {
            get
            {
                return m_triggerDispatcherCount;
            }
        }

        private readonly IScheduleConfiguration m_config;

        /// <summary>
        /// Creates instance
        /// </summary>
        public PipQueue(IScheduleConfiguration config)
        {
            Contract.Requires(config != null);

            m_config = config;

            // If adaptive IO is enabled, then start with the half of the maxIO.
            var ioLimit = config.AdaptiveIO ? (config.MaxIO + 1) / 2 : config.MaxIO;

            m_chooseWorkerCacheLookupQueue = new ChooseWorkerQueue(this, config.MaxChooseWorkerCacheLookup);
            m_chooseWorkerCpuQueue = new ChooseWorkerQueue(this, config.MaxChooseWorkerCpu);

            m_queuesByKind = new Dictionary<DispatcherKind, DispatcherQueue>()
                             {
                                 {DispatcherKind.IO, new DispatcherQueue(this, ioLimit)},
                                 {DispatcherKind.ChooseWorkerCacheLookup, m_chooseWorkerCacheLookupQueue},
                                 {DispatcherKind.CacheLookup, new DispatcherQueue(this, config.MaxCacheLookup)},
                                 {DispatcherKind.ChooseWorkerCpu, m_chooseWorkerCpuQueue},
                                 {DispatcherKind.CPU, new DispatcherQueue(this, config.MaxProcesses)},
                                 {DispatcherKind.Materialize, new DispatcherQueue(this, config.MaxMaterialize)},
                                 {DispatcherKind.Light, new DispatcherQueue(this, config.MaxLightProcesses)}
                             };

            m_hasAnyChange = new ManualResetEventSlim(initialState: true /* signaled */);

            Tracing.Logger.Log.PipQueueConcurrency(
                Events.StaticContext,
                ioLimit,
                config.MaxChooseWorkerCacheLookup,
                config.MaxCacheLookup,
                config.MaxChooseWorkerCpu,
                config.MaxProcesses,
                config.MaxMaterialize,
                config.MaxLightProcesses,
                config.MasterCacheLookupMultiplier.ToString(),
                config.MasterCpuMultiplier.ToString());
        }

        /// <inheritdoc/>
        public int GetNumRunningByKind(DispatcherKind kind) => m_queuesByKind[kind].NumRunning;

        /// <inheritdoc/>
        public int GetNumQueuedByKind(DispatcherKind kind) => m_queuesByKind[kind].NumQueued;

        /// <inheritdoc/>
        public int GetMaxParallelDegreeByKind(DispatcherKind kind) => m_queuesByKind[kind].MaxParallelDegree;

        /// <summary>
        /// Sets the max parallelism for the given queue
        /// </summary>
        public void SetMaxParallelDegreeByKind(DispatcherKind kind, int maxParallelDegree)
        {
            if (m_queuesByKind[kind].AdjustParallelDegree(maxParallelDegree) && maxParallelDegree > 0)
            {
                TriggerDispatcher();
            }
        }

        /// <summary>
        /// Drains the priority queues inside.
        /// </summary>
        /// <remarks>
        /// Returns a task that completes when queue is fully drained
        /// </remarks>
        public void DrainQueues()
        {
            Contract.Requires(!IsDraining, "PipQueue has already started draining.");
            Contract.Requires(!IsDisposed);
            IsDraining = true;

            while (!IsFinished)
            {
                var startTime = TimestampUtilities.Timestamp;
                Interlocked.Increment(ref m_dispatcherLoopCount);

                m_hasAnyChange.Reset();

                foreach (var queue in m_queuesByKind.Values)
                {
                    queue.StartTasks();
                }

                m_dispatcherLoopTime += TimestampUtilities.Timestamp - startTime;

                // We run another iteration if at least one of these is true:
                // (1) An item has been completed.
                // (2) A new item is added to the queue: Enqueue(...) is called.
                // (3) If there is no pip running or queued.
                // (4) When you change the limit of one of the queues.
                // (5) Cancelling pip

                if (!IsFinished)
                {
                    m_hasAnyChange.Wait();
                }
            }

            if (IsCancelled)
            {
                Contract.Assert(m_hasAnyRunning != null, "If cancellation is requested, the taskcompletionsource to keep track of running items cannot be null");

                // Make sure that all running tasks are completed.
                m_hasAnyRunning.Task.Wait();
            }

            IsDraining = false;
        }

        /// <inheritdoc/>
        public void Enqueue(RunnablePip runnablePip)
        {
            Contract.Requires(runnablePip != null);
            Contract.Assert(runnablePip.DispatcherKind != DispatcherKind.None, "RunnablePip should be associated with a dispatcher kind when it is enqueued");

            if (IsCancelled)
            {
                // If the cancellation is requested, do not enqueue.
                return;
            }

            m_queuesByKind[runnablePip.DispatcherKind].Enqueue(runnablePip);

            Interlocked.Increment(ref m_numRunningOrQueued);

            // Let the dispatcher know that there is a new work item enqueued.
            TriggerDispatcher();
        }

        /// <summary>
        /// Finalizes the dispatcher so that external work will not be scheduled
        /// </summary>
        /// <remarks>
        /// Pips that already exist in the queue can still schedule their dependents after we call this method.
        /// This method allows dispatcher to stop draining when there are no pips running or queued.
        /// </remarks>
        public void SetAsFinalized()
        {
            m_isFinalized = true;
            TriggerDispatcher();
        }

        /// <summary>
        /// Adjusts the max parallel degree of the IO dispatcher queue
        /// </summary>
        /// <remarks>
        /// We introduce another limit for the IO queue, which is 'currentMax'. CurrentMax specifies the max parallel degree for the IO queue.
        /// CurrentMax initially equals to (maxIO + 1)/2. Then, based on the machine resources, it will vary between 1 and maxIO (both inclusive) during the build.
        /// This method will be called every second to adjust the IO limit.
        /// </remarks>
        public void AdjustIOParallelDegree(PerformanceCollector.MachinePerfInfo machinePerfInfo)
        {
            if (!IsDraining || !m_config.AdaptiveIO)
            {
                return;
            }

            var ioDispatcher = m_queuesByKind[DispatcherKind.IO];
            int currentMax = ioDispatcher.MaxParallelDegree;

            // If numRunning is closer to the currentMax, consider increasing the limit based on the resource usage
            // We should not only look at the disk usage activity but also CPU, RAM as well because the pips running on the IO queue consume CPU and RAM resources as well.
            // TODO: Instead of looking at all disk usages, just look at the ones which are associated with the build files (both inputs and outputs).
            bool hasLowGlobalUsage = machinePerfInfo.CpuUsagePercentage < 90 &&
                                     machinePerfInfo.RamUsagePercentage < 90 &&
                                     machinePerfInfo.DiskUsagePercentages.All(a => a < 90);
            bool numRunningIsNearMax = ioDispatcher.NumRunning > currentMax * 0.8;

            if (numRunningIsNearMax && (currentMax < m_config.MaxIO) && hasLowGlobalUsage)
            {
                // The new currentMax will be the midpoint of currentMax and absoluteMax.
                currentMax = (m_config.MaxIO + currentMax + 1) / 2;

                ioDispatcher.AdjustParallelDegree(currentMax);
                TriggerDispatcher(); // After increasing the limit, trigger the dispatcher so that we can start new tasks.
            }
            else if (machinePerfInfo.DiskUsagePercentages.Any(a => a > 95))
            {
                // If any of the disks usage is higher than 95%, then decrease the limit.
                // TODO: Should we look at the CPU or MEM usage as well to decrease the limit?
                currentMax = (currentMax + 1) / 2;
                ioDispatcher.AdjustParallelDegree(currentMax);
            }

            // TODO: Right now, we only care about the disk active time. We should also take the avg disk queue length into account.
            // Average disk queue length is a product of disk transfers/sec (response X I/O) and average disk sec/transfer.
        }

        /// <inheritdoc />
        public void Cancel()
        {
            m_hasAnyRunning = new TaskCompletionSource<bool>();
            IsCancelled = true;
            TriggerDispatcher();
        }

        /// <summary>
        /// Disposes the dispatcher
        /// </summary>
        public void Dispose()
        {
            if (!IsDisposed)
            {
                foreach (var queue in m_queuesByKind.Values)
                {
                    queue.Dispose();
                }
            }

            m_chooseWorkerCpuQueue?.Dispose();
            m_chooseWorkerCacheLookupQueue?.Dispose();

            IsDisposed = true;
        }

        internal void DecrementRunningOrQueuedPips()
        {
            Interlocked.Decrement(ref m_numRunningOrQueued);

            // if cancellation is requested, check the number of running tasks.
            if (m_hasAnyRunning != null && m_queuesByKind.Sum(a => a.Value.NumRunning) == 0)
            {
                m_hasAnyRunning.TrySetResult(true);
            }

            TriggerDispatcher();
        }

        internal void TriggerDispatcher()
        {
            Interlocked.Increment(ref m_triggerDispatcherCount);
            m_hasAnyChange.Set();
        }
    }
}
