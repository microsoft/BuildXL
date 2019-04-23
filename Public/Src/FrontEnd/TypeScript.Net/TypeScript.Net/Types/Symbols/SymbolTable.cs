// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities.Collections;
using JetBrains.Annotations;

namespace TypeScript.Net.Types
{
    /// <summary>
    /// Symbol table used by the checker.
    /// </summary>
    public sealed class SymbolTable : ISymbolTable
    {
        private readonly bool m_isReadOnly = false;

        /// <summary>
        /// Readonly empty instance of the symbol table.
        /// </summary>
        public static SymbolTable Empty { get; } = new SymbolTable(isReadonly: true);

        /// <inheritdoc />
        public int Count => m_symbols.Count;

        private readonly Dictionary<string, ISymbol> m_symbols;

        /// <nodoc/>
        public SymbolTable(int capacity = 4, bool isReadonly = false)
        {
            m_symbols = new Dictionary<string, ISymbol>(capacity);
            m_isReadOnly = isReadonly;
        }

        /// <inheritdoc/>
        [CanBeNull]
        public ISymbol this[string index]
        {
            get
            {
                ISymbol value;
                if (index != null && m_symbols.TryGetValue(index, out value))
                {
                    return value;
                }

                return null;
            }

            set
            {
                Contract.Assert(value != null);
                Contract.Assert(!m_isReadOnly);

                m_symbols[index] = value;
            }
        }

        /// <inheritdoc/>
        public Map<ISymbol>.Enumerator GetEnumerator()
        {
            return m_symbols.GetEnumerator();
        }

        /// <inheritdoc/>
        public bool TryGetSymbol(string index, out ISymbol result)
        {
            return m_symbols.TryGetValue(index, out result);
        }

        /// <nodoc/>
        public Map<ISymbol>.ValueCollection GetSymbols()
        {
            return m_symbols.Values;
        }

        /// <nodoc/>
        public static bool HasProperty([NotNull] ISymbolTable table, [NotNull] string id)
        {
            return table[id] != null;
        }

        /// <nodoc/>
        public static bool TryGetProperty([NotNull] ISymbolTable table, [NotNull] string id, out ISymbol result)
        {
            return table.TryGetSymbol(id, out result);
        }

        /// <nodoc />
        internal static ISymbolTable Create([NotNull]IReadOnlyList<ISymbol> symbols)
        {
            ISymbolTable result = new SymbolTable(symbols.Count);
            foreach (var symbol in symbols.AsStructEnumerable())
            {
                result[symbol.Name] = symbol;
            }

            return result;
        }

        /// <nodoc />
        [NotNull]
        internal static ISymbolTable Create()
        {
            return new SymbolTable();
        }
    }
}
