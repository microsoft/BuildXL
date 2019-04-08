// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace TypeScript.Net.Types
{
    /// <summary>
    /// Symbol table used by the checker.
    /// </summary>
    public interface ISymbolTable
    {
        /// <summary>
        /// Returns symbol by specified index.
        /// </summary>
        ISymbol this[string index] { get; set; }

        /// <summary>
        /// Returns struct-based enumerator to get all the elements from the symbol table.
        /// </summary>
        Map<ISymbol>.Enumerator GetEnumerator();

        /// <summary>
        /// Returns a symbol by a given kdy.
        /// </summary>
        bool TryGetSymbol(string index, out ISymbol result);

        /// <summary>
        /// Returns number of elements in the symbol table.
        /// </summary>
        int Count { get; }
    }
}
