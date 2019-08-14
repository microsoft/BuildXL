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
        private readonly PathTable m_pathTable;

        /// <summary>
        /// Maps paths to parent index
        /// </summary>
        private readonly ConcurrentBigMap<AbsolutePath, int> m_pathToParentIndexMap = new ConcurrentBigMap<AbsolutePath, int>();

        /// <summary>
        /// Maps strings to parent index
        /// </summary>
        private readonly ConcurrentBigSet<(StringId id, InlinedStringKind kind)> m_stringSet = new ConcurrentBigSet<(StringId, InlinedStringKind)>();

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
        public PathTable PathTable => m_pathTable;

        /// <summary>
        /// Creates a writer
        /// </summary>
        public InliningWriter(Stream stream, PathTable pathTable, bool debug = false, bool leaveOpen = true, bool logStats = false)
             : base(debug, stream, leaveOpen, logStats)
        {
            m_pathTable = pathTable;

            // Reserve invalid as 0-th index
            m_pathToParentIndexMap.Add(AbsolutePath.Invalid, 0);
            m_stringSet.Add((new StringId(int.MaxValue), InlinedStringKind.Default));
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
                    PathAtom entryPathName = entryPath.GetName(m_pathTable);

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

            var parentIndex = EnsurePath(path.GetParent(m_pathTable));
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
        public int WriteAndGetIndex(StringId stringId, InlinedStringKind kind = default)
        {
            if (!stringId.IsValid)
            {
                WriteCompact(0);
                return 0;
            }

            var getResult = m_stringSet.GetOrAdd((stringId, kind));

            // Write the index
            WriteCompact(getResult.Index);

            // Check if string is already written
            if (!getResult.IsFound)
            {
                WriteStringIdValue(stringId, kind);
            }

            return getResult.Index;
        }

        /// <todoc />
        public virtual void WriteStringIdValue(in StringId stringId, InlinedStringKind kind)
        {
            var binaryString = m_pathTable.StringTable.GetBinaryString(stringId);
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
