// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text.RegularExpressions;
using TypeScript.Net.Types;

namespace BuildXL.Ide.LanguageServer.Utilities
{
    /// <summary>
    /// Utilities to retrieve documentation from elements in the AST
    /// </summary>
    public static class DocumentationUtilities
    {
        /// <summary>
        /// A regex to help delete leading whitespaces and decorators for multi-line comments and documentation.
        /// E.g.
        ///     /**
        ///      * This is an example of a multi-line documentation.
        ///      *
        ///      * Another line in this documentation.
        ///      */
        /// </summary>
        private static readonly Regex s_multiLineCommentRegex = new Regex(@"[^\S\r\n]*/?\*+/?", RegexOptions.Multiline);

        /// <summary>
        /// Gets the documentation for a symbol, joining multiple lines with <see cref="Environment.NewLine"/>
        /// </summary>
        /// <param name="symbol">Symbol to get documentation for</param>
        /// <returns>The documentation string</returns>
        public static string GetDocumentationForSymbolAsString(ISymbol symbol)
        {
            return string.Join(Environment.NewLine, GetDocumentationForSymbol(symbol));
        }

        /// <summary>
        /// Gets the documentation for a symbol
        /// </summary>
        /// <param name="symbol">Symbol to get documentation for</param>
        /// <returns>The documentation string</returns>
        public static IEnumerable<string> GetDocumentationForSymbol(ISymbol symbol)
        {
            INode node = symbol.GetFirstDeclarationOrDefault();
            if (node == null)
            {
                yield break;
            }

            foreach (var docString in GetDocumentationForNode(node))
            {
                yield return docString;
            }
        }

        private static IEnumerable<string> GetDocumentationForNode(INode node)
        {
            var sourceFile = node.GetSourceFile();

            Contract.Assert(sourceFile != null);
            if (!sourceFile.PerNodeTrivia.TryGetValue(node, out var searchingForTrivia))
            {
                yield break;
            }

            if (searchingForTrivia.LeadingComments == null || searchingForTrivia.LeadingComments.Length == 0)
            {
                // As a last resort if there are any decorators on this node the trivia may be associated with those nodes,
                // so go through those and try to get their trivia too
                foreach (var decorator in node.Decorators?.Elements ?? Enumerable.Empty<IDecorator>())
                {
                    foreach (var decoratorDocString in GetDocumentationForNode(decorator))
                    {
                        yield return decoratorDocString;
                    }
                }

                yield break;
            }

            // Get the last leading comment for the node we are searching for.
            // That is the best-guess documentation for the node.
            var comment = searchingForTrivia.LeadingComments.Last();
            if (comment.IsSingleLine)
            {
                yield return comment
                    .Content
                    .Replace("//", string.Empty)
                    .Trim();
            }
            else
            {
                yield return s_multiLineCommentRegex.Replace(comment.Content, string.Empty).Trim();
            }
        }
    }
}
