// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

#pragma warning disable CS1591 // Missing XML comment

namespace BuildXL.Native.Processes
{
    /// <summary>
    /// Indicates the variety of failure encountered while creating a 'detached' server process.
    /// See <see cref="Native.Processes.ProcessUtilities.CreateDetachedProcess"/> and <c>DetoursServices.cpp</c>
    /// </summary>
    public enum CreateDetachedProcessStatus : int
    {
        /// <nodoc />
        Succeeded = 0,

        /// <summary>
        /// Generic failure.
        /// </summary>
        ProcessCreationFailed = 1,

        /// <summary>
        /// Process creation failed since the new process could not escape a containing job object.
        /// </summary>
        JobBreakwayFailed = 2,
    }

    /// <summary>
    /// Indicates the variety of failure encountered while creating and detouring a new build process
    /// (doing so is a multi-step process in which some but not all failures suggest a BuildXL vs. user issue).
    /// This is indication is returned from native Detours and must be kept in sync. See DetoursServices.h
    /// </summary>
    public enum CreateDetouredProcessStatus : int
    {
        /// <nodoc />
        Succeeded = 0,

        /// <summary>
        /// Failed to create a process before trying to detour it. This failure is likely to have occurred even without detours intervention.
        /// </summary>
        ProcessCreationFailed = 1,

        /// <summary>
        /// Process created, but injection of the detours library failed.
        /// </summary>
        DetouringFailed = 2,

        /// <summary>
        /// Process was created, but was not assigned to the requested job object.
        /// </summary>
        JobAssignmentFailed = 3,

        /// <summary>
        /// Configuration of explicit inheritance of the provided handles failed.
        /// </summary>
        HandleInheritanceFailed = 4,

        /// <summary>
        /// Failed to resume the process (originally suspended) after detouring.
        /// </summary>
        ProcessResumeFailed = 5,

        /// <summary>
        /// Detours was injected, but configuring the new detours (file access manifest, etc.) failed.
        /// </summary>
        PayloadCopyFailed = 6,

        /// <summary>
        /// The process should be launched in a silo, but the creation process failed
        /// </summary>
        AddProcessToSiloFailed = 7,

        /// <summary>
        /// Creation of the process attribute list failed
        /// </summary>
        CreateProcessAttributeListFailed = 8
    }

    /// <summary>
    /// See http://msdn.microsoft.com/en-us/library/windows/desktop/aa365150(v=vs.85).aspx
    /// </summary>
    [Flags]
    public enum PipeOpenMode : uint
    {
        /// <nodoc />
        PipeAccessDuplex = 0x3,

        /// <nodoc />
        PipeAccessInbound = 0x1,

        /// <nodoc />
        PipeAccessOutbound = 0x2,

        /// <nodoc />
        FileFlagFirstPipeInstance = 0x80000,

        /// <nodoc />
        FileFlagWriteThrough = 0x80000000,

        /// <nodoc />
        FileFlagOverlapped = 0x40000000,
    }

    /// <summary>
    /// See http://msdn.microsoft.com/en-us/library/windows/desktop/aa365150(v=vs.85).aspx
    /// </summary>
    [Flags]
    public enum PipeMode : uint
    {
        /// <nodoc />
        PipeTypeByte = 0x0,

        /// <nodoc />
        PipeTypeMessage = 0x4,

        /// <nodoc />
        PipeReadmodeMessage = 0x2,

        /// <nodoc />
        PipeRejectRemoteClients = 0x8,
    }

    /// <summary>
    /// The Microsoft Windows security model enables you to control access to process objects.
    /// </summary>
    /// <remarks>
    /// http://msdn.microsoft.com/en-us/library/windows/desktop/ms684880(v=vs.85).aspx
    /// </remarks>
    [Flags]
    public enum ProcessSecurityAndAccessRights : uint
    {
        /// <summary>
        /// Required to retrieve certain information about a process, such as its token, exit code, and priority class.
        /// </summary>
        PROCESS_QUERY_INFORMATION = 0x0400,

        /// <summary>
        /// Required to read memory in a process using ReadProcessMemory.
        /// </summary>
        PROCESS_VM_READ = 0x0010,

        /// <summary>
        /// Required to duplicate a handle using DuplicateHandle.
        /// </summary>
        PROCESS_DUP_HANDLE = 0x0040,
    }

    /// <summary>
    /// The information class for the limits to be set.
    /// </summary>
    public enum JOBOBJECTINFOCLASS
    {
        JobObjectBasicAccountingInformation = 1,
        JobObjectBasicAndIOAccountingInformation = 8,
        AssociateCompletionPortInformation = 7,
        BasicLimitInformation = 2,
        JobObjectBasicProcessIdList = 3,
        BasicUIRestrictions = 4,
        EndOfJobTimeInformation = 6,
        ExtendedLimitInformation = 9,
        SecurityLimitInformation = 5,
        GroupInformation = 11,
    }

    /// <summary>
    /// See http://msdn.microsoft.com/en-us/library/windows/desktop/ms684147(v=vs.85).aspx
    /// </summary>
    [Flags]
    public enum JOBOBJECT_LIMIT_FLAGS : uint
    {
        JOB_OBJECT_LIMIT_WORKINGSET = 0x1,
        JOB_OBJECT_LIMIT_PROCESS_TIME = 0x2,
        JOB_OBJECT_LIMIT_JOB_TIME = 0x4,
        JOB_OBJECT_LIMIT_ACTIVE_PROCESS = 0x8,
        JOB_OBJECT_LIMIT_AFFINITY = 0x10,
        JOB_OBJECT_LIMIT_PRIORITY_CLASS = 0x20,
        JOB_OBJECT_LIMIT_PRESERVE_JOB_TIME = 0x40,
        JOB_OBJECT_LIMIT_SCHEDULING_CLASS = 0x80,
        JOB_OBJECT_LIMIT_PROCESS_MEMORY = 0x100,
        JOB_OBJECT_LIMIT_JOB_MEMORY = 0x200,
        JOB_OBJECT_LIMIT_DIE_ON_UNHANDLED_EXCEPTION = 0x400,
        JOB_OBJECT_LIMIT_BREAKAWAY_OK = 0x800,
        JOB_OBJECT_LIMIT_SILENT_BREAKAWAY_OK = 0x1000,
        JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000,
        JOB_OBJECT_LIMIT_SUBSET_AFFINITY = 0x4000,
    }
}
#pragma warning restore CS1591 // Missing XML comment
