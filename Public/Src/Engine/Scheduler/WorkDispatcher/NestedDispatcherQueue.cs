// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using System.Linq;

namespace BuildXL.Scheduler.WorkDispatcher
{
    /// <summary>
    /// A container queue consists of several DispatcherQueues for module affinity.
    /// </summary>
    /// <remarks>
    /// Based on the pip's preference, we enqueue a pip into the preferred DispatcherQueue.
    /// </remarks>
    public class NestedDispatcherQueue : DispatcherQueue
    {
        private readonly DispatcherQueue[] m_dispatcherQueues;

        /// <nodoc/>
        public NestedDispatcherQueue(PipQueue pipQueue, int maxParallelDegree, int workerCount)
            : base(pipQueue, maxParallelDegree)
        {
            Contract.Requires(maxParallelDegree > 0);

            m_dispatcherQueues = new DispatcherQueue[workerCount];
            for (int i = 0; i < m_dispatcherQueues.Length; i++)
            {
                m_dispatcherQueues[i] = new DispatcherQueue(pipQueue, maxParallelDegree);
            }

        }

        internal override bool AdjustParallelDegree(int newParallelDegree)
        {
            foreach (var worker in m_dispatcherQueues)
            {
                worker.AdjustParallelDegree(newParallelDegree);
            }

            return true;
        }

        /// <nodoc/>
        public override int NumAcquiredSlots => m_dispatcherQueues.Sum(a => a.NumAcquiredSlots);

        /// <nodoc/>
        public override int NumRunningPips => m_dispatcherQueues.Sum(a => a.NumRunningPips);

        /// <nodoc/>
        public override int NumQueued => m_dispatcherQueues.Sum(a => a.NumQueued);

        /// <nodoc/>
        public override void Enqueue(RunnablePip runnablePip)
        {
            Contract.Requires(!IsDisposed);

            m_dispatcherQueues[runnablePip.PreferredWorkerId ?? 0].Enqueue(runnablePip);
        }

        /// <nodoc/>
        public override void StartTasks()
        {
            Contract.Requires(!IsDisposed);

            foreach (var worker in m_dispatcherQueues)
            {
                worker.StartTasks();
            }
        }

        /// <nodoc/>
        public override void Dispose()
        {
            foreach (var worker in m_dispatcherQueues)
            {
                worker.Dispose();
            }

            base.Dispose();
        }
    }
}
