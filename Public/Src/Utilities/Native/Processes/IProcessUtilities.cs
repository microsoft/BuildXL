// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using BuildXL.Utilities;
using JetBrains.Annotations;
using Microsoft.Win32.SafeHandles;
#if FEATURE_SAFE_PROCESS_HANDLE
using ProcessHandle = System.Runtime.InteropServices.SafeHandle;
using ProcessPtr = Microsoft.Win32.SafeHandles.SafeProcessHandle;
#else
using SafeProcessHandle = BuildXL.Interop.Windows.SafeProcessHandle;
using ProcessHandle = System.Runtime.InteropServices.HandleRef;
using ProcessPtr = System.IntPtr;
#endif

namespace BuildXL.Native.Processes
{
    /// <summary>
    /// Interface for static methods that potentially have to call into native.
    /// </summary>
    interface IProcessUtilities
    {
        /// <summary><see cref="ProcessUtilities.NormalizeAndHashPath"/></summary>
        int NormalizeAndHashPath(string path, out byte[] normalizedPathBytes);

        /// <summary><see cref="ProcessUtilities.AreBuffersEqual"/></summary>
        bool AreBuffersEqual(byte[] buffer1, byte[] buffer2);

        /// <summary><see cref="ProcessUtilities.IsNativeInDebugConfiguration"/></summary>
        bool IsNativeInDebugConfiguration();

        /// <summary><see cref="ProcessUtilities.SetNativeConfiguration(bool)"/></summary>
        void SetNativeConfiguration(bool isDebug);

        /// <summary><see cref="ProcessUtilities.TerminateProcess"/></summary>
        bool TerminateProcess(SafeProcessHandle hProcess, int exitCode);

        /// <summary><see cref="ProcessUtilities.GetExitCodeProcess"/></summary>
        bool GetExitCodeProcess(SafeProcessHandle hProcess, out int exitCode);

        /// <summary><see cref="ProcessUtilities.SetHandleInformation"/></summary>
        bool SetHandleInformation(SafeHandle hObject, int dwMask, int dwFlags);

        /// <summary><see cref="ProcessUtilities.GetCurrentProcess"/></summary>
        IntPtr GetCurrentProcess();

        /// <summary><see cref="ProcessUtilities.IsWow64Process"/></summary>
        bool IsWow64Process([CanBeNull]SafeProcessHandle process);

        /// <summary><see cref="ProcessUtilities.GetProcessTimes"/></summary>
        bool GetProcessTimes(IntPtr handle, out long creation, out long exit, out long kernel, out long user);

        /// <summary><see cref="ProcessUtilities.DuplicateHandle"/></summary>
        bool DuplicateHandle(ProcessHandle hSourceProcessHandle, SafeHandle hSourceHandle, ProcessHandle hTargetProcess, out SafeWaitHandle targetHandle, int dwDesiredAccess, bool bInheritHandle, int dwOptions);

        /// <summary><see cref="ProcessUtilities.OpenProcess"/></summary>
        SafeProcessHandle OpenProcess(ProcessSecurityAndAccessRights dwDesiredAccess, bool bInheritHandle, uint dwProcessId);

        /// <summary><see cref="ProcessUtilities.IsProcessInJob"/></summary>
        bool IsProcessInJob(SafeProcessHandle hProcess, IntPtr hJob, out bool result);

        /// <summary><see cref="ProcessUtilities.GetModuleFileNameEx"/></summary>
        uint GetModuleFileNameEx(SafeProcessHandle hProcess, IntPtr hModule, StringBuilder lpBaseName, uint nSize);

        /// <summary><see cref="ProcessUtilities.AssignProcessToJobObject"/></summary>
        bool AssignProcessToJobObject(IntPtr hJob, ProcessPtr hProcess);

        /// <summary><see cref="ProcessUtilities.CreateProcessInjector"/></summary>
        IProcessInjector CreateProcessInjector(Guid payloadGuid, SafeHandle remoteInjectorPipe, SafeHandle reportPipe, string dllX86, string dllX64, ArraySegment<byte> payload);

        /// <summary><see cref="ProcessUtilities.SerializeEnvironmentBlock"/></summary>
        byte[] SerializeEnvironmentBlock(IReadOnlyDictionary<string, string> environmentVariables);

        /// <summary><see cref="ProcessUtilities.CreateDetouredProcess"/></summary>
        CreateDetouredProcessStatus CreateDetouredProcess(string lpcwCommandLine, int dwCreationFlags, IntPtr lpEnvironment, string lpcwWorkingDirectory, SafeHandle hStdInput, SafeHandle hStdOutput, SafeHandle hStdError, SafeHandle hJob, IProcessInjector injector, bool addProcessToContainer, out SafeProcessHandle phProcess, out SafeThreadHandle phThread, out int pdwProcessId, out int errorCode);

        /// <summary><see cref="ProcessUtilities.CreateDetachedProcess"/></summary>
        CreateDetachedProcessStatus CreateDetachedProcess(string commandLine, IReadOnlyDictionary<string, string> environmentVariables, string workingDirectory, out int newProcessId, out int errorCode);

        /// <summary><see cref="ProcessUtilities.CreateNamedPipe"/></summary>
        SafeFileHandle CreateNamedPipe(string lpName, PipeOpenMode dwOpenMode, PipeMode dwPipeMode, int nMaxInstances, int nOutBufferSize, int nInBufferSize, int nDefaultTimeout, IntPtr lpSecurityAttributes);

        /// <summary><see cref="ProcessUtilities.ApplyDriveMappings"/></summary>
        bool ApplyDriveMappings(PathMapping[] mappings);

        /// <summary><see cref="ProcessUtilities.CreateJobObject"/></summary>
        IntPtr CreateJobObject(IntPtr lpJobAttributes, string lpName);

        /// <summary><see cref="ProcessUtilities.QueryInformationJobObject"/></summary>
        unsafe bool QueryInformationJobObject(IntPtr hJob, JOBOBJECTINFOCLASS JobObjectInformationClass, void* lpJobObjectInfo, uint cbJobObjectInfoLength, out uint lpReturnLength);

        /// <summary><see cref="ProcessUtilities.SetInformationJobObject"/></summary>
        unsafe bool SetInformationJobObject(IntPtr hJob, JOBOBJECTINFOCLASS JobObjectInfoClass, void* lpJobObjectInfo, uint cbJobObjectInfoLength);

        /// <summary><see cref="ProcessUtilities.TerminateJobObject"/></summary>
        bool TerminateJobObject(IntPtr hJob, int exitCode);

        /// <summary><see cref="ProcessUtilities.OSSupportsNestedJobs"/></summary>
        bool OSSupportsNestedJobs();

        /// <summary><see cref="ProcessUtilities.AttachContainerToJobObject"/></summary>
        void AttachContainerToJobObject(
            IntPtr hJob,
            IReadOnlyDictionary<ExpandedAbsolutePath, IReadOnlyList<ExpandedAbsolutePath>> redirectedDirectories,
            bool enableWciFilter,
            IEnumerable<string> bindFltExclusions,
            out IEnumerable<string> warnings);

        /// <summary><see cref="ProcessUtilities.TryCleanUpContainer"/></summary>
        bool TryCleanUpContainer(IntPtr hJob, out IEnumerable<string> errors);

        /// <summary><see cref="ProcessUtilities.IsWciAndBindFiltersAvailable()"/></summary>
        bool IsWciAndBindFiltersAvailable();

        /// <summary><see cref="ProcessUtilities.SetupProcessDumps(string, out string)"/></summary>
        bool SetupProcessDumps(string logsDirectory, out string coreDumpDirectory);

        /// <summary><see cref="ProcessUtilities.TeardownProcessDumps()"/></summary>
        void TeardownProcessDumps();
    }
}
