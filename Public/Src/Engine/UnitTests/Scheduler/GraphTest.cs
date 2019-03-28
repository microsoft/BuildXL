// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities;
using Test.BuildXL.TestUtilities;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit;
using Xunit.Abstractions;

namespace Test.BuildXL.Scheduler
{
    public sealed class GraphTest : BuildXL.TestUtilities.Xunit.XunitBuildXLTest
    {
        public GraphTest(ITestOutputHelper output)
            : base(output) { }

        [Fact]
        public void TestGraphConstruction()
        {
            MutableDirectedGraph graph;
            NodeId[] nodes;

            CreateGraph(out graph, out nodes);

            XAssert.AreEqual(graph.NodeCount, nodes.Length);
            XAssert.AreEqual(graph.EdgeCount, 12);

            XAssert.IsTrue(graph.ContainsEdge(nodes[11], nodes[8]));
            XAssert.IsFalse(graph.ContainsEdge(nodes[11], nodes[2]));

            XAssert.IsTrue(graph.IsSinkNode(nodes[0]));
            XAssert.IsTrue(graph.IsSinkNode(nodes[1]));
            XAssert.IsTrue(graph.IsSinkNode(nodes[2]));
            XAssert.IsTrue(graph.IsSinkNode(nodes[3]));
            XAssert.IsTrue(graph.IsSinkNode(nodes[4]));
            XAssert.IsTrue(graph.IsSourceNode(nodes[7]));
            XAssert.IsTrue(graph.IsSourceNode(nodes[10]));
            XAssert.IsTrue(graph.IsSourceNode(nodes[11]));

            XAssert.IsFalse(graph.IsSourceNode(nodes[6]));
            XAssert.IsFalse(graph.IsSinkNode(nodes[6]));

            VerifyNodeHeights(graph, nodes);
        }

        [Fact]
        public void TestGraphConstructionWithLightEdges()
        {
            MutableDirectedGraph graph;
            NodeId[] nodes;

            CreateGraphWithLightEdges(out graph, out nodes);

            XAssert.AreEqual(graph.NodeCount, nodes.Length);
            XAssert.AreEqual(graph.EdgeCount, 5);

            XAssert.IsTrue(graph.ContainsEdge(nodes[0], nodes[1], isLight: true));
            XAssert.IsTrue(graph.ContainsEdge(nodes[0], nodes[1], isLight: false));
            XAssert.IsFalse(graph.ContainsEdge(nodes[1], nodes[0], isLight: true));
            XAssert.IsFalse(graph.ContainsEdge(nodes[1], nodes[0], isLight: false));
            XAssert.IsFalse(graph.ContainsEdge(nodes[1], nodes[3], isLight: true));

            XAssert.IsTrue(graph.IsSinkNode(nodes[3]));
            XAssert.IsFalse(graph.IsSourceNode(nodes[3]), "Has an incoming light edge");
        }

        [Fact]
        public void TestGraphTraversal()
        {
            MutableDirectedGraph graph;
            NodeId[] nodes;

            CreateGraph(out graph, out nodes);

            var succOfNode9 = new HashSet<Edge>(graph.GetOutgoingEdges(nodes[9]));
            XAssert.IsTrue(
                EdgeSetEqual(
                    succOfNode9,
                    new HashSet<Edge> {new Edge(nodes[6]), new Edge(nodes[3]), new Edge(nodes[4])}));

            var predOfNode8 = new HashSet<Edge>(graph.GetIncomingEdges(nodes[8]));
            XAssert.IsTrue(
                EdgeSetEqual(
                    predOfNode8,
                    new HashSet<Edge> {new Edge(nodes[10]), new Edge(nodes[11])}));

            foreach (NodeId s in graph.GetSourceNodes())
            {
                XAssert.IsTrue(graph.IsSourceNode(s));
            }

            foreach (NodeId s in graph.GetSinkNodes())
            {
                XAssert.IsTrue(graph.IsSinkNode(s));
            }
        }

