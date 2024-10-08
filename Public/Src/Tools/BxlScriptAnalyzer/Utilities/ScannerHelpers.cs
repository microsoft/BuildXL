// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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

            int backtickCount = 0;
            bool backslashesAreAllowed = false;
            string previousIdentifierText = null;

            while ((tokenKind = scanner.Scan()) != TypeScript.Net.Types.SyntaxKind.EndOfFileToken)
            {
                if (tokenKind == TypeScript.Net.Types.SyntaxKind.CloseBraceToken && backtickCount > 0)
                {
                    yield return (scanner.TokenPos, scanner.TextPos, tokenKind);

                    lastTokenKind = tokenKind;
                    tokenKind = scanner.RescanTemplateToken(backslashesAreAllowed);
                }

                if (tokenKind == TypeScript.Net.Types.SyntaxKind.TemplateHead)
                {
                    backtickCount++;
                    backslashesAreAllowed = sourceFile.BackslashesAllowedInPathInterpolation && Scanner.IsPathLikeInterpolationFactory(previousIdentifierText);
                }
                else if (tokenKind == TypeScript.Net.Types.SyntaxKind.TemplateTail)
                {
                    backtickCount--;
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
