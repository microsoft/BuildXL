// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
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
        public static Dictionary<uint, ReportedProcess>? GetAndOptionallyDumpProcesses(JobObject jobObject, LoggingContext loggingContext, string? survivingPipProcessDumpDirectory, bool dumpProcess, out Exception? dumpException)
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
                        if (dumpProcess)
                        {
                            if (!string.IsNullOrEmpty(survivingPipProcessDumpDirectory))
                            {
                                DumpProcess(loggingContext, survivingPipProcessDumpDirectory!, reportedProcess, out Exception? childDumpException);
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

        private static void DumpProcess(LoggingContext loggingContext, string survivingPipProcessDumpDirectory, ReportedProcess reportedProcess, out Exception? childDumpException)
        {
            childDumpException = null;

            if (TryGetProcessById((int)reportedProcess.ProcessId, out var processToBeDumped, loggingContext, out Exception? getProcessIdException))
            {
                string dumpPath = System.IO.Path.Combine(survivingPipProcessDumpDirectory, $"Dump_{reportedProcess.ParentProcessId}_{reportedProcess.ProcessId}_{processToBeDumped?.ProcessName}.zip");
                if (!ProcessDumper.TryDumpProcess(processToBeDumped!, dumpPath, out Exception dumpException, compress: true))
                {
                    Tracing.Logger.Log.DumpSurvivingPipProcessChildrenStatus(loggingContext, processToBeDumped!.ProcessName, $"Failed with exception: {dumpException?.Message}");
                    getProcessIdException = dumpException;
                }
                else
                {
                    Tracing.Logger.Log.DumpSurvivingPipProcessChildrenStatus(loggingContext, processToBeDumped!.ProcessName, $"Succeeded at path: {dumpPath}");
                }
            }

            childDumpException ??= getProcessIdException;
        }

        private static bool TryGetProcessById(int pid, out System.Diagnostics.Process? process, LoggingContext loggingContext, out Exception? childDumpException)
        {
            process = null;
            childDumpException = null;
            try
            {
                process = System.Diagnostics.Process.GetProcessById(pid);
                // Process.GetProcessById returns an object that is not fully initialized. Instead, the fields are populated the first time they are accessed.
                // Because of that if a process exits before the object is initialized, reading any of the properties will result in an InvalidOperationException
                // exception. Force initialization be querying the process name. Local function is used here just to make sure that the compiler does not
                // optimize away the property access.
                doNothing(process.ProcessName);
                return true;
            }
            catch (ArgumentException aEx)
            {
                Tracing.Logger.Log.DumpSurvivingPipProcessChildrenStatus(loggingContext, pid.ToString(), $"Failed with Exception: {aEx.Message}");
                childDumpException = aEx;
            }
            catch (InvalidOperationException ioEx)
            {
                Tracing.Logger.Log.DumpSurvivingPipProcessChildrenStatus(loggingContext, pid.ToString(), $"Failed with Exception: {ioEx.Message}");
                childDumpException = ioEx;
            }
            return false;

            void doNothing(string _)
            {
                // no-op
            }
        }
    }
}

