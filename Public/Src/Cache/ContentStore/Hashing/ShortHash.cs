// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Runtime.CompilerServices;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    /// An abridged representation of a <see cref="ContentHash"/>.
    /// Format:
    /// byte[0]: HashType
    /// byte[1-11]: ContentHash[0-10]
    /// </summary>
    public readonly struct ShortHash : IEquatable<ShortHash>, IComparable<ShortHash>
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
        public ShortHash(ContentHash hash) : this(ToOrdinal(hash)) { }

        /// <nodoc />
        public ShortHash(FixedBytes bytes) => Value = ReadOnlyFixedBytes.FromFixedBytes(ref bytes);

        /// <nodoc />
        public ShortHash(ReadOnlyFixedBytes bytes) => Value = bytes;

        /// <nodoc />
        public ReadOnlyFixedBytes Value { get; }

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
        public override bool Equals(object obj)
        {
            return obj is ShortHash hash && Equals(hash);
        }

        /// <inheritdoc />
        public override int GetHashCode() => Value.GetHashCode();

        /// <nodoc />
        public static bool operator ==(ShortHash left, ShortHash right) => left.Equals(right);

        /// <nodoc />
        public static bool operator !=(ShortHash left, ShortHash right) => !left.Equals(right);

        /// <nodoc />
        public static implicit operator ShortHash(ContentHash hash) => new ShortHash(hash);

        /// <nodoc />
        public byte[] ToByteArray()
        {
            return Value.ToByteArray(SerializedLength);
        }

        private static FixedBytes ToOrdinal(ContentHash hash)
        {
            var hashBytes = hash.ToFixedBytes();
            var result = new FixedBytes();

            unchecked
            {
                result[0] = (byte)hash.HashType;
            }
            
            for (int i = 0; i < HashLength; i++)
            {
                result[i + 1] = hashBytes[i];
            }

            return result;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return $"{HashType.Serialize()}{ContentHash.SerializedDelimiter.ToString()}{Value.ToHex(1, HashLength)}";
        }

        /// <inheritdoc />
        public int CompareTo(ShortHash other)
        {
            return Value.CompareTo(other.Value);
        }
    }
}
