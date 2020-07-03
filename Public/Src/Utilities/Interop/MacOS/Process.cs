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

        /// <remarks>
        /// Implemented by calling "kill -0".
        /// This should be much cheaper than calling Process.GetProcessById().
        /// </remarks>
        public static bool IsAlive(int pid) => Impl_Common.kill(pid, 0) == 0;

        /// <remarks>
        /// Implemented by sending SIGKILL to a process with id <paramref name="pid"/>.
        /// The return value indicates whether the KILL signal was sent successfully.
        /// </remarks>
        public static bool ForceQuit(int pid) => Impl_Common.kill(pid, 9) == 0;

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
        public static bool SetupProcessDumps(string logsDirectory, StringBuilder buffer, long length) => IsMacOS
            ? Impl_Mac.SetupProcessDumps(logsDirectory, buffer, length)
            : false;

        /// <summary>
        /// Cleans up the core dump facilities created by calling <see cref="SetupProcessDumps(string, StringBuilder, long)"/>
        /// </summary>
        public static void TeardownProcessDumps()
        {
            if (IsMacOS)
            {
                Impl_Mac.TeardownProcessDumps();
            }
        }

        /// <inheritdoc />
        public static bool IsElevated()
        {
            return geteuid() == 0;
        }
    }
}
