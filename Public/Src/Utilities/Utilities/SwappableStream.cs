// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using System.IO;

namespace BuildXL.Utilities
{
    /// <nodoc />
    public sealed class SwappableStream : Stream
    {
        /// <nodoc />
        public override bool CanRead => m_stream.CanRead;

        /// <nodoc />
        public override bool CanSeek => m_stream.CanSeek;

        /// <nodoc />
        public override bool CanWrite => m_stream.CanWrite;

        /// <nodoc />
        public override long Length => m_stream.Length;

        /// <nodoc />
        public override long Position { get => m_stream.Position; set => m_stream.Position = value; }

        private Stream m_stream;

        /// <nodoc />
        public SwappableStream(Stream stream)
        {
            Contract.RequiresNotNull(stream);
            m_stream = stream;
        }

        /// <nodoc />
        public void Swap(Stream stream)
        {
            Contract.RequiresNotNull(stream);
            m_stream = stream;
        }

        /// <nodoc />
        public override void Flush()
        {
            m_stream.Flush();
        }

        /// <nodoc />
        public override int ReadByte()
        {
            return m_stream.ReadByte();
        }

        /// <nodoc />
        public override void Close()
        {
            m_stream.Close();
        }

        /// <nodoc />
        public override int Read(byte[] buffer, int offset, int count)
        {
            return m_stream.Read(buffer, offset, count);
        }

        /// <nodoc />
        public override long Seek(long offset, SeekOrigin origin)
        {
            return m_stream.Seek(offset, origin);
        }

        /// <nodoc />
        public override void SetLength(long value)
        {
            m_stream.SetLength(value);
        }

        /// <nodoc />
        public override void Write(byte[] buffer, int offset, int count)
        {
            m_stream.Write(buffer, offset, count);
        }
    }
}
