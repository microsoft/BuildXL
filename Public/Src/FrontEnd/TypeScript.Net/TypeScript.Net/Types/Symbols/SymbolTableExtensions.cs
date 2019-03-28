// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
