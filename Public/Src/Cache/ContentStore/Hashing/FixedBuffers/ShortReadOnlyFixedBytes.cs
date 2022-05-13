// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using BuildXL.Cache.ContentStore.Interfaces.Utils;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    ///     Readonly storage for up to 12 bytes with common behavior.
    /// </summary>
    public readonly unsafe struct ShortReadOnlyFixedBytes : IEquatable<ShortReadOnlyFixedBytes>, IComparable<ShortReadOnlyFixedBytes>
    {
        /// <summary>
        ///     Maximum number of bytes that can be stored.
        /// </summary>
        public const int MaxLength = ShortHash.SerializedLength;

        // Using the same approach to implement read-only version we use in 'ReadOnlyFixedBytes'.
        [StructLayout(LayoutKind.Sequential, Size = MaxLength)]
        internal readonly struct FixedBuffer
        {
            public readonly byte FixedElementField;
        }

        internal readonly FixedBuffer _bytes;

        private static readonly char[] NybbleHex = HexUtilities.NybbleToHex;

        /// <nodoc />
        public ShortReadOnlyFixedBytes(byte[] buffer, int length = MaxLength, int offset = 0)
        {
            var len = Math.Min(length, Math.Min(buffer.Length, MaxLength));
            this = FromSpan(buffer.AsSpan(start: offset, len));
        }

        /// <nodoc />
        public ShortReadOnlyFixedBytes(ReadOnlySpan<byte> source)
        {
            // Unfortunately, we can not expect that the length is less then the MaxLength, because many existing clients do not respect it.
            var len = Math.Min(source.Length, MaxLength);

            fixed (byte* d = &_bytes.FixedElementField)
            {
                var span = new Span<byte>(d, len);
                source.Slice(0, len).CopyTo(span);
            }
        }

        /// <summary>
        /// Creates a new instance of <see cref="ShortReadOnlyFixedBytes"/> from a sequence of bytes.
        /// </summary>
        public static ShortReadOnlyFixedBytes FromSpan(ReadOnlySpan<byte> source)
        {
            if (source.Length >= MaxLength)
            {
                // We can only re-interpret cast if the incoming length is greater (or equal) than the length of the output.
                return MemoryMarshal.Read<ShortReadOnlyFixedBytes>(source);
            }

            return new ShortReadOnlyFixedBytes(source);
        }

        /// <summary>
        ///     Returns the length of the fixed bytes.
        /// </summary>
        public int Length => MaxLength;

        /// <summary>
        ///     Return byte at zero-based position.
        /// </summary>
        public byte this[int index]
        {
            get
            {
                fixed (byte* p = &_bytes.FixedElementField)
                {
                    return p[index];
                }
            }
        }
        
        internal static ReadOnlySpan<byte> AsSpan(byte* data, int length)
        {
            return new ReadOnlySpan<byte>(data, length);
        }

        /// <summary>
        ///     Gets get the hash value packed in a byte array.
        /// </summary>
        public byte[] ToByteArray(int length = MaxLength)
        {
            Contract.Requires(length >= 0);
            Contract.Requires(length <= MaxLength);

            var buffer = new byte[length];
            Serialize(buffer, length);
            return buffer;
        }

        /// <inheritdoc />
        public bool Equals(ShortReadOnlyFixedBytes other)
        {
            // other is fixed (lives on the stack), so we can safely use 'other.AsSpan'.
            var o = &other._bytes.FixedElementField;
            fixed (byte* p = &_bytes.FixedElementField)
            {
                return AsSpan(p, length: MaxLength).SequenceEqual(AsSpan(o, length: MaxLength));
            }
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            // It is enough to use the first 4 bytes (excluding byte 0 which is hash type) for the hash code.
            fixed (byte* p = &_bytes.FixedElementField)
            {
                // It is enough to use the first 4 bytes (excluding byte 0 which is hash type) for the hash code.
                return p[1] | p[2] << 8 | p[3] << 16 | p[4] << 24;
            }
        }

        /// <inheritdoc />
        public int CompareTo(ShortReadOnlyFixedBytes other)
        {
            var o = &other._bytes.FixedElementField;
            fixed (byte* p = &_bytes.FixedElementField)
            {
                return AsSpan(p, length: MaxLength).SequenceCompareTo(AsSpan(o, length: MaxLength));
            }
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return ToHex();
        }

        /// <summary>
        ///     Give the bytes as a hex string.
        /// </summary>
        public string ToHex(int length = MaxLength) => ToHex(0, length);

        /// <summary>
        ///     Give the bytes as a hex string.
        /// </summary>
        public string ToHex(int offset, int length)
        {
            Contract.Requires(length >= 0);
            Contract.Requires(length + offset <= MaxLength);

            char* buffer = stackalloc char[(2 * length) + 1];
            FillBuffer(buffer, offset, length);

            return new string(buffer);
        }

        private void FillBuffer(char* buffer, int offset, int length)
        {
            var j = 0;

            fixed (byte* p = &_bytes.FixedElementField)
            {
                for (var i = offset; i < length + offset; i++)
                {
                    buffer[j++] = NybbleHex[(p[i] & 0xF0) >> 4];
                    buffer[j++] = NybbleHex[p[i] & 0x0F];
                }
            }

            Contract.Assert(j == 2 * length);
            buffer[j] = '\0';
        }

        /// <summary>
        ///     Appends the bytes as a hex into a given <paramref name="builder"/>.
        /// </summary>
        public void ToHex(StringBuilder builder, int offset, int length)
        {
            Contract.Requires(length >= 0);
            Contract.Requires(length + offset <= MaxLength);

            int bufferLength = (2 * length) + 1;
            char* buffer = stackalloc char[bufferLength];
            FillBuffer(buffer, offset, length);

            // FillBuffer writes a trailing '\0'. But for this case the last character is not needed.
            builder.AppendCharStar(bufferLength - 1, buffer);
        }

        /// <summary>
        ///     Serialize to a string.
        /// </summary>
        public string Serialize()
        {
            return ToHex();
        }

        /// <summary>
        ///     Serialize value to a buffer.
        /// </summary>
        public void Serialize(byte[] buffer, int length = MaxLength, int offset = 0)
        {
            var len = Math.Min(length, Math.Min(buffer.Length, MaxLength));

            fixed (byte* s = &_bytes.FixedElementField)
            {
                Marshal.Copy((IntPtr)s, buffer, offset, len);
            }
        }

        /// <summary>
        ///     Serialize whole value to a binary writer.
        /// </summary>
        public void Serialize(BinaryWriter writer, byte[] buffer, int length = MaxLength)
        {
            fixed (byte* p = &_bytes.FixedElementField)
            {
                AsSpan(p, length).CopyTo(buffer);
            }

            writer.Write(buffer);
        }

        /// <summary>
        ///     Serialize whole value to a binary writer.
        /// </summary>
        public void Serialize(BinaryWriter writer)
        {
#if NET_COREAPP
            // BinaryWriter.Write(Span) only available on .NET Core
            fixed (byte* p = &_bytes.FixedElementField)
            {
                var span = AsSpan(p, MaxLength);
                writer.Write(span);
            }
#else
            using var handle = ContentHashExtensions.ShortHashBytesArrayPool.Get();
            Serialize(writer, handle.Value);
#endif
        }

        /// <nodoc />
        public static bool operator ==(ShortReadOnlyFixedBytes left, ShortReadOnlyFixedBytes right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(ShortReadOnlyFixedBytes left, ShortReadOnlyFixedBytes right)
        {
            return !left.Equals(right);
        }

        /// <nodoc />
        public static bool operator <(ShortReadOnlyFixedBytes left, ShortReadOnlyFixedBytes right)
        {
            return left.CompareTo(right) < 0;
        }

        /// <nodoc />
        public static bool operator >(ShortReadOnlyFixedBytes left, ShortReadOnlyFixedBytes right)
        {
            return left.CompareTo(right) > 0;
        }
    }
}
