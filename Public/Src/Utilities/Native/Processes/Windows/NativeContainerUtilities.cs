// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Runtime.InteropServices;
using BuildXL.Native.IO;

#nullable enable

namespace BuildXL.Native.Processes.Windows
{
#pragma warning disable CS1591 // Missing XML comment

    /// <summary>
    /// Contains low-level APIs that interact with WCI and Bind filter
    /// </summary>
    public static class NativeContainerUtilities
    {
        private static readonly string s_legacyBindDllLocation = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), ExternDll.BindfltLegacy);
        private static readonly string s_bindDllLocation = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), ExternDll.Bindflt);
        private static readonly bool s_isOfficialBindDllPresent = FileUtilities.Exists(s_bindDllLocation);

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
        public static readonly string WciDllLocation = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), ExternDll.Wcifs);

        /// <summary>
        /// Location of the user-mode DLL for the Bind driver
        /// </summary>
        /// <remarks>
        /// Older versions of the OS come with a DLL for bind with a deprecated name. If the current DLL name is not found on disk, we return the legacy one.
        /// </remarks>
        public static readonly string BindDllLocation = s_isOfficialBindDllPresent? s_bindDllLocation : s_legacyBindDllLocation;
        
        /// <summary>
        /// We depend on the RS6 user-mode DLLs to interact with WCI and Bind filters.
        /// 10.0.18301.1000 is the minimum required version.
        /// </summary>
        public static readonly Version MinimumRequiredVersion = Version.Parse("10.0.18301.1000");

        /// <summary>
        /// Isolation mode for a WCI filter instance.
        ///
        /// Sparse indicates that all layers in a session are sparse and should be automatically merged into
        /// a combined view; non-sparse indicates that layers are logically separate and that any file that
        /// should appear in a "higher" layer must have a WCI session specific reparse point set to allow the
        /// lower layer file to be visible.
        ///
        /// Hard isolation provides copy-on-write behavior from lower layers - when a file in the view is rewritten,
        /// file content from a lower layer is promoted into the writable layer for modification or append. Hard
        /// isolation also creates tombstones in the writable layer indicating deleted files.
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
        /// Used in placing a WCI reparse point into the real filesystem to map a file or directory to a
        /// corresponding relative path within a WCI virtualization layer.
        /// </summary>
        /// <remarks>
        /// This struct cannot be used for calling WciReadReparsePointData(), it will not correctly marshal.
        /// For such a case, allocate an unmanaged buffer to receive data, create a clone of this struct as a
        /// class, and use Marshal.PtrToStructure(unmanagedBuffer, instanceOfDataClass).
        /// </remarks>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WC_REPARSE_POINT_DATA
        {
            public const int MAX_PATH = 260;

            public uint Flags;

            /// <summary>
            /// The layer GUID to target. Typically this is set to the first/topmost layer in a set of layers,
            /// so that virtualization utilizes each layer in succession.
            /// </summary>
            public GUID LayerId;

            /// <summary>
            /// The number of characters in <see cref="Name"/> not including a null character.
            /// </summary>
            public ushort NameLength;

            /// <summary>
            /// Virtual (i.e. relative to the layer root) name of the fully expanded file.
            /// No terminating null character is needed.
            /// </summary>
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MAX_PATH)]
            public string Name;
        }

        /// <summary>
        /// Reparse point types. Some of these types translate to different tags
        /// based on the nesting mode.
        /// </summary>
        // Ref: wcifs.h
        public enum WC_REPARSE_POINT_TYPE
        {
            WcReparseTypePlaceholder,
            WcReparseTypeLink,
            WcReparseTypeTombstone,
            WcReparseTypeMaximum
        }

        /// <summary>
        /// Layer descriptor flags for WCI
        /// </summary>
        [Flags]
        public enum LayerDescriptorFlags : uint
        {
            None = 0x0,

            /// <summary>
            /// This layer was created from the sandbox as a result of a snapshot.
            /// </summary>
            Dirty = 1,

            /// <summary>
            /// This layer is considered part of the base image.
            /// </summary>
            Base =  1 << 1,

            /// <summary>
            /// Indicates this layer is the host operating system.
            /// </summary>
            Host = 1 << 2,

            /// <summary>
            /// This layer is sparse, meaning it merges with lower and higher layers to create a combined
            /// filesystem view. This value can also be set for a whole WCI session using <see cref="WC_ISOLATION_MODE"/>.
            /// </summary>
            Sparse = 1 << 3,

            /// <summary>
            /// Inherits security descriptors from lower layers.
            /// </summary>
            InheritSecurity = 1 << 4,
        }


        /// <summary>
        /// Mapping flags for the Bind filter
        /// </summary>
        [Flags]
        public enum BfSetupFilterFlags : long
        {
            None = 0x0,
        
            BINDFLT_FLAG_READ_ONLY_MAPPING = 0x00000001,

            /// <summary>
            /// Generates a merged binding, mapping target entries to the virtualization root.
            /// </summary>
            BINDFLT_FLAG_MERGED_BIND_MAPPING = 0x00000002,

            /// <summary>
            /// Use the binding mapping attached to the mapped-in job object instead of the default global mapping.
            /// </summary>
            BINDFLT_FLAG_USE_CURRENT_SILO_MAPPING = 0x00000004,

            BINDFLT_FLAG_REPARSE_ON_FILES = 0x00000008,

            /// <summary>
            /// Skips checks on file/dir creation inside a non-merged, read-only mapping.
            /// Only usable when READ_ONLY_MAPPING is set.
            /// </summary>
            BINDFLT_FLAG_SKIP_SHARING_CHECK = 0x00000010,

            BINDFLT_FLAG_CLOUD_FILES_ECPS = 0x00000020,

            /// <summary>
            /// Tells bindflt to fail mapping with STATUS_INVALID_PARAMETER if a mapping produces
            /// multiple targets.
            /// </summary>
            BINDFLT_FLAG_NO_MULTIPLE_TARGETS = 0x00000040,

            /// <summary>
            /// Turns on caching by asserting that the backing store for name mappings is immutable.
            /// </summary>
            BINDFLT_FLAG_IMMUTABLE_BACKING = 0x00000080,

            BINDFLT_FLAG_PREVENT_CASE_SENSITIVE_BINDING = 0x00000100,

            /// <summary>
            /// Tells bindflt to fail with STATUS_OBJECT_PATH_NOT_FOUND when a mapping is being added
            /// but its parent paths (ancestors) have not already been added.
            /// </summary>
            BINDFLT_FLAG_EMPTY_VIRT_ROOT = 0x00000200,

            BINDFLT_FLAG_NO_REPARSE_ON_ROOT = 0x10000000,
            BINDFLT_FLAG_BATCHED_REMOVE_MAPPINGS = 0x20000000,
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
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int FilterLoad([In] [MarshalAs(UnmanagedType.LPWStr)] string strDriverName);

        /// <summary>
        /// Creates a custom container description from an XML string.
        /// </summary>
        [DllImport(ExternDll.Container, SetLastError = true, CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int WcCreateDescriptionFromXml([In] [MarshalAs(UnmanagedType.LPWStr)] string xmlDescription, [Out] out IntPtr description);

        /// <summary>
        /// Enables or disables a privilege from the calling thread or process. 
        /// </summary>
        [DllImport(ExternDll.Ntdll, SetLastError = true, CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int RtlAdjustPrivilege([In] Priviledge privilege, [In] bool bEnablePrivilege, [In] bool isThreadPrivilege, [Out] out bool previousValue);

        /// <summary>
        /// Makes an impersonation token that represents the process user and assigns to the current thread. 
        /// </summary>
        /// <remarks>
        /// See https://docs.microsoft.com/en-us/windows/desktop/api/securitybaseapi/nf-securitybaseapi-impersonateself
        /// </remarks>
        [DllImport(ExternDll.Ntdll, SetLastError = true, CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int RtlImpersonateSelf([In] SecurityImpersonationLevel securityImpersonationLevel, [In] uint accessMask, [Out] out IntPtr threadToken);

        /// <summary>
        /// Terminates the impersonation of a client application.
        /// </summary>
        [DllImport(ExternDll.Advapi32, SetLastError = true, CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern bool RevertToSelf();

        /// <summary>
        /// Deletes a custom container description created by <see cref="WcCreateDescriptionFromXml(string, out IntPtr)"/>
        /// </summary>
        [DllImport(ExternDll.Container, SetLastError = true, CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern void WcDestroyDescription([In] IntPtr description);

        /// <summary>
        /// Adds container functionality to an existing job object.
        /// </summary>
        /// <returns>An HRESULT code.</returns>
        [DllImport(ExternDll.Container, SetLastError = true, CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int WcCreateContainer([In] IntPtr jobHandle, [In] IntPtr description, [In] bool isServerSilo);

        /// <summary>
        /// This routine will clean up permanent artifacts associated with a given
        /// container that do not disappear with the job object. It should be
        /// called after a container is done running.
        /// </summary>
        /// <param name="jobHandle">The job object handle for the container.</param>
        /// <param name="volume">
        /// Supplies the mount path of the sandbox volume, for detaching filesystem filters
        /// attached during WcCreateContainer(). If null is passed in, no detach is performed.
        /// </param>
        /// <returns>An HRESULT code.</returns>
        [DllImport(ExternDll.Container, SetLastError = true, CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int WcCleanupContainer([In] IntPtr jobHandle, [In] [MarshalAs(UnmanagedType.LPWStr)] string? volume);

        /// <summary>
        /// Sets up the WCIFS filter for the silo (job object) specified by the JobHandle, with the specified IsolationMode.
        /// It also takes in a path to a scratch root and a list of Layer Descriptors to configure the mapping.
        /// </summary>
        /// <param name="jobHandle">
        /// Handle to the job object associated with this isolation root or NULL/zero
        /// if the root is associated with the host silo.</param>
        /// <param name="isolationMode">The isolation mode of this container and its layers.</param>
        /// <param name="scratchRootPath">The container scratch root path.</param>
        /// <param name="layerDescriptions">
        /// Ordered layer information. Filesystem operations will occur in the scratch root followed
        /// by each successive layer in this list.
        /// </param>
        /// <param name="layerCount">The count of layers in <paramref name="layerDescriptions"/>.</param>
        /// <param name="nestingMode">Directs the file operation to the appropriate filter instance.</param>
        /// <returns>An HRESULT code.</returns>
        [DllImport(ExternDll.Wcifs, SetLastError = true, CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int WciSetupFilter(
            [In] IntPtr jobHandle, 
            [In] WC_ISOLATION_MODE isolationMode, 
            [In] [MarshalAs(UnmanagedType.LPWStr)] string scratchRootPath,
            [In] [MarshalAs(UnmanagedType.LPArray)] WC_LAYER_DESCRIPTOR[] layerDescriptions, 
            [In] uint layerCount, 
            [In] WC_NESTING_MODE nestingMode);

        /// <summary>
        /// <see cref="BfSetupFilterInternal(IntPtr, BfSetupFilterFlags, string, string, string[], ulong)"/>, this is a 
        /// pinvoke that uses a legacy version of bind dll.
        /// </summary>
        [DllImport(ExternDll.BindfltLegacy, SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "BfSetupFilter")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int BfSetupFilterLegacyInternal(
            [In] IntPtr jobHandle, 
            [In] BfSetupFilterFlags flags, 
            [In] [MarshalAs(UnmanagedType.LPWStr)] string virtualizationRootPath,
            [In] [MarshalAs(UnmanagedType.LPWStr)] string virtualizationTargetPath, 
            [In] [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] virtualizationExceptionPaths, 
            [In] ulong pathCount);

        /// <summary>
        /// This routine attaches Bind filter to sandbox volume
        /// </summary>
        [DllImport(ExternDll.Bindflt, SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "BfSetupFilter")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int BfSetupFilterInternal(
            [In] IntPtr jobHandle,
            [In] BfSetupFilterFlags flags,
            [In] [MarshalAs(UnmanagedType.LPWStr)] string virtualizationRootPath,
            [In] [MarshalAs(UnmanagedType.LPWStr)] string virtualizationTargetPath,
            [In] [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.LPWStr)] string[] virtualizationExceptionPaths,
            [In] ulong pathCount);

        /// <summary>
        /// <see cref="BfRemoveMappingInternal(IntPtr, string)"/>, this is a pinvoke that uses a legacy version of bind dll.
        /// </summary>
        [DllImport(ExternDll.BindfltLegacy, SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "BfRemoveMapping")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int BfRemoveMappingLegacyInternal([In] IntPtr jobHandle, [MarshalAs(UnmanagedType.LPWStr)] string virtualizationPath);

        /// <summary>
        /// Removes a mapping from the Bind filter
        /// </summary>
        /// <remarks>
        /// Mappings should be removed before the container is cleaned up
        /// </remarks>
        [DllImport(ExternDll.Bindflt, SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "BfRemoveMapping")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern int BfRemoveMappingInternal([In] IntPtr jobHandle, [MarshalAs(UnmanagedType.LPWStr)] string virtualizationPath);

        /// <summary>
        /// Delegate for <see cref="BfRemoveMapping"/>
        /// </summary>
        public delegate int BfRemoveMappingDelegate(
            IntPtr jobHandle, 
            string virtualizationPath);

        /// <summary>
        /// Delegate for <see cref="BfSetupFilter"/>
        /// </summary>
        public delegate int BfSetupFilterDelegate(IntPtr jobHandle,
            BfSetupFilterFlags flags,
            string virtualizationRootPath,
            string virtualizationTargetPath,
            string[] virtualizationExceptionPaths,
            ulong pathCount);

        /// <summary>
        /// <see cref="BfSetupFilterInternal(IntPtr, BfSetupFilterFlags, string, string, string[], ulong)"/>
        /// </summary>
        /// <remarks>
        /// This function points to either the official or legacy bind dll depending on what we found available on the OS
        /// </remarks>
        public static BfSetupFilterDelegate BfSetupFilter = s_isOfficialBindDllPresent ? (BfSetupFilterDelegate) BfSetupFilterInternal : BfSetupFilterLegacyInternal;

        /// <summary>
        /// <see cref="BfRemoveMappingInternal(IntPtr, string)"/>
        /// </summary>
        /// <remarks>
        /// This function points to either the official or legacy bind dll depending on what we found available on the OS
        /// </remarks>
        public static BfRemoveMappingDelegate BfRemoveMapping = s_isOfficialBindDllPresent ? (BfRemoveMappingDelegate) BfRemoveMappingInternal : BfRemoveMappingLegacyInternal;

        /// <summary>
        /// Changes an existing file to be a WCI reparse point file and sets the reparse point data.
        /// </summary>
        [DllImport(ExternDll.Wcifs, SetLastError = true, CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int WciSetReparsePointData(
            [In] [MarshalAs(UnmanagedType.LPWStr)] string FilePath,
            [In] ref WC_REPARSE_POINT_DATA reparsePointData,
            [In] UInt16 DataSize);

        /// <summary>
        /// Removes WCI reparse point data from the specified path.
        /// </summary>
        /// <param name="filePath">The path of the reparse point.</param>
        /// <param name="nestingMode">Directs the file operation to the appropriate filter instance.</param>
        /// <returns>An HRESULT.</returns>
        [DllImport(ExternDll.Wcifs, SetLastError = true, CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int WcRemoveReparseData(string filePath, WC_NESTING_MODE nestingMode);

        /// <summary>
        /// Flags used with <see cref="WciSetReparsePointDataEx"/>.
        /// </summary>
        [Flags]
        public enum SetReparsePointDataFlags : uint
        {
            None = 0x0,
            LinkPlaceholder = 1,
        }

        /// <summary>
        /// Creates a reparse point.
        /// </summary>
        [DllImport(ExternDll.Wcifs, SetLastError = true, CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int WciSetReparsePointDataEx(
            [In] [MarshalAs(UnmanagedType.LPWStr)] string FilePath,
            [In] ref WC_REPARSE_POINT_DATA reparsePointData,
            [In] UInt16 DataSize,
            [In] SetReparsePointDataFlags Flags,
            [In] WC_NESTING_MODE NestingMode);

        /// <summary>
        /// Creates a WCI tombstone reparse point for a file, indicating that a file is not present.
        /// Similar to a 'whiteout' in Linux, this immediately returns a file-not-found result when
        /// accessing this path.
        /// </summary>
        [DllImport(ExternDll.Wcifs, SetLastError = true, CharSet = CharSet.Unicode)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static extern int WciSetTombstone(
            [In] [MarshalAs(UnmanagedType.LPWStr)] string FilePath);
    }
}
