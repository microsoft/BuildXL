// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.InteropServices;
using static BuildXL.Interop.Dispatch;

namespace BuildXL.Interop.Unix
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
            /// <summary>Total usable main memory size in bytes</summary>
            public ulong TotalBytes;

            /// <summary>Available main memory size in bytes</summary>
            public ulong FreeBytes;
        }

        /// <summary>
        /// PressureLevel models the possible VM memory pressure levels on macOS
        /// See: https://developer.apple.com/documentation/dispatch/dispatch_source_memorypressure_flags_t
        /// </summary>
        public enum PressureLevel : int
        {
            #pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
            Normal = 1,
            Warning = 2,
            Critical = 4
            #pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
        }

        /// <summary>
        /// Returns the current host memory usage information to the caller
        /// </summary>
        /// <param name="buffer">A RamUsageInfo struct pointer to hold memory statistics</param>
        public static int GetRamUsageInfo(ref RamUsageInfo buffer) => IsMacOS
            ? Impl_Mac.GetRamUsageInfo(ref buffer)
            : Impl_Linux.GetRamUsageInfo(ref buffer);

        /// <summary>
        /// Returns the current memory pressure level of the VM
        /// </summary>
        /// <param name="level">A PressureLevel pointer to hold the current VM memory pressure level</param>
        public static int GetMemoryPressureLevel(ref PressureLevel level) => IsMacOS
            ? Impl_Mac.GetMemoryPressureLevel(ref level)
            : Impl_Linux.GetMemoryPressureLevel(ref level);
    }
}
