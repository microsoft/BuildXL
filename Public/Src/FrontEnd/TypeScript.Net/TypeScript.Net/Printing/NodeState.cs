// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
