// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using JetBrains.Annotations;
using TypeScript.Net.Types;
using NotNullAttribute= JetBrains.Annotations.NotNullAttribute;

namespace TypeScript.Net.Printing
{
    /// <summary>
    /// Discriminator for <see cref="NodeOrNodesOrNull"/> union type.
    /// </summary>
    public enum NodeOrNodesOrNullType
    {
        /// <nodoc />
        Node,

        /// <nodoc />
        Nodes,

        /// <nodoc />
        Null,
    }

    /// <nodoc/>
    public readonly struct NodeOrNodesOrNull
    {
        /// <nodoc/>
        public INode Node { get; }

        /// <nodoc/>
        public INodeArray<INode> Nodes { get; }

        /// <nodoc/>
        public NodeOrNodesOrNull([NotNull] INode node)
        {
            Contract.Requires(node != null);

            Node = node;
            Nodes = null;
        }

        /// <nodoc/>
        public NodeOrNodesOrNull([NotNull] INodeArray<INode> nodes)
        {
            Contract.Requires(nodes != null);
            Node = null;
            Nodes = nodes;
        }

        /// <nodoc/>
        [SuppressMessage("Microsoft.Naming", "CA1721:PropertyNamesShouldNotMatchGetMethods", Justification = "Type nomenclature is necessary within a compiler.")]
        public NodeOrNodesOrNullType Type
        {
            get
            {
                return Node != null
                    ? NodeOrNodesOrNullType.Node
                    : (Nodes != null ? NodeOrNodesOrNullType.Nodes : NodeOrNodesOrNullType.Null);
            }
        }

        /// <nodoc/>
        public object Value => (object)Node ?? Nodes;
    }
}
