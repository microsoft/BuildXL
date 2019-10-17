// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.ContentStore.UtilitiesCore;

namespace BuildXL.Cache.ContentStore.Hashing
{
    /// <summary>
    ///     Readonly storage for up to 32 bytes with common behavior.
    /// </summary>
    public readonly unsafe struct ReadOnlyFixedBytes : IEquatable<ReadOnlyFixedBytes>, IComparable<ReadOnlyFixedBytes>
    {
        // This struct relies on a hole in the C# type system.
        // The C# compiler does not allow to define readonly 'fixed size buffers':
        //
        // error CS0106: The modifier 'readonly' is not valid for this item
        // private readonly fixed byte _bytes[MaxLength];
        //
        // But this is unfortunate, because it prevents us from making this struct and all the structs that embeds it using 'readonly' modifier.
        // Readonly structs are good in both in terms of design and performance and using large readonly structs could give non-negligible performance gains.
        //
        // The FixedBuffer struct declared below, basically mimics what the compiler does under the hood for 'fixed size buffers'.
        // But in this case the compiler does not emit an error even though the current struct is not fully readonly.

        /// <summary>
        ///     Maximum number of bytes that can be stored.
        /// </summary>
        public const int MaxLength = FixedBytes.MaxLength;

        [StructLayout(LayoutKind.Sequential, Size = MaxLength)]
        private readonly struct FixedBuffer
        {
            public readonly byte FixedElementField;
        }

        private readonly FixedBuffer _bytes;

        /// <summary>
        ///     Maximum number of hex characters in the string form.
        /// </summary>
        public const int MaxHexLength = FixedBytes.MaxHexLength;

        private static readonly char[] NybbleHex = FixedBytes.NybbleHex;

        /// <nodoc />
        private ReadOnlyFixedBytes(ref FixedBytes fixedBytes)
        {
            fixed (byte* d = &_bytes.FixedElementField)
            {
                for (int i = 0; i < MaxLength; i++)
                {
                    d[i] = fixedBytes[i];
                }
            }
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ReadOnlyFixedBytes"/> struct.
        /// </summary>
        public ReadOnlyFixedBytes(byte[] buffer, int length = MaxLength, int offset = 0)
        {
            Contract.Requires(buffer != null);
            // Unfortunately, we can not expect that the length is less then the MaxLength, because many existing clients do not respect it.
            var len = Math.Min(length, Math.Min(buffer.Length, MaxLength));

            fixed (byte* d = &_bytes.FixedElementField)
            {
                Marshal.Copy(buffer, offset, (IntPtr)d, len);
            }
        }

        /// <summary>
        ///     Creates a new instance of the <see cref="ReadOnlyFixedBytes"/> by reading the <paramref name="length"/> bytes from the <paramref name="reader"/>.
        /// </summary>
        /// <remarks>
        ///     The final struct will contain at most <paramref name="length"/> bytes, but it is possible to have less bytes if the reader does not have enough data.
        /// </remarks>
        public static ReadOnlyFixedBytes ReadFrom(BinaryReader reader, int length = MaxLength)
        {
            return new ReadOnlyFixedBytes(reader.ReadBytes(length), length, offset: 0);
        }

        /// <summary>
        ///     Converts the string representation of a buffer in a hex form to a blob.
        /// </summary>
        public static ReadOnlyFixedBytes Parse(string serialized)
        {
            Contract.Requires(serialized != null);
            Contract.Requires(serialized.Length > 0);

            if (!TryParse(serialized, out var result, out var length))
            {
                throw new ArgumentException($"{serialized} is not a recognized hex string");
            }

            if (length > MaxLength)
            {
                throw new ArgumentException($"{serialized} is too long");
            }

            return result;
        }

        /// <summary>
        ///     Attempt to parse value from a string.
        /// </summary>
        public static bool TryParse(string serialized, out ReadOnlyFixedBytes result, out int length)
        {
            Contract.Requires(serialized != null);

            byte[] bytes;

            try
            {
                bytes = HexUtilities.HexToBytes(serialized);
            }
            catch (ArgumentException)
            {
                result = default(ReadOnlyFixedBytes);
                length = 0;
                return false;
            }

            length = bytes.Length;
            result = new ReadOnlyFixedBytes(bytes, bytes.Length, offset: 0);
            
            return true;
        }

        /// <summary>
        ///     Creates a new instance of the <see cref="ReadOnlyFixedBytes"/> from the <paramref name="fixedBytes"/>.
        /// </summary>
        public static ReadOnlyFixedBytes FromFixedBytes(ref FixedBytes fixedBytes)
        {
            return new ReadOnlyFixedBytes(ref fixedBytes);
        }

        /// <summary>
        ///     Create a random value.
        /// </summary>
        public static ReadOnlyFixedBytes Random(int length = MaxLength)
        {
            Contract.Requires(length >= 0);
            Contract.Requires(length <= MaxLength);

            var bytes = ThreadSafeRandom.GetBytes(length);
            return new ReadOnlyFixedBytes(bytes, length);
        }

        /// <summary>
        ///     Returns the lengths of the fixed bytes.
        /// </summary>
        public int Length => MaxLength;

        /// <summary>
        ///     Return byte at zero-based position.
        /// </summary>
        public byte this[int index]
        {
            get
            {
                Contract.Requires(index >= 0 && index < MaxLength);

                fixed (byte* p = &_bytes.FixedElementField)
                {
                    return p[index];
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
        public bool Equals(ReadOnlyFixedBytes other)
        {
            // 'o' is implicitely fixed.
            byte* o = &other._bytes.FixedElementField;
            fixed (byte* p = &_bytes.FixedElementField)
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
            fixed (byte* p = &_bytes.FixedElementField)
            {
                // It is enough to use the first 4 bytes for the hash code.
                return p[0] | p[1] << 8 | p[2] << 16 | p[3] << 24;
            }
        }

        /// <inheritdoc />
        public int CompareTo(ReadOnlyFixedBytes other)
        {
            byte* o = &other._bytes.FixedElementField;
            fixed (byte* p = &_bytes.FixedElementField)
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
                for (var i = offset; i < length; i++)
                {
                    buffer[j++] = NybbleHex[(p[i] & 0xF0) >> 4];
                    buffer[j++] = NybbleHex[p[i] & 0x0F];
                }
            }

            Contract.Assert(j == (2 * (length - offset)));
            buffer[j] = '\0';
        }

        /// <summary>
        ///     Appends the bytes as a hex into a given <paramref name="builder"/>.
        /// </summary>
        public void ToHex(StringBuilder builder, int offset, int length)
        {
            Contract.Requires(length >= 0);
            Contract.Requires(length + offset <= MaxLength);

            char* buffer = stackalloc char[(2 * length) + 1];
            FillBuffer(buffer, offset, length);

            builder.AppendCharStar(length, buffer);
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
        public void Serialize(BinaryWriter writer, int length = MaxLength)
        {
            // Consider pooling the buffer
            var buffer = new byte[length];
            
            fixed (byte* p = &_bytes.FixedElementField)
            {
                for (var i = 0; i < length; i++)
                {
                    buffer[i] = p[i];
                }
            }

            writer.Write(buffer);
        }

        /// <nodoc />
        public static bool operator ==(ReadOnlyFixedBytes left, ReadOnlyFixedBytes right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(ReadOnlyFixedBytes left, ReadOnlyFixedBytes right)
        {
            return !left.Equals(right);
        }

        /// <nodoc />
        public static bool operator <(ReadOnlyFixedBytes left, ReadOnlyFixedBytes right)
        {
            return left.CompareTo(right) < 0;
        }

        /// <nodoc />
        public static bool operator >(ReadOnlyFixedBytes left, ReadOnlyFixedBytes right)
        {
            return left.CompareTo(right) > 0;
        }
    }
}
