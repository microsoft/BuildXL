// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
#if NETCOREAPP
using System.Buffers.Binary;
#endif
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using BuildXL.Utilities;
using BuildXL.Utilities.Core;

namespace BuildXL.Native.IO
{
    /// <summary>
    /// This corresponds to FILE_ID_INFO as returned by GetFileInformationByHandleEx (with <see cref="BuildXL.Native.IO.Windows.FileSystemWin.FileInfoByHandleClass.FileIdInfo"/>).
    /// http://msdn.microsoft.com/en-us/library/windows/desktop/hh802691(v=vs.85).aspx
    /// </summary>
    /// <remarks>
    /// Note that the FileId field supports a ReFS-sized ID. This is because the corresponding FileIdInfo class was added in 8.1 / Server 2012 R2.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct FileIdAndVolumeId : IEquatable<FileIdAndVolumeId>
    {
        internal static readonly int Size = Marshal.SizeOf<FileIdAndVolumeId>();

        /// <summary>
        /// Volume containing the file.
        /// </summary>
        public readonly ulong VolumeSerialNumber;

        /// <summary>
        /// Unique identifier of the referenced file (within the containing volume).
        /// </summary>
        public readonly FileId FileId;

        /// <obvious />
        public FileIdAndVolumeId(ulong volumeSerialNumber, FileId fileId)
        {
            VolumeSerialNumber = volumeSerialNumber;
            FileId = fileId;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public bool Equals(FileIdAndVolumeId other)
        {
            return FileId == other.FileId && VolumeSerialNumber == other.VolumeSerialNumber;
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(FileId.GetHashCode(), VolumeSerialNumber.GetHashCode());
        }

        /// <nodoc />
        public static bool operator ==(FileIdAndVolumeId left, FileIdAndVolumeId right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(FileIdAndVolumeId left, FileIdAndVolumeId right)
        {
            return !left.Equals(right);
        }

        /// <summary>
        /// Serializes this instance of <see cref="FileIdAndVolumeId"/>.
        /// </summary>
        public void Serialize(BinaryWriter writer)
        {
            writer.Write(VolumeSerialNumber);
            FileId.Serialize(writer);
        }

#if NETCOREAPP
        /// <summary>
        /// Serializes this instance of <see cref="FileIdAndVolumeId"/> into <paramref name="destination"/>.
        /// </summary>
        public void Serialize(Span<byte> destination)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(destination, VolumeSerialNumber);
            FileId.Serialize(destination.Slice(sizeof(ulong)));
        }
#endif

        /// <summary>
        /// The number of bytes written by <see cref="Serialize(BinaryWriter)"/>.
        /// </summary>
        public const int SerializedByteLength = sizeof(ulong) + FileId.SerializedByteLength;

        /// <summary>
        /// Deserializes into an instance of <see cref="FileIdAndVolumeId"/>.
        /// </summary>
        public static FileIdAndVolumeId Deserialize(BinaryReader reader)
        {
            return new FileIdAndVolumeId(reader.ReadUInt64(), FileId.Deserialize(reader));
        }

#if NETCOREAPP
        /// <summary>
        /// Deserializes an instance of <see cref="FileIdAndVolumeId"/> from <paramref name="source"/>.
        /// </summary>
        public static FileIdAndVolumeId Deserialize(ReadOnlySpan<byte> source, out int bytesRead)
        {
            ulong volumeSerialNumber = BinaryPrimitives.ReadUInt64LittleEndian(source);
            FileId fileId = FileId.Deserialize(source.Slice(sizeof(ulong)), out int fileIdBytesRead);
            bytesRead = sizeof(ulong) + fileIdBytesRead;
            return new FileIdAndVolumeId(volumeSerialNumber, fileId);
        }
#endif
    }
}
