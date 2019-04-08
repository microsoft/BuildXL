// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Text;
using Bond.IO;
using BuildXL.Utilities;
using BuildXL.Utilities.Serialization;

namespace BuildXL.Engine.Cache.Fingerprints
{
    /// <summary>
    /// A Bond IOutputStream which writes to a <see cref="InliningWriter"/> using the associated string table to deduplicate strings
    /// </summary>
    internal readonly struct InliningWriterDedupOutputStream : IOutputStream
    {
        private readonly InliningWriter m_writer;

        public InliningWriterDedupOutputStream(InliningWriter writer)
        {
            m_writer = writer;
        }

        public long Position
        {
            get { return m_writer.BaseStream.Position; }
            set { m_writer.BaseStream.Position = value; }
        }

        public void WriteBytes(ArraySegment<byte> data)
        {
            if (data.Array == null)
            {
                Contract.Assert(data.Count == 0);
                m_writer.Write(false);
            }
            else
            {
                m_writer.Write(true);
                m_writer.Write(data.Array, data.Offset, data.Count);
            }
        }

        public void WriteDouble(double value) => m_writer.Write(value);

        public void WriteFloat(float value) => m_writer.Write(value);

        public void WriteString(Encoding encoding, string value, int size)
        {
            var stringId = value == null ? StringId.Invalid : StringId.Create(m_writer.PathTable.StringTable, value);
            m_writer.Write(stringId);
        }

        public void WriteUInt16(ushort value) => m_writer.Write(value);

        public void WriteUInt32(uint value) => m_writer.Write(value);

        public void WriteUInt64(ulong value) => m_writer.Write(value);

        public void WriteUInt8(byte value) => m_writer.Write(value);

        public void WriteVarUInt16(ushort value) => m_writer.WriteCompact((int)value);

        public void WriteVarUInt32(uint value) => m_writer.WriteCompact((uint)value);

        public void WriteVarUInt64(ulong value) => m_writer.WriteCompact(unchecked((long)value));
    }
}
