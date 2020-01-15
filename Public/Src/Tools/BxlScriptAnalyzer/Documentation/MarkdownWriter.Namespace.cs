// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.FrontEnd.Script.Analyzer.Documentation
{
    public static partial class MarkdownWriter
    {
        private static OrderedContent[] GetOrderedNamespaceContents()
        {
            return new[]
            {
                new OrderedContent
                {
                    Type = DocNodeType.Namespace,
                    Title = "Nested Namespaces",
                    WriteDocNode = (n, sb) => { }, // Do nothing for namespaces!
                },
                new OrderedContent
                {
                    Type = DocNodeType.Function,
                    Title = "Functions",
                    WriteDocNode = AppendFunction,
                },
                new OrderedContent
                {
                    Type = DocNodeType.Value,
                    Title = "Value",
                    WriteDocNode = (n, sb) => { }, // WriteValue
                },
                new OrderedContent
                {
                    Type = DocNodeType.Interface,
                    Title = "Interfaces",
                    WriteDocNode = AddInterface,
                },
                new OrderedContent
                {
                    Type = DocNodeType.Enum,
                    Title = "Enums",
                    WriteDocNode = AppendEnum, // WriteEnum
                },
            };
        }
    }
}
