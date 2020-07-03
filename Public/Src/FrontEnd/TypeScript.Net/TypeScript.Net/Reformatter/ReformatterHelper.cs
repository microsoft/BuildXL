// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using TypeScript.Net.Types;

namespace TypeScript.Net.Reformatter
{
    /// <nodoc/>
    public static class ReformatterHelper
    {
        /// <nodoc/>
        public static string GetFormattedText(this INode node)
        {
            return Format(node, onlyFunctionHeader: false);
        }

        /// <nodoc/>
        public static string GetText(this INode node)
        {
            return Format(node, onlyFunctionHeader: false);
        }

        /// <nodoc/>
        public static string FormatOnlyFunctionHeader(INode node)
        {
            return Format(node, onlyFunctionHeader: true);
        }

        private static string Format(INode node, bool onlyFunctionHeader)
        {
            using (var writer = new ScriptWriter())
            {
                node.Cast<IVisitableNode>().Accept(new ReformatterVisitor(writer, onlyFunctionHeader));
                return writer.ToString();
            }
        }
    }
}
