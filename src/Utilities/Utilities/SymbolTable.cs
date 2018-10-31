// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Optimized table of ordinally-compared case-sensitive dotted identifiers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This data structure only ever grows, identifiers are never removed. The entire abstraction is completely
    /// thread-safe.
    /// </para>
    ///
    /// <para>
    /// When all insertions have been done, the table can be frozen, which discards some transient state and
    /// cuts heap consumption to a minimum. Trying to add a new path once the table has been frozen will crash.
    /// </para>
    /// </remarks>
    public sealed class SymbolTable : HierarchicalNameTable
    {
        /// <summary>
        /// Envelope for serialization
        /// </summary>
        public static readonly FileEnvelope FileEnvelope = new FileEnvelope(name: "SymbolTable", version: 0);

        private const char IdentifierDelimiter = '.';

        /// <summary>
        /// Initializes a new identifier table.
        /// </summary>
        public SymbolTable(StringTable stringTable)
            : base(stringTable, false, IdentifierDelimiter)
        {
            Contract.Requires(stringTable != null);
        }

        /// <summary>
        /// Initializes a new identifier table with a private string table.
        /// </summary>
        public SymbolTable()
            : base(new StringTable(), false, '.')
        {
        }

        /// <summary>
        /// Deserializes
        /// </summary>
        /// <remarks>
        /// Returns null if the StringTable task returns null, or if it turns out that hash codes no longer match
        /// </remarks>
        public static async Task<SymbolTable> DeserializeAsync(BuildXLReader reader, Task<StringTable> stringTableTask)
        {
            Contract.Requires(reader != null);
            Contract.Requires(stringTableTask != null);

            var state = await ReadSerializationStateAsync(reader, stringTableTask);
            var stringTable = await stringTableTask;
            if (state != null && stringTable != null)
            {
                return new SymbolTable(state, stringTable);
            }

            return null;
        }

        private SymbolTable(SerializedState state, StringTable stringTable)
            : base(state, stringTable, false, IdentifierDelimiter)
        {
        }
    }
}
