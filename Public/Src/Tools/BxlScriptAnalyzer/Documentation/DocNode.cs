// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text.RegularExpressions;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Script.Analyzer.Documentation
{
    /// <nodoc />
    public class DocNode
    {
        private ConcurrentDictionary<string, DocNode> ChildNodes { get; } = new ConcurrentDictionary<string, DocNode>();

        /// <nodoc />
        public DocNode(DocNodeType docNodeType, DocNodeVisibility visibility, string name, List<string> trivia, Module module, AbsolutePath specPath, DocNode parent, string appendix)
        {
            Contract.Assert(!string.IsNullOrEmpty(name));
            Contract.Assert(module != null);

            DocNodeType = docNodeType;
            Visibility = visibility;
            Name = name;
            NodeId = module.GetNextNodeId();
            Module = module;
            SpecPath = specPath;
            Parent = parent;

            Doc = Documentation.Parse(trivia, docNodeType);

            Appendix = appendix;
        }

        /// <nodoc />
        public DocNode GetOrAdd(DocNodeType type, DocNodeVisibility visibility, AbsolutePath specPath, string name, List<string> trivia, string appendix)
        {
            return ChildNodes.GetOrAdd(name, _ => new DocNode(type, visibility, name, trivia, Module, specPath, this, appendix));
        }

        /// <nodoc />
        public DocNodeType DocNodeType { get; }

        /// <nodoc />
        public DocNodeVisibility Visibility { get; }

        /// <nodoc />
        public int NodeId { get; }

        /// <nodoc />
        public Module Module { get; }

        /// <nodoc />
        public AbsolutePath SpecPath { get; }

        /// <nodoc />
        public string Name { get; }

        /// <nodoc />
        public DocNode Parent { get; }

        /// <nodoc />
        public Documentation Doc { get; }

        /// <nodoc />
        public string Appendix { get; set; }

        /// <nodoc />
        public IEnumerable<DocNode> Children => ChildNodes.Values;

        // TODO: This is ineffecient

        /// <nodoc />
        public string FullName => Parent == null ? Name : Parent.FullName + "." + Name;

        /// <nodoc />
        public string Header
        {
            get
            {
                if (string.IsNullOrEmpty(m_header))
                {
                    string descriptor = string.Empty;

                    switch (DocNodeType)
                    {
                        case DocNodeType.Enum:
                            descriptor = "Enum";
                            break;
                        case DocNodeType.Namespace:
                            descriptor = "Namespace";
                            break;
                        case DocNodeType.Interface:
                            descriptor = "Interface";
                            break;
                        case DocNodeType.Type:
                            descriptor = "Type";
                            break;
                        case DocNodeType.Value:
                            descriptor = "Value";
                            break;
                        case DocNodeType.Function:
                            descriptor = "Function";
                            break;
                        case DocNodeType.Property:
                            descriptor = "Property";
                            break;
                        case DocNodeType.InstanceFunction:
                            descriptor = "Instance Function";
                            break;
                        default:
                            throw new ArgumentOutOfRangeException($"{nameof(DocNode)}.{nameof(Header)}:{DocNodeType}");
                    }

                    m_header = $"{FullName} {descriptor}";
                }

                return m_header;
            }
        }

        /// <nodoc />
#pragma warning disable CA1308 // Normalize strings to uppercase
        public string Anchor => Header.ToLowerInvariant().Replace(' ', '-');
#pragma warning restore CA1308 // Normalize strings to uppercase

        private string m_header;
    }

    /// <summary>
    /// Simple node containing documentation for all node kinds.
    /// </summary>
    public abstract class Documentation
    {
        /// <summary>
        /// Divided list of comments intended for the generic description of the node.
        /// </summary>
        public List<string> Description { get; } = new List<string>();

        /// <summary>
        /// Construct a <see cref="Documentation"/> appropriate for the given <paramref name="docNodeType" />.
        /// </summary>
        public static Documentation Parse(List<string> comments, DocNodeType docNodeType)
        {
            if (comments != null && comments.Count > 0 && comments[0].StartsWith("/**"))
            {
                // Trim inputs
                comments = comments
                    .Select(s => s.Trim(' ', '\t', '*', '\r', '\n', '/'))
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();

                if (comments.Count > 0)
                {
                    switch (docNodeType)
                    {
                        case DocNodeType.Function:
                        case DocNodeType.InstanceFunction:
                            return new FunctionDocumentation(comments);
                        case DocNodeType.Enum:
                        case DocNodeType.Namespace:
                        case DocNodeType.Interface:
                        case DocNodeType.Type:
                        case DocNodeType.Value:
                        case DocNodeType.Property:
                        case DocNodeType.EnumMember:
                            return new DefaultDocumentation(comments);
                        default:
                            throw new ArgumentOutOfRangeException(nameof(docNodeType), docNodeType, null);
                    }
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Simple node where all documentation belongs to the description.
    /// </summary>
    public class DefaultDocumentation : Documentation
    {
        /// <summary>
        /// Constructor.
        /// </summary>
        public DefaultDocumentation(List<string> comments)
        {
            Description.AddRange(comments);
        }
    }

    /// <summary>
    /// Function-specific node with special case for comment lines beginning with '@param'.
    /// </summary>
    public class FunctionDocumentation : Documentation
    {
        private const string ParamPattern = "@param ([^\\s]+)( (.*))?";
        private static readonly Regex rgx = new Regex(ParamPattern);

        /// <summary>
        /// Constructor.
        /// </summary>
        public FunctionDocumentation(List<string> comments)
        {
            foreach (string comment in comments)
            {
                var matches = rgx.Matches(comment);
                if (matches.Count > 0)
                {
                    Params.Add((matches[0].Groups[1].Value, matches[0].Groups[3].Value));
                }
                else
                {
                    Description.Add(comment);
                }
            }
        }

        /// <summary>
        /// List of documentation for parameters.
        /// </summary>
        /// <remarks>Pairs: (parameter name, parameter description)</remarks>
        public List<(string, string)> Params { get; } = new List<(string, string)>();
    }
}
