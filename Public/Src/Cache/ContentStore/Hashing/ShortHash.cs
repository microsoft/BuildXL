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

        /// <summary>
        /// The length in hex characters of the hash portion of a short hash. NOTE: This does NOT include characters for the hash type
        /// </summary>
        public const int HashStringLength = HashLength * 2;

        /// <nodoc />
        public readonly ShortReadOnlyFixedBytes Value;

        /// <nodoc />
        public ShortHash(ContentHash hash) : this(ToShortReadOnlyBytes(hash)) { }

        /// <nodoc />
        public ShortHash(ShortReadOnlyFixedBytes bytes) => Value = bytes;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ShortHash" /> struct from string
        /// </summary>
        public ShortHash(string serialized)
        {
            Contract.Requires(serialized != null);

            if (!TryParse(serialized, out this))
            {
                throw new ArgumentException($"{serialized} is not a recognized content hash");
            }
        }

        /// <nodoc />
        public byte this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Value[index + 1];
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
            if (ContentHash.TryParse(str, out var longHash, isShortHash: true))
            {
                result = longHash.AsShortHash();
                return true;
            }

            result = default;
            return false;
        }

        /// <nodoc />
        public static ShortHash FromSpan(ReadOnlySpan<byte> data)
        {
            return new ShortHash(ShortReadOnlyFixedBytes.FromSpan(data));
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
            Value.Serialize(writer);
        }

        private static unsafe ShortReadOnlyFixedBytes ToShortReadOnlyBytes(ContentHash hash)
        {
            Span<ShortReadOnlyFixedBytes> result = stackalloc ShortReadOnlyFixedBytes[1];
            hash.Serialize(MemoryMarshal.AsBytes(result), offset: 0, length: HashLength);
            return result[0];
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
            Contract.Requires(hashLength <= HashLength, $"hashLength should be <= HashLength. hashLength={hashLength}, HashLength={HashLength}");
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
