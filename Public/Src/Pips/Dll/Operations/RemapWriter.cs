// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Serialization;

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// Writes absolute paths, string ids, and pipdataentries so the values of each item are present inline in the stream.
    /// Format should be read by the remapreader.
    /// </summary>
    internal class RemapWriter : PipWriter
    {
        private InliningWriter m_inliningWriter;
        private SymbolTable m_symbolTable;

        /// <summary>
        /// Creates a new RemapWriter
        /// </summary>
        public RemapWriter(Stream stream, PipExecutionContext context, bool debug = false, bool leaveOpen = true, bool logStats = false)
            : base(debug, stream, leaveOpen, logStats)
        {
            m_inliningWriter = new InnerInliningWriter(stream, context.PathTable, debug, leaveOpen, logStats);
            m_symbolTable = context.SymbolTable;
        }

        /// <summary>
        /// Writes an absolute path
        /// </summary>
        public override void Write(AbsolutePath value)
        {
            m_inliningWriter.Write(value);
        }

        /// <summary>
        /// Writes a path atom
        /// </summary>
        public override void Write(PathAtom value)
        {
            m_inliningWriter.Write(value);
        }

        /// <summary>
        /// Writes a string id
        /// </summary>
        public override void Write(StringId value)
        {
            m_inliningWriter.Write(value);
        }

        /// <summary>
        /// Writes a pip data id
        /// </summary>
        public override void WritePipDataId(in StringId value)
        {
            m_inliningWriter.WriteAndGetIndex(value, InlinedStringKind.PipData);
        }

        /// <summary>
        /// Writes a full symbol
        /// </summary>
        public override void Write(FullSymbol value)
        {
            Write(value.ToString(m_symbolTable));
        }

        private class InnerInliningWriter : InliningWriter
        {
            public InnerInliningWriter(Stream stream, PathTable pathTable, bool debug = false, bool leaveOpen = true, bool logStats = false)
                : base(stream, pathTable, debug, leaveOpen, logStats)
            {
            }

            public override void WriteStringIdValue(in StringId stringId, InlinedStringKind kind)
            {
                if (kind == InlinedStringKind.PipData)
                {
                    var binaryString = PathTable.StringTable.GetBinaryString(stringId);
                    var entries = new PipDataEntryList(binaryString.UnderlyingBytes);
                    WriteCompact(entries.Count);
                    foreach (var e in entries)
                    {
                        e.Serialize(this);
                    }
                }
                else
                {
                    base.WriteStringIdValue(stringId, kind);
                }
            }
        }
    }
}
