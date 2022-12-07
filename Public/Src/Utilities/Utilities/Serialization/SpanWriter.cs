// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Runtime.CompilerServices;

#nullable enable

namespace BuildXL.Utilities.Serialization
{
    /// <summary>
    /// A lightweight wrapper around <see cref="Span{T}"/> that tracks its position.
    /// </summary>
    /// <remarks>
    /// The main purpose of this type is serializing instances directly to spans.
    /// Because its a struct, the serialization methods should get the instance by ref in order for the caller methods to "observe" the position
    /// changes that happen during serialization.
    /// </remarks>
    public ref struct SpanWriter
    {
        /// <summary>
        /// The original span.
        /// </summary>
        internal Span<byte> Span { get; }

        /// <summary>
        /// The current position inside the span. A valid range is [0..Span.Length].
        /// </summary>
        public int Position { get; set; }

        /// <nodoc />
        public bool IsEnd => Span.Length == Position;

        /// <summary>
        /// Returns a remaining length available to the writer.
        /// </summary>
        public int RemainingLength => Span.Length - Position;

        /// <summary>
        /// Gets the remaining data in the original span.
        /// </summary>
        public Span<byte> Remaining => Span.Slice(Position);

        /// <summary>
        /// Gets the written data in the original span.
        /// </summary>
        public Span<byte> WrittenBytes => Span.Slice(0, Position);

        /// <nodoc />
        public SpanWriter(Span<byte> span)
        {
            Span = span;
            Position = 0;
        }

        /// <nodoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteByte(byte b)
        {
            EnsureLength(sizeof(byte));
            Span[Position++] = b;
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void EnsureLength(int minLength)
        {
            if (RemainingLength < minLength)
            {
                ThrowArgumentException(minLength);
            }
        }

        private void ThrowArgumentException(int minLength)
        {
            throw new ArgumentException(
                $"The reader should have at least {minLength} length but has {RemainingLength}.");
        }

        /// <nodoc />
        public static implicit operator SpanWriter(Span<byte> span)
        {
            return new SpanWriter(span);
        }
    }
}