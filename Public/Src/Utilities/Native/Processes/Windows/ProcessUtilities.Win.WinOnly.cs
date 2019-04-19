// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Runtime.InteropServices;
using BuildXL.Native.IO.Windows;

namespace BuildXL.Native.Processes.Windows
{
#pragma warning disable CS1591 // Missing XML comment
#pragma warning disable CA1051 // Do not declare visible instance fields
#pragma warning disable CA1823 // Unused field
#pragma warning disable SA1139 // Use literal suffix notation instead of casting
#pragma warning disable SA1649 // File name must match first type name

    /// <summary>
    /// Some native methods for Windows only.
    /// </summary>
    public unsafe partial class ProcessUtilitiesWin
    {
        /// <nodoc />
        public enum NtStatus : uint
        {
            STATUS_SUCCESS = 0x00000000,
            STATUS_BUFFER_OVERFLOW = unchecked((uint)0x80000005L),
            STATUS_INFO_LENGTH_MISMATCH = unchecked((uint)0xC0000004L),
        }

        /// <nodoc />
        public enum SystemInforamtionClass
        {
            SystemBasicInformation = 0,
            SystemPerformanceInformation = 2,
            SystemTimeOfDayInformation = 3,
            SystemProcessInformation = 5,
            SystemProcessorPerformanceInformation = 8,
            SystemHandleInformation = 16,
            SystemInterruptInformation = 23,
            SystemExceptionInformation = 33,
            SystemRegistryQuotaInformation = 37,
            SystemLookasideInformation = 45,
        }

        /// <nodoc />
        public enum ObjectInformationClass
        {
            ObjectBasicInformation = 0,
            ObjectNameInformation = 1,
            ObjectTypeInformation = 2,
            ObjectAllTypesInformation = 3,
            ObjectHandleInformation = 4,
        }

        /// <summary>
        /// Specifies the type of information to be retrieved from NtQueryInformationProcess
        /// </summary>
        /// <remarks>
        /// https://msdn.microsoft.com/en-us/library/windows/desktop/ms684280(v=vs.85).aspx
        /// </remarks>
        public enum ProcessInformationClass
        {
            /// <summary>
            /// Retrieves a pointer to a PEB structure that can be used to determine whether the specified process is being debugged,
            /// and a unique value used by the system to identify the specified process.
            /// </summary>
            ProcessBasicInformation = 0,

            /// <summary>
            /// Retrieves a DWORD_PTR value that is the port number of the debugger for the process.
            /// A nonzero value indicates that the process is being run under the control of a ring 3 debugger.
            /// </summary>
            ProcessDebugPort = 7,

            /// <summary>
            /// Determines whether the process is running in the WOW64 environment (WOW64 is the x86 emulator that allows Win32-based applications to run on 64-bit Windows).
            /// </summary>
            ProcessWow64Information = 26,

            /// <summary>
            /// Retrieves a UNICODE_STRING value containing the name of the image file for the process.
            /// </summary>
            ProcessImageFileName = 27,

            /// <summary>
            /// Retrieves a ULONG value indicating whether the process is considered critical.
            /// </summary>
            ProcessBreakOnTermination = 29,

            /// <summary>
            /// Retrieves a SUBSYSTEM_INFORMATION_TYPE value indicating the subsystem type of the process. The buffer pointed to by the ProcessInformation parameter should be large enough to hold a single SUBSYSTEM_INFORMATION_TYPE enumeration.
            /// </summary>
            ProcessSubsystemInformation = 75,
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct SystemHandleTableEntryInfo
        {
            public ushort UniqueProcessId;
            public ushort CreatorBackTraceIndex;
            public byte ObjectTypeNumber;
            public byte HandleAttributes;
            public ushort HandleValue;
            public IntPtr ObjectPtr;
            public int GrantedAccess;
        }

        [DllImport(ExternDll.Ntdll, SetLastError = false)]
        public static extern uint NtQuerySystemInformation(
            SystemInforamtionClass systemInformationClass,
            IntPtr systemInformation, uint systemInformationLength, out uint returnLength);

