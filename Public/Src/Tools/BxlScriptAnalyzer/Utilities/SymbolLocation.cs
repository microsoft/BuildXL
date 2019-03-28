// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using JetBrains.Annotations;
using TypeScript.Net.Types;

namespace TypeScript.Net.DScript
{
    /// <nodoc />
    public enum SymbolLocationKind
    {
        /// <nodoc />
        File,
        
        /// <nodoc />
        LocalLocation
    }

    /// <summary>
    /// Represents location of a symbol.
    /// </summary>
    public sealed class SymbolLocation
    {
        /// <nodoc />
        public SymbolLocationKind Kind { get; }

        /// <nodoc />
        public ISymbol Symbol { get; }

        /// <nodoc />
        public TextRange Range { get; }

        /// <nodoc />
        public string Path { get; }

        /// <nodoc />
        private SymbolLocation(SymbolLocationKind kind, string path, ISymbol symbol, TextRange range)
        {
            Kind = kind;
            Symbol = symbol;
            Range = range;
            Path = path;
        }

        /// <summary>
        /// Creates a file symbol location.
        /// </summary>
        public static SymbolLocation FileLocation(string path) => new SymbolLocation(SymbolLocationKind.File, path: path, symbol: null, range: default);
        
        /// <summary>
        /// Creates a file-local location of a symbol.
        /// </summary>
        public static SymbolLocation LocalLocation(string path, TextRange range, [CanBeNull]ISymbol symbol) => new SymbolLocation(SymbolLocationKind.LocalLocation, path: path, symbol: symbol, range: range);
    }
}
