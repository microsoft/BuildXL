// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
        private static extern bool CloseHandle(IntPtr hObject);

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
        [DllImport(BuildXL.Interop.Libraries.WindowsKernel32, EntryPoint = "GetProcessMemoryInfo", SetLastError = true)]
        public static extern bool GetProcessMemoryInfo(IntPtr handle, [In, Out] PROCESSMEMORYCOUNTERSEX ppsmemCounters, uint cb);
    }
}
