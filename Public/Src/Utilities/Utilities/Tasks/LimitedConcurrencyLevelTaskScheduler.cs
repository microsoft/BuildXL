// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// Adopted from Parallel Extensions from MSDN galery, http://code.msdn.microsoft.com/windowsdesktop/Samples-for-Parallel-b4b76364
// downloaded  2/2/6/2013
#pragma warning disable 1591

namespace BuildXL.Utilities.Tasks
{
    /// <summary>
    /// Provides a task scheduler that ensures a maximum concurrency level while
    /// running on top of the ThreadPool.
    /// </summary>
    public sealed class LimitedConcurrencyLevelTaskScheduler : TaskScheduler
    {
        /// <summary>Whether the current thread is processing work items.</summary>
        [ThreadStatic]
        private static bool s_currentThreadIsProcessingItems;

        /// <summary>The maximum concurrency level allowed by this scheduler.</summary>
        private readonly int m_maxDegreeOfParallelism;

        /// <summary>The list of tasks to be executed.</summary>
        private readonly LinkedList<Task> m_tasks = new LinkedList<Task>(); // protected by lock(_tasks)

        /// <summary>Whether the scheduler is currently processing work items.</summary>
        private int m_delegatesQueuedOrRunning; // protected by lock(_tasks)

        /// <summary>
        /// Initializes an instance of the LimitedConcurrencyLevelTaskScheduler class with the
        /// specified degree of parallelism.
        /// </summary>
        /// <param name="maxDegreeOfParallelism">The maximum degree of parallelism provided by this scheduler.</param>
        public LimitedConcurrencyLevelTaskScheduler(int maxDegreeOfParallelism)
        {
            Contract.Requires(maxDegreeOfParallelism >= 1);

            m_maxDegreeOfParallelism = maxDegreeOfParallelism;
        }

        /// <summary>Gets the maximum concurrency level supported by this scheduler.</summary>
        public sealed override int MaximumConcurrencyLevel
        {
            get { return m_maxDegreeOfParallelism; }
        }

        /// <summary>Queues a task to the scheduler.</summary>
        /// <param name="task">The task to be queued.</param>
        protected sealed override void QueueTask(Task task)
        {
            // Add the task to the list of tasks to be processed.  IfStatement there aren't enough
            // delegates currently queued or running to process tasks, schedule another.
            lock (m_tasks)
            {
                m_tasks.AddLast(task);
                if (m_delegatesQueuedOrRunning < m_maxDegreeOfParallelism)
                {
                    ++m_delegatesQueuedOrRunning;
                    NotifyThreadPoolOfPendingWork();
                }
            }
        }

        /// <summary>
        /// Informs the ThreadPool that there's work to be executed for this scheduler.
        /// </summary>
        private void NotifyThreadPoolOfPendingWork()
        {
            ThreadPool.UnsafeQueueUserWorkItem(
#pragma warning disable SA1114 // Parameter list must follow declaration
                _ =>
                {
                    // Note that the current thread is now processing work items.
                    // This is necessary to enable inlining of tasks into this thread.
                    s_currentThreadIsProcessingItems = true;
                    try
                    {
                        // Process all available items in the queue.
                        while (true)
                        {
                            Task item;
                            lock (m_tasks)
                            {
                                // When there are no more items to be processed,
                                // note that we're done processing, and get out.
                                if (m_tasks.Count == 0)
                                {
                                    --m_delegatesQueuedOrRunning;
                                    break;
                                }

                                // Get the next item from the queue
                                item = m_tasks.First.Value;
                                m_tasks.RemoveFirst();
                            }

                            // Execute the task we pulled out of the queue
                            TryExecuteTask(item);
                        }
                    }

                        // We're done processing items on the current thread
                    finally
                    {
                        s_currentThreadIsProcessingItems = false;
                    }
                },
#pragma warning restore SA1114 // Parameter list must follow declaration
                null);
        }

        /// <summary>Attempts to execute the specified task on the current thread.</summary>
        /// <param name="task">The task to be executed.</param>
        /// <param name="taskWasPreviouslyQueued">A flag indicating if the task was previously queued.</param>
        /// <returns>Whether the task could be executed on the current thread.</returns>
        protected sealed override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
        {
            // IfStatement this thread isn't already processing a task, we don't support inlining
            if (!s_currentThreadIsProcessingItems)
            {
                return false;
            }

            // IfStatement the task was previously queued, remove it from the queue
            if (taskWasPreviouslyQueued)
            {
                TryDequeue(task);
            }

            // Try to run the task.
            return TryExecuteTask(task);
        }

        /// <summary>Attempts to remove a previously scheduled task from the scheduler.</summary>
        /// <param name="task">The task to be removed.</param>
        /// <returns>Whether the task could be found and removed.</returns>
        protected sealed override bool TryDequeue(Task task)
        {
            lock (m_tasks)
            {
                return m_tasks.Remove(task);
            }
        }

        /// <summary>Gets an enumerable of the tasks currently scheduled on this scheduler.</summary>
        /// <returns>An enumerable of the tasks currently scheduled.</returns>
        protected sealed override IEnumerable<Task> GetScheduledTasks()
        {
            var lockTaken = false;
            try
            {
                Monitor.TryEnter(m_tasks, ref lockTaken);
                if (lockTaken)
                {
                    return m_tasks.ToArray();
                }
                else
                {
                    throw new NotSupportedException();
                }
            }
            finally
            {
                if (lockTaken)
                {
                    Monitor.Exit(m_tasks);
                }
            }
        }
    }
}
