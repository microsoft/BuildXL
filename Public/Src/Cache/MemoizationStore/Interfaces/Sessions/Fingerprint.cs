// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;

namespace BuildXL.Cache.MemoizationStore.Interfaces.Sessions
{
    /// <summary>
    ///     A binary fingerprint with 33 bytes storage.
    /// </summary>
    public readonly struct Fingerprint : IEquatable<Fingerprint>, IComparable<Fingerprint>
    {
        /// <summary>
        ///     Maximum number of bytes that can be stored.
        /// </summary>
        public const int MaxLength = ReadOnlyFixedBytes.MaxLength;

        /// <summary>
        ///     Maximum number of hex characters in the string form.
        /// </summary>
        public const int MaxHexLength = ReadOnlyFixedBytes.MaxHexLength;

        private readonly byte _length;
        private readonly ReadOnlyFixedBytes _bytes;

        /// <summary>
        ///     Initializes a new instance of the <see cref="Fingerprint"/> struct.
        /// </summary>
        public Fingerprint(FixedBytes fixedBytes, int length)
        {
            _bytes = ReadOnlyFixedBytes.FromFixedBytes(ref fixedBytes);

            unchecked {
                _length = (byte)length;
            }
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="Fingerprint"/> struct.
        /// </summary>
        public Fingerprint(ReadOnlyFixedBytes fixedBytes, int length)
        {
            _bytes = fixedBytes;

            unchecked {
                _length = (byte)length;
            }
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="Fingerprint"/> struct.
        /// </summary>
        public Fingerprint(string serialized)
        {
            Contract.Requires(serialized != null);

            if (!TryParse(serialized, out this))
            {
                throw new ArgumentException($"{serialized} is not a recognized fingerprint");
            }
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="Fingerprint"/> struct.
        /// </summary>
        public Fingerprint(byte[] buffer, int length = MaxLength, int offset = 0)
        {
            Contract.Requires(buffer != null);

            var len = Math.Min(buffer.Length, length);
            _bytes = new ReadOnlyFixedBytes(buffer, len, offset);
            
            unchecked {
                _length = (byte)len;
            }
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="Fingerprint"/> struct.
        /// </summary>
        public Fingerprint(int length, BinaryReader reader)
        {
            unchecked {
                _length = (byte)length;
            }
            
            _bytes = ReadOnlyFixedBytes.ReadFrom(reader, length);
        }

        /// <summary>
        ///     Gets the byte length.
        /// </summary>
        public int Length => _length;

        /// <summary>
        ///     Return the byte at zero-based position.
        /// </summary>
        public byte this[int index] => _bytes[index];

        /// <summary>
        ///     Gets the value packed in a byte array.
        /// </summary>
        public byte[] ToByteArray()
        {
            Contract.Requires(Length > 0);

            return _bytes.ToByteArray(Length);
        }

        /// <summary>
        ///     Gets the value packed in a FixedBytes.
        /// </summary>
        public FixedBytes ToFixedBytes()
        {
            Contract.Requires(Length > 0);

            return new FixedBytes(_bytes);
        }

        /// <inheritdoc />
        public bool Equals(Fingerprint other)
        {
            Contract.Assert(Length > 0);
            Contract.Assert(other.Length > 0);

            return _bytes.Equals(other._bytes);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            Contract.Assert(Length > 0);

            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            Contract.Assert(Length > 0);

            return _bytes.GetHashCode();
        }

        /// <inheritdoc />
        public int CompareTo(Fingerprint other)
        {
            Contract.Assert(Length > 0);
            Contract.Assert(other.Length > 0);

            return _bytes.CompareTo(other._bytes);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return ToHex();
        }

        /// <summary>
        ///     Give the bytes as a hex string.
        /// </summary>
        public string ToHex()
        {
            Contract.Requires(Length > 0);

            return _bytes.ToHex(_length);
        }

        /// <summary>
        ///     Serialize to a string.
        /// </summary>
        public string Serialize()
        {
            return ToHex();
        }

        /// <summary>
        ///     Serialize whole value to a buffer.
        /// </summary>
        public void Serialize(byte[] buffer, int offset = 0)
        {
            Contract.Requires(buffer != null);
            Contract.Requires(Length > 0);

            _bytes.Serialize(buffer, _length, offset);
        }

        /// <summary>
        ///     Serialize whole value to a binary writer.
        /// </summary>
        public void Serialize(BinaryWriter writer)
        {
            Contract.Requires(writer != null);
            Contract.Requires(Length > 0);

            writer.Write(_length);
            _bytes.Serialize(writer, _length);
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="Fingerprint"/> struct.
        /// </summary>
        public static Fingerprint Deserialize(BinaryReader reader)
        {
            Contract.Requires(reader != null);
            var length = reader.ReadByte();
            var buffer = ReadOnlyFixedBytes.ReadFrom(reader, length);
            return new Fingerprint(buffer, length);
        }

        /// <summary>
        ///     Serialize just the bytes to a binary writer.
        /// </summary>
        public void SerializeBytes(BinaryWriter writer)
        {
            Contract.Requires(writer != null);
            Contract.Requires(Length > 0);

            _bytes.Serialize(writer, _length);
        }

        /// <nodoc />
        public static bool operator ==(Fingerprint left, Fingerprint right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(Fingerprint left, Fingerprint right)
        {
            return !left.Equals(right);
        }

        /// <nodoc />
        public static bool operator <(Fingerprint left, Fingerprint right)
        {
            return left.CompareTo(right) < 0;
        }

        /// <nodoc />
        public static bool operator >(Fingerprint left, Fingerprint right)
        {
            return left.CompareTo(right) > 0;
        }

        /// <summary>
        ///     Create a random value.
        /// </summary>
        public static Fingerprint Random(int length = MaxLength)
        {
            return new Fingerprint(ReadOnlyFixedBytes.Random(length), length);
        }

        /// <summary>
        ///     Try to parse the serialized form.
        /// </summary>
        public static bool TryParse(string serialized, out Fingerprint value)
        {
            if (!ReadOnlyFixedBytes.TryParse(serialized, out var v, out var length))
            {
                value = default(Fingerprint);
                return false;
            }

            if (length == 0 || length > MaxLength)
            {
                value = default(Fingerprint);
                return false;
            }

            value = new Fingerprint(v, length);
            return true;
        }
    }
}
