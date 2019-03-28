// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities;

namespace BuildXL.Scheduler.Graph
{
    /// <summary>
    /// Represents a filtered view over a graph
    /// </summary>
    internal sealed class FilteredDirectedGraph : IReadonlyDirectedGraph
    {
        /// <summary>
        /// The underlying graph
        /// </summary>
        private readonly DirectedGraph m_graph;

        /// <summary>
        /// The filter over the graph nodes
        /// </summary>
        private readonly VisitationTracker m_nodeFilter;

        /// <summary>
        /// Predicate used for where clauses over nodes
        /// </summary>
        private readonly Func<NodeId, bool> m_nodePredicate;

        /// <summary>
        /// Predicate used for where clauses over edges
        /// </summary>
        private readonly Func<Edge, bool> m_edgePredicate;

        private readonly Lazy<Dictionary<NodeId, int>> m_nodeHeights;

        public FilteredDirectedGraph(DirectedGraph graph, VisitationTracker nodeFilter)
        {
            m_graph = graph;
            m_nodeFilter = nodeFilter;
            m_nodePredicate = node => nodeFilter.WasVisited(node);
            m_edgePredicate = edge => nodeFilter.WasVisited(edge.OtherNode);
            m_nodeHeights = Lazy.Create(ComputeHeights);
        }

        /// <inheritdoc />
        int IReadonlyDirectedGraph.NodeCount => m_nodeFilter.VisitedCount;

        /// <inheritdoc />
        NodeRange IReadonlyDirectedGraph.NodeRange => m_graph.NodeRange;

        /// <inheritdoc />
        public IEnumerable<NodeId> Nodes => m_graph.Nodes.Where(m_nodePredicate);

        /// <inheritdoc />
        public IEnumerable<NodeId> ReversedNodes => m_graph.ReversedNodes.Where(m_nodePredicate);

        /// <inheritdoc />
        public bool ContainsNode(NodeId node)
        {
            return m_nodeFilter.WasVisited(node);
        }

        /// <inheritdoc />
        int IReadonlyDirectedGraph.CountIncomingHeavyEdges(NodeId node)
        {
            int count = 0;
            foreach (var edge in m_graph.GetIncomingEdges(node))
            {
                if (!edge.IsLight && m_nodeFilter.WasVisited(edge.OtherNode))
                {
                    count++;
                }
            }

            return count;
        }

        /// <inheritdoc />
        int IReadonlyDirectedGraph.CountOutgoingHeavyEdges(NodeId node)
        {
            int count = 0;
            foreach (var edge in m_graph.GetOutgoingEdges(node))
            {
                if (!edge.IsLight && m_nodeFilter.WasVisited(edge.OtherNode))
                {
                    count++;
                }
            }

            return count;
        }

        /// <inheritdoc />
        public IEnumerable<Edge> GetIncomingEdges(NodeId node)
        {
            Contract.Requires(ContainsNode(node), "Cannot get successors of a non-existent node id");
            return m_graph.GetIncomingEdges(node).Where(m_edgePredicate);
        }

        /// <inheritdoc />
        int IReadonlyDirectedGraph.GetIncomingEdgesCount(NodeId node)
        {
            Contract.Requires(ContainsNode(node), "Cannot get predecessors of a non-existent node id");
            int count = 0;
            foreach (var edge in m_graph.GetIncomingEdges(node))
            {
                if (m_nodeFilter.WasVisited(edge.OtherNode))
                {
                    count++;
                }
            }

            return count;
        }

        /// <inheritdoc />
        int IReadonlyDirectedGraph.GetNodeHeight(NodeId node)
        {
            Contract.Requires(ContainsNode(node), "Cannot get height of a non-existent node id");
            return m_nodeHeights.Value[node];
        }

        /// <inheritdoc />
        public IEnumerable<Edge> GetOutgoingEdges(NodeId node)
        {
            Contract.Requires(ContainsNode(node), "Cannot get successors of a non-existent node id");
            return m_graph.GetOutgoingEdges(node).Where(m_edgePredicate);
        }

        /// <inheritdoc />
        public IEnumerable<NodeId> GetSinkNodes()
        {
            return m_graph.Nodes.Where(n => m_nodePredicate(n) && ((IReadonlyDirectedGraph)this).IsSinkNode(n));
        }

        /// <inheritdoc />
        public IEnumerable<NodeId> GetSourceNodes()
        {
            return m_graph.GetSourceNodes().Where(n => m_nodePredicate(n) && ((IReadonlyDirectedGraph)this).IsSourceNode(n));
        }

        /// <inheritdoc />
        bool IReadonlyDirectedGraph.IsSinkNode(NodeId node)
        {
            Contract.Requires(ContainsNode(node), "Argument node must be a valid node id");
            foreach (var edge in m_graph.GetOutgoingEdges(node))
            {
                if (m_nodeFilter.WasVisited(edge.OtherNode))
                {
                    return false;
                }
            }

            return true;
        }

        /// <inheritdoc />
        bool IReadonlyDirectedGraph.IsSourceNode(NodeId node)
        {
            Contract.Requires(ContainsNode(node), "Argument node must be a valid node id");
            foreach (var edge in m_graph.GetIncomingEdges(node))
            {
                if (m_nodeFilter.WasVisited(edge.OtherNode))
                {
                    return false;
                }
            }

            return true;
        }

        /// <inheritdoc />
        bool IReadonlyDirectedGraph.IsValidNodeId(NodeId node)
        {
            if (m_nodeFilter.WasVisited(node))
            {
                return true;
            }

            return false;
        }

        /// <inheritdoc />
        void IReadonlyDirectedGraph.Serialize(BuildXLWriter writer)
        {
            Contract.Requires(writer != null, "Argument writer cannot be null");
            throw new NotImplementedException();
        }

        private Dictionary<NodeId, int> ComputeHeights()
        {
            const int Visited = -1;
            var heights = new Dictionary<NodeId, int>();

            var stack = new Stack<NodeId>();

            foreach (var sinkNode in GetSinkNodes())
            {
                stack.Clear();
                stack.Push(sinkNode);
                while (stack.Count > 0)
                {
                    var node = stack.Pop();
                    int height;
                    if (!heights.TryGetValue(node, out height))
                    {
                        // Unvisited.
                        heights.Add(node, Visited);
                        stack.Push(node);
                        foreach (var incoming in GetIncomingEdges(node))
                        {
                            stack.Push(incoming.OtherNode);
                        }
                    }
                    else
                    {
                        if (height == Visited)
                        {
                            height = 0;

                            // Node was already visited, so update based on dependencies
                            foreach (var incoming in GetIncomingEdges(node))
                            {
                                var incomingNodeHeight = heights[incoming.OtherNode];
                                height = Math.Max(incomingNodeHeight + 1, height);
                            }

                            heights[node] = height;
                        }
                        else
                        {
                            Contract.Assume(height >= 0);
                        }
                    }
                }
            }

            return heights;
        }
    }
}
