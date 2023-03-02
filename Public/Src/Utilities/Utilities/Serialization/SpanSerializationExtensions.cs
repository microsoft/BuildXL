// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using BuildXL.Utilities.Core;

#nullable enable

namespace BuildXL.Utilities.Serialization
{
    /// <summary>
    /// A set of extension methods for <see cref="SpanReader"/>.
    /// </summary>
    /// <remarks>
    /// This class mimics the API available via <see cref="System.IO.BinaryReader"/>,
    ///  <see cref="System.IO.BinaryWriter"/>, <see cref="BuildXL.Utilities.Core.BuildXLReader"/>, and <see cref="BuildXL.Utilities.Core.BuildXLWriter"/>  but for (de)serializing
    /// entities from a <see cref="ReadOnlySpan{T}"/> and <see cref="Span{T}"/> instead of a stream.
    /// </remarks>
    public static class SpanSerializationExtensions
    {
        /// <nodoc />
        public static SpanReader AsReader(this ReadOnlySpan<byte> reader) => new SpanReader(reader);

        /// <nodoc />
        public static SpanReader AsReader(this Span<byte> reader) => new SpanReader(reader);
        
        /// <nodoc />
        public static SpanWriter AsWriter(this Span<byte> reader) => new SpanWriter(reader);

        /// <nodoc />
        public static bool ReadBoolean(this ref SpanReader reader) => reader.ReadByte() != 0;

        /// <nodoc />
        public static int ReadInt32(this ref SpanReader reader)
        {
            return BinaryPrimitives.ReadInt32LittleEndian(reader.ReadSpan(sizeof(int)));
        }

        /// <nodoc />
        public static long ReadInt64(this ref SpanReader reader)
        {
            return BinaryPrimitives.ReadInt64LittleEndian(reader.ReadSpan(sizeof(long)));
        }

        /// <summary>
        /// Reads <see cref="uint"/>.
        /// </summary>
        public static uint ReadUInt32Compact(this ref SpanReader reader)
        {
            var value = reader.Read7BitEncodedInt();
            return unchecked((uint)value);
        }

        /// <nodoc />
        public static void WriteCompact(this ref SpanWriter writer, uint value)
        {
            writer.Write7BitEncodedInt(unchecked((int)value));
        }

        /// <nodoc />
        public static int ReadInt32Compact(this ref SpanReader reader)
        {
            return reader.Read7BitEncodedInt();
        }

        /// <nodoc />
        public static ushort ReadUInt16(this ref SpanReader reader) 
            => BinaryPrimitives.ReadUInt16LittleEndian(reader.ReadSpan(sizeof(ushort)));

        /// <nodoc />
        public static long ReadInt64Compact(this ref SpanReader reader)
        {
            return reader.Read7BitEncodedLong();
        }

        /// <summary>
        /// Compactly writes an int
        /// </summary>
        public static void WriteCompact(this ref SpanWriter writer, int value)
        {
            writer.Write7BitEncodedInt(value);
        }

        /// <nodoc />
        public static void WriteCompact(this ref SpanWriter writer, long value)
        {
            writer.WriteCompact(unchecked((ulong)value));
        }

        /// <nodoc />
        public static void Write7BitEncodedInt(this ref SpanWriter writer, int value)
        {
            uint uValue = unchecked((uint)value);

            // Write out an int 7 bits at a time. The high bit of the byte,
            // when on, tells reader to continue reading more bytes.
            //
            // Using the constants 0x7F and ~0x7F below offers smaller
            // codegen than using the constant 0x80.

            while (uValue > 0x7Fu)
            {
                writer.Write(unchecked((byte)(uValue | ~0x7Fu)));
                uValue >>= 7;
            }

            writer.Write((byte)uValue);
        }

        /// <nodoc />
        public static void WriteCompact(this ref SpanWriter writer, ulong value)
        {
            int writeCount = 1;
            unchecked
            {
                // Write out a long 7 bits at a time.  The high bit of the byte,
                // when on, tells reader to continue reading more bytes.
                while (value >= 0x80)
                {
                    writeCount++;
                    writer.Write((byte)(value | 0x80));
                    value >>= 7;
                }

                writer.Write((byte)value);
            }
        }

        /// <nodoc />
        public static TimeSpan ReadTimeSpan(this ref SpanReader reader) =>
            TimeSpan.FromTicks(reader.Read7BitEncodedLong());

