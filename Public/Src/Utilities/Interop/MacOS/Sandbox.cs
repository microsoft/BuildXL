// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace BuildXL.Interop.MacOS
{
    /// <summary>
    /// The Sandbox class offers interop calls for sandbox based tasks into the macOS sandbox interop library
    /// </summary>
    public static class Sandbox
    {
        /// <nodoc />
        public static readonly int ReportQueueSuccessCode = 0x1000;

        /// <nodoc />
        public static readonly int SandboxSuccess = 0x0;

        /// <nodoc />
        [DllImport(Libraries.BuildXLInteropLibMacOS)]
        public static extern unsafe int NormalizePathAndReturnHash(byte[] pPath, byte* buffer, int bufferLength);

        /// <nodoc />
        [StructLayout(LayoutKind.Sequential)]
        public struct ESConnectionInfo
        {
            /// <nodoc />
            public int Error;

            /// <nodoc />
            public ulong Client;

            /// <nodoc />
            public ulong Source;

            /// <nodoc />
            public ulong RunLoop;
        }

        /// <nodoc />
        [DllImport(Libraries.BuildXLInteropLibMacOS, SetLastError = true)]
        public static extern void InitializeEndpointSecuritySandbox(ref ESConnectionInfo info, int host);

        /// <nodoc />
        [DllImport(Libraries.BuildXLInteropLibMacOS, SetLastError = true)]
        public static extern void DeinitializeEndpointSecuritySandbox(ESConnectionInfo info);

        /// <nodoc />
        [DllImport(Libraries.BuildXLInteropLibMacOS, CallingConvention=CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern void ObserverFileAccessReports(
            ref ESConnectionInfo info,
            [MarshalAs(UnmanagedType.FunctionPtr)] AccessReportCallback callbackPointer,
            long accessReportSize);
            
        /// <nodoc />
        [StructLayout(LayoutKind.Sequential)]
        public struct KextConnectionInfo
        {
            /// <nodoc />
            public int Error;

            /// <nodoc />
            public uint Connection;

            /// <summary>
            /// The end of the struct is used for handles to raw memory directly, so we can save and pass CoreFoundation types around
            /// between managed and unmanaged code
            /// </summary>
            private readonly ulong m_restricted1; // IONotificationPortRef
        }

        [DllImport(Libraries.BuildXLInteropLibMacOS, CallingConvention = CallingConvention.Cdecl)]
        private static extern void InitializeKextConnection(ref KextConnectionInfo info, long infoSize);

        /// <nodoc />
        public static void InitializeKextConnection(ref KextConnectionInfo info)
            => InitializeKextConnection(ref info, Marshal.SizeOf(info));

        /// <nodoc />
        [StructLayout(LayoutKind.Sequential)]
        public struct KextSharedMemoryInfo
        {
            /// <nodoc />
            public int Error;

            /// <nodoc />
            public ulong Address;

            /// <nodoc />
            public uint Port;
        }

        [DllImport(Libraries.BuildXLInteropLibMacOS, CallingConvention = CallingConvention.Cdecl)]
        private static extern void InitializeKextSharedMemory(ref KextSharedMemoryInfo memoryInfo, long memoryInfoSize, KextConnectionInfo info);

        /// <nodoc />
        public static void InitializeKextSharedMemory(KextConnectionInfo connectionInfo, ref KextSharedMemoryInfo memoryInfo)
            => InitializeKextSharedMemory(ref memoryInfo, Marshal.SizeOf(memoryInfo), connectionInfo);

        /// <nodoc />
        [DllImport(Libraries.BuildXLInteropLibMacOS, CallingConvention = CallingConvention.Cdecl)]
        public static extern void DeinitializeKextConnection(KextConnectionInfo info);

        /// <nodoc />
        [DllImport(Libraries.BuildXLInteropLibMacOS, CallingConvention = CallingConvention.Cdecl)]
        public static extern void DeinitializeKextSharedMemory(KextSharedMemoryInfo memoryInfo, KextConnectionInfo info);

        /// <nodoc />
        [DllImport(Libraries.BuildXLInteropLibMacOS, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        [return: MarshalAs(UnmanagedType.LPStr)]
        public static extern void KextVersionString(StringBuilder s, int size);

        /// <nodoc />
        [Flags]
        public enum ConnectionType
        {
            /// <nodoc />
            Kext,
            
            /// <nodoc />
            EndpointSecurity
        }

        /// <nodoc />
        [DllImport(Libraries.BuildXLInteropLibMacOS, EntryPoint = "SendPipStarted")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SendPipStarted(int processId, long pipId, byte[] famBytes, int famBytesLength, ConnectionType type, ref KextConnectionInfo info);

        /// <nodoc />
        [DllImport(Libraries.BuildXLInteropLibMacOS, EntryPoint = "SendPipProcessTerminated")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SendPipProcessTerminated(long pipId, int processId, ConnectionType type, ref KextConnectionInfo info);

        /// <nodoc />
        [DllImport(Libraries.BuildXLInteropLibMacOS, EntryPoint = "CheckForDebugMode")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CheckForDebugMode(ref bool isDebug, KextConnectionInfo info);

        /// <nodoc />
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
                    IsThresholdEnabled(CpuUsageBlockPercent);

                bool IsThresholdEnabled(uint percent) => percent > 0 && percent < 100;
            }
        }

        /// <nodoc />
        [StructLayout(LayoutKind.Sequential)]
        public struct KextConfig
        {
            /// <nodoc />
            public uint ReportQueueSizeMB;

            /// <nodoc />
            public bool EnableReportBatching;

            /// <nodoc />
            public ResourceThresholds ResourceThresholds;
            
            /// <nodoc />
            public bool EnableCatalinaDataPartitionFiltering;
        }

        /// <nodoc />
        [DllImport(Libraries.BuildXLInteropLibMacOS, EntryPoint = "Configure")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool Configure(KextConfig config, KextConnectionInfo info);

        /// <nodoc />
        [DllImport(Libraries.BuildXLInteropLibMacOS, EntryPoint = "UpdateCurrentResourceUsage")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UpdateCurrentResourceUsage(uint cpuUsageBasisPoints, uint availableRamMB, KextConnectionInfo info);

        private static readonly Encoding s_accessReportStringEncoding = Encoding.UTF8;

        /// <nodoc />
        [StructLayout(LayoutKind.Sequential)]
        public struct AccessReportStatistics
        {
            /// <nodoc />
            public ulong CreationTime;

            /// <nodoc />
            public ulong EnqueueTime;

            /// <nodoc />
            public ulong DequeueTime;
        }

        /// <nodoc />
        [StructLayout(LayoutKind.Sequential)]
        public struct AccessReport
        {
            /// <summary>Reported file operation.</summary>
            public FileOperation Operation;

            /// <summary>Process ID of the process making the accesses</summary>
            public int Pid;

            /// <summary>Process ID of the root pip process</summary>
            public int RootPid;

            /// <nodoc />
            public uint RequestedAccess;

            /// <nodoc />
            public uint Status;

            /// <nodoc />
            public uint ExplicitLogging;

            /// <nodoc />
            public uint Error;

            /// <nodoc />
            public long PipId;

            /// <nodoc />
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = Constants.MaxPathLength)]
            public string Path;

            /// <nodoc />
            public AccessReportStatistics Statistics;

            /// <nodoc />
            public string DecodeOperation() => Operation.GetName();
        }

        /// <nodoc />
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void AccessReportCallback(AccessReport report, int error);

        /// <nodoc />
        [DllImport(Libraries.BuildXLInteropLibMacOS, CallingConvention=CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        public static extern void ListenForFileAccessReports(
            [MarshalAs(UnmanagedType.FunctionPtr)] AccessReportCallback callbackPointer,
            long accessReportSize,
            ulong address,
            uint port);

        /// <summary>
        /// Callback the kernel extension can use to report any unrecoverable failures.
        ///
        /// This callback adhears to the IOAsyncCallback0 signature (see https://developer.apple.com/documentation/iokit/ioasynccallback0?language=objc)
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

        /// <nodoc />
        [DllImport(Libraries.BuildXLInteropLibMacOS, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool SetFailureNotificationHandler([MarshalAs(UnmanagedType.FunctionPtr)] NativeFailureCallback callback, KextConnectionInfo info);

        /// <nodoc />
        [DllImport(Libraries.BuildXLInteropLibMacOS)]
        public static extern ulong GetMachAbsoluteTime();
    }
}
