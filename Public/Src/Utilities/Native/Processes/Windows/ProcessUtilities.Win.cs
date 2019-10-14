// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using BuildXL.Interop.Windows;
using BuildXL.Interop;
using BuildXL.Utilities;
using Microsoft.Win32.SafeHandles;
#if FEATURE_SAFE_PROCESS_HANDLE
using ProcessHandle = System.Runtime.InteropServices.SafeHandle;
using ProcessPtr = Microsoft.Win32.SafeHandles.SafeProcessHandle;
#else
using SafeProcessHandle = BuildXL.Interop.Windows.SafeProcessHandle;
using ProcessHandle = System.Runtime.InteropServices.HandleRef;
using ProcessPtr = System.IntPtr;
#endif

namespace BuildXL.Native.Processes.Windows
{
    /// <summary>
    /// Implementation of native methods for Windows.
    /// </summary>
    public unsafe partial class ProcessUtilitiesWin : IProcessUtilities
    {
        /// <inheritdoc />
        public int NormalizeAndHashPath(string path, out byte[] normalizedPathBytes)
        {
            Assert64Process();

            // in the native Windows world strings are represented as null-terminated words, hence the length is (path.Length + 1) * 2
            normalizedPathBytes = new byte[(path.Length + 1) * sizeof(char)];

            fixed (byte* p = normalizedPathBytes)
            {
                return ExternNormalizeAndHashPath(path, p, normalizedPathBytes.Length);
            }
        }

        /// <inheritdoc />
        /// <remarks>
        /// It is not clear why we call into native to compare the content of 2 byte arrays (using 'memcmp') instead of doing it right here.
        /// </remarks>
        public bool AreBuffersEqual(byte[] buffer1, byte[] buffer2)
        {
            Contract.Requires(buffer1 != null);
            Contract.Requires(buffer2 != null);
            Assert64Process();

            if (buffer1.Length != buffer2.Length)
            {
                return false;
            }

            fixed (byte* p = buffer1)
            fixed (byte* q = buffer2)
            {
                return ExternAreBuffersEqual(p, q, buffer1.Length);
            }
        }

        /// <inheritdoc />
        public bool IsNativeInDebugConfiguration()
            => ExternIsDetoursDebug();

        /// <inheritdoc />
        public void SetNativeConfiguration(bool isDebug)
        {
            throw new NotSupportedException("On Windows this can be queried directly, so no need to set it upfront");
        }

        /// <inheritdoc />
        public bool TerminateProcess(SafeProcessHandle hProcess, int exitCode)
            => ExternTerminateProcess(hProcess, exitCode);

        /// <inheritdoc />
        public bool GetExitCodeProcess(SafeProcessHandle hProcess, out int exitCode)
            => ExternGetExitCodeProcess(hProcess, out exitCode);

        /// <inheritdoc />
        public bool SetHandleInformation(SafeHandle hObject, int dwMask, int dwFlags)
            => ExternSetHandleInformation(hObject, dwMask, dwFlags);

        /// <inheritdoc />
        public IntPtr GetCurrentProcess()
            => ExternGetCurrentProcess();

        /// <inheritdoc />
        [SuppressMessage("Microsoft.Reliability", "CA2001:AvoidCallingProblematicMethods")]
        public bool IsWow64Process(SafeProcessHandle process)
        {
            IntPtr handle = process == null ? GetCurrentProcess() : process.DangerousGetHandle();

            if (IO.Windows.FileSystemWin.StaticIsOSVersionGreaterOrEqual(IO.Windows.FileSystemWin.MinWindowsVersionThatSupportsWow64Processes))
            {
                return ExternIsWow64Process(handle, out bool result) && result;
            }

            return false;
        }

        /// <inheritdoc />
        public bool GetProcessTimes(IntPtr handle, out long creation, out long exit, out long kernel, out long user)
            => Process.ExternGetProcessTimes(handle, out creation, out exit, out kernel, out user);

