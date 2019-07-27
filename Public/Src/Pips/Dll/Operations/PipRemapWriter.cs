// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Serialization;

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// Writes absolute paths, string ids, and pipdataentries so the values of each item are present inline in the stream.
    /// Format should be read by the <see cref="PipRemapReader"/>.
    /// </summary>
    internal class PipRemapWriter : PipWriter
    {
        private readonly InliningWriter m_inliningWriter;
        private readonly PipDataEntriesPointerInlineWriter m_pipDataEntriesPointerInlineWriter;
        private readonly SymbolTable m_symbolTable;
        private readonly PipGraphFragmentContext m_context;

        /// <summary>
        /// Creates a new RemapWriter
        /// </summary>
        public PipRemapWriter(Stream stream, PipExecutionContext context, PipGraphFragmentContext pipGraphFragmentContext, bool debug = false, bool leaveOpen = true, bool logStats = false)
            : base(debug, stream, leaveOpen, logStats)
        {
            m_inliningWriter = new InliningWriter(stream, context.PathTable, debug, leaveOpen, logStats);
            m_pipDataEntriesPointerInlineWriter = new PipDataEntriesPointerInlineWriter(stream, context.PathTable, debug, leaveOpen, logStats);
            m_symbolTable = context.SymbolTable;
            m_context = pipGraphFragmentContext;
        }

        /// <summary>
        /// Writes an absolute path
        /// </summary>
        public override void Write(AbsolutePath value)
        {
            m_inliningWriter.Write(value);
        }

        /// <summary>
        /// Writes a directory artifact
        /// </summary>
        public override void Write(DirectoryArtifact value)
        {
            FullSymbol variableName;
            if (m_context.TryGetVariableNameForDirectory(value, out variableName))
            {
                m_inliningWriter.Write(true);
                m_inliningWriter.Write(variableName);
                m_inliningWriter.Write(value);
            }
            else
            {
                m_inliningWriter.Write(false);
                m_inliningWriter.Write(value);
            }
        }

        /// <summary>
        /// Writes a pip id value
        /// </summary>
        public override void WritePipIdValue(uint value)
        {
            FullSymbol variableName;
            if (m_context.TryGetVariableNameForPipIdValue(value, out variableName))
            {
                m_inliningWriter.Write(true);
                m_inliningWriter.Write(variableName);
                m_inliningWriter.Write(value);
            }
            else
            {
                m_inliningWriter.Write(false);
                m_inliningWriter.Write(value);
            }
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
        public override void WritePipDataEntriesPointer(in StringId value)
        {
            m_pipDataEntriesPointerInlineWriter.Write(value);
        }

        /// <summary>
        /// Writes a full symbol
        /// </summary>
        public override void Write(FullSymbol value)
        {
            Write(value.ToString(m_symbolTable));
        }

        private class PipDataEntriesPointerInlineWriter : InliningWriter
        {
            public PipDataEntriesPointerInlineWriter(Stream stream, PathTable pathTable, bool debug = false, bool leaveOpen = true, bool logStats = false)
                : base(stream, pathTable, debug, leaveOpen, logStats)
            {
            }

            protected override void WriteBinaryStringSegment(in StringId stringId)
            {
                var binaryString = PathTable.StringTable.GetBinaryString(stringId);
                var entries = new PipDataEntryList(binaryString.UnderlyingBytes);
                WriteCompact(entries.Count);
                foreach (var e in entries)
                {
                    e.Serialize(this);
                }
            }
        }
    }
}