        /// <nodoc />
        public static DateTime ReadDateTime(this ref SpanReader reader) =>
            DateTime.FromBinary(reader.ReadInt64());

        internal static int Read7BitEncodedInt(this ref SpanReader reader)
        {
            // Unlike writing, we can't delegate to the 64-bit read on
            // 64-bit platforms. The reason for this is that we want to
            // stop consuming bytes if we encounter an integer overflow.

            uint result = 0;
            byte byteReadJustNow;

            // Read the integer 7 bits at a time. The high bit
            // of the byte when on means to continue reading more bytes.
            //
            // There are two failure cases: we've read more than 5 bytes,
            // or the fifth byte is about to cause integer overflow.
            // This means that we can read the first 4 bytes without
            // worrying about integer overflow.

            const int MaxBytesWithoutOverflow = 4;
            for (var shift = 0; shift < MaxBytesWithoutOverflow * 7; shift += 7)
            {
                // ReadByte handles end of stream cases for us.
                byteReadJustNow = reader.ReadByte();
                unchecked
                {
                    result |= (byteReadJustNow & 0x7Fu) << shift;
                }

                if (byteReadJustNow <= 0x7Fu)
                {
                    return (int)result; // early exit
                }
            }

            // Read the 5th byte. Since we already read 28 bits,
            // the value of this byte must fit within 4 bits (32 - 28),
            // and it must not have the high bit set.

            byteReadJustNow = reader.ReadByte();
            if (byteReadJustNow > 0b_1111u)
            {
                // throw new FormatException(SR.Format_Bad7BitInt);
                throw new FormatException();
            }

            result |= (uint)byteReadJustNow << MaxBytesWithoutOverflow * 7;
            return unchecked((int)result);
        }

        /// <summary>
        /// The method returns a byte array from <paramref name="reader"/>, please note, that the length 
        /// of the final array might be smaller than the given <paramref name="length"/> if <paramref name="allowIncomplete"/>
        /// is true (false by default).
        /// </summary>
        public static byte[] ReadBytes(this ref SpanReader reader, int length, bool allowIncomplete = false)
        {
            // This implementation's behavior when incomplete = true
            // mimics BinaryReader.ReadBytes which allows
            // returning an array less than the size requested.
            return reader.ReadSpan(length, allowIncomplete).ToArray();
        }

        /// <nodoc />
        internal static long Read7BitEncodedLong(this ref SpanReader reader)
        {
            // Read out an Int64 7 bits at a time.  The high bit
            // of the byte when on means to continue reading more bytes.
            long count = 0;
            var shift = 0;
            byte b;
            do
            {
                // ReadByte handles end of stream cases for us.
                b = reader.ReadByte();
                long m = b & 0x7f;
                count |= m << shift;
                shift += 7;
            }
            while ((b & 0x80) != 0);
            return count;
        }

        /// <nodoc />
        public delegate T ReadItemFromSpan<out T>(ref SpanReader reader);

        /// <nodoc />
        public delegate void WriteItemToSpan<in T>(ref SpanWriter writer, T item);

        /// <nodoc />
        public static T[] ReadArray<T>(this ref SpanReader reader, ReadItemFromSpan<T> itemReader, int minimumLength = 0)
        {
            var length = reader.ReadInt32Compact();
            if (length == 0)
            {
                return Array.Empty<T>();
            }

            var array = reader.ReadArrayCore(itemReader, length, minimumLength: minimumLength);

            return array;
        }

        private static T[] ReadArrayCore<T>(this ref SpanReader reader, ReadItemFromSpan<T> itemReader, int length, int minimumLength = 0)
        {
            var arrayLength = Math.Max(minimumLength, length);
            var array = arrayLength == 0 ? Array.Empty<T>() : new T[arrayLength];
            for (var i = 0; i < length; i++)
            {
                array[i] = itemReader(ref reader);
            }

            return array;
        }

        /// <nodoc />
        public static T Read<T>(this ref SpanReader reader)
            where T : unmanaged
        {
            var itemSpan = reader.ReadSpan(Unsafe.SizeOf<T>());
            var result = MemoryMarshal.Read<T>(itemSpan);
            return result;
        }

        /// <nodoc />
        public static void Write(this ref SpanWriter writer, byte value) => writer.WriteByte(value); // Using a special overload instead of using a generic method for performance reasons
        
