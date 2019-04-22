// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using Newtonsoft.Json;
using System.Collections.Generic;
using System;
using BuildXL.Utilities;

namespace BuildXL.Engine.Cache.Serialization
{
    /// <summary>
    /// Represents a JSON object as a tree node.
    /// </summary>
    public class JsonNode
    {
        /// <summary>
        /// The parent of this node.
        /// This is used for printing traversal paths to leaf nodes.
        /// </summary>
        public JsonNode Parent;

        /// <summary>
        /// The JSON property name.
        /// </summary>
        public string Name;

        /// <summary>
        /// The JSON property values.
        /// </summary>
        public List<string> Values;

        /// <summary>
        /// Nested JSON objects.
        /// </summary>
        public LinkedList<JsonNode> Children;

        /// <summary>
        /// Constructor.
        /// </summary>
        public JsonNode()
        {
            Values = new List<string>();
            Children = new LinkedList<JsonNode>();
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            var other = obj as JsonNode;

            JsonNode a = this, b = other;
            while (a != null && b != null)
            {
                if (a.Name != b.Name)
                {
                    return false;
                }

                a = a.Parent;
                b = b.Parent;
            }

            if (a != null || b != null)
            {
                return false;
            }

            if (this.Values.Count != other.Values.Count)
            {
                return false;
            }

            for (int i = 0; i < this.Values.Count; ++i)
            {
                if (this.Values[i] != other.Values[i])
                {
                    return false;
                }
            }

            return true;
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        /// <remarks>
        /// To simplify custom printing for entire trees, this only handles printing <see cref="Values"/>and not <see cref="Name"/>.
        /// </remarks>
        public override string ToString()
        {
            using (var sbPool = Pools.GetStringBuilder())
            {
                var sb = sbPool.Instance;
                if (Values.Count == 1)
                {
                    sb.Append(Values[0]);
                }
                else
                {
                    sb.AppendFormat("\t[{0}]", String.Join(",", Values));
                }

                sb.AppendLine();
                return sb.ToString();
            }

        }
    }

    /// <summary>
    /// Reads a valid JSON object into a tree.
    /// </summary>
    public class JsonTree
    {
        /// <summary>
        /// Given a valid JSON object, builds a tree of <see cref="JsonNode"/>s.
        /// </summary>
        /// <returns>
        /// The root node of the tree built.
        /// </returns>
        public static JsonNode BuildTree(string json)
        {
            var reader = new JsonTextReader(new StringReader(json));
            var parentStack = new Stack<JsonNode>();
            var currentNode = new JsonNode();

            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonToken.StartObject:
                        parentStack.Push(currentNode);
                        break;
                    case JsonToken.PropertyName:
                        var parentNode = parentStack.Peek();
                        currentNode = new JsonNode()
                        {
                            Parent = parentNode,
                            Name = reader.Value.ToString()
                        };
                        parentNode.Children.AddFirst(currentNode);
                        break;
                    case JsonToken.String:
                        currentNode.Values.Add(reader.Value.ToString());
                        break;
                    case JsonToken.EndObject:
                        currentNode = parentStack.Pop();
                        break;
                    case JsonToken.StartArray:
                    case JsonToken.EndArray:
                    default:
                        break;
                }
            }

            return currentNode;
        }

        /// <summary>
        /// Collects all the nodes of a tree that have values.
        /// </summary>
        public static List<JsonNode> CollectValueNodes(JsonNode root)
        {
            var nodeStack = new Stack<JsonNode>();
            var valueNodes = new List<JsonNode>();

            nodeStack.Push(root);

            JsonNode currentNode;
            while (nodeStack.Count != 0)
            {
                currentNode = nodeStack.Pop();

                // Value node
                if (currentNode.Values.Count > 0)
                {
                    valueNodes.Add(currentNode);
                }

                foreach (var child in currentNode.Children)
                {
                    nodeStack.Push(child);
                }
            }

            return valueNodes;
        }

        /// <summary>
        /// Diffs two <see cref="JsonNode"/> trees.
        /// </summary>
        /// <returns>
        /// A string representation of the diff.
        /// </returns>
        public static string PrintTreeDiff(JsonNode rootA, JsonNode rootB)
        {
            var changeList = DiffTrees(rootA, rootB);
            return PrintTreeChangeList(changeList);
        }

