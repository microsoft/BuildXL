// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Serialization;

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// Reads in pips written with RemapWriter and remaps absolute paths, string ids from the value present in the stream to the value in the given for the same path/string in the given context.
    /// </summary>
    internal class PipRemapReader : PipReader
    {
        private readonly PipGraphFragmentContext m_pipGraphFragmentContext;
        private readonly PipExecutionContext m_pipExecutionContext;
        private readonly InliningReader m_inliningReader;
        private readonly PipDataEntriesPointerInlineReader m_pipDataEntriesPointerInlineReader;

        /// <summary>
        /// Create a new RemapReader
        /// </summary>
        public PipRemapReader(PipExecutionContext pipExecutionContext, PipGraphFragmentContext pipGraphFragmentContext, Stream stream, bool debug = false, bool leaveOpen = true)
            : base(debug, pipExecutionContext.StringTable, stream, leaveOpen)
        {
            Contract.Requires(pipExecutionContext != null);
            Contract.Requires(pipGraphFragmentContext != null);
            Contract.Requires(stream != null);

            m_pipExecutionContext = pipExecutionContext;
            m_pipGraphFragmentContext = pipGraphFragmentContext;
            m_inliningReader = new InliningReader(stream, pipExecutionContext.PathTable, debug, leaveOpen);
            m_pipDataEntriesPointerInlineReader = new PipDataEntriesPointerInlineReader(m_inliningReader, stream, pipExecutionContext.PathTable, debug, leaveOpen);
        }

        /// <inheritdoc />
        public override PipId RemapPipId(PipId pipId) => m_pipGraphFragmentContext.RemapPipId(pipId);

        /// <inheritdoc />
        public override PipId ReadPipId() => RemapPipId(base.ReadPipId());

        /// <inheritdoc />
        public override DirectoryArtifact ReadDirectoryArtifact() => m_pipGraphFragmentContext.RemapDirectory(base.ReadDirectoryArtifact());

        /// <inheritdoc />
        public override AbsolutePath ReadAbsolutePath() => m_inliningReader.ReadAbsolutePath();

        /// <inheritdoc />
        public override StringId ReadStringId() => m_inliningReader.ReadStringId();

        /// <inheritdoc />
        public override PathAtom ReadPathAtom() => m_inliningReader.ReadPathAtom();

        /// <inheritdoc />
        public override FullSymbol ReadFullSymbol() => FullSymbol.Create(m_pipExecutionContext.SymbolTable, ReadString());

        /// <inheritdoc />
        public override StringId ReadPipDataEntriesPointer() => m_pipDataEntriesPointerInlineReader.ReadStringId();

        private class PipDataEntriesPointerInlineReader : InliningReader
        {
            private byte[] m_pipDatabuffer = new byte[1024];
            private readonly InliningReader m_baseInliningReader;

            public PipDataEntriesPointerInlineReader(InliningReader baseInliningReader, Stream stream, PathTable pathTable, bool debug = false, bool leaveOpen = true)
                : base(stream, pathTable, debug, leaveOpen)
            {
                m_baseInliningReader = baseInliningReader;
            }

            protected override BinaryStringSegment ReadBinaryStringSegment(ref byte[] buffer)
            {
                int count = ReadInt32Compact();

                return PipDataBuilder.WriteEntries(GetEntries(), count, ref m_pipDatabuffer);

                IEnumerable<PipDataEntry> GetEntries()
                {
                    for (int i = 0; i < count; i++)
                    {
                        yield return PipDataEntry.Deserialize(m_baseInliningReader);
                    }
                }
            }
        }
    }
}
