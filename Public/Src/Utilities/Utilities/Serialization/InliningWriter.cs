// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Utilities.Collections;

namespace BuildXL.Utilities.Serialization
{
    /// <summary>
    /// Specialized BuildXLWriter which inlines written AbsolutePath/StringId data (name and parent/characters) so that
    /// they can be deserialized and populate a path/string table which does not necessarily contain them. This requires
    /// <see cref="InliningReader"/> to be used for deserialization.
    /// </summary>
    public class InliningWriter : BuildXLWriter
    {
        /// <summary>
        /// Maps paths to parent index
        /// </summary>
        private readonly ConcurrentBigMap<AbsolutePath, int> m_pathToParentIndexMap = new ConcurrentBigMap<AbsolutePath, int>();

        /// <summary>
        /// Maps strings to parent index
        /// </summary>
        private readonly ConcurrentBigSet<StringId> m_stringSet = new ConcurrentBigSet<StringId>();

        /// <summary>
        /// Serialized path count
        /// </summary>
        public int SerializedPathCount => m_pathToParentIndexMap.Count;

        /// <summary>
        /// Serialized string count
        /// </summary>
        public int SerializedStringCount => m_stringSet.Count;

        private byte[] m_buffer = new byte[1024];

        /// <summary>
        /// The underlying path table
        /// </summary>
        public PathTable PathTable { get; private set; }

        /// <summary>
        /// Creates a writer
        /// </summary>
        public InliningWriter(Stream stream, PathTable pathTable, bool debug = false, bool leaveOpen = true, bool logStats = false)
             : base(debug, stream, leaveOpen, logStats)
        {
            PathTable = pathTable;

            // Reserve invalid as 0-th index
            m_pathToParentIndexMap.Add(AbsolutePath.Invalid, 0);
            m_stringSet.Add(new StringId(int.MaxValue));
        }

        /// <inheritdoc />
        public override void Write(AbsolutePath value)
        {
            WriteAndGetIndex(value);
        }

        /// <summary>
        /// Adds the paths and gets the index of the path in list (this index is valid both during serialization and deser
        /// </summary>
        public int WriteAndGetIndex(AbsolutePath path)
        {
            if (!path.IsValid)
            {
                WriteCompact(0);
                return 0;
            }

            var maxWrittenPath = m_pathToParentIndexMap.Count - 1;
            var index = EnsurePath(path);
            WriteCompact(index);

            if (index > maxWrittenPath)
            {
                for (int i = maxWrittenPath + 1; i <= index; i++)
                {
                    var entryPathAndParentIndex = m_pathToParentIndexMap.BackingSet[i];
                    int entryParentIndex = entryPathAndParentIndex.Value;
                    AbsolutePath entryPath = entryPathAndParentIndex.Key;
                    PathAtom entryPathName = entryPath.GetName(PathTable);

                    WriteCompact(entryParentIndex);
                    Write(entryPathName);
                }
            }

            return index;
        }

        private int EnsurePath(AbsolutePath path)
        {
            if (!path.IsValid)
            {
                return 0;
            }

            var getResult = m_pathToParentIndexMap.TryGet(path);
            if (getResult.IsFound)
            {
                return getResult.Index;
            }

            var parentIndex = EnsurePath(path.GetParent(PathTable));
            var addResult = m_pathToParentIndexMap.GetOrAdd(path, parentIndex);
            Contract.Assert(!addResult.IsFound);

            return addResult.Index;
        }

        /// <inheritdoc />
        public override void Write(PathAtom value)
        {
            WriteAndGetIndex(value.StringId);
        }

        /// <inheritdoc />
        public override void Write(StringId value)
        {
            WriteAndGetIndex(value);
        }

        /// <summary>
        /// Adds the strings and gets the index of the string in list (this index is valid both during serialization and deser
        /// </summary>
        private int WriteAndGetIndex(StringId stringId)
        {
            if (!stringId.IsValid)
            {
                WriteCompact(0);
                return 0;
            }

            var getResult = m_stringSet.GetOrAdd(stringId);

            // Write the index
            WriteCompact(getResult.Index);

            // Check if string is already written
            if (!getResult.IsFound)
            {
                WriteBinaryStringSegment(stringId);
            }

            return getResult.Index;
        }

        /// <todoc />
        protected virtual void WriteBinaryStringSegment(in StringId stringId)
        {
            var binaryString = PathTable.StringTable.GetBinaryString(stringId);
            var stringByteLength = binaryString.UnderlyingBytes.Length;

            CollectionUtilities.GrowArrayIfNecessary(ref m_buffer, stringByteLength);

            // Write if string is ascii or UTF-16
            Write(binaryString.OnlyContains8BitChars);

            // Write the byte length
            WriteCompact(stringByteLength);

            // Copy bytes to buffer and write bytes
            binaryString.UnderlyingBytes.CopyTo(
                index: 0,
                destinationArray: m_buffer,
                destinationIndex: 0,
                length: stringByteLength);
            Write(m_buffer, 0, stringByteLength);

        }
    }
}
