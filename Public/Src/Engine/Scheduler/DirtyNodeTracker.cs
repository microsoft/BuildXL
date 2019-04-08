// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// Tracks dirty nodes and invalidates transitive dependents when node is marked as dirty.
    /// </summary>
    public class DirtyNodeTracker
    {
        /// <summary>
        /// Set of dirty nodes, as persisted from some prior state and updated via scanning file changes.
        /// </summary>
        protected readonly RangedNodeSet DirtyNodes;

        /// <summary>
        /// Set of nodes that always remain dirty on each execution due to features they use that are not compatible with incremental scheduling.
        /// </summary>
        protected readonly RangedNodeSet PerpetualDirtyNodes;

        /// <summary>
        /// The node dataflow graph
        /// </summary>
        protected readonly DirectedGraph Graph;

        /// <summary>
        /// Indicates whether the set of dirty nodes has changed
        /// </summary>
        protected bool DirtyNodesChanged;

        /// <summary>
        /// Indicates whether the set of perpetual dirty nodes has changed
        /// </summary>
        protected bool PerpetualDirtyNodesChanged;

        /// <summary>
        /// Set of nodes that have ever been executed and have materialized their outputs.
        /// </summary>
        /// <remarks>
        /// Initially, no nodes are not in this set. Suppose that a node n is executed but does not materialize its outputs, perhaps
        /// because it is not selected by the filter. Nevertheless, n is marked clean. Now, suppose further that in the next build n is selected.
        /// Had we not keep track the fact that n has not materialized its outputs, n is considered clean and no execution is performed.
        /// With this set we can mark dirty all clean selected nodes that are not in this set, and thus those nodes will be executed.
        /// </remarks>
        protected readonly RangedNodeSet MaterializedNodes;

        /// <summary>
        /// Indicates whether the set <see cref="MaterializedNodes"/> has changed.
        /// </summary>
        protected bool MaterializedNodesChanged;

        private readonly ObjectPool<Queue<NodeId>> m_nodeQueuePool = new ObjectPool<Queue<NodeId>>(() => new Queue<NodeId>(), queue => queue.Clear());

        /// <summary>
        /// Checks if dirty tracker state has changed.
        /// </summary>
        public bool HasChanged => DirtyNodesChanged || PerpetualDirtyNodesChanged || MaterializedNodesChanged;

        /// <summary>
        /// Pending updated state of dirty node tracker that can be used to update state concurrently.
        /// </summary>
        public sealed class PendingUpdatedState
        {
            private readonly ConcurrentDictionary<NodeId, bool> m_cleanNodes = new ConcurrentDictionary<NodeId, bool>();
            private readonly ConcurrentDictionary<NodeId, bool> m_materializedNodes = new ConcurrentDictionary<NodeId, bool>();
            private readonly ConcurrentDictionary<NodeId, bool> m_perpetuallyDirtyNodes = new ConcurrentDictionary<NodeId, bool>();

            /// <summary>
            /// Nodes marked clean.
            /// </summary>
            public IEnumerable<NodeId> CleanNodes => m_cleanNodes.Keys;

            /// <summary>
            /// Nodes that have materialized their outputs.
            /// </summary>
            public IEnumerable<NodeId> MaterializedNodes => m_materializedNodes.Keys;

            /// <summary>
            /// Nodes marked perpetually dirty.
            /// </summary>
            public IEnumerable<NodeId> PerpetuallyDirtyNodes => m_perpetuallyDirtyNodes.Keys;

            /// <summary>
            /// Marks node clean.
            /// </summary>
            public void MarkNodeClean(NodeId nodeId) => m_cleanNodes.TryAdd(nodeId, true);

            /// <summary>
            /// Marks node materialized.
            /// </summary>
            public void MarkNodeMaterialized(NodeId nodeId) => m_materializedNodes.TryAdd(nodeId, true);

            /// <summary>
            /// Marks node perpetually dirty.
            /// </summary>
            public void MarkNodePerpetuallyDirty(NodeId nodeId) => m_perpetuallyDirtyNodes.TryAdd(nodeId, true);

            /// <summary>
            /// Checks if node has been marked clean.
            /// </summary>
            public bool IsNodeClean(NodeId nodeId) => m_cleanNodes.ContainsKey(nodeId);

            /// <summary>
            /// Checks if node has been marked materialized.
            /// </summary>
            public bool IsNodeMaterialized(NodeId nodeId) => m_materializedNodes.ContainsKey(nodeId);

            /// <summary>
            /// Checks if node has been marked perpetually dirty.
            /// </summary>
            public bool IsNodePrepetuallyDirty(NodeId nodeId) => m_perpetuallyDirtyNodes.ContainsKey(nodeId);

            /// <summary>
            /// Checks if this pending state is still pending.
            /// </summary>
            public bool IsStillPending { get; private set; } = true;

            /// <summary>
            /// Applies pending state, which makes this pending state unpending.
            /// </summary>
            public void ApplyPendingState(DirtyNodeTracker tracker)
            {
                if (!IsStillPending)
                {
                    return;
                }

                foreach (var cleanNode in CleanNodes)
                {
                    tracker.MarkNodeClean(cleanNode);
                }

                // Perpetually dirty nodes must be marked first, so that the materialization can be marked later, see MarkNodeMaterialized.
                foreach (var perpetuallyDirtyNode in PerpetuallyDirtyNodes)
                {
                    tracker.MarkNodePerpetuallyDirty(perpetuallyDirtyNode);

                    // Mark perpetually dirty node clean first, so we can dirty it later.
                    tracker.MarkNodeClean(perpetuallyDirtyNode);
                }

                foreach (var materializedNode in MaterializedNodes)
                {
                    tracker.MarkNodeMaterialized(materializedNode);
                }

                // Dirty all perpetually dirty nodes and their downstream.
                tracker.MarkNodesDirty(PerpetuallyDirtyNodes);

                IsStillPending = false;
            }

            /// <summary>
            /// Clears all pending updates.
            /// </summary>
            public void Clear()
            {
                m_cleanNodes.Clear();
                m_materializedNodes.Clear();
                m_perpetuallyDirtyNodes.Clear();
            }
        }

        /// <summary>
        /// Serialized state for <see cref="DirtyNodeTracker"/>.
        /// </summary>
        public class DirtyNodeTrackerSerializedState
        {
            /// <summary>
            /// The set of dirty nodes.
            /// </summary>
            public readonly RangedNodeSet DirtyNodes;

            /// <summary>
            /// The set of nodes that have materialized their outputs.
            /// </summary>
            public readonly RangedNodeSet MaterializedNodes;

            /// <summary>
            /// The set of nodes that stay dirty even after execution or running from cache.
            /// </summary>
            public readonly RangedNodeSet PerpetualDirtyNodes;

            /// <summary>
            /// Creates an instance of <see cref="DirtyNodeTrackerSerializedState"/>.
            /// </summary>
            public DirtyNodeTrackerSerializedState(DirtyNodeTracker dirtyNodeTracker)
                : this(dirtyNodeTracker.DirtyNodes, dirtyNodeTracker.MaterializedNodes, dirtyNodeTracker.PerpetualDirtyNodes)
            {
                Contract.Requires(dirtyNodeTracker != null);
            }

            private DirtyNodeTrackerSerializedState(RangedNodeSet dirtyNodes, RangedNodeSet materializedNodes, RangedNodeSet perpetualDirtyNodes)
            {
                Contract.Requires(dirtyNodes != null);
                Contract.Requires(materializedNodes != null);
                Contract.Requires(perpetualDirtyNodes != null);

                DirtyNodes = dirtyNodes;
                MaterializedNodes = materializedNodes;
                PerpetualDirtyNodes = perpetualDirtyNodes;
            }

            /// <summary>
            /// Serializes this instance using an instance of <see cref="BuildXLWriter"/>
            /// </summary>
            public void Serialize(BuildXLWriter writer)
            {
                Contract.Requires(writer != null);

                DirtyNodes.Serialize(writer);
                MaterializedNodes.Serialize(writer);
                PerpetualDirtyNodes.Serialize(writer);
            }

            /// <summary>
            /// Deserializes an instance of <see cref="DirtyNodeTrackerSerializedState"/> using an instance of <see cref="BuildXLReader"/>.
            /// </summary>
            /// <param name="reader"></param>
            public static DirtyNodeTrackerSerializedState Deserialize(BuildXLReader reader)
            {
                Contract.Requires(reader != null);

                RangedNodeSet dirtyNodes = RangedNodeSet.Deserialize(reader);
                RangedNodeSet materializedNodes = RangedNodeSet.Deserialize(reader);
                RangedNodeSet perpetualDirtyNodes = RangedNodeSet.Deserialize(reader);

                return new DirtyNodeTrackerSerializedState(dirtyNodes, materializedNodes, perpetualDirtyNodes);
            }
        }

        /// <summary>
        /// Pending updated state.
        /// </summary>
        public readonly PendingUpdatedState PendingUpdates = new PendingUpdatedState();

        /// <summary>
        /// Creates an instence of <see cref="DirtyNodeTracker"/>.
        /// </summary>
        /// <param name="graph">the node dataflow graph.</param>
        /// <param name="dirtyNodes">the set of dirty nodes.</param>
        /// <param name="perpetualDirtyNodes">the set of nodes that stay dirty even after execution or running from cache.</param>
        /// <param name="dirtyNodesChanged">flag indicating whether dirty nodes have changed</param>
        /// <param name="materializedNodes">the set of nodes that have materialized their outputs</param>
        public DirtyNodeTracker(DirectedGraph graph, RangedNodeSet dirtyNodes, RangedNodeSet perpetualDirtyNodes, bool dirtyNodesChanged, RangedNodeSet materializedNodes)
        {
            Contract.Requires(graph != null);
            Contract.Requires(dirtyNodes != null);
            Contract.Requires(materializedNodes != null);
            Contract.Requires(perpetualDirtyNodes != null);

            Graph = graph;
            DirtyNodes = dirtyNodes;
            PerpetualDirtyNodes = perpetualDirtyNodes;
            DirtyNodesChanged = dirtyNodesChanged;
            MaterializedNodes = materializedNodes;
        }

        /// <summary>
        /// Creates an instence of <see cref="DirtyNodeTracker"/>.
        /// </summary>
        public DirtyNodeTracker(DirectedGraph graph, DirtyNodeTrackerSerializedState dirtyNodeTrackerSerializedState)
            : this(graph, dirtyNodeTrackerSerializedState.DirtyNodes, dirtyNodeTrackerSerializedState.PerpetualDirtyNodes, false, dirtyNodeTrackerSerializedState.MaterializedNodes)
        {
            Contract.Requires(graph != null);
            Contract.Requires(dirtyNodeTrackerSerializedState != null);
        }

        /// <summary>
        /// Creates a serialized state of this instance.
        /// </summary>
        public DirtyNodeTrackerSerializedState CreateSerializedState() => new DirtyNodeTrackerSerializedState(this);

        /// <summary>
        /// Indicates if this node needs to regenerate its outputs, due to file changes.
        /// </summary>
        /// <remarks>
        /// This method is NOT thread safe.
        /// </remarks>
        public bool IsNodeDirty(NodeId node)
        {
            Contract.Requires(node.IsValid);
            return DirtyNodes.Contains(node) || PerpetualDirtyNodes.Contains(node);
        }

        /// <summary>
        /// Indicates if this node needs to regenerate its outputs, due to file changes.
        /// </summary>
        /// <remarks>
        /// This method is NOT thread safe.
        /// </remarks>
        public bool IsNodePerpetualDirty(NodeId node)
        {
            Contract.Requires(node.IsValid);
            return PerpetualDirtyNodes.Contains(node);
        }

        /// <summary>
        /// Checks if the specified node is has ever materialized its outputs.
        /// </summary>
        /// <remarks>
        /// For detailed description, see remarks of <see cref="MaterializedNodes"/>.
        /// </remarks>
        public bool IsNodeMaterialized(NodeId node)
        {
            Contract.Requires(node.IsValid);
            return MaterializedNodes.Contains(node);
        }

        /// <summary>
        /// Checks if the specified node is clean and has ever materialized its outputs.
        /// </summary>
        public bool IsNodeCleanAndMaterialized(NodeId node)
        {
            Contract.Requires(node.IsValid);
            return !IsNodeDirty(node) && IsNodeMaterialized(node);
        }

        /// <summary>
        /// Checks if the specified node is clean.
        /// </summary>
        public bool IsNodeClean(NodeId node)
        {
            Contract.Requires(node.IsValid);
            return !IsNodeDirty(node);
        }

        /// <summary>
        /// Checks if the specified node is clean but not materialized.
        /// </summary>
        public bool IsNodeCleanButNotMaterialized(NodeId node)
        {
            Contract.Requires(node.IsValid);
            return IsNodeClean(node) && !IsNodeMaterialized(node);
        }

        /// <summary>
        /// All dirty nodes.
        /// </summary>
        /// <remarks>
        /// This method is NOT thread safe.
        /// </remarks>
        public IEnumerable<NodeId> AllDirtyNodes => DirtyNodes;

        /// <summary>
        /// All materialized nodes.
        /// </summary>
        /// <remarks>
        /// This method is NOT thread safe.
        /// </remarks>
        public IEnumerable<NodeId> AllMaterializedNodes => MaterializedNodes;

        /// <summary>
        /// All perpetually dirty nodes.
        /// </summary>
        /// <remarks>
        /// This method is NOT thread safe.
        /// </remarks>
        public IEnumerable<NodeId> AllPerpertuallyDirtyNodes => PerpetualDirtyNodes;

        /// <summary>
        /// Marks a node as no longer being dirty. This should only be performed (a) after the inputs and outputs of the node are tracked in the change tracker
        /// and (b) after the node has run to completion w.r.t. those inputs / outputs. The node will become dirty again if its outputs change.
        /// </summary>
        /// <remarks>
        /// This method is NOT thread safe.
        /// </remarks>
        public void MarkNodeClean(NodeId node)
        {
            Contract.Requires(node.IsValid);

            DirtyNodes.Remove(node);
            DirtyNodesChanged = true;
        }

        /// <summary>
        /// Adds a node as perpetually dirty.
        /// </summary>
        /// <remarks>
        /// Use this feature to mark nodes dirty for the life of this dirty tracker set, regardless of reloads and reexecution of cached nodes.
        /// This is used for nodes that use features that are not reflected in incremental scheduling, so they are always performed on each build, even incrementally scheduled ones.
        /// </remarks>
        public void MarkNodePerpetuallyDirty(NodeId node)
        {
            Contract.Requires(node.IsValid);

            if (PerpetualDirtyNodes.Contains(node))
            {
                return;
            }

            PerpetualDirtyNodes.Add(node);
            PerpetualDirtyNodesChanged = true;
        }

        /// <summary>
        /// Marks a node in a graph as dirty. This should mark all of its dependents transitively as dirty.
        /// Since we maintain dependents dirty for all dirty nodes, we can stop when a dirty node is found.
        /// </summary>
        /// <remarks>
        /// This method is NOT thread safe.
        /// </remarks>
        public void MarkNodeDirty(NodeId node, Action<NodeId> action = null)
        {
            Contract.Requires(node.IsValid);

            if (DirtyNodes.Contains(node))
            {
                // Most of the time when this method is invoked, the node is already dirty.
                // So, we should not make this case expensive by getting a queue from a pool like
                // what we do below.
                return;
            }

            using (var queueWrapper = m_nodeQueuePool.GetInstance())
            {
                var nodesToDirty = queueWrapper.Instance;
                nodesToDirty.Enqueue(node);
                DirtyNodes.Add(node);
                DirtyNodesChanged = true;

                ProcessDirtyNodeQueue(nodesToDirty, action);
            }
        }

        /// <summary>
        /// Marks a list of nodes in a graph as dirty. This should mark all of their dependents transitively as dirty.
        /// Since we maintain dependents dirty for all dirty nodes, we can stop when a dirty node is found.
        /// </summary>
        /// <remarks>
        /// This method is NOT thread safe.
        /// </remarks>
        public void MarkNodesDirty(IEnumerable<NodeId> nodes, Action<NodeId> action = null)
        {
            Contract.Requires(nodes != null);

            using (var queueWrapper = m_nodeQueuePool.GetInstance())
            {
                var nodesToDirty = queueWrapper.Instance;
                foreach (var node in nodes)
                {
                    if (!DirtyNodes.Contains(node))
                    {
                        nodesToDirty.Enqueue(node);
                        DirtyNodes.Add(node);
                        DirtyNodesChanged = true;
                    }
                }

                if (nodesToDirty.Count > 0)
                {
                    ProcessDirtyNodeQueue(nodesToDirty, action);
                }
            }
        }

        /// <summary>
        /// Propagates node dirtiness to transitive dependents.
        /// </summary>
        private void ProcessDirtyNodeQueue(Queue<NodeId> nodesToDirty, Action<NodeId> action = null)
        {
            Contract.Requires(nodesToDirty != null);

            do
            {
                NodeId node = nodesToDirty.Dequeue();

                if (MaterializedNodes.Contains(node))
                {
                    MaterializedNodes.Remove(node);
                }

                action?.Invoke(node);

                foreach (var edge in Graph.GetOutgoingEdges(node))
                {
                    NodeId dependent = edge.OtherNode;

                    if (!DirtyNodes.Contains(dependent))
                    {
                        nodesToDirty.Enqueue(dependent);
                        DirtyNodes.Add(dependent);
                    }
                }
            }
            while (nodesToDirty.Count > 0);
        }

        /// <summary>
        /// Marks a node in a graph materialized.
        /// </summary>
        public void MarkNodeMaterialized(NodeId node)
        {
            Contract.Requires(node.IsValid);
            Contract.Assert(
                !DirtyNodes.Contains(node) || PerpetualDirtyNodes.Contains(node),
                "Node can only be marked materialized if the node is clean or node is prepetually dirty");

            if (MaterializedNodes.Contains(node))
            {
                return;
            }

            MaterializedNodes.Add(node);
            MaterializedNodesChanged = true;
        }

        /// <summary>
        /// Materialized pending updated states.
        /// </summary>
        internal void MaterializePendingUpdatedState() => PendingUpdates.ApplyPendingState(this);
    }
}
