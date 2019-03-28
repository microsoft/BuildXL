// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using TypeScript.Net.Scanning;
using TypeScript.Net.Types;

namespace TypeScript.Net.Parsing
{
    /// <nodoc />
    public static class MissingSemicolonAnalyzer
    {
        private static readonly HashSet<SyntaxKind> s_syntaxKindsToAnalyzer = new HashSet<SyntaxKind>
                                                                     {
                                                                         SyntaxKind.VariableStatement,
                                                                         SyntaxKind.ReturnStatement,
                                                                         SyntaxKind.BreakStatement,
                                                                         SyntaxKind.ContinueStatement,
                                                                         SyntaxKind.ThrowStatement,
                                                                         SyntaxKind.ImportDeclaration,
                                                                         SyntaxKind.ExportDeclaration,
                                                                         SyntaxKind.ImportEqualsDeclaration,
                                                                         SyntaxKind.DoStatement,
                                                                         SyntaxKind.DebuggerStatement,
                                                                         SyntaxKind.PropertyDeclaration,
                                                                         SyntaxKind.ExpressionStatement,
                                                                         SyntaxKind.TypeAliasDeclaration,
                                                                     };

        /// <summary>
        /// Returns true if the given <paramref name="node"/> does not ends with a semicolon.
        /// </summary>
        public static bool IsSemicolonMissingAfter(INode node, TextSource text, out int position)
        {
            position = -1;

            if (!s_syntaxKindsToAnalyzer.Contains(node.Kind))
            {
                return false;
            }

            var charPosition = node.End - 1;
            var charCodeAtPosition = text.CharCodeAt(charPosition);
            if (charCodeAtPosition != CharacterCodes.Semicolon)
            {
                // When files are parsed while preserving whitespace, the semicolon might be followed by whitespace characters which is ok.
                // We therefore have to walk back until the first non-whitespace character.
                while (true)
                {
                    if (!Scanner.IsLineBreak(charCodeAtPosition) && !Scanner.IsWhiteSpace(charCodeAtPosition))
                    {
                        break;
                    }

                    charPosition--;
                    if (charPosition == 0)
                    {
                        break;
                    }

                    charCodeAtPosition = text.CharCodeAt(charPosition);

                    if (charCodeAtPosition == CharacterCodes.Semicolon)
                    {
                        // This node is fine
                        return false;
                    }
                }

                position = charPosition;
                return true;
            }

            return false;
        }
    }
}
