// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Buffers.Binary;

#nullable enable

namespace BuildXL.Utilities.Serialization
{
    /// <summary>
    /// A set of extension methods for <see cref="SpanReader"/>.
    /// </summary>
    /// <remarks>
    /// This class mimics the API available via <see cref="System.IO.BinaryReader"/> and <see cref="BuildXLReader"/> but not for deserializing
    /// entities from a <see cref="ReadOnlySpan{T}"/> instead of a stream.
    /// </remarks>
    public static class SpanReaderExtensions
    {
        /// <nodoc />
        public static SpanReader AsReader(this ReadOnlySpan<byte> source) => new SpanReader(source);

        /// <nodoc />
        public static SpanReader AsReader(this Span<byte> source) => new SpanReader(source);

        /// <nodoc />
        public static bool ReadBoolean(this ref SpanReader source) => source.ReadByte() != 0;

        /// <nodoc />
        public static int ReadInt32(this ref SpanReader source)
        {
            source.EnsureLength(sizeof(int));
            return BinaryPrimitives.ReadInt32LittleEndian(source.ReadSpan(sizeof(int)));
        }

        /// <nodoc />
        public static long ReadInt64(this ref SpanReader source)
        {
            source.EnsureLength(sizeof(long));
            // ReadSpan moves the source forward.
            return BinaryPrimitives.ReadInt64LittleEndian(source.ReadSpan(sizeof(long)));
        }

        /// <summary>
        /// Reads <see cref="uint"/>.
        /// </summary>
        public static uint ReadUInt32Compact(this ref SpanReader source)
        {
            var value = source.Read7BitEncodedInt();
            return unchecked((uint)value);
        }

        /// <nodoc />
        public static int ReadInt32Compact(ref this SpanReader reader)
        {
            return reader.Read7BitEncodedInt();
        }

        /// <nodoc />
        public static long ReadInt64Compact(this ref SpanReader source)
        {
            return source.Read7BitEncodedLong();
        }

        /// <nodoc />
        public static TimeSpan ReadTimeSpan(this ref SpanReader source) =>
            TimeSpan.FromTicks(source.Read7BitEncodedLong());

        /// <nodoc />
        public static DateTime ReadDateTime(this ref SpanReader source) =>
            DateTime.FromBinary(source.ReadInt64());

        internal static int Read7BitEncodedInt(this ref SpanReader source)
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
                byteReadJustNow = source.ReadByte();
                result |= (byteReadJustNow & 0x7Fu) << shift;

                if (byteReadJustNow <= 0x7Fu)
                {
                    return (int)result; // early exit
                }
            }

            // Read the 5th byte. Since we already read 28 bits,
            // the value of this byte must fit within 4 bits (32 - 28),
            // and it must not have the high bit set.

            byteReadJustNow = source.ReadByte();
            if (byteReadJustNow > 0b_1111u)
            {
                // throw new FormatException(SR.Format_Bad7BitInt);
                throw new FormatException();
            }

            result |= (uint)byteReadJustNow << MaxBytesWithoutOverflow * 7;
            return unchecked((int)result);
        }

        /// <summary>
        /// The method returns a byte array from <paramref name="source"/>, please note, that the length of the final array might be smaller than the given <paramref name="length"/>.
        /// </summary>
        public static byte[] ReadBytes(this ref SpanReader source, int length)
        {
            // This implementation mimics the one from BinaryReader that allows
            // getting back an array of a smaller size than requested.
            return source.ReadSpan(length).ToArray();
        }

        /// <nodoc />
        internal static long Read7BitEncodedLong(this ref SpanReader source)
        {
            // Read out an Int64 7 bits at a time.  The high bit
            // of the byte when on means to continue reading more bytes.
            long count = 0;
            var shift = 0;
            byte b;
            do
            {
                // ReadByte handles end of stream cases for us.
                b = source.ReadByte();
                long m = b & 0x7f;
                count |= m << shift;
                shift += 7;
            }
            while ((b & 0x80) != 0);
            return count;
        }

        /// <nodoc />
        public delegate T ReadArrayFromSpan<out T>(ref SpanReader source);

        /// <nodoc />
        public static T[] ReadArray<T>(ref this SpanReader source, ReadArrayFromSpan<T> reader, int minimumLength = 0)
        {
            var length = source.ReadInt32Compact();
            if (length == 0)
            {
                return Array.Empty<T>();
            }

            var array = source.ReadArrayCore(reader, length, minimumLength: minimumLength);

            return array;
        }

        private static T[] ReadArrayCore<T>(ref this SpanReader source, ReadArrayFromSpan<T> reader, int length, int minimumLength = 0)
        {
            var arrayLength = Math.Max(minimumLength, length);
            var array = arrayLength == 0 ? Array.Empty<T>() : new T[arrayLength];
            for (var i = 0; i < length; i++)
            {
                array[i] = reader(ref source);
            }

            return array;
        }
    }
}