        /// <summary>
        /// Formats and prints the resulting <see cref="ChangeList{T}"/> from a <see cref="DiffTrees(JsonNode, JsonNode)"/>.
        /// </summary>
        /// <returns>
        /// A string representation of the change list.
        /// </returns>
        public static string PrintTreeChangeList(ChangeList<JsonNode> changeList)
        {
            var treeToPrint = new PrintNode();
            for (int i = 0; i < changeList.Count; ++i)
            {
                var change = changeList[i];
                var node = change.Value;
                var path = new LinkedList<string>();
                // Use "it.Parent != null" instead of "it != null" to 
                // ignore printing the root parent node that matches the opening bracket
                // for all JSON objects
                for (var it = node; it.Parent != null; it = it.Parent)
                {
                    path.AddFirst(it.Name);
                }

                var printNode = treeToPrint;
                // Build a tree of the change list values, placing nodes based off
                // their positions in the old or new tree
                foreach (var pathAtom in path)
                {
                    if (!printNode.Children.ContainsKey(pathAtom))
                    {
                        printNode.Children.Add(pathAtom, new PrintNode());
                    }

                    printNode = printNode.Children[pathAtom];
                }

                printNode.ChangedNodes.Add(change);
            }

            return treeToPrint.ToString();
        }

        /// <summary>
        /// Diffs two trees.
        /// </summary>
        /// <param name="rootA">
        /// The root of the original tree.
        /// </param>
        /// <param name="rootB">
        /// The root of the transformed tree.
        /// </param>
        /// <returns>
        /// A <see cref="ChangeList{JsonNode}"/> containing
        /// the leaf nodes that differ.
        /// </returns>
        public static ChangeList<JsonNode> DiffTrees(JsonNode rootA, JsonNode rootB)
        {
            var leavesA = CollectValueNodes(rootA);
            var leavesB = CollectValueNodes(rootB);

            var changeList = new ChangeList<JsonNode>(leavesA, leavesB);

            return changeList;
        }

        /// <summary>
        /// Find a node with the give name.
        /// </summary>
        /// <returns>
        /// The first node encountered with a matching name or
        /// null if there are no nodes with a matching name.
        /// </returns>
        public static JsonNode FindNodeByName(JsonNode root, string name)
        {
            if (root == null)
            {
                return null;
            }

            var stack = new Stack<JsonNode>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                var node = stack.Pop();

                if (node.Name == name)
                {
                    return node;
                }

                foreach (var child in node.Children)
                {
                    stack.Push(child);
                }
            }

