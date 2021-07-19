// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;
using static BuildXL.Interop.Unix.Impl_Common;
using static BuildXL.Interop.Unix.IO;
using static BuildXL.Interop.Unix.Constants;
using static BuildXL.Interop.Unix.Memory;
using static BuildXL.Interop.Unix.Process;
using static BuildXL.Interop.Unix.Processor;

namespace BuildXL.Interop.Unix
{
    /// <summary>
    /// The IO class for Mac-specific operations
    /// </summary>
    public static class Impl_Mac
    {
        internal static int GetRamUsageInfo(ref RamUsageInfo buffer)
        {
            var buf = new MacRamUsageInfo();
            var ret = GetRamUsageInfo(ref buf, Marshal.SizeOf(buf));
            if (ret != 0) return ERROR;
            buffer.TotalBytes = s_totalMemoryBytes.Value;
            buffer.FreeBytes = s_totalMemoryBytes.Value - (buf.AppMemory + buf.Wired + buf.Compressed);
            return 0;
        }

        internal static string GetMountNameForPath(string path)
        {
            var statFsBuffer = new StatFsBuffer();
            var error = StatFs(path, ref statFsBuffer);
            if (error != 0)
            {
                return null;
            }

            return statFsBuffer.f_mntonname;
        }

        private static readonly Lazy<ulong> s_totalMemoryBytes = new Lazy<ulong>(GetPhysicalMemoryBytes);

        private const int _SC_PAGESIZE_OSX = 29;
        private const int _SC_PHYS_PAGES_OSX = 200;

        private static ulong GetPhysicalMemoryBytes()
        {
            long physicalPages = sysconf(_SC_PHYS_PAGES_OSX);
            long pageSize = sysconf(_SC_PAGESIZE_OSX);
            return (ulong)(physicalPages * pageSize);
        }

        /// <summary>Don't follow symbolic links when setting/getting xattrs</summary>
        internal const int XATTR_NOFOLLOW = 1;

        [StructLayout(LayoutKind.Sequential)]
        internal struct MacRamUsageInfo
        {
            #pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
            public ulong Active;
            public ulong Inactive;
            public ulong Wired;
            public ulong Speculative;
            public ulong Free;
            public ulong Purgable;
            public ulong FileBacked;
            public ulong Compressed;
            public ulong Internal;
            #pragma warning restore CS1591 // Missing XML comment for publicly visible type or member

            /// <summary>
            /// The "AppMemory" is defined to be the difference of <see cref="Internal"/> and <see cref="Purgable"/>
            /// </summary>
            public ulong AppMemory => Internal - Purgable;
        }

        /// <summary>
        /// C# representation of the native struct statfs64
        ///
        /// CODESYNC: https://github.com/apple/darwin-xnu/blob/master/bsd/sys/mount.h#L103-L120
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct StatFsBuffer
        {
            /// <summary>fundamental file system block size</summary>
            public uint f_bsize;

            /// <summary>optimal transfer block size</summary>
            public int f_iosize;

            /// <summary>total data blocks in file system</summary>
            public ulong f_blocks;

            /// <summary>free blocks in fs</summary>
            public ulong f_bfree;

            /// <summary>free blocks avail to non-superuser</summary>
            public ulong f_bavail;

            /// <summary>total file nodes in file system</summary>
            public ulong f_files;

            /// <summary>free file nodes in fs</summary>
            public ulong f_ffree;

            /// <summary>file system id</summary>
            [MarshalAs(UnmanagedType.ByValArray, SizeConst=2)]
            public int[] f_fsid;

            /// <summary>user that mounted the filesystem</summary>
            public int f_owner;

            /// <summary>type of filesystem</summary>
            public uint f_type;

            /// <summary>copy of mount exported flags</summary>
            public uint f_flags;

            /// <summary>fs sub-type (flavor)</summary>
            public uint f_fssubtype;

            /// <summary>fs type name</summary>
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst=16)]
            public string f_fstypename;

            /// <summary>directory on which mounted</summary>
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst=Constants.MaxPathLength)]
            public string f_mntonname;

            /// <summary>mounted filesystem</summary>
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst=Constants.MaxPathLength)]
            public string f_mntfromname;

            /// <summary>For future use</summary>
            [MarshalAs(UnmanagedType.ByValArray, SizeConst=8)]
            public uint[] f_reserved;
        }

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

        /// <summary>
        /// This routine returns information about a mounted file system.
        /// The <paramref name="path"/> argument is the path name of any file or directory
        /// within the mounted file system.  The <paramref name="buf"/> argument is a pointer
        /// to a <see cref="StatFsBuffer"/> structure.
        /// </summary>
        /// <returns>
        /// 0 on success, -1 otherwise.
        /// </returns>
        [DllImport(Libraries.LibC, SetLastError = true, EntryPoint = "statfs64")]
        private static extern int StatFs([MarshalAs(UnmanagedType.LPStr)] string path, ref StatFsBuffer buf);

        [DllImport(Libraries.BuildXLInteropLibMacOS)]
        private static extern int GetRamUsageInfo(ref MacRamUsageInfo buffer, long bufferSize);

        [DllImport(Libraries.BuildXLInteropLibMacOS)]
        internal static extern int GetPeakWorkingSetSize(int pid, ref ulong buffer, bool includeChildProcesses);

        [DllImport(Libraries.BuildXLInteropLibMacOS)]
        internal static extern int GetMemoryPressureLevel(ref PressureLevel level);

        [DllImport(Libraries.BuildXLInteropLibMacOS)]
        internal static extern int GetCpuLoadInfo(ref CpuLoadInfo buffer, long bufferSize);

        [DllImport(Libraries.BuildXLInteropLibMacOS)]
        internal static extern int GetProcessResourceUsage(int pid, ref ProcessResourceUsage buffer, long bufferSize, bool includeChildProcesses);

        #region Sandbox
        [DllImport(Libraries.BuildXLInteropLibMacOS)]
        internal static extern unsafe int NormalizePathAndReturnHash(byte[] pPath, byte* buffer, int bufferLength);
        #endregion

        [DllImport(Libraries.LibC, SetLastError = true)]
        unsafe internal static extern int setxattr(
            [MarshalAs(UnmanagedType.LPStr)] string path,
            [MarshalAs(UnmanagedType.LPStr)] string name,
            void *value,
            ulong size,
            uint position,
            int options);

        [DllImport(Libraries.LibC, SetLastError = true)]
        internal static extern long getxattr(
            [MarshalAs(UnmanagedType.LPStr)] string path,
            [MarshalAs(UnmanagedType.LPStr)] string name,
            ref long value,
            ulong size,
            uint position,
            int options);

        [DllImport(Libraries.BuildXLInteropLibMacOS)]
        internal static extern bool SetupProcessDumps(string logsDirectory, StringBuilder buffer, long length);

        [DllImport(Libraries.BuildXLInteropLibMacOS)]
        internal static extern void TeardownProcessDumps();
    }
}