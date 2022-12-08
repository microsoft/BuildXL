// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

#nullable enable

namespace BuildXL.Utilities.Serialization
{
    /// <summary>
    /// A set of extension methods for <see cref="SpanReader"/>.
    /// </summary>
    /// <remarks>
    /// This class mimics the API available via <see cref="System.IO.BinaryReader"/>,
    ///  <see cref="System.IO.BinaryWriter"/>, <see cref="BuildXLReader"/>, and <see cref="BuildXLWriter"/>  but for (de)serializing
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
        public static void WriteInt32Compact(this ref SpanWriter writer, int value)
        {
            writer.WriteUInt64Compact(unchecked((uint)value));
        }

        /// <nodoc />
        public static void WriteUInt32Compact(this ref SpanWriter writer, uint value)
        {
            writer.WriteUInt64Compact(unchecked((ulong)value));
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

        /// <nodoc />
        public static void WriteInt64Compact(this ref SpanWriter writer, long value)
        {
            writer.WriteUInt64Compact(unchecked((ulong)value));
        }
        
        /// <nodoc />
        public static void WriteCompact(this ref SpanWriter writer, long value)
        {
            writer.WriteUInt64Compact(unchecked((ulong)value));
        }

        /// <nodoc />
        public static void WriteUInt64Compact(this ref SpanWriter writer, ulong value)
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
                result |= (byteReadJustNow & 0x7Fu) << shift;

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
        public static ReadOnlySpan<T> Read<T>(this ref SpanReader reader, int count)
            where T : unmanaged
        {
            // Reading a span instead of reading bytes to avoid unnecessary allocations.
            var itemSpan = reader.ReadSpan(Unsafe.SizeOf<T>() * count);
            var result = MemoryMarshal.Cast<byte, T>(itemSpan);
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
        /// Writes an array
        /// </summary>
        public static void Write<T>(this ref SpanWriter writer, T[] value, WriteItemToSpan<T> write)
        {
            WriteReadOnlyListCore(ref writer, value, write);
        }

        private static void WriteReadOnlyListCore<T, TReadOnlyList>(this ref SpanWriter writer, TReadOnlyList value, WriteItemToSpan<T> write)
            where TReadOnlyList : IReadOnlyList<T>
        {
            writer.WriteInt32Compact(value.Count);
            for (int i = 0; i < value.Count; i++)
            {
                write(ref writer, value[i]);
            }
        }
    }
}
