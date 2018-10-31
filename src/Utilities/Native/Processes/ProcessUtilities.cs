// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using BuildXL.Utilities;
using SafeWaitHandle = Microsoft.Win32.SafeHandles.SafeWaitHandle;
using SafeFileHandle = Microsoft.Win32.SafeHandles.SafeFileHandle;

#if FEATURE_SAFE_PROCESS_HANDLE
using Microsoft.Win32.SafeHandles;
using ProcessHandle = System.Runtime.InteropServices.SafeHandle;
using ProcessPtr = Microsoft.Win32.SafeHandles.SafeProcessHandle;
#else
using ProcessHandle = System.Runtime.InteropServices.HandleRef;
using ProcessPtr = System.IntPtr;
using SafeProcessHandle = BuildXL.Interop.Windows.SafeProcessHandle;
#endif

namespace BuildXL.Native.Processes
{
    /// <summary>
    /// Static facade class for methods that potentially have to call into native.
    /// </summary>
    public static class ProcessUtilities
    {
        private static readonly IProcessUtilities s_nativeMethods = OperatingSystemHelper.IsUnixOS
            ? (IProcessUtilities) new Unix.ProcessUtilitiesUnix()
            : (IProcessUtilities) new Windows.ProcessUtilitiesWin();

        #region Structs and Constants
#pragma warning disable CS1591 // Missing XML comment

        public const int DUPLICATE_CLOSE_SOURCE = 1;
        public const int DUPLICATE_SAME_ACCESS = 2;
        public const int ERROR_BAD_EXE_FORMAT = 193;
        public const int ERROR_EXE_MACHINE_TYPE_MISMATCH = 216;
        public const int CREATE_NO_WINDOW = 0x08000000;
        public const int DETACHED_PROCESS = 0x00000008;
        public const int CREATE_SUSPENDED = 0x00000004;
        public const int CREATE_UNICODE_ENVIRONMENT = 0x00000400;
        public const int CREATE_DEFAULT_ERROR_MODE = 0x04000000;

        public const int HANDLE_FLAG_INHERIT = 1;
        public const int HANDLE_FLAG_PROTECT_FROM_CLOSE = 2;

        public const uint JOB_OBJECT_MSG_END_OF_JOB_TIME = 1;
        public const uint JOB_OBJECT_MSG_END_OF_PROCESS_TIME = 2;
        public const uint JOB_OBJECT_MSG_ACTIVE_PROCESS_LIMIT = 3;
        public const uint JOB_OBJECT_MSG_ACTIVE_PROCESS_ZERO = 4;
        public const uint JOB_OBJECT_MSG_NEW_PROCESS = 6;
        public const uint JOB_OBJECT_MSG_EXIT_PROCESS = 7;
        public const uint JOB_OBJECT_MSG_ABNORMAL_EXIT_PROCESS = 8;
        public const uint JOB_OBJECT_MSG_PROCESS_MEMORY_LIMIT = 9;
        public const uint JOB_OBJECT_MSG_JOB_MEMORY_LIMIT = 10;

        public const int ERROR_ABANDONED_WAIT_0 = 735;

        public const uint INFINITY = 0xFFFFFFFF;
#pragma warning restore CS1591 // Missing XML comment
        #endregion

        /// <summary>
        /// Computes a normalized path representation and a stable hash value for a string representing a path.
        ///
        /// The normalized path is returned as a byte array representing the encoding of the path in the native world.
        /// The return value is the hash code used in the native world.
        /// 
        /// @requires path != null
        /// </summary>
        public static int NormalizeAndHashPath(string path, out byte[] bytes)
            => s_nativeMethods.NormalizeAndHashPath(path, out bytes);

        /// <summary>
        /// Returns if two native buffers are equal up to a given number of elements.
        /// 
        /// @requires buffer1 != null
        /// @requires buffer2 != null
        /// </summary>
        public static bool AreBuffersEqual(byte[] buffer1, byte[] buffer2)
            => s_nativeMethods.AreBuffersEqual(buffer1, buffer2);

        /// <summary>
        /// Returns if the native library is in debug configuration.
        /// </summary>
        public static bool IsNativeInDebugConfiguration()
            => s_nativeMethods.IsNativeInDebugConfiguration();

        /// <summary>
        /// Terminates the specified process and all of its threads.
        /// </summary>
        public static bool TerminateProcess(SafeProcessHandle hProcess, int exitCode)
            => s_nativeMethods.TerminateProcess(hProcess, exitCode);

        /// <summary>
        /// Returns the exit code of a process identified by a handle.
        /// </summary>
        public static bool GetExitCodeProcess(SafeProcessHandle hProcess, out int exitCode)
            => s_nativeMethods.GetExitCodeProcess(hProcess, out exitCode);

        /// <nodoc />
        public static bool SetHandleInformation(SafeHandle hObject, int dwMask, int dwFlags)
            => s_nativeMethods.SetHandleInformation(hObject, dwMask, dwFlags);

        /// <nodoc />
        public static IntPtr GetCurrentProcess()
            => s_nativeMethods.GetCurrentProcess();

        /// <nodoc />
        public static bool IsWow64Process(SafeProcessHandle process = null)
            => s_nativeMethods.IsWow64Process(process);

        /// <nodoc />
        public static bool GetProcessTimes(IntPtr handle, out long creation, out long exit, out long kernel, out long user)
            => s_nativeMethods.GetProcessTimes(handle, out creation, out exit, out kernel, out user);

        /// <nodoc />
        public static bool DuplicateHandle(ProcessHandle hSourceProcessHandle, SafeHandle hSourceHandle, ProcessHandle hTargetProcess, out SafeWaitHandle targetHandle, int dwDesiredAccess, bool bInheritHandle, int dwOptions)
            => s_nativeMethods.DuplicateHandle(hSourceProcessHandle, hSourceHandle, hTargetProcess, out targetHandle, dwDesiredAccess, bInheritHandle, dwOptions);

        /// <summary>
        /// Opens an existing local process object.
        /// </summary>
        /// <inheritdoc />
        public static SafeProcessHandle OpenProcess(ProcessSecurityAndAccessRights dwDesiredAccess, bool bInheritHandle, uint dwProcessId)
            => s_nativeMethods.OpenProcess(dwDesiredAccess, bInheritHandle, dwProcessId);

        /// <summary>
        /// Determines whether the process is running in the specified job.
        /// </summary>
        public static bool IsProcessInJob(SafeProcessHandle hProcess, IntPtr hJob, out bool result)
            => s_nativeMethods.IsProcessInJob(hProcess, hJob, out result);

        /// <summary>
        /// Retrieves the fully qualified path for the file containing the specified module.
        /// </summary>
        public static uint GetModuleFileNameEx(SafeProcessHandle hProcess, IntPtr hModule, StringBuilder lpBaseName, uint nSize)
            => s_nativeMethods.GetModuleFileNameEx(hProcess, hModule, lpBaseName, nSize);

        /// <summary>
        /// Assigns a process to an existing job object.
        /// </summary>
        /// <param name="hJob">A handle to the job object to which the process will be associated. </param>
        /// <param name="hProcess">A handle to the process to associate with the job object</param>
        /// <returns>If the function succeeds, the return value is nonzero.</returns>
        public static bool AssignProcessToJobObject(IntPtr hJob, ProcessPtr hProcess)
            => s_nativeMethods.AssignProcessToJobObject(hJob, hProcess);

        /// <nodoc />
        public static IProcessInjector CreateProcessInjector(Guid payloadGuid, SafeHandle remoteInjectorPipe, SafeHandle reportPipe, string dllX86, string dllX64, ArraySegment<byte> payload)
            => s_nativeMethods.CreateProcessInjector(payloadGuid, remoteInjectorPipe, reportPipe, dllX86, dllX64, payload);

        /// <summary>
        /// Serialize an environment variable dictionary to the form expected by CreateProcess.
        /// Note that you must provide CREATE_UNICODE_ENVIRONMENT to CreateProcess.
        /// If a null dictionary is provided, returns null.
        /// </summary>
        public static byte[] SerializeEnvironmentBlock(IReadOnlyDictionary<string, string> environmentVariables)
            => s_nativeMethods.SerializeEnvironmentBlock(environmentVariables);

        /// <nodoc />
        public static CreateDetouredProcessStatus CreateDetouredProcess(
            string lpcwCommandLine,
            int dwCreationFlags,
            IntPtr lpEnvironment,
            string lpcwWorkingDirectory,
            SafeHandle hStdInput,
            SafeHandle hStdOutput,
            SafeHandle hStdError,
            SafeHandle hJob,
            IProcessInjector injector,
            out SafeProcessHandle phProcess,
            out SafeThreadHandle phThread,
            out int pdwProcessId,
            out int errorCode)
        {
            return s_nativeMethods.CreateDetouredProcess(
                lpcwCommandLine,
                dwCreationFlags,
                lpEnvironment,
                lpcwWorkingDirectory,
                hStdInput,
                hStdOutput,
                hStdError,
                hJob,
                injector,
                addProcessToContainer: false,
                out phProcess,
                out phThread,
                out pdwProcessId,
                out errorCode);
        }

        /// <summary>
        /// This is a CreateProcess wrapper suitable for spawning off long-lived server processes.
        /// In particular:
        /// - The new process does not inherit any handles (TODO: If needed, one could allow explicit handle inheritance here).
        /// - The new process is detached from the current job, if any (CREATE_BREAKAWAY_FROM_JOB)
        ///   (note that process creation fails if breakwaway is not allowed).
        /// - The new process gets a new (invisible) console (CREATE_NO_WINDOW).
        /// Note that lpEnvironment is assumed to be a unicode environment block.
        /// </summary>
        public static CreateDetachedProcessStatus CreateDetachedProcess(string commandLine, IReadOnlyDictionary<string, string> environmentVariables, string workingDirectory, out int newProcessId, out int errorCode)
            => s_nativeMethods.CreateDetachedProcess(commandLine, environmentVariables, workingDirectory, out newProcessId, out errorCode);

