// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Threading;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Pips.DirectedGraph
{
    /// <summary>
    /// Common implementation for <see cref="IReadonlyDirectedGraph"/>. Derived implementations determine whether the graph is mutable
    /// </summary>
    /// <remarks>
    /// Implementations of this class should be thread-safe.
    /// </remarks>
    public abstract class DirectedGraph : IReadonlyDirectedGraph
    {
        /// <summary>
        /// Envelope for serialization
        /// </summary>
        public static readonly FileEnvelope FileEnvelope = new FileEnvelope(name: "DirectedGraph", version: 0);

        /// <summary>
        /// Object used to verify that enumerator was created by directed graph
        /// </summary>
        private readonly object m_enumeratorVerificationObject = new object();

        /// <summary>
        /// The count of edges
        /// </summary>
        protected int m_edgeCount;

        /// <summary>
        /// Node id counter.
        /// </summary>
        protected int m_lastNodeId;

        /// <summary>
        /// Incoming edges.. Access through GetInEdgeListHeader method.
        /// </summary>
        protected readonly ConcurrentDenseIndex<NodeEdgeListHeader> InEdges;

        /// <summary>
        /// Outgoing edges.. Access through GetOutEdgeListHeader method.
        /// </summary>
        protected readonly ConcurrentDenseIndex<NodeEdgeListHeader> OutEdges;

        /// <summary>
        /// Lengths of node dependency chains. Access through GetNodeHeight method.
        /// </summary>
        protected readonly ConcurrentDenseIndex<int> NodeHeights;

        /// <summary>
        /// Gets the next index and edge given the current index and whether iterating incoming edges vs outgoing edges
        /// </summary>
        /// <param name="currentIndex">the current index for the incoming (isIncoming = true) or outgoing (isIncoming = false) edge</param>
        /// <param name="edge">returns the edge for the current index</param>
        /// <param name="nextIndex">returns the index of the next edge in the edge list</param>
        /// <param name="isIncoming">true if iterating incoming edges. False if iterating outgoing edges.</param>
        protected abstract void GetEdgeAndNextIndex(int currentIndex, out Edge edge, out int nextIndex, bool isIncoming);

        /// <summary>
        /// Gets the outgoing edge list header (containing the first edge index and count) for the node at the given index
        /// </summary>
        protected abstract NodeEdgeListHeader GetOutEdgeListHeader(uint index);

        /// <summary>
        /// Gets the incoming edge list header (containing the first edge index and count) for the node at the given index
        /// </summary>
        protected abstract NodeEdgeListHeader GetInEdgeListHeader(uint index);

        /// <summary>
        /// Gets node height of the node in the graph (ie the length of the longest chain of dependencies)
        /// </summary>
        protected abstract int GetNodeHeight(uint index);

        #region Constructor

        /// <summary>
        /// Class constructor.
        /// </summary>
        protected DirectedGraph()
        {
            m_lastNodeId = 0;
            m_edgeCount = 0;
            InEdges = new ConcurrentDenseIndex<NodeEdgeListHeader>(false);
            OutEdges = new ConcurrentDenseIndex<NodeEdgeListHeader>(false);
            NodeHeights = new ConcurrentDenseIndex<int>(false);
        }

        #endregion Constructor

        #region Properties

        /// <inheritdoc/>
        public int NodeCount => m_lastNodeId;

        /// <inheritdoc/>
        public NodeRange NodeRange => new NodeRange(new NodeId(1), new NodeId((uint)m_lastNodeId));

        /// <nodoc/>
        public int EdgeCount => m_edgeCount;

        #endregion Properties

        /// <inheritdoc/>
        public int GetNodeHeight(NodeId node)
        {
            Contract.Requires(ContainsNode(node), "Cannot get height of a non-existent node id");
            return GetNodeHeight(node.Value);
        }

        /// <inheritdoc/>
        [Pure]
        public bool ContainsNode(NodeId node)
        {
            return node.Value > NodeId.Invalid.Value && node.Value <= m_lastNodeId;
        }

        /// <inheritdoc/>
        [Pure]
        public bool IsSourceNode(NodeId node)
        {
            Contract.Requires(ContainsNode(node), "Argument node must be a valid node id");
            return GetInEdgeListHeader(node.Value).Count == 0;
        }

        /// <inheritdoc/>
        [Pure]
        public bool IsSinkNode(NodeId node)
        {
            Contract.Requires(ContainsNode(node), "Argument node must be a valid node id");
            return GetOutEdgeListHeader(node.Value).Count == 0;
        }

        #region Creation

        /// <nodoc/>
        public Enumerator GetOutgoingEdges(NodeId node)
        {
            var edges = GetOutEdgeListHeader(node.Value);
            return GetOutgoingEdges(edges);
        }

        private Enumerator GetOutgoingEdges(NodeEdgeListHeader edges)
        {
            return new Enumerator(this, edges.FirstIndex, edges.Count, m_enumeratorVerificationObject, isIncoming: false);
        }

        /// <inheritdoc/>
        IEnumerable<Edge> IReadonlyDirectedGraph.GetOutgoingEdges(NodeId node)
        {
            Contract.Requires(ContainsNode(node), "Cannot get successors of a non-existent node id");
            return GetOutgoingEdges(node);
        }

        /// <inheritdoc/>
        IEnumerable<Edge> IReadonlyDirectedGraph.GetIncomingEdges(NodeId node)
        {
            Contract.Requires(ContainsNode(node), "Cannot get successors of a non-existent node id");
            return GetIncomingEdges(node);
        }

        /// <summary>
        /// Computes <code>GetIncomingEdges(nodeId).Count(edge => !edge.IsLight)</code> efficiently.
        /// </summary>
        public int CountIncomingHeavyEdges(NodeId node)
        {
            int count = 0;
            var edges = GetInEdgeListHeader(node.Value);
            var index = edges.FirstIndex;
            for (int i = 0; i < edges.Count; i++)
            {
                Edge edge;
                GetEdgeAndNextIndex(index, out edge, out index, isIncoming: true);
                if (!edge.IsLight)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Computes <code>GetOutgoingEdges(nodeId).Count(edge => !edge.IsLight)</code> efficiently.
        /// </summary>
        public int CountOutgoingHeavyEdges(NodeId node)
        {
            int count = 0;
            var edges = GetOutEdgeListHeader(node.Value);
            var index = edges.FirstIndex;
            for (int i = 0; i < edges.Count; i++)
            {
                Edge edge;
                GetEdgeAndNextIndex(index, out edge, out index, isIncoming: false);
                if (!edge.IsLight)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Gets the outgoing edges for the node
        /// </summary>
        public Enumerator GetIncomingEdges(NodeId node)
        {
            var edges = GetInEdgeListHeader(node.Value);
            return new Enumerator(this, edges.FirstIndex, edges.Count, m_enumeratorVerificationObject, isIncoming: true);
        }

        /// <summary>
        /// Gets the incoming edges for the node
        /// </summary>
        public int GetIncomingEdgesCount(NodeId node)
        {
            Contract.Requires(ContainsNode(node), "Cannot get predecessors of a non-existent node id");
            var edges = GetInEdgeListHeader(node.Value);
            return edges.Count;
        }

        /// <summary>
        /// Gets source nodes of the graph.
        /// </summary>
        /// <inheritdoc/>
        public IEnumerable<NodeId> GetSourceNodes()
        {
            var max = m_lastNodeId;
            for (uint i = 1; i <= max; i++)
            {
                var edges = GetInEdgeListHeader(i);
                if (edges.Count == 0)
                {
                    yield return new NodeId(i);
                }
            }
        }

        /// <inheritdoc/>
        public IEnumerable<NodeId> GetSinkNodes()
        {
            var max = m_lastNodeId;
            for (uint i = 1; i <= max; i++)
            {
                var edges = GetOutEdgeListHeader(i);
                if (edges.Count == 0)
                {
                    yield return new NodeId(i);
                }
            }
        }
        #endregion Creation

        #region Enumeration

        /// <inheritdoc/>
        [Pure]
        public IEnumerable<NodeId> Nodes
        {
            get
            {
                var max = (uint)m_lastNodeId;
                for (uint i = 1; i <= max; i++)
                {
                    yield return new NodeId(i);
                }
            }
        }

        /// <inheritdoc/>
        [Pure]
        public IEnumerable<NodeId> ReversedNodes
        {
            get
            {
                var max = (uint)m_lastNodeId;
                for (uint i = max; i >= 1; i--)
                {
                    yield return new NodeId(i);
                }
            }
        }

        #endregion Enumeration

        #region Serialization

        /// <summary>
        /// Saves this graph on a given binary stream.
        /// </summary>
        public virtual void Serialize(BuildXLWriter writer)
        {
            Contract.Requires(writer != null, "Argument writer cannot be null");
            uint max = (uint)m_lastNodeId;

            writer.Write(max);
            writer.Write(m_edgeCount);

            for (uint node = 1; node <= max; node++)
            {
                var outEdges = GetOutEdgeListHeader(node);

                writer.WriteCompact(outEdges.Count);

                int count = 0;
                foreach (var edge in GetOutgoingEdges(outEdges))
                {
                    edge.Serialize(writer);
                    count++;
                }

                Contract.Assert(outEdges.Count == count);
            }

            for (uint node = 1; node <= max; node++)
            {
                var nodeHeight = GetNodeHeight(node);
                writer.WriteCompact(nodeHeight);

                NodeEdgeListHeader inEdges = GetInEdgeListHeader(node);
                writer.WriteCompact(inEdges.Count);
            }
        }

        #endregion Serialization

        #region Helpers

        /// <inheritdoc/>
        [Pure]
        public bool IsValidNodeId(NodeId node)
        {
            return node.Value > NodeId.Invalid.Value && node.Value <= m_lastNodeId;
        }

        #endregion Helpers

        /// <summary>
        /// Enumerator of lists.
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA1710:IdentifiersShouldHaveCorrectSuffix")]
        public struct Enumerator : IEnumerator<Edge>, IEnumerable<Edge>
        {
            private readonly DirectedGraph m_graph;
            private int m_nextIndex;
            private Edge m_current;
            private readonly bool m_isIncoming;
            private int m_remainingCount;

            internal Enumerator(DirectedGraph graph, int index, int count, object verificationObject, bool isIncoming)
            {
                Contract.Assert(verificationObject == graph.m_enumeratorVerificationObject);

                m_graph = graph;
                m_nextIndex = index;
                m_current = Edge.Invalid;
                m_remainingCount = count;
                m_isIncoming = isIncoming;
            }

            /// <inheritdoc />
            public bool MoveNext()
            {
                if (m_remainingCount > 0)
                {
                    m_remainingCount--;
                    m_graph.GetEdgeAndNextIndex(m_nextIndex, out m_current, out m_nextIndex, m_isIncoming);
                    return true;
                }

                return false;
            }

            /// <inheritdoc />
            public Edge Current => m_current;

            object IEnumerator.Current => Current;

            void IEnumerator.Reset()
            {
                throw Contract.AssertFailure("Don't do this.");
            }

            /// <inheritdoc />
            public void Dispose()
            {
            }

            /// <summary>
            /// Gets the enumerator. Just returns this instance
            /// </summary>
            public Enumerator GetEnumerator()
            {
                return this;
            }

            IEnumerator<Edge> IEnumerable<Edge>.GetEnumerator()
            {
                return this;
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return this;
            }
        }

        /// <summary>
        /// Identifies a linked list of edges in a directed graph
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
        protected struct NodeEdgeListHeader
        {
            private const int SealedNodeBit = unchecked((int)(1 << 31));

            private const int CountMask = ~SealedNodeBit;

            /// <summary>
            /// The index of the first edge in the linked list represented by this pointer
            /// </summary>
            public int FirstIndex;

            /// <summary>
            /// The count of edges in the linked list chain
            /// </summary>
            public int Count => m_countAndSealed & CountMask;

            /// <summary>
            /// Count and sealed bit (highest bit)
            /// </summary>
            private int m_countAndSealed;

            /// <summary>
            /// Creates a new header for a linked list of edges
            /// </summary>
            public NodeEdgeListHeader(int firstIndex, int count)
            {
                Contract.Requires(firstIndex >= 0);
                Contract.Requires(count >= 0);

                m_countAndSealed = count;
                FirstIndex = firstIndex;
            }

            /// <summary>
            /// Gets whether the node is sealed (i.e. supports only addition of incoming edges by the sealing edge scope)
            /// </summary>
            internal bool IsSealed => (m_countAndSealed & SealedNodeBit) != 0;

            /// <summary>
            /// Seals the node
            /// </summary>
            internal void Seal()
            {
                Contract.Requires(!IsSealed, "Attempted to reseal sealed node");
                var newCountAndSealed = Interlocked.Add(ref m_countAndSealed, SealedNodeBit);
                Contract.Assert((newCountAndSealed & SealedNodeBit) != 0);
            }

            internal void InterlockedIncrementCount()
            {
                Interlocked.Increment(ref m_countAndSealed);
            }

            internal int InterlockedDecrementFirstIndex()
            {
                return Interlocked.Decrement(ref FirstIndex);
            }
        }
    }
}
