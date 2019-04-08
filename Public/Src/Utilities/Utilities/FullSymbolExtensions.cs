// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Set of extension methods for <see cref="FullSymbol"/>.
    /// </summary>
    public static class FullSymbolExtensions
    {
        /// <summary>
        /// Cached version of invalid full symbol.
        /// </summary>
        /// <remarks>
        /// It is safe to pass null as a <see cref="SymbolTable"/> in this case.
        /// </remarks>
        private static readonly char[] InvalidFullSymbol = FullSymbol.Invalid.ToString(symbolTable: null).ToCharArray();

        /// <summary>
        /// Appends a string representation of <paramref name="fullSymbol"/> into <paramref name="builder"/>.
        /// </summary>
        public static void Append(this StringBuilder builder, FullSymbol fullSymbol, SymbolTable symbolTable)
        {
            builder.Append(fullSymbol.ToStringAsCharArray(symbolTable));
        }
        
        /// <summary>
        /// Returns a string representation of a <paramref name="fullSymbol"/> as a character array.
        /// </summary>
        public static char[] ToStringAsCharArray(this FullSymbol fullSymbol, SymbolTable symbolTable)
        {
            if (!fullSymbol.IsValid)
            {
                return InvalidFullSymbol;
            }

            return symbolTable.ExpandNameToCharArray(fullSymbol.Value);
        }
    }
}
