// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

#pragma warning disable SA1649 // File name must match first type name

namespace TypeScript.Net.Types
{
    /// <summary>
    /// Specifies the node type.
    /// </summary>
    public enum NodeType
    {
        /// <summary>
        /// Marks the interface as leaf node, e.g. IForInStatement.
        /// Leaf nodes are nodes that can be constructed and have a syntactical representation.
        /// </summary>
        Leaf,

        /// <summary>
        /// Marks the interface as an abstract node, e.g. IClassLikeDeclaration.
        /// Abstract nodes are used for abstraction only and have no syntactical representation.
        /// Abstract interfaces nodes are partially implemented by abstract classes.
        /// </summary>
        Abstract,

        /// <summary>
        /// Like <see cref="NodeType.Abstract" />, however, marker interfaces have no implementation at all.
        /// </summary>
        Marker,
    }

    /// <summary>
    /// Attribute that is used by code gen analyzer to generate valid parse tree.
    /// </summary>
    [AttributeUsage(AttributeTargets.Interface)]
    public sealed class NodeInfoAttribute : Attribute
    {
        /// <nodoc/>
        public SyntaxKind[] SyntaxKinds { get; set; }

        /// <nodoc/>
        public NodeType NodeType { get; set; }
    }
}