        /// <nodoc />
        public static void Write(this ref SpanWriter writer, ushort value) => writer.WriteShort(value); // Using a special overload instead of using a generic method for performance reasons

        /// <nodoc />
        public static void Write<T>(this ref SpanWriter writer, T value)
            where T : unmanaged
        {
#if NET5_0_OR_GREATER
            // This version only works in .NET Core, because CreateReadOnlySpan is not available for full framework.
            var bytes = MemoryMarshal.CreateReadOnlySpan(
                reference: ref Unsafe.As<T, byte>(ref Unsafe.AsRef(in value)),
                length: Unsafe.SizeOf<T>());
            writer.WriteSpan(bytes);
#else
            // For the full framework case (that is not used in production on a hot paths)
            // using an array pool with 1 element to avoid stackalloc approach that
            // causes issues because writer.WriteSpan can be re-created on the fly with a new instance from the array writer.
            using var pooledHandle = ConversionArrayPool.Array<T>.ArrayPool.GetInstance(1);
            var input = pooledHandle.Instance;
            input[0] = value;

            var bytes = MemoryMarshal.AsBytes(input.AsSpan());
            writer.WriteSpan(bytes);
#endif
        }

        /// <nodoc />
        public static void Write<T>(this ref SpanWriter writer, Span<T> span)
            where T : unmanaged
        {
            var bytes = MemoryMarshal.AsBytes(span);
            writer.WriteSpan(bytes);
        }
        
        /// <nodoc />
        public static void Write(this ref SpanWriter writer, Span<byte> bytes)
        {
            writer.WriteSpan(bytes);
        }

        /// <summary>
        /// Writes an array.
        /// </summary>
        public static void Write<T>(this ref SpanWriter writer, T[] value, WriteItemToSpan<T> write)
        {
            WriteReadOnlyListCore(ref writer, value, write);
        }
        
        /// <summary>
        /// Writes a readonly list.
        /// </summary>
        public static void Write<T>(this ref SpanWriter writer, IReadOnlyList<T> value, WriteItemToSpan<T> write)
        {
            WriteReadOnlyListCore(ref writer, value, write);
        }

        private static void WriteReadOnlyListCore<T, TReadOnlyList>(this ref SpanWriter writer, TReadOnlyList value, WriteItemToSpan<T> write)
            where TReadOnlyList : IReadOnlyList<T>
        {
            writer.WriteCompact(value.Count);
            for (int i = 0; i < value.Count; i++)
            {
                write(ref writer, value[i]);
            }
        }

        /// <summary>
        /// Reads a string from <paramref name="reader"/>.
        /// </summary>
        public static string ReadString(this ref SpanReader reader, Encoding? encoding = null)
        {
            // Adopted from BinaryReader.ReadString.
            const int MaxCharBytesSize = 128;
            encoding ??= Encoding.UTF8;
            var maxCharsSize = encoding.GetMaxCharCount(MaxCharBytesSize);
            var decoder = encoding.GetDecoder();

            // Length of the string in bytes, not chars
            int stringLength = reader.Read7BitEncodedInt();
            if (stringLength < 0)
            {
                throw new InvalidOperationException($"The string length is invalid {stringLength}");
            }

            if (stringLength == 0)
            {
                return string.Empty;
            }

            // pooled buffers
            using var pooledCharBuffer = Pools.GetCharArray(maxCharsSize);
            var charBuffer = pooledCharBuffer.Instance;

            StringBuilder? sb = null;
            int currPos = 0;
            do
            {
                int readLength = ((stringLength - currPos) > MaxCharBytesSize) ? MaxCharBytesSize : (stringLength - currPos);

                var span = reader.ReadSpan(readLength, allowIncomplete: true);
                int n = span.Length;
                if (n == 0)
                {
                    throw new InvalidOleVariantTypeException("Unexpected end of binary stream.");
                }

#if NET5_0_OR_GREATER
                int charsRead = decoder.GetChars(span, charBuffer.AsSpan(), flush: false);
#else
                var charBytes = span.ToArray();
                int charsRead = decoder.GetChars(charBytes, 0, n, charBuffer, 0);
#endif
                if (currPos == 0 && span.Length == stringLength)
                {
                    return new string(charBuffer, 0, charsRead);
                }

                // Since we could be reading from an untrusted data source, limit the initial size of the
                // StringBuilder instance we're about to get or create. It'll expand automatically as needed.
                sb ??= StringBuilderCache.Acquire(Math.Min(stringLength, StringBuilderCache.MaxBuilderSize)); // Actual string length in chars may be smaller.
                sb.Append(charBuffer, 0, charsRead);
                currPos += n;
            } while (currPos < stringLength);

            // In this case we can return the buffer back, but not in the case when we read the whole string.
            return StringBuilderCache.GetStringAndRelease(sb);
        }

