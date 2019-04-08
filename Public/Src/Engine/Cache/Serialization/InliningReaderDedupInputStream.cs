// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Text;
using Bond.IO;
using BuildXL.Utilities.Serialization;

namespace BuildXL.Engine.Cache.Fingerprints
{
    /// <summary>
    /// A Bond IInputStream which reads to a <see cref="InliningReader"/> using the associated string table to deduplicate strings
    /// </summary>
    internal readonly struct InliningReaderDedupInputStream : IInputStream, ICloneable<InliningReaderDedupInputStream>
    {
        private readonly InliningReader m_reader;

        public InliningReaderDedupInputStream(InliningReader reader)
        {
            m_reader = reader;
        }

        public long Length => m_reader.BaseStream.Length;

        public long Position
        {
            get { return m_reader.BaseStream.Position; }
            set { m_reader.BaseStream.Position = value; }
        }

        public InliningReaderDedupInputStream Clone()
        {
            throw Contract.AssertFailure("This method should not be called");
        }

        public ArraySegment<byte> ReadBytes(int count)
        {
            if (m_reader.ReadBoolean())
            {
                return new ArraySegment<byte>(m_reader.ReadBytes(count));
            }
            else
            {
                Contract.Assert(count == 0);
                return default(ArraySegment<byte>);
            }
        }

        public double ReadDouble() => m_reader.ReadDouble();

        public float ReadFloat() => m_reader.ReadSingle();

        public string ReadString(Encoding encoding, int size)
        {
            var stringId = m_reader.ReadStringId();
            return stringId.IsValid ? stringId.ToString(m_reader.PathTable.StringTable) : null;
        }

        public ushort ReadUInt16() => m_reader.ReadUInt16();

        public uint ReadUInt32() => m_reader.ReadUInt32();

        public ulong ReadUInt64() => m_reader.ReadUInt64();

        public byte ReadUInt8() => m_reader.ReadByte();

        public ushort ReadVarUInt16() => (ushort)m_reader.ReadInt32Compact();

        public uint ReadVarUInt32() => m_reader.ReadUInt32Compact();

        public ulong ReadVarUInt64() => unchecked((ulong)m_reader.ReadInt64Compact());

        public void SkipBytes(int count)
        {
            throw Contract.AssertFailure("This method should not be called");
        }
    }
}
