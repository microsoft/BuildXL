// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
