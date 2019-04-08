// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Scheduler.WorkDispatcher
{
    /// <summary>
    /// Dispatcher queue which fires tasks and is managed by <see cref="PipQueue"/>.
    /// </summary>
    [SuppressMessage("Microsoft.Naming", "CA1711:IdentifiersShouldNotHaveIncorrectSuffix")]
    public class DispatcherQueue : IDisposable
    {
        private PriorityQueue<RunnablePip> m_queue = new PriorityQueue<RunnablePip>();
        private readonly PipQueue m_pipQueue;

        private int m_numRunning;
        private readonly object m_startTasksLock = new object();

        /// <summary>
        /// Maximum parallelism degree
        /// </summary>
        public int MaxParallelDegree { get; private set; }

        /// <summary>
        /// Number of tasks running now
        /// </summary>
        public int NumRunning => Volatile.Read(ref m_numRunning);

        /// <summary>
        /// Number of items waiting in the queue
        /// </summary>
        public int NumQueued => m_queue.Count;

        /// <summary>
        /// Maximum number of tasks run at the same since the queue has been started
        /// </summary>
        public int MaxRunning { get; private set; }

        /// <summary>
        /// Whether the dispatcher is disposed or not
        /// </summary>
        public bool IsDisposed { get; private set; }

        /// <summary>
        /// Constructor
        /// </summary>
        public DispatcherQueue(PipQueue pipQueue, int maxParallelDegree)
        {
            m_pipQueue = pipQueue;
            MaxParallelDegree = maxParallelDegree;
            m_numRunning = 0;
        }

        /// <summary>
        /// Enqueues the given runnable pip
        /// </summary>
        public void Enqueue(RunnablePip runnablePip)
        {
            Contract.Requires(!IsDisposed);

            m_queue.Enqueue(runnablePip.Priority, runnablePip);
        }

        /// <summary>
        /// Starts all tasks until the queue becomes empty or concurrency limit is satisfied
        /// </summary>
        public void StartTasks()
        {
            Contract.Requires(!IsDisposed);

            // Acquire the lock. This is needed because other threads (namely the ChooseWorker thread may call this
            // method).
            lock (m_startTasksLock)
            {
                RunnablePip runnablePip;
                while (MaxParallelDegree > NumRunning && Dequeue(out runnablePip))
                {
                    StartTask(runnablePip);
                }
            }
        }

        /// <summary>
        /// Dequeues from the priority queue
        /// </summary>
        private bool Dequeue(out RunnablePip runnablePip)
        {
            if (m_queue.Count != 0)
            {
                runnablePip = m_queue.Dequeue();
                return true;
            }

            runnablePip = null;
            return false;
        }

        /// <summary>
        /// Starts single task from the given runnable pip on the given dispatcher
        /// </summary>
        private void StartTask(RunnablePip runnablePip)
        {
            IncrementNumRunning();

            StartRunTaskAsync(runnablePip);
        }
        
        /// <summary>
        /// Runs pip asynchronously on a separate thread. This should not block the current
        /// thread on completion of the pip.
        /// </summary>
        [SuppressMessage("AsyncUsage", "AsyncFixer03:FireForgetAsyncVoid", Justification = "Fire and forget is intentional.")]
        protected virtual async void StartRunTaskAsync(RunnablePip runnablePip)
        {
            await Task.Yield();

            await RunCoreAsync(runnablePip);
        }

        /// <summary>
        /// Runs pip asynchronously
        /// </summary>
        protected async Task RunCoreAsync(RunnablePip runnablePip)
        {
            DispatcherReleaser releaser = new DispatcherReleaser(this);
            try
            {
                // Unhandled exceptions (Catastrophic BuildXL Failures) during a pip's execution will be thrown here without an AggregateException.
                await runnablePip.RunAsync(releaser);
            }
            finally
            {
                releaser.Release();
                m_pipQueue.DecrementRunningOrQueuedPips(); // Trigger dispatching loop in the PipQueue
            }
        }

        /// <summary>
        /// Release resource for a pip
        /// </summary>
        public void ReleaseResource()
        {
            Interlocked.Decrement(ref m_numRunning); // Decrease the number of running tasks in the current queue.
            m_pipQueue.TriggerDispatcher();
        }

        /// <summary>
        /// Disposes the queue
        /// </summary>
        public virtual void Dispose()
        {
            m_queue = null;
            IsDisposed = true;
        }

        private void IncrementNumRunning()
        {
            MaxRunning = Math.Max(MaxRunning, Interlocked.Increment(ref m_numRunning));
        }

        /// <summary>
        /// Adjust the max parallel degree to decrease or increase concurrency
        /// </summary>
        internal bool AdjustParallelDegree(int newParallelDegree)
        {
            if (MaxParallelDegree != newParallelDegree)
            {
                MaxParallelDegree = newParallelDegree;
                return true;
            }

            return false;
        }
    }
}
