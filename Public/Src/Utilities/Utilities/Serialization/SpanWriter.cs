// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Buffers;
using System.Diagnostics.ContractsLight;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

#nullable enable

namespace BuildXL.Utilities.Serialization
{
    /// <summary>
    /// A lightweight wrapper for writing data into <see cref="Span{T}"/>.
    /// </summary>
    /// <remarks>
    /// The main purpose of this type is serializing instances directly to spans.
    /// Because its a struct, the serialization methods should get the instance by ref in order for the caller methods to "observe" the position
    /// changes that happen during serialization.
    /// The instance might be created with <see cref="ArrayBufferWriter{T}"/> to allow writing into an expandable buffers.
    /// </remarks>
    public ref struct SpanWriter
    {
        /// <summary>
        /// An optional expandable buffer writer the current writer writes to.
        /// </summary>
        private readonly ArrayBufferWriter<byte>? m_bufferWriter;

        // Re-computing the remaining length instead of computing it on the fly, because
        // we access 'RemainingLength' property on a hot path and its cheaper to update
        // two fields once the position has changed instead of re-computing the property all the time.
        private int m_remainingLength;

        private int m_position;

        /// <summary>
        /// The original span.
        /// </summary>
        internal Span<byte> Span { get; }

        /// <summary>
        /// The current position inside the span. A valid range is [0..Span.Length].
        /// </summary>
        public int Position
        {
            readonly get => m_position;
            set
            {
                m_position = value;
                m_remainingLength = Span.Length - m_position;
            }
        }

        /// <nodoc />
        public bool IsEnd => Span.Length == Position;

        /// <summary>
        /// Returns a remaining length available to the writer.
        /// </summary>
        public int RemainingLength => m_remainingLength;

        /// <summary>
        /// Gets the remaining data in the original span.
        /// </summary>
        public Span<byte> Remaining => Span.Slice(Position);

        /// <summary>
        /// Gets the written data in the original span.
        /// </summary>
        public Span<byte> WrittenBytes => Span.Slice(0, Position);

        /// <nodoc />
        public SpanWriter(ArrayBufferWriter<byte> bufferWriter, int defaultSizeHint = 4 * 1024)
        {
            m_bufferWriter = bufferWriter;

            Span = bufferWriter.GetSpan(defaultSizeHint);
            m_position = 0;
            m_remainingLength = Span.Length;
        }
        
        /// <nodoc />
        public SpanWriter(Span<byte> span)
        {
            Span = span;
            m_remainingLength = Span.Length;
            m_position = 0;
            m_bufferWriter = null;
        }

        /// <nodoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteByte(byte b)
        {
            EnsureLength(sizeof(byte));
            Span[Position++] = b;
        }
        
        /// <nodoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteShort(ushort value)
        {
            unchecked
            {
                EnsureLength(sizeof(ushort));
                Span[Position] = (byte)value;
                Span[Position + 1] = (byte)(value >> 8);

                Position += 2;
            }
        }

        /// <summary>
        /// Advances the current position by <paramref name="length"/>.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// The operation will fail if <code>Position + length >= Span.Length;</code>.
        /// </exception>
        public void Advance(int length)
        {
            EnsureLength(length);
            Position += length;
        }

        /// <nodoc />
        public void WriteSpan(ReadOnlySpan<byte> source)
        {
            EnsureLength(source.Length);
            source.CopyTo(Span.Slice(Position, source.Length));
            Position += source.Length;
        }

        internal void EnsureLength(int minLength)
        {
            if (RemainingLength < minLength)
            {
                // Extracting the core logic to make this method more suitable for inlining.
                DoEnsureLength(minLength);
            }
        }

        private void DoEnsureLength(int minLength)
        {
            if (m_bufferWriter != null)
            {
                // The 'Advance' method can't be used here because it just moves the cursor in the IBufferWriter
                // and won't re-allocate an underlying data store.
                // To do that we have to specify a bigger size when calling 'ArrayBufferWriter{T}.GetSpan()'.
                // So in case when we don't have enough space, we just re-creating a span writer
                // with the size hint enough to keep the required data.

                var other = new SpanWriter(m_bufferWriter, (Position + minLength) * 2);
                other.WriteSpan(WrittenBytes);
                this = other;
            }
            else
            {
                InsufficientLengthException.Throw(minLength, RemainingLength);
            }
        }

        /// <nodoc />
        public static implicit operator SpanWriter(Span<byte> span)
        {
            return new SpanWriter(span);
        }
    }
}