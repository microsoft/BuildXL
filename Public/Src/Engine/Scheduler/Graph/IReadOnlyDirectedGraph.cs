// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.Utilities;
using JetBrains.Annotations;

namespace BuildXL.Scheduler.Graph
{
    /// <summary>
    /// Directed graph with unweighted edges. Edges are tagged as either 'light' or 'heavy'
    /// (note that from some node A to B, there may be both a light and a heavy edge).
    /// </summary>
    public interface IReadonlyDirectedGraph
    {
        /// <summary>
        /// Gets edges pointing to successor nodes.
        /// </summary>
        /// <remarks>This method returns an empty enumerable if node doesn't exist</remarks>
        [NotNull]
        IEnumerable<Edge> GetOutgoingEdges(NodeId node);

        /// <summary>
        /// Gets edges pointing to predecessor nodes.
        /// </summary>
        /// <remarks>This method returns an empty enumerable if node doesn't exist</remarks>
        [NotNull]
        IEnumerable<Edge> GetIncomingEdges(NodeId node);

        /// <summary>
        /// Computes <code>GetIncomingEdges(nodeId).Count(edge => !edge.IsLight)</code> efficiently.
        /// </summary>
        /// WIP: result > 1
        int CountIncomingHeavyEdges(NodeId node);

        /// <summary>
        /// Computes <code>GetOutgoingEdges(nodeId).Count(edge => !edge.IsLight)</code> efficiently.
        /// </summary>
        /// WIP: result > 1
        int CountOutgoingHeavyEdges(NodeId node);

        /// <summary>
        /// Gets the dependency chain height of the node
        /// </summary>
        /// <remarks>
        /// The value 0 indicates the node has no dependencies.
        /// </remarks>
        [System.Diagnostics.Contracts.Pure]
        int GetNodeHeight(NodeId node);

        /// <summary>
        /// Gets count of edges pointing to predecessor nodes.
        /// </summary>
        int GetIncomingEdgesCount(NodeId node);

        /// <summary>
        /// Gets source nodes of the graph.
        /// </summary>
        IEnumerable<NodeId> GetSourceNodes();

        /// <summary>
        /// Get sink nodes of the graph.
        /// </summary>
        IEnumerable<NodeId> GetSinkNodes();

        /// <summary>
        /// Saves this graph on a given binary stream.
        /// </summary>
        void Serialize([NotNull]BuildXLWriter writer);

        /// <summary>
        /// Checks if a node is valid.
        /// </summary>
        [System.Diagnostics.Contracts.Pure]
        bool IsValidNodeId(NodeId node);

        /// <summary>
        /// Checks if graph contains a node
        /// </summary>
        [System.Diagnostics.Contracts.Pure]
        bool ContainsNode(NodeId node);

        /// <summary>
        /// Checks if a node is a source node.
        /// </summary>
        /// <remarks>A node is a source node if it has no incoming edge</remarks>
        [System.Diagnostics.Contracts.Pure]
        bool IsSourceNode(NodeId node);

        /// <summary>
        /// Checks if a node is a sink node.
        /// </summary>
        /// <remarks>A node is a sink node if it has no outgoing edge</remarks>
        [System.Diagnostics.Contracts.Pure]
        bool IsSinkNode(NodeId node);

        /// <summary>
        /// Number of nodes.
        /// </summary>
        int NodeCount { get; }

        /// <summary>
        /// Gets the range of valid node IDs.
        /// </summary>
        NodeRange NodeRange { get; }

        /// <summary>
        /// List of nodes.
        /// </summary>
        IEnumerable<NodeId> Nodes { get; }

        /// <summary>
        /// List of nodes in reverse creation order.
        /// </summary>
        IEnumerable<NodeId> ReversedNodes { get; }
    }
}
