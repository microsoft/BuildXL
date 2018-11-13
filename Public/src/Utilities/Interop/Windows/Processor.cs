// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Runtime.InteropServices;

namespace BuildXL.Interop.Windows
{
    /// <summary>
    /// The Processor class offers interop calls for processor based tasks into operating system facilities
    /// </summary>
    public static class Processor
    {
        /// <nodoc />
        [DllImport(BuildXL.Interop.Libraries.WindowsKernel32, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetSystemTimes(
            out long lpIdleTime,
            out long lpKernelTime,
            out long lpUserTime);

        /// <nodoc />
        [StructLayout(LayoutKind.Sequential)]
        public struct PERFORMANCE_INFORMATION
        {
            /// <nodoc />
            public uint cb;

            /// <nodoc />
            public IntPtr CommitTotal;

            /// <nodoc />
            public IntPtr CommitLimit;

            /// <nodoc />
            public IntPtr CommitPeak;

            /// <nodoc />
            public IntPtr PhysicalTotal;

            /// <nodoc />
            public IntPtr PhysicalAvailable;

            /// <nodoc />
            public IntPtr SystemCache;

            /// <nodoc />
            public IntPtr KernelTotal;

            /// <nodoc />
            public IntPtr KernelPaged;

            /// <nodoc />
            public IntPtr KernelNonpaged;

            /// <nodoc />
            public IntPtr PageSize;

            /// <nodoc />
            public uint HandleCount;

            /// <nodoc />
            public uint ProcessCount;

            /// <nodoc />
            public uint ThreadCount;

            /// <nodoc />
            public static PERFORMANCE_INFORMATION CreatePerfInfo()
            {
                PERFORMANCE_INFORMATION pi = new PERFORMANCE_INFORMATION();
                pi.cb = (uint)Marshal.SizeOf(typeof(PERFORMANCE_INFORMATION));
                return pi;
            }
        }

        /// <nodoc />
        [DllImport(BuildXL.Interop.Libraries.WindowsPsApi, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetPerformanceInfo(out PERFORMANCE_INFORMATION pPerformanceInformation, uint cb);
    }
}
