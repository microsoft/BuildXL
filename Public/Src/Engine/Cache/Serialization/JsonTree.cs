// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using System.Collections.Generic;
using System;
using BuildXL.Utilities;
using JsonDiffPatchDotNet;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
    public static class JsonTree
    {
        private static JsonDiffPatch s_jdp = null;

        static JsonTree()
        {
            var diffOptions = new Options();
            diffOptions.ArrayDiff = ArrayDiffMode.Efficient;
            diffOptions.TextDiff = TextDiffMode.Simple;

            s_jdp = new JsonDiffPatch(diffOptions);
        }

        /// <summary>
        /// Given a valid JSON object, builds a tree of <see cref="JsonNode"/>s.
        /// </summary>
        /// <returns>
        /// The root node of the tree built.
        /// </returns>
        public static JsonNode Deserialize(string json)
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
                    // Arrays are represented by either nodes having multiple values, or multiple children
                    // This will remove unnecessary "nameless" nested arrays that don't provide meaningful information
                    // These two strings deserialize the same: { "key" : [value] }, { "key" : ["value"] }
                    case JsonToken.StartArray:
                    case JsonToken.EndArray:
                    default:
                        break;
                }
            }

            return currentNode;
        }

        /// <summary>
        /// Given the root <see cref="JsonNode"/> a <see cref="JsonTree"/>, converts the tree into a valid JSON string.
        /// </summary>
        /// <remarks>
        /// This function is recursive to allow re-using <see cref="JsonFingerprinter"/> helper functions for nested objects instead of having to re-build the JSON string from scratch.
        /// The max nested depth for JSON representation of fingerprints is relatively low (~5 stacks), so stack memory should be trivial.
        /// </remarks>
        public static string Serialize(JsonNode root)
        {
            return JsonFingerprinter.CreateJsonString(wr =>
            {
                // If the root is being used to just point to a bunch of child nodes, skip printing it
                if (string.IsNullOrEmpty(root.Name))
                {
                    for (var it = root.Children.Last; it != null; it = it.Previous)
                    {
                        BuildStringHelper(it.Value, wr);
                    }
                }
                else
                {
                    BuildStringHelper(root, wr);
                }
            },
            Formatting.Indented);
        }

        private static void BuildStringHelper(JsonNode root, IFingerprinter wr)
        {
            if (root.Children.Count == 1 && root.Values.Count == 0)
            {
                wr.AddNested(root.Name, nestedWr =>
                {
                    BuildStringHelper(root.Children.First.Value, nestedWr);
                });
            }
            else if (root.Children.Count == 0 && root.Values.Count == 1)
            {
                wr.Add(root.Name, root.Values[0]);
            }
            else
            {
                // Adding a collection is typically used to add a homogeneous collection of objects.
                // In this case, the values of the node and the children of the node both need to be added within the same nested collection.
                // To get around this without adding two collections associated with the same node, use just the current root node as the collection and
                // manually add the node's values and node's children.
                wr.AddCollection<JsonNode, IEnumerable<JsonNode>>(root.Name, new JsonNode[] { root }, (collectionWriter, n) =>
                {
                    foreach (var value in n.Values)
                    {
                        collectionWriter.Add(value);
                    }

                    for (var it = n.Children.Last; it != null; it = it.Previous)
                    {
                        BuildStringHelper(it.Value, collectionWriter);
                    }
                });
            }

        }


        /// <summary>
        /// Diffs two <see cref="JsonNode"/> trees.
        /// </summary>
        /// <returns>
        /// A JSON string representation of the diff.
        /// </returns>
        public static string PrintTreeDiff(JsonNode rootA, JsonNode rootB)
        {
            var jsonA = Serialize(rootA);
            var jsonB = Serialize(rootB);

            var diff = s_jdp.Diff(JToken.Parse(jsonA), JToken.Parse(jsonB));

            return diff == null ? string.Empty : diff.ToString();
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
    }
}
