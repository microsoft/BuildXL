// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.InteropServices;
using System.Text;
using static BuildXL.Interop.Unix.IO;
using static BuildXL.Interop.Libraries;

namespace BuildXL.Interop.Unix
{
    /// <summary>
    /// The IO class for Mac-specific operations
    /// </summary>
    public static class Impl_Common
    {
        [DllImport(LibC, SetLastError = true)]
        internal static extern int symlink(string target, string symlinkFilePath);

        [DllImport(LibC, SetLastError = true)]
        internal static extern int mkfifo(string pathname, FilePermissions mode);

        [DllImport(LibC, SetLastError = true)]
        unsafe internal static extern int read(int fd, byte* buf, int bufsiz);

        [DllImport(LibC, SetLastError = true)]
        internal static extern int read(int fd, byte[] buf, int bufsiz);

        [DllImport(LibC, SetLastError = true)]
        internal static extern int link(string link, string hardlinkFilePath);

        [DllImport(LibC, SetLastError = true)]
        internal static extern long sysconf(int name);

        [DllImport(LibC, SetLastError = true, CharSet = CharSet.Ansi)]
        internal static extern long readlink(string link, StringBuilder buffer, long length);

        [DllImport(LibC, SetLastError = true, CharSet = CharSet.Ansi)]
        internal static extern int chmod(string pathname, FilePermissions mode);

        [DllImport(LibC, SetLastError = true)]
        internal static extern uint geteuid();

        [DllImport(LibC, SetLastError = true)]
        internal static extern int kill(int pid, int signal);
    }
}