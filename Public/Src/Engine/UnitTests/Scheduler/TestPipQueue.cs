// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Pips;
using BuildXL.Pips.Operations;
using BuildXL.Scheduler;
using BuildXL.Scheduler.Graph;
using BuildXL.Scheduler.WorkDispatcher;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using Test.BuildXL.TestUtilities.Xunit;

namespace Test.BuildXL.Scheduler
{
    /// <summary>
    /// Test pip queue which allows altering the result of running a pip and delaying pip execution.
    /// </summary>
    public sealed class TestPipQueue : IPipQueue
    {
        private readonly IPipQueue m_innerQueue;
        private bool m_paused;

        private readonly object m_lock = new object();
        private Queue<Action> m_pausedQueueActions;
        private readonly Dictionary<PipId, HashSet<PipId>> m_runBeforeConstraints = new Dictionary<PipId, HashSet<PipId>>(); 
        private readonly Dictionary<PipId, int> m_pendingConstraintCount = new Dictionary<PipId, int>();
        private readonly Dictionary<PipId, Action> m_pendingConstraintEnqueueAction = new Dictionary<PipId, Action>();
        private readonly LoggingContext m_loggingContext;

        public TestPipQueue(IPipQueue innerQueue, LoggingContext loggingContext, bool initiallyPaused = false)
        {
            m_innerQueue = innerQueue;
            m_loggingContext = loggingContext;
            m_paused = initiallyPaused;
            if (m_paused)
            {
                m_pausedQueueActions = new Queue<Action>();
            }
        }

        public bool Paused => m_paused;

        internal void OnPipCompleted(PipId pipId)
        {
            Contract.Assume(!Paused);

            List<Action> enqueueActions = null;
            lock (m_lock)
            {
                // Possibly release pips constrained after this one.
                HashSet<PipId> runsBefore;
                if (m_runBeforeConstraints.TryGetValue(pipId, out runsBefore))
                {
                    enqueueActions = new List<Action>();
                    foreach (PipId after in runsBefore)
                    {
                        int refcount = m_pendingConstraintCount[after];
                        refcount--;
                        Contract.Assume(refcount >= 0);
                        m_pendingConstraintCount[after] = refcount;

                        // When the ref count hits zero, we should either re-queue the pip right now
                        // or normally-queue it immediately when the scheduler calls Enqueue for it.
                        if (refcount == 0 && m_pendingConstraintEnqueueAction.ContainsKey(after))
                        {
                            enqueueActions.Add(m_pendingConstraintEnqueueAction[after]);
                            m_pendingConstraintEnqueueAction.Remove(after);
                        }
                    }
                }
            }

            if (enqueueActions != null)
            {
                foreach (Action enqueue in enqueueActions)
                {
                    enqueue();
                }
            }
        }

        /// <inheritdoc/>
        public bool IsDisposed => m_innerQueue.IsDisposed;

        /// <inheritdoc/>
        public void DrainQueues()
        {
            Contract.Requires(!IsDraining, "PipQueue has already started draining.");
            Contract.Requires(!IsDisposed);
            m_innerQueue.DrainQueues();
        }

        /// <inheritdoc/>
        public void Enqueue(RunnablePip runnablePip)
        {
            Contract.Requires(runnablePip != null);
            Contract.Requires(!IsDisposed);

            lock (m_lock)
            {
                var pipId = runnablePip.PipId;
                if (Paused)
                {
                    // When paused, all pips go into a staging queue. Unpause will replay Enqeueue again (but with Paused == false)
                    m_pausedQueueActions.Enqueue(() => Enqueue(runnablePip));
                }
                else
                {
                    // Paused is now false and always will be. No new constraints will be added.
                    // Previously-staged pips, if any, may have constraints - if so, they go into a constraint staging area.
                    // Otherwise, we actually queue the pip.
                    int refcount;
                    if (m_pendingConstraintCount.TryGetValue(pipId, out refcount) && refcount > 0)
                    {
                        m_pendingConstraintEnqueueAction.Add(pipId, () => m_innerQueue.Enqueue(runnablePip));
                    }
                    else
                    {
                        m_innerQueue.Enqueue(runnablePip);
                    }
                }
            }
        }

        /// <summary>
        /// Adds an extra constraint so that <paramref name="after"/> will stay queued until <paramref name="before"/> runs.
        /// </summary>
        public void ConstrainExecutionOrder(Pip before, Pip after)
        {
            Contract.Requires(Paused, "Queue must be initially paused, since pips must be added to the schedule to get an ID for constraints");

            lock (m_lock)
            {
                ConstrainExecutionOrderInternal(before.PipId, after.PipId);
            }
        }

