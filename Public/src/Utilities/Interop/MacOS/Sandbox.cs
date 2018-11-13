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
    public unsafe static class Sandbox
    {
        /// <nodoc />
        public static readonly int ReportQueueSuccessCode = 0x1000;

        /// <nodoc />
        public static readonly int KextSuccess = 0x0;

        /// <nodoc />
        [DllImport(Libraries.InteropLibMacOS, EntryPoint = "NormalizeAndHashPath")]
        public unsafe static extern int ExternNormalizeAndHashPath(byte* pPath, byte* buffer, int bufferLength);

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

        /// <nodoc />
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate KextConnectionInfo KextConnectionInfoCallback();

        /// <nodoc />
        [DllImport(Libraries.InteropLibMacOS, CallingConvention = CallingConvention.Cdecl)]
        public static extern void InitializeKextConnectionInfoCallback([MarshalAs(UnmanagedType.FunctionPtr)] KextConnectionInfoCallback callback);

        /// <nodoc />
        [DllImport(Libraries.InteropLibMacOS, CallingConvention = CallingConvention.Cdecl)]
        public static extern void InitializeKextConnection(KextConnectionInfo* info);

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

        /// <nodoc />
        [DllImport(Libraries.InteropLibMacOS, CallingConvention = CallingConvention.Cdecl)]
        public static extern void InitializeKextSharedMemory(KextSharedMemoryInfo* memoryInfo);

        /// <nodoc />
        [DllImport(Libraries.InteropLibMacOS, CallingConvention = CallingConvention.Cdecl)]
        public static extern void DeinitializeKextConnection();

        /// <nodoc />
        [DllImport(Libraries.InteropLibMacOS, CallingConvention = CallingConvention.Cdecl)]
        public static extern void DeinitializeKextSharedMemory(KextSharedMemoryInfo* memoryInfo);

        /// <nodoc />
        [DllImport(Libraries.InteropLibMacOS, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
        [return: MarshalAs(UnmanagedType.LPStr)]
        public static extern void KextVersionString(StringBuilder s, int size);

        /// <nodoc />
        [DllImport(Libraries.InteropLibMacOS, EntryPoint = "SendPipStarted")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public unsafe static extern bool SendPipStarted(int processId, long pipId, IntPtr famBytes, int famBytesLength);

        /// <nodoc />
        [DllImport(Libraries.InteropLibMacOS, EntryPoint = "SendPipProcessTerminated")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SendPipProcessTerminated(long pipId, int processId);

        /// <nodoc />
        [DllImport(Libraries.InteropLibMacOS, EntryPoint = "CheckForDebugMode")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CheckForDebugMode(bool *isDebugModeEnabled);

        /// <nodoc />
        [DllImport(Libraries.InteropLibMacOS, EntryPoint = "SetReportQueueSize")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetReportQueueSize(ulong reportQueueSizeMB);

        /// <nodoc />
        [StructLayout(LayoutKind.Sequential)]
        public struct AccessReport
        {
            /// <nodoc />
            public uint Type;

            /// <nodoc />
            [MarshalAs(UnmanagedType.ByValArray, SizeConst=64)]
            public byte[] Operation;

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
            public uint DesiredAccess;

            /// <nodoc />
            public uint ShareMode;

            /// <nodoc />
            public uint Disposition;

            /// <nodoc />
            public uint FlagsAndAttributes;

            /// <nodoc />
            public uint PathId;

            /// <nodoc />
            [MarshalAs(UnmanagedType.ByValArray, SizeConst=1024)]
            public byte[] Path;
        }

        /// <nodoc />
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void AccessReportCallback(AccessReport report, int error);

        /// <nodoc />
        [DllImport(Libraries.InteropLibMacOS, CallingConvention=CallingConvention.Cdecl)]
        public static extern void ListenForFileAccessReports([MarshalAs(UnmanagedType.FunctionPtr)] AccessReportCallback callbackPointer, ulong address, uint port);

        /// <summary>
        /// The FailureNotificationCallback adhears to the IOAsyncCallback0 signature (see https://developer.apple.com/documentation/iokit/ioasynccallback0?language=objc)
        /// We don't transfer any data from the sandbox kernel extension to the managed code when an unrecoverable error happens for now, this can potentially be extended later.
        /// </summary>
        /// <param name="refCon">The pointer to the callback itself</param>
        /// <param name="status">Error code indicating what failure happened</param>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        public delegate void FailureNotificationCallback(void *refCon, int status);

        /// <nodoc />
        [DllImport(Libraries.InteropLibMacOS, CallingConvention = CallingConvention.Cdecl)]
        public static extern bool SetFailureNotificationHandler([MarshalAs(UnmanagedType.FunctionPtr)] FailureNotificationCallback callback);
    }
}
