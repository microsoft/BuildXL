// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;

namespace BuildXL.Scheduler.WorkDispatcher
{
    /// <summary>
    /// A container queue consists of several ChooseWorkerQueues for module affinity.
    /// </summary>
    /// <remarks>
    /// Based on the pip's preference, we enqueue a pip into the preferred ChooseWorkerQueue.
    /// </remarks>
    [SuppressMessage("Microsoft.Naming", "CA1711:IdentifiersShouldNotHaveIncorrectSuffix")]
    public class NestedChooseWorkerQueue : ChooseWorkerQueue
    {
        private readonly ChooseWorkerQueue[] m_chooseWorkerQueues;

        /// <nodoc/>
        public NestedChooseWorkerQueue(PipQueue pipQueue, int maxParallelDegree, int workerCount)
            : base(pipQueue, maxParallelDegree)
        {
            Contract.Requires(maxParallelDegree > 0);

            m_chooseWorkerQueues = new ChooseWorkerQueue[workerCount];
            for (int i = 0; i < m_chooseWorkerQueues.Length; i++)
            {
                m_chooseWorkerQueues[i] = new ChooseWorkerQueue(pipQueue, maxParallelDegree);
            }

        }

        internal override bool AdjustParallelDegree(int newParallelDegree)
        {
            foreach (var worker in m_chooseWorkerQueues)
            {
                worker.AdjustParallelDegree(newParallelDegree);
            }

            return true;
        }

        /// <nodoc/>
        public override int NumAcquiredSlots => m_chooseWorkerQueues.Sum(a => a.NumAcquiredSlots);

        /// <nodoc/>
        public override int NumRunningPips => m_chooseWorkerQueues.Sum(a => a.NumRunningPips);

        /// <nodoc/>
        public override int NumQueued => m_chooseWorkerQueues.Sum(a => a.NumQueued);

        /// <nodoc/>
        internal override long FastChooseNextCount => m_chooseWorkerQueues.Sum(a => a.FastChooseNextCount);

        /// <nodoc/>
        public override TimeSpan RunTime => TimeSpan.FromTicks(m_chooseWorkerQueues.Sum(a => a.RunTime.Ticks));

        /// <nodoc/>
        public override void Enqueue(RunnablePip runnablePip)
        {
            Contract.Requires(!IsDisposed);

            m_chooseWorkerQueues[runnablePip.PreferredWorkerId ?? 0].Enqueue(runnablePip);
        }

        /// <nodoc/>
        public override void StartTasks()
        {
            Contract.Requires(!IsDisposed);

            foreach (var worker in m_chooseWorkerQueues)
            {
                worker.StartTasks();
            }
        }

        /// <nodoc/>
        public override void Dispose()
        {
            foreach (var worker in m_chooseWorkerQueues)
            {
                worker.Dispose();
            }

            base.Dispose();
        }
    }
}
