using System.Collections.Generic;
using System.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Serialization;

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// Reads in pips written with RemapWriter and remaps absolute paths, string ids from the value present in the stream to the value in the given for the same path/string in the given context.
    /// </summary>
    internal class RemapReader : PipReader
    {
        /// <summary>
        /// RemapReader PipGraphFragmentContext
        /// </summary>
        public readonly PipGraphFragmentContext Context;
        private readonly PipExecutionContext m_executionContext;
        private SymbolTable m_symbolTable;
        private InliningReader m_inliningReader;

        /// <summary>
        /// RemapReader
        /// </summary>
        public RemapReader(PipGraphFragmentContext fragmentContext, Stream stream, PipExecutionContext context, bool debug = false, bool leaveOpen = true)
            : base(debug, context.StringTable, stream, leaveOpen)
        {
            Context = fragmentContext;
            m_executionContext = context;
            m_inliningReader = new InnerInliningReader(stream, context.PathTable, debug, leaveOpen);
            m_symbolTable = context.SymbolTable;
        }

        /// <summary>
        /// RemapReader
        /// </summary>
        public override DirectoryArtifact ReadDirectoryArtifact()
        {
            var directoryArtifact = base.ReadDirectoryArtifact();
            return Context.Remap(directoryArtifact);
        }

        /// <summary>
        /// RemapReader
        /// </summary>
        public override AbsolutePath ReadAbsolutePath()
        {
            return m_inliningReader.ReadAbsolutePath();
        }

        /// <summary>
        /// RemapReader
        /// </summary>
        public override StringId ReadStringId()
        {
            return m_inliningReader.ReadStringId();
        }

        /// <summary>
        /// RemapReader
        /// </summary>
        public override PathAtom ReadPathAtom()
        {
            return m_inliningReader.ReadPathAtom();
        }

        /// <summary>
        /// RemapReader
        /// </summary>
        public DirectoryArtifact ReadUnmappedDirectoryArtifact()
        {
            return base.ReadDirectoryArtifact();
        }

        /// <summary>
        /// RemapReader
        /// </summary>
        public override FullSymbol ReadFullSymbol()
        {
            return FullSymbol.Create(m_symbolTable, ReadString());
        }

        /// <summary>
        /// RemapReader
        /// </summary>
        public override StringId ReadPipDataId()
        {
            return m_inliningReader.ReadStringId(InlinedStringKind.PipData);
        }

        /// <summary>
        /// RemapReader
        /// </summary>
        public override uint ReadPipIdValue()
        {
            var pipIdValue = base.ReadPipIdValue();
            return Context.Remap(pipIdValue);
        }

        private class InnerInliningReader : InliningReader
        {
            private byte[] m_pipDatabuffer = new byte[1024];

            public InnerInliningReader(Stream stream, PathTable pathTable, bool debug = false, bool leaveOpen = true)
                : base(stream, pathTable, debug, leaveOpen)
            {
            }

            public override BinaryStringSegment ReadStringIdValue(InlinedStringKind kind, ref byte[] buffer)
            {
                if (kind == InlinedStringKind.PipData)
                {
                    int count = ReadInt32Compact();

                    return PipDataBuilder.WriteEntries(GetEntries(), count, ref m_pipDatabuffer);

                    IEnumerable<PipDataEntry> GetEntries()
                    {
                        for (int i = 0; i < count; i++)
                        {
                            yield return PipDataEntry.Deserialize(this);
                        }
                    }
                }
                else
                {
                    return base.ReadStringIdValue(kind, ref buffer);
                }
            }
        }
    }
}
