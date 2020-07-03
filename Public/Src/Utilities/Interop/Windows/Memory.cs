// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Runtime.InteropServices;

namespace BuildXL.Interop.Windows
{
    /// <summary>
    /// The Memory class offers interop calls for memory based tasks into operating system facilities
    /// </summary>
    public static class Memory
    {
        /// <nodoc />
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public sealed class MEMORYSTATUSEX
        {
            /// <nodoc />
            public uint dwLength;

            /// <nodoc />
            public uint dwMemoryLoad;

            /// <nodoc />
            public ulong ullTotalPhys;

            /// <nodoc />
            public ulong ullAvailPhys;

            /// <nodoc />
            public ulong ullTotalPageFile;

            /// <nodoc />
            public ulong ullAvailPageFile;

            /// <nodoc />
            public ulong ullTotalVirtual;

            /// <nodoc />
            public ulong ullAvailVirtual;

            /// <nodoc />
            public ulong ullAvailExtendedVirtual;

            /// <nodoc />
            public MEMORYSTATUSEX()
            {
                dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            }
        }

        /// <nodoc />
        [DllImport(BuildXL.Interop.Libraries.WindowsKernel32, SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

        /// <nodoc />
        [DllImport(BuildXL.Interop.Libraries.WindowsPsApi, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EmptyWorkingSet(IntPtr handle);

        /// <summary>
        /// Get memory usage with all counters for Windows
        /// </summary>
        public static Process.PROCESSMEMORYCOUNTERSEX GetMemoryUsageCounters(IntPtr handle)
        {
            Process.PROCESSMEMORYCOUNTERSEX processMemoryCounters = new Windows.Process.PROCESSMEMORYCOUNTERSEX();
            return Process.GetProcessMemoryInfo(handle, processMemoryCounters, processMemoryCounters.cb)
                ? processMemoryCounters
                : null;
        }

        /// <nodoc />
        public enum WorkingSetSizeFlags
        {
            /// <summary>
            /// The WorkingSet cannot go below the configured minimum value
            /// </summary>
            MinEnable = 1 << 0,

            /// <summary>
            /// The WorkingSet can go below the configured minimum value
            /// </summary>
            MinDisable = 1 << 1,

            /// <summary>
            /// The WorkingSet cannot exceed the configured maximum value
            /// </summary>
            MaxEnable = 1 << 2,

            /// <summary>
            /// The WorkingSet can exceed the configured maximum value
            /// </summary>
            MaxDisable = 1 << 3,
        }

        /// <nodoc />
        [DllImport(Libraries.WindowsKernel32, SetLastError = true)]
        public static extern bool SetProcessWorkingSetSizeEx(IntPtr handle, UIntPtr min, UIntPtr max, WorkingSetSizeFlags flags);
    }
}
