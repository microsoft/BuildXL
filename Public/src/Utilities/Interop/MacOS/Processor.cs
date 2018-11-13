// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.InteropServices;

namespace BuildXL.Interop.MacOS
{
    /// <summary>
    /// The Processor class offers interop calls for processor based tasks into operating system facilities
    /// </summary>
    public unsafe static class Processor
    {
        /// <nodoc />
        [StructLayout(LayoutKind.Sequential)]
        public unsafe struct CpuLoadInfo
        {
            /// <nodoc />
            public ulong SystemTime;

            /// <nodoc />
            public ulong UserTime;

            /// <nodoc />
            public ulong IdleTime;
        }

        /// <summary>
        /// Returns the current CPU load info accross all CPU cores to the caller
        /// </summary>
        /// <param name="buffer">A CpuLoadInfo struct to hold the timing inforamtion of the current host CPU</param>
        /// <returns></returns>
        [DllImport(BuildXL.Interop.Libraries.InteropLibMacOS)]
        public static extern int GetCpuLoadInfo(CpuLoadInfo *buffer);
    }
}