        [Fact]
        public void TestGraphTraversalWithLightEdges()
        {
            MutableDirectedGraph graph;
            NodeId[] nodes;

            CreateGraphWithLightEdges(out graph, out nodes);

            var succOfNode0 = new HashSet<Edge>(graph.GetOutgoingEdges(nodes[0]));
            XAssert.IsTrue(
                EdgeSetEqual(
                    succOfNode0,
                    new HashSet<Edge> {new Edge(nodes[1], isLight: true), new Edge(nodes[1], isLight: false)}));

            var succOfNode1 = new HashSet<Edge>(graph.GetOutgoingEdges(nodes[1]));
            XAssert.IsTrue(
                EdgeSetEqual(
                    succOfNode1,
                    new HashSet<Edge> {new Edge(nodes[2], isLight: true), new Edge(nodes[3], isLight: false)}));
        }

        [Fact]
        public void TestGraphEnumeration()
        {
            MutableDirectedGraph graph;
            NodeId[] nodes;

            CreateGraph(out graph, out nodes);

            var nodeSet = new HashSet<NodeId>(graph.Nodes);
            XAssert.AreEqual(nodeSet.Count, nodes.Length);
            XAssert.IsTrue(NodeSetEqual(new HashSet<NodeId>(nodes), nodeSet));
        }

        [Fact]
        public async Task TestGraphSerialization()
        {
            using (var stream = new MemoryStream())
            {
                MutableDirectedGraph graph;
                NodeId[] nodes;

                CreateGraph(out graph, out nodes);

                var writer = new BuildXLWriter(debug: false, stream: stream, leaveOpen: true, logStats: false);
                graph.Serialize(writer);

                MutableDirectedGraph newMutableGraph;
                stream.Position = 0;
                var reader = new BuildXLReader(debug: false, stream: stream, leaveOpen: true);
                newMutableGraph = MutableDirectedGraph.Deserialize(reader);

                DeserializedDirectedGraph newImmutableDirectedGraph;
                stream.Position = 0;
                newImmutableDirectedGraph = await DeserializedDirectedGraph.DeserializeAsync(reader);

                XAssert.IsTrue(newMutableGraph.ContainsEdge(nodes[11], nodes[8]));
                XAssert.IsFalse(newMutableGraph.ContainsEdge(nodes[11], nodes[2]));

                TestGraphSerializationPerformCommonValidations(newImmutableDirectedGraph, nodes, graph);
                TestGraphSerializationPerformCommonValidations(newMutableGraph, nodes, graph);
            }
        }

        private void VerifyNodeHeights(IReadonlyDirectedGraph graph, NodeId[] nodes)
        {
            XAssert.AreEqual(0, graph.GetNodeHeight(nodes[7]));
            XAssert.AreEqual(0, graph.GetNodeHeight(nodes[10]));
            XAssert.AreEqual(0, graph.GetNodeHeight(nodes[11]));

            XAssert.AreEqual(1, graph.GetNodeHeight(nodes[8]));
            XAssert.AreEqual(1, graph.GetNodeHeight(nodes[9]));

            XAssert.AreEqual(2, graph.GetNodeHeight(nodes[3]));
            XAssert.AreEqual(2, graph.GetNodeHeight(nodes[4]));
            XAssert.AreEqual(2, graph.GetNodeHeight(nodes[5]));
            XAssert.AreEqual(2, graph.GetNodeHeight(nodes[6]));

            XAssert.AreEqual(3, graph.GetNodeHeight(nodes[0]));
            XAssert.AreEqual(3, graph.GetNodeHeight(nodes[1]));
            XAssert.AreEqual(3, graph.GetNodeHeight(nodes[2]));
        }

