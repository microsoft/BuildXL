// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using BuildXL.Cache.ContentStore.UtilitiesCore;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    /// An abridged representation of a <see cref="ContentHash"/>.
    /// Format:
    /// byte[0]: HashType
    /// byte[1-11]: ContentHash[0-10]
    /// </summary>
    public readonly struct ShortHash : IEquatable<ShortHash>, IComparable<ShortHash>, IToStringConvertible
    {
        /// <summary>
        /// The length in bytes of a short hash. NOTE: This DOES include the byte for the hash type
        /// </summary>
        public const int SerializedLength = 12;

        /// <summary>
        /// The length in bytes of the hash portion of a short hash. NOTE: This does NOT include the byte for the hash type
        /// </summary>
        public const int HashLength = SerializedLength - 1;

        /// <nodoc />
        public ShortHash(ContentHash hash) : this(ToShortReadOnlyBytes(hash)) { }

        /// <nodoc />
        public ShortHash(ReadOnlyFixedBytes bytes) => Value = new ShortReadOnlyFixedBytes(ref bytes);

        /// <nodoc />
        public ShortHash(ShortReadOnlyFixedBytes bytes) => Value = bytes;

        /// <nodoc />
        public ShortReadOnlyFixedBytes Value { get; }

        /// <nodoc />
        public byte this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Value[index];
        }

        /// <nodoc />
        public HashType HashType => (HashType)Value[0];

        /// <inheritdoc />
        public bool Equals(ShortHash other) => Value == other.Value;

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return obj is ShortHash hash && Equals(hash);
        }

        /// <inheritdoc />
        public override int GetHashCode() => Value.GetHashCode();

        /// <summary>
        /// Attempts to create a <see cref="ShortHash"/> instance from a given string.
        /// </summary>
        public static bool TryParse(string str, out ShortHash result)
        {
            var longHashAsString = str.PadRight(ContentHash.SerializedLength * 2 + 3, '0');
            if (ContentHash.TryParse(longHashAsString, out var longHash))
            {
                result = longHash.AsShortHash();
                return true;
            }

            result = default;
            return false;
        }

        /// <nodoc />
        public static ShortHash FromBytes(byte[] data)
        {
            return new ShortHash(new ShortReadOnlyFixedBytes(data));
        }

        /// <nodoc />
        public static bool operator ==(ShortHash left, ShortHash right) => left.Equals(right);

        /// <nodoc />
        public static bool operator !=(ShortHash left, ShortHash right) => !left.Equals(right);

        /// <nodoc />
        public static bool operator <(ShortHash left, ShortHash right) => left.CompareTo(right) < 0;

        /// <nodoc />
        public static bool operator >(ShortHash left, ShortHash right) => left.CompareTo(right) > 0;

        /// <nodoc />
        public static implicit operator ShortHash(ContentHash hash) => new ShortHash(hash);

        /// <summary>
        /// Gets the byte array representation of the short hash.
        /// Consider using ContentHashExtensions.ToPooledByteArray instead to avoid extra allocations.
        /// </summary>
        public byte[] ToByteArray()
        {
            return Value.ToByteArray(SerializedLength);
        }

        /// <summary>
        /// Serialize to a buffer.
        /// </summary>
        public void Serialize(byte[] buffer)
        {
            Value.Serialize(buffer, SerializedLength);
        }

        /// <summary>
        /// Serialize whole value to a binary writer.
        /// </summary>
        public void Serialize(BinaryWriter writer)
        {
            using var handle = ContentHashExtensions.ShortHashBytesArrayPool.Get();
            Value.Serialize(writer, handle.Value);
        }

        private static unsafe ShortReadOnlyFixedBytes ToShortReadOnlyBytes(ContentHash hash)
        {
            var result = new ShortReadOnlyFixedBytes();

            // Bypassing the readonliness of the struct.
            // Can't use 'MemoryMarshal.AsBytes' here, because that method is not available for 472.
            // It is safe to do so, because the 'result' variable resides on stack and can't be moved by GC.

            byte* ptr = (byte*)&result._bytes;
            //hash.Serialize(new Span<byte>(ptr, SerializedLength), offset: 0, length: HashLength);
            hash.Serialize(new Span<byte>(ptr, SerializedLength), offset: 0, length: HashLength);

            return result;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return ToString(HashLength);
        }

        /// <summary>
        /// Gets string representation of the short hash with a given length.
        /// </summary>
        public string ToString(int hashLength)
        {
            Contract.Check(hashLength <= HashLength)?.Requires($"hashLength should be <= HashLength. hashLength={hashLength}, HashLength={HashLength}");
            return $"{HashType.Serialize()}{ContentHash.SerializedDelimiter.ToString()}{Value.ToHex(1, hashLength)}";
        }

        /// <nodoc />
        public void ToString(StringBuilder sb)
        {
            sb.Append(HashType.Serialize())
                .Append(ContentHash.SerializedDelimiter.ToString());
            Value.ToHex(sb, 1, HashLength);
        }

        /// <inheritdoc />
        public int CompareTo(ShortHash other)
        {
            return Value.CompareTo(other.Value);
        }
    }
}
