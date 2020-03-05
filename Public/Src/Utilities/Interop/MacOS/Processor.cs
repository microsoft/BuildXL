// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.InteropServices;

using static BuildXL.Interop.Dispatch;
using static BuildXL.Interop.MacOS.Constants;

namespace BuildXL.Interop.MacOS
{
    /// <summary>
    /// The Processor class offers interop calls for processor based tasks into operating system facilities
    /// </summary>
    public static class Processor
    {
        #pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
        [StructLayout(LayoutKind.Sequential)]
        public struct CpuLoadInfo
        {
            public ulong SystemTime;
            public ulong UserTime;
            public ulong IdleTime;
        }
        #pragma warning restore CS1591 // Missing XML comment for publicly visible type or member

        /// <summary>
        /// Returns the current CPU load info accross all CPU cores to the caller
        /// </summary>
        /// <param name="buffer">A CpuLoadInfo struct to hold the timing inforamtion of the current host CPU</param>
        public static int GetCpuLoadInfo(ref CpuLoadInfo buffer) => IsMacOS
            ? Impl_Mac.GetCpuLoadInfo(ref buffer, Marshal.SizeOf(buffer))
            : Impl_Linux.GetCpuLoadInfo(ref buffer, Marshal.SizeOf(buffer));
    }
}
