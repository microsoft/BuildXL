// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using static BuildXL.Interop.MacOS.IO;
using static BuildXL.Interop.MacOS.Constants;

using Mode=BuildXL.Interop.MacOS.IO.FilePermissions;

namespace BuildXL.Interop.MacOS
{
    /// <summary>
    /// The IO class for Linux-specific operations
    /// </summary>
    public static class IO_Linux
    {
        /// <summary>Name of the standard C library</summary>
        private const string LibC = "c";

        /// <summary>
        /// Version of __fxstatat syscalls to use.
        /// </summary>
        private const int __Ver = 1;

        /// <summary>Linux specific implementation of <see cref="IO.GetFileSystemType"/> </summary>
        internal static int GetFileSystemType(SafeFileHandle fd, StringBuilder fsTypeName, long bufferSize)
        {
            var path = ToPath(fd);
            if (path == null)
            {
                return ERROR;
            }

            return Try(() =>
                {
                    var di = new DriveInfo(path);
                    fsTypeName.Append(di.DriveFormat);
                    return 0;
                },
                ERROR);
        }

        /// <summary>Linux specific implementation of <see cref="IO.StatFileDescriptor"/> </summary>
        public static int StatFileDescriptor(SafeFileHandle fd, ref StatBuffer statBuf) 
        {
            return StatFile(ToInt(fd), string.Empty, followSymlink: false, ref statBuf);
        }

        /// <summary>Linux specific implementation of <see cref="IO.StatFile"/> </summary>
        internal static int StatFile(string path, bool followSymlink, ref StatBuffer statBuf)
        {
            Contract.Requires(!string.IsNullOrEmpty(path));
            return StatFile(AT_FDCWD, path, followSymlink, ref statBuf);
        }

        private static int StatFile(int fd, string path, bool followSymlink, ref StatBuffer statBuf)
        {
            // using 'fstatat' instead of the newer 'statx' because Ubuntu 18.04 doesn't have iit
            var buf = new stat_buf();
            int result = StatFile(fd, path, followSymlink, ref buf);
            if (result != 0)
            {
                return ERROR;
            }
            else
            {
                Translate(buf, ref statBuf);
                return 0;
            }
        }

        /// <summary>
        /// pathname, dirfd, and flags to identify the target file in one of the following ways:
        ///
        /// An absolute pathname
        ///    If pathname begins with a slash, then it is an absolute pathname that identifies the target file.  In this
        ///    case, dirfd is ignored.
        ///
        /// A relative pathname
        ///    If pathname is a string that begins with a character other than a slash and dirfd is AT_FDCWD, then
        ///    pathname is a relative pathname that is interpreted relative to the process's current working directory.
        ///
        /// A directory-relative pathname
        ///    If  pathname  is  a  string that begins with a character other than a slash and dirfd is a file descriptor
        ///    that refers to a directory, then pathname is a relative pathname that is interpreted relative to the
        ///    directory referred to by dirfd.
        /// </summary>
        private static int StatFile(int dirfd, string pathname, bool followSymlink, ref stat_buf buf)
        {
            Contract.Requires(pathname != null);

            int flags = 0
                | (!followSymlink                 ? AT_SYMLINK_NOFOLLOW : 0)
                | (string.IsNullOrEmpty(pathname) ? AT_EMPTY_PATH : 0);

            int result;
            while (
                (result = fstatat(__Ver, dirfd, pathname, ref buf, flags)) < 0 && 
                Marshal.GetLastWin32Error() == (int)Errno.EINTR);
            return result;
        }

        /// <summary>Linux specific implementation of <see cref="IO.SafeReadLink"/> </summary>
        internal static long SafeReadLink(string link, StringBuilder buffer, long bufferSize)
        {
            var resultLength = readlink(link, buffer, bufferSize);
            if (resultLength < 0) return ERROR;
            buffer.Length = (int)resultLength;
            return resultLength;
        }

        /// <summary>Linux specific implementation of <see cref="IO.Open"/> </summary>
        internal static SafeFileHandle Open(string pathname, OpenFlags flags, FilePermissions permissions)
        {
            int result;
            while (
                (result = open(pathname, Translate(flags), permissions)) < 0 && 
                Marshal.GetLastWin32Error() == (int)Errno.EINTR);
            return new SafeFileHandle(new IntPtr(result), ownsHandle: true);
        }

