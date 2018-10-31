// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Native.IO.Windows
{
    /// <summary>
    /// These flags indicate the changes represented by a particular Usn record.
    /// See http://msdn.microsoft.com/en-us/library/windows/desktop/hh802708(v=vs.85).aspx
    /// </summary>
    [Flags]
    public enum UsnChangeReasons : uint
    {
        /// <summary>
        /// A user has either changed one or more file or directory attributes (for example, the read-only,
        /// hidden, system, archive, or sparse attribute), or one or more time stamps.
        /// </summary>
        BasicInfoChange = 0x00008000,

        /// <summary>
        /// The file or directory is closed.
        /// </summary>
        Close = 0x80000000,

        /// <summary>
        /// The compression state of the file or directory is changed from or to compressed.
        /// </summary>
        CompressionChange = 0x00020000,

        /// <summary>
        /// The file or directory is extended (added to).
        /// </summary>
        DataExtend = 0x00000002,

        /// <summary>
        /// The data in the file or directory is overwritten.
        /// </summary>
        DataOverwrite = 0x00000001,

        /// <summary>
        /// The file or directory is truncated.
        /// </summary>
        DataTruncation = 0x00000004,

        /// <summary>
        /// The user made a change to the extended attributes of a file or directory.
        /// These NTFS file system attributes are not accessible to Windows-based applications.
        /// </summary>
        ExtendedAttributesChange = 0x00000400,

        /// <summary>
        /// The file or directory is encrypted or decrypted.
        /// </summary>
        EncryptionChange = 0x00040000,

        /// <summary>
        /// The file or directory is created for the first time.
        /// </summary>
        FileCreate = 0x00000100,

        /// <summary>
        /// The file or directory is deleted.
        /// </summary>
        FileDelete = 0x00000200,

        /// <summary>
        /// An NTFS file system hard link is added to or removed from the file or directory.
        /// An NTFS file system hard link, similar to a POSIX hard link, is one of several directory
        /// entries that see the same file or directory.
        /// </summary>
        HardLinkChange = 0x00010000,

        /// <summary>
        /// A user changes the FILE_ATTRIBUTE_NOT_CONTENT_INDEXED attribute.
        /// </summary>
        IndexableChange = 0x00004000,

        /// <summary>
        /// The one or more named data streams for a file are extended (added to).
        /// </summary>
        NamedDataExtend = 0x00000020,

        /// <summary>
        /// The data in one or more named data streams for a file is overwritten.
        /// </summary>
        NamedDataOverwrite = 0x00000010,

        /// <summary>
        /// The one or more named data streams for a file is truncated.
        /// </summary>
        NamedDataTruncation = 0x00000040,

        /// <summary>
        /// The object identifier of a file or directory is changed.
        /// </summary>
        ObjectIdChange = 0x00080000,

        /// <summary>
        /// A file or directory is renamed, and the file name in the USN_RECORD_V3 structure is the new name.
        /// </summary>
        RenameNewName = 0x00002000,

        /// <summary>
        /// The file or directory is renamed, and the file name in the USN_RECORD_V3 structure is the previous name.
        /// </summary>
        RenameOldName = 0x00001000,

        /// <summary>
        /// The reparse point that is contained in a file or directory is changed, or a reparse point is added to or
        /// deleted from a file or directory.
        /// </summary>
        ReparsePointChange = 0x00100000,

        /// <summary>
        /// A change is made in the access rights to a file or directory.
        /// </summary>
        SecurityChange = 0x00000800,

        /// <summary>
        /// A named stream is added to or removed from a file, or a named stream is renamed.
        /// </summary>
        StreamChange = 0x00200000,
    }
}