        /// <nodoc />
        public static SafeFileHandle CreateNamedPipe(string lpName, PipeOpenMode dwOpenMode, PipeMode dwPipeMode, int nMaxInstances, int nOutBufferSize, int nInBufferSize, int nDefaultTimeout, IntPtr lpSecurityAttributes)
            => s_nativeMethods.CreateNamedPipe(lpName, dwOpenMode, dwPipeMode, nMaxInstances, nOutBufferSize, nInBufferSize, nDefaultTimeout, lpSecurityAttributes);

        /// <nodoc />
        public static bool ApplyDriveMappings(PathMapping[] mappings)
            => s_nativeMethods.ApplyDriveMappings(mappings);

        /// <summary>
        /// Creates or opens a job object.
        /// </summary>
        /// <param name="lpJobAttributes">
        /// A pointer to a SECURITY_ATTRIBUTES structure that specifies the security descriptor for
        /// the job object and determines whether child processes can inherit the returned handle
        /// </param>
        /// <param name="lpName">The name of the job</param>
        /// <returns>If the function succeeds, the return value is a handle to the job object</returns>
        public static IntPtr CreateJobObject(IntPtr lpJobAttributes, string lpName)
            => s_nativeMethods.CreateJobObject(lpJobAttributes, lpName);

        /// <summary>
        /// Retrieves limit and job state information from the job object.
        /// </summary>
        /// <param name="hJob">A handle to the job whose information is being queried</param>
        /// <param name="JobObjectInformationClass">The information class for the limits to be queried</param>
        /// <param name="lpJobObjectInfo">The limit or job state information</param>
        /// <param name="cbJobObjectInfoLength">The count of the job information being queried, in bytes</param>
        /// <param name="lpReturnLength">
        /// A pointer to a variable that receives the length of data written to the structure pointed
        /// to by the lpJobObjectInfo parameter
        /// </param>
        /// <returns>If the function succeeds, the return value is nonzero.</returns>
        public static unsafe bool QueryInformationJobObject(IntPtr hJob, JOBOBJECTINFOCLASS JobObjectInformationClass, void* lpJobObjectInfo, uint cbJobObjectInfoLength, out uint lpReturnLength)
            => s_nativeMethods.QueryInformationJobObject(hJob, JobObjectInformationClass, lpJobObjectInfo, cbJobObjectInfoLength, out lpReturnLength);

        /// <summary>
        /// Sets limits for a job object.
        /// </summary>
        /// <param name="hJob">A handle to the job whose limits are being set. </param>
        /// <param name="JobObjectInfoClass">The information class for the limits to be set. </param>
        /// <param name="lpJobObjectInfo">The limits or job state to be set for the job</param>
        /// <param name="cbJobObjectInfoLength">The size of the job information being set, in bytes.</param>
        /// <returns>If the function succeeds, the return value is nonzero</returns>
        public static unsafe bool SetInformationJobObject(IntPtr hJob, JOBOBJECTINFOCLASS JobObjectInfoClass, void* lpJobObjectInfo, uint cbJobObjectInfoLength)
            => s_nativeMethods.SetInformationJobObject(hJob, JobObjectInfoClass, lpJobObjectInfo, cbJobObjectInfoLength);

        /// <summary>
        /// Terminates all processes in an existing job object.
        /// </summary>
        /// <param name="hJob">
        /// A handle to the job whose processes will be terminated. The CreateJobObject or OpenJobObject
        /// function returns this handle. This handle must have the JOB_OBJECT_TERMINATE access right. The handle for each process
        /// in the job object must have the PROCESS_TERMINATE access right.
        /// </param>
        /// <param name="exitCode">
        /// The exit code to be used by all processes and threads in the job object. Use the
        /// GetExitCodeProcess function to retrieve each process's exit value. Use the GetExitCodeThread function to retrieve each
        /// thread's exit value.
        /// </param>
        /// <returns>If the function succeeds, the return value is nonzero.</returns>
        public static bool TerminateJobObject(IntPtr hJob, int exitCode)
            => s_nativeMethods.TerminateJobObject(hJob, exitCode);

        /// <summary>
        /// Returns whether the operating system supports nested jobs.
        /// </summary>
        public static bool OSSupportsNestedJobs()
            => s_nativeMethods.OSSupportsNestedJobs();
    }

    /// <summary>
    /// Extensions for <see cref="CreateDetouredProcessStatus" />.
    /// </summary>
    public static class CreateDetouredProcessStatusExtensions
    {
        /// <summary>
        /// Indicates if this status is specific to injecting detours / sandboxing; Detours and sandboxing-specific failures
        /// merit an 'internal error' indication rather than suggesting user fault.
        /// </summary>
        public static bool IsDetoursSpecific(this CreateDetouredProcessStatus status)
        {
            return status != CreateDetouredProcessStatus.Succeeded && status != CreateDetouredProcessStatus.ProcessCreationFailed;
        }
    }
}
