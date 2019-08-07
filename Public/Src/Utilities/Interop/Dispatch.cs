// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Interop.Windows;

#if NET_CORE
using System.Runtime.InteropServices;
#endif

#if FEATURE_SAFE_PROCESS_HANDLE
using Microsoft.Win32.SafeHandles;
#endif

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
#if NET_CORE
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
        /// Gets the elevated status of the process.
        /// </summary>
        /// <returns>True if process is running elevated, otherwise false.</returns>
        public static bool IsElevated()
        {
            switch (s_currentOS)
            {
                case OperatingSystem.MacOS:
                    return MacOS.Process.IsElevated();
                case OperatingSystem.Win:
                    return Windows.Process.IsElevated();
                default:
                    throw new PlatformNotSupportedException();
            }
        }

        /// <summary>
        /// Returns total processor time for a given process.  The process must be running or else an exception is thrown.
        /// </summary>
        public static TimeSpan TotalProcessorTime(System.Diagnostics.Process proc)
        {
            switch (s_currentOS)
            {
                case OperatingSystem.MacOS:
                {
                    var buffer = new MacOS.Process.ProcessTimesInfo();
                    MacOS.Process.GetProcessTimes(proc.Id, ref buffer, includeChildProcesses: false);
                    long ticks = (long)(buffer.SystemTimeNs + buffer.UserTimeNs) / 100;
                    return new TimeSpan(ticks);
                }

                default:
                {
                    return proc.TotalProcessorTime;
                }
            }
        }

        /// <summary>
        /// Returns the peak memory usage (in bytes) of a specific process
        /// </summary>
        /// <param name="handle">When calling from Windows the SafeProcessHandle is required</param>
        /// <param name="pid">On non-windows systems a process id has to be provided</param>
        public static ulong? GetActivePeakMemoryUsage(IntPtr handle, int pid)
        {
            switch (s_currentOS)
            {
                case OperatingSystem.MacOS:
                    {
                        ulong peakMemoryUsage = 0;
                        if (MacOS.Memory.GetPeakWorkingSetSize(pid, ref peakMemoryUsage) == MACOS_INTEROP_SUCCESS)
                        {
                            return peakMemoryUsage;
                        }

                        return null;
                    }
                default:
                    {
                        Process.PROCESSMEMORYCOUNTERSEX processMemoryCounters = new Windows.Process.PROCESSMEMORYCOUNTERSEX();

                        if (Process.GetProcessMemoryInfo(handle, processMemoryCounters, processMemoryCounters.cb))
                        {
                            return processMemoryCounters.PeakWorkingSetSize;
                        }

                        return null;
                    }
            }
        }
    }
}
