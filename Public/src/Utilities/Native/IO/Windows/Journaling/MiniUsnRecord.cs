// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Utilities;

namespace BuildXL.Native.IO.Windows
{
    /// <summary>
    /// Represents per-file USN data (that returned from a single-file query with <see cref="FileSystemWin.ReadFileUsnByHandle(Microsoft.Win32.SafeHandles.SafeFileHandle, bool)"/>).
    /// </summary>
    /// <remarks>
    /// This is the managed projection of the useful fields of <see cref="Windows.FileSystemWin.NativeUsnRecordV3"/> when querying a single file.
    /// It does not correspond to any actual native structure.
    /// </remarks>
    public readonly struct MiniUsnRecord : IEquatable<MiniUsnRecord>
    {
        /// <summary>
        /// ID of the file to which this record pertains
        /// </summary>
        public readonly FileId FileId;

        /// <summary>
        /// Change journal cursor at which this record sits.
        /// </summary>
        public readonly Usn Usn;

        /// <summary>
        /// Creates a MiniUsnRecord
        /// </summary>
        public MiniUsnRecord(FileId file, Usn usn)
        {
            FileId = file;
            Usn = usn;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public bool Equals(MiniUsnRecord other)
        {
            return FileId == other.FileId && Usn == other.Usn;
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(FileId.GetHashCode(), Usn.GetHashCode());
        }

        /// <nodoc />
        public static bool operator ==(MiniUsnRecord left, MiniUsnRecord right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(MiniUsnRecord left, MiniUsnRecord right)
        {
            return !left.Equals(right);
        }
    }
}
