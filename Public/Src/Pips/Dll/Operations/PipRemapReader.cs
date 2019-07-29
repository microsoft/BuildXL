// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
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
        /// <summary>
        /// RemapReader PipGraphFragmentContext
        /// </summary>
        public readonly PipGraphFragmentContext Context;

        private readonly PipExecutionContext m_executionContext;
        private readonly SymbolTable m_symbolTable;
        private readonly InliningReader m_inliningReader;
        private readonly PipDataEntriesPointerInlineReader m_pipDataEntriesPointerInlineReader;

        /// <summary>
        /// Create a new RemapReader
        /// </summary>
        public PipRemapReader(PipGraphFragmentContext fragmentContext, Stream stream, PipExecutionContext context, bool debug = false, bool leaveOpen = true)
            : base(debug, context.StringTable, stream, leaveOpen)
        {
            Context = fragmentContext;
            m_executionContext = context;
            m_inliningReader = new InliningReader(stream, context.PathTable, debug, leaveOpen);
            m_pipDataEntriesPointerInlineReader = new PipDataEntriesPointerInlineReader(m_inliningReader, stream, context.PathTable, debug, leaveOpen);
            m_symbolTable = context.SymbolTable;
        }

        /// <summary>
        /// Read a directory artifact
        /// </summary>
        public override DirectoryArtifact ReadDirectoryArtifact()
        {
            var isDirectoryStoredAsVariableName = base.ReadBoolean();
            DirectoryArtifact directoryArtifact;
            if (isDirectoryStoredAsVariableName)
            {
                var directoryArtifactVariableName = base.ReadFullSymbol();
                var serializedDirectoryArtifact = base.ReadDirectoryArtifact();
                if (!Context.TryGetDirectoryArtifactForVariableName(directoryArtifactVariableName, out directoryArtifact))
                {
                    directoryArtifact = serializedDirectoryArtifact;
                    Context.AddDirectoryMapping(directoryArtifactVariableName, directoryArtifact);
                }
            }
            else
            {
                directoryArtifact = base.ReadDirectoryArtifact();
            }

            return Context.RemapDirectory(directoryArtifact);
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
            return FullSymbol.Create(m_symbolTable, ReadString());
        }

        /// <summary>
        /// Reads a pip data entries pointer.
        /// </summary>
        public override StringId ReadPipDataEntriesPointer()
        {
            return m_pipDataEntriesPointerInlineReader.ReadStringId();
        }

        /// <summary>
        /// Reads the value of a pip id
        /// </summary>
        public override uint ReadPipIdValue()
        {
            var isPipIdValueStoredAsVariableName = base.ReadBoolean();
            uint pipIdValue;
            if (isPipIdValueStoredAsVariableName)
            {
                var pipIdValueVariableName = base.ReadFullSymbol();
                var serializedPipIdValue = base.ReadPipIdValue();
                if (!Context.TryGetPipIdValueForVariableName(pipIdValueVariableName, out pipIdValue))
                {
                    pipIdValue = serializedPipIdValue;
                    Context.AddPipIdValueMapping(pipIdValueVariableName, pipIdValue);
                }
            }
            else
            {
                pipIdValue = base.ReadPipIdValue();
            }

            return Context.RemapPipIdValue(pipIdValue);
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
