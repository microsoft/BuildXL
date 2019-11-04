// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Runtime.InteropServices;
using System.Text;

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member 

namespace BuildXL.Interop.MacOS
{
    /// <summary>
    /// The Sandbox class offers interop calls for sandbox based tasks into the macOS sandbox interop library
    /// </summary>
    public static class Sandbox
    {
        public static readonly int ReportQueueSuccessCode = 0x1000;
        public static readonly int SandboxSuccess = 0x0;

        [DllImport(Libraries.BuildXLInteropLibMacOS)]
        public static extern unsafe int NormalizePathAndReturnHash(byte[] pPath, byte* buffer, int bufferLength);

        [StructLayout(LayoutKind.Sequential)]
        public struct ESConnectionInfo
        {
            public int Error;
            public ulong Client;
            public ulong Source;
            public ulong RunLoop;
        }

        [DllImport(Libraries.BuildXLInteropLibMacOS, SetLastError = true)]
        public static extern void InitializeEndpointSecuritySandbox(ref ESConnectionInfo info, int host);

        [DllImport(Libraries.BuildXLInteropLibMacOS, SetLastError = true)]
        public static extern void DeinitializeEndpointSecuritySandbox(ESConnectionInfo info);

        [DllImport(Libraries.BuildXLInteropLibMacOS, CallingConvention=CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern void ObserverFileAccessReports(
            ref ESConnectionInfo info,
            [MarshalAs(UnmanagedType.FunctionPtr)] AccessReportCallback callbackPointer,
            long accessReportSize);

        [StructLayout(LayoutKind.Sequential)]
        public struct KextConnectionInfo
        {
            public int Error;
            public uint Connection;

            /// <summary>
            /// The end of the struct is used for handles to raw memory directly, so we can save and pass CoreFoundation types around
            /// between managed and unmanaged code
            /// </summary>
            private readonly ulong m_restricted1; // IONotificationPortRef
        }

        [DllImport(Libraries.BuildXLInteropLibMacOS, CallingConvention = CallingConvention.Cdecl)]
        private static extern void InitializeKextConnection(ref KextConnectionInfo info, long infoSize);

        public static void InitializeKextConnection(ref KextConnectionInfo info)
            => InitializeKextConnection(ref info, Marshal.SizeOf(info));

        [StructLayout(LayoutKind.Sequential)]
        public struct KextSharedMemoryInfo
        {
            public int Error;
            public ulong Address;
            public uint Port;
        }

        [DllImport(Libraries.BuildXLInteropLibMacOS, CallingConvention = CallingConvention.Cdecl)]
        private static extern void InitializeKextSharedMemory(ref KextSharedMemoryInfo memoryInfo, long memoryInfoSize, KextConnectionInfo info);

        public static void InitializeKextSharedMemory(KextConnectionInfo connectionInfo, ref KextSharedMemoryInfo memoryInfo)
            => InitializeKextSharedMemory(ref memoryInfo, Marshal.SizeOf(memoryInfo), connectionInfo);

        [DllImport(Libraries.BuildXLInteropLibMacOS, CallingConvention = CallingConvention.Cdecl)]
        public static extern void DeinitializeKextConnection(KextConnectionInfo info);

        [DllImport(Libraries.BuildXLInteropLibMacOS, CallingConvention = CallingConvention.Cdecl)]
        public static extern void DeinitializeKextSharedMemory(KextSharedMemoryInfo memoryInfo, KextConnectionInfo info);

        [DllImport(Libraries.BuildXLInteropLibMacOS, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        [return: MarshalAs(UnmanagedType.LPStr)]
        public static extern void KextVersionString(StringBuilder s, int size);

        [Flags]
        public enum ConnectionType
        {
            Kext,
            EndpointSecurity
        }

        [DllImport(Libraries.BuildXLInteropLibMacOS, EntryPoint = "SendPipStarted")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SendPipStarted(int processId, long pipId, byte[] famBytes, int famBytesLength, ConnectionType type, ref KextConnectionInfo info);

        [DllImport(Libraries.BuildXLInteropLibMacOS, EntryPoint = "SendPipProcessTerminated")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SendPipProcessTerminated(long pipId, int processId, ConnectionType type, ref KextConnectionInfo info);

        [DllImport(Libraries.BuildXLInteropLibMacOS, EntryPoint = "CheckForDebugMode")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CheckForDebugMode(ref bool isDebug, KextConnectionInfo info);

        [StructLayout(LayoutKind.Sequential)]
        public struct ResourceThresholds
        {
            /// <summary>
            /// A process can be blocked if the current CPU usage becomes higher than this value.
            /// </summary>
            public uint CpuUsageBlockPercent;

            /// <summary>
            /// A blocked process can be awakened only when CPU usage is below this value.
            /// Defaults to <see cref="CpuUsageBlockPercent"/> when set to 0.
            /// </summary>
            public uint CpuUsageWakeupPercent;

            /// <summary>
            /// Always block processes when available RAM drops below this threshold (takes precedence over CPU usage).
            /// </summary>
            public uint MinAvailableRamMB;

            /// <summary>
            /// Returns whether these resource threshold parameters enable process throttling or not.
            /// </summary>
            public bool IsProcessThrottlingEnabled()
            {
                return
                    MinAvailableRamMB > 0 ||
                    isThresholdEnabled(CpuUsageBlockPercent);

                static bool isThresholdEnabled(uint percent) => percent > 0 && percent < 100;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KextConfig
        {
            public uint ReportQueueSizeMB;
            public bool EnableReportBatching;
            public ResourceThresholds ResourceThresholds;
            public bool EnableCatalinaDataPartitionFiltering;
        }

        [DllImport(Libraries.BuildXLInteropLibMacOS, EntryPoint = "Configure")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool Configure(KextConfig config, KextConnectionInfo info);

        [DllImport(Libraries.BuildXLInteropLibMacOS, EntryPoint = "UpdateCurrentResourceUsage")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UpdateCurrentResourceUsage(uint cpuUsageBasisPoints, uint availableRamMB, KextConnectionInfo info);

        private static readonly Encoding s_accessReportStringEncoding = Encoding.UTF8;

        [StructLayout(LayoutKind.Sequential)]
        public struct AccessReportStatistics
        {
            public ulong CreationTime;
            public ulong EnqueueTime;
            public ulong DequeueTime;
        }

        /// <summary>
        /// Struct sent in place of <see cref="AccessReport.PathOrPipStats"/> in case of a 
        /// <see cref="FileOperation.OpProcessTreeCompleted"/> operation.
        /// </summary>
        /// <remarks>
        /// CODESYNC: Public/Src/Sandbox/MacOs/Sandbox/Src/BuildXLSandboxShared.hpp
        /// </remarks>
        [StructLayout(LayoutKind.Explicit, Size = 1024)]
        public struct PipKextStats 
        {
            [MarshalAs(UnmanagedType.U4)][FieldOffset(0)]
            public uint LastPathLookupElemCount;

            [MarshalAs(UnmanagedType.U4)][FieldOffset(4)]
            public uint LastPathLookupNodeCount;

            [MarshalAs(UnmanagedType.U4)][FieldOffset(8)]
            public uint LastPathLookupNodeSize;

            [MarshalAs(UnmanagedType.U4)][FieldOffset(12)]
            public uint NumCacheHits;

            [MarshalAs(UnmanagedType.U4)][FieldOffset(16)]
            public uint NumCacheMisses;

            [MarshalAs(UnmanagedType.U4)][FieldOffset(20)]
            public uint CacheRecordCount;

            [MarshalAs(UnmanagedType.U4)][FieldOffset(24)]
            public uint CacheRecordSize;

            [MarshalAs(UnmanagedType.U4)][FieldOffset(28)]
            public uint CacheNodeCount;

            [MarshalAs(UnmanagedType.U4)][FieldOffset(32)]
            public uint CacheNodeSize;

            [MarshalAs(UnmanagedType.U4)][FieldOffset(36)]
            public uint NumForks;

            [MarshalAs(UnmanagedType.U4)][FieldOffset(40)]
            public uint NumHardLinkRetries;
        } 

        /// <remarks>
        /// CODESYNC: Public/Src/Sandbox/MacOs/Sandbox/Src/BuildXLSandboxShared.hpp
        /// </remarks>
        [StructLayout(LayoutKind.Sequential)]
        public struct AccessReport
        {
            /// <summary>Reported file operation.</summary>
            public FileOperation Operation;

            /// <summary>Process ID of the process making the accesses</summary>
            public int Pid;

            /// <summary>Process ID of the root pip process</summary>
            public int RootPid;

            public uint RequestedAccess;
            public uint Status;
            public uint ExplicitLogging;
            public uint Error;
            public long PipId;

            /// <summary>
            /// Corresponds to a <c>union { char path[MAXPATHLEN]; PipCompletionStats pipStats; }</c> C type.
            /// Use <see cref="DecodePath"/> and <see cref="DecodePipKextStats"/> method to decode this value
            /// into either a path string or a <see cref="PipKextStats"/> structure.
            /// </summary>
            [MarshalAs(UnmanagedType.ByValArray, SizeConst=Constants.MaxPathLength)]
            public byte[] PathOrPipStats;

            public AccessReportStatistics Statistics;

            public string DecodeOperation() => Operation.GetName();

            /// <summary>
            /// Interprets <see cref="PathOrPipStats"/> as a 0-terminated UTF8-encoded string.
            /// </summary>
            public string DecodePath()
            {
                return s_accessReportStringEncoding.GetString(PathOrPipStats).TrimEnd('\0');
            }

            /// <summary>
            /// Encodes a given string into a byte array of a given size.
            /// </summary>
            public static byte[] EncodePath(string path, int bufferLength = Constants.MaxPathLength)
            {
                byte[] result = new byte[bufferLength];
                s_accessReportStringEncoding.GetBytes(path, charIndex: 0, charCount: path.Length, bytes: result, byteIndex: 0);
                return result;
            }

            /// <summary>
            /// Interprets <see cref="PathOrPipStats"/> as an instance of the <see cref="PipKextStats"/> struct.
            /// </summary>
            public PipKextStats DecodePipKextStats()
            {
                var handle = GCHandle.Alloc(PathOrPipStats, GCHandleType.Pinned);
                try
                {
                    return (PipKextStats)Marshal.PtrToStructure(handle.AddrOfPinnedObject(), typeof(PipKextStats));
                }
                finally
                {
                    handle.Free();
                }
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void AccessReportCallback(AccessReport report, int error);

        [DllImport(Libraries.BuildXLInteropLibMacOS, CallingConvention=CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern void ListenForFileAccessReports(
            [MarshalAs(UnmanagedType.FunctionPtr)] AccessReportCallback callbackPointer,
            long accessReportSize,
            ulong address,
            uint port);

        /// <summary>
        /// Callback the kernel extension can use to report any unrecoverable failures.
        ///
        /// This callback adheres to the IOAsyncCallback0 signature (see https://developer.apple.com/documentation/iokit/ioasynccallback0?language=objc)
        /// We don't transfer any data from the sandbox kernel extension to the managed code when an unrecoverable error happens for now, this can potentially be extended later.
        /// </summary>
        /// <param name="refCon">The pointer to the callback itself</param>
        /// <param name="status">Error code indicating what failure happened</param>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public unsafe delegate void NativeFailureCallback(void *refCon, int status);

        /// <summary>
        /// Callback the SandboxConnection uses to report any unrecoverable failure back to
        /// the scheduler (which, in response, should then terminate the build).
        /// </summary>
        /// <param name="status">Error code indicating what failure happened</param>
        /// <param name="description">Arbitrary description</param>
        public delegate void ManagedFailureCallback(int status, string description);

        [DllImport(Libraries.BuildXLInteropLibMacOS, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool SetFailureNotificationHandler([MarshalAs(UnmanagedType.FunctionPtr)] NativeFailureCallback callback, KextConnectionInfo info);

        [DllImport(Libraries.BuildXLInteropLibMacOS)]
        public static extern ulong GetMachAbsoluteTime();
    }
}

#pragma warning restore CS1591