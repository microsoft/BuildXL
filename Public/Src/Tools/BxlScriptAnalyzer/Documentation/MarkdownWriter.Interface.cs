// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BuildXL.FrontEnd.Script.Analyzer.Documentation
{
    public static partial class MarkdownWriter
    {
        private static readonly OrderedContent[] s_getOrderedInterfaceContents =
        {
            new OrderedContent
            {
                Type = DocNodeType.Property,
                Title = "Properties",
                WriteDocNode = AppendProperty,
            },
            new OrderedContent
            {
                Type = DocNodeType.InstanceFunction,
                Title = "Functions",
                WriteDocNode = AppendFunction,
            },
        };

        private static void LinkTable(DocNode node, StringBuilder sb)
        {
            // Tables need some space
            sb.AppendLine();

            sb.AppendLine($"| Parent | Module | Workspace |");
            sb.AppendLine("| - | - | - |");
            sb.AppendLine($"| {CreateLink(node.Parent)} | {CreateLink(node.Module)} | {CreateLink(node.Module.DocWorkspace)} |");
        }

        private static void AddInterface(DocNode node, StringBuilder sb)
        {
            var orderedContent = s_getOrderedInterfaceContents;

            sb.AppendLine($"# {node.Header}");

            LinkTable(node, sb);

            sb.AppendLine(CreateDescription(node));
            CreateContentsSections(node, orderedContent, sb);
        }

        private static void AppendType(DocNode node, StringBuilder sb)
        {
            sb.AppendLine($"# {node.Header}");

            LinkTable(node, sb);

            sb.AppendLine(CreateDescription(node));
        }

        private static void AppendProperty(DocNode node, StringBuilder sb)
        {
            string desc = CreateDescription(node, false);

            if (!string.IsNullOrEmpty(desc))
            {
                sb.AppendLine($"## {node.Header}");
                sb.AppendLine(desc);
            }
        }

        private static void AppendValue(DocNode node, StringBuilder sb)
        {
            sb.AppendLine($"# {node.Header}");

            LinkTable(node, sb);

            sb.AppendLine(CreateDescription(node));
        }

        private static void AppendFunction(DocNode node, StringBuilder sb)
        {
            sb.AppendLine($"# {node.Header}");

            LinkTable(node, sb);

            sb.AppendLine(CreateDescription(node));

            if (node.Doc is FunctionDocumentation funcDoc
                && funcDoc != null
                && funcDoc.Params?.Count > 0)
            {
                sb.AppendLine($"### Parameters");

                foreach (var paramTuple in funcDoc.Params)
                {
                    sb.Append($"* **{paramTuple.Item1}**");

                    if (string.IsNullOrEmpty(paramTuple.Item2))
                    {
                        sb.AppendLine();
                    }
                    else
                    {
                        sb.AppendLine($": {paramTuple.Item2}");
                    }
                }
            }
        }

        private static void AppendEnum(DocNode node, StringBuilder sb)
        {
            sb.AppendLine($"# {node.Header}");

            LinkTable(node, sb);

            sb.AppendLine(CreateDescription(node));

            sb.AppendLine("### Values");

            foreach (var child in node.Children.OrderBy(n => n.Name))
            {
                sb.AppendLine($"* {child.Name}");

                var description = CreateDescription(child, false);
                if (!string.IsNullOrEmpty(description))
                {
                    sb.AppendLine($"  * {description.Replace(NewLine, NewLine + "  * ")}");
                }
            }
        }

        private static string CreateDescription(DocNode node, bool header = true)
        {
            List<string> desc = CreateDescriptionParagraphs(node);

            if (desc.Count > 0)
            {
                return $"{(header ? "### Definition" + NewLine : string.Empty)}{string.Join(NewLine, desc)}";
            }

            return string.Empty;
        }
    }
}