        [DllImport(ExternDll.Ntdll, SetLastError = false)]
        public static extern uint NtQueryObject(IntPtr handle, ObjectInformationClass objectInformationClass,
            IntPtr objectInformation, uint objectInformationLength, out uint returnLength);

        /// <summary>
        /// Retrieves information about the specified process.
        /// </summary>
        /// <param name="handle">A handle to the process for which information is to be retrieved.</param>
        /// <param name="processInformationClass">The type of process information to be retrieved.
        /// This parameter can be one of the following values from the <see cref="ProcessInformationClass"/> enumeration.</param>
        /// <param name="processInformation">
        /// A pointer to a buffer supplied by the calling application into which the function writes the requested information.
        /// The size of the information written varies depending on the data type of the processInformationClass parameter:
        /// </param>
        /// <param name="processInformationLength">The size of the buffer pointed to by the processInformation parameter, in bytes.</param>
        /// <param name="returnLength">A pointer to a variable in which the function returns the size of the requested information. </param>
        /// <returns>Returns zero on success.</returns>
        /// <remarks>
        /// https://msdn.microsoft.com/en-us/library/windows/desktop/ms684280(v=vs.85).aspx
        /// </remarks>
        [DllImport(ExternDll.Ntdll, SetLastError = false)]
        public static extern uint NtQueryInformationProcess(SafeHandle handle, ProcessInformationClass processInformationClass,
            IntPtr processInformation, uint processInformationLength, out uint returnLength);

        /// <summary>
        /// Reads data from an area of memory in a specified process.
        /// </summary>
        /// <param name="handle">Handle to the process with memory that is being read. The handle must have PROCESS_VM_READ access to the process.</param>
        /// <param name="baseAddress">A pointer to the base address in the specified process from which to read.</param>
        /// <param name="buffer">A pointer to a buffer that receives the contents from the address space of the specified process.</param>
        /// <param name="size">The number of bytes to be read from the specified process.</param>
        /// <param name="bytesRead">A pointer to a variable that receives the number of bytes transferred into the specified buffer.</param>
        /// <returns>Returns true on success.</returns>
        /// <remarks>
        /// The entire area to be read must be accessible or the operation fails.
        /// https://msdn.microsoft.com/en-us/library/windows/desktop/ms680553(v=vs.85).aspx
        /// The output value bytesRead can be different than the value of the size
        /// if the requested read crosses into an area of the process that
        /// is inaccessible(and that was made inaccessible during the data
        /// transfer).  If this occurs a value of false is returned and
        /// GetLastError returns a "short read" error indicator.
        /// </remarks>
        [DllImport(ExternDll.Kernel32, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ReadProcessMemory(SafeHandle handle, IntPtr baseAddress, IntPtr buffer, ulong size, out ulong bytesRead);

        /// <summary>
        /// Reads a structure for the specified process's memory.
        /// </summary>
        /// <typeparam name="T">The type of the structure to read.</typeparam>
        /// <param name="processHandle">Handle to the process with memory that is being read.</param>
        /// <param name="address">A pointer to the address in the specified process from which to read.</param>
        /// <returns>Returns an initialized structure of type T on success, or a default T on failure.</returns>
        public static T ReadProcessStructure<T>(SafeHandle processHandle, IntPtr address) where T : struct
        {
            // Protect against calling with null. This allows subsequent calls to ReadProcessStructure
            // to be made without having to test previous results for zero. For example, reading
            // the command line from a process excution block (PEB) requires several process reads.
            // As long as the default(T) zero initializes IntPtr values (which it does) then
            // ReadProcessStructure can be called sequentially.
            if (address == IntPtr.Zero)
            {
                return default(T);
            }

            // Find the marshal size and allocate a buffer for it.
            // It is important to note that AllocHGlobal requires a FreeHGlobal after
            // the buffer has been converted into a managed object. This is
            // why there is a try\finally block.
            var typeSize = Marshal.SizeOf(typeof(T));
            var typePointer = Marshal.AllocHGlobal(typeSize);
            ulong bytesRead;

            // If we fail to read the memory, then we must return a default(T) to ensure
            // a property initialized result.
            T result = default(T);
            try
            {
                // ReadProcessMemory will return false if it cannot transfer all of the
                // data requested by typeSize. This can occur when ReadProcessMemory
                // cannot access the memory requestd in the process. Key takeaway,
                // the function will not return true unless it can read all of the
                // data requested.
                if (ReadProcessMemory(processHandle, address, typePointer, (ulong)typeSize, out bytesRead))
                {
                    Contract.Assert(bytesRead == (ulong)typeSize);
                    result = Marshal.PtrToStructure<T>(typePointer);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(typePointer);
            }

            return result;
        }

        /// <summary>
        /// Reads a unicode string from the specified process.
        /// </summary>
        /// <param name="processHandle">The process handle from where the string will be read.</param>
        /// <param name="unicodeString">The <seealso cref="UNICODE_STRING"/> to read.</param>
        /// <returns>Returns the string read from the process, or an empty string on failure.</returns>
        /// <remarks>
        /// Note that an empty string may also be returned if the string was indeed empty in the process.
        /// </remarks>
        public static string ReadProcessUnicodeString(SafeHandle processHandle, UNICODE_STRING unicodeString)
        {
            string result = string.Empty;
            if ((unicodeString.Length != 0) &&
                (unicodeString.Buffer != IntPtr.Zero))
            {
                var stringBuffer = Marshal.AllocHGlobal(unicodeString.Length);
                ulong stringBytesRead;
                try
                {
                    // ReadProcessMemory will return false if it cannot transfer all of the
                    // data requested by unicodeString.Length. This can occur when ReadProcessMemory
                    // cannot access the memory requestd in the process. Key takeaway,
                    // the function will not return true unless it can read all of the
                    // data requested.
                    if (ReadProcessMemory(processHandle, unicodeString.Buffer, stringBuffer, unicodeString.Length, out stringBytesRead))
                    {
                        result = Marshal.PtrToStringUni(stringBuffer, unicodeString.Length / 2);
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(stringBuffer);
                }
            }

            return result;
        }

        public static string ObjectName(IntPtr handle)
        {
            uint length = 1024;
            IntPtr ptr = IntPtr.Zero;
            try
            {
                ptr = Marshal.AllocHGlobal((int)length);
                NtStatus ret = (NtStatus)NtQueryObject(handle, ObjectInformationClass.ObjectNameInformation, ptr, length, out length);
                if (ret == NtStatus.STATUS_BUFFER_OVERFLOW)
                {
                    Marshal.FreeHGlobal(ptr);
                    ptr = Marshal.AllocHGlobal((int)length);
                    ret = (NtStatus)NtQueryObject(handle, ObjectInformationClass.ObjectNameInformation, ptr, length, out length);
                }

                if (ret == NtStatus.STATUS_SUCCESS)
                {
                    var us = Marshal.PtrToStructure<UNICODE_STRING>(ptr);
                    return Marshal.PtrToStringUni(us.Buffer, us.Length / 2);
                }
                else
                {
                    Debug.WriteLine("ObjectName: failed getting object name from handle, error " + ret);
                }
            }
            finally
            {
                if (ptr != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(ptr);
                }
            }

            return null;
        }

        /// <summary>
        /// Creates an input/output (I/O) completion port and associates it with a specified file handle,
        /// or creates an I/O completion port that is not yet associated with a file handle, allowing association at a later time.
        /// </summary>
        /// <remarks>
        /// http://msdn.microsoft.com/en-us/library/windows/desktop/aa363862(v=vs.85).aspx
        /// </remarks>
        [DllImport(ExternDll.Kernel32, EntryPoint = "CreateIoCompletionPort", SetLastError = true)]
        public static extern SafeIOCompletionPortHandle CreateIoCompletionPort(
            IntPtr handle,
            IntPtr existingCompletionPort,
            IntPtr completionKey,
            int numberOfConcurrentThreads);

        /// <summary>
        /// Attempts to dequeue an I/O completion packet from the specified I/O completion port. If there is no completion packet
        /// queued, the function waits for a pending I/O operation associated with the completion port to complete.
        /// </summary>
        /// <remarks>
        /// http://msdn.microsoft.com/en-us/library/windows/desktop/aa364986(v=vs.85).aspx
        /// </remarks>
        [DllImport(ExternDll.Kernel32, EntryPoint = "GetQueuedCompletionStatus", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern unsafe bool GetQueuedCompletionStatus(
            SafeIOCompletionPortHandle completionPort,
            out uint lpNumberOfBytes,
            out IntPtr lpCompletionKey,
            out IntPtr lpOverlapped,
            uint dwMilliseconds);

        /// <nodoc />
        public static bool FindFileAccessPolicyInTree(
            byte[] recordBytes,
            string absolutePath,
            UIntPtr absolutePathLength,
            out uint conePolicy,
            out uint nodePolicy,
            out uint pathId,
            out IO.Usn expectedUsn)
        {
            Assert64Process();

            GCHandle pinnedRecordArray = GCHandle.Alloc(recordBytes, GCHandleType.Pinned);
            try
            {
                IntPtr record = pinnedRecordArray.AddrOfPinnedObject();
                return ExternFindFileAccessPolicyInTree(
                    record,
                    absolutePath,
                    absolutePathLength,
                    out conePolicy,
                    out nodePolicy,
                    out pathId,
                    out expectedUsn);
            }
            finally
            {
                pinnedRecordArray.Free();
            }
        }

        /// <nodoc />
        [DllImport("kernel32.dll")]
        public static extern IntPtr GetConsoleWindow();

        /// <nodoc />
        [DllImport("dbghelp.dll", EntryPoint = "MiniDumpWriteDump", CallingConvention = CallingConvention.StdCall,
            CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool MiniDumpWriteDump(
            IntPtr hProcess,
            uint processId,
            SafeHandle hFile,
            uint dumpType,
            IntPtr expParam,
            IntPtr userStreamParam,
            IntPtr callbackParam);

        /// <summary>
        /// Defined: http://msdn.microsoft.com/en-us/library/windows/desktop/ms680519(v=vs.85).aspx
        /// </summary>
        [Flags]
        public enum MINIDUMP_TYPE : uint
        {
            MiniDumpNormal = 0x00000000,
            MiniDumpWithDataSegs = 0x00000001,
            MiniDumpWithFullMemory = 0x00000002,
            MiniDumpWithHandleData = 0x00000004,
            MiniDumpFilterMemory = 0x00000008,
            MiniDumpScanMemory = 0x00000010,
            MiniDumpWithUnloadedModules = 0x00000020,
            MiniDumpWithIndirectlyReferencedMemory = 0x00000040,
            MiniDumpFilterModulePaths = 0x00000080,
            MiniDumpWithProcessThreadData = 0x00000100,
            MiniDumpWithPrivateReadWriteMemory = 0x00000200,
            MiniDumpWithoutOptionalData = 0x00000400,
            MiniDumpWithFullMemoryInfo = 0x00000800,
            MiniDumpWithThreadInfo = 0x00001000,
            MiniDumpWithCodeSegs = 0x00002000,
            MiniDumpWithoutAuxiliaryState = 0x00004000,
            MiniDumpWithFullAuxiliaryState = 0x00008000,
            MiniDumpWithPrivateWriteCopyMemory = 0x00010000,
            MiniDumpIgnoreInaccessibleMemory = 0x00020000,
            MiniDumpWithTokenInformation = 0x00040000,
            MiniDumpWithModuleHeaders = 0x00080000,
            MiniDumpFilterTriage = 0x00100000,
            MiniDumpValidTypeFlags = 0x001fffff,
        }

        /// <summary>
        /// Contains information related to a unicode string
        /// </summary>
        /// <remarks>
        /// https://msdn.microsoft.com/en-us/library/windows/desktop/aa380518(v=vs.85).aspx
        /// </remarks>
        [StructLayout(LayoutKind.Sequential)]
        public struct UNICODE_STRING
        {
            /// <summary>
            /// Length (in bytes) of string stored in buffer. Note it may NOT be null terminated.
            /// </summary>
            public ushort Length;

            /// <summary>
            /// Maximum size that buffer can be written.
            /// </summary>
            public ushort MaximumLength;

            /// <summary>
            /// String buffer.
            /// </summary>
            public IntPtr Buffer;
        }

        /// <summary>
        /// Contains information for a process runtime parameters (retrieved from PEB)
        /// </summary>
        /// <remarks>
        /// https://msdn.microsoft.com/en-us/library/windows/desktop/aa813741(v=vs.85).aspx
        /// </remarks>
        [StructLayout(LayoutKind.Sequential)]
        public struct RTL_USER_PROCESS_PARAMETERS
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
            private readonly byte[] Reserved1;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 10)]
            private readonly IntPtr[] Reserved2;

            /// <summary>
            /// Unicode string representing the image file used for this process.
            /// </summary>
            public UNICODE_STRING ImagePathName;

            /// <summary>
            /// The command line used to start the process
            /// </summary>
            /// <remarks>
            /// Note that the command line includes both the image file path and the arguments.
            /// </remarks>
            public UNICODE_STRING CommandLine;
        }

        /// <summary>
        /// Contains information for a process execution block.
        /// </summary>
        /// <remarks>
        /// https://msdn.microsoft.com/en-us/library/windows/desktop/aa813706(v=vs.85).aspx
        /// </remarks>
        [StructLayout(LayoutKind.Sequential)]
        public struct PEB
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
            private readonly byte[] Reserved;

            /// <summary>
            /// Indicates whether the specified process is currently being debugged.
            /// </summary>
            public byte BeingDebugged;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
            private readonly byte[] Reserved2;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
            private readonly IntPtr[] Reserved3;

            /// <summary>
            /// A pointer to a PEB_LDR_DATA structure that contains information about the loaded modules for the process.
            /// </summary>
            public IntPtr Ldr;

            /// <summary>
            /// A pointer to an RTL_USER_PROCESS_PARAMETERS structure that contains process parameter information such as the command line.
            /// </summary>
            public IntPtr ProcessParameters;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 3)]
            private readonly IntPtr[] Reserved4;
            private readonly IntPtr AtlThunkSListPtr;
            private readonly IntPtr Reserved5;
            private readonly uint Reserved6;
            private readonly IntPtr Reserved7;
            private readonly uint Reserved8;
            private readonly uint AtlThunkSListPtr32;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 45)]
            private readonly IntPtr[] Reserved9;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 96)]
            private readonly byte[] Reserved10;
            private readonly IntPtr PostProcessInitRoutine;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)]
            private readonly byte[] Reserved11;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
            private readonly IntPtr[] Reserved12;
            public uint SessionId;
        }

        /// <summary>
        /// Contains information for basic process information.
        /// </summary>
        /// <remarks>
        /// https://msdn.microsoft.com/en-us/library/windows/desktop/ms684280(v=vs.85).aspx
        /// </remarks>
        [StructLayout(LayoutKind.Sequential)]
        public struct PROCESS_BASIC_INFORMATION
        {
            private readonly IntPtr Reserved1;

            /// <summary>
            /// Pointer to a PEB structure.
            /// </summary>
            public IntPtr PebBaseAddress;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
            private readonly IntPtr[] Reserved2;

            /// <summary>
            /// System's unique identifier for this process.
            /// </summary>
            public ulong UniqueProcessId;

            private readonly IntPtr Reserved3;
        }
    }

#pragma warning restore SA1139 // Use literal suffix notation instead of casting
#pragma warning restore SA1649 // File name must match first type name
#pragma warning restore CA1051 // Do not declare visible instance fields
#pragma warning restore CA1823 // Unused field
#pragma warning disable CS1591 // Missing XML comment
}
