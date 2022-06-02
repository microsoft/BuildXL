// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Runtime.CompilerServices;

#nullable enable

namespace BuildXL.Utilities.Serialization
{
    /// <summary>
    /// A lightweight wrapper around <see cref="ReadOnlySpan{T}"/> that tracks its position.
    /// </summary>
    /// <remarks>
    /// The main purposes of this type is to use it for deserializing instance directly from spans.
    /// Because its a struct, the deserialization methods should get the instance of it by ref in order for the caller methods to "observe" the position
    /// changed that happens during deserialization.
    /// </remarks>
    public ref struct SpanReader
    {
        /// <summary>
        /// The original span.
        /// </summary>
        internal ReadOnlySpan<byte> Span { get; }

        /// <summary>
        /// The current position inside the span. A valid range is [0..Span.Length-1].
        /// </summary>
        internal int Position { get; private set; }

        /// <nodoc />
        public bool IsEnd => Span.Length == Position;

        /// <summary>
        /// Returns a remaining length available by the reader.
        /// </summary>
        public int RemainingLength => Span.Length - Position;

        /// <summary>
        /// Gets the remaining data in the original span.
        /// </summary>
        public ReadOnlySpan<byte> Remaining => Span.Slice(Position);

        /// <nodoc />
        public SpanReader(ReadOnlySpan<byte> span)
        {
            Span = span;
            Position = 0;
        }

        /// <nodoc />
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public byte ReadByte()
        {
            EnsureLength(sizeof(byte));
            return Span[Position++];
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

        /// <summary>
        /// The method returns a byte array from <seealso cref="Span"/>.
        /// Please note, that the length of the final array might be smaller than the given <paramref name="length"/>.
        /// </summary>
        public ReadOnlySpan<byte> ReadSpan(int length)
        {
            // This implementation mimics the one from BinaryReader that allows
            // getting back an array of a smaller size than requested.
            length = Math.Min(RemainingLength, length);
            var result = Span.Slice(Position, length);
            Position += length;
            return result;
        }
        
        internal void EnsureLength(int minLength)
        {
            if (RemainingLength < minLength)
            {
                throw new ArgumentException(
                    $"The reader should have at least {minLength} length but has {RemainingLength}.");
            }
        }
    }

}