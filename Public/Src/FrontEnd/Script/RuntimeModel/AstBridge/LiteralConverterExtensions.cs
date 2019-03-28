// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Core;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge
{
    /// <summary>
    /// Helper class for converting different kinds of literals form parse AST to evaluation AST.
    /// </summary>
    internal static class LiteralConverterExtensions
    {
        /// <summary>
        /// Converts provided literal from string representation to 32-bit integer.
        /// </summary>
        public static Number TryConvertToNumber(this TypeScript.Net.Types.ILiteralExpression literal)
        {
            Contract.Requires(literal != null);
            Contract.Requires(literal.Kind == TypeScript.Net.Types.SyntaxKind.NumericLiteral);

            return LiteralConverter.TryConvertNumber(literal.Text);
        }
    }
}
