// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;

namespace BuildXL.Native.IO.Windows
{
    /// <summary>
    /// <c>WIN32_FIND_DATA</c>
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    [SuppressMessage("Microsoft.Naming", "CA1707:IdentifiersShouldNotContainUnderscores")]
    [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
#pragma warning disable SA1649 // File name should match first type name
    public struct WIN32_FIND_DATA
#pragma warning restore SA1649
    {
        /// <summary>
        /// The file attributes of a file
        /// </summary>
        public FileAttributes DwFileAttributes;

        /// <summary>
        /// Specified when a file or directory was created
        /// </summary>
        public System.Runtime.InteropServices.ComTypes.FILETIME FtCreationTime;

        /// <summary>
        /// Specifies when the file was last read from, written to, or for executable files, run.
        /// </summary>
        public System.Runtime.InteropServices.ComTypes.FILETIME FtLastAccessTime;

        /// <summary>
        /// For a file, the structure specifies when the file was last written to, truncated, or overwritten.
        /// For a directory, the structure specifies when the directory is created.
        /// </summary>
        public System.Runtime.InteropServices.ComTypes.FILETIME FtLastWriteTime;

        /// <summary>
        /// The high-order DWORD value of the file size, in bytes.
        /// </summary>
        public uint NFileSizeHigh;

        /// <summary>
        /// The low-order DWORD value of the file size, in bytes.
        /// </summary>
        public uint NFileSizeLow;

        /// <summary>
        /// If the dwFileAttributes member includes the FILE_ATTRIBUTE_REPARSE_POINT attribute, this member specifies the reparse point tag.
        /// </summary>
        public uint DwReserved0;

        /// <summary>
        /// Reserved for future use.
        /// </summary>
        public uint DwReserved1;

        /// <summary>
        /// The name of the file.
        /// </summary>
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = NativeIOConstants.MaxPath)]
        public string CFileName;

        /// <summary>
        /// An alternative name for the file.
        /// </summary>
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
        public string CAlternate;
    }

    /// <summary>
    /// Set of extensions method for <see cref="WIN32_FIND_DATA"/>.
    /// </summary>
    /// <remarks>
    /// Instead of adding logic directly into <see cref="WIN32_FIND_DATA"/> we add it as an extension method to keep the class a POCO class with no behavior.
    /// </remarks>
    public static class Win32FindDataExtensions
    {
        /// <summary>
        /// Gets the size of a file.
        /// </summary>
        public static long GetFileSize(in this WIN32_FIND_DATA data)
        {
            return (long)data.NFileSizeHigh << 32 | (long)data.NFileSizeLow;
        }
    }
}
