// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.InteropServices;
using System.Text;
using static BuildXL.Interop.Dispatch;
using static BuildXL.Interop.Unix.Constants;
using static BuildXL.Interop.Unix.Impl_Common;

namespace BuildXL.Interop.Unix
{
    /// <summary>
    /// The Process class offers interop calls for process based tasks into operating system facilities
    /// </summary>
    public static class Process
    {
        /// <nodoc />
        [StructLayout(LayoutKind.Sequential)]
        public struct ProcessResourceUsage
        {
            /// <nodoc />
            public double StartTime;

            /// <nodoc />
            public double ExitTime;

            /// <summary>
            /// System time of a given process in nanoseconds.
            /// </summary>
            public ulong SystemTimeNs;

            /// <summary>
            /// User time of a given process in nanoseconds.
            /// </summary>
            public ulong UserTimeNs;

            /// <summary>
            /// Bytes read from disk
            /// </summary>
            public ulong DiskBytesRead;

            /// <summary>
            /// Bytes written to disk
            /// </summary>
            public ulong DiskBytesWritten;
        }

        /// <summary>
        /// Returns process resource usage information to the caller
        /// </summary>
        /// <param name="pid">The process id to check</param>
        /// <param name="buffer">A ProcessResourceUsage struct to hold the process resource information</param>
        /// <param name="includeChildProcesses">Whether the result should include the execution times of all the child processes</param>
        public static int GetProcessResourceUsage(int pid, ref ProcessResourceUsage buffer, bool includeChildProcesses) => IsMacOS
            ? Impl_Mac.GetProcessResourceUsage(pid, ref buffer, Marshal.SizeOf(buffer), includeChildProcesses)
            : ERROR;

        /// <summary>
        /// Returns true if core dump file creation for abnormal process exits has been set up successfully, and passes out
        /// the path where the system writes core dump files.
        /// </summary>
        /// <param name="logsDirectory">The logs directory</param>
        /// <param name="buffer">A buffer to hold the core dump file directory</param>
        /// <param name="length">The buffer length</param>
        [DllImport(Libraries.BuildXLInteropLibMacOS)]
        public static extern bool SetupProcessDumps(string logsDirectory, StringBuilder buffer, long length);

        /// <summary>
        /// Cleans up the core dump facilities created by calling <see cref="SetupProcessDumps(string, StringBuilder, long)"/>
        /// </summary>
        [DllImport(Libraries.BuildXLInteropLibMacOS)]
        public static extern void TeardownProcessDumps();

        /// <inheritdoc />
        public static bool IsElevated()
        {
            return geteuid() == 0;
        }
    }
}
