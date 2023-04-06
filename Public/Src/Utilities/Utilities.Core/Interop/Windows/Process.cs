// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

#if FEATURE_SAFE_PROCESS_HANDLE
using Microsoft.Win32.SafeHandles;
#endif

namespace BuildXL.Interop.Windows
{
    /// <summary>
    /// The Process class offers interop calls for process based tasks into operating system facilities
    /// </summary>
    public static class Process
    {
        /// <nodoc />
        [DllImport(BuildXL.Interop.Libraries.WindowsAdvApi32, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool OpenProcessToken(
            IntPtr processHandle,
            uint desiredAccess, out IntPtr tokenHandle);

        /// <nodoc />
        [DllImport(BuildXL.Interop.Libraries.WindowsAdvApi32, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetTokenInformation(
            IntPtr tokenHandle,
            TOKEN_INFORMATION_CLASS tokenInformationClass,
            IntPtr tokenInformation,
            uint tokenInformationLength,
            out uint returnLength);

        /// <nodoc />
        [DllImport(BuildXL.Interop.Libraries.WindowsKernel32, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr hObject);

        /// <nodoc />
        private enum TOKEN_INFORMATION_CLASS
        {
            TokenUser = 1,
            TokenGroups,
            TokenPrivileges,
            TokenOwner,
            TokenPrimaryGroup,
            TokenDefaultDacl,
            TokenSource,
            TokenType,
            TokenImpersonationLevel,
            TokenStatistics,
            TokenRestrictedSids,
            TokenSessionId,
            TokenGroupsAndPrivileges,
            TokenSessionReference,
            TokenSandBoxInert,
            TokenAuditPolicy,
            TokenOrigin,
            TokenElevationType,
            TokenLinkedToken,
            TokenElevation,
            TokenHasRestrictions,
            TokenAccessInformation,
            TokenVirtualizationAllowed,
            TokenVirtualizationEnabled,
            TokenIntegrityLevel,
            TokenUIAccess,
            TokenMandatoryPolicy,
            TokenLogonSid,
            TokenIsAppContainer,
            TokenCapabilities,
            TokenAppContainerSid,
            TokenAppContainerNumber,
            TokenUserClaimAttributes,
            TokenDeviceClaimAttributes,
            TokenRestrictedUserClaimAttributes,
            TokenRestrictedDeviceClaimAttributes,
            TokenDeviceGroups,
            TokenRestrictedDeviceGroups,
            TokenSecurityAttributes,
            TokenIsRestricted,
            MaxTokenInfoClass,
        }

        /// <nodoc />
        public const uint STANDARD_RIGHTS_READ = 0x00020000;

        /// <nodoc />
        public const uint TOKEN_QUERY = 0x0008;

        /// <nodoc />
        public const uint TOKEN_READ = STANDARD_RIGHTS_READ | TOKEN_QUERY;

        /// <nodoc />
        public struct TOKEN_ELEVATION
        {
            /// <nodoc />
            public int TokenIsElevated;
        }

        /// <inheritdoc />
        public static bool IsElevated()
        {
            bool ret = false;
            IntPtr hToken; // Invalid handle.

            if (OpenProcessToken(System.Diagnostics.Process.GetCurrentProcess().Handle, TOKEN_QUERY, out hToken))
            {
                uint tokenInfLength = 0;

                // first call gets lenght of tokenInformation
                ret = GetTokenInformation(hToken, TOKEN_INFORMATION_CLASS.TokenElevation, IntPtr.Zero, tokenInfLength, out tokenInfLength);

                IntPtr tokenInformation = Marshal.AllocHGlobal((IntPtr)tokenInfLength);

                if (tokenInformation != IntPtr.Zero)
                {
                    ret = GetTokenInformation(hToken, TOKEN_INFORMATION_CLASS.TokenElevation, tokenInformation, tokenInfLength, out tokenInfLength);

                    if (ret)
                    {
                        TOKEN_ELEVATION tokenElevation = (TOKEN_ELEVATION)Marshal.PtrToStructure(tokenInformation, typeof(TOKEN_ELEVATION));

                        ret = tokenElevation.TokenIsElevated != 0;
                    }

                    Marshal.FreeHGlobal(tokenInformation);
                    CloseHandle(hToken);
                }
            }

            return ret;
        }

        /// <nodoc />
        [DllImport(BuildXL.Interop.Libraries.WindowsKernel32, EntryPoint = "GetProcessTimes", SetLastError = true)]
        [ResourceExposure(ResourceScope.None)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ExternGetProcessTimes(IntPtr handle, out long creation, out long exit, out long kernel, out long user);

        /// <nodoc />
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public sealed class PROCESSMEMORYCOUNTERSEX
        {
            /// <nodoc />
            public uint cb;

            /// <nodoc />
            public uint PageFaultCount;

            /// <nodoc />
            public ulong PeakWorkingSetSize;

            /// <nodoc />
            public ulong WorkingSetSize;

            /// <nodoc />
            public ulong QuotaPeakPagedPoolUsage;

            /// <nodoc />
            public ulong QuotaPagedPoolUsage;

            /// <nodoc />
            public ulong QuotaPeakNonPagedPoolUsage;

            /// <nodoc />
            public ulong QuotaNonPagedPoolUsage;

            /// <nodoc />
            public ulong PagefileUsage;

            /// <nodoc />
            public ulong PeakPagefileUsage;

            /// <nodoc />
            public ulong PrivateUsage;

            /// <nodoc />
            public PROCESSMEMORYCOUNTERSEX()
            {
                cb = (uint)Marshal.SizeOf(typeof(PROCESSMEMORYCOUNTERSEX));
            }
        }

        /// <nodoc />
        [DllImport(BuildXL.Interop.Libraries.WindowsPsApi, EntryPoint = "GetProcessMemoryInfo", SetLastError = true)]
        public static extern bool GetProcessMemoryInfo(IntPtr handle, [In, Out] PROCESSMEMORYCOUNTERSEX ppsmemCounters, uint cb);

        [Flags]
        private enum ThreadAccess : int
        {
            SUSPEND_RESUME = (0x0002),
        }

        [DllImport(Libraries.WindowsKernel32, SetLastError = true)]
        private static extern IntPtr OpenThread(ThreadAccess dwDesiredAccess, bool bInheritHandle, uint dwThreadId);

        [DllImport(Libraries.WindowsKernel32, SetLastError = true)]
        private static extern int SuspendThread(IntPtr hThread);

        [DllImport(Libraries.WindowsKernel32, SetLastError = true)]
        private static extern int ResumeThread(IntPtr hThread);

        /// <nodoc />
        public static bool Suspend(System.Diagnostics.Process process)
        {
            foreach (System.Diagnostics.ProcessThread thread in process.Threads)
            {
                // Open thread with required permissions
                IntPtr threadHandle = OpenThread(ThreadAccess.SUSPEND_RESUME, false, (uint)thread.Id);

                if (threadHandle == IntPtr.Zero)
                {
                    // If it points to zero, OpenThread function is failed.
                    return false;
                }

                try
                {
                    int suspendThreadResult = SuspendThread(threadHandle);
                    if (suspendThreadResult == -1)
                    {
                        return false;
                    }
                }
                finally
                {
                    Process.CloseHandle(threadHandle);
                }
            }

            return true;
        }

        /// <nodoc />
        public static bool Resume(System.Diagnostics.Process process)
        {
            foreach (System.Diagnostics.ProcessThread thread in process.Threads)
            {
                var threadHandle = OpenThread(ThreadAccess.SUSPEND_RESUME, false, (uint)thread.Id);

                if (threadHandle == IntPtr.Zero)
                {
                    // If it points to zero, OpenThread function is failed.
                    return false;
                }

                try
                {
                    int resumeThreadResult = ResumeThread(threadHandle);
                    if (resumeThreadResult == -1)
                    {
                        return false;
                    }
                }
                finally
                {
                    Process.CloseHandle(threadHandle);
                }
            }

            return true;
        }

#pragma warning disable ERP022 // Unobserved exception in generic exception handler

        /// <remarks>
        /// Implemented using Process.GetProcessById()
        /// </remarks>
        internal static bool IsAlive(int pid)
        {
            try
            {
                return System.Diagnostics.Process.GetProcessById(pid) != null;
            }
            catch
            {
                return false;
            }
        }

        /// <remarks>
        /// Implemented using Process.GetProcessById().Kill()
        /// </remarks>
        internal static bool ForceQuit(int pid)
        {
            try
            {
                System.Diagnostics.Process.GetProcessById(pid).Kill();
                return true;
            }
            catch
            {
                return false;
            }
        }

#pragma warning restore ERP022 // Unobserved exception in generic exception handler
    }
}
