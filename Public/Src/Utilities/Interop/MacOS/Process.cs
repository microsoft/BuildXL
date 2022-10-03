// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
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
        /// <summary>
        /// Code sync with ProcessResourceUsage defined in 'process.h' for Unix
        /// </summary>
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
            /// Number of read operations performed.
            /// </summary>
            public ulong DiskReadOps;

            /// <summary>
            /// Bytes read from disk
            /// </summary>
            public ulong DiskBytesRead;

            /// <summary>
            /// Number of write operations performed.
            /// </summary>
            public ulong DiskWriteOps;

            /// <summary>
            /// Bytes written to disk
            /// </summary>
            public ulong DiskBytesWritten;

            /// <summary>
            /// Value of the resident set size, representing the amount of physical memory used by a process right now.
            /// </summary>
            public ulong WorkingSetSize;

            /// <summary>
            /// Peak value of the resident set size, representing the maximum amount of physical memory used by a process at any time.
            /// </summary>
            public ulong PeakWorkingSetSize;

            /// <summary>
            /// The process name.
            /// </summary>
            public string Name;

            /// <summary>
            /// The process id this usage information belongs to.
            /// </summary>
            public int ProcessId;
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
        /// Populates a process resource usage information buffer with memory usage information only.
        /// </summary>
        /// <param name="pid">The process id to check</param>
        /// <param name="buffer">A ProcessResourceUsage struct to hold the memory usage information</param>
        /// <param name="includeChildProcesses">Whether the result should include the usage numbers of all the child processes</param>
        public static int GetProcessMemoryUsage(int pid, ref ProcessResourceUsage buffer, bool includeChildProcesses) => IsMacOS
            ? Impl_Mac.GetProcessResourceUsageSnapshot(pid, ref buffer, Marshal.SizeOf(buffer), includeChildProcesses)
            : Impl_Linux.GetProcessMemoryUsageSnapshot(pid, ref buffer, Marshal.SizeOf(buffer), includeChildProcesses);

        /// <summary>
        /// Returns a collection of process resource usage information for a process tree rooted at the given process id, snapshotting the complete process tree at the time of the call.
        /// </summary>
        /// <param name="pid">The process id of the root of the tree</param>
        public static IEnumerable<ProcessResourceUsage?> GetResourceUsageForProcessTree(int pid) => IsMacOS
            ? throw new NotImplementedException()
            : Impl_Linux.GetResourceUsageForProcessTree(pid, includeChildren: true);

        /// <summary>
        /// Retrieve the (immediate) child processes of the given process
        /// </summary>
        public static IEnumerable<int> GetChildProcesses(int processId) => IsMacOS
            ? throw new NotImplementedException()
            : Impl_Linux.GetChildProcesses(processId);

        /// <summary>
        /// Returns true if core dump file creation for abnormal process exits has been set up successfully, and passes out
        /// the path where the system writes core dump files.
        /// </summary>
        /// <param name="logsDirectory">The logs directory</param>
        /// <param name="buffer">A buffer to hold the core dump file directory</param>
        /// <param name="length">The buffer length</param>
        /// <param name="error">Detailed error</param>
        public static bool SetupProcessDumps(string logsDirectory, StringBuilder buffer, long length, out string error)
        {
            error = string.Empty;
            return IsMacOS ? Impl_Mac.SetupProcessDumps(logsDirectory, buffer, length) : true;
        }
            
        /// <summary>
        /// Cleans up the core dump facilities created by calling <see cref="SetupProcessDumps(string, StringBuilder, long, out string)"/>
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
