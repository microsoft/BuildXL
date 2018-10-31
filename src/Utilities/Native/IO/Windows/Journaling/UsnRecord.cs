// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Utilities;

namespace BuildXL.Native.IO.Windows
{
    /// <summary>
    /// Represents USN data from reading a journal.
    /// </summary>
    /// <remarks>
    /// This is the managed projection of the useful fields of <see cref="Windows.FileSystemWin.NativeUsnRecordV3"/>.
    /// It does not correspond to any actual native structure.
    /// Note that this record may be invalid. A record is invalid if it has Usn 0, which indicates
    /// that either the volume's change journal is disabled or that this particular file has not
    /// been modified since the change journal was enabled.
    /// </remarks>
    public readonly struct UsnRecord : IEquatable<UsnRecord>
    {
        /// <summary>
        /// ID of the file to which this record pertains
        /// </summary>
        public readonly FileId FileId;

        /// <summary>
        /// ID of the containing directory of the file at the time of this change.
        /// </summary>
        public readonly FileId ContainerFileId;

        /// <summary>
        /// Change journal cursor at which this record sits.
        /// </summary>
        public readonly Usn Usn;

        /// <summary>
        /// Reason for the change.
        /// </summary>
        public readonly UsnChangeReasons Reason;

        /// <summary>
        /// Creates a UsnRecord
        /// </summary>
        public UsnRecord(FileId file, FileId container, Usn usn, UsnChangeReasons reasons)
        {
            FileId = file;
            ContainerFileId = container;
            Usn = usn;
            Reason = reasons;
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public bool Equals(UsnRecord other)
        {
            return FileId == other.FileId && Usn == other.Usn && Reason == other.Reason && ContainerFileId == other.ContainerFileId;
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(FileId.GetHashCode(), Usn.GetHashCode(), (int)Reason, ContainerFileId.GetHashCode());
        }

        /// <nodoc />
        public static bool operator ==(UsnRecord left, UsnRecord right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(UsnRecord left, UsnRecord right)
        {
            return !left.Equals(right);
        }
    }
}
