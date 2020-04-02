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

        private static readonly Lazy<ulong> s_totalMemoryBytes = new Lazy<ulong>(GetPhysicalMemoryBytes);

        private const int _SC_PAGESIZE_OSX = 29;
        private const int _SC_PHYS_PAGES_OSX = 200;

        private static ulong GetPhysicalMemoryBytes()
        {
            long physicalPages = sysconf(_SC_PHYS_PAGES_OSX);
            long pageSize = sysconf(_SC_PAGESIZE_OSX);
            return (ulong)(physicalPages * pageSize);
        }

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

        [DllImport(Libraries.BuildXLInteropLibMacOS)]
        private static extern int GetRamUsageInfo(ref MacRamUsageInfo buffer, long bufferSize);

        [DllImport(Libraries.BuildXLInteropLibMacOS)]
        internal static extern int GetPeakWorkingSetSize(int pid, ref ulong buffer);

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
    }
}