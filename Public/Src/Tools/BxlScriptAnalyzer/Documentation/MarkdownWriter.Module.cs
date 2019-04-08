// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BuildXL.FrontEnd.Script.Analyzer.Documentation
{
    public static partial class MarkdownWriter
    {
        private static void WriteModule(Module module)
        {
            if (s_moduleList != null && !s_moduleList.Contains(module.Name))
            {
                return;
            }

            var contents = CollectPublicModuleContents(module);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"# {module.Title} Module");
            sb.AppendLine("* Workspace");
            sb.AppendLine($"  * {CreateLink(module.DocWorkspace)}");

            CreateContentsTable(contents, sb);

            foreach (var node in contents.OrderBy(dn => (int)dn.DocNodeType).ThenBy(dn => dn.FullName))
            {
                switch (node.DocNodeType)
                {
                    case DocNodeType.Namespace:
                        // WriteNamespace(node, sb);
                        break;
                    case DocNodeType.Interface:
                        AddInterface(node, sb);
                        break;
                    case DocNodeType.Type:
                        AppendType(node, sb);
                        break;
                    case DocNodeType.Enum:
                        AppendEnum(node, sb);
                        break;
                    case DocNodeType.Value:
                        AppendValue(node, sb);
                        break;
                    case DocNodeType.InstanceFunction:
                    case DocNodeType.Function:
                        AppendFunction(node, sb);
                        break;
                    case DocNodeType.Property:
                        AppendProperty(node, sb);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException($"{nameof(MarkdownWriter)}.{nameof(WriteModule)}:{node.DocNodeType}");
                }
            }

            WriteToDoc(CreateFileName(module), sb);
        }

        private static IOrderedEnumerable<DocNode> CollectPublicModuleContents(Module module)
        {
            var result = new List<DocNode>();
            foreach (var child in module.Children)
            {
                if (child.DocNodeType == DocNodeType.Namespace)
                {
                    CollectAllNamespaces(child, result);
                }
                else
                {
                    result.Add(child);
                }
            }

            return result
                .Where(d => d.Visibility == DocNodeVisibility.Public)
                .OrderBy(d => (int)d.DocNodeType)
                .ThenBy(d => d.FullName);
        }

        private static void CollectAllNamespaces(DocNode current, List<DocNode> nodes)
        {
            if (current.DocNodeType != DocNodeType.Namespace)
            {
                nodes.Add(current);
            }

            if (current.DocNodeType == DocNodeType.Namespace)
            {
                foreach (var child in current.Children.OrderBy(child => child.FullName))
                {
                    CollectAllNamespaces(child, nodes);
                }
            }
        }

        private static void CreateContentsTable(IEnumerable<DocNode> contents, StringBuilder sb)
        {
            sb.AppendLine();
            sb.AppendLine("| Type | Name | Description |");
            sb.AppendLine("|------|------|-------------|");
            foreach (var node in contents.OrderBy(c => c.DocNodeType).ThenBy(c => c.FullName))
            {
                CreateContentsRow(node, sb);
            }
        }

        private static void CreateContentsRow(DocNode node, StringBuilder sb)
        {
            sb.AppendLine($"| {CreateDocNodeTypeImage(node.DocNodeType)} | {CreateLink(node)} | {string.Join(NewLine, CreateDescriptionParagraphs(node, 40))} |");
        }
    }
}
