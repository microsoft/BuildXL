// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Runtime.InteropServices;
using BuildXL.Interop.Unix;

using static BuildXL.Interop.Unix.Process;

namespace BuildXL.Interop
{
    /// <summary>
    /// Static class with entry points for common platform interop calls into system facilities
    /// </summary>
    public static class Dispatch
    {
        private static readonly OperatingSystem s_currentOS = CurrentOS();

        /// <summary>
        /// Error code for indicating a successful result from an interop call on macOS
        /// </summary>
        public static int MACOS_INTEROP_SUCCESS = 0x0;

        /// <summary>
        /// Indicates the currently running operating system of the host machine.
        /// </summary>
        public static OperatingSystem CurrentOS()
        {
#if NETCOREAPP
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return OperatingSystem.Unix;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return OperatingSystem.MacOS;
            }
#endif
            return OperatingSystem.Win;
        }

        /// <summary>
        /// Returns true when executing on OSX.
        /// </summary>
        public static readonly bool IsMacOS = CurrentOS() == OperatingSystem.MacOS;

        /// <summary>
        /// Returns true when executing on Windows.
        /// </summary>
        public static readonly bool IsWinOS = CurrentOS() == OperatingSystem.Win;

        /// <summary>
        /// Gets the elevated status of the process.
        /// </summary>
        /// <returns>True if process is running elevated, otherwise false.</returns>
        public static bool IsElevated() => IsWinOS
            ? Windows.Process.IsElevated()
            : Unix.Process.IsElevated();

        /// <summary>
        /// Checks if a process with id <paramref name="pid"/> exists.
        /// </summary>
        /// <param name="pid">ID of the process to check</param>
        public static bool IsProcessAlive(int pid) => IsWinOS
            ? Windows.Process.IsAlive(pid)
            : Unix.Process.IsAlive(pid);

        /// <summary>
        /// Forcefully terminates a process with id <paramref name="pid"/>.
        /// The return value indicates success.
        /// </summary>
        /// <param name="pid">ID of the process to kill</param>
        public static bool ForceQuit(int pid) => IsWinOS
            ? Windows.Process.ForceQuit(pid)
            : Unix.Process.ForceQuit(pid);

        /// <summary>
        /// Forcefully terminates this process.
        /// </summary>
        public static void ForceQuit() => ForceQuit(System.Diagnostics.Process.GetCurrentProcess().Id);

        /// <summary>
        /// Returns the memory counters of a specific process only when running on Windows,
        /// otherwise counters for a whole processes tree rooted at the given process id.
        /// </summary>
        /// <param name="handle">When calling from Windows the SafeProcessHandle is required</param>
        /// <param name="pid">On non-windows systems a process id has to be provided</param>
        public static ProcessMemoryCountersSnapshot? GetMemoryCountersSnapshot(IntPtr handle, int pid)
        {
            switch (s_currentOS)
            {
                case OperatingSystem.Win:
                    {
                        var counters = Windows.Memory.GetMemoryUsageCounters(handle);
                        if (counters != null)
                        {
                            return ProcessMemoryCountersSnapshot.CreateFromBytes(
                                counters.PeakWorkingSetSize,
                                counters.WorkingSetSize,
                                (counters.WorkingSetSize + counters.PeakWorkingSetSize) / 2,
                                counters.PeakPagefileUsage,
                                counters.PagefileUsage);
                        }

                        return null;
                    }
                default:
                    {
                        var usage = new ProcessResourceUsage();
                        if (Unix.Process.GetProcessMemoryUsage(pid, ref usage, includeChildProcesses: true) == MACOS_INTEROP_SUCCESS)
                        {
                            return ProcessMemoryCountersSnapshot.CreateFromBytes(
                                usage.PeakWorkingSetSize,
                                usage.WorkingSetSize,
                                (usage.WorkingSetSize + usage.PeakWorkingSetSize) / 2,
                                peakCommitSize: 0,
                                lastCommitSize: 0);
                        }

                        return null;
                    }
            }
        }
    }
}
