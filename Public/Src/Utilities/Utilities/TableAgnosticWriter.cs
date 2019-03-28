// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using BuildXL.Utilities.Qualifier;

namespace BuildXL.Utilities
{
    /// <summary>
    /// A writer that serializes path, string and symbol-related objects in a table-agnostic way
    /// </summary>
    /// <remarks>
    /// Useful for serializing objects that may come from different path/string/symbol tables. Should be used in correspondance with <see cref="TableAgnosticReader"/>.
    /// TODO: Reconsider this approach once perf measurements have been performed.
    /// </remarks>
    public class TableAgnosticWriter : QualifierTableAgnosticWriter
    {
        private readonly PathTable m_pathTable;
        private readonly SymbolTable m_symbolTable;
        private readonly StringTable m_stringTable;

        /// <nodoc/>
        public TableAgnosticWriter(PathTable pathTable, SymbolTable symbolTable, QualifierTable qualifierTable, bool debug, Stream stream, bool leaveOpen, bool logStats)
            : base(qualifierTable, debug, stream, leaveOpen, logStats)
        {
            m_pathTable = pathTable;
            m_symbolTable = symbolTable;
            m_stringTable = pathTable.StringTable;
        }

        /// <summary>
        /// Writes a StringId using its underlying string representation
        /// </summary>
        public override void Write(StringId value)
        {
            Start<StringId>();
            Write(value.IsValid);
            if (value.IsValid)
            {
                Write(value.ToString(m_stringTable));
            }

            End();
        }

        /// <summary>
        /// Writes an AbsolutePath using its underlying string representation
        /// </summary>
        public override void Write(AbsolutePath value)
        {
            Start<AbsolutePath>();
            Write(value.IsValid);
            if (value.IsValid)
            {
                Write(value.ToString(m_pathTable));
            }

            End();
        }

        /// <summary>
        /// Write a RelativePath using its underlying string representation
        /// </summary>
        public override void Write(RelativePath value)
        {
            Start<RelativePath>();
            Write(value.IsValid);
            if (value.IsValid)
            {
                Write(value.ToString(m_stringTable));
            }

            End();
        }

        /// <summary>
        /// Writes TokenText using its underlying string representation
        /// </summary>
        public override void Write(TokenText value)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Writes a FullSymbol using its underlying string representation
        /// </summary>
        public override void Write(FullSymbol value)
        {
            Start<FullSymbol>();
            Write(value.IsValid);
            if (value.IsValid)
            {
                Write(value.ToString(m_symbolTable));
            }

            End();
        }

        /// <summary>
        /// Writes a PathAtom using its underlying string representation
        /// </summary>
        public override void Write(PathAtom value)
        {
            Start<PathAtom>();
            Write(value.IsValid);
            if (value.IsValid)
            {
                Write(value.ToString(m_stringTable));
            }

            End();
        }

        /// <summary>
        /// Writes a SymbolAtom using its underlying string representation
        /// </summary>
        public override void Write(SymbolAtom value)
        {
            Start<SymbolAtom>();
            Write(value.IsValid);
            if (value.IsValid)
            {
                Write(value.ToString(m_stringTable));
            }

            End();
        }
    }
}
