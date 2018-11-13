// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using BuildXL.Interop.Windows;
#if FEATURE_SAFE_PROCESS_HANDLE
using Microsoft.Win32.SafeHandles;
#endif

namespace BuildXL.Interop
{
    /// <summary>
    /// Static class with entry points for common platform interop calls into system facilities
    /// </summary>
    public static unsafe class Dispatch
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
#if FEATURE_CORECLR
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
        /// Gets the process times of a specific process
        /// </summary>
        /// <param name="handle">When calling from Windows the SafeProcessHandle is required</param>
        /// <param name="pid">On non-windows systems a process id has to be provided</param>
        /// <param name="creation">The out variable to report process start time</param>
        /// <param name="exit">The out variable to report process exit time</param>
        /// <param name="kernel">The out variable to report kernel time of the process</param>
        /// <param name="user">The out variable to report user time of the process</param>
        /// <returns></returns>
        public static bool GetProcessTimes(IntPtr handle, int pid, out long creation, out long exit, out long kernel, out long user)
        {
            switch (s_currentOS)
            {
                case OperatingSystem.MacOS:
                    {
                        creation = 0;
                        exit = 0;
                        kernel = 0;
                        user = 0;

                        MacOS.Process.ProcessTimesInfo processTimes = new MacOS.Process.ProcessTimesInfo();
                        if (MacOS.Process.GetProcessTimes(pid, &processTimes) == MACOS_INTEROP_SUCCESS)
                        {
                            var now = DateTime.UtcNow;

                            // The reported units will be negative seconds
                            creation = now.AddSeconds(processTimes.StartTime).ToLocalTime().Ticks;
                            exit = processTimes.ExitTime != 0 ? now.AddSeconds(processTimes.ExitTime).ToLocalTime().Ticks : now.Ticks;
                            kernel = (long)processTimes.SystemTime;
                            user = (long)processTimes.UserTime;

                            return true;
                        }

                        return false;
                    }
                default:
                    return Process.ExternGetProcessTimes(handle, out creation, out exit, out kernel, out user);
            }
        }

        /// <summary>
        /// Returns the peak memory usage of a specific process
        /// </summary>
        /// <param name="handle">When calling from Windows the SafeProcessHandle is required</param>
        /// <param name="pid">On non-windows systems a process id has to be provided</param>
        /// <returns></returns>
        public static ulong? GetActivePeakMemoryUsage(IntPtr handle, int pid)
        {
            switch (s_currentOS)
            {
                case OperatingSystem.MacOS:
                    {
                        ulong peakMemoryUsage = 0;
                        if (MacOS.Process.GetPeakWorkingSetSize(pid, &peakMemoryUsage) == MACOS_INTEROP_SUCCESS)
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
