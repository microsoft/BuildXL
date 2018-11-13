// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.InteropServices;

namespace BuildXL.Interop.MacOS
{
    /// <summary>
    /// The Memory class offers interop calls for memory based tasks into operating system facilities
    /// </summary>
    public unsafe static class Memory
    {
        /// <nodoc />
        [StructLayout(LayoutKind.Sequential)]
        public unsafe struct RamUsageInfo
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
        }

        /// <summary>
        /// Returns the current host memory usage information to the caller
        /// </summary>
        /// <param name="buffer">A RamUsageInfo struct pointer to hold memory statistics</param>
        /// <returns></returns>
        [DllImport(BuildXL.Interop.Libraries.InteropLibMacOS)]
        public static extern int GetRamUsageInfo(RamUsageInfo *buffer);
    }
}
