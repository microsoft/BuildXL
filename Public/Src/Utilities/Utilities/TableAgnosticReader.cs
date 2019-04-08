// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using BuildXL.Utilities.Qualifier;

namespace BuildXL.Utilities
{
    /// <summary>
    /// A reader that deserializes path, string, symbol-related and qualifier objects in a table-agnostic way (e.g. as strings)
    /// </summary>
    /// <remarks>
    /// Useful for deserializing objects that may come from different path/string/symbol/qualifier tables. Should be used in correspondance with <see cref="TableAgnosticWriter"/>.
    /// TODO: Reconsider this approach once perf measurements have been performed.
    /// </remarks>
    public class TableAgnosticReader : QualifierTableAgnosticReader
    {
        private readonly PathTable m_pathTable;
        private readonly SymbolTable m_symbolTable;
        private readonly StringTable m_stringTable;

        /// <nodoc/>
        public TableAgnosticReader(PathTable pathTable, SymbolTable symbolTable, QualifierTable qualifierTable, bool debug, Stream stream, bool leaveOpen)
            : base(qualifierTable, debug, stream, leaveOpen)
        {
            m_pathTable = pathTable;
            m_symbolTable = symbolTable;
            m_stringTable = pathTable.StringTable;
        }

        /// <summary>
        /// Reads a StringId from a string representation
        /// </summary>
        public override StringId ReadStringId()
        {
            Start<StringId>();
            var isValid = ReadBoolean();
            var value = isValid ? m_stringTable.AddString(ReadString()) : StringId.Invalid;
            End();
            return value;
        }

        /// <summary>
        /// Reads an AbsolutePath from a string representation
        /// </summary>
        public override AbsolutePath ReadAbsolutePath()
        {
            Start<AbsolutePath>();
            var isValid = ReadBoolean();
            var value = isValid ? AbsolutePath.Create(m_pathTable, ReadString()) : AbsolutePath.Invalid;
            End();
            return value;
        }

        /// <summary>
        /// Reads a RelativePath from a string representation
        /// </summary>
        public override RelativePath ReadRelativePath()
        {
            Start<RelativePath>();
            var isValid = ReadBoolean();
            var value = isValid ? RelativePath.Create(m_stringTable, ReadString()) : RelativePath.Invalid;
            End();
            return value;
        }

        /// <summary>
        /// Reads TokenText from a string representation
        /// </summary>
        public override TokenText ReadTokenText()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Reads a FullSymbol from a string representation
        /// </summary>
        public override FullSymbol ReadFullSymbol()
        {
            Start<FullSymbol>();
            var isValid = ReadBoolean();
            var value = isValid ? FullSymbol.Create(m_symbolTable, ReadString()) : FullSymbol.Invalid;
            End();
            return value;
        }

        /// <summary>
        /// Reads a PathAtom from a string representation
        /// </summary>
        public override PathAtom ReadPathAtom()
        {
            Start<PathAtom>();
            var isValid = ReadBoolean();
            PathAtom value = isValid ? PathAtom.Create(m_stringTable, ReadString()) : PathAtom.Invalid;
            End();
            return value;
        }

        /// <summary>
        /// Reads a SymbolAtom from a string representation
        /// </summary>
        public override SymbolAtom ReadSymbolAtom()
        {
            Start<SymbolAtom>();
            var isValid = ReadBoolean();
            SymbolAtom value = isValid ? SymbolAtom.Create(m_stringTable, ReadString()) : SymbolAtom.Invalid;
            End();
            return value;
        }
    }
}
