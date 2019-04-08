// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.Threading;
using BuildXL.Utilities.Collections;

namespace BuildXL.Scheduler.Graph
{
    /// <summary>
    /// Allows for tracking of nodes that have been visited in a graph traversal.
    /// NOTE: All methods on this object are thread safe except <see cref="UnsafeReset"/>.
    /// </summary>
    public sealed class VisitationTracker
    {
        private readonly ConcurrentBitArray m_visited;

        private int m_visitedCount;

        /// <summary>
        /// The number of nodes which have been visited
        /// </summary>
        public int VisitedCount => Volatile.Read(ref m_visitedCount);

        /// <summary>
        /// Creates a VisitationTracker for a specific graph. Only valid while that graph remains unchanged.
        /// </summary>
        public VisitationTracker(IReadonlyDirectedGraph graph)
        {
            Contract.Requires(graph != null);

            m_visited = new ConcurrentBitArray(graph.NodeCount);
        }

        /// <summary>
        /// Marks a node as being visited. Returns whether the node had previously been visited. Only valid for use by
        /// nodes that were in the graph at the time this VisitationTracker was created.
        /// </summary>
        public bool MarkVisited(NodeId node)
        {
            Contract.Requires(node.IsValid);
            if (m_visited.TrySet((int)(node.Value - 1), true))
            {
                Interlocked.Increment(ref m_visitedCount);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Checks to see if a node has been visited
        /// </summary>
        public bool WasVisited(NodeId node)
        {
            Contract.Requires(node.IsValid);

            return m_visited[(int)(node.Value - 1)];
        }

        /// <summary>
        /// Marks all nodes as unvisited.
        /// </summary>
        public void UnsafeReset()
        {
            m_visited.UnsafeClear();
            m_visitedCount = 0;
        }
    }
}
