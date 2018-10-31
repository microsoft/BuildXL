// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Utilities
{
    /// <summary>
    /// Abstract representation of a full symbol, partial symbol, or symbol atom
    /// </summary>
    public interface ISymbol
    {
        /// <summary>
        /// Attempts to get the full symbol of the combined symbols. If this value represents a full symbol
        /// it is returned unmodified.
        /// </summary>
        /// <param name="symbolTable">the symbol table</param>
        /// <param name="root">the root symbol</param>
        /// <param name="fullSymbol">the combined symbol</param>
        /// <returns>true if the combined symbol was in the symbol table, otherwise false</returns>
        bool TryGetFullSymbol(SymbolTable symbolTable, FullSymbol root, out FullSymbol fullSymbol);

        /// <summary>
        /// Converts the symbol to its string representation
        /// </summary>
        /// <param name="symbolTable">the symbol table</param>
        /// <returns>the string representation of the symbol</returns>
        string ToString(SymbolTable symbolTable);
    }
}
