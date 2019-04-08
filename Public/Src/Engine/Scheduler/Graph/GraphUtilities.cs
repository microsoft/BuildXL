// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Scheduler.Graph
{
    /// <summary>
    /// Graph traversal and analytics.
    /// </summary>
    public static class GraphUtilities
    {
        /// <summary>
        /// Topologically sorts nodes in a given graph.  If no <paramref name="nodes"/> are specified all nodes in the graph
        /// are used (<see cref="IReadonlyDirectedGraph.Nodes"/>); if <paramref name="nodes"/> are specified, they must all
        /// belong to the given <paramref name="graph"/>.
        ///
        /// Returns a multi-value dictionary with keys ranging from 0 to max node height.
        /// </summary>
        public static MultiValueDictionary<int, NodeId> TopSort(this IReadonlyDirectedGraph graph, IEnumerable<NodeId> nodes = null)
        {
            nodes = nodes ?? graph.Nodes;
            MultiValueDictionary<int, NodeId> nodesByHeight = new MultiValueDictionary<int, NodeId>();
            foreach (var node in nodes)
            {
                var height = graph.GetNodeHeight(node);
                nodesByHeight.Add(height, node);
            }

            return nodesByHeight;
        }

        /// <summary>
        /// Checks if there exists a path between <paramref name="from"/> and <paramref name="to"/> (following directed edges 'outward').
        /// </summary>
        /// <remarks>
        /// Since the underlying <see cref="IReadonlyDirectedGraph"/> is not thread-safe, the caller is responsible for synchronizing access to it.
        /// This algorithm requires a particular graph structure:
        /// - The <see cref="IReadonlyDirectedGraph"/> must not contain cycles.
        /// - The <see cref="NodeId"/>s of each node visited must form a topological labelling.
        ///   Precisely, for any edge N -> M (outgoing from N, incoming to M), the node ID M must have a value strictly greater than N.
        /// (note that traversing a cycle fails the second condition, so no separate cycle validation is needed).
        /// A <see cref="BuildXLException"/> is thrown if these conditions are found to be violated (in a very limited set of cases, depending on the part of the graph actually visited);
        /// this check can instead be suppressed if <paramref name="skipOutOfOrderNodes"/> is set, but one must then be very careful to know which nodes may be skipped as a result of possible misordering.
        /// </remarks>
        public static bool IsReachableFrom(this IReadonlyDirectedGraph graph, NodeId from, NodeId to, bool skipOutOfOrderNodes = false)
        {
            Contract.Requires(graph != null);
            Contract.Requires(from.IsValid && to.IsValid);
            Contract.Requires(graph.ContainsNode(from) && graph.ContainsNode(to));

            // First, some fast paths that don't need to grab RangedNodeSets.
            if (from == to)
            {
                return true;
            }

            if (from.Value > to.Value)
            {
                return false;
            }

            using (PooledObjectWrapper<RangedNodeSet> pooledSetA = SchedulerPools.RangedNodeSetPool.GetInstance())
            using (PooledObjectWrapper<RangedNodeSet> pooledSetB = SchedulerPools.RangedNodeSetPool.GetInstance())
            using (PooledObjectWrapper<RangedNodeSet> pooledSetC = SchedulerPools.RangedNodeSetPool.GetInstance())
            {
                return IsReachableFromInternal(graph, from, to, pooledSetA.Instance, pooledSetB.Instance, pooledSetC.Instance, skipOutOfOrderNodes);
            }
        }

        private static bool IsReachableFromInternal(
            IReadonlyDirectedGraph graph,
            NodeId from,
            NodeId to,
            RangedNodeSet pooledSetA,
            RangedNodeSet pooledSetB,
            RangedNodeSet pooledSetC,
            bool skipOutOfOrderNodes)
        {
            // This implementation attempts to efficiently traverse a graph without any precomputed indices or labeling beyond topologically ordered node values.
            // Index-based approaches are tricky for the expected usage (the BuildXL scheduler) in which the underlying graph is dynamic.
            // Instead, we traverse the graph with no prior information in hand, with a careful traversal order and some pruning.
            // The thinking on pruning / use of topological labels is not new; for some more robust examples see e.g. FELINE:
            //      Veloso, Renê Rodrigues, et al. "Reachability Queries in Very Large Graphs: A Fast Refined Online Search Approach." EDBT. 2014.
            // Figure 6 in particular gives some geometric insight, though the pruning here is less effective.

            // Now, let's build some intuition about this implementation. We begin from a naive approach and will refine to what's actually implemented.
            // First, consider the problem of traversing an _undirected_ graph to determine reachability from some point M to N.
            // o\ /o          o\ /o
            //   M -- o -- o -- N
            // o/ \o          o/ \o
            // We can imagine the graph in some two-dimensional layout. To perform well with M and N fairly close, it would be wise to proceed in a breadth-first fashion:
            // ●\ /●          o\ /o
            //   M -- ● -- o -- N
            // ●/ \●          o/ \o
            // On the first iteration, we have all nodes reachable in one hop from M. On the i'th iteration, we have all nodes reachable in 'i' hops. In the example above,
            // N would be found on iteration 3. Geometrically, think of the reached set as a circle expanding outward from M. Note that on each iteration i, we only need to
            // hold *the nodes i hopes away* (not i - 1 hops etc.) so in fact we are tracking the outer circumference of this circle. This works since there exists some single
            // integer i by which N is at least i hops away (if reachable).

            // We can leverage the geometric intuition of a circle to improve that approach a bit. Assume that on some iteration i, we have visited all nodes interior to the circle,
            // and the discrete nodes are so numerous and dense as to approximate a circle's area. We can then think of i as a radius and the area as a count of nodes visited -
            // on the order of i^2. If we instead traverse the same radius via two circles - each of radius (i/2) then the visited area is (i/2)^2 * 2 = i^2 / 2. Intersection of
            // the circles implies a path between the two nodes.
            // ●\ /●          ●\ /●
            //   M -- ● -- ● -- N
            // ●/ \●          ●/ \●
            // (above: the prior example on iteration 1 when expanding outward from both endpoints; a path will be found on iteration 2 instead of 3).
            // We can still track only the outer circumference of the circles, so long as we alternately expand each circle (rather than expanding both instantaneously; each expansion
            // increases the effective search radius by one and so there's no way for the circles to skip past each other).

            // Now we first leverage the assumption of a *directed* graph. Simply, we can follow edges in opposing directions from each node (now labelling specifically as 'to' {T} and from'{F}),
            // which geometrically looks like expanding semi-circles rather than circles (the nodes labeled X were skipped based on direction):
            // X\  />●           ●>\ />X
            //   >F --> ● --> ● --> T
            // X/  \>●           ●>/ \>X

            // Finally we consider the usefulness of a topological labeling of the nodes. This is in fact a generalization of following edges in only one direction:
            // - Outgoing-incident nodes must have higher labels than the one current.
            // - Incoming-incident nodes must by symmetry have lower labels than the one currnet.
            // This means that traversing outgoing edges result in a node-set (for the semi-circle's edge) that increases monotonically over iterations. Symmetrically
            // the set for incoming edge traversal decreases monotonically. The example below adds bracketed topological labels; note that for outgoing edges we initially
            // have [4, 4] and then [5, 7], and for incoming we first have [11, 11] and then [8, 10]. With this in mind we can see it is futile to traverse from node 10 to node 1,
            // when expanding the incoming range since that node is on the 'wrong side' of the outgoing range already.
            //     X[3]\     />●[7]           ●[9]>\     />X[12]
            //          >F[4] --> ●[6] --> ●[8] --> T[11]
            //  /->X[2]/     \>●[5]         ->●[10]>/    \>X[13]
            // X[1] -----------------------/
            RangedNodeSet incomingRangeSet = pooledSetA;
            incomingRangeSet.SetSingular(to);
            var outgoingRangeSet = pooledSetB;
            outgoingRangeSet.SetSingular(from);

            var swap = pooledSetC;
            swap.Clear();

            bool toggle = false;

            // Loop condition is effectively !incomingRangeSet.IsEmpty && !outgoingRangeSet.IsEmpty, but checked as one of the ranges changes.
            while (true)
            {
                Func<IReadonlyDirectedGraph, NodeId, IEnumerable<Edge>> getEdges;
                Func<NodeId, NodeId, bool> validateEdgeTopoProperty;
                RangedNodeSet walkFromNodes;
                RangedNodeSet intersectWith;
                NodeRange incidentNodeFilter;

                if (toggle)
                {
                    // Decreasing from 'to' to NodeId.Min
                    getEdges = (g, n) => g.GetIncomingEdges(n);
                    validateEdgeTopoProperty = (node, other) => node.Value > other.Value;
                    incidentNodeFilter = NodeRange.CreateLowerBound(outgoingRangeSet.Range.FromInclusive);
                    walkFromNodes = incomingRangeSet;
                    intersectWith = outgoingRangeSet;
                }
                else
                {
                    // Increasing from 'from' to NodeId.Max
                    getEdges = (g, n) => g.GetOutgoingEdges(n);
                    validateEdgeTopoProperty = (node, other) => node.Value < other.Value;
                    incidentNodeFilter = NodeRange.CreateUpperBound(incomingRangeSet.Range.ToInclusive);
                    walkFromNodes = outgoingRangeSet;
                    intersectWith = incomingRangeSet;
                }

                NodeId intersection;
                NodeRange range;
                if (RangeIncidentNodesAndIntersect(
                    graph,
                    walkFromNodes,
                    getEdges,
                    validateEdgeTopoProperty,
                    incidentNodeFilter,
                    intersectWith,
                    skipOutOfOrderNodes,
                    range: out range,
                    intersection: out intersection))
                {
                    return true;
                }

                if (range.IsEmpty)
                {
                    break;
                }

                swap.ClearAndSetRange(range);
                AddIncidentNodes(graph, walkFromNodes, getEdges, validateEdgeTopoProperty, incidentNodeFilter, skipOutOfOrderNodes, swap);

                if (toggle)
                {
                    RangedNodeSet temp = incomingRangeSet;
                    incomingRangeSet = swap;
                    swap = temp;
                }
                else
                {
                    RangedNodeSet temp = outgoingRangeSet;
                    outgoingRangeSet = swap;
                    swap = temp;
                }

                toggle = !toggle;
            }

            return false;
        }

        private static bool RangeIncidentNodesAndIntersect(
            IReadonlyDirectedGraph graph,
            RangedNodeSet walkFromNodes,
            Func<IReadonlyDirectedGraph, NodeId, IEnumerable<Edge>> getEdges,
            Func<NodeId, NodeId, bool> validateEdgeTopoProperty,
            NodeRange incidentNodeFilter,
            RangedNodeSet intersectWith,
            bool skipOutOfOrderNodes,
            out NodeRange range,
            out NodeId intersection)
        {
            // Note that initially, currentMin > currentMax so NodeRange.CreatePossiblyEmpty
            // would return an empty range. We return an empty range iff no nodes pass incidentNodeFilter below.
            uint currentMin = NodeId.MaxValue;
            uint currentMax = NodeId.MinValue;

            foreach (NodeId existingNode in walkFromNodes)
            {
                IEnumerable<Edge> edges = getEdges(graph, existingNode);
                foreach (Edge edge in edges)
                {
                    NodeId other = edge.OtherNode;

                    if (!validateEdgeTopoProperty(existingNode, other))
                    {
                        if (skipOutOfOrderNodes)
                        {
                            continue;
                        }

                        throw new BuildXLException(I($"Topological order violated due to an edge between nodes {existingNode} and {other}"));
                    }

                    if (!incidentNodeFilter.Contains(other))
                    {
                        continue;
                    }

                    if (other.Value > currentMax)
                    {
                        currentMax = edge.OtherNode.Value;
                        Contract.AssertDebug(currentMax <= NodeId.MaxValue && currentMax >= NodeId.MinValue);
                    }

                    if (other.Value < currentMin)
                    {
                        currentMin = edge.OtherNode.Value;
                        Contract.AssertDebug(currentMin <= NodeId.MaxValue && currentMin >= NodeId.MinValue);
                    }

                    if (intersectWith.Contains(other))
                    {
                        intersection = other;
                        Contract.AssertDebug(currentMin <= NodeId.MaxValue && currentMin >= NodeId.MinValue);
                        Contract.AssertDebug(currentMax <= NodeId.MaxValue && currentMax >= NodeId.MinValue);
                        range = NodeRange.CreatePossiblyEmpty(new NodeId(currentMin), new NodeId(currentMax));
                        return true;
                    }
                }
            }

            intersection = NodeId.Invalid;
            Contract.AssertDebug(currentMin <= NodeId.MaxValue && currentMin >= NodeId.MinValue);
            Contract.AssertDebug(currentMax <= NodeId.MaxValue && currentMax >= NodeId.MinValue);
            range = NodeRange.CreatePossiblyEmpty(new NodeId(currentMin), new NodeId(currentMax));
            return false;
        }

        private static void AddIncidentNodes(
            IReadonlyDirectedGraph graph,
            RangedNodeSet walkFromNodes,
            Func<IReadonlyDirectedGraph, NodeId, IEnumerable<Edge>> getEdges,
            Func<NodeId, NodeId, bool> validateEdgeTopoProperty,
            NodeRange incidentNodeFilter,
            bool skipOutOfOrderNodes,
            RangedNodeSet addTo)
        {
            Contract.Requires(!incidentNodeFilter.IsEmpty);

            foreach (NodeId existingNode in walkFromNodes)
            {
                IEnumerable<Edge> edges = getEdges(graph, existingNode);
                foreach (Edge edge in edges)
                {
                    NodeId other = edge.OtherNode;
                    if (skipOutOfOrderNodes && !validateEdgeTopoProperty(existingNode, other))
                    {
                        continue;
                    }

                    if (incidentNodeFilter.Contains(other))
                    {
                        addTo.Add(edge.OtherNode);
                    }
                }
            }
        }
    }
}
