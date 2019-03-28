// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using TypeScript.Net.Types;

namespace TypeScript.Net.Printing
{
    /// <summary>
    /// Holds the state of a abstract syntax tree.
    /// Can be used to detect what has changed.
    /// </summary>
    public sealed class AbstractSyntaxTreeState
    {
        internal IDictionary<INode, NodeState> Nodes { get; }

        private AbstractSyntaxTreeState(IDictionary<INode, NodeState> nodes)
        {
            Contract.Requires(nodes != null);

            Nodes = nodes;
        }

        /// <nodoc/>
        public static AbstractSyntaxTreeState Create(INode tree)
        {
            Contract.Requires(tree != null);

            var dictionary = new Dictionary<INode, NodeState>();
            var n = CreateMyNode(new NodeOrNodesOrNull(tree), dictionary);
            return new AbstractSyntaxTreeState(dictionary);
        }

        private static NodeState CreateMyNode(in NodeOrNodesOrNull node, IDictionary<INode, NodeState> dictionary)
        {
            Contract.Requires(dictionary != null);

            var myNodes = new List<NodeState>();

            foreach (var child in NodeWalkerEx.GetChildNodes(node))
            {
                var m = CreateMyNode(child, dictionary);
                myNodes.Add(m);
            }

            var result = new NodeState(node, myNodes.ToArray());
            if
                (node.Node != null)
            {
                dictionary[node.Node] = result;
            }

            return result;
        }
    }
}