        /// <summary>Linux specific implementation of <see cref="IO.GetFilePermissionsForFilePath"/> </summary>
        internal static int GetFilePermissionsForFilePath(string path, bool followSymlink)
        {
            var stat = new stat_buf();
            int errorCode = StatFile(AT_FDCWD, path, followSymlink, ref stat);
            return errorCode == 0 ? (int)stat.st_mode : ERROR;
        }

        /// <summary>Linux specific implementation of <see cref="IO.SetFilePermissionsForFilePath"/> </summary>
        internal static int SetFilePermissionsForFilePath(string path, FilePermissions permissions, bool followSymlink)
        {
            if (!followSymlink && IsSymlink(path))
            {
                // Permissions do not apply to symlinks on Linux systems (only on BSD systems).
                return 0;
            }

            return chmod(path, permissions);
        }

        /// <summary>
        /// Linux specific implementation of <see cref="IO.SetTimeStampsForFilePath"/>
        /// </summary>
        /// <remarks>
        /// Only atime and mtime are settable
        /// </remarks>
        internal static int SetTimeStampsForFilePath(string path, bool followSymlink, StatBuffer buf)
        {
            int flags = followSymlink ? 0 : AT_SYMLINK_NOFOLLOW;
            var atime = new Timespec { Tv_sec = buf.TimeLastAccess,       Tv_nsec = buf.TimeNSecLastAccess };
            var mtime = new Timespec { Tv_sec = buf.TimeLastModification, Tv_nsec = buf.TimeNSecLastModification };
            return utimensat(AT_FDCWD, path, new[] { atime, mtime }, flags);
        }

        private static bool IsSymlink(string path)
        {
            var buf = new stat_buf();
            return 
                fstatat(__Ver, AT_FDCWD, path, ref buf, AT_SYMLINK_NOFOLLOW) == 0 &&
                (buf.st_mode & (ushort)FilePermissions.S_IFLNK) != 0;
        }

        private static int ToInt(SafeFileHandle fd) => fd.DangerousGetHandle().ToInt32();
        private static string ToPath(SafeFileHandle fd)
        {
            var path = new StringBuilder(MaxPathLength);
            return SafeReadLink($"/proc/self/fd/{ToInt(fd)}", path, path.Capacity) >= 0
                ? path.ToString()
                : null;
        }

        private static O_Flags Translate(OpenFlags flags)
        {
            return Enum
                .GetValues(typeof(OpenFlags))
                .Cast<OpenFlags>()
                .Where(f => flags.HasFlag(f))
                .Aggregate(O_Flags.O_NONE, (acc, f) => acc | TranslateOne(f));
        }

        private static O_Flags TranslateOne(OpenFlags flag)
        {
            return flag switch
            {
                OpenFlags.O_RDONLY   => O_Flags.O_RDONLY,
                OpenFlags.O_WRONLY   => O_Flags.O_WRONLY,
                OpenFlags.O_RDWR     => O_Flags.O_RDWR,
                OpenFlags.O_NONBLOCK => O_Flags.O_NONBLOCK,
                OpenFlags.O_APPEND   => O_Flags.O_APPEND,
                OpenFlags.O_CREAT    => O_Flags.O_CREAT,
                OpenFlags.O_TRUNC    => O_Flags.O_TRUNC,
                OpenFlags.O_EXCL     => O_Flags.O_EXCL,
                OpenFlags.O_NOFOLLOW => O_Flags.O_NOFOLLOW | O_Flags.O_PATH,
                OpenFlags.O_SYMLINK  => O_Flags.O_NOFOLLOW | O_Flags.O_PATH,
                OpenFlags.O_CLOEXEC  => O_Flags.O_CLOEXEC,
                                   _ => O_Flags.O_NONE,
            };
        }

        private static uint Concat(params uint[] elems) => elems.Aggregate(0U, (a, e) => a*10+e);

        private static void Translate(stat_buf from, ref StatBuffer to)
        {
            to.DeviceID                 = (int)from.st_dev;
            to.InodeNumber              = from.st_ino;
            to.Mode                     = (ushort)from.st_mode;
            to.HardLinks                = (ushort)from.st_nlink;
            to.UserID                   = from.st_uid;
            to.GroupID                  = from.st_gid;
            to.Size                     = from.st_size;
            to.TimeLastAccess           = from.st_atime;
            to.TimeLastModification     = from.st_mtime;
            to.TimeLastStatusChange     = from.st_ctime;
            to.TimeCreation             = 0; // not available 
            // even though EXT4 supports nanosecond precision, the kernel time is not
            // necessarily getting updated every 1ns so nsec values can still be quantized
            to.TimeNSecLastAccess       = from.st_atime_nsec;
            to.TimeNSecLastModification = from.st_mtime_nsec;
            to.TimeNSecLastStatusChange = from.st_ctime_nsec;
            to.TimeNSecCreation         = 0; // not available
        }

