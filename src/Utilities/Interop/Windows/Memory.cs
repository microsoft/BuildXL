// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
    }
}
