// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using BuildXL.Utilities.Collections;

namespace BuildXL.Utilities.Serialization
{
    /// <summary>
    /// Specialized BuildXLReader which reads inline written AbsolutePath/StringId data (name and parent/characters) so that
    /// to populate a path/string table which does not necessarily contain them. This requires
    /// <see cref="InliningWriter"/> to be used for serialization.
    /// </summary>
    public class InliningReader : BuildXLReader
    {
        private readonly PathTable m_pathTable;

        /// <summary>
        /// Maps paths to parent index
        /// </summary>
        private readonly ConcurrentDenseIndex<AbsolutePath> m_readPaths = new ConcurrentDenseIndex<AbsolutePath>(debug: false);

        /// <summary>
        /// The maximum path id read so far.
        /// Initial value is 1 (0 is reserved for invalid)
        /// </summary>
        private int m_maxReadPathIndex = 0;

        /// <summary>
        /// Maps strings to parent index
        /// </summary>
        private readonly ConcurrentDenseIndex<StringId> m_readStrings = new ConcurrentDenseIndex<StringId>(debug: false);

        /// <summary>
        /// The maximum string id read so far.
        /// Initial value is 1 (0 is reserved for invalid)
        /// </summary>
        private int m_maxReadStringIndex = 0;

        /// <summary>
        /// The underlying path table
        /// </summary>
        public PathTable PathTable => m_pathTable;

        /// <summary>
        /// Serialized path count
        /// </summary>
        public int DeserializedPathCount => m_maxReadPathIndex;

        /// <summary>
        /// Serialized string count
        /// </summary>
        public int SerializedStringCount => m_maxReadStringIndex;

        private byte[] m_buffer = new byte[1024];

        /// <summary>
        /// Creates a reader
        /// </summary>
        public InliningReader(Stream stream, PathTable pathTable, bool debug = false, bool leaveOpen = true)
             : base(debug, stream, leaveOpen)
        {
            m_pathTable = pathTable;
        }

        /// <inheritdoc />
        public override AbsolutePath ReadAbsolutePath()
        {
            // Read the index
            var index = ReadInt32Compact();

            // Check if string already encountered
            if (index > m_maxReadPathIndex)
            {
                AbsolutePath entryPath = AbsolutePath.Invalid;
                for (int i = m_maxReadPathIndex + 1; i <= index; i++)
                {
                    int entryParentIndex = ReadInt32Compact();
                    PathAtom entryPathName = ReadPathAtom();
                    AbsolutePath parentPath = m_readPaths[(uint)entryParentIndex];
                    entryPath = parentPath.IsValid ?
                        parentPath.Combine(m_pathTable, entryPathName) :
                        AbsolutePath.Create(m_pathTable, entryPathName.ToString(m_pathTable.StringTable) + Path.DirectorySeparatorChar);
                    m_readPaths[(uint)i] = entryPath;
                }

                m_maxReadPathIndex = index;
                return entryPath;
            }

            return m_readPaths[(uint)index];
        }

        /// <inheritdoc />
        public override PathAtom ReadPathAtom()
        {
            var stringId = ReadStringId();
            return stringId.IsValid ? new PathAtom(stringId) : PathAtom.Invalid;
        }

        /// <inheritdoc />
        public override StringId ReadStringId()
        {
            return ReadStringId(kind: InlinedStringKind.Default);
        }

        /// <todoc />
        public StringId ReadStringId(InlinedStringKind kind)
        {
            // Read the index
            var index = ReadInt32Compact();

            // Check if string already encountered
            if (index > m_maxReadStringIndex)
            {
                var binaryString = ReadStringIdValue(kind, ref m_buffer);
                var stringId = m_pathTable.StringTable.AddString(binaryString);
                m_readStrings[(uint)index] = stringId;
                m_maxReadStringIndex = index;
            }

            return m_readStrings[(uint)index];
        }

        /// <todoc />
        public virtual BinaryStringSegment ReadStringIdValue(InlinedStringKind kind, ref byte[] buffer)
        {
            // This is a new string
            // Read if string is ascii or UTF-16
            bool isAscii = ReadBoolean();

            // Read the byte length
            int byteLength = ReadInt32Compact();

            CollectionUtilities.GrowArrayIfNecessary(ref buffer, byteLength);

            // Read the bytes into the buffer
            Read(buffer, 0, byteLength);

            var binaryString = new BinaryStringSegment(buffer, 0, byteLength, isAscii);
            return binaryString;
        }
    }
}
