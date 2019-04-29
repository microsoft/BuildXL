// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Threading;

namespace BuildXL.Scheduler.Graph
{
    /// <summary>
    /// Mutable implementation of a DirectedGraph
    /// </summary>
    /// <remarks>
    /// This  class is thread-safe.
    /// This structure is implemented as a set of nodes represented as integers. Auxillary indexes
    /// (via linked list next pointers to indexes in the set)
    /// allow enumerating incoming and outgoing nodes for a particular node.
    /// NOTE: This structure is write-optimized until <see cref="Seal"/> is called. <see cref="Seal"/>
    /// should be called once all expected modifications are complete.
    /// </remarks>
    public sealed class MutableDirectedGraph : DirectedGraph
    {
        private const int Unvisited = -2;
        private const int Visited = -1;

        private readonly object m_computeNodeHeightsSyncRoot = new object();

        private readonly BigBuffer<LinkedEdgeSetItem> m_edgeSetBuffer;
        private ConcurrentBigSet<LinkedEdgeSetItem> m_edgeSet;
        private readonly ReadWriteLock[] m_locks;
        private MutableGraphState m_state = MutableGraphState.Mutating;

        private readonly ReadWriteLock m_globalLock = ReadWriteLock.Create();
        private int m_modificationsSinceLastNodeHeightComputation;

        /// <summary>
        /// Pool of edge scopes for bulk addition of incoming edges
        /// </summary>
        private readonly ConcurrentQueue<EdgeScope> m_bulkEdgeScopes = new ConcurrentQueue<EdgeScope>();

        /// <summary>
        /// The access verification to ensure scopes can only be created by this object.
        /// </summary>
        private readonly object m_accessVerifier = new object();

        /// <summary>
        /// Class constructor
        /// </summary>
        public MutableDirectedGraph()
        {
            m_edgeSetBuffer = new BigBuffer<LinkedEdgeSetItem>();
            m_edgeSet = new ConcurrentBigSet<LinkedEdgeSetItem>(backingItemsBuffer: m_edgeSetBuffer);

            // Create enough locks to ensure reasonable low contention even
            // if all threads are accessing this data structure
            m_locks = new ReadWriteLock[Environment.ProcessorCount * 4];
            for (int i = 0; i < m_locks.Length; i++)
            {
                m_locks[i] = ReadWriteLock.Create();
            }
        }

        /// <inheritdoc/>
        protected override void GetEdgeAndNextIndex(int currentIndex, out Edge edge, out int nextIndex, bool isIncoming)
        {
            LinkedEdgeSetItem item = m_edgeSetBuffer[currentIndex];

            if (isIncoming)
            {
                edge = item.TargetEdge;
                nextIndex = item.NextTargetIncomingEdgeIndex;
            }
            else
            {
                edge = item.SourceEdge;
                nextIndex = item.NextSourceOutgoingEdgeIndex;
            }
        }

        /// <inheritdoc/>
        protected override DirectedGraph.NodeEdgeListHeader GetInEdgeListHeader(uint index)
        {
            using (AcquireLockForState(m_locks[GetLockNumber(index)], MutableGraphState.Sealed))
            {
                return InEdges[index];
            }
        }

        /// <inheritdoc/>
        protected override DirectedGraph.NodeEdgeListHeader GetOutEdgeListHeader(uint index)
        {
            using (AcquireLockForState(m_locks[GetLockNumber(index)], MutableGraphState.Sealed))
            {
                return OutEdges[index];
            }
        }

        /// <inheritdoc/>
        protected override int GetNodeHeight(uint index)
        {
            ComputeNodeHeights();

            using (AcquireLockForState(m_locks[GetLockNumber(index)], MutableGraphState.Sealed))
            {
                return NodeHeights[index];
            }
        }

