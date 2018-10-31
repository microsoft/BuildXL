// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Runtime.InteropServices;

namespace BuildXL.Native.Processes
{
    /// <nodoc />
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct PathMapping
    {
        [MarshalAs(UnmanagedType.U2)]
        private readonly char Drive;
        [MarshalAs(UnmanagedType.LPWStr)]
        private readonly string Path;

        /// <nodoc />
        public PathMapping(char drive, string path)
        {
            Drive = drive;
            Path = path;
        }
    }

    /// <summary>
    /// Contains information used to associate a completion port with a job.
    /// You can associate one completion port with a job.
    /// There is no way to terminate the association and no way to associate a different port with the job.
    /// </summary>
    /// <remarks>
    /// http://msdn.microsoft.com/en-us/library/windows/desktop/ms684141(v=vs.85).aspx
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    public struct JOBOBJECT_ASSOCIATE_COMPLETION_PORT
    {
        /// <nodoc />
        public IntPtr CompletionKey;

        /// <nodoc />
        public IntPtr CompletionPort;
    }

    /// <summary>
    /// Contains I/O accounting information for a process or a job object. For a job object, the counters include all
    /// operations performed by all processes that have ever been associated with the job, in addition to all processes
    /// currently associated with the job.
    /// http://msdn.microsoft.com/en-us/library/windows/desktop/ms684125(v=vs.85).aspx
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct IO_COUNTERS
    {
#pragma warning disable CS1591 // Missing XML comment
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
#pragma warning restore CS1591 // Missing XML comment
    }

    /// <summary>
    /// Contains basic and extended limit information for a job object.
    /// http://msdn.microsoft.com/en-us/library/windows/desktop/ms684156(v=vs.85).aspx
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
#pragma warning disable CS1591 // Missing XML comment
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public JOBOBJECT_LIMIT_FLAGS LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public long Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
#pragma warning restore CS1591 // Missing XML comment
    }

    /// <summary>
    /// Contains the process identifier list for a job object. If the job is nested, the process identifier list consists of
    /// all processes associated with the job and its child jobs.
    /// http://msdn.microsoft.com/en-us/library/windows/desktop/ms684150(v=vs.85).aspx
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct JOBOBJECT_BASIC_PROCESS_ID_LIST
    {
        /// <nodoc />
        public const int MaxProcessIdListLength = 256; // arbitrary number

        /// <nodoc />
        public static readonly int RequiredBufferSizeForMaxProcessIdListLength = Marshal.SizeOf<JOBOBJECT_BASIC_PROCESS_ID_LIST>() + (UIntPtr.Size * (MaxProcessIdListLength - 1));

        /// <nodoc />
        public uint NumberOfAssignedProcesses;

        /// <nodoc />
        public uint NumberOfProcessIdsInList;

        /// <summary>
        /// First element in an array of 32 or 64-bit representation process IDs. Note that it is unusual to store a PID with 64 bits; <c>GetProcessId</c> returns a 32-bit ID.
        /// We assume that each entry can be cast to fit in 32-bits.
        /// </summary>
        /// <remarks>
        /// Valid to access only if <see cref="NumberOfProcessIdsInList"/> is greater than zero.
        /// </remarks>
        public UIntPtr ProcessIdListFirst;
    }

    /// <summary>
    /// Contains basic accounting information for a job object.
    /// </summary>
    /// <remarks>
    /// http://msdn.microsoft.com/en-us/library/windows/desktop/ms684143(v=vs.85).aspx
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    public struct JOBOBJECT_BASIC_ACCOUNTING_INFORMATION
    {
#pragma warning disable CS1591 // Missing XML comment
        public ulong TotalUserTime;
        public ulong TotalKernelTime;
        public ulong ThisPeriodTotalUserTime;
        public ulong ThisPeriodTotalKernelTime;
        public uint TotalPageFaultCount;
        public uint TotalProcesses;
        public uint ActiveProcesses;
        public uint TotalTerminatedProcesses;
#pragma warning restore CS1591 // Missing XML comment
    }

    /// <summary>
    /// Contains basic accounting information and IO counters for a job object.
    /// </summary>
    /// <remarks>
    /// https://msdn.microsoft.com/en-us/library/windows/desktop/ms684144(v=vs.85).aspx
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    public struct JOBOBJECT_BASIC_AND_IO_ACCOUNTING_INFORMATION
    {
#pragma warning disable CS1591 // Missing XML comment
        public JOBOBJECT_BASIC_ACCOUNTING_INFORMATION BasicAccountingInformation;
        public IO_COUNTERS IOCounters;
#pragma warning restore CS1591 // Missing XML comment
    }
}