        private void TestGraphSerializationPerformCommonValidations(DirectedGraph graph, NodeId[] nodes, IReadonlyDirectedGraph sourceGraph)
        {
            XAssert.AreEqual(graph.NodeCount, nodes.Length);
            XAssert.AreEqual(graph.EdgeCount, 12);

            XAssert.IsTrue(graph.IsSinkNode(nodes[0]));
            XAssert.IsTrue(graph.IsSinkNode(nodes[1]));
            XAssert.IsTrue(graph.IsSinkNode(nodes[2]));
            XAssert.IsTrue(graph.IsSinkNode(nodes[3]));
            XAssert.IsTrue(graph.IsSinkNode(nodes[4]));
            XAssert.IsTrue(graph.IsSourceNode(nodes[7]));
            XAssert.IsTrue(graph.IsSourceNode(nodes[10]));
            XAssert.IsTrue(graph.IsSourceNode(nodes[11]));

            XAssert.IsFalse(graph.IsSourceNode(nodes[6]));
            XAssert.IsFalse(graph.IsSinkNode(nodes[6]));

            foreach (var node in sourceGraph.Nodes)
            {
                XAssert.IsTrue(EdgeSetEqual(new HashSet<Edge>(sourceGraph.GetOutgoingEdges(node)), new HashSet<Edge>(graph.GetOutgoingEdges(node))));
                XAssert.IsTrue(EdgeSetEqual(new HashSet<Edge>(sourceGraph.GetIncomingEdges(node)), new HashSet<Edge>(graph.GetIncomingEdges(node))));
            }

            VerifyNodeHeights(graph, nodes);

            var succOfNode9 = new HashSet<Edge>(graph.GetOutgoingEdges(nodes[9]));
            XAssert.IsTrue(
                EdgeSetEqual(
                    succOfNode9,
                    new HashSet<Edge>
                    {
                        new Edge(nodes[6]),
                        new Edge(nodes[3]),
                        new Edge(nodes[4])
                    }));

            var predOfNode8 = new HashSet<Edge>(graph.GetIncomingEdges(nodes[8]));
            XAssert.IsTrue(
                EdgeSetEqual(
                    predOfNode8,
                    new HashSet<Edge>
                    {
                        new Edge(nodes[10]),
                        new Edge(nodes[11])
                    }));
        }

        [Fact]
        public void TestGraphSerializationWithLightEdges()
        {
            using (var tempStorage = new TempFileStorage(canGetFileNames: true, rootPath: TestOutputDirectory))
            {
                MutableDirectedGraph graph;
                NodeId[] nodes;

                CreateGraphWithLightEdges(out graph, out nodes);

                string fileName = tempStorage.GetUniqueFileName();
                using (FileStream fileStream = File.Open(fileName, FileMode.Create))
                {
                    var writer = new BuildXLWriter(debug: false, stream: fileStream, leaveOpen: false, logStats: false);
                    graph.Serialize(writer);
                }

                MutableDirectedGraph newGraph;

                using (FileStream fileStream = File.Open(fileName, FileMode.Open))
                {
                    var reader = new BuildXLReader(debug: false, stream: fileStream, leaveOpen: false);
                    newGraph = MutableDirectedGraph.Deserialize(reader);
                }

                XAssert.AreEqual(newGraph.NodeCount, nodes.Length);
                XAssert.AreEqual(newGraph.EdgeCount, 5);

                XAssert.IsTrue(newGraph.ContainsEdge(nodes[0], nodes[1], isLight: true));
                XAssert.IsTrue(newGraph.ContainsEdge(nodes[0], nodes[1], isLight: false));
                XAssert.IsFalse(newGraph.ContainsEdge(nodes[1], nodes[0], isLight: true));
                XAssert.IsFalse(newGraph.ContainsEdge(nodes[1], nodes[0], isLight: false));
                XAssert.IsFalse(newGraph.ContainsEdge(nodes[1], nodes[3], isLight: true));
            }
        }

        [Fact]
        public async Task TestGraphFailSerialization()
        {
            using (var stream = new MemoryStream())
            {
                var writer = new BinaryWriter(stream);
                writer.Write(7);
                writer.Write(7);

                stream.Position = 0;

                bool encounteredAcceptableException = false;
                try
                {
                    DeserializedDirectedGraph deserialized =
                        await DeserializedDirectedGraph.DeserializeAsync(new BuildXLReader(debug: false, stream: stream, leaveOpen: false));
                }
                catch (IOException)
                {
                    encounteredAcceptableException = true;
                }
                catch (BuildXLException)
                {
                    encounteredAcceptableException = true;
                }

                XAssert.IsTrue(encounteredAcceptableException);
            }
        }