        /// <inheritdoc />
        public bool DuplicateHandle(ProcessHandle hSourceProcessHandle, SafeHandle hSourceHandle, ProcessHandle hTargetProcess, out SafeWaitHandle targetHandle, int dwDesiredAccess, bool bInheritHandle, int dwOptions)
            => ExternDuplicateHandle(hSourceProcessHandle, hSourceHandle, hTargetProcess, out targetHandle, dwDesiredAccess, bInheritHandle, dwOptions);

        /// <inheritdoc />
        public SafeProcessHandle OpenProcess(ProcessSecurityAndAccessRights dwDesiredAccess, bool bInheritHandle, uint dwProcessId)
            => ExternOpenProcess(dwDesiredAccess, bInheritHandle, dwProcessId);

        /// <inheritdoc />
        public bool IsProcessInJob(SafeProcessHandle hProcess, IntPtr hJob, out bool result)
            => ExternIsProcessInJob(hProcess, hJob, out result);

        /// <inheritdoc />
        public uint GetModuleFileNameEx(SafeProcessHandle hProcess, IntPtr hModule, StringBuilder lpBaseName, uint nSize)
            => ExternGetModuleFileNameEx(hProcess, hModule, lpBaseName, nSize);

        /// <inheritdoc />
        public bool AssignProcessToJobObject(IntPtr hJob, ProcessPtr hProcess)
            => ExternAssignProcessToJobObject(hJob, hProcess);

        /// <inheritdoc />
        public IProcessInjector CreateProcessInjector(Guid payloadGuid, SafeHandle remoteInjectorPipe, SafeHandle reportPipe, string dllX86, string dllX64, ArraySegment<byte> payload)
            => new ProcessInjectorWin(payloadGuid, remoteInjectorPipe, reportPipe, dllX86, dllX64, payload);

        /// <inheritdoc />
        public byte[] SerializeEnvironmentBlock(IReadOnlyDictionary<string, string> environmentVariables)
        {
            if (environmentVariables == null)
            {
                return null;
            }

            int numEnvironmentVariables = environmentVariables.Count;

            // get the keys
            var keys = environmentVariables.Keys.ToArray();

            // get the values
            var values = environmentVariables.Values.ToArray();

            // sort both by the keys
            // Windows 2000 requires the environment block to be sorted by the key
            // It will first converting the case the strings and do ordinal comparison.
            Array.Sort(keys, values, StringComparer.OrdinalIgnoreCase);

            using (PooledObjectWrapper<StringBuilder> wrap = Pools.GetStringBuilder())
            {
                // create a list of null terminated "key=val" strings
                StringBuilder stringBuff = wrap.Instance;
                for (int i = 0; i < numEnvironmentVariables; ++i)
                {
                    stringBuff.Append(keys[i]);
                    stringBuff.Append('=');
                    stringBuff.Append(values[i]);
                    stringBuff.Append('\0');
                }

                // an extra null at the end indicates end of list.
                stringBuff.Append('\0');

                return Encoding.Unicode.GetBytes(stringBuff.ToString());
            }
        }

        /// <inheritdoc />
        public CreateDetouredProcessStatus CreateDetouredProcess(
           string lpcwCommandLine,
           int dwCreationFlags,
           IntPtr lpEnvironment,
           string lpcwWorkingDirectory,
           SafeHandle hStdInput,
           SafeHandle hStdOutput,
           SafeHandle hStdError,
           SafeHandle hJob,
           IProcessInjector injector,
           bool addProcessToSilo,
           out SafeProcessHandle phProcess,
           out SafeThreadHandle phThread,
           out int pdwProcessId,
           out int errorCode)
        {
            Assert64Process();

            var status = ExternCreateDetouredProcess(
                lpcwCommandLine,
                dwCreationFlags,
                lpEnvironment,
                lpcwWorkingDirectory,
                hStdInput,
                hStdOutput,
                hStdError,
                hJob,
                injector == null ? IntPtr.Zero : injector.Injector(),
                addProcessToSilo,
                out phProcess,
                out phThread,
                out pdwProcessId);

            errorCode = status == CreateDetouredProcessStatus.Succeeded ? 0 : Marshal.GetLastWin32Error();

            // TODO: Enforce this postcondition.
            // Contract.Assume(status == CreateDetouredProcessStatus.Succeeded || errorCode != 0, "Expected a valid error code on failure.");
            return status;
        }

        /// <inheritdoc />
        public CreateDetachedProcessStatus CreateDetachedProcess(
            string commandLine,
            IReadOnlyDictionary<string, string> environmentVariables,
            string workingDirectory,
            out int newProcessId,
            out int errorCode)
        {
            Assert64Process();

            byte[] serializedEnvironmentBlock = SerializeEnvironmentBlock(environmentVariables);

            var status = ExternCreateDetachedProcess(
                commandLine,
                serializedEnvironmentBlock,
                workingDirectory,
                out newProcessId);

            errorCode = status == CreateDetachedProcessStatus.Succeeded ? 0 : Marshal.GetLastWin32Error();
            return status;
        }

        /// <inheritdoc />
        public SafeFileHandle CreateNamedPipe(string lpName, PipeOpenMode dwOpenMode, PipeMode dwPipeMode, int nMaxInstances, int nOutBufferSize, int nInBufferSize, int nDefaultTimeout, IntPtr lpSecurityAttributes)
            => ExternCreateNamedPipe(lpName, dwOpenMode, dwPipeMode, nMaxInstances, nOutBufferSize, nInBufferSize, nDefaultTimeout, lpSecurityAttributes);

        /// <inheritdoc />
        public bool WaitNamedPipe(string pipeName, uint timeout) => ExternWaitNamedPipe(pipeName, timeout);

        /// <inheritdoc />
        public bool ApplyDriveMappings(PathMapping[] mappings)
        {
            Assert64Process();
            return ExternRemapDevices(mappings.Length, mappings);
        }

        /// <inheritdoc />
        public IntPtr CreateJobObject(IntPtr lpJobAttributes, string lpName)
            => ExternCreateJobObject(lpJobAttributes, lpName);

        /// <inheritdoc />
        public bool QueryInformationJobObject(IntPtr hJob, JOBOBJECTINFOCLASS JobObjectInformationClass, void* lpJobObjectInfo, uint cbJobObjectInfoLength, out uint lpReturnLength)
            => ExternQueryInformationJobObject(hJob, JobObjectInformationClass, lpJobObjectInfo, cbJobObjectInfoLength, out lpReturnLength);

        /// <inheritdoc />
        public bool SetInformationJobObject(IntPtr hJob, JOBOBJECTINFOCLASS JobObjectInfoClass, void* lpJobObjectInfo, uint cbJobObjectInfoLength)
            => ExternSetInformationJobObject(hJob, JobObjectInfoClass, lpJobObjectInfo, cbJobObjectInfoLength);

        /// <inheritdoc />
        public bool TerminateJobObject(IntPtr hJob, int exitCode)
            => ExternTerminateJobObject(hJob, exitCode);

        /// <inheritdoc />
        public bool OSSupportsNestedJobs()
            => IO.Windows.FileSystemWin.StaticIsOSVersionGreaterOrEqual(IO.Windows.FileSystemWin.MinWindowsVersionThatSupportsNestedJobs);

        internal static void Assert64Process()
        {
            Contract.Assert(IntPtr.Size == 8, "BuildXL is 64 bit process only.");
        }

        [DllImport(ExternDll.BuildXLNatives64, EntryPoint = "NormalizeAndHashPath", CharSet = CharSet.Unicode)]
        private static extern unsafe int ExternNormalizeAndHashPath(
            [MarshalAs(UnmanagedType.LPWStr)] string path,
            byte* buffer, int bufferLength);

