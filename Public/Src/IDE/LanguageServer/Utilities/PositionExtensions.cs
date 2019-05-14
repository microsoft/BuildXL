// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

using Microsoft.VisualStudio.LanguageServer.Protocol;
using TypeScript.Net.Diagnostics;
using TypeScript.Net.Types;
using TypeScript.Net.Utilities;
using Range = Microsoft.VisualStudio.LanguageServer.Protocol.Range;

namespace BuildXL.Ide.LanguageServer
{
    /// <summary>
    /// Extensions to use for <see cref="Position"/> class.
    /// </summary>
    public static class PositionExtensions
    {
        /// <summary>
        /// Returns an empty range from (1,1) to (1,1).
        /// </summary>
        public static Range EmptyRange()
        {
            var position = new Position() { Line = 1, Character = 1 };
            return new Range() { Start = position, End = position };
        }

        /// <summary>
        /// Converts <paramref name="position"/> (which is 0-based) to <see cref="LineAndColumn"/> (which is 1-based).
        /// </summary>
        public static LineAndColumn ToLineAndColumn(this Position position)
        {
            return new LineAndColumn(
                Convert.ToInt32(position.Line + 1),
                Convert.ToInt32(position.Character + 1));
        }

        /// <summary>
        /// Converts <paramref name="lineAndColumn"/> (which is 1-based) to <see cref="Position"/> (which is 0-based).
        /// </summary>
        public static Position ToPosition(this LineAndColumn lineAndColumn)
        {
            return new Position()
            {
                Line = lineAndColumn.Line - 1,
                Character = lineAndColumn.Character - 1,
            };
        }

        /// <summary>
        /// Converts <paramref name="lineInfo"/> (which is 1-based) to <see cref="Position"/> (which is 0-based).
        /// </summary>
        public static Position ToPosition(this LineInfo lineInfo)
        {
            return new Position
            {
                Line = lineInfo.Line - 1,
                Character = lineInfo.Position - 1,
            };
        }

        /// <summary>
        /// Get the starting position of the node (trivia included).
        /// </summary>
        public static Position GetStartPosition(this INode node)
        {
            var sourceFile = node.GetSourceFile();
            var textSpan = DiagnosticUtilities.GetErrorSpanForNode(sourceFile, node);

            return textSpan.GetStartPosition(sourceFile);
        }

        /// <summary>
        /// Get the starting position of the TextSpan in the provided SourceFile.
        /// </summary>
        public static Position GetStartPosition(this ITextSpan textSpan, ISourceFile sourceFile)
        {
            var lineAndColumn = LineInfoExtensions.GetLineAndColumnBy(
                position: textSpan.Start,
                sourceFile: sourceFile,
                skipTrivia: false);

            return lineAndColumn.ToPosition();
        }

        /// <summary>
        /// Get the ending position of the node (trivia included).
        /// </summary>
        public static Position GetEndPosition(this INode node)
        {
            var sourceFile = node.GetSourceFile();
            var textSpan = DiagnosticUtilities.GetErrorSpanForNode(sourceFile, node);

            return textSpan.GetEndPosition(sourceFile);
        }

        /// <summary>
        /// Get the ending position of the TextSpan in the provided SourceFile.
        /// </summary>
        public static Position GetEndPosition(this ITextSpan textSpan, ISourceFile sourceFile)
        {
            var lineAndColumn = LineInfoExtensions.GetLineAndColumnBy(
                position: textSpan.Start + textSpan.Length,
                sourceFile: sourceFile,
                skipTrivia: false);

            return lineAndColumn.ToPosition();
        }
    }
}
