// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading.Tasks;

namespace BuildXL.Utilities.Collections
{
    /// <summary>
    /// Helper functions related to reading from and writing to streams.
    /// </summary>
    public static class StreamUtilities
    {
        /// <summary>
        /// Reads an array of 32-bit integers
        /// </summary>
        public static int[] ReadInt32Array(this BinaryReader reader, ref byte[]? buffer, int count)
        {
            Contract.RequiresNotNull(reader);
            Contract.Requires(count >= 0);
            Array.Resize(ref buffer, count * sizeof(int));

            int offset = 0;
            int left = count * sizeof(int);
            while (left > 0)
            {
                int readCount = reader.Read(buffer, offset, left);
                if (readCount == 0)
                {
                    throw new IOException("Unexpected end of stream");
                }

                offset += readCount;
                left -= readCount;
            }

            var int32Array = new int[count];
            Buffer.BlockCopy(buffer, 0, int32Array, 0, count * sizeof(int));
            return int32Array;
        }

        /// <summary>
        /// Reads an into a range of the given buffer.
        /// NOTE: Unlike <see cref="BinaryReader.Read(byte[], int, int)"/> this ensures entirety of range is read.
        /// </summary>
        public static void ReadRequiredRange(this BinaryReader reader, byte[] buffer, int offset, int count)
        {
            var remaining = count;
            while (remaining > 0)
            {
                int readCount = reader.Read(buffer, offset, remaining);
                if (readCount == 0)
                {
                    throw new IOException("Unexpected end of stream");
                }

                offset += readCount;
                remaining -= readCount;
            }
        }

        /// <summary>
        /// Reads an into a range of the given buffer.
        /// NOTE: Unlike <see cref="Stream.Read(byte[], int, int)"/> this ensures entirety of range is read.
        /// </summary>
        public static void ReadRequiredRange(this Stream stream, byte[] buffer, int offset, int count)
        {
            var remaining = count;
            while (remaining > 0)
            {
                int readCount = stream.Read(buffer, offset, remaining);
                if (readCount == 0)
                {
                    throw new IOException("Unexpected end of stream");
                }

                offset += readCount;
                remaining -= readCount;
            }
        }

        /// <summary>
        /// Reads an into a range of the given buffer.
        /// NOTE: Unlike <see cref="Stream.ReadAsync(byte[], int, int)"/> this ensures entirety of range is read.
        /// </summary>
        public static async Task ReadRequiredRangeAsync(this Stream stream, byte[] buffer, int offset, int count)
        {
            var remaining = count;
            while (remaining > 0)
            {
                int readCount = await stream.ReadAsync(buffer, offset, remaining);
                if (readCount == 0)
                {
                    throw new IOException("Unexpected end of stream");
                }

                offset += readCount;
                remaining -= readCount;
            }
        }

        /// <summary>
        /// Reads the entire <paramref name="count"/> into <paramref name="buffer"/> if available.
        /// NOTE: Unlike <see cref="BinaryReader.Read(byte[], int, int)"/> this method continues reading from the underlying stream
        /// until it reaches the end of it or until the entire requested <paramref name="count"/> is read.
        /// </summary>
        public static int TryReadAll(this BinaryReader reader, byte[] buffer, int offset, int count)
        {
            int total = 0;
            var remaining = count;
            while (remaining > 0)
            {
                // Reading from the underlying stream directly to avoid stack overflow.
                int readCount = reader.BaseStream.Read(buffer, offset, remaining);
                total += readCount;
                if (readCount == 0)
                {
                    break;
                }

                offset += readCount;
                remaining -= readCount;
            }

            return total;
        }

        /// <summary>
        /// Reads the entire <paramref name="count"/> into <paramref name="buffer"/> if available.
        /// NOTE: Unlike <see cref="BinaryReader.Read(byte[], int, int)"/> this method continues reading from the underlying stream
        /// until it reaches the end of it or until the entire requested <paramref name="count"/> is read.
        /// </summary>
        public static async Task<int> ReadAllAsync(this Stream stream, byte[] buffer, int offset, int count)
        {
            int total = 0;
            var remaining = count;
            while (remaining > 0)
            {
                int readCount = await stream.ReadAsync(buffer, offset, remaining);
                total += readCount;
                if (readCount == 0)
                {
                    break;
                }

                offset += readCount;
                remaining -= readCount;
            }

            return total;
        }
    }
}