        private const int MaxArrayPoolRentalSize = 64 * 1024; // try to keep rentals to a reasonable size

        /// <summary>
        /// Writes a given <paramref name="value"/> into <paramref name="writer"/>.
        /// </summary>
        public static void Write(this ref SpanWriter writer, string value, Encoding? encoding = null)
        {
            encoding ??= Encoding.UTF8;

            // Adopted from BinaryWriter.Write(string value)
#if NET5_0_OR_GREATER
            if (value.Length <= 127 / 3)
            {
                // Max expansion: each char -> 3 bytes, so 127 bytes max of data, +1 for length prefix
                Span<byte> buffer = stackalloc byte[128];
                int actualByteCount = encoding.GetBytes(value, buffer.Slice(1));
                buffer[0] = (byte)actualByteCount; // bypass call to Write7BitEncodedInt
                var slice = buffer.Slice(0, actualByteCount + 1 /* length prefix */);
                writer.WriteSpan(slice);
                return;
            }

            if (value.Length <= MaxArrayPoolRentalSize / 3)
            {
                using var wrapper = Pools.GetByteArray(value.Length * 3); // max expansion: each char -> 3 bytes
                var rented = wrapper.Instance;
                int actualByteCount = encoding.GetBytes(value, rented);
                writer.Write7BitEncodedInt(actualByteCount);
                writer.Write(rented.AsSpan(0, actualByteCount));
                return;
            }

            // Slow path
            writer.Write7BitEncodedInt(encoding.GetByteCount(value));
            WriteCharsCommonWithoutLengthPrefix(ref writer, value, encoding);
#else
            // Fairly naive version for the full framework.
            var bytes = encoding.GetBytes(value);
            writer.Write7BitEncodedInt(bytes.Length);
            writer.Write(bytes);
#endif
        }
#if NET5_0_OR_GREATER
        private static void WriteCharsCommonWithoutLengthPrefix(this ref SpanWriter writer, ReadOnlySpan<char> chars, Encoding encoding)
        {
            // If our input is truly enormous, the call to GetMaxByteCount might overflow,
            // which we want to avoid. Theoretically, any Encoding could expand from chars -> bytes
            // at an enormous ratio and cause us problems anyway given small inputs, but this is so
            // unrealistic that we needn't worry about it.

            byte[] rented;

            if (chars.Length <= MaxArrayPoolRentalSize)
            {
                // GetByteCount may walk the buffer contents, resulting in 2 passes over the data.
                // We prefer GetMaxByteCount because it's a constant-time operation.

                int maxByteCount = encoding.GetMaxByteCount(chars.Length);
                if (maxByteCount <= MaxArrayPoolRentalSize)
                {
                    using var rentedHandlerInner = Pools.GetByteArray(maxByteCount);
                    rented = rentedHandlerInner.Instance;
                    int actualByteCount = encoding.GetBytes(chars, rented);
                    WriteToOutStream(ref writer, rented, 0, actualByteCount);
                    return;
                }
            }

            // We're dealing with an enormous amount of data, so acquire an Encoder.
            // It should be rare that callers pass sufficiently large inputs to hit
            // this code path, and the cost of the operation is dominated by the transcoding
            // step anyway, so it's ok for us to take the allocation here.

            using var rentedHandler = Pools.GetByteArray(MaxArrayPoolRentalSize);
            rented = rentedHandler.Instance;
            Encoder encoder = encoding.GetEncoder();
            bool completed;

            do
            {
                encoder.Convert(chars, rented, flush: true, charsUsed: out int charsConsumed, bytesUsed: out int bytesWritten, completed: out completed);
                if (bytesWritten != 0)
                {
                    WriteToOutStream(ref writer, rented, 0, bytesWritten);
                }

                chars = chars.Slice(charsConsumed);
            } while (!completed);
        }

        private static void WriteToOutStream(ref SpanWriter writer, byte[] buffer, int offset, int count)
        {
            writer.Write(buffer.AsSpan(offset, count));
        }
#endif
    }
}
