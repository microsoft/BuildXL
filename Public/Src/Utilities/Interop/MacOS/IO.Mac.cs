// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using static BuildXL.Interop.MacOS.Constants;
using static BuildXL.Interop.MacOS.IO;

namespace BuildXL.Interop.MacOS
{
    /// <summary>
    /// The IO class for Mac-specific operations
    /// </summary>
    public static class IO_Mac
    {
        [DllImport(Libraries.BuildXLInteropLibMacOS, SetLastError = true)]
        private static extern int StatFileDescriptor(SafeFileHandle fd, ref StatBuffer statBuf, long statBufferSize);

        [DllImport(Libraries.BuildXLInteropLibMacOS, SetLastError = true)]
        private static extern int StatFile(string path, bool followSymlink, ref StatBuffer statBuf, long statBufferSize);

        /// <summary>OSX specific implementation of <see cref="IO.GetFileSystemType"/> </summary>
        [DllImport(Libraries.BuildXLInteropLibMacOS, SetLastError = true, CharSet = CharSet.Ansi)]
        internal static extern int GetFileSystemType(SafeFileHandle fd, StringBuilder fsTypeName, long bufferSize);

        /// <summary>OSX specific implementation of <see cref="IO.StatFileDescriptor"/> </summary>
        internal unsafe static int StatFileDescriptor(SafeFileHandle fd, ref StatBuffer statBuf) 
            => StatFileDescriptor(fd, ref statBuf, sizeof(StatBuffer));

        /// <summary>OSX specific implementation of <see cref="IO.StatFile"/> </summary>
        internal unsafe static int StatFile(string path, bool followSymlink, ref StatBuffer statBuf)
            => StatFile(path, followSymlink, ref statBuf, sizeof(StatBuffer));

        /// <summary>OSX specific implementation of <see cref="IO.SafeReadLink"/> </summary>
        [DllImport(Libraries.BuildXLInteropLibMacOS, SetLastError = true, CharSet = CharSet.Ansi)]
        internal static extern long SafeReadLink(string link, StringBuilder buffer, long length);

        /// <summary>OSX specific implementation of <see cref="IO.Open"/> </summary>
        [DllImport(Libraries.BuildXLInteropLibMacOS, SetLastError = true)]
        internal static extern SafeFileHandle Open(string pathname, OpenFlags flags, FilePermissions permission);

        /// <summary>OSX specific implementation of <see cref="IO.GetFilePermissionsForFilePath"/> </summary>
        [DllImport(Libraries.BuildXLInteropLibMacOS, SetLastError = true)]
        internal static extern int GetFilePermissionsForFilePath(string path, bool followSymlink);

        /// <summary>OSX specific implementation of <see cref="IO.SetFilePermissionsForFilePath"/> </summary>
        [DllImport(Libraries.BuildXLInteropLibMacOS, SetLastError = true)]
        internal static extern int SetFilePermissionsForFilePath(string path, FilePermissions permissions, bool followSymlink);

        /// <summary>OSX specific implementation of <see cref="IO.SetTimeStampsForFilePath"/> </summary>
        [DllImport(Libraries.BuildXLInteropLibMacOS, SetLastError = true)]
        internal static extern int SetTimeStampsForFilePath(string path, bool followSymlink, StatBuffer buffer);

        /// <summary>OSX specific implementation of <see cref="IO.symlink"/> </summary>
        [DllImport(Libraries.LibC, SetLastError = true)]
        internal static extern int symlink(string target, string symlinkFilePath);

        /// <nodoc />
        [DllImport(Libraries.LibC, SetLastError = true)]
        internal static extern int link(string link, string hardlinkFilePath);
    }
}