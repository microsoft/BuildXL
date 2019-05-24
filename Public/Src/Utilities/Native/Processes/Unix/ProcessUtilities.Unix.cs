// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Runtime.InteropServices;
using System.Text;
using BuildXL.Interop.MacOS;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using JetBrains.Annotations;
using Microsoft.Win32.SafeHandles;

#if FEATURE_SAFE_PROCESS_HANDLE
using ProcessHandle = System.Runtime.InteropServices.SafeHandle;
using ProcessPtr = Microsoft.Win32.SafeHandles.SafeProcessHandle;
#else
using ProcessHandle = System.Runtime.InteropServices.HandleRef;
using ProcessPtr = System.IntPtr;
using SafeProcessHandle = BuildXL.Interop.Windows.SafeProcessHandle;
#endif

namespace BuildXL.Native.Processes.Unix
{
    /// <summary>
    /// Implementation of native methods for Unix-based systems.
    /// </summary>
    public class ProcessUtilitiesUnix : IProcessUtilities
    {
        private static bool s_isDebugModeEnabled = false;

        /// <inheritdoc />
        public void SetNativeConfiguration(bool isDebugMode)
        {
            s_isDebugModeEnabled = isDebugMode;
        }

        /// <inheritdoc />
        public unsafe int NormalizeAndHashPath(string path, out byte[] normalizedPathBytes)
        {
            // in the native Unix world strings are represented as UTF8-encoded null-terminated chars (1 char == 1 byte)
            byte[] pathBytes = Encoding.UTF8.GetBytes((path + '\0').ToCharArray());
            normalizedPathBytes = new byte[pathBytes.Length];

            fixed (byte* outBuffer = &normalizedPathBytes[0])
            {
                return Sandbox.NormalizePathAndReturnHash(pathBytes, outBuffer, normalizedPathBytes.Length);
            }
        }

        /// <inheritdoc />
        public bool AreBuffersEqual(byte[] buffer1, byte[] buffer2)
        {
            Contract.Requires(buffer1 != null);
            Contract.Requires(buffer2 != null);

            if (buffer1.Length != buffer2.Length)
            {
                return false;
            }

            for (var i = 0; i < buffer1.Length; i++)
            {
                if (buffer1[i] != buffer2[i])
                {
                    return false;
                }
            }

            return true;
        }

        /// <inheritdoc />
        public bool IsNativeInDebugConfiguration() => s_isDebugModeEnabled;

        /// <inheritdoc />
        public bool TerminateProcess(SafeProcessHandle hProcess, int exitCode)
            => throw new NotImplementedException();

        /// <inheritdoc />
        public bool GetExitCodeProcess(SafeProcessHandle hProcess, out int exitCode)
            => throw new NotImplementedException();

        /// <inheritdoc />
        public bool SetHandleInformation(SafeHandle hObject, int dwMask, int dwFlags)
            => throw new NotImplementedException();

        /// <inheritdoc />
        public IntPtr GetCurrentProcess()
            => throw new NotImplementedException();

        /// <inheritdoc />
        public bool IsWow64Process([CanBeNull] SafeProcessHandle process)
            => throw new NotImplementedException();

        /// <inheritdoc />
        public bool GetProcessTimes(IntPtr handle, out long creation, out long exit, out long kernel, out long user)
            => throw new NotImplementedException();

        /// <inheritdoc />
        public bool DuplicateHandle(ProcessHandle hSourceProcessHandle, SafeHandle hSourceHandle, ProcessHandle hTargetProcess, out SafeWaitHandle targetHandle, int dwDesiredAccess, bool bInheritHandle, int dwOptions)
            => throw new NotImplementedException();

        /// <inheritdoc />
        public SafeProcessHandle OpenProcess(ProcessSecurityAndAccessRights dwDesiredAccess, bool bInheritHandle, uint dwProcessId)
            => throw new NotImplementedException();

        /// <inheritdoc />
        public bool IsProcessInJob(SafeProcessHandle hProcess, IntPtr hJob, out bool result)
            => throw new NotImplementedException();

        /// <inheritdoc />
        public uint GetModuleFileNameEx(SafeProcessHandle hProcess, IntPtr hModule, StringBuilder lpBaseName, uint nSize)
            => throw new NotImplementedException();

        /// <inheritdoc />
        public bool AssignProcessToJobObject(IntPtr hJob, ProcessPtr hProcess)
            => throw new NotImplementedException();

        /// <inheritdoc />
        public IProcessInjector CreateProcessInjector(Guid payloadGuid, SafeHandle remoteInjectorPipe, SafeHandle reportPipe, string dllX86, string dllX64, ArraySegment<byte> payload)
            => throw new NotImplementedException();

        /// <inheritdoc />
        public byte[] SerializeEnvironmentBlock(IReadOnlyDictionary<string, string> environmentVariables)
            => new byte[0]; // TODO: this is only used for communication between BuildXL and external sandboxed process, which we don't do yet on Unix systems.

        /// <inheritdoc />
        public CreateDetouredProcessStatus CreateDetouredProcess(string lpcwCommandLine, int dwCreationFlags, IntPtr lpEnvironment, string lpcwWorkingDirectory, SafeHandle hStdInput, SafeHandle hStdOutput, SafeHandle hStdError, SafeHandle hJob, IProcessInjector injector, bool addProcessToSilo, out SafeProcessHandle phProcess, out SafeThreadHandle phThread, out int pdwProcessId, out int errorCode)
            => throw new NotImplementedException();

        /// <inheritdoc />
        public CreateDetachedProcessStatus CreateDetachedProcess(string commandLine, IReadOnlyDictionary<string, string> environmentVariables, string workingDirectory, out int newProcessId, out int errorCode)
            => throw new NotImplementedException();

        /// <inheritdoc />
        public SafeFileHandle CreateNamedPipe(string lpName, PipeOpenMode dwOpenMode, PipeMode dwPipeMode, int nMaxInstances, int nOutBufferSize, int nInBufferSize, int nDefaultTimeout, IntPtr lpSecurityAttributes)
            => throw new NotImplementedException();

        /// <inheritdoc />
        public bool ApplyDriveMappings(PathMapping[] mappings)
            => throw new NotImplementedException();

        /// <inheritdoc />
        public IntPtr CreateJobObject(IntPtr lpJobAttributes, string lpName)
            => throw new NotImplementedException();

        /// <inheritdoc />
        public unsafe bool QueryInformationJobObject(IntPtr hJob, JOBOBJECTINFOCLASS JobObjectInformationClass, void* lpJobObjectInfo, uint cbJobObjectInfoLength, out uint lpReturnLength)
            => throw new NotImplementedException();

        /// <inheritdoc />
        public unsafe bool SetInformationJobObject(IntPtr hJob, JOBOBJECTINFOCLASS JobObjectInfoClass, void* lpJobObjectInfo, uint cbJobObjectInfoLength)
            => throw new NotImplementedException();

        /// <inheritdoc />
        public bool TerminateJobObject(IntPtr hJob, int exitCode)
            => throw new NotImplementedException();

        /// <inheritdoc />
        public bool OSSupportsNestedJobs() => true;

        /// <inheritdoc />
        public void AttachContainerToJobObject(
            IntPtr hJob,
            IReadOnlyDictionary<ExpandedAbsolutePath, IReadOnlyList<ExpandedAbsolutePath>> redirectedDirectories,
            bool enableWciFilter,
            IEnumerable<string> bindFltExclusions,
            out IEnumerable<string> warnings)
            => throw new NotImplementedException();

        /// <inheritdoc />
        public bool TryCleanUpContainer(IntPtr hJob, out IEnumerable<string> errors) => throw new NotImplementedException();

        /// <inheritdoc />
        public bool IsWciAndBindFiltersAvailable() => false;

        /// <inheritdoc />
        public bool SetupProcessDumps(string logsDirectory, out string coreDumpDirectory)
        {
            var sb = new StringBuilder(NativeIOConstants.MaxPath);
            var result = Process.SetupProcessDumps(logsDirectory, sb, sb.MaxCapacity);
            coreDumpDirectory = sb.ToString();

            return result;
        }

        /// <inheritdoc />
        public void TeardownProcessDumps()
        {
            Process.TeardownProcessDumps();
        }
    }
}
