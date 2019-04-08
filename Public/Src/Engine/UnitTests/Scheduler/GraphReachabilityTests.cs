// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;

namespace Test.BuildXL.Scheduler
{
    /// <summary>
    /// Tests for <see cref="GraphUtilities.IsReachableFrom" />
    /// </summary>
    public class GraphReachabilityTests
    {
        [Fact]
        public void DisconnectedNodes()
        {
            var graph = new MutableDirectedGraph();
            NodeId a = graph.CreateNode();
            NodeId b = graph.CreateNode();

            XAssert.IsFalse(graph.IsReachableFrom(a, b));
            XAssert.IsFalse(graph.IsReachableFrom(b, a));
        }

        [Fact]
        public void TriviallyConnectedNodes()
        {
            var graph = new MutableDirectedGraph();
            NodeId a = graph.CreateNode();
            NodeId b = graph.CreateNode();
            graph.AddEdge(a, b);

            XAssert.IsTrue(graph.IsReachableFrom(a, b));
            XAssert.IsFalse(graph.IsReachableFrom(b, a));
        }

        [Fact]
        public void TrivialViolationOfTopologicalOrder()
        {
            var graph = new MutableDirectedGraph();
            NodeId a = graph.CreateNode();
            NodeId b = graph.CreateNode();
            NodeId c = graph.CreateNode();
            NodeId d = graph.CreateNode();
            graph.AddEdge(a, b);
            graph.AddEdge(d, c); // This edge breaks the topological labelling.

            try
            {
                // Note that to fail, we need c > a here due to a fast-path for 'unreachable'
                // that doesn't look at any edges. 
                graph.IsReachableFrom(a, c);
            }
            catch (BuildXLException)
            {
                return;
            }

            XAssert.Fail("Expected a failure due to a topological order violation");
        }

        [Fact]
        public void TopologicalOrderViolationsIgnoredIfRequested()
        {
            var graph = new MutableDirectedGraph();
            NodeId a = graph.CreateNode();
            NodeId b = graph.CreateNode();
            NodeId c = graph.CreateNode();
            NodeId d = graph.CreateNode();
            graph.AddEdge(a, b);
            graph.AddEdge(d, c); // This edge breaks the topological labelling.

            XAssert.IsFalse(graph.IsReachableFrom(a, c, skipOutOfOrderNodes: true));
        }

        [Fact]
        public void SimpleCycle()
        {
            var graph = new MutableDirectedGraph();
            NodeId a = graph.CreateNode();
            NodeId b = graph.CreateNode();
            NodeId c = graph.CreateNode();
            NodeId d = graph.CreateNode();
            NodeId e = graph.CreateNode();
            NodeId f = graph.CreateNode();
            graph.AddEdge(a, b);
            graph.AddEdge(b, c);
            graph.AddEdge(c, b); // Cycle-forming backedge (violates topo order).
            graph.AddEdge(d, e);
            graph.AddEdge(e, f);

            XAssert.IsTrue(graph.IsReachableFrom(a, b), "Shouldn't visit the backedge");
            XAssert.IsTrue(graph.IsReachableFrom(a, c), "Shouldn't visit the backedge");

            try
            {
                // Note that to fail, we need a > e here due to a fast-path for 'unreachable'
                // that doesn't look at any edges. 
                graph.IsReachableFrom(a, f); // Fails since it should find the backedge c -> b
            }
            catch (BuildXLException)
            {
                return;
            }

            XAssert.Fail("Expected a failure due to a topological order violation");
        }

        [Fact]
        public void MultipleLevels()
        {
            var graph = new MutableDirectedGraph();

            NodeId l0 = graph.CreateNode();

            NodeId l1_n1 = graph.CreateNode();
            NodeId l1_n2 = graph.CreateNode();
            graph.AddEdge(l0, l1_n1);
            graph.AddEdge(l0, l1_n2);
            graph.AddEdge(l1_n1, l1_n2);

            NodeId l2_n1 = graph.CreateNode();
            NodeId l2_n2 = graph.CreateNode();
            graph.AddEdge(l1_n1, l2_n1);
            graph.AddEdge(l1_n2, l2_n2);

            NodeId l3_n1 = graph.CreateNode();
            NodeId l3_n2 = graph.CreateNode();
            graph.AddEdge(l2_n1, l3_n1);
            graph.AddEdge(l2_n2, l3_n2);

            NodeId l4 = graph.CreateNode();
            graph.AddEdge(l3_n1, l4);
            graph.AddEdge(l3_n2, l4);

            XAssert.IsTrue(graph.IsReachableFrom(l1_n1, l3_n2));
            XAssert.IsTrue(graph.IsReachableFrom(l0, l4));

            XAssert.IsFalse(graph.IsReachableFrom(l3_n2, l1_n1));
            XAssert.IsFalse(graph.IsReachableFrom(l4, l0));
        }

        [Fact]
        public void RandomGraph30()
        {
            IReadonlyDirectedGraph graph = CreateRandomAcyclicGraph(new Random(42), nodeCount: 30);
            VerifyReachability(graph);
        }

        private static IReadonlyDirectedGraph CreateRandomAcyclicGraph(Random rng, int nodeCount)
        {
            var graph = new MutableDirectedGraph();
            var nodes = new NodeId[nodeCount];
            for (int i = 0; i < nodeCount; i++)
            {
                nodes[i] = graph.CreateNode();
            }

            for (int i = 0; i < nodeCount; i++)
            {
                int toIndex = i;
                while ((toIndex = rng.Next(toIndex + 1, nodeCount)) < nodeCount)
                {
                    graph.AddEdge(nodes[i], nodes[toIndex]);
                }
            }

            return graph;
        }

        private static void VerifyReachability(IReadonlyDirectedGraph graph)
        {
            foreach (NodeId node in graph.Nodes)
            {
                foreach (NodeId otherNode in graph.Nodes)
                {
                    if (otherNode.Value < node.Value)
                    {
                        continue;
                    }

                    XAssert.AreEqual(
                        IsReachableTrivial(graph, node, otherNode),
                        graph.IsReachableFrom(node, otherNode),
                        "Incorrect reachability between {0} and {1}", node, otherNode);
                }
            }
        }

        /// <summary>
        /// Simple reachability query reference implementation.
        /// </summary>
        private static bool IsReachableTrivial(IReadonlyDirectedGraph graph, NodeId from, NodeId to)
        {
            if (from == to)
            {
                return true;
            }

            foreach (Edge edge in graph.GetOutgoingEdges(from))
            {
                if (IsReachableTrivial(graph, edge.OtherNode, to))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
