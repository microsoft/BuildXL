// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.InteropServices;

namespace BuildXL.Interop.MacOS
{
    /// <summary>
    /// The Process class offers interop calls for process based tasks into operating system facilities
    /// </summary>
    public unsafe static class Process
    {        
        /// <nodoc />
        [StructLayout(LayoutKind.Sequential)]
        public unsafe struct ProcessTimesInfo
        {
            /// <nodoc />
            public double StartTime;

            /// <nodoc />
            public double ExitTime;

            /// <nodoc />
            public ulong SystemTime;

            /// <nodoc />
            public ulong UserTime;
        }

        /// <summary>
        /// Returns process timing information to the caller
        /// </summary>
        /// <param name="pid">The process id to check</param>
        /// <param name="buffer">A ProcesstTimesInfo struct to hold the process timing information</param>
        /// <returns></returns>
        [DllImport(BuildXL.Interop.Libraries.InteropLibMacOS)]
        public static extern int GetProcessTimes(int pid, ProcessTimesInfo *buffer);

        /// <summary>
        /// Returns a process peak working set size to the caller
        /// </summary>
        /// <param name="pid">The process id to check</param>
        /// <param name="buffer">A long pointer to hold the process peak memory usage</param>
        /// <returns></returns>
        [DllImport(BuildXL.Interop.Libraries.InteropLibMacOS)]
        public static extern int GetPeakWorkingSetSize(int pid, ulong *buffer);

        /// <nodoc />
        [DllImport("libc", SetLastError = true)]
        private static extern unsafe uint geteuid();

        /// <inheritdoc />
        public static bool IsElevated()
        {
            return geteuid() == 0;
        }
    }
}
