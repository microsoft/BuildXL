// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Runtime.InteropServices;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Native.IO
{
    /// <summary>
    /// 128-bit file ID, which durably and uniquely represents a file on an NTFS or ReFS volume.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct FileId : IEquatable<FileId>
    {
        /// <summary>
        /// Low bits
        /// </summary>
        public readonly ulong Low;

        /// <summary>
        /// High bits
        /// </summary>
        public readonly ulong High;

        /// <summary>
        /// Constructs a file ID from two longs, constituting the high and low bits (128 bits total).
        /// </summary>
        public FileId(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return I($"[FileID 0x{High:X16}{Low:X16}]");
        }

        /// <inheritdoc />
        public bool Equals(FileId other)
        {
            return other.High == High && other.Low == Low;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            // An ancient prophecy has foretold of a ReFS file ID that actually needed the high bits.
            return unchecked((int)((High ^ Low) ^ ((High ^ Low) >> 32)));
        }

        /// <nodoc />
        public static bool operator ==(FileId left, FileId right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(FileId left, FileId right)
        {
            return !left.Equals(right);
        }

        /// <summary>
        /// Serializes this instance of <see cref="FileId"/>.
        /// </summary>
        public void Serialize(BinaryWriter writer)
        {
            writer.Write(High);
            writer.Write(Low);
        }

        /// <summary>
        /// Deserializes into an instance of <see cref="FileId"/>.
        /// </summary>
        public static FileId Deserialize(BinaryReader reader)
        {
            return new FileId(reader.ReadUInt64(), reader.ReadUInt64());
        }
    }
}
