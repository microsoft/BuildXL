// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Threading;
using BuildXL.FrontEnd.Sdk;

namespace BuildXL.FrontEnd.Core
{
    /// <summary>
    /// Cycle detector
    /// </summary>
    /// <remarks>
    /// Cycle detection happens in a background thread.
    /// Value promise chains can be explicitly added yielding a handle object, and removed by disposing that handle object.
    /// When a cycle is identified, a callback is invoked.
    /// The background threads starts out with the lowest possible priority, and that priority can be tweaked if the actual evaluation makes no (or little) progress.
    /// </remarks>
    public sealed class CycleDetector : ICycleDetector
    {
        private readonly object m_syncRoot = new object();
        private EventWaitHandle m_waitHandle;
        private Thread m_thread;
        private bool m_canceled;
        private int m_priority;

        /// <summary>
        /// List of edge sets which still need to get processed in some form
        /// </summary>
        private readonly LinkedList<EdgeSet> m_pendingEdgeSets = new LinkedList<EdgeSet>();

        private readonly CycleDetectorStatistics m_statistics;

        internal CycleDetector(CycleDetectorStatistics statistics)
        {
            Contract.Requires(statistics != null);

            m_statistics = statistics;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// In order to avoid unnecessary costly thread creation,
        /// the cycle detector doesn't create its background thread immediately.
        /// </remarks>
        public void EnsureStarted()
        {
            bool anyPendingEdgeSets;
            lock (m_pendingEdgeSets)
            {
                anyPendingEdgeSets = m_pendingEdgeSets.Count > 0;
            }

            if (anyPendingEdgeSets)
            {
                lock (m_syncRoot)
                {
                    if (m_thread == null)
                    {
                        m_waitHandle = new AutoResetEvent(false);
                        m_thread = new Thread(Run);
                        m_thread.IsBackground = true;
                        m_thread.Priority = ThreadPriority.Lowest;
                        m_thread.Start();
                        Interlocked.Increment(ref m_statistics.CycleDetectionThreadsCreated);
                    }

                    m_waitHandle.Set();
                }
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The background threads starts out with the lowest possible priority, and that priority can be increased if the actual evaluation makes no (or little) progress.
        /// Disposing the returned object decreases the priority again.
        /// </remarks>
        public IDisposable IncreasePriority()
        {
            lock (m_syncRoot)
            {
                m_priority++;
                if (m_priority == 1 && m_thread != null)
                {
                    m_thread.Priority = ThreadPriority.Lowest;
                }

                return new PriorityDecreaser(this);
            }
        }

        /// <summary>
        /// Helper class that allows for <see cref="IncreasePriority"/> in a <code>using</code> statement.
        /// </summary>
        private sealed class PriorityDecreaser : IDisposable
        {
            private CycleDetector m_cycleDetector;

            public PriorityDecreaser(CycleDetector cycleDetector)
            {
                m_cycleDetector = cycleDetector;
            }

            public void Dispose()
            {
                if (m_cycleDetector != null)
                {
                    lock (m_cycleDetector.m_syncRoot)
                    {
                        m_cycleDetector.m_priority--;
                        if (m_cycleDetector.m_priority == 0 && m_cycleDetector.m_thread != null)
                        {
                            m_cycleDetector.m_thread.Priority = ThreadPriority.Normal;
                        }

                        m_cycleDetector = null;
                    }
                }
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            lock (m_syncRoot)
            {
                if (m_thread != null)
                {
                    m_thread.Priority = ThreadPriority.Normal;
                    Volatile.Write(ref m_canceled, true);
                    m_waitHandle.Set();
                    m_thread.Join();
                    m_waitHandle.Dispose();
                    m_waitHandle = null;
                    m_thread = null;
                }
            }
        }

        #region Managing edge sets

        /// <summary>
        /// An edge set is induced by a chain of value promises that are active in a logical thread of the evaluation
        /// </summary>
        /// <remarks>
        /// Initially, only a delegate that can compute the actual value promise chain is given.
        /// If the background thread wants to process this item at some point,
        /// then the actual value promise chain (representing a set of edges) is be computed,
        /// and the set of edges that it induces can be queried.
        /// The edge set transitions through a sequence of states while it is being processed. See <see cref="EdgeSetState"/> for more details on the state transitions.
        /// </remarks>
        private sealed class EdgeSet : IDisposable
        {
            private readonly CycleDetector m_cycleDetector;
            private readonly Func<IValuePromise[]> m_valuePromiseChainGetter;
            private IValuePromise[] m_valuePromiseChain;
            public readonly Action CycleAnnouncer;

            /// <summary>
            /// The node is defined after the edge set got inserted to the pending edge sets when <see cref="State"/> is <see cref="EdgeSetState.PendingAdd"/>.
            /// </summary>
            /// <remarks>
            /// Updates to <see cref="PendingEdgeSetNode"/> and <see cref="State"/> must be protected by locking on <see cref="m_pendingEdgeSets"/>.
            /// </remarks>
            public LinkedListNode<EdgeSet> PendingEdgeSetNode;

            /// <summary>
            /// During its lifetime, the edgeset transitions through various states.
            /// </summary>
            /// <remarks>
            /// Updates to <see cref="PendingEdgeSetNode"/> and <see cref="State"/> must be protected by locking on <see cref="m_pendingEdgeSets"/>.
            /// </remarks>
            public EdgeSetState State;

            public EdgeSet(CycleDetector worker, Func<IValuePromise[]> valuePromiseChainGetter, Action cycleAnnouncer)
            {
                m_cycleDetector = worker;
                m_valuePromiseChainGetter = valuePromiseChainGetter;
                CycleAnnouncer = cycleAnnouncer;
            }

            /// <summary>
            /// Computes a value promise chain
            /// </summary>
            /// <returns>Whether the computed chain is valid (and we didn't get disposed concurrently)</returns>
            public bool ComputeValuePromiseChain()
            {
                m_valuePromiseChain = m_valuePromiseChainGetter();
                return m_cycleDetector.ValuePromiseChainComputed(this); // advances state under big lock, result indicates if computed value is still valid
            }

            /// <summary>
            /// After a call to <see cref="ComputeValuePromiseChain"/>, this property enumerates the directed edges induced by the chain.
            /// </summary>
            public IEnumerable<Edge> Edges
            {
                get
                {
                    for (int i = 0; i < m_valuePromiseChain.Length - 1; i++)
                    {
                        // Or should the edges point the other way? Doesn't matter, we are looking for a cycle in a directed graph.
                        yield return new Edge(m_valuePromiseChain[i], m_valuePromiseChain[i + 1]);
                    }
                }
            }

            /// <summary>
            /// Indicate that edge set is no longer to be considered for cycle detection
            /// </summary>
            public void Dispose()
            {
                m_cycleDetector.Remove(this); // advances state under big lock
            }
        }

        private readonly struct Edge
        {
            public readonly IValuePromise From;

            public readonly IValuePromise To;

            public Edge(IValuePromise from, IValuePromise to)
            {
                From = from;
                To = to;
            }
        }

        /// <summary>
        /// An edge set can go through various states while being processes
        /// </summary>
        private enum EdgeSetState
        {
            /// <summary>
            /// Initial state; edge set is waiting to be selected by background thread for processing
            /// </summary>
            /// <remarks>
            /// Possible successor state: <see cref="Adding"/> (if edge set is selected by background thread for processing), <see cref="Invalid"/> (if edge set is removed before ever being selected by background thread).
            /// </remarks>
            PendingAdd,

            /// <summary>
            /// State in which actual chain is computed by walking context parents
            /// </summary>
            /// <remarks>
            /// Possible successor state: <see cref="Added"/> (if chain was fully computed without a concurrent remove request), <see cref="Abandoned"/> (if edge set is removed before chain was fully computed).
            /// </remarks>
            Adding,

            /// <summary>
            /// State in which actual chain was computed, added to the grain, and a depth-first search is performed to find cycle
            /// </summary>
            /// <remarks>
            /// Possible successor state: <see cref="PendingRemove"/> (when edge set is marked for removal from directed graph when main evaluation continues).
            /// </remarks>
            Added,

            /// <summary>
            /// State in which edge set is waiting to be removed from directed graph
            /// </summary>
            /// <remarks>
            /// Possible successor state: <see cref="Invalid"/> (state transitions after edge set is actually removed from directed graph by background thread).
            /// </remarks>
            PendingRemove,

            /// <summary>
            /// State in which edge set was signalled to be removed from directed graph before actually being fully added to the graph.
            /// </summary>
            /// <remarks>
            /// Possible successor state: <see cref="Invalid"/> (state transitions after edge set is discarded by background thread).
            /// </remarks>
            Abandoned,

            /// <summary>
            /// Terminal state.
            /// </summary>
            Invalid,
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The chain is actually computed lazily in a background thread.
        /// </remarks>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "It's always returned, and it's responsibility of the caller to dispose it.")]
        public IDisposable AddValuePromiseChain(Func<IValuePromise[]> valuePromiseChainGetter, Action cycleAnnouncer)
        {
            var edgeSet = new EdgeSet(this, valuePromiseChainGetter, cycleAnnouncer);
            lock (m_pendingEdgeSets)
            {
                Contract.Assume(edgeSet.State == EdgeSetState.PendingAdd);
                edgeSet.PendingEdgeSetNode = m_pendingEdgeSets.AddLast(edgeSet);
            }

            Interlocked.Increment(ref m_statistics.CycleDetectionChainsAdded);
            return edgeSet;
        }

        /// <summary>
        /// Tries to obtain first pending edge set, and advances state under big lock.
        /// </summary>
        private bool TryRemoveFirstPendingEdgeSet(out EdgeSet edgeSet, out bool add)
        {
            lock (m_pendingEdgeSets)
            {
                if (m_pendingEdgeSets.Count > 0)
                {
                    edgeSet = m_pendingEdgeSets.First.Value;
                    switch (edgeSet.State)
                    {
                        case EdgeSetState.PendingAdd:
                            edgeSet.State = EdgeSetState.Adding;
                            add = true;
                            edgeSet.PendingEdgeSetNode = null;
                            break;
                        case EdgeSetState.PendingRemove:
                            edgeSet.State = EdgeSetState.Invalid;
                            add = false;
                            break;
                        default:
                            throw Contract.AssertFailure("unexpected edge set state");
                    }

                    m_pendingEdgeSets.RemoveFirst();
                    return true;
                }
            }

            edgeSet = null;
            add = false;
            return false;
        }

        /// <summary>
        /// Advances state under big lock after chain has been computed.
        /// </summary>
        private bool ValuePromiseChainComputed(EdgeSet edgeSet)
        {
            lock (m_pendingEdgeSets)
            {
                switch (edgeSet.State)
                {
                    case EdgeSetState.Adding:
                        edgeSet.State = EdgeSetState.Added;
                        return true;
                    case EdgeSetState.Abandoned:
                        edgeSet.State = EdgeSetState.Invalid;
                        Interlocked.Increment(ref m_statistics.CycleDetectionChainsAbandonedWhileProcessing);
                        return false;
                    default:
                        throw Contract.AssertFailure("unexpected edge set state");
                }
            }
        }

        /// <summary>
        /// Indicates that edge set should no longer be considered for cycle detection, and advances state under big lock accordingly.
        /// </summary>
        private void Remove(EdgeSet edgeSet)
        {
            lock (m_pendingEdgeSets)
            {
                switch (edgeSet.State)
                {
                    case EdgeSetState.PendingAdd:
                        edgeSet.State = EdgeSetState.Invalid;
                        Contract.Assume(edgeSet.PendingEdgeSetNode != null);
                        m_pendingEdgeSets.Remove(edgeSet.PendingEdgeSetNode);
                        Interlocked.Increment(ref m_statistics.CycleDetectionChainsRemovedBeforeProcessing);
                        break;
                    case EdgeSetState.Adding:
                        edgeSet.State = EdgeSetState.Abandoned;
                        break;
                    case EdgeSetState.Added:
                        edgeSet.State = EdgeSetState.PendingRemove;
                        m_pendingEdgeSets.AddFirst(edgeSet);

                        // We don't need to set EdgeSet.PendingEdgeSetNode, as the edge set will now eventually be processed for removal, and it will never be dropped from the middle of m_pendingEdgeSets.
                        Interlocked.Increment(ref m_statistics.CycleDetectionChainsRemovedAfterProcessing);
                        break;
                    case EdgeSetState.Invalid:
                    case EdgeSetState.Abandoned:
                    case EdgeSetState.PendingRemove:
                        // could happen if edge set gets disposed multiple times
                        break;
                    default:
                        throw Contract.AssertFailure("unexpected edge set state");
                }
            }
        }
        #endregion

        #region Thread state and logic owned by background thread

        /// <summary>
        /// Background thread runner
        /// </summary>
        private void Run()
        {
            var directedGraphWithReferenceCounts = new DirectedGraphWithReferenceCounts();
            while (true)
            {
                m_waitHandle.WaitOne();
                if (Volatile.Read(ref m_canceled))
                {
                    return;
                }

                EdgeSet edgeSet;
                bool add;
                while (TryRemoveFirstPendingEdgeSet(out edgeSet, out add))
                {
                    if (add)
                    {
                        if (edgeSet.ComputeValuePromiseChain())
                        {
                            // chain computation finished successfully (otherwise, we were concurrently notified that the edge set is no longer valid, and thus we just drop it on the floor)
                            directedGraphWithReferenceCounts.AddEdgeSetAndFindCycle(edgeSet);
                        }
                    }
                    else
                    {
                        directedGraphWithReferenceCounts.RemoveEdgeSet(edgeSet);
                    }
                }
            }
        }

        /// <summary>
        /// Directed graph with reference counts for edges
        /// </summary>
        private sealed class DirectedGraphWithReferenceCounts
        {
            /// <summary>
            /// Underlying representation for directed graph with reference counts
            /// </summary>
            private readonly Dictionary<IValuePromise, Dictionary<IValuePromise, int>> m_graph = new Dictionary<IValuePromise, Dictionary<IValuePromise, int>>();

            /// <summary>
            /// Set of markers that is only non-empty during a depth-first search.
            /// </summary>
            /// <remarks>
            /// Absense of entry means value promise not visited yet;
            /// <code>false</code> means value promise is being visited;
            /// <code>true</code> means value promise has been fully visited and no cycle was found.
            /// </remarks>
            private readonly Dictionary<IValuePromise, bool> m_markers = new Dictionary<IValuePromise, bool>();

            /// <summary>
            /// Perform a depth-first search, finding a cycle by marking visited value promises.
            /// </summary>
            /// <returns><code>true</code> if and only if no cycle was found</returns>
            private bool IsCycleFreeFrom(IValuePromise valuePromise)
            {
                bool isCycleFree;
                if (m_markers.TryGetValue(valuePromise, out isCycleFree))
                {
                    return isCycleFree;
                }

                m_markers[valuePromise] = false; // valuePromise is being visited; if we recursively find it again, it's a cycle!
                Dictionary<IValuePromise, int> toCountedValuePromises;
                if (m_graph.TryGetValue(valuePromise, out toCountedValuePromises))
                {
                    foreach (var toValuePromise in toCountedValuePromises.Keys)
                    {
                        if (!IsCycleFreeFrom(toValuePromise))
                        {
                            return false;
                        }
                    }
                }

                m_markers[valuePromise] = true; // valuePromise is fully visited and no cycle was found from it.
                return true;
            }

            /// <summary>
            /// Adds an edge set (increasing reference counts),
            /// and if resulting graph has a cycle, invoke the edge set's cycle announcer.
            /// </summary>
            public void AddEdgeSetAndFindCycle(EdgeSet edgeSet)
            {
                foreach (var edge in edgeSet.Edges)
                {
                    AddEdge(edge);
                }

                Contract.Assume(m_markers.Count == 0);
                foreach (var valuePromiseWithOutgoingEdges in m_graph.Keys)
                {
                    if (!IsCycleFreeFrom(valuePromiseWithOutgoingEdges))
                    {
                        edgeSet.CycleAnnouncer();
                        break;
                    }
                }

                m_markers.Clear();
            }

            private void AddEdge(Edge edge)
            {
                Dictionary<IValuePromise, int> toCountedValuePromises;
                if (!m_graph.TryGetValue(edge.From, out toCountedValuePromises))
                {
                    m_graph.Add(edge.From, toCountedValuePromises = new Dictionary<IValuePromise, int>());
                }

                int count;
                if (!toCountedValuePromises.TryGetValue(edge.To, out count))
                {
                    count = 0;
                }

                toCountedValuePromises[edge.To] = count + 1;
            }

            /// <summary>
            /// Remove edge set (decreasing reference counts).
            /// </summary>
            public void RemoveEdgeSet(EdgeSet edgeSet)
            {
                foreach (var edge in edgeSet.Edges)
                {
                    RemoveEdge(edge);
                }
            }

            private void RemoveEdge(Edge edge)
            {
                Dictionary<IValuePromise, int> toCountedValuePromises = m_graph[edge.From];
                var newCount = toCountedValuePromises[edge.To] - 1;
                if (newCount == 0)
                {
                    toCountedValuePromises.Remove(edge.From);
                }
                else
                {
                    toCountedValuePromises[edge.To] = newCount;
                }
            }
        }
        #endregion
    }
}