        private static T Try<T>(Func<T> action, T errorValue)
        {
            try
            {
                return action();
            }
            #pragma warning disable ERP022 // Unobserved exception in generic exception handler
            catch
            {
                return errorValue;
            }
            #pragma warning restore ERP022 // Unobserved exception in generic exception handler
        }

        #region P-invoke consts, structs, and other type definitions
        #pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

        private const int AT_FDCWD              = -100;
        private const int AT_SYMLINK_NOFOLLOW   = 0x100;
        private const int AT_EMPTY_PATH         = 0x1000;

        /// <summary>
        /// struct stat from stat.h
        /// </summary>
        /// <remarks>
        /// IMPORTANT: the explicitly specified size of 256 must match the value of 'sizeof(struct stat)' in C
        /// </remarks>
        [StructLayout(LayoutKind.Sequential, Size = 144)]
        private struct stat_buf
        {
            public  UInt64   st_dev;     // device
            public  UInt64   st_ino;     // inode
            public  UInt64   st_nlink;   // number of hard links
            public  UInt32   st_mode;    // protection
            public  UInt32   st_uid;     // user ID of owner
            public  UInt32   st_gid;     // group ID of owner
            private UInt32   _padding;   // padding for structure alignment
            public  UInt64   st_rdev;    // device type (if inode device)
            public  Int64    st_size;    // total size, in bytes
            public  Int64    st_blksize; // blocksize for filesystem I/O
            public  Int64    st_blocks;  // number of blocks allocated
            public  Int64    st_atime;   // time of last access
            public  Int64    st_atime_nsec; // Timespec.tv_nsec partner to st_atime
            public  Int64    st_mtime;   // time of last modification
            public  Int64    st_mtime_nsec; // Timespec.tv_nsec partner to st_mtime
            public  Int64    st_ctime;   // time of last status change
            public  Int64    st_ctime_nsec; // Timespec.tv_nsec partner to st_ctime
            /* More spare space here for future expansion (controlled by explicitly specifying struct size) */
        }

        /// <summary>
        /// Flags for <see cref="open"/>
        /// </summary>
        [Flags]
        public enum O_Flags : int
        {
            O_NONE      = 0,
            O_RDONLY    = 0,     // open for reading only
            O_WRONLY    = 1,     // open for writing only
            O_RDWR      = 2,     // open for reading and writing
            O_CREAT     = 64,    // create file if it does not exist
            O_EXCL      = 128,   // error if O_CREAT and the file exists
            O_NOCCTY    = 256,
            O_TRUNC     = 512,   // truncate size to 0
            O_APPEND    = 1024,  // append on each write
            O_NONBLOCK  = 2048,  // do not block on open or for data to become available 
            O_ASYNC     = 8192,
            O_DIRECT    = 16384,
            O_DIRECTORY = 65536,
            O_NOFOLLOW  = 131072,  // do not follow symlinks
            O_CLOEXEC   = 524288, // mark as close-on-exec
            O_SYNC      = 1052672,
            O_PATH      = 2097152, // allow open of symlinks
        }
        #endregion

        #region P-invoke function definitions
        [DllImport(LibC, SetLastError = true)]
        private static extern int open(string pathname, O_Flags flags, FilePermissions permission);

        [DllImport(LibC, SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern long readlink(string link, StringBuilder buffer, long length);

        [DllImport(LibC, SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern int chmod(string pathname, Mode mode);

        [DllImport(LibC, EntryPoint = "__fxstatat", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern int fstatat(int __ver, int fd, string pathname, ref stat_buf buff, int flags);

        [DllImport(LibC, SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern int utimensat(int dirfd, string pathname, Timespec[] times, int flags);

        /// <summary>Linux specific implementation of <see cref="IO.symlink"/> </summary>
        [DllImport(LibC, SetLastError = true, CharSet = CharSet.Ansi)]
        internal static extern int symlink(string target, string symlinkFilePath);

        /// <summary>Linux specific implementation of <see cref="IO.link"/> </summary>
        [DllImport(LibC, SetLastError = true, CharSet = CharSet.Ansi)]
        internal static extern int link(string link, string hardlinkFilePath);

        #pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
        #endregion
    }
}