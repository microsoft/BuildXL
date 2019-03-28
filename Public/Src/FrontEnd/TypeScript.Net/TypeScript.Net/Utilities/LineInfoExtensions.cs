// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using TypeScript.Net.Diagnostics;
using TypeScript.Net.Scanning;
using TypeScript.Net.Types;

namespace TypeScript.Net.Utilities
{
    /// <summary>
    /// Set of extension method for <see cref="LineInfo"/> struct and for line info computation.
    /// </summary>
    public static class LineInfoExtensions
    {
        /// <nodoc />
        public static LineAndColumn ToLineAndColumn(this LineInfo lineInfo)
        {
            return new LineAndColumn(lineInfo.Line, lineInfo.Position);
        }

        /// <nodoc />
        public static LocationData ToLocationData(this LineInfo lineInfo, AbsolutePath path)
        {
            return !path.IsValid ? LocationData.Invalid : new LocationData(path, lineInfo.Line, lineInfo.Position);
        }

        /// <summary>
        /// Returns lazily computed line and column information by node's position.
        /// </summary>
        public static LineInfo GetLineInfo(this INode node, ISourceFile sourceFile)
        {
            return LineInfo.FromLineMap(sourceFile.LineMap, node.Pos + node.GetLeadingTriviaLength());
        }

        /// <summary>
        /// Returns lazily computed line and column information by node's end position.
        /// </summary>
        public static LineInfo GetLineInfoEnd(this INode node, ISourceFile sourceFile)
        {
            return LineInfo.FromLineMap(sourceFile.LineMap, node.End);
        }

        /// <summary>
        /// Returns line and column for specified <paramref name="position"/>.
        /// </summary>
        public static LineAndColumn GetLineAndColumnBy(int position, ISourceFile sourceFile, bool skipTrivia)
        {
            Contract.Requires(sourceFile != null);
            return skipTrivia
                ? Scanner.GetLineAndCharacterOfPositionSkippingTrivia(sourceFile, position)
                : Scanner.GetLineAndCharacterOfPosition(sourceFile, position);
        }

        /// <summary>
        /// Returns line and column for provided <paramref name="diagnostic"/>.
        /// </summary>
        public static LineAndColumn GetLineAndColumn(this Diagnostic diagnostic, ISourceFile sourceFile)
        {
            return Scanner.GetLineAndCharacterOfPosition(sourceFile, diagnostic.Start ?? -1);
        }
    }
}
