// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler.WorkDispatcher;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Threading;

namespace BuildXL.Scheduler.Distribution
{
    /// <summary>
    /// Handles choose worker computation for <see cref="Scheduler"/>
    /// </summary>
    internal abstract class ChooseWorkerContext
    {
        /// <summary>
        /// The number of times a pip was blocked from acquiring a worker due to resource limits
        /// </summary>
        public int ChooseBlockedCount;

        /// <summary>
        /// The number of times a pip successfully acquired a worker
        /// </summary>
        public int ChooseSuccessCount;

        protected readonly LoggingContext LoggingContext;

        protected readonly IPipQueue PipQueue;

        protected readonly IReadOnlyList<Worker> Workers;

        /// <summary>
        /// Tracks a sequence number in order to verify if workers resources have changed since
        /// the time it was checked. This is used by ChooseWorker to decide if the worker queue can
        /// be paused
        /// </summary>
        protected int WorkerEnableSequenceNumber = 0;

        /// <summary>
        /// Whether the current BuildXL instance serves as a master node in the distributed build and has workers attached.
        /// </summary>
        protected bool AnyRemoteWorkers => Workers.Count > 1;

        protected readonly LocalWorker LocalWorker;

        protected readonly DispatcherKind Kind;

        protected readonly int MaxParallelDegree;

        private readonly ReadWriteLock m_chooseWorkerTogglePauseLock = ReadWriteLock.Create();

        protected ChooseWorkerContext(
            LoggingContext loggingContext,
            IReadOnlyList<Worker> workers,
            IPipQueue pipQueue,
            DispatcherKind kind,
            int maxParallelDegree)
        {
            Workers = workers;
            PipQueue = pipQueue;
            LocalWorker = (LocalWorker)workers[0];
            LoggingContext = loggingContext;
            Kind = kind;
            MaxParallelDegree = maxParallelDegree;

            foreach (var worker in Workers)
            {
                worker.ResourcesChanged += OnWorkerResourcesChanged;
            }
        }

        public async Task<Worker> ChooseWorkerAsync(RunnablePip runnablePip)
        {
            var worker = await ChooseWorkerCore(runnablePip);

            if (worker == null)
            {
                runnablePip.IsWaitingForWorker = true;
                Interlocked.Increment(ref ChooseBlockedCount);

                // Attempt to pause the choose worker queue since resources are not available
                TogglePauseChooseWorkerQueue(pause: true, blockedPip: runnablePip);
            }
            else
            {
                runnablePip.IsWaitingForWorker = false;
                Interlocked.Increment(ref ChooseSuccessCount);

                // Ensure the queue is unpaused if we managed to choose a worker
                TogglePauseChooseWorkerQueue(pause: false);
            }

            return worker;
        }

        /// <summary>
        /// Choose a worker
        /// </summary>
        protected abstract Task<Worker> ChooseWorkerCore(RunnablePip runnablePip);

        protected bool MustRunOnMaster(RunnablePip runnablePip)
        {
            if (!AnyRemoteWorkers)
            {
                return true;
            }

            return runnablePip.PipType == PipType.Ipc && ((IpcPip)runnablePip.Pip).MustRunOnMaster;
        }

        protected void TogglePauseChooseWorkerQueue(bool pause, RunnablePip blockedPip = null)
        {
            Contract.Requires(pause == (blockedPip != null), "Must specify blocked pip if and only if pausing the choose worker queue");

            if (pause)
            {
                using (m_chooseWorkerTogglePauseLock.AcquireWriteLock())
                {
                    // Compare with the captured sequence number before the pip re-entered the queue
                    // to avoid race conditions where pip cannot acquire worker resources become available then queue is paused
                    // potentially indefinitely (not likely but theoretically possilbe)
                    if (Volatile.Read(ref WorkerEnableSequenceNumber) == blockedPip.ChooseWorkerSequenceNumber)
                    {
                        SetQueueMaxParallelDegree(0);
                    }
                }
            }
            else
            {
                using (m_chooseWorkerTogglePauseLock.AcquireReadLock())
                {
                    // Update the sequence number. This essentially is called for every increase in resources
                    // and successful acquisition of workers to track changes in resource state that invalidate
                    // decision to pause choose worker queue.
                    Interlocked.Increment(ref WorkerEnableSequenceNumber);

                    // Unpause the queue
                    SetQueueMaxParallelDegree(MaxParallelDegree);
                }
            }
        }

        private void OnWorkerResourcesChanged(Worker worker, WorkerResource resourceKind, bool increased)
        {
            if (increased)
            {
                TogglePauseChooseWorkerQueue(pause: false);
            }
        }

        private void SetQueueMaxParallelDegree(int maxConcurrency)
        {
            PipQueue.SetMaxParallelDegreeByKind(Kind, maxConcurrency);
        }
    }
}
