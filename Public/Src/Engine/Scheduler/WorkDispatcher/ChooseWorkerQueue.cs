// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Utilities.Core.Tasks;

namespace BuildXL.Scheduler.WorkDispatcher
{
    /// <summary>
    /// Dispatcher queue running on a dedicated thread
    /// </summary>
    /// <remarks>
    /// This dispatcher queue is used to choose workers for pips. As it is on the hot path, 
    /// it runs on a dedicated task scheduler.
    /// </remarks>
    public class ChooseWorkerQueue : DispatcherQueue
    {
        private readonly DedicatedThreadsTaskScheduler m_taskScheduler;
        private readonly TaskFactory m_taskFactory;

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
            try
            {
                await m_taskFactory.StartNew(async () =>
                {
                    await RunCoreAsync(runnablePip);

                    if (NumAcquiredSlots < MaxParallelDegree)
                    {
                        // Fast path for running more work which queues the task to
                        // execute the next item before the task completes so the 
                        // queue does not block waiting for work
                        StartTasks();
                    }
                }).Unwrap();
            }
            catch (InvalidOperationException)
            {
                // If the scheduler is terminating due to ctrl-c, the pip queue might be still draining in very rare cases. 
                // In those rare cases, m_taskFactory will be disposed before we start new items above. That's why, 
                // we ignore InvalidOperationException instances here. It is safe to do because the scheduler is already being
                // terminated.
            }
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            m_taskScheduler.Dispose();
            base.Dispose();
        }
    }
}
