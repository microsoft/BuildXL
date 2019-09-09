// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
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
        private readonly PipGraphFragmentContext m_pipGraphFragmentContext;
        private readonly PipExecutionContext m_pipExecutionContext;

        /// <summary>
        /// Creates an instance of <see cref="PipRemapWriter"/>.
        /// </summary>
        public PipRemapWriter(PipExecutionContext pipExecutionContext, PipGraphFragmentContext pipGraphFragmentContext, Stream stream, bool debug = false, bool leaveOpen = true, bool logStats = false)
            : base(debug, stream, leaveOpen, logStats)
        {
            Contract.Requires(pipExecutionContext != null);
            Contract.Requires(pipGraphFragmentContext != null);
            Contract.Requires(stream != null);

            m_pipExecutionContext = pipExecutionContext;
            m_pipGraphFragmentContext = pipGraphFragmentContext;
            m_inliningWriter = new InliningWriter(stream, pipExecutionContext.PathTable, debug, leaveOpen, logStats);
            m_pipDataEntriesPointerInlineWriter = new PipDataEntriesPointerInlineWriter(m_inliningWriter, stream, pipExecutionContext.PathTable, debug, leaveOpen, logStats);
        }

        /// <inheritdoc />
        public override void Write(AbsolutePath value) => m_inliningWriter.Write(value);

        /// <inheritdoc />
        public override void Write(PathAtom value) => m_inliningWriter.Write(value);

        /// <inheritdoc />
        public override void Write(StringId value) => m_inliningWriter.Write(value);

        /// <inheritdoc />
        public override void WritePipDataEntriesPointer(in StringId value) => m_pipDataEntriesPointerInlineWriter.Write(value);

        /// <inheritdoc />
        public override void Write(FullSymbol value) => Write(value.ToString(m_pipExecutionContext.SymbolTable));

        private class PipDataEntriesPointerInlineWriter : InliningWriter
        {
            private readonly InliningWriter m_baseInliningWriter;

            public PipDataEntriesPointerInlineWriter(InliningWriter baseInliningWriter, Stream stream, PathTable pathTable, bool debug = false, bool leaveOpen = true, bool logStats = false)
                : base(stream, pathTable, debug, leaveOpen, logStats)
            {
                m_baseInliningWriter = baseInliningWriter;
            }

            protected override void WriteBinaryStringSegment(in StringId stringId)
            {
                var binaryString = PathTable.StringTable.GetBinaryString(stringId);
                var entries = new PipDataEntryList(binaryString.UnderlyingBytes);
                WriteCompact(entries.Count);
                foreach (var e in entries)
                {
                    // Use base inlining writer for serializing the entries because
                    // this writer is only for serializing pip data entries pointer.
                    e.Serialize(m_baseInliningWriter);
                }
            }
        }
    }
}
