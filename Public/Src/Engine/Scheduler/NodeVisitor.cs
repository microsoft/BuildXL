// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities;

namespace BuildXL.Scheduler
{
    internal delegate bool VisitNode(NodeId node);

    /// <summary>
    /// Encapsulates transitive visitation functions against a graph
    /// </summary>
    internal sealed class NodeVisitor
    {
        private readonly IReadonlyDirectedGraph m_dataflowGraph;

        private readonly ObjectPool<Queue<NodeId>> m_nodeQueuePool = new ObjectPool<Queue<NodeId>>(() => new Queue<NodeId>(), queue => queue.Clear());

        /// <summary>
        /// Creates a new node visitor
        /// </summary>
        /// <param name="dataflowGraph">the dataflow graph</param>
        public NodeVisitor(IReadonlyDirectedGraph dataflowGraph)
        {
            m_dataflowGraph = dataflowGraph;
        }

        /// <summary>
        /// Visits the transitive reachable nodes of the given nodes
        /// </summary>
        public void VisitTransitiveReachableNodes(
            IEnumerable<NodeId> startNodes,
            VisitationTracker visitedNodes,
            VisitNode visitNodeDependencies,
            VisitNode visitNodeDependents)
        {
            using (var queueWrapper = m_nodeQueuePool.GetInstance())
            {
                var queue = queueWrapper.Instance;
                AddIfNotVisited(queue, startNodes, visitedNodes);

                VisitTransitiveClosure(
                    queue,
                    visitedNodes,
                    visitNodeDependencies: visitNodeDependencies,
                    visitNodeDependents: visitNodeDependents,
                    dependencies: true,
                    dependents: true);
            }
        }

        /// <summary>
        /// Visits the transitive dependencies of the nodes
        /// </summary>
        public void VisitTransitiveDependencies(
            IEnumerable<NodeId> startNodes,
            VisitationTracker visitedNodes,
            VisitNode visitNode)
        {
            using (var queueWrapper = m_nodeQueuePool.GetInstance())
            {
                var queue = queueWrapper.Instance;
                AddIfNotVisited(queue, startNodes, visitedNodes);

                VisitTransitiveClosure(
                    queue,
                    visitedNodes,
                    visitNodeDependents: null,
                    visitNodeDependencies: visitNode,
                    dependencies: true,
                    dependents: false);
            }
        }

        /// <summary>
        /// Visits the transitive dependencies of the node
        /// </summary>
        public void VisitTransitiveDependencies(
            NodeId startNode,
            VisitationTracker visitedNodes,
            VisitNode visitNode)
        {
            using (var queueWrapper = m_nodeQueuePool.GetInstance())
            {
                var queue = queueWrapper.Instance;
                AddIfNotVisited(queue, startNode, visitedNodes);

                VisitTransitiveClosure(
                    queue,
                    visitedNodes,
                    visitNodeDependents: null,
                    visitNodeDependencies: visitNode,
                    dependencies: true,
                    dependents: false);
            }
        }

        /// <summary>
        /// Visits the transitive dependents of the nodes
        /// </summary>
        public void VisitTransitiveDependents(
            IEnumerable<NodeId> startNodes,
            VisitationTracker visitedNodes,
            VisitNode visitNode)
        {
            using (var queueWrapper = m_nodeQueuePool.GetInstance())
            {
                var queue = queueWrapper.Instance;
                AddIfNotVisited(queue, startNodes, visitedNodes);

                VisitTransitiveClosure(
                    queue,
                    visitedNodes,
                    visitNodeDependents: visitNode,
                    visitNodeDependencies: null,
                    dependencies: false,
                    dependents: true);
            }
        }

        /// <summary>
        /// Visits the transitive dependents of the node
        /// </summary>
        public void VisitTransitiveDependents(
            NodeId startNode,
            VisitationTracker visitedNodes,
            VisitNode visitNode)
        {
            using (var queueWrapper = m_nodeQueuePool.GetInstance())
            {
                var queue = queueWrapper.Instance;
                AddIfNotVisited(queue, startNode, visitedNodes);

                VisitTransitiveClosure(
                    queue,
                    visitedNodes,
                    visitNodeDependents: visitNode,
                    visitNodeDependencies: null,
                    dependencies: false,
                    dependents: true);
            }
        }

        private static void AddIfNotVisited(Queue<NodeId> queue, NodeId node, VisitationTracker visitedNodes)
        {
            if (visitedNodes.MarkVisited(node))
            {
                queue.Enqueue(node);
            }
        }

        private static void AddIfNotVisited(Queue<NodeId> queue, IEnumerable<NodeId> nodes, VisitationTracker visitedNodes)
        {
            foreach (var node in nodes)
            {
                if (visitedNodes.MarkVisited(node))
                {
                    queue.Enqueue(node);
                }
            }
        }

        private void VisitTransitiveClosure(
            Queue<NodeId> queue,
            VisitationTracker visitedNodes,
            VisitNode visitNodeDependencies,
            VisitNode visitNodeDependents,
            bool dependencies,
            bool dependents)
        {
            Contract.Assert(visitNodeDependencies == null || dependencies);
            Contract.Assert(visitNodeDependents == null || dependents);

            while (queue.Count != 0)
            {
                var node = queue.Dequeue();

                if (dependencies)
                {
                    if (visitNodeDependencies == null || visitNodeDependencies(node))
                    {
                        foreach (Edge inEdge in m_dataflowGraph.GetIncomingEdges(node))
                        {
                            if (visitedNodes.MarkVisited(inEdge.OtherNode))
                            {
                                queue.Enqueue(inEdge.OtherNode);
                            }
                        }
                    }
                }

                if (dependents)
                {
                    if (visitNodeDependents == null || visitNodeDependents(node))
                    {
                        foreach (Edge outEdge in m_dataflowGraph.GetOutgoingEdges(node))
                        {
                            if (visitedNodes.MarkVisited(outEdge.OtherNode))
                            {
                                queue.Enqueue(outEdge.OtherNode);
                            }
                        }
                    }
                }
            }
        }
    }
}
