// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Pips.Operations;

namespace BuildXL.Scheduler.WorkDispatcher
{
    /// <summary>
    /// Dispatcher queue which fires tasks and is managed by <see cref="PipQueue"/>.
    /// </summary>
    public class DispatcherQueue : IDisposable
    {
#if NET6_0_OR_GREATER
        private sealed class ReverseIntegerComparer : IComparer<int>
        {
            /// <nodoc/>
            public int Compare(int x, int y)
            {
                return y.CompareTo(x);
            }
        }

        // C# PriorityQueue dequeues the lowest priority the first unlike our custom PriorityQueue. We pass a custom comparer to reverse this behavior.
        private System.Collections.Generic.PriorityQueue<RunnablePip, int> m_queue = new System.Collections.Generic.PriorityQueue<RunnablePip, int>(new ReverseIntegerComparer());
        private object m_lock = new object();
#else
        private PriorityQueue<RunnablePip> m_queue = new PriorityQueue<RunnablePip>();
#endif
        
        private readonly PipQueue m_pipQueue;

        private int m_numAcquiredSlots;
        private int m_numRunningPips;
        private int m_numQueuedPips;
        private int m_numQueuedProcessPips;
        private readonly object m_startTasksLock = new object();

        /// <summary>
        /// Maximum parallelism degree
        /// </summary>
        public int MaxParallelDegree { get; private set; }

        /// <summary>
        /// Number of acquired slots
        /// </summary>
        public virtual int NumAcquiredSlots => Volatile.Read(ref m_numAcquiredSlots);

        /// <summary>
        /// Number of pips running
        /// </summary>
        /// <remarks>
        /// One pip can acquire more than one slot, so
        /// we have two counters: NumRunningSlots and NumRunningPips
        /// </remarks>
        public virtual int NumRunningPips => Volatile.Read(ref m_numRunningPips);

        /// <summary>
        /// Number of items waiting in the queue
        /// </summary>
        /// <remarks>
        /// PriorityQueue.Count is not performant, so we do not use it here.
        /// </remarks>
        public virtual int NumQueued => Volatile.Read(ref m_numQueuedPips);

        /// <summary>
        /// Number of process pips queued
        /// </summary>
        public int NumProcessesQueued => Volatile.Read(ref m_numQueuedProcessPips);

        /// <summary>
        /// Maximum number of tasks run at the same since the queue has been started
        /// </summary>
        public int MaxRunning { get; private set; }

        /// <summary>
        /// Whether the dispatcher is disposed or not
        /// </summary>
        public bool IsDisposed { get; private set; }

        internal readonly bool UseWeight;

        /// <summary>
        /// Constructor
        /// </summary>
        public DispatcherQueue(PipQueue pipQueue, int maxParallelDegree, bool useWeight = false)
        {
            m_pipQueue = pipQueue;
            MaxParallelDegree = maxParallelDegree;
            m_numAcquiredSlots = 0;
            m_numQueuedProcessPips = 0;
            UseWeight = useWeight;
            m_stack = new ConcurrentStack<int>();
            for (int i = MaxParallelDegree - 1; i >= 0; i--)
            {
                m_stack.Push(i);
            }
        }

        private readonly ConcurrentStack<int> m_stack;

        /// <summary>
        /// Enqueues the given runnable pip
        /// </summary>
        public virtual void Enqueue(RunnablePip runnablePip)
        {
            Contract.Requires(!IsDisposed);

#if NET6_0_OR_GREATER
            lock (m_lock)
            {
                m_queue.Enqueue(runnablePip, runnablePip.Priority);
            }
#else
            m_queue.Enqueue(runnablePip, runnablePip.Priority);
#endif
            
            Interlocked.Increment(ref m_numQueuedPips);

            if (runnablePip.PipType == PipType.Process)
            {
                Interlocked.Increment(ref m_numQueuedProcessPips);
            }
        }

        /// <summary>
        /// Starts all tasks until the queue becomes empty or concurrency limit is satisfied
        /// </summary>
        public virtual void StartTasks()
        {
            Contract.Requires(!IsDisposed);

            // Acquire the lock. This is needed because other threads (namely the ChooseWorker thread may call this
            // method).
            lock (m_startTasksLock)
            {
                RunnablePip runnablePip;
                int maxParallelDegree = MaxParallelDegree;
                while (maxParallelDegree > NumAcquiredSlots && Dequeue(out runnablePip))
                {
                    int slots = UseWeight ? runnablePip.Weight : 1;

                    // If a pip needs slots higher than the total number of slots, still allow it to run as long as there are no other
                    // pips running (the number of acquired slots is 0)
                    if (NumAcquiredSlots != 0 && NumAcquiredSlots + slots > maxParallelDegree)
                    {
                        Enqueue(runnablePip);
                        break;
                    }

                    Interlocked.Increment(ref m_numRunningPips);
                    MaxRunning = Math.Max(MaxRunning, Interlocked.Add(ref m_numAcquiredSlots, slots));

                    StartRunTaskAsync(runnablePip);
                }
            }
        }

        /// <summary>
        /// Dequeues from the priority queue
        /// </summary>
        private bool Dequeue(out RunnablePip runnablePip)
        {
            if (NumQueued != 0)
            {
                Interlocked.Decrement(ref m_numQueuedPips);
#if NET6_0_OR_GREATER
                lock (m_lock)
                {
                    runnablePip = m_queue.Dequeue();
                }
#else
                runnablePip = m_queue.Dequeue();
#endif

                // A race is still possible in rare cases, so we check
                // whether the returned item is not null.
                if (runnablePip != null)
                {
                    if (runnablePip.PipType == PipType.Process)
                    {
                        Interlocked.Decrement(ref m_numQueuedProcessPips);
                    }

                    return true;
                }
            }

            runnablePip = null;
            return false;
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
            int tid = -1;
            if (runnablePip.IncludeInTracer)
            {
                m_stack.TryPop(out tid);
                runnablePip.ThreadId = tid;
            }

            try
            {
                // Unhandled exceptions (Catastrophic BuildXL Failures) during a pip's execution will be thrown here without an AggregateException.
                await runnablePip.RunAsync(releaser);
            }
            finally
            {
                if (tid != -1)
                {
                    m_stack.Push(tid);
                }
               
                releaser.Release(runnablePip.Weight);
                Interlocked.Decrement(ref m_numRunningPips);
                m_pipQueue.DecrementRunningOrQueuedPips(); // Trigger dispatching loop in the PipQueue
            }
        }

        /// <summary>
        /// Release resource for a pip
        /// </summary>
        public void ReleaseResource(int weight)
        {
            int slots = UseWeight ? weight : 1;
            Interlocked.Add(ref m_numAcquiredSlots, -slots); // Decrease the number of running slots in the current queue.
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

        /// <summary>
        /// Adjust the max parallel degree to decrease or increase concurrency
        /// </summary>
        internal virtual bool AdjustParallelDegree(int newParallelDegree)
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
