// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Globalization;

namespace BuildXL.Utilities.Core
{
    /// <summary>
    /// Thrown by <see cref="StringTable"/> when the table is structurally full and no further strings can be
    /// interned. The <see cref="StringTable"/> layout is bounded by the 32-bit <see cref="StringId"/> encoding
    /// (11-bit buffer number + 21-bit offset), so once every byte buffer, every overflow buffer, and the large
    /// string buffer are full, the table cannot grow further. There is no recovery path; callers should treat
    /// this as fatal.
    /// </summary>
    /// <remarks>
    /// This is a dedicated exception type (rather than a generic <c>ContractException</c>) so callers can catch
    /// it precisely without masking unrelated assertion failures, and so diagnostic handlers can read the
    /// structural counters from properties instead of parsing the message string.
    /// </remarks>
    public sealed class StringTableExhaustedException : Exception
    {
        /// <summary>Total number of strings interned in the table at the time of failure.</summary>
        public int StringCount { get; }

        /// <summary>Approximate total bytes occupied by interned strings at the time of failure.</summary>
        public long SizeInBytes { get; }

        /// <summary>Number of regular byte-buffer slots the table can hold (structural cap).</summary>
        public int ByteBuffersCount { get; }

        /// <summary>Number of regular byte buffers actually allocated at the time of failure.</summary>
        public int ByteBuffersUsed { get; }

        /// <summary>Number of overflow buffers configured (each up to 2^21 individual byte[] slots).</summary>
        public int OverflowBuffersCount { get; }

        /// <summary>The next overflow-buffer index that would have been written to (equals <see cref="OverflowBuffersCount"/> at exhaustion).</summary>
        public int OverflowCurrentIndex { get; }

        /// <summary>Total number of strings stored across all overflow buffers at the time of failure.</summary>
        public long OverflowUsedStrings { get; }

        /// <summary>Number of strings stored in the large-string buffer at the time of failure.</summary>
        public long LargeStringCount { get; }

        /// <summary>Approximate bytes occupied by strings in the large-string buffer at the time of failure.</summary>
        public long LargeStringSize { get; }

        /// <nodoc />
        public StringTableExhaustedException(
            int stringCount,
            long sizeInBytes,
            int byteBuffersCount,
            int byteBuffersUsed,
            int overflowBuffersCount,
            int overflowCurrentIndex,
            long overflowUsedStrings,
            long largeStringCount,
            long largeStringSize)
            : base(BuildMessage(
                stringCount,
                sizeInBytes,
                byteBuffersCount,
                byteBuffersUsed,
                overflowBuffersCount,
                overflowCurrentIndex,
                overflowUsedStrings,
                largeStringCount,
                largeStringSize))
        {
            StringCount = stringCount;
            SizeInBytes = sizeInBytes;
            ByteBuffersCount = byteBuffersCount;
            ByteBuffersUsed = byteBuffersUsed;
            OverflowBuffersCount = overflowBuffersCount;
            OverflowCurrentIndex = overflowCurrentIndex;
            OverflowUsedStrings = overflowUsedStrings;
            LargeStringCount = largeStringCount;
            LargeStringSize = largeStringSize;
        }

        private static string BuildMessage(
            int stringCount,
            long sizeInBytes,
            int byteBuffersCount,
            int byteBuffersUsed,
            int overflowBuffersCount,
            int overflowCurrentIndex,
            long overflowUsedStrings,
            long largeStringCount,
            long largeStringSize)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "This string table ran out of space. Count={0}, SizeInBytes={1}, ByteBuffers={2} (used={3}), OverflowBuffers={4} (currentIndex={5}, usedStrings={6}), LargeStringBuffer (count={7}, size={8})",
                stringCount,
                sizeInBytes,
                byteBuffersCount,
                byteBuffersUsed,
                overflowBuffersCount,
                overflowCurrentIndex,
                overflowUsedStrings,
                largeStringCount,
                largeStringSize);
        }
    }
}
