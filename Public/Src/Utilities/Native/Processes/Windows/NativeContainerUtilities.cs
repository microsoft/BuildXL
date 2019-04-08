// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Runtime.InteropServices;

namespace BuildXL.Native.Processes.Windows
{
#pragma warning disable CS1591 // Missing XML comment

    /// <summary>
    /// Contains low-level APIs that interact with WCI and Bind filter
    /// </summary>
    public static class NativeContainerUtilities
    {
        /// <summary>
        /// Name of the WCI filter driver as it is registered in the OS
        /// </summary>
        public const string WciDriverName = "wcifs";

        /// <summary>
        /// Name of the Bind filter driver as it is registered in the OS
        /// </summary>
        public const string BindDriverName = "bindflt";

        /// <summary>
        /// Location of the user-mode DLL for the WCI driver
        /// </summary>
        public static readonly string WciDllLocation = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "wci.dll");

        /// <summary>
        /// Location of the user-mode DLL for the Bind driver
        /// </summary>
        public static readonly string BindDllLocation = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "bindflt.dll");


        /// <summary>
        /// We depend on the RS6 user-mode DLLs to interact with WCI and Bind filters.
        /// 10.0.18301.1000 is the minimum required version.
        /// </summary>
        public static readonly Version MinimumRequiredVersion = Version.Parse("10.0.18301.1000");

        /// <summary>
        /// Isolation mode for the WCI filter
        /// </summary>
        public enum WC_ISOLATION_MODE
        {
            IsolationModeHard = 0,
            IsolationModeSoft,
            IsolationModeSparseSoft,
            IsolationModeSparseHard,
            IsolationModeMaximum
        }

        /// <summary>
        /// Nesting mode for the WCI filter
        /// </summary>
        public enum WC_NESTING_MODE
        {
            WcNestingModeInner = 0,
            WcNestingModeOuter,
            WcNestingModeMaximum
        }

        /// <summary>
        /// A source path that can be mapped by the WCI filter
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WC_LAYER_DESCRIPTOR
        {
            /// <summary>
            /// The ID of the layer.
            /// </summary>
            public GUID LayerId;

            /// <nodoc/>
            public LayerDescriptorFlags Flags;

            /// <summary>
            /// Path to the layer root directory, null-terminated.
            /// </summary>
            [MarshalAs(UnmanagedType.LPWStr)]
            public string Path;
        }

        /// <summary>
        /// A target path for the WCI filter
        /// </summary>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WC_REPARSE_POINT_DATA
        {
            public uint Flags;
            public GUID LayerId;

            //
            // Virtual (i.e. relative to the layer root) name of the fully expanded
            // file NameLength is in characters and does not include a NULL character.
            //
            public uint NameLength;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 1)]
            public string Name;
        }

        /// <summary>
        /// Layer descriptor flags for WCI
        /// </summary>
        [Flags]
        public enum LayerDescriptorFlags : uint
        {
            None = 0x0,
            Dirty = 1,
            Base =  1 << 1,
            Host = 1 << 2,
            Sparse = 1 << 3,
            InheritSecurity = 1 << 4,
        }


        /// <summary>
        /// Mapping flags for the Bind filter
        /// </summary>
        [Flags]
        public enum BfSetupFilterFlags : long
        {
            BINDFLT_FLAG_READ_ONLY_MAPPING = 0x00000001,
            BINDFLT_FLAG_MERGED_BIND_MAPPING = 0x00000002,
            BINDFLT_FLAG_USE_CURRENT_SILO_MAPPING = 0x00000004,
            BINDFLT_FLAG_REPARSE_ON_FILES = 0x00000008,
        }

        /// <summary>
        /// Impersonation level
        /// </summary>
        public enum SecurityImpersonationLevel : uint
        {
            SecurityAnonymous,
            SecurityIdentification,
            SecurityImpersonation,
            SecurityDelegation
        }

        /// <summary>
        /// Requested priviledge
        /// </summary>
        public enum Priviledge : uint
        {
            SE_MIN_WELL_KNOWN_PRIVILEGE = SE_CREATE_TOKEN_PRIVILEGE,
            SE_CREATE_TOKEN_PRIVILEGE = 2,
            SE_ASSIGNPRIMARYTOKEN_PRIVILEGE = 3,
            SE_LOCK_MEMORY_PRIVILEGE = 4,
            SE_INCREASE_QUOTA_PRIVILEGE = 5,
            SE_MACHINE_ACCOUNT_PRIVILEGE = 6,
            SE_TCB_PRIVILEGE = 7,
            SE_SECURITY_PRIVILEGE = 8,
            SE_TAKE_OWNERSHIP_PRIVILEGE = 9,
            SE_LOAD_DRIVER_PRIVILEGE = 10,
            SE_SYSTEM_PROFILE_PRIVILEGE = 11,
            SE_SYSTEMTIME_PRIVILEGE = 12,
            SE_PROF_SINGLE_PROCESS_PRIVILEGE = 13,
            SE_INC_BASE_PRIORITY_PRIVILEGE = 14,
            SE_CREATE_PAGEFILE_PRIVILEGE = 15,
            SE_CREATE_PERMANENT_PRIVILEGE = 16,
            SE_BACKUP_PRIVILEGE = 17,
            SE_RESTORE_PRIVILEGE = 18,
            SE_SHUTDOWN_PRIVILEGE = 19,
            SE_DEBUG_PRIVILEGE = 20,
            SE_AUDIT_PRIVILEGE = 21,
            SE_SYSTEM_ENVIRONMENT_PRIVILEGE = 22,
            SE_CHANGE_NOTIFY_PRIVILEGE = 23,
            SE_REMOTE_SHUTDOWN_PRIVILEGE = 24,
            SE_UNDOCK_PRIVILEGE = 25,
            SE_SYNC_AGENT_PRIVILEGE = 26,
            SE_ENABLE_DELEGATION_PRIVILEGE = 27,
            SE_MANAGE_VOLUME_PRIVILEGE = 28,
            SE_IMPERSONATE_PRIVILEGE = 29,
            SE_CREATE_GLOBAL_PRIVILEGE = 30,
            SE_TRUSTED_CREDMAN_ACCESS_PRIVILEGE = 31,
            SE_RELABEL_PRIVILEGE = 32,
            SE_INC_WORKING_SET_PRIVILEGE = 33,
            SE_TIME_ZONE_PRIVILEGE = 34,
            SE_CREATE_SYMBOLIC_LINK_PRIVILEGE = 35,
            SE_MAX_WELL_KNOWN_PRIVILEGE = SE_CREATE_SYMBOLIC_LINK_PRIVILEGE
        }

        /// <nodoc/>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct GUID
        {
            public int a;
            public short b;
            public short c;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
            public byte[] d;
        }

        /// <nodoc/>
        public static GUID ToGuid(Guid guid)
        {
            var newGuid = new GUID();

            var guidData = guid.ToByteArray();

            newGuid.a = BitConverter.ToInt32(guidData, 0);
            newGuid.b = BitConverter.ToInt16(guidData, 4);
            newGuid.c = BitConverter.ToInt16(guidData, 6);

            newGuid.d = new byte[8];
            Array.Copy(guidData, 8, newGuid.d, 0, 8);

            return newGuid;
        }

        /// <summary>
        /// Tries to load a given filter driver
        /// </summary>
        /// <param name="strDriverName"></param>
        [DllImport(ExternDll.Fltlib, SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern int FilterLoad([In] [MarshalAs(UnmanagedType.LPWStr)] string strDriverName);

        /// <summary>
        /// Creates a custom container description from an XML string.
        /// </summary>
        [DllImport(ExternDll.Container, SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern int WcCreateDescriptionFromXml([In] [MarshalAs(UnmanagedType.LPWStr)] string xmlDescription, [Out] out IntPtr description);

        /// <summary>
        /// Enables or disables a privilege from the calling thread or process. 
        /// </summary>
        [DllImport(ExternDll.Ntdll, SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern int RtlAdjustPrivilege([In] Priviledge privilege, [In] bool bEnablePrivilege, [In] bool isThreadPrivilege, [Out] out bool previousValue);

        /// <summary>
        /// Makes an impersonation token that represents the process user and assigns to the current thread. 
        /// </summary>
        /// <remarks>
        /// See https://docs.microsoft.com/en-us/windows/desktop/api/securitybaseapi/nf-securitybaseapi-impersonateself
        /// </remarks>
        [DllImport(ExternDll.Ntdll, SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern int RtlImpersonateSelf([In] SecurityImpersonationLevel securityImpersonationLevel, [In] uint accessMask, [Out] out IntPtr threadToken);

        /// <summary>
        /// Terminates the impersonation of a client application.
        /// </summary>
        [DllImport(ExternDll.Advapi32, SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool RevertToSelf();

        /// <summary>
        /// Deletes a custom container description created by <see cref="WcCreateDescriptionFromXml(string, out IntPtr)"/>
        /// </summary>
        [DllImport(ExternDll.Container, SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern void WcDestroyDescription([In] IntPtr description);

        /// <summary>
        /// Creates a container given a custom container description.
        /// </summary>
        [DllImport(ExternDll.Container, SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern int WcCreateContainer([In] IntPtr jobHandle, [In] IntPtr description, [In] bool isServerSilo);

        /// <summary>
        /// This routine will clean up permanent artifacts associated with a given
        /// container that do not disappear with the job artifact. It should be
        /// called after a container is done running.
        /// </summary>
        [DllImport(ExternDll.Container, SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern int WcCleanupContainer([In] IntPtr jobHandle, [In] [MarshalAs(UnmanagedType.LPWStr)] string volume);

        /// <summary>
        /// Sets up the WCIFS filter for the silo specified by the JobHandle, with the specified IsolationMode.
        /// It also takes in a path to a scratch root and a list of Layer Descriptors to configure the mapping.
        /// </summary>
        [DllImport(ExternDll.Wcifs, SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern int WciSetupFilter(
            [In] IntPtr jobHandle, 
            [In] WC_ISOLATION_MODE isolationMode, 
            [In] [MarshalAs(UnmanagedType.LPWStr)] string scratchRootPath,
            [In] [MarshalAs(UnmanagedType.LPArray)] WC_LAYER_DESCRIPTOR[] layerDescriptions, 
            [In] uint layerCount, 
            [In] WC_NESTING_MODE nestingMode);

        /// <summary>
        /// This routine attaches Bind filter to sandbox volume
        /// </summary>
        [DllImport(ExternDll.Bindflt, SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern int BfSetupFilter(
            [In] IntPtr jobHandle, 
            [In] BfSetupFilterFlags flags, 
            [In] [MarshalAs(UnmanagedType.LPWStr)] string virtualizationRootPath,
            [In] [MarshalAs(UnmanagedType.LPWStr)] string virtualizationTargetPath, 
            [In] [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] virtualizationExceptionPaths, 
            [In] ulong pathCount);

        /// <summary>
        /// Removes a mapping from the Bind filter
        /// </summary>
        /// <remarks>
        /// Mappings should be removed before the container is cleaned up
        /// </remarks>
        [DllImport(ExternDll.Bindflt, SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern int BfRemoveMapping([In] IntPtr jobHandle, [MarshalAs(UnmanagedType.LPWStr)] string virtualizationPath);

        /// <summary>
        /// Changes an existing file to be a WCI reparse point file and sets the reparse point data.
        /// </summary>
        [DllImport(ExternDll.Wcifs, SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern int WciSetReparsePointData(
                [In] [MarshalAs(UnmanagedType.LPWStr)] string FilePath,
                [In] ref WC_REPARSE_POINT_DATA reparsePointData,
                [In] UInt16 DataSize
        );
    }
}
