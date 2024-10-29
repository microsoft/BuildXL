// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using BuildXL.Native.Processes;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Instrumentation.Common;
using Microsoft.Win32.SafeHandles;
#if !FEATURE_SAFE_PROCESS_HANDLE
using SafeProcessHandle = BuildXL.Interop.Windows.SafeProcessHandle;
#endif

#nullable enable

namespace BuildXL.Processes
{
    /// <summary>
    /// Obtain the surviving processes from a JobObject and dump process if required.
    /// </summary>
    public class JobObjectProcessDumper
    {
        private const int MaxProcessPathLength = 1024;

        /// <summary>
        /// Attempts to obtain a collection of surviving processes and dump all processes in a process tree if required. Any files existing in the dump directory will be deleted.
        /// </summary>
        public static Dictionary<uint, ReportedProcess>? GetAndOptionallyDumpProcesses(
            JobObject jobObject,
            LoggingContext loggingContext,
            string? survivingPipProcessDumpDirectory,
            bool dumpProcess,
            string[] excludedDumpProcessNames,
            out Exception? dumpException)
        {
            dumpException = null;

            if (OperatingSystemHelper.IsMacOS || OperatingSystemHelper.IsLinuxOS)
            {
                dumpException = new PlatformNotSupportedException();
                return null;
            }

            if (!jobObject.TryGetProcessIds(loggingContext, out uint[]? survivingChildProcessIds) || survivingChildProcessIds!.Length == 0)
            {
                dumpException = new BuildXLException("Could not enumerate the child process tree. Fail the entire operation");
                return null;
            }

            var survivingChildProcesses = new Dictionary<uint, ReportedProcess>();
            foreach (uint processId in survivingChildProcessIds)
            {
                using (SafeProcessHandle processHandle = ProcessUtilities.OpenProcess(
                    ProcessSecurityAndAccessRights.PROCESS_QUERY_INFORMATION |
                    ProcessSecurityAndAccessRights.PROCESS_VM_READ,
                    false,
                    processId))
                {
                    if (!jobObject.ContainsProcess(processHandle))
                    {
                        // We are too late: process handle is invalid because it closed already,
                        // or process id got reused by another process.
                        continue;
                    }

                    if (!ProcessUtilities.GetExitCodeProcess(processHandle, out int exitCode))
                    {
                        // we are too late: process id got reused by another process
                        continue;
                    }

                    using (PooledObjectWrapper<StringBuilder> wrap = Pools.GetStringBuilder())
                    {
                        StringBuilder sb = wrap.Instance;
                        if (sb.Capacity < MaxProcessPathLength)
                        {
                            sb.Capacity = MaxProcessPathLength;
                        }

                        if (ProcessUtilities.GetModuleFileNameEx(processHandle, IntPtr.Zero, sb, (uint)sb.Capacity) <= 0)
                        {
                            // we are probably too late
                            continue;
                        }

                        // Attempt to read the process arguments (command line) from the process
                        // memory. This is not fatal if it does not succeed.
                        string processArgs = string.Empty;

                        var basicInfoSize = (uint)Marshal.SizeOf<Native.Processes.Windows.ProcessUtilitiesWin.PROCESS_BASIC_INFORMATION>();
                        var basicInfoPtr = Marshal.AllocHGlobal((int)basicInfoSize);
                        uint basicInfoReadLen;
                        uint survivingChildParentProcessId = 0;
                        try
                        {
                            if (Native.Processes.Windows.ProcessUtilitiesWin.NtQueryInformationProcess(
                                processHandle,
                                Native.Processes.Windows.ProcessUtilitiesWin.ProcessInformationClass.ProcessBasicInformation,
                                basicInfoPtr, basicInfoSize, out basicInfoReadLen) == 0)
                            {
                                Native.Processes.Windows.ProcessUtilitiesWin.PROCESS_BASIC_INFORMATION basicInformation = Marshal.PtrToStructure<Native.Processes.Windows.ProcessUtilitiesWin.PROCESS_BASIC_INFORMATION>(basicInfoPtr);
                                Contract.Assert(basicInformation.UniqueProcessId == processId);

                                // Obtain the immediate ParentProcessId to preserve the tree structure(to use it for naming the dump file).
                                survivingChildParentProcessId = (uint)basicInformation.InheritedFromUniqueProcessId.ToInt32();
                                // NativeMethods.ReadProcessStructure and NativeMethods.ReadUnicodeString handle null\zero addresses
                                // passed into them. Since these are all value types, then there is no need to do any type
                                // of checking as passing zero through will just result in an empty process args string.
                                var peb = Native.Processes.Windows.ProcessUtilitiesWin.ReadProcessStructure<Native.Processes.Windows.ProcessUtilitiesWin.PEB>(processHandle, basicInformation.PebBaseAddress);
                                var processParameters = Native.Processes.Windows.ProcessUtilitiesWin.ReadProcessStructure<Native.Processes.Windows.ProcessUtilitiesWin.RTL_USER_PROCESS_PARAMETERS>(processHandle, peb.ProcessParameters);
                                processArgs = Native.Processes.Windows.ProcessUtilitiesWin.ReadProcessUnicodeString(processHandle, processParameters.CommandLine);
                            }
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(basicInfoPtr);
                        }

                        string path = sb.ToString();
                        var reportedProcess = new ReportedProcess(processId, path, processArgs) { ParentProcessId = survivingChildParentProcessId };
                        survivingChildProcesses.Add(processId, reportedProcess);
                        // Providing an option to not dump process if not required
                        if (dumpProcess && !excludedDumpProcessNames.Any(procName => string.Equals(Path.GetFileName(reportedProcess.Path), procName, OperatingSystemHelper.PathComparison)))
                        {
                            if (!string.IsNullOrEmpty(survivingPipProcessDumpDirectory))
                            {
                                DumpProcess(loggingContext, survivingPipProcessDumpDirectory!, processHandle, reportedProcess, out Exception? childDumpException);
                                dumpException ??= childDumpException;
                            }
                            else
                            {
                                Tracing.Logger.Log.DumpSurvivingPipProcessChildrenStatus(loggingContext, reportedProcess.ProcessId.ToString(), $"Failed due to missing dump directory.");
                                continue;
                            }
                        }
                    }
                }
            }
            return survivingChildProcesses;
        }

        private static void DumpProcess(LoggingContext loggingContext, string survivingPipProcessDumpDirectory, SafeHandle processHandle, ReportedProcess reportedProcess, out Exception? childDumpException)
        {
            childDumpException = null;

            var executableName =  Path.GetFileNameWithoutExtension(reportedProcess.Path);
            string dumpPath = Path.Combine(survivingPipProcessDumpDirectory, $"Dump_{reportedProcess.ParentProcessId}_{reportedProcess.ProcessId}_{executableName}.zip");
            if (!ProcessDumper.TryDumpProcess(processHandle, (int)reportedProcess.ProcessId, executableName, dumpPath, out Exception dumpException, compress: true))
            {
                Tracing.Logger.Log.DumpSurvivingPipProcessChildrenStatus(loggingContext, executableName, $"Failed with exception: {dumpException?.Message}");
                childDumpException = dumpException;
            }
            else
            {
                Tracing.Logger.Log.DumpSurvivingPipProcessChildrenStatus(loggingContext, executableName, $"Succeeded at path: {dumpPath}");
            }
        }
    }
}

