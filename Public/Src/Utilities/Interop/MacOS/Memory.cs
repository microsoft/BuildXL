// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.InteropServices;

namespace BuildXL.Interop.MacOS
{
    /// <summary>
    /// The Memory class offers interop calls for memory based tasks into operating system facilities
    /// </summary>
    public static class Memory
    {
        /// <nodoc />
        [StructLayout(LayoutKind.Sequential)]
        public struct RamUsageInfo
        {
            /// <nodoc />
            public ulong Active;

            /// <nodoc />
            public ulong Inactive;

            /// <nodoc />
            public ulong Wired;

            /// <nodoc />
            public ulong Speculative;

            /// <nodoc />
            public ulong Free;

            /// <nodoc />
            public ulong Purgable;

            /// <nodoc />
            public ulong FileBacked;

            /// <nodoc />
            public ulong Compressed;

            /// <nodoc />
            public ulong Internal;

            /// <summary>
            /// The "AppMemory" is defined to be the difference of <see cref="Internal"/> and <see cref="Purgable"/>
            /// </summary>
            public ulong AppMemory => Internal - Purgable;
        }

        [DllImport(Libraries.BuildXLInteropLibMacOS)]
        private static extern int GetRamUsageInfo(ref RamUsageInfo buffer, long bufferSize);

        /// <summary>
        /// Returns the current host memory usage information to the caller
        /// </summary>
        /// <param name="buffer">A RamUsageInfo struct pointer to hold memory statistics</param>
        public static int GetRamUsageInfo(ref RamUsageInfo buffer)
            => GetRamUsageInfo(ref buffer, Marshal.SizeOf(buffer));

        /// <summary>
        /// Returns a process peak working set size in bytes
        /// </summary>
        /// <param name="pid">The process id to check</param>
        /// <param name="buffer">A long pointer to hold the process peak memory usage</param>
        [DllImport(Libraries.BuildXLInteropLibMacOS)]
        public static extern int GetPeakWorkingSetSize(int pid, ref ulong buffer);
    }
}
