// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Core;

namespace BuildXL.Pips.DirectedGraph
{
    /// <summary>
    /// Tracks which nodes have been visited during a graph traversal.
    /// </summary>
    internal interface IVisitationTracker
    {
        /// <summary>
        /// Marks a node as visited. Returns true only if the node was not previously visited, i.e., the caller is the one
        /// that should process it. Implementations may refuse to visit some nodes by always returning false for them.
        /// </summary>
        bool MarkVisited(NodeId node);
    }
}
