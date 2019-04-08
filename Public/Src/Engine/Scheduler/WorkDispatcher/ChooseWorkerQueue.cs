// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Scheduler.WorkDispatcher
{
    /// <summary>
    /// Dispatcher queue which fires tasks and is managed by <see cref="PipQueue"/>.
    /// </summary>
    [SuppressMessage("Microsoft.Naming", "CA1711:IdentifiersShouldNotHaveIncorrectSuffix")]
    public class ChooseWorkerQueue : DispatcherQueue
    {
        private readonly DedicatedThreadsTaskScheduler m_taskScheduler;
        private readonly TaskFactory m_taskFactory;
        private int m_fastChooseNextCount;

        /// <summary>
        /// The number of times choose worker queue could immediately start next task without blocking
        /// </summary>
        internal int FastChooseNextCount => m_fastChooseNextCount;

        private long m_runTimeTicks;

        /// <summary>
        /// Time spent running work on the queue
        /// </summary>
        public TimeSpan RunTime
        {
            get
            {
                return TimeSpan.FromTicks(m_runTimeTicks);
            }
        }

        /// <summary>
        /// Constructor
        /// </summary>
        public ChooseWorkerQueue(PipQueue pipQueue, int maxParallelDegree) 
            : base(pipQueue, maxParallelDegree)
        {
            Contract.Requires(maxParallelDegree > 0);

            m_taskScheduler = new DedicatedThreadsTaskScheduler(maxParallelDegree, "ChooseWorker Thread");
            m_taskFactory = new TaskFactory(m_taskScheduler);
        }

        /// <inheritdoc />
        [SuppressMessage("AsyncUsage", "AsyncFixer03:FireForgetAsyncVoid", Justification = "Fire and forget is intentional.")]
        protected override async void StartRunTaskAsync(RunnablePip runnablePip)
        {
            // Run the pip on the custom dedicated thread task scheduler
            await m_taskFactory.StartNew(async () =>
            {
                var startTime = TimestampUtilities.Timestamp;

                await RunCoreAsync(runnablePip);

                Interlocked.Add(ref m_runTimeTicks, (TimestampUtilities.Timestamp - startTime).Ticks);

                if (NumRunning < MaxRunning)
                {
                    Interlocked.Increment(ref m_fastChooseNextCount);

                    // Fast path for running more work which queues the task to
                    // execute the next item before the task completes so the 
                    // queue does not block waiting for work
                    StartTasks();
                }
            }).Unwrap();
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            m_taskScheduler.Dispose();
            base.Dispose();
        }
    }
}
