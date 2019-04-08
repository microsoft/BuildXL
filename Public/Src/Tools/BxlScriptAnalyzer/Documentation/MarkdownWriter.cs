// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace BuildXL.FrontEnd.Script.Analyzer.Documentation
{
    /// <summary>
    /// Tool which writes out DScript documentation from source text.
    /// </summary>
    public static partial class MarkdownWriter
    {
        private const string IconSize = "14x14";
        private const string TypeImageUrl = "https://docs.microsoft.com/en-us/media/toolbars/type.svg";
        private const string MemberImageUrl = "https://docs.microsoft.com/en-us/media/toolbars/member.svg";
        private const string NewLine = "\r\n";

        private const string FilePathStepSeparator = "._.";
        private const string FilePathExtension = ".md";

        private static string s_rootFolder;
        private static string s_rootLink = string.Empty;
        private static HashSet<string> s_moduleList = null;

        /// <summary>
        /// Define the root folder where files should be written.
        /// </summary>
        public static void SetRootFolder(string rootFolder)
        {
            s_rootFolder = rootFolder;
        }

        /// <summary>
        /// Define the wiki path where the emitted docs will live.
        /// </summary>
        public static void SetRootLink(string rootLink)
        {
            s_rootLink = rootLink;
        }

        /// <summary>
        /// Define the list of modules to emit.
        /// </summary>
        public static void SetModuleList(HashSet<string> moduleList)
        {
            s_moduleList = moduleList;
        }

        private static void WriteToDoc(string fileName, StringBuilder sb)
        {
            File.WriteAllText(Path.Combine(s_rootFolder, fileName + FilePathExtension), sb.ToString());
        }

        private static string CreateDocNodeTypeImage(DocNodeType nodeType)
        {
            switch (nodeType)
            {
                case DocNodeType.Namespace:
                    return $"![Namespace](https://docs.microsoft.com/en-us/media/toolbars/namespace.svg ={IconSize})";
                case DocNodeType.Enum:
                    return $"![Enum]({TypeImageUrl} ={IconSize})";
                case DocNodeType.Interface:
                    return $"![Interface]({TypeImageUrl} ={IconSize})";
                case DocNodeType.Type:
                    return $"![Type]({TypeImageUrl} ={IconSize})";
                case DocNodeType.Property:
                    return $"![Property]({MemberImageUrl} ={IconSize})";
                case DocNodeType.Value:
                    return $"![Value]({MemberImageUrl} ={IconSize})";
                case DocNodeType.Function:
                case DocNodeType.InstanceFunction:
                    return $"![Function]({MemberImageUrl} ={IconSize})";
                default:
                    return null;
            }
        }

        private static string CreateLink(DocWorkspace workspace)
        {
            return CreateLink(workspace.Name, CreateFileName(workspace), null);
        }

        private static string CreateLink(Module module)
        {
            return CreateLink(module.Title, CreateFileName(module), module);
        }

        private static string CreateLink(string name, string path, Module module, string anchor = null)
        {
            string prefix = $"/{(string.IsNullOrEmpty(s_rootLink) ? string.Empty : s_rootLink + "/")}{path}";

            // Ignore path if same module
            if (module != null && !string.IsNullOrEmpty(anchor) && string.Equals(path, CreateFileName(module), StringComparison.OrdinalIgnoreCase))
            {
                prefix = string.Empty;
            }

            // TODO: Update this logic when docs are spread across many files
            return $"[{name}]({prefix}{(string.IsNullOrEmpty(anchor) ? string.Empty : "#" + anchor)})";
        }

        private static string CreateLink(DocNode docNode)
        {
            if (docNode == null)
            {
                return string.Empty;
            }

            string linkAnchor = null;
            if (docNode.DocNodeType != DocNodeType.Namespace)
            {
                linkAnchor = docNode.Anchor;
            }

            return CreateLink(docNode.FullName, CreateFileName(docNode), docNode.Module, linkAnchor);
        }

        private static void CreateContentsSections(DocNode node, OrderedContent[] orderedContents, StringBuilder sb)
        {
            foreach (var orderedContent in orderedContents)
            {
                var typeChildren = node.Children.Where(c => c.DocNodeType == orderedContent.Type).OrderBy(tc => tc.Name).ToList();

                if (typeChildren.Count > 0)
                {
                    // Tables need some space
                    sb.AppendLine();

                    sb.AppendLine($"| {orderedContent.Title} | Description |");
                    sb.AppendLine("| - | - |");

                    foreach (var child in typeChildren)
                    {
                        sb.AppendLine($"| {CreateLink(child)} | {string.Join(NewLine, CreateDescriptionParagraphs(child, 40))} |");
                    }

                    sb.AppendLine();

                    foreach (var child in typeChildren)
                    {
                        orderedContent.WriteDocNode(child, sb);
                    }
                }
            }
        }

        private class OrderedContent
        {
            public DocNodeType Type { get; set; }

            public string Title { get; set; }

#pragma warning disable CA1308 // Normalize strings to uppercase
            public string AnchorName => Title.ToLowerInvariant().Replace(' ', '-');
#pragma warning restore CA1308 // Normalize strings to uppercase

            public Action<DocNode, StringBuilder> WriteDocNode { get; set; }
        }

        private static string CreateFileName(DocWorkspace workspace)
        {
            return "index";
        }

        private static string CreateFileName(Module module)
        {
            if (!string.IsNullOrEmpty(module.Version))
            {
                return module.Name + FilePathStepSeparator + module.Version;
            }
            else
            {
                return module.Name;
            }
        }

        private static string CreateFileName(DocNode docNode)
        {
            return CreateFileName(docNode.Module);
        }

        private static List<string> CreateDescriptionParagraphs(DocNode node, int maxLength = 0)
        {
            List<string> description;
            if (node.Doc?.Description != null)
            {
                description = new List<string>(node.Doc.Description);
            }
            else
            {
                description = new List<string>();
            }

            if (!string.IsNullOrEmpty(node.Appendix))
            {
                description.Add(node.Appendix);
            }

            // Truncate if necessary
            if (maxLength > 0 && description.Count > 0)
            {
                // Keep the first line and trim it to maxLength
                return new List<string> { description[0].Substring(0, Math.Min(description[0].Length, maxLength)) };
            }

            return description;
        }
    }
}
