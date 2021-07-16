// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;

namespace BuildXL.Utilities
{
    /// <summary>
    /// A read-only stream implemented over <see cref="ReadOnlyMemory{T}"/>
    /// </summary>
    public sealed class ReadOnlyMemoryStream : Stream
    {
        /// <inheritdoc />
        public override bool CanRead { get; } = true;

        /// <inheritdoc />
        public override bool CanSeek { get; } = false;

        /// <inheritdoc />
        public override bool CanWrite { get; } = false;

        /// <inheritdoc />
        public override long Length => m_memory.Length;

        /// <inheritdoc />
        public override long Position { get => m_position; set => throw new NotImplementedException(); }

        private int m_position;
        private ReadOnlyMemory<byte> m_memory;

        /// <nodoc />
        public ReadOnlyMemoryStream()
            : this(ReadOnlyMemory<byte>.Empty)
        {
        }

        /// <nodoc />
        public ReadOnlyMemoryStream(ReadOnlyMemory<byte> memory)
        {
            m_memory = memory;
        }

        /// <summary>
        /// Exchange the underlying memory stream.
        /// </summary>
        public void Swap(ReadOnlyMemory<byte> memory)
        {
            m_position = 0;
            m_memory = memory;
        }

        /// <inheritdoc />
        public override void Flush()
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }

        /// <inheritdoc />
        public override int ReadByte()
        {
            if (m_position < m_memory.Length)
            {
                return m_memory.Span[m_position++];
            }

            return -1;
        }

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count)
        {
            var span = buffer.AsSpan(offset, count);
            var copySize = Math.Min(span.Length, m_memory.Length - m_position);
            m_memory.Slice(m_position, copySize).Span.CopyTo(span);
            m_position += copySize;
            return copySize;
        }
    }
}
