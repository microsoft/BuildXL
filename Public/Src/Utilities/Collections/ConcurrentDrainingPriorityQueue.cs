// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace BuildXL.Utilities.Collections
{
    /// <summary>
    /// A thread-safe priority queue of tasks that automatically removes tasks and runs them with a configurable degree of
    /// parallelism
    /// </summary>
    /// <remarks>
    /// Optionally, this collection supports the concept of semaphores, limiting maximum concurrency for particular resources.
    /// When semaphore limits are reached, items that require semaphore-guarded resources get queued up separately.
    /// While items that exceed semaphore limits get queued up, other items can jump ahead.
    /// As soon as semaphore-guarded resources are freed up, previously separated queued up postponed items get preferentially
    /// dequeued.
    /// Starvation is prevented by maintaining separate queue for postponed items.
    /// TODO: Introduce a separate resource class for machine resources such as predicted memory or I/O usage.
    /// Items postponed due to semaphore exhaustion will still get preferential treatment, ignoring other machine resources.
    /// If the next regular item exceeds available machine resources, we simply wait until resources have been freed up.
    /// This way, we avoid starvation due to machine resources.
    /// </remarks>
    [SuppressMessage("Microsoft.Naming", "CA1711:IdentifiersShouldNotHaveIncorrectSuffix", Justification = "But it is a queue...")]
    public sealed class ConcurrentDrainingPriorityQueue<TItem, TTask> : IDisposable
        where TTask : Task
    {
        private readonly WaitCallback m_threadPooledDrainQueue;
        private readonly Action<Task, object> m_taskContinuation;
        private int m_maxDegreeOfParallelism;
        private int m_pendingThreadPooledDrainQueues;
        private TaskCompletionSource<long> m_queueDrainedTaskSource;
        private int m_priorityQueued;
        private long m_version;
        private readonly object m_syncRoot = new object();

        #region fields that are write-protected by m_syncRoot (reads of individual values may not require locking)

        private ConcurrentPriorityQueue<TItem> m_priorityQueue = new ConcurrentPriorityQueue<TItem>();
        private Func<TItem, TTask> m_taskCreator;
        private TaskCompletionSource<long> m_allTasksCompletedSource;
        private int m_maxRunning;
        private int m_running;

        private Func<TItem, ItemResources> m_itemResourceGetter;
        private SemaphoreSet m_semaphores;

        /// <summary>
        /// Queue of items dequeued from priority queue, but not yet ready to execute because of semaphore constraints.
        /// </summary>
        private ConcurrentPriorityQueue<(TItem, ItemResources)> m_semaphoreQueue;

        private int m_semaphoreQueued;

        #endregion

        /// <summary>
        /// Creates an instance
        /// </summary>
        /// <remarks>
        /// Note that continuations of the created tasks will be scheduled.
        /// The TPL may chose to run the continuation on the thread that causes the task to transition into its final state --- you must ensure that that thread doesn't block!
        /// </remarks>
        public ConcurrentDrainingPriorityQueue(
            Func<TItem, TTask> taskCreator,
            int maxDegreeOfParallelism = -1,
            Func<TItem, ItemResources> itemResourceGetter = null,
            SemaphoreSet semaphores = null)
        {
            Contract.Requires(taskCreator != null);
            Contract.Requires(maxDegreeOfParallelism >= -1);
            Contract.Requires((itemResourceGetter == null) == (semaphores == null), "itemResourceGetter and semaphores must be both be non-null or both be null");

            m_threadPooledDrainQueue = ThreadPooledDrainQueue;
            m_taskContinuation = TaskContinuation;
            m_maxDegreeOfParallelism = maxDegreeOfParallelism;
            m_taskCreator = taskCreator;
            m_itemResourceGetter = itemResourceGetter;
            m_semaphores = semaphores;
        }

        /// <summary>
        /// Gets or sets the maximum degree of parallelism
        /// </summary>
        public int MaxDegreeOfParallelism
        {
            get
            {
                Contract.Ensures(Contract.Result<int>() >= -1);
                return m_maxDegreeOfParallelism;
            }

            set
            {
                Contract.Requires(value >= -1);
                Volatile.Write(ref m_maxDegreeOfParallelism, value);
                ScheduleDrainQueue(value);
            }
        }

        /// <summary>
        /// Approximate number of elements currently in the priority queue, not including <code>Running</code> or <code>SemaphoreQueued</code> tasks.
        /// </summary>
        public int PriorityQueued
        {
            get
            {
                Contract.Ensures(Contract.Result<int>() >= 0);
                return Volatile.Read(ref m_priorityQueued);
            }
        }

        /// <summary>
        /// Approximate number of elements currently queued waiting for semaphore resources, not including <code>Running</code> or <code>PriorityQueued</code> tasks.
        /// </summary>
        public int SemaphoreQueued
        {
            get
            {
                Contract.Ensures(Contract.Result<int>() >= 0);
                return Volatile.Read(ref m_semaphoreQueued);
            }
        }

        /// <summary>
        /// Approximate number of tasks currently running
        /// </summary>
        public int Running
        {
            get
            {
                Contract.Ensures(Contract.Result<int>() >= 0);
                return Volatile.Read(ref m_running);
            }
        }

        /// <summary>
        /// Maximum observed number of tasks running concurrently
        /// </summary>
        public int MaxRunning
        {
            get
            {
                Contract.Ensures(Contract.Result<int>() >= 0);
                return Volatile.Read(ref m_maxRunning);
            }
        }

        /// <summary>
        /// Disposes this instance.
        /// </summary>
        /// <remarks>
        /// Disposing this queue stops processing any further enqueued items;
        /// however, it doesn't dispose of the items themselves, or cancel any tasks that are already running.
        /// Also, note that there is a race with the task scheduler, and a task that was scheduled to run might still start
        /// running even after the Dispose call returns.
        /// </remarks>
        public void Dispose()
        {
            // We don't release m_queueDrainedTaskSource and m_allTasksCompletedSource, but those don't really matter.
            lock (m_syncRoot)
            {
                m_priorityQueue = null;
                m_taskCreator = null;
                m_itemResourceGetter = null;
                m_semaphores = null;
                m_semaphoreQueue = null;
            }
        }

        /// <summary>
        /// Event handler that gets invoked when an item has been drained from the queue and its run completed
        /// </summary>
        /// <remarks>
        /// Multiple events may be fired concurrently. <code>WhenAllRun</code> and <code>WhenDone</code> will only complete when
        /// all event handlers have returned.
        /// The event handler should do minimal work, as the queue won't re-use the slot before the event handler returns.
        /// Any exception leaked by the event handler may terminate the process.
        /// </remarks>
        public event EventHandler<ItemCompletedEventArgs<TItem, TTask>> ItemCompleted;

        /// <summary>
        /// Event handler that gets invoked when an item has been queued because of exhausted semaphore limits
        /// </summary>
        /// <remarks>
        /// This event handler gets executed under a lock. It should not interact with the queue in any way to avoid deadlocks.
        /// </remarks>
        public event EventHandler<ItemSemaphoreQueuedEventArgs<TItem>> ItemSemaphoreQueued;

        /// <summary>
        /// Event handler that gets invoked when an item has been dequeued previously exhausted semaphore resources have become available
        /// </summary>
        /// <remarks>
        /// This event handler gets executed under a lock. It should not interact with the queue in any way to avoid deadlocks.
        /// </remarks>
        public event EventHandler<ItemSemaphoreQueuedEventArgs<TItem>> ItemSemaphoreDequeued;

        /// <summary>
        /// Adds an item to the queue
        /// </summary>
        /// <remarks>
        /// Items can be added even after the instance has been disposed, increasing the <see cref="PriorityQueued" /> counter; however,
        /// they will not be run.
        /// </remarks>
        public void Enqueue(int priority, TItem item)
        {
            Contract.Requires(priority >= 0);

            if (Interlocked.Increment(ref m_priorityQueued) == 1)
            {
                // We are now transitioning from "no queued items" to "some queued items".
                // We allocate a fresh task source on which one can wait for when the queue is empty again.
                SpinWait.SpinUntil(() => Volatile.Read(ref m_queueDrainedTaskSource) == null);
                TaskCompletionSource<long> oldSource = Interlocked.Exchange(ref m_queueDrainedTaskSource, new TaskCompletionSource<long>());
                Contract.Assert(oldSource == null);
            }

            ConcurrentPriorityQueue<TItem> priorityQueue = Volatile.Read(ref m_priorityQueue);
            if (priorityQueue == null)
            {
                // instance got disposed
                return;
            }

            priorityQueue.Enqueue(priority, item);

            // Make sure that we don't have a subtle reordering problem
            // between enqueuing an item and changing the degree of parallelism.
            Interlocked.MemoryBarrier();

            ScheduleDrainQueue(Volatile.Read(ref m_maxDegreeOfParallelism));
        }

        private void ScheduleDrainQueue(int maxDegreeOfParallelism)
        {
            if (maxDegreeOfParallelism != 0)
            {
                var pending = Interlocked.Increment(ref m_pendingThreadPooledDrainQueues);
                if (maxDegreeOfParallelism == -1 || pending <= maxDegreeOfParallelism)
                {
                    ThreadPool.QueueUserWorkItem(m_threadPooledDrainQueue, null);
                }
                else
                {
                    Interlocked.Decrement(ref m_pendingThreadPooledDrainQueues);
                }
            }
        }

        private bool TryAcquireItem(out TItem item, out ItemResources itemResources)
        {
            Contract.Requires(Monitor.IsEntered(m_syncRoot));

            ConcurrentPriorityQueue<TItem> priorityQueue = m_priorityQueue;
            if (priorityQueue == null)
            {
                // instance got disposed
                item = default(TItem);
                itemResources = default(ItemResources);
                return false;
            }

            // 1. Do we have a queued up semaphore that fits now?

            // TODO: We are just looking at one top-priority pip that got previously postponed because of semaphore constraints. While that one doesn't fit, there might be some other one that fits now. However, finding that in the heap of postponed pips in an efficient is not trivial (we shouldn't traverse all postponed pips every time).
            if (m_semaphoreQueue != null &&
                m_semaphoreQueue.TryPeek(out int priority, out (TItem item, ItemResources itemResources) t) &&
                m_semaphores.TryAcquireResources(t.itemResources))
            {
                item = t.item;
                itemResources = t.itemResources;
                var success = m_semaphoreQueue.TryDequeue(out priority, out (TItem, ItemResources) u);
                Contract.Assert(success);
                Contract.Assert(t.Equals(u));
                m_semaphoreQueued--;

                var eh = ItemSemaphoreDequeued;
                if (eh != null)
                {
                    eh(this, new ItemSemaphoreQueuedEventArgs<TItem>(false, item, itemResources));
                }

                return true;
            }

            // 2. If not, try to dequeue regular item.
            while (true)
            {
                if (!priorityQueue.TryDequeue(out priority, out item))
                {
                    itemResources = default(ItemResources);
                    return false;
                }

                if (m_itemResourceGetter == null)
                {
                    itemResources = ItemResources.Empty;
                }
                else
                {
                    itemResources = m_itemResourceGetter(item);
                    Contract.Assume(itemResources.IsValid);
                    if (!m_semaphores.TryAcquireResources(itemResources))
                    {
                        // We couldn't increment the semaphores for this item, so queue it up...
                        m_semaphoreQueue = m_semaphoreQueue ?? new ConcurrentPriorityQueue<(TItem item , ItemResources itemResources)>();
                        m_semaphoreQueue.Enqueue(
                            priority,
                            (item, itemResources));
                        m_semaphoreQueued++;

                        var eh = ItemSemaphoreQueued;
                        if (eh != null)
                        {
                            eh(this, new ItemSemaphoreQueuedEventArgs<TItem>(true, item, itemResources));
                        }

                        // ... and try another regular item.
                        continue;
                    }
                }

                return true;
            }
        }

        private void ThreadPooledDrainQueue(object state)
        {
            // This code runs on a threadpool thread.
            // If a something terrible happens and an exception is thrown, it should take the process down (and that's a good thing for correctness).
            // Before that happens, someone who has subscribed to AppDomain.CurrentDomain.UnhandledException can do some logging.
            // (However, while running unit tests, someone seems to just swallow unhandled exceptions. That's annoying. In any case, the queue will consume the item and eventually complete.)
            Interlocked.Decrement(ref m_pendingThreadPooledDrainQueues);

            int maxDegreeOfParallelism = Volatile.Read(ref m_maxDegreeOfParallelism);
            if (maxDegreeOfParallelism == 0)
            {
                return;
            }

            while (true)
            {
                TItem item;
                ItemResources itemResources;
                int nextRunning = Volatile.Read(ref m_running) + 1;
                if (maxDegreeOfParallelism != -1 &&
                    nextRunning > maxDegreeOfParallelism)
                {
                    return;
                }

                Func<TItem, TTask> taskCreator;
                lock (m_syncRoot)
                {
                    nextRunning = m_running + 1;
                    if (maxDegreeOfParallelism != -1 &&
                        nextRunning > maxDegreeOfParallelism)
                    {
                        return;
                    }

                    if (!TryAcquireItem(out item, out itemResources))
                    {
                        // instance got disposed, or there is nothing to do that fits the semaphores
                        return;
                    }

                    if (nextRunning == 1)
                    {
                        // We are now transitioning from "no running tasks" to "some running tasks".
                        // We allocate a fresh task source on which one can wait for when all tasks have completed.
                        Contract.Assert(m_allTasksCompletedSource == null);
                        m_allTasksCompletedSource = new TaskCompletionSource<long>();
                    }

                    m_running = nextRunning;
                    m_maxRunning = Math.Max(m_maxRunning, nextRunning);
                    taskCreator = m_taskCreator;
                    Contract.Assert(taskCreator != null);
                }

                if (Interlocked.Decrement(ref m_priorityQueued) == 0)
                {
                    // We are now transitioning from "some queued items" to "no queued items".
                    // This involves allocating a globally unique version number so that different "rounds" can be distinguished.
                    TaskCompletionSource<long> source = Interlocked.Exchange(ref m_queueDrainedTaskSource, null);
                    source.SetResult(Interlocked.Increment(ref m_version));
                }

                TTask task = null;
                try
                {
                    task = taskCreator(item);
                }
                finally
                {
                    // If the taskCreator failed with an exception, or returned null, we are still going to mark this task as done to avoid hangs.
                    if (task == null)
                    {
                        TaskContinuation(null, Tuple.Create(item, itemResources));
                    }
                }

                ThreadPooledDrainQueueHelper(task, item, itemResources);
            }
        }

        // This helper method decreases the overhead of a creating big state machine for async/await.
        [SuppressMessage("AsyncUsage", "AsyncFixer01:UnnecessaryAsyncAwait")]
        [SuppressMessage("AsyncUsage", "AsyncFixer03:FireForgetAsyncVoid")]
        private async void ThreadPooledDrainQueueHelper(TTask task, TItem item, ItemResources itemResources)
        {
            Contract.Assume(task != null);

            // TODO: The following is problematic, as the TPL may choose to run the continuation on the thread that causes the task to transition into its final state --- if that thread then blocks, we might get into a deadlock!
            // .NET 4.6 comes with a new flag: TaskContinuationOptions.RunContinuationsAsynchronously; that might fix the issue.

            // This method violates best practices of using async/await due to mixing async/await and threadpool.
            // We await below just to propagate the exceptions occurring in 'task'
            await task.ContinueWith(
                m_taskContinuation,
                Tuple.Create(item, itemResources),
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);

            // don't wait for clean-up task
        }

        private void TaskContinuation(Task task, object state)
        {
            // This code runs on a threadpool thread.
            // If a something terrible happens and an exception is thrown, it should take the process down (and that's a good thing for correctness).
            // Before that happens, someone who has subscribed to AppDomain.CurrentDomain.UnhandledException can do some logging.
            // (However, while running unit tests, someone seems to just swallow unhandled exceptions. That's annoying. In any case, the queue will consume the item and eventually complete.)
            var t = (Tuple<TItem, ItemResources>)state;
            try
            {
                if (task != null)
                {
                    // Exception handling needs to be put in the event handler before accessing the result of the task.
                    EventHandler<ItemCompletedEventArgs<TItem, TTask>> eh = ItemCompleted;

                    bool isExceptionHandled = false;
                    if (eh != null)
                    {
                        var args = new ItemCompletedEventArgs<TItem, TTask>(t.Item1, (TTask)task);
                        eh(this, args);
                        isExceptionHandled |= args.IsExceptionHandled;
                    }

                    if (task.IsFaulted && !isExceptionHandled)
                    {
                        ExceptionDispatchInfo.Capture(task.Exception.InnerException).Throw();
                    }
                }
            }
            finally
            {
                lock (m_syncRoot)
                {
                    if (m_itemResourceGetter != null)
                    {
                        m_semaphores.ReleaseResources(t.Item2);
                    }

                    if (--m_running == 0)
                    {
                        // We are now transitioning from "some running tasks" to "no running tasks".
                        // This involves allocating a globally unique version number so that different "rounds" can be distinguished.
                        m_allTasksCompletedSource.SetResult(Interlocked.Increment(ref m_version));
                        m_allTasksCompletedSource = null;
                    }
                }

                ScheduleDrainQueue(Volatile.Read(ref m_maxDegreeOfParallelism));
            }
        }

        /// <summary>
        /// Task that completes when the queue has been drained; however, item tasks may still be running (and enqueue more items)
        /// </summary>
        /// <remarks>The long value is a version number; if it remains the same, then no tasks were queued in the meantime</remarks>
        public Task<long> WhenQueueDrained()
        {
            TaskCompletionSource<long> source = Volatile.Read(ref m_queueDrainedTaskSource);
            if (source != null)
            {
                return source.Task;
            }

            return Task.FromResult(Volatile.Read(ref m_version));
        }

        /// <summary>
        /// Task that completes when no more item tasks are running; however, more items may still be queued and could cause new
        /// items tasks to run at any time
        /// </summary>
        /// <remarks>The long value is a version number; if it remains the same, then no tasks were run in the meantime</remarks>
        public Task<long> WhenAllTasksCompleted()
        {
            Contract.Ensures(Contract.Result<Task<long>>() != null);

            // we don't need to lock on m_syncRoot, as just reading m_allTasksCompletedSource should always be conceptually safe
            TaskCompletionSource<long> source = Volatile.Read(ref m_allTasksCompletedSource);
            if (source != null)
            {
                return source.Task;
            }

            return Task.FromResult(Volatile.Read(ref m_version));
        }

        /// <summary>
        /// Task that completes when the queue is empty and no more tasks are running
        /// </summary>
        public async Task<long> WhenDone()
        {
            long nextVersion = Volatile.Read(ref m_version);
            long previousVersion;
            do
            {
                previousVersion = nextVersion;
                await WhenQueueDrained();
                await WhenAllTasksCompleted();
                nextVersion = Volatile.Read(ref m_version);
            }
            while (previousVersion != nextVersion);

            return nextVersion;
        }
    }
}