        [DllImport(ExternDll.BuildXLNatives64, EntryPoint = "AreBuffersEqual")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ExternAreBuffersEqual(byte* buffer1, byte* buffer2, int bufferLength);

        [DllImport(ExternDll.BuildXLNatives64, EntryPoint = "IsDetoursDebug", SetLastError = true, BestFitMapping = false,
           ThrowOnUnmappableChar = true)]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool ExternIsDetoursDebug();

        /// <remarks>
        /// http://msdn.microsoft.com/en-us/library/windows/desktop/ms686714(v=vs.85).aspx
        /// </remarks>
        [DllImport(ExternDll.Kernel32, EntryPoint = "TerminateProcess", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ExternTerminateProcess(SafeProcessHandle hProcess, int exitCode);

        [DllImport(ExternDll.Kernel32, EntryPoint = "GetExitCodeProcess", SetLastError = true)]
        [ResourceExposure(ResourceScope.None)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ExternGetExitCodeProcess(SafeProcessHandle processHandle, out int exitCode);

        [DllImport(ExternDll.Kernel32, EntryPoint = "SetHandleInformation", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ExternSetHandleInformation(SafeHandle hObject, int dwMask, int dwFlags);

        [DllImport(ExternDll.Kernel32, EntryPoint = "GetCurrentProcess", SetLastError = true)]
        [ResourceExposure(ResourceScope.Process)]
        private static extern IntPtr ExternGetCurrentProcess();

        [DllImport(ExternDll.Kernel32, EntryPoint = "IsWow64Process", SetLastError = true, CallingConvention = CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ExternIsWow64Process(
            [In] IntPtr process,
            [MarshalAs(UnmanagedType.Bool)]
            [Out] out bool wow64Process);

        [DllImport(ExternDll.Kernel32, EntryPoint = "DuplicateHandle", SetLastError = true, BestFitMapping = false)]
        [ResourceExposure(ResourceScope.Machine)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ExternDuplicateHandle(
            ProcessHandle hSourceProcessHandle,
            SafeHandle hSourceHandle,
            ProcessHandle hTargetProcess,
            out SafeWaitHandle targetHandle,
            int dwDesiredAccess,
            [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
            int dwOptions);

        /// <remarks>
        /// http://msdn.microsoft.com/en-us/library/windows/desktop/ms684320(v=vs.85).aspx
        /// </remarks>
        [DllImport(ExternDll.Kernel32, EntryPoint = "OpenProcess", SetLastError = true)]
        private static extern SafeProcessHandle ExternOpenProcess(
            ProcessSecurityAndAccessRights dwDesiredAccess,
            [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle,
            uint dwProcessId);

        /// <remarks>
        /// http://msdn.microsoft.com/en-us/library/windows/desktop/ms684127(v=vs.85).aspx
        /// </remarks>
        [DllImport(ExternDll.Kernel32, EntryPoint = "IsProcessInJob", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ExternIsProcessInJob(SafeProcessHandle hProcess, IntPtr hJob, [MarshalAs(UnmanagedType.Bool)] out bool result);

        /// <remarks>
        /// http://msdn.microsoft.com/en-us/library/windows/desktop/ms683198(v=vs.85).aspx
        /// </remarks>
        [DllImport(ExternDll.Psapi, EntryPoint = "GetModuleFileNameEx", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern uint ExternGetModuleFileNameEx(
            SafeProcessHandle hProcess,
            IntPtr hModule,
            [Out] StringBuilder lpBaseName,
            uint nSize);

        [DllImport(ExternDll.Kernel32, EntryPoint = "AssignProcessToJobObject", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ExternAssignProcessToJobObject(IntPtr hJob, ProcessPtr hProcess);

        [DllImport(ExternDll.BuildXLNatives64, EntryPoint = "CreateDetouredProcess", SetLastError = true, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        [return: MarshalAs(UnmanagedType.I4)]
        private static extern CreateDetouredProcessStatus ExternCreateDetouredProcess(
            [MarshalAs(UnmanagedType.LPWStr)] string lpcwCommandLine,
            int dwCreationFlags,
            IntPtr lpEnvironment,
            [MarshalAs(UnmanagedType.LPWStr)] string lpcwWorkingDirectory,
            SafeHandle hStdInput,
            SafeHandle hStdOutput,
            SafeHandle hStdError,
            SafeHandle hJob,
            IntPtr injector,
            bool addProcessToSilo,
            out SafeProcessHandle phProcess,
            out SafeThreadHandle phThread,
            out int pdwProcessId);

        [DllImport(ExternDll.BuildXLNatives64, EntryPoint = "CreateDetachedProcess", SetLastError = true, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        [return: MarshalAs(UnmanagedType.I4)]
        private static extern CreateDetachedProcessStatus ExternCreateDetachedProcess(
            [MarshalAs(UnmanagedType.LPWStr)] string lpcwCommandLine,
            [MarshalAs(UnmanagedType.LPArray)] byte[] lpEnvironment,
            [MarshalAs(UnmanagedType.LPWStr)] string lpcwWorkingDirectory,
            out int pdwProcessId);

        [DllImport(ExternDll.Kernel32, EntryPoint = "CreateNamedPipeW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        [ResourceExposure(ResourceScope.Process)]
        private static extern SafeFileHandle ExternCreateNamedPipe(
            string lpName,
            PipeOpenMode dwOpenMode,
            PipeMode dwPipeMode,
            int nMaxInstances,
            int nOutBufferSize,
            int nInBufferSize,
            int nDefaultTimeout,
            IntPtr lpSecurityAttributes);

        [DllImport(ExternDll.Kernel32, EntryPoint = "WaitNamedPipe", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ExternWaitNamedPipe(string pipeName, uint timeout);

        [DllImport(ExternDll.BuildXLNatives64, EntryPoint = "RemapDevices", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ExternRemapDevices(
            int mapCount,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPStr)]
            PathMapping[] mappings);

        [DllImport(ExternDll.BuildXLNatives64, EntryPoint = "FindFileAccessPolicyInTree", SetLastError = true, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ExternFindFileAccessPolicyInTree(
            IntPtr record,
            [MarshalAs(UnmanagedType.LPWStr)] string absolutePath,
            UIntPtr absolutePathLength,
            out uint conePolicy,
            out uint nodePolicy,
            out uint pathId,
            out IO.Usn expectedUsn);

        [DllImport(ExternDll.Kernel32, EntryPoint = "CreateJobObject", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr ExternCreateJobObject([In] IntPtr lpJobAttributes, string lpName);

        [DllImport(ExternDll.Kernel32, EntryPoint = "QueryInformationJobObject", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ExternQueryInformationJobObject(IntPtr hJob, JOBOBJECTINFOCLASS JobObjectInformationClass, void* lpJobObjectInfo, uint cbJobObjectInfoLength, out uint lpReturnLength);

        [DllImport(ExternDll.Kernel32, EntryPoint = "SetInformationJobObject", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ExternSetInformationJobObject(IntPtr hJob, JOBOBJECTINFOCLASS JobObjectInfoClass, void* lpJobObjectInfo, uint cbJobObjectInfoLength);

        [DllImport(ExternDll.Kernel32, EntryPoint = "TerminateJobObject", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ExternTerminateJobObject(IntPtr hJob, int exitCode);

        /// <inheritdoc />
        public bool SetupProcessDumps(string logsDirectory, out string coreDumpDirectory)
        {
            coreDumpDirectory = "Not implemented on Windows machines.";
            return true;
        }

        /// <inheritdoc />
        public void TeardownProcessDumps() { }
    }
}
