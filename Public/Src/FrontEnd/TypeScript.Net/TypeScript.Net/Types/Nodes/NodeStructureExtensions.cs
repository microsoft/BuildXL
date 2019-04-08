// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;
using TypeScript.Net.Parsing;

namespace TypeScript.Net.Types
{
    /// <nodoc/>
    public static class NodeStructureExtensions
    {
        /// <summary>
        /// Gets all child nodes of <paramref name="node"/>.
        /// </summary>
        /// <param name="node">The node.</param>
        /// <returns>All child nodes.</returns>
        public static IEnumerable<INode> GetChildNodes(this INode node)
        {
            var l = new List<INode>();
            NodeWalker.ForEachChild<object>(node, child =>
            {
                l.Add(child.ResolveUnionType());
                return null;
            });

            return l;
        }

        /// <summary>
        /// Gets all descendant nodes of  <paramref name="node"/> including  <paramref name="node"/>.
        /// </summary>
        /// <param name="node">The node.</param>
        /// <returns>All descendant nodes and self.</returns>
        public static IEnumerable<INode> GetSelfAndDescendantNodes(this INode node)
        {
            yield return node;

            foreach (var child in node.GetChildNodes())
            {
                foreach (var c in child.GetSelfAndDescendantNodes())
                {
                    yield return c.ResolveUnionType();
                }
            }
        }

        /// <summary>
        /// Gets all proper descendant nodes of  <paramref name="node"/>.
        /// </summary>
        /// <returns>All proper descendant nodes.</returns>
        public static IEnumerable<INode> GetDescendantNodes(this INode node)
        {
            foreach (var child in node.GetChildNodes())
            {
                foreach (var c in child.GetSelfAndDescendantNodes())
                {
                    yield return c;
                }
            }
        }

        /// <summary>
        /// Returns the source file that contains the node or null.
        /// </summary>
        [CanBeNull]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ISourceFile GetSourceFile([CanBeNull] this INode node)
        {
            return node?.SourceFile ?? GetSourceFileSlow(node);
        }

        /// <summary>
        /// Returns the source file for the given node by traversing node's parents.
        /// </summary>
        [CanBeNull]
        internal static ISourceFile GetSourceFileSlow(this INode node)
        {
            while (node != null && node.Kind != SyntaxKind.SourceFile)
            {
                node = node?.Parent;
            }

            return (ISourceFile)node;
        }

        /// <nodoc/>
        public static void Replace(this INode node, INode newNode)
        {
            ReplaceChild(node.Parent, node, newNode);
        }

        private static object DynamicCast(object source, Type destType)
        {
            var srcType = source.GetType();
            if (destType.IsAssignableFrom(srcType))
            {
                return source;
            }

            var paramTypes = new[] { srcType };
            var cast = destType.GetMethod("op_Implicit", paramTypes)
                ?? destType.GetMethod("op_Explicit", paramTypes);

            if (cast != null)
            {
                return cast.Invoke(null, new[] { source });
            }

            if (destType.IsEnum)
            {
                return Enum.ToObject(destType, source);
            }

            throw new InvalidCastException();
        }

        /// <summary>
        /// Replaces <paramref name="oldNode"/> with <paramref name="newNode"/>.
        /// Throws an exception if <paramref name="oldNode"/> is not a direct child of <paramref name="node"/>.
        /// </summary>
        public static void ReplaceChild(this INode node, INode oldNode, INode newNode)
        {
            var actualNode = node.Cast<INode>();

            // using reflection for find and replace, as node walker does not provide a write access.
            var type = actualNode.GetType();

            foreach (var member in type.GetMembers(BindingFlags.Instance | BindingFlags.SetProperty | BindingFlags.FlattenHierarchy
                | BindingFlags.GetProperty | BindingFlags.Public | BindingFlags.NonPublic).OfType<PropertyInfo>())
            {
                var value = member.GetValue(actualNode);

                if (value is INodeArray<INode>)
                {
                    var arr = (INodeArray<INode>)value;
                    for (var i = 0; i < arr.Count; i++)
                    {
                        var element = arr[i];
                        if (Equals(element, oldNode))
                        {
                            dynamic arrD = arr;
                            dynamic newValue = DynamicCast(newNode, arr.GetType().GenericTypeArguments.First());
                            arrD.UnsafeMutableElementsForDynamicAccess[i] = newValue;
                            return;
                        }
                    }
                }

                if (Equals(value, oldNode))
                {
                    // we may need to wrap the node in an union type wrapper
                    var newValue = DynamicCast(newNode, member.PropertyType);
                    member.SetValue(actualNode, newValue);
                    return;
                }
            }

            throw new InvalidOperationException("oldNode is not a member of node or a union type member!");
        }
    }
}
