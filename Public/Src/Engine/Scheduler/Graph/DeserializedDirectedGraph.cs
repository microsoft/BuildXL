// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Scheduler.Graph
{
    /// <summary>
    /// DirectedGraph optimized for fast deserialization
    /// </summary>
    /// <remarks>
    /// Has been optimized for fast deserialization. Incoming and Outgoing edges are stored in a large buffer. Edges for a particular node occupy a
    /// contiguous edge span and the node edge list header points to the first index in that span and specifies the length of the span for the count.
    /// </remarks>
    public sealed class DeserializedDirectedGraph : DirectedGraph
    {
        private readonly BigBuffer<Edge> m_edgeBuffer;

        /// <summary>
        /// Class constructor
        /// </summary>
        public DeserializedDirectedGraph()
        {
            m_edgeBuffer = new BigBuffer<Edge>();
        }

        /// <inheritdoc/>
        protected override void GetEdgeAndNextIndex(int currentIndex, out Edge edge, out int nextIndex, bool isIncoming)
        {
            edge = m_edgeBuffer[currentIndex];
            nextIndex = currentIndex + 1;
        }

        /// <inheritdoc/>
        protected override DirectedGraph.NodeEdgeListHeader GetInEdgeListHeader(uint index)
        {
            return InEdges[index];
        }

        /// <inheritdoc/>
        protected override DirectedGraph.NodeEdgeListHeader GetOutEdgeListHeader(uint index)
        {
            return OutEdges[index];
        }

        /// <inheritdoc/>
        protected override int GetNodeHeight(uint index)
        {
            return NodeHeights[index];
        }

        /// <summary>
        /// Deserializes
        /// </summary>
        public static Task<DeserializedDirectedGraph> DeserializeAsync(BuildXLReader reader)
        {
            Contract.Requires(reader != null);

            DeserializedDirectedGraph result = new DeserializedDirectedGraph();
            result.Load(reader);
            return Task.FromResult(result);
        }

        /// <summary>
        /// Loads this graph from a given binary stream.
        /// </summary>
        private void Load(BuildXLReader reader)
        {
            m_lastNodeId = (int)reader.ReadUInt32();
            m_edgeCount = reader.ReadInt32();
            Contract.Assume(m_lastNodeId <= NodeId.MaxValue);

            // Create buffer with space for in edges and out edges
            m_edgeBuffer.Initialize(m_edgeCount * 2);
            int edgeIndex = 0;

            // Read the out edges
            {
                var accessor = m_edgeBuffer.GetAccessor();

                // TODO: This loop takes 70% of the deserialization time; make faster.
                for (uint i = 1; i <= m_lastNodeId; ++i)
                {
                    int outNodeCount = reader.ReadInt32Compact();
                    int startEdgeIndex = edgeIndex;
                    for (int j = 0; j < outNodeCount; ++j)
                    {
                        var outgoingEdge = Edge.Deserialize(reader);
                        accessor[edgeIndex] = outgoingEdge;
                        edgeIndex++;
                    }

                    OutEdges[i] = new NodeEdgeListHeader(startEdgeIndex, outNodeCount);
                }
            }

            // Read the count of in edges
            // TODO: This loop takes 10% of the deserialization time; make faster.
            for (uint i = 1; i <= m_lastNodeId; i++)
            {
                int nodeHeight = reader.ReadInt32Compact();
                NodeHeights[i] = nodeHeight;

                int inEdgeCount = reader.ReadInt32Compact();

                // first edge index starts at end of edge span for this node and is decrements as edges
                // are discovered and added below. At the end the first edge index will
                // be at the beginning of the span
                edgeIndex += inEdgeCount;
                InEdges[i] = new NodeEdgeListHeader(edgeIndex, inEdgeCount);
            }

            // Write each out edge set to graph and compute the in edges
            // TODO: This parallel loop takes 20% of the (sequential) deserialization time; make faster.
            Parallel.For(1, m_lastNodeId + 1,
                (i) =>
                {
                    NodeId node = new NodeId((uint)i);
                    var accessor = m_edgeBuffer.GetAccessor();
                    foreach (var e in GetOutgoingEdges(node))
                    {
                        var inEdge = new Edge(node, e.IsLight);

                        // Note: We access the InEdges array element directly, not via the ConcurrentDenseIndex's indexer property.
                        // As a result, we mutate the actual array element, instead of a copy.
                        var p = InEdges.GetBufferPointer(e.OtherNode.Value);
                        var index = p.Buffer[p.Index].InterlockedDecrementFirstIndex();

                        // Set the prior index and point header at that index. The initial value
                        // to index should be set to the index after the span. So this will always
                        // set an index within the edge span
                        accessor[index] = inEdge;
                    }
                });
        }
    }
}