        /// <summary>
        /// Adds extra constraints such that <paramref name="after"/> will run after all presently-queued (not 'lazy') source hashing pips.
        /// </summary>
        public void ConstrainExecutionOrderAfterSourceFileHashing(PipTable pipTable, PipGraph pipGraph, Pip after)
        {
            Contract.Requires(Paused, "Queue must be initially paused, since pips must be added to the schedule to get an ID for constraints");
            
            lock (m_lock)
            {
                foreach (PipId hashSourceFilePip in pipTable.Keys)
                {
                    if (pipTable.GetPipType(hashSourceFilePip) != PipType.HashSourceFile)
                    {
                        continue;
                    }

                    var nodeId = hashSourceFilePip.ToNodeId();
                    bool runnableOnDemand = pipGraph.DataflowGraph.GetOutgoingEdges(nodeId).All(edge => edge.IsLight);

                    if (!runnableOnDemand)
                    {
                        ConstrainExecutionOrderInternal(hashSourceFilePip, after.PipId);
                    }
                }
            }
        }

        /// <summary>
        /// Adds an extra constraint so that <paramref name="after"/> will stay queued until <paramref name="before"/> runs.
        /// </summary>
        /// <remarks>
        /// m_lock must be held.
        /// </remarks>
        public void ConstrainExecutionOrderInternal(PipId before, PipId after)
        {
            Contract.Requires(Paused, "Queue must be initially paused, since pips must be added to the schedule to get an ID for constraints");

            Contract.Assume(Monitor.IsEntered(m_lock));
            Contract.Assume(before.IsValid, "'before' pip needs to be added to the scheduler (to get an ID)");
            Contract.Assume(after.IsValid, "'after' pip needs to be added to the scheduler (to get an ID)");

            HashSet<PipId> runsBefore;
            if (!m_runBeforeConstraints.TryGetValue(before, out runsBefore))
            {
                runsBefore = new HashSet<PipId>();
                m_runBeforeConstraints.Add(before, runsBefore);
            }

            if (!runsBefore.Add(after))
            {
                Contract.Assume(false, "Constraint already exists");
            }

            int refcount;
            Analysis.IgnoreResult(m_pendingConstraintCount.TryGetValue(after, out refcount));
            Contract.Assume(refcount >= 0);
            m_pendingConstraintCount[after] = refcount + 1;
        }

        public void Unpause()
        {
            Contract.Requires(Paused);
            Contract.Ensures(!Paused);

            Queue<Action> pausedQueueActions;
            lock (m_lock)
            {
                m_paused = false;
                pausedQueueActions = m_pausedQueueActions;
                m_pausedQueueActions = null;
            }

            // The paused queue actions are expected to call Enqueue and take m_lock.
            while (pausedQueueActions.Count > 0)
            {
                pausedQueueActions.Dequeue()();
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            lock (m_lock)
            {
                foreach (KeyValuePair<PipId, int> pipAndRefCount in m_pendingConstraintCount)
                {
                    XAssert.AreEqual(0, pipAndRefCount.Value, "A pip constrained to run after others was never released to run");
                }
            }

            m_innerQueue.Dispose();
        }

        /// <inheritdoc/>
        public void Cancel()
        {
            m_innerQueue.Cancel();
        }

        /// <inheritdoc/>
        public int GetNumRunningByKind(DispatcherKind queueKind) => m_innerQueue.GetNumRunningByKind(queueKind);

        /// <inheritdoc/>
        public int GetNumQueuedByKind(DispatcherKind queueKind) => m_innerQueue.GetNumQueuedByKind(queueKind);

        /// <inheritdoc/>
        public void SetMaxParallelDegreeByKind(DispatcherKind queueKind, int maxParallelDegree) => m_innerQueue.SetMaxParallelDegreeByKind(queueKind, maxParallelDegree);

        /// <inheritdoc/>
        public void SetAsFinalized()
        {
            m_innerQueue.SetAsFinalized();
        }

        /// <inheritdoc/>
        public int GetMaxParallelDegreeByKind(DispatcherKind queueKind) => m_innerQueue.GetMaxParallelDegreeByKind(queueKind);

        /// <inheritdoc/>
        public void AdjustIOParallelDegree(PerformanceCollector.MachinePerfInfo machinePerfInfo)
        {
            m_innerQueue.AdjustIOParallelDegree(machinePerfInfo);
        }

        /// <inheritdoc/>
        public int MaxProcesses => 10;

        /// <inheritdoc/>
        public bool IsFinished => m_innerQueue.IsFinished;

        /// <inheritdoc/>
        public bool IsDraining => m_innerQueue.IsDraining;

        /// <inheritdoc/>
        public int NumSemaphoreQueued => m_innerQueue.NumSemaphoreQueued;

        /// <inheritdoc/>
        public int TotalNumSemaphoreQueued => m_innerQueue.TotalNumSemaphoreQueued;
    }
}
