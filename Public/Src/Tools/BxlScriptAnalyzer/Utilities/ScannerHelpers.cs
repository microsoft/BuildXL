// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using TypeScript.Net.Scanning;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.Analyzer.Utilities
{
    internal static class ScannerHelpers
    {
        public static IEnumerable<(int start, int end, TypeScript.Net.Types.SyntaxKind kind)> GetTokens(ISourceFile sourceFile, bool preserveTrivia, bool preserveComments)
        {
            var scanner = new Scanner(ScriptTarget.Latest, preserveTrivia: preserveTrivia, allowBackslashesInPathInterpolation: sourceFile.BackslashesAllowedInPathInterpolation, text: sourceFile.Text, preserveComments: preserveComments);
            TypeScript.Net.Types.SyntaxKind tokenKind = TypeScript.Net.Types.SyntaxKind.Unknown;
            TypeScript.Net.Types.SyntaxKind lastTokenKind = TypeScript.Net.Types.SyntaxKind.Unknown;

            bool backtickFound = false;
            bool backslashesAreAllowed = false;
            string previousIdentifierText = null;

            while ((tokenKind = scanner.Scan()) != TypeScript.Net.Types.SyntaxKind.EndOfFileToken)
            {
                if (tokenKind == TypeScript.Net.Types.SyntaxKind.CloseBraceToken && backtickFound)
                {
                    yield return (scanner.TokenPos, scanner.TextPos, tokenKind);

                    lastTokenKind = tokenKind;
                    tokenKind = scanner.RescanTemplateToken(backslashesAreAllowed);
                }

                if (tokenKind == TypeScript.Net.Types.SyntaxKind.TemplateHead)
                {
                    backtickFound = true;
                    backslashesAreAllowed = sourceFile.BackslashesAllowedInPathInterpolation && Scanner.IsPathLikeInterpolationFactory(previousIdentifierText);
                }
                else if (tokenKind == TypeScript.Net.Types.SyntaxKind.TemplateTail)
                {
                    backtickFound = false;
                }

                yield return GetStartAndEndPositions(scanner, tokenKind, lastTokenKind);

                if (tokenKind == TypeScript.Net.Types.SyntaxKind.Identifier)
                {
                    previousIdentifierText = scanner.TokenValue;
                }
                else
                {
                    previousIdentifierText = null;
                }
            }
        }

        private static (int start, int end, TypeScript.Net.Types.SyntaxKind tokenKind) GetStartAndEndPositions(Scanner scanner, TypeScript.Net.Types.SyntaxKind currentTokenKind, TypeScript.Net.Types.SyntaxKind lastTokenKind)
        {
            if (currentTokenKind == TypeScript.Net.Types.SyntaxKind.PublicKeyword)
            {
                if (lastTokenKind == TypeScript.Net.Types.SyntaxKind.AtToken)
                {
                    return (start: scanner.TokenPos - 2, end: scanner.TextPos, currentTokenKind);
                }
                else
                {
                    currentTokenKind = TypeScript.Net.Types.SyntaxKind.Identifier;
                }
            }


            return (start: scanner.TokenPos, end: scanner.TextPos, tokenKind: currentTokenKind);
        }
    }
}
