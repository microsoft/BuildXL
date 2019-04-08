// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.CompilerServices;
using JetBrains.Annotations;

namespace TypeScript.Net.Types
{
    /// <summary>
    /// Set of extension methods related for union types in the AST.
    /// </summary>
    public static class UnionNodeExtension
    {
        /// <summary>
        /// Helper function that unwraps the node from the union type if needed.
        /// </summary>
        /// <remarks>
        /// This helper is particularly useful for checking node's identity.
        /// Each node could be wrapped into the union type that makes reference identity invalid by default.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static INode ResolveUnionType([CanBeNull]this INode node)
        {
            return node?.GetActualNode();

            // while (node is IUnionNode)
            // {
            //    node = ((IUnionNode) node).Node;
            // }

            // return node;
        }
    }
}
