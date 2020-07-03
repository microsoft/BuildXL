// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace TypeScript.Net.Printing
{
    internal class NodeState
    {
        public NodeOrNodesOrNull OriginalNode { get; }

        public NodeState[] Children { get; }

        public NodeState(NodeOrNodesOrNull originalNode, NodeState[] children)
        {
            OriginalNode = originalNode;
            Children = children;
        }
    }
}