        /// <summary>
        /// Creates a test graph.
        /// </summary>
        private static void CreateGraph(out MutableDirectedGraph graph, out NodeId[] nodes)
        {
            graph = new MutableDirectedGraph();
            XAssert.AreEqual(graph.NodeCount, 0);
            XAssert.AreEqual(graph.EdgeCount, 0);

            // Test creation.
            nodes = new NodeId[12];
            for (int i = 0; i < nodes.Length; ++i)
            {
                nodes[i] = graph.CreateNode();
            }

            XAssert.AreEqual(graph.NodeCount, nodes.Length);
            XAssert.AreEqual(graph.EdgeCount, 0);

            foreach (NodeId t in nodes)
            {
                XAssert.IsTrue(graph.ContainsNode(t));
                XAssert.IsTrue(graph.IsSourceNode(t));
                XAssert.IsTrue(graph.IsSinkNode(t));
            }

            graph.AddEdge(nodes[5], nodes[0]);
            graph.AddEdge(nodes[5], nodes[1]);
            graph.AddEdge(nodes[6], nodes[1]);
            graph.AddEdge(nodes[6], nodes[2]);
            graph.AddEdge(nodes[7], nodes[5]);
            graph.AddEdge(nodes[8], nodes[5]);
            graph.AddEdge(nodes[9], nodes[6]);
            graph.AddEdge(nodes[9], nodes[3]);
            graph.AddEdge(nodes[9], nodes[4]);
            graph.AddEdge(nodes[10], nodes[8]);
            graph.AddEdge(nodes[11], nodes[8]);
            graph.AddEdge(nodes[11], nodes[9]);
        }

        /// <summary>
        /// Creates a test graph.
        /// There are light edges between nodes 0 - 1, 1 - 2, and 2 - 0.
        /// There are heavy edges between 0 - 1 and 1 - 3
        /// </summary>
        private static void CreateGraphWithLightEdges(out MutableDirectedGraph graph, out NodeId[] nodes)
        {
            graph = new MutableDirectedGraph();
            XAssert.AreEqual(graph.NodeCount, 0);
            XAssert.AreEqual(graph.EdgeCount, 0);

            // Test creation.
            nodes = new NodeId[4];
            for (int i = 0; i < nodes.Length; ++i)
            {
                nodes[i] = graph.CreateNode();
            }

            XAssert.AreEqual(graph.NodeCount, nodes.Length);
            XAssert.AreEqual(graph.EdgeCount, 0);

            foreach (NodeId t in nodes)
            {
                XAssert.IsTrue(graph.ContainsNode(t));
                XAssert.IsTrue(graph.IsSourceNode(t));
                XAssert.IsTrue(graph.IsSinkNode(t));
            }

            graph.AddEdge(nodes[0], nodes[1], isLight: true);
            graph.AddEdge(nodes[1], nodes[2], isLight: true);
            graph.AddEdge(nodes[2], nodes[0], isLight: true);
            graph.AddEdge(nodes[0], nodes[1], isLight: false);
            graph.AddEdge(nodes[1], nodes[3], isLight: false);
        }

        /// <summary>
        /// Checks if two node sets are equal.
        /// </summary>
        private static bool NodeSetEqual(HashSet<NodeId> set1, HashSet<NodeId> set2)
        {
            Contract.Requires(set1 != null, "Argument set1 cannot be null");
            Contract.Requires(set2 != null, "Argument set2 cannot be null");

            return set1.IsSubsetOf(set2) && set2.IsSubsetOf(set1);
        }

        /// <summary>
        /// Checks if two edge sets are equal.
        /// </summary>
        private static bool EdgeSetEqual(HashSet<Edge> set1, HashSet<Edge> set2)
        {
            Contract.Requires(set1 != null, "Argument set1 cannot be null");
            Contract.Requires(set2 != null, "Argument set2 cannot be null");

            return set1.IsSubsetOf(set2) && set2.IsSubsetOf(set1);
        }
    }
}
