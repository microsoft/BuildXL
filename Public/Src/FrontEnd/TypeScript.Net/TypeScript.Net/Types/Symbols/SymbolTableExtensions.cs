// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;

namespace TypeScript.Net.Types
{
    /// <nodoc/>
    public static class SymbolTableExtensions
    {
        /// <nodoc/>
        public static IEnumerable<ISymbol> Enumerate(this ISymbolTable symbolTable)
        {
            Contract.Requires(symbolTable != null);

            foreach (var index in symbolTable)
            {
                yield return index.Value;
            }
        }
    }
}