            return null;
        }

        /// <summary>
        /// Removes a branch from its parent tree, leaving it as an independent tree.
        /// </summary>
        public static void EmancipateBranch(JsonNode root)
        {
            if (root == null)
            {
                return;
            }

            var parent = root.Parent;
            // The branch is the root of the tree, so there's nothing to cut it from
            if (parent == null)
            {
                return;
            }

            parent.Children.Remove(root);
            root.Parent = null;
        }

        /// <summary>
        /// Moves a branch from its current parent to a new parent.
        /// </summary>
        public static void ReparentBranch(JsonNode root, JsonNode newParent)
        {
            if (root == null || newParent == null)
            {
                return;
            }

            EmancipateBranch(root);
            newParent.Children.AddLast(root);
            root.Parent = newParent;
        }

        /// <summary>
        /// Prints a tree.
        /// </summary>
        public static string PrintTree(JsonNode root)
        {
            using (var sbPool = Pools.GetStringBuilder())
            {
                var sb = sbPool.Instance;

                var search = new Stack<(JsonNode n, int d)>();

                // If the root is being used to just point to a bunch of child nodes, skip printing it
                if (root.Name == null)
                {
                    foreach (var child in root.Children)
                    {
                        search.Push((child, 0));
                    }
                }
                else
                {
                    search.Push((root, 0));
                }

                var indentPrefix = "";
                while (search.Count != 0)
                {
                    var (n, d) = search.Pop();

                    indentPrefix = new string('\t', d);

                    sb.AppendLine(string.Format("{0}[{1}]", indentPrefix, n.Name));

                    foreach (var val in n.Values)
                    {
                        // Add appropriate indents for multi-line values
                        var print = val.Replace(Environment.NewLine, Environment.NewLine + "\t" + indentPrefix);
                        sb.AppendLine(string.Format("\t{0}\"{1}\"", indentPrefix, print));
                    }

                    foreach (var child in n.Children)
                    {
                        search.Push((child, d + 1));
                    }
                }

                return sb.ToString();
            }
        }

        /// <summary>
        /// Prints the leaves in a tree and the paths to the leaves. Internal for testing.
        /// </summary>
        internal static string PrintLeaves(JsonNode root)
        {
            using (var sbPool = Pools.GetStringBuilder())
            {
                var sb = sbPool.Instance;
                var nodeStack = new Stack<JsonNode>();
                var nameStack = new Stack<string>();

                nodeStack.Push(root);
                nameStack.Push(root.Name);

                JsonNode currentNode;
                string currentName;

                while (nodeStack.Count != 0)
                {
                    currentNode = nodeStack.Pop();
                    currentName = nameStack.Pop();

                    // Leaf node
                    if (currentNode.Children.Count == 0)
                    {
                        sb.AppendLine();
                        sb.AppendFormat("{0}:", currentName);

                        foreach (var value in currentNode.Values)
                        {
                            sb.AppendFormat("\"{0}\",", value);
                        }
                    }

                    foreach (var child in currentNode.Children)
                    {
                        nodeStack.Push(child);
                        nameStack.Push(currentName + ":" + child.Name);
                    }
                }

                return sb.ToString();
            }
        }

        /// <summary>
        /// Reads a JSON string into a new, formatted JSON string.
        /// This function will keep all JSON properties with the same name, whereas <see cref="Newtonsoft.Json"/>
        /// equivalents consolidate properties of the same name and only includes the last property with a particular name.
        /// </summary>
        /// <remarks>
        /// This is not an efficient function and should be used conservatively for displaying JSON to users in a readable format.
        /// </remarks>
        public static string PrettyPrintJson(string json)
        {
            using (var sbPool = Pools.GetStringBuilder())
            using (var reader = new JsonTextReader(new StringReader(json)))
            {
                using (var writer = new JsonTextWriter(new StringWriter(sbPool.Instance)))
                {
                    writer.Formatting = Formatting.Indented;

                    var parentStack = new Stack<JsonNode>();
                    var currentNode = new JsonNode();

                    while (reader.Read())
                    {
                        switch (reader.TokenType)
                        {
                            case JsonToken.StartObject:
                                writer.WriteStartObject();
                                break;
                            case JsonToken.PropertyName:
                                writer.WritePropertyName(reader.Value.ToString());
                                break;
                            // BuildXL JSON, only allow string values
                            case JsonToken.String:
                                writer.WriteValue(reader.Value.ToString());
                                break;
                            case JsonToken.EndObject:
                                writer.WriteEndObject();
                                break;
                            case JsonToken.StartArray:
                                writer.WriteStartArray();
                                break;
                            case JsonToken.EndArray:
                                writer.WriteEndArray();
                                break;
                            case JsonToken.Comment:
                                writer.WriteComment(reader.Value.ToString());
                                break;
                            default:
                                break;
                        }
                    }

                    // Make sure writer is fully closed and done writing before using the underlying string
                    return sbPool.Instance.ToString();
                }
            }
        }

        /// <summary>
        /// Helper class for printing the result of <see cref="DiffTrees(JsonNode, JsonNode)"/>
        /// in a tree format, organizing <see cref="ChangeList{T}"/> values by their position in the original trees.
        /// Each <see cref="PrintNode"/> represents a position in a tree. That position may have existed in the original tree,
        /// the resulting tree, or both.
        /// </summary>
        private class PrintNode
        {
            /// <summary>
            /// The <see cref="ChangeList{T}.ChangeListValue"/>s that represent nodes removed or added at this particular position in the tree.
            /// </summary>
            public readonly List<ChangeList<JsonNode>.ChangeListValue> ChangedNodes;

            /// <summary>
            /// The children of this node. The name of a child node is encapsulated in the key.
            /// This means that the root of a tree will be nameless.
            /// </summary>
            public readonly Dictionary<string, PrintNode> Children;

            /// <summary>
            /// Constructor.
            /// </summary>
            public PrintNode()
            {
                ChangedNodes = new List<ChangeList<JsonNode>.ChangeListValue>();
                Children = new Dictionary<string, PrintNode>();
            }

            /// <summary>
            /// Helper struct for holding state used while recursively printing tree.
            /// </summary>
            private struct RecursionState
            {
                /// <summary>
                /// The node being printed.
                /// </summary>
                public readonly PrintNode PrintNode;

                /// <summary>
                /// How many level deeps in the tree the node is, where 0 is top-level node
                /// that gets printed.
                /// </summary>
                public readonly int Level;

                /// <summary>
                /// The name of the node being printed.
                /// </summary>
                public readonly string Name;

                /// <summary>
                /// Constructor.
                /// </summary>
                public RecursionState(PrintNode printNode, int level, string name)
                {
                    PrintNode = printNode;
                    Level = level;
                    Name = name;
                }
            }

            /// <summary>
            /// Returns the tree of changes encapusulated by this <see cref="PrintNode"/> as an indented, formatted string.
            /// </summary>
            public override string ToString()
            {
                using (var sbPool = Pools.GetStringBuilder())
                {
                    var sb = sbPool.Instance;
                    var stack = new Stack<RecursionState>();
                    stack.Push(new RecursionState(this, -1, null));

                    while (stack.Count != 0)
                    {
                        var curr = stack.Pop();
                        var indentPrefix = curr.Level > 0 ? new string('\t', curr.Level) : string.Empty;

                        if (curr.Name != null)
                        {
                            sb.AppendLine(indentPrefix + curr.Name);
                        }

                        // Each PrintNode represents one position in a tree. The values of a PrintNode represent nodes that were added or removed at that position.
                        // The max number of values at a position is 2, when a node is removed from one position and a new one is added at the same position. 
                        // This is equivalent to modifying the state of that node.
                        switch (curr.PrintNode.ChangedNodes.Count)
                        {
                            // A node exists in one tree that does not exist in the other
                            // In this case, print all the values of the node as either added or removed
                            case 1:
                                var changeListValue = curr.PrintNode.ChangedNodes[0];
                                sb.Append(indentPrefix + changeListValue.ToString());
                                break;
                            // A node exists in both trees, but with different values (equivalent to modifying the node)
                            // Consolidate the print out by diffing just the values
                            case 2:
                                ChangeList<JsonNode>.ChangeListValue removed, added;
                                if (curr.PrintNode.ChangedNodes[0].ChangeType == ChangeList<JsonNode>.ChangeType.Removed)
                                {
                                    removed = curr.PrintNode.ChangedNodes[0];
                                    added = curr.PrintNode.ChangedNodes[1];
                                }
                                else
                                {
                                    removed = curr.PrintNode.ChangedNodes[1];
                                    added = curr.PrintNode.ChangedNodes[0];
                                }

                                // Order of removed vs added node is not guaranteed in the change list,
                                // but when diffing the nodes' values, the removed node represents a node from the old tree,
                                // so it should go first and represent the old list to get the correct diff.
                                var changeList = new ChangeList<string>(removed.Value.Values, added.Value.Values);

                                if (changeList.Count == 0)
                                {
                                    // There was no difference in values between the removed node and added node,
                                    // this means that the node simply moved positions.
                                    // In this case, print the full list of values for both the removed and added node
                                    sb.Append(indentPrefix + removed.ToString());
                                    sb.Append(indentPrefix + added.ToString());
                                }
                                else
                                {
                                    // Otherwise, rely on the normal diff
                                    sb.Append(changeList.ToString(indentPrefix));
                                }

                                break;
                            default:
                                break;
                        }

                        foreach (var child in curr.PrintNode.Children)
                        {
                            stack.Push(new RecursionState(child.Value, curr.Level + 1, child.Key));
                        }
                    }

                    return sb.ToString();
                }
            }
        }
    }
}
