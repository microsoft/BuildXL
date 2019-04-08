// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
