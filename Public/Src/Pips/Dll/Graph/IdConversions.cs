// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#pragma warning disable SA1649 // File name must match first type name

using BuildXL.Pips.DirectedGraph;

namespace BuildXL.Pips.Graph
{
    /// <summary>
    /// NodeId Utilities
    /// </summary>
    public static class NodeIdExtensions
    {
        /// <summary>
        /// Converts a NodeId to PipId
        /// </summary>
        public static PipId ToPipId(in this NodeId nodeId)
        {
            return new PipId(nodeId.Value);
        }
    }

    /// <summary>
    /// PipId Utilities
    /// </summary>
    public static class PipIdExtensions
    {
        /// <summary>
        /// Converts a PipId to NodeId
        /// </summary>
        public static NodeId ToNodeId(in this PipId pipId)
        {
            return new NodeId(pipId.Value);
        }
    }
}
