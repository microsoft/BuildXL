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

        /// <summary>
        /// Read a directory artifact
        /// </summary>
        public override DirectoryArtifact ReadDirectoryArtifact()
        {
            return m_pipGraphFragmentContext.RemapDirectory(base.ReadDirectoryArtifact());
        }

        /// <summary>
        /// Reads an absolute path
        /// </summary>
        public override AbsolutePath ReadAbsolutePath()
        {
            return m_inliningReader.ReadAbsolutePath();
        }

        /// <summary>
        /// Reads a string id
        /// </summary>
        public override StringId ReadStringId()
        {
            return m_inliningReader.ReadStringId();
        }

        /// <summary>
        /// Reads a path atom
        /// </summary>
        public override PathAtom ReadPathAtom()
        {
            return m_inliningReader.ReadPathAtom();
        }

        /// <summary>
        /// Reads a full symbol
        /// </summary>
        public override FullSymbol ReadFullSymbol()
        {
            return FullSymbol.Create(m_pipExecutionContext.SymbolTable, ReadString());
        }

        /// <summary>
        /// Reads a pip data entries pointer.
        /// </summary>
        public override StringId ReadPipDataEntriesPointer()
        {
            return m_pipDataEntriesPointerInlineReader.ReadStringId();
        }

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
