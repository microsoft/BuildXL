// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.InteropServices;

namespace BuildXL.Interop.MacOS
{
    /// <summary>
    /// The Processor class offers interop calls for processor based tasks into operating system facilities
    /// </summary>
    public static class Processor
    {
        /// <nodoc />
        [StructLayout(LayoutKind.Sequential)]
        public struct CpuLoadInfo
        {
            /// <nodoc />
            public ulong SystemTime;

            /// <nodoc />
            public ulong UserTime;

            /// <nodoc />
            public ulong IdleTime;
        }

        [DllImport(Libraries.BuildXLInteropLibMacOS)]
        private static extern int GetCpuLoadInfo(ref CpuLoadInfo buffer, long bufferSize);

        /// <summary>
        /// Returns the current CPU load info accross all CPU cores to the caller
        /// </summary>
        /// <param name="buffer">A CpuLoadInfo struct to hold the timing inforamtion of the current host CPU</param>

        public static int GetCpuLoadInfo(ref CpuLoadInfo buffer)
            => GetCpuLoadInfo(ref buffer, Marshal.SizeOf(buffer));
    }
}