        /// <summary>
        /// Checks if graph contains an edge.
        /// </summary>
        [Pure]
        public bool ContainsEdge(NodeId source, NodeId target, bool isLight = false)
        {
            Contract.Requires(ContainsNode(source), "Argument source must be a valid node id");
            Contract.Requires(ContainsNode(target), "Argument target must refer to a valid target node id");

            // Subtle: We need to use GetOutgoingEdges if target is sealed because its
            // edges will not be in the edge set
            if (m_state == MutableGraphState.Mutating && !IsSealed(target))
            {
                return m_edgeSet.Contains(new LinkedEdgeSetItem(source, target, isLight));
            }
            else
            {
                foreach (var edge in GetOutgoingEdges(source))
                {
                    if (edge.OtherNode == target)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        #region Creation

        /// <summary>
        /// Creates node.
        /// </summary>
        public NodeId CreateNode()
        {
            using (m_globalLock.AcquireReadLock())
            {
                var newNodeId = Interlocked.Increment(ref m_lastNodeId);
                Contract.Assume(newNodeId > 0 && newNodeId < NodeId.MaxValue, "All node id's have been allocated already");

                var node = new NodeId((uint)newNodeId);
                Interlocked.Increment(ref m_modificationsSinceLastNodeHeightComputation);
                return node;
            }
        }

        /// <summary>
        /// Compute the node heights for all nodes
        /// </summary>
        private void ComputeNodeHeights()
        {
            if (Volatile.Read(ref m_modificationsSinceLastNodeHeightComputation) != 0)
            {
                lock (m_computeNodeHeightsSyncRoot)
                {
                    int modificationsSinceLastNodeHeightComputation = Volatile.Read(ref m_modificationsSinceLastNodeHeightComputation);
                    while (modificationsSinceLastNodeHeightComputation != 0)
                    {
                        var max = m_lastNodeId;
                        for (uint i = 1; i <= max; i++)
                        {
                            NodeHeights[i] = Unvisited;
                        }

                        Stack<NodeId> nodeStack = new Stack<NodeId>();
                        foreach (var node in Nodes)
                        {
                            nodeStack.Push(node);
                            ComputeNodeHeightsCore(nodeStack);
                        }

                        modificationsSinceLastNodeHeightComputation =
                            Interlocked.Add(
                                ref m_modificationsSinceLastNodeHeightComputation,
                                -modificationsSinceLastNodeHeightComputation);
                    }
                }
            }
        }

        private void ComputeNodeHeightsCore(Stack<NodeId> nodeStack)
        {
            while (nodeStack.Count != 0)
            {
                var node = nodeStack.Pop();
                var nodeHeight = NodeHeights[node.Value];

                switch (nodeHeight)
                {
                    case Unvisited:
                        // Mark the node as visited
                        NodeHeights[node.Value] = Visited;

                        // Push back onto stack so node is updated after visiting
                        // dependencies
                        nodeStack.Push(node);

                        foreach (var incoming in GetIncomingEdges(node))
                        {
                            nodeStack.Push(incoming.OtherNode);
                        }

                        break;
                    case Visited:
                        nodeHeight = 0;

                        // Node was already visited, so update based on dependencies
                        foreach (var incoming in GetIncomingEdges(node))
                        {
                            var incomingNodeHeight = NodeHeights[incoming.OtherNode.Value];
                            nodeHeight = Math.Max(incomingNodeHeight + 1, nodeHeight);
                        }

                        NodeHeights[node.Value] = nodeHeight;
                        break;
                    default:
                        Contract.Assume(nodeHeight >= 0);
                        break;
                }
            }
        }

        private void GetLocksOrdered(ref NodeId source, ref NodeId target, out ReadWriteLock minLock, out ReadWriteLock maxLock)
        {
            int sourceLockNumber = GetLockNumber(source.Value);
            int targetLockNumber = GetLockNumber(target.Value);

            // Always take the minimum node lock first to avoid deadlocks
            int minLockNumber = Math.Min(sourceLockNumber, targetLockNumber);
            int maxLockNumber = Math.Max(sourceLockNumber, targetLockNumber);

            minLock = m_locks[minLockNumber];
            maxLock = minLockNumber != maxLockNumber ? m_locks[maxLockNumber] : ReadWriteLock.Invalid;
        }

        /// <summary>
        /// Prevents further modification to graph and indicates that the graph
        /// should switch to being read-optimized
        /// </summary>
        public void Seal()
        {
            using (m_globalLock.AcquireWriteLock())
            {
                m_state = MutableGraphState.Sealed;

                // Remove the edge set which is only needed for deduplication when adding
                m_edgeSet = null;
            }
        }

        /// <inheritdoc/>
        public override void Serialize(BuildXLWriter writer)
        {
            Contract.Requires(writer != null, "Argument writer cannot be null");

            // ComputeNodeHeights takes up half of the time of serialization.
            // By starting early, we effectively but serialization time in half
            var computeNodeHeights = Task.Run(() => ComputeNodeHeights());
            base.Serialize(writer);
            computeNodeHeights.GetAwaiter().GetResult();
        }

        /// <summary>
        /// Takes a read or write lock for the given operation depending on the state of the graph .
        /// For instance, there can be concurrent edge additions while in the Mutating state for the same node/lock,
        /// but enumeration will take an exclusive lock to ensure it gets consistent information while graph is in Mutating state.
        /// Once the graph enters the sealed state, enumerations allow sharing. Therefore, operations that enumerate edges will pass
        /// sharingState = Sealed while the AddEdge operation passes sharingState = Mutating.
        /// </summary>
        /// <param name="rwLock">the read write lock to acquire read or write access from</param>
        /// <param name="sharingState">specifies the state for which concurrent access is allowed for the operation protected by this lock</param>
        /// <returns>the read or write lock (as a switch read write lock)</returns>
        private SwitchReadWriteLock AcquireLockForState(ReadWriteLock rwLock, MutableGraphState sharingState)
        {
            if (!rwLock.IsValid)
            {
                return default(SwitchReadWriteLock);
            }

            using (m_globalLock.HasExclusiveAccess ? ReadLock.Invalid : m_globalLock.AcquireReadLock())
            {
                return new SwitchReadWriteLock(rwLock, isSharing: sharingState == m_state);
            }
        }

        /// <summary>
        /// Defines the state for the graph
        /// </summary>
        private enum MutableGraphState
        {
            /// <summary>
            /// Graph is mutable and write-optimized (concurrent write operations on same node allowed and reads take exclusive lock on node)
            /// </summary>
            Mutating,

            /// <summary>
            /// Graph is immutable and read-optimized (concurrent read operations allowed)
            /// </summary>
            Sealed,
        }

        /// <summary>
        /// This structure is used to represent a sharing or exclusive lock depending
        /// on the flag passed during construction.
        /// </summary>
        private readonly struct SwitchReadWriteLock : IDisposable
        {
            /// <summary>
            /// True if the read lock should be acquired/release. False for write lock.
            /// </summary>
            private readonly bool m_isRead;

            /// <summary>
            /// The underlying read write lock
            /// </summary>
            private readonly ReadWriteLock m_rwLock;

            public SwitchReadWriteLock(ReadWriteLock rwLock, bool isSharing)
            {
                m_rwLock = rwLock;
                m_isRead = isSharing;

                if (m_isRead)
                {
                    m_rwLock.EnterReadLock();
                }
                else
                {
                    m_rwLock.EnterWriteLock();
                }
            }

            public void Dispose()
            {
                if (m_rwLock.IsValid)
                {
                    if (m_isRead)
                    {
                        m_rwLock.ExitReadLock();
                    }
                    else
                    {
                        m_rwLock.ExitWriteLock();
                    }
                }
            }
        }

        private int GetLockNumber(uint nodeIndex)
        {
            return (int)(nodeIndex % (uint)m_locks.Length);
        }

        /// <summary>
        /// Acquires an edge scope giving ability to bulk add ALL incoming edges for a node at once. No further
        /// edges may be added to node after releasing the scope. Edges are only committed to the graph after
        /// scope is disposed.
        /// </summary>
        [SuppressMessage("Microsoft.Reliability", "CA2000:DisposeObjectsBeforeLosingScope")]
        public EdgeScope AcquireExclusiveIncomingEdgeScope(NodeId target)
        {
            if (!m_bulkEdgeScopes.TryDequeue(out EdgeScope scope))
            {
                scope = new EdgeScope(this, m_accessVerifier);
            }

            scope.SetTarget(target, m_accessVerifier);
            return scope;
        }

        /// <summary>
        /// Adds edge.
        /// </summary>
        public bool AddEdge(NodeId source, NodeId target, bool isLight = false)
        {
            Contract.Requires(ContainsNode(source), "Argument source must be a valid node id");
            Contract.Requires(ContainsNode(target), "Argument target must be a valid node id");
            Contract.Requires(
                ContainsNode(source) && ContainsNode(target),
                "Cannot add edge between non-existent node ids");

            ReadWriteLock minLock;
            ReadWriteLock maxLock;
            GetLocksOrdered(ref source, ref target, out minLock, out maxLock);
            bool added;

            using (m_globalLock.AcquireReadLock())
            {
                Contract.Assert(m_state == MutableGraphState.Mutating, "Graph mutations are not permitted after sealing");
                using (AcquireLockForState(minLock, MutableGraphState.Mutating))
                using (AcquireLockForState(maxLock, MutableGraphState.Mutating))
                {
                    // AddEdgeUnchecked is thread-safe with respect to concurrent modifications
                    added = AddEdgeUnchecked(source, target, isLight);
                }

                if (added)
                {
                    Interlocked.Increment(ref m_modificationsSinceLastNodeHeightComputation);
                }
            }

            return added;
        }

        private void AddEdges(NodeId target, HashSet<Edge> incomingEdges)
        {
            BufferPointer<NodeEdgeListHeader> targetInEdges = InEdges.GetBufferPointer(target.Value);
            Contract.Assert(targetInEdges.Buffer[targetInEdges.Index].IsSealed, "Bulk additions can only be made to sealed nodes");

            using (m_globalLock.AcquireReadLock())
            {
                Contract.Assert(m_state == MutableGraphState.Mutating, "Graph mutations are not permitted after sealing");

                foreach (var incoming in incomingEdges)
                {
                    var source = incoming.OtherNode;
                    var incomingLock = m_locks[GetLockNumber(source.Value)];
                    using (AcquireLockForState(incomingLock, MutableGraphState.Mutating))
                    {
                        // AddEdgeUnchecked is thread-safe with respect to concurrent modifications
                        AddEdgeUnchecked(
                            source: source,
                            target: target,
                            targetInEdges: targetInEdges,
                            isLight: incoming.IsLight,
                            bulkAddingTargetIncoming: true);
                    }
                }

                Interlocked.Increment(ref m_modificationsSinceLastNodeHeightComputation);
            }
        }

        private bool IsSealed(NodeId target)
        {
            BufferPointer<NodeEdgeListHeader> targetInEdges = InEdges.GetBufferPointer(target.Value);
            return targetInEdges.Buffer[targetInEdges.Index].IsSealed;
        }

        private void SealNodeForBulkIncomingEdgeAddition(NodeId target)
        {
            BufferPointer<NodeEdgeListHeader> targetInEdges = InEdges.GetBufferPointer(target.Value);
            targetInEdges.Buffer[targetInEdges.Index].Seal();
            Contract.Assert(targetInEdges.Buffer[targetInEdges.Index].IsSealed, "Node should be sealed");
        }

        private bool AddEdgeUnchecked(NodeId source, NodeId target, bool isLight)
        {
            BufferPointer<NodeEdgeListHeader> targetInEdges = InEdges.GetBufferPointer(target.Value);
            Contract.Assert(!targetInEdges.Buffer[targetInEdges.Index].IsSealed, "Attempted to add edge to sealed node");

            return AddEdgeUnchecked(
                source: source,
                target: target,
                targetInEdges: targetInEdges,
                isLight: isLight,
                bulkAddingTargetIncoming: false);
        }

        private bool AddEdgeUnchecked(
            NodeId source,
            NodeId target,
            BufferPointer<NodeEdgeListHeader> targetInEdges,
            bool isLight,
            bool bulkAddingTargetIncoming)
        {
            BufferPointer<NodeEdgeListHeader> outEdges = OutEdges.GetBufferPointer(source.Value);

            var edgeSetItem = new LinkedEdgeSetItem(source, target, isLight);
            int index = 0;
            if (!bulkAddingTargetIncoming)
            {
                ConcurrentBigSet<LinkedEdgeSetItem>.GetAddOrUpdateResult result =
                    m_edgeSet.GetOrAdd(edgeSetItem);

                if (result.IsFound)
                {
                    // Edge already existed
                    return false;
                }

                index = result.Index;
            }
            else
            {
                index = m_edgeSet.ReservedNextIndex(m_edgeSetBuffer);
                m_edgeSetBuffer[index] = edgeSetItem;
            }

            // Update head index for in edges and out edges
            int inEdgesNext = Interlocked.Exchange(ref targetInEdges.Buffer[targetInEdges.Index].FirstIndex, index);
            int outEdgesNext = Interlocked.Exchange(ref outEdges.Buffer[outEdges.Index].FirstIndex, index);

            var linkedEdgeSetItemPtr = m_edgeSetBuffer.GetBufferPointer(index);

            // Update next pointers
            linkedEdgeSetItemPtr.Buffer[linkedEdgeSetItemPtr.Index].NextTargetIncomingEdgeIndex = inEdgesNext;
            linkedEdgeSetItemPtr.Buffer[linkedEdgeSetItemPtr.Index].NextSourceOutgoingEdgeIndex = outEdgesNext;

            Interlocked.Increment(ref m_edgeCount);

            // Update edge counts
            targetInEdges.Buffer[targetInEdges.Index].InterlockedIncrementCount();
            outEdges.Buffer[outEdges.Index].InterlockedIncrementCount();
            return true;
        }

        #endregion Creation

        /// <summary>
        /// Deserializes
        /// </summary>
        public static MutableDirectedGraph Deserialize(BuildXLReader reader)
        {
            Contract.Requires(reader != null);
            MutableDirectedGraph graph = new MutableDirectedGraph();
            graph.Load(reader);
            return graph;
        }

        /// <summary>
        /// Loads this graph from a given binary stream.
        /// </summary>
        private void Load(BuildXLReader reader)
        {
            m_lastNodeId = (int)reader.ReadUInt32();
            var readEdgeCount = reader.ReadInt32();

            // Read the out edges
            for (uint i = 1; i <= m_lastNodeId; ++i)
            {
                var node = new NodeId(i);
                int outNodeCount = reader.ReadInt32Compact();
                for (int j = 0; j < outNodeCount; ++j)
                {
                    var outgoingEdge = Edge.Deserialize(reader);
                    AddEdgeUnchecked(node, outgoingEdge.OtherNode, outgoingEdge.IsLight);
                }

                Contract.Assert(OutEdges[i].Count == outNodeCount);
            }

            Contract.Assert(m_edgeCount == readEdgeCount);

            // Read the count of in edges
            for (uint i = 1; i <= m_lastNodeId; i++)
            {
                int nodeHeight = reader.ReadInt32Compact();
                NodeHeights[i] = nodeHeight;

                int inEdgeCount = reader.ReadInt32Compact();
                Contract.Assert(InEdges[i].Count == inEdgeCount);
            }
        }

        /// <summary>
        /// Scope for bulk addition of edges
        /// </summary>
        public sealed class EdgeScope : IDisposable
        {
            private readonly MutableDirectedGraph m_owner;
            private NodeId m_target;
            private readonly HashSet<Edge> m_incomingEdges = new HashSet<Edge>();

            /// <summary>
            /// Creates a new edge scope. This should only be called by the owning <see cref="MutableDirectedGraph"/>
            /// </summary>
            public EdgeScope(MutableDirectedGraph owner, object accessVerifier)
            {
                Contract.Assert(owner.m_accessVerifier == accessVerifier, "EdgeScope can only be created by a parent MutableDirectedGraphs");
                m_owner = owner;
            }

            /// <summary>
            /// Sets new target for edge scope. This should only be called by the owning <see cref="MutableDirectedGraph"/>
            /// </summary>
            internal void SetTarget(NodeId target, object accessVerifier)
            {
                Contract.Assert(m_owner.m_accessVerifier == accessVerifier, "SetTarget can only be called by a parent MutableDirectedGraphs");
                m_target = target;
                m_owner.SealNodeForBulkIncomingEdgeAddition(target);
            }

            /// <summary>
            /// Adds edge to the given source node to be added on disposal of this scope
            /// </summary>
            public bool AddEdge(NodeId source, bool isLight = false)
            {
                return m_incomingEdges.Add(new Edge(source, isLight));
            }

            /// <summary>
            /// Commits the edges to the graph
            /// </summary>
            public void Dispose()
            {
                m_owner.AddEdges(m_target, m_incomingEdges);
                m_incomingEdges.Clear();
                m_target = NodeId.Invalid;

                m_owner.m_bulkEdgeScopes.Enqueue(this);
            }
        }

#pragma warning disable CA2231 // Overload operator equals on overriding value type Equals
        /// <summary>
        /// Represents an edge between a source node and target node along with
        /// linked list pointer to next outgoing edge for source node and
        /// next incoming edge for target node
        /// </summary>
        private struct LinkedEdgeSetItem : IEquatable<LinkedEdgeSetItem>
#pragma warning restore CA2231 // Overload operator equals on overriding value type Equals
        {
            public readonly Edge TargetEdge;
            public readonly Edge SourceEdge;
            public int NextSourceOutgoingEdgeIndex;
            public int NextTargetIncomingEdgeIndex;

            public LinkedEdgeSetItem(NodeId sourceNode, NodeId targetNode, bool isLightEdge, int nextSourceOutgoingEdgeIndex = -1, int nextTargetIncomingEdgeIndex = -1)
            {
                // The target edge contains the source node as other node
                TargetEdge = new Edge(sourceNode, isLightEdge);

                // The source edge contains the target node as other node
                SourceEdge = new Edge(targetNode, isLightEdge);
                NextSourceOutgoingEdgeIndex = nextSourceOutgoingEdgeIndex;
                NextTargetIncomingEdgeIndex = nextTargetIncomingEdgeIndex;
            }

            public bool Equals(LinkedEdgeSetItem other)
            {
                return TargetEdge == other.TargetEdge && SourceEdge == other.SourceEdge;
            }

            public override int GetHashCode()
            {
                return HashCodeHelper.Combine(TargetEdge.GetHashCode(), SourceEdge.GetHashCode());
            }

            public override bool Equals(object obj)
            {
                return obj is LinkedEdgeSetItem && Equals((LinkedEdgeSetItem)obj);
            }
        }
    }
}
