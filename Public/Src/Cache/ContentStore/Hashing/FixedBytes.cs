// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.ContentStore.UtilitiesCore;

#pragma warning disable 649 // Field is never assigned

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    ///     Storage for up to 32 bytes with common behavior.
    /// </summary>
    public unsafe struct FixedBytes : IEquatable<FixedBytes>, IComparable<FixedBytes>
    {
        /// <summary>
        ///     Maximum number of bytes that can be stored.
        /// </summary>
        public const int MaxLength = 33;

        /// <summary>
        ///     Maximum number of hex characters in the string form.
        /// </summary>
        public const int MaxHexLength = MaxLength * 2;

        internal static readonly char[] NybbleHex =
        {'0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'A', 'B', 'C', 'D', 'E', 'F'};

        private fixed byte _bytes[MaxLength];

        /// <nodoc />
        public FixedBytes(in ReadOnlyFixedBytes bytes)
        {
            fixed (byte* d = _bytes)
            {
                for (var i = 0; i < MaxLength; i++)
                {
                    d[i] = bytes[i];
                }
            }
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="FixedBytes"/> struct.
        /// </summary>
        public FixedBytes(string serialized)
        {
            Contract.Requires(serialized != null);
            Contract.Requires(serialized.Length > 0);

            int length;
            if (!TryParse(serialized, out this, out length))
            {
                throw new ArgumentException($"{serialized} is not a recognized hex string");
            }

            if (length > MaxLength)
            {
                throw new ArgumentException($"{serialized} is too long");
            }
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="FixedBytes"/> struct.
        /// </summary>
        public FixedBytes(byte[] buffer, int length = MaxLength, int offset = 0)
        {
            var len = Math.Min(length, Math.Min(buffer.Length, MaxLength));
            var j = offset;

            fixed (byte* d = _bytes)
            {
                for (var i = 0; i < len; i++)
                {
                    d[i] = buffer[j++];
                }
            }
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="FixedBytes"/> struct.
        /// </summary>
        public FixedBytes(BinaryReader reader, int length = MaxLength)
        {
            var buffer = reader.ReadBytes(length);
            var len = Math.Min(length, Math.Min(buffer.Length, MaxLength));

            fixed (byte* p = _bytes)
            {
                for (var i = 0; i < len; i++)
                {
                    p[i] = buffer[i];
                }
            }
        }

        /// <summary>
        ///     Return byte at zero-based position.
        /// </summary>
        public byte this[int index]
        {
            get
            {
                Contract.Requires(index >= 0);
                Contract.Requires(index < MaxLength);

                fixed (byte* p = _bytes)
                {
                    return p[index];
                }
            }

            set
            {
                Contract.Requires(index >= 0);
                Contract.Requires(index < MaxLength);

                fixed (byte* p = _bytes)
                {
                    p[index] = value;
                }
            }
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
        public bool Equals(FixedBytes other)
        {
            byte* o = other._bytes;
            fixed (byte* p = _bytes)
            {
                for (var i = 0; i < MaxLength; i++)
                {
                    if (p[i] != o[i])
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            // ReSharper disable once NonReadonlyMemberInGetHashCode
            fixed (byte* p = _bytes)
            {
                return p[0] | p[1] << 8 | p[2] << 16 | p[3] << 24;
            }
        }

        /// <inheritdoc />
        public int CompareTo(FixedBytes other)
        {
            byte* o = other._bytes;
            fixed (byte* p = _bytes)
            {
                for (var i = 0; i < MaxLength; i++)
                {
                    var compare = p[i].CompareTo(o[i]);
                    if (compare != 0)
                    {
                        return compare;
                    }
                }

                return 0;
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
        public string ToHex(int length = MaxLength)
        {
            Contract.Requires(length >= 0);
            Contract.Requires(length <= MaxLength);

            char* buffer = stackalloc char[(2 * length) + 1];
            var j = 0;

            fixed (byte* p = _bytes)
            {
                for (var i = 0; i < length; i++)
                {
                    buffer[j++] = NybbleHex[(p[i] & 0xF0) >> 4];
                    buffer[j++] = NybbleHex[p[i] & 0x0F];
                }
            }

            Contract.Assert(j == (2 * length));
            buffer[j] = '\0';

            return new string(buffer);
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
            var i = offset;

            fixed (byte* s = _bytes)
            {
                for (var j = 0; j < len; j++)
                {
                    buffer[i++] = s[j];
                }
            }
        }

        /// <summary>
        ///     Serialize whole value to a binary writer.
        /// </summary>
        public void Serialize(BinaryWriter writer, int length = MaxLength)
        {
            var buffer = new byte[length];

            fixed (byte* p = _bytes, d = buffer)
            {
                for (var i = 0; i < length; i++)
                {
                    d[i] = p[i];
                }
            }

            writer.Write(buffer);
        }

        /// <nodoc />
        public static bool operator ==(FixedBytes left, FixedBytes right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(FixedBytes left, FixedBytes right)
        {
            return !left.Equals(right);
        }

        /// <nodoc />
        public static bool operator <(FixedBytes left, FixedBytes right)
        {
            return left.CompareTo(right) < 0;
        }

        /// <nodoc />
        public static bool operator >(FixedBytes left, FixedBytes right)
        {
            return left.CompareTo(right) > 0;
        }

        /// <summary>
        ///     Create a random value.
        /// </summary>
        public static FixedBytes Random(int length = MaxLength)
        {
            Contract.Requires(length >= 0);
            Contract.Requires(length <= MaxLength);

            var bytes = ThreadSafeRandom.GetBytes(length);
            return new FixedBytes(bytes, length);
        }

        /// <summary>
        ///     Attempt to parse value from a string.
        /// </summary>
        public static bool TryParse(string serialized, out FixedBytes value, out int length)
        {
            Contract.Requires(serialized != null);

            try
            {
                // TODO: Assimilate to avoid allocation (bug 1365340)
                var bytes = HexUtilities.HexToBytes(serialized);
                value = new FixedBytes(bytes);
                length = bytes.Length;
                return true;
            }
            catch (ArgumentException)
            {
            }

            value = default(FixedBytes);
            length = 0;
            return false;
        }
    }
}
