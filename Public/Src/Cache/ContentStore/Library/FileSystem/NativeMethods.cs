// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using Microsoft.Win32.SafeHandles;

#pragma warning disable SA1600 // Elements must be documented
#pragma warning disable SA1602 // Enumeration items must be documented

namespace BuildXL.Cache.ContentStore.FileSystem
{
    // ReSharper disable InconsistentNaming
    // ReSharper disable UnusedMember.Global

    /// <summary>
    ///     Class that contains method calls from unmanaged libraries.
    /// </summary>
    internal static class NativeMethods
    {
        // BEGIN copied from CLR ndp\clr\src\bcl\mscorlib.csproj
        private const string KERNEL32 = "kernel32.dll";

        // Error codes from WinError.h
        internal const int ERROR_SUCCESS = 0x0;
        internal const int ERROR_INVALID_FUNCTION = 0x1;
        internal const int ERROR_FILE_NOT_FOUND = 0x2;
        internal const int ERROR_PATH_NOT_FOUND = 0x3;
        internal const int ERROR_ACCESS_DENIED = 0x5;
        internal const int ERROR_INVALID_HANDLE = 0x6;
        internal const int ERROR_NOT_ENOUGH_MEMORY = 0x8;
        internal const int ERROR_INVALID_DATA = 0xd;
        internal const int ERROR_INVALID_DRIVE = 0xf;
        internal const int ERROR_NO_MORE_FILES = 0x12;
        internal const int ERROR_NOT_READY = 0x15;
        internal const int ERROR_BAD_LENGTH = 0x18;
        internal const int ERROR_SHARING_VIOLATION = 0x20;
        internal const int ERROR_HANDLE_EOF = 0x26;
        internal const int ERROR_NOT_SUPPORTED = 0x32;
        internal const int ERROR_FILE_EXISTS = 0x50;
        internal const int ERROR_INVALID_PARAMETER = 0x57;
        internal const int ERROR_BROKEN_PIPE = 0x6D;
        internal const int ERROR_CALL_NOT_IMPLEMENTED = 0x78;
        internal const int ERROR_INSUFFICIENT_BUFFER = 0x7A;
        internal const int ERROR_INVALID_NAME = 0x7B;
        internal const int ERROR_BAD_PATHNAME = 0xA1;
        internal const int ERROR_ALREADY_EXISTS = 0xB7;
        internal const int ERROR_ENVVAR_NOT_FOUND = 0xCB;
        internal const int ERROR_FILENAME_EXCED_RANGE = 0xCE; // filename too long.
        internal const int ERROR_NO_DATA = 0xE8;
        internal const int ERROR_PIPE_NOT_CONNECTED = 0xE9;
        internal const int ERROR_MORE_DATA = 0xEA;
        internal const int ERROR_DIRECTORY = 0x10B;
        internal const int ERROR_ABANDONED_WAIT0 = 0x2DF;
        internal const int ERROR_OPERATION_ABORTED = 0x3E3; // 995; For IO Cancellation
        internal const int ERROR_IO_INCOMPLETE = 0x3E4;
        internal const int ERROR_IO_PENDING = 0x3E5;
        internal const int ERROR_NOT_FOUND = 0x490; // 1168; For IO Cancellation
        internal const int ERROR_NO_TOKEN = 0x3f0;
        internal const int ERROR_DLL_INIT_FAILED = 0x45A;
        internal const int ERROR_NON_ACCOUNT_SID = 0x4E9;
        internal const int ERROR_NOT_ALL_ASSIGNED = 0x514;
        internal const int ERROR_UNKNOWN_REVISION = 0x519;
        internal const int ERROR_INVALID_OWNER = 0x51B;
        internal const int ERROR_INVALID_PRIMARY_GROUP = 0x51C;
        internal const int ERROR_NO_SUCH_PRIVILEGE = 0x521;
        internal const int ERROR_PRIVILEGE_NOT_HELD = 0x522;
        internal const int ERROR_NONE_MAPPED = 0x534;
        internal const int ERROR_INVALID_ACL = 0x538;
        internal const int ERROR_INVALID_SID = 0x539;
        internal const int ERROR_INVALID_SECURITY_DESCR = 0x53A;
        internal const int ERROR_BAD_IMPERSONATION_LEVEL = 0x542;
        internal const int ERROR_CANT_OPEN_ANONYMOUS = 0x543;
        internal const int ERROR_NO_SECURITY_ON_OBJECT = 0x546;
        internal const int ERROR_TRUSTED_RELATIONSHIP_FAILURE = 0x6FD;

        public const int FILE_SHARE_READ = 0x00000001;
        public const int FILE_SHARE_WRITE = 0x00000002;
        public const int FILE_SHARE_DELETE = 0x00000004;

        public const int FILE_READ_DATA = 0x0001;
        public const int FILE_WRITE_DATA = 0x0002;
        public const int FILE_APPEND_DATA = 0x0004;
        public const int FILE_READ_ATTRIBUTES = 0x0080;
        public const int FILE_WRITE_ATTRIBUTES = 0x0100;

        [DllImport(KERNEL32, SetLastError = true, CharSet = CharSet.Auto, BestFitMapping = false)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DeleteFile(string path);

        [DllImport(KERNEL32, SetLastError = true, CharSet = CharSet.Auto, BestFitMapping = false)]
        internal static extern SafeFileHandle CreateFile(
            string lpFileName,
            int dwDesiredAccess,
            System.IO.FileShare dwShareMode,
            IntPtr securityAttrs,
            System.IO.FileMode dwCreationDisposition,
            System.IO.FileOptions dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport(KERNEL32, SetLastError = true, CharSet = CharSet.Auto, BestFitMapping = false)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool FlushFileBuffers(SafeFileHandle hFile);

        [StructLayout(LayoutKind.Sequential)]
        internal readonly struct FILE_TIME
        {
            public FILE_TIME(DateTime dateTime)
            {
                long fileTime = dateTime.ToFileTimeUtc();

                unchecked
                {
                    ftTimeLow = (uint)fileTime;
                    ftTimeHigh = (uint)(fileTime >> 32);
                }
            }

            // ReSharper disable PrivateFieldCanBeConvertedToLocalVariable
            private readonly uint ftTimeLow;
            private readonly uint ftTimeHigh;

            // ReSharper restore PrivateFieldCanBeConvertedToLocalVariable
        }

        [DllImport(KERNEL32, SetLastError = true)]
        [ResourceExposure(ResourceScope.None)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern unsafe bool SetFileTime(
            SafeFileHandle hFile, FILE_TIME* creationTime, FILE_TIME* lastAccessTime, FILE_TIME* lastWriteTime);

        // END Copied from CLR

        // Copied from CloudBuild/private/DevTools/dbs/common/Utils/src/FileSystemNativeMethods.cs
        private const uint FORMAT_MESSAGE_FROM_SYSTEM = 0x00001000;

        [DllImport(KERNEL32, SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern uint FormatMessage(
            uint dwFlags,
            IntPtr lpSource,
            uint dwMessageId,
            uint dwLanguageId,
            StringBuilder lpBuffer,
            uint nSize,
            IntPtr Arguments);

        /// <summary>
        ///     Provides a human-readable error message for the given error code.
        /// </summary>
        internal static string GetErrorMessage(int code)
        {
            StringBuilder message = new StringBuilder(255);

            var result = FormatMessage(
                FORMAT_MESSAGE_FROM_SYSTEM,
                IntPtr.Zero,
                (uint)code,
                0,
                message,
                (uint)message.Capacity,
                IntPtr.Zero);

            if (result == 0)
            {
                return $"Failed to format message for error code [{code}]. Code for format error: {Marshal.GetLastWin32Error()}.";
            }

            // .Trim() added because FormatMessage appends /r/n
            return message.ToString().Trim();
        }

        // END copied from CloudBuild/private/DevTools/dbs/common/Utils/src/FileSystemNativeMethods.cs

        // Copied from Microsoft.TeamFoundation.Common
        [Flags]
        internal enum MoveFileOption
        {
            MOVEFILE_COPY_ALLOWED = 0x2,
            MOVEFILE_CREATE_HARDLINK = 0x10,
            MOVEFILE_DELAY_UNTIL_REBOOT = 0x4,
            MOVEFILE_FAIL_IF_NOT_TRACKABLE = 0x20,
            MOVEFILE_REPLACE_EXISTING = 0x1,
            MOVEFILE_WRITE_THROUGH = 0x8
        }

        [DllImport(KERNEL32, SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool MoveFileEx(string src, string dst, MoveFileOption dwFlags);

        // End copied from Microsoft.TeamFoundation.Common

        // BEGIN copy //depot/winmain/sdktools/DevicePath/Interop

        /// <summary>
        ///     Structure used to represent the NTSTATUS codes returned by many Native API calls.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public readonly struct NtStatus
        {
            /// <summary>
            ///     Contains the status code as defined in ntstatus.h
            /// </summary>
            private readonly int m_statusCode;

            /// <summary>
            ///     Gets gets or sets the status code as an unsigned int.
            /// </summary>
            public uint StatusCodeUint => unchecked((uint)m_statusCode);

            /// <summary>
            ///     Gets a value indicating whether gets whether or not this is a failing status code.
            /// </summary>
            public bool Failed => m_statusCode < 0;

            /// <summary>
            ///     Returns a string containing the hexadecimal status code.
            /// </summary>
            /// <returns>A string containing the hexadecimal status code.</returns>
            public override string ToString()
            {
                return "0x" + m_statusCode.ToString("X");
            }

            /// <summary>
            ///     Gets returns a string containing the name of the status code as defined in ntstatus.h or "Undefined".
            /// </summary>
            public string StatusName
            {
                get
                {
                    if (Enum.IsDefined(typeof(NtStatusCode), StatusCodeUint))
                    {
                        return ((NtStatusCode)StatusCodeUint).ToString();
                    }

                    return "Undefined";
                }
            }
        }

        /* wdm.h
        typedef struct _IO_STATUS_BLOCK {
                union {
                    NTSTATUS Status;
                    PVOID Pointer;
                } DUMMYUNIONNAME;

                ULONG_PTR Information;
            } IO_STATUS_BLOCK, *PIO_STATUS_BLOCK;
        */

        /// <summary>
        ///     A driver sets an IRP's I/O status block to indicate the final status of an I/O request,
        ///     before calling IoCompleteRequest for the IRP.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public readonly struct IoStatusBlock
        {
            /// <summary>
            ///     Reserved. For internal use only.  Note that this is where the status is actually stored (like a union).
            /// </summary>
            private readonly IntPtr Pointer;

            /// <summary>
            ///     Note that this is not a pointer and should be treated instead as either a 32 or 64 bit ulong, depending on the
            ///     platform.
            ///     Call its ToInt32() or ToInt64() method to get its value.
            ///     This is set to a request-dependent value. For example, on successful completion of a transfer request,
            ///     this is set to the number of bytes transferred. If a transfer request is completed with
            ///     another STATUS_XXX, this member is set to zero.
            /// </summary>
            private readonly IntPtr Information;
        }

        /// <summary>
        ///     Enumeration of the various file information classes.
        ///     See wdm.h.
        /// </summary>
        public enum FileInformationClass
        {
            FileDirectoryInformation = 1,
            FileFullDirectoryInformation, // 2
            FileBothDirectoryInformation, // 3
            FileBasicInformation, // 4
            FileStandardInformation, // 5
            FileInternalInformation, // 6
            FileEaInformation, // 7
            FileAccessInformation, // 8
            FileNameInformation, // 9
            FileRenameInformation, // 10
            FileLinkInformation, // 11
            FileNamesInformation, // 12
            FileDispositionInformation, // 13
            FilePositionInformation, // 14
            FileFullEaInformation, // 15
            FileModeInformation, // 16
            FileAlignmentInformation, // 17
            FileAllInformation, // 18
            FileAllocationInformation, // 19
            FileEndOfFileInformation, // 20
            FileAlternateNameInformation, // 21
            FileStreamInformation, // 22
            FilePipeInformation, // 23
            FilePipeLocalInformation, // 24
            FilePipeRemoteInformation, // 25
            FileMailslotQueryInformation, // 26
            FileMailslotSetInformation, // 27
            FileCompressionInformation, // 28
            FileObjectIdInformation, // 29
            FileCompletionInformation, // 30
            FileMoveClusterInformation, // 31
            FileQuotaInformation, // 32
            FileReparsePointInformation, // 33
            FileNetworkOpenInformation, // 34
            FileAttributeTagInformation, // 35
            FileTrackingInformation, // 36
            FileIdBothDirectoryInformation, // 37
            FileIdFullDirectoryInformation, // 38
            FileValidDataLengthInformation, // 39
            FileShortNameInformation, // 40
            FileIoCompletionNotificationInformation, // 41
            FileIoStatusBlockRangeInformation, // 42
            FileIoPriorityHintInformation, // 43
            FileSfioReserveInformation, // 44
            FileSfioVolumeInformation, // 45
            FileHardLinkInformation, // 46
            FileProcessIdsUsingFileInformation, // 47
            FileNormalizedNameInformation, // 48
            FileNetworkPhysicalNameInformation, // 49
            FileIdGlobalTxDirectoryInformation, // 50
            FileIsRemoteDeviceInformation, // 51
            FileAttributeCacheInformation, // 52
            FileNumaNodeInformation, // 53
            FileStandardLinkInformation, // 54
            FileRemoteProtocolInformation, // 55
            FileMaximumInformation
        }

        private const string DosToNtPathPrefix = @"\??\";
        private const string AlternativeDosToNtPathPrefix = @"\\?\";

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public readonly struct FileLinkInformation
        {
            // ReSharper disable PrivateFieldCanBeConvertedToLocalVariable
            private readonly byte ReplaceIfExists;
            private readonly IntPtr RootDirectoryHandle;
            private readonly uint FileNameLength;

            /// <summary>
            ///     Allocates a constant-sized buffer for the FileName.  MAX_PATH for the path, 4 for the DosToNtPathPrefix.
            /// </summary>
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = FileSystemConstants.MaxLongPath + 4)]
            private readonly string FileName;

            // ReSharper restore PrivateFieldCanBeConvertedToLocalVariable
            public FileLinkInformation(string destinationPath, bool replaceIfExists)
            {
                FileName = destinationPath;
                FileNameLength = (uint)(2 * FileName.Length);
                RootDirectoryHandle = IntPtr.Zero;
                ReplaceIfExists = (byte)(replaceIfExists ? 1 : 0);
            }
        }

        // NtSetInformationFile
        /* ntifs.h
            __kernel_entry NTSYSCALLAPI
            NTSTATUS
            NTAPI
            NtSetInformationFile (
                __in HANDLE FileHandle,
                __out PIO_STATUS_BLOCK IoStatusBlock,
                __in_bcount(Length) PVOID FileInformation,
                __in ULONG Length,
                __in FILE_INFORMATION_CLASS FileInformationClass
                );
        */

        /// <summary>
        ///     The ZwSetInformationFile routine changes various kinds of information about a file object.
        /// </summary>
        /// <param name="fileHandle">
        ///     Handle to the file object. This handle is created by a successful call to ZwCreateFile or
        ///     ZwOpenFile.
        /// </param>
        /// <param name="ioStatusBlock">
        ///     An IO_STATUS_BLOCK structure that receives the final completion status and information about the requested
        ///     operation. The Information member receives the number of bytes set on the file.
        /// </param>
        /// <param name="fileInformation">
        ///     Pointer to a buffer that contains the information to set for the file. The particular structure in this
        ///     buffer is determined by the FileInformationClass parameter. Setting any member of the structure to zero tells
        ///     ZwSetInformationFile to
        ///     leave the current information about the file for that member unchanged.
        /// </param>
        /// <param name="length">The size, in bytes, of the FileInformation buffer.</param>
        /// <param name="fileInformationClass">
        ///     The type of information, supplied in the buffer pointed to by FileInformation, to set for the
        ///     file.
        /// </param>
        /// <returns>An NtStatus code indicating success or failure.</returns>
        [DllImport("ntdll.dll", ExactSpelling = true)]
        public static extern NtStatus NtSetInformationFile(
            SafeFileHandle fileHandle,
            out IoStatusBlock ioStatusBlock,
#pragma warning disable CS0618 // 'UnmanagedType.AsAny' is obsolete
            [MarshalAs(UnmanagedType.AsAny)] object fileInformation,
#pragma warning restore CS0618 // 'UnmanagedType.AsAny' is obsolete
            uint length,
            FileInformationClass fileInformationClass);

        /// <summary>
        ///     Holds the defined values for Ntstatus codes from ntstatus.h.
        /// </summary>
        public enum NtStatusCode : uint
        {
            // I moved StatusWait0 above StatusSuccess because they represent the same value and I
            // wanted that value to get translated into "StatusSuccess" instead of "StatusWait0"
            // when ToString() is called on it.

            /// <summary>
            ///     MessageId: StatusWait0
            ///     MessageText:
            ///     StatusWait0
            /// </summary>
            StatusWait0 = 0x00000000, // winnt

            //
            // The success status codes 0 - 63 are reserved for wait completion status.
            // FacilityCodes = 0x5 - = 0xF have been allocated by various drivers.
            StatusSuccess = 0x00000000, // ntsubauth

            /// <summary>
            ///     MessageId: StatusWait1
            ///     MessageText:
            ///     StatusWait1
            /// </summary>
            StatusWait1 = 0x00000001,

            /// <summary>
            ///     MessageId: StatusWait2
            ///     MessageText:
            ///     StatusWait2
            /// </summary>
            StatusWait2 = 0x00000002,

            /// <summary>
            ///     MessageId: StatusWait3
            ///     MessageText:
            ///     StatusWait3
            /// </summary>
            StatusWait3 = 0x00000003,

            /// <summary>
            ///     MessageId: StatusWait63
            ///     MessageText:
            ///     StatusWait63
            /// </summary>
            StatusWait63 = 0x0000003F,

            // The success status codes 128 - 191 are reserved for wait completion
            // status with an abandoned mutant object.
            StatusAbandoned = 0x00000080,

            /// <summary>
            ///     MessageId: StatusAbandonedWait0
            ///     MessageText:
            ///     StatusAbandonedWait0
            /// </summary>
            StatusAbandonedWait0 = 0x00000080, // winnt

            /// <summary>
            ///     MessageId: StatusAbandonedWait63
            ///     MessageText:
            ///     StatusAbandonedWait63
            /// </summary>
            StatusAbandonedWait63 = 0x000000BF,

            // The success status codes 256, 257, 258, and 258 are reserved for
            // User Apc, Kernel Apc, Alerted, and Timeout.

            /// <summary>
            ///     MessageId: StatusUserApc
            ///     MessageText:
            ///     StatusUserApc
            /// </summary>
            StatusUserApc = 0x000000C0, // winnt

            /// <summary>
            ///     MessageId: StatusKernelApc
            ///     MessageText:
            ///     StatusKernelApc
            /// </summary>
            StatusKernelApc = 0x00000100,

            /// <summary>
            ///     MessageId: StatusAlerted
            ///     MessageText:
            ///     StatusAlerted
            /// </summary>
            StatusAlerted = 0x00000101,

            /// <summary>
            ///     MessageId: StatusTimeout
            ///     MessageText:
            ///     StatusTimeout
            /// </summary>
            StatusTimeout = 0x00000102, // winnt

            /// <summary>
            ///     MessageId: StatusPending
            ///     MessageText:
            ///     The operation that was requested is pending completion.
            /// </summary>
            StatusPending = 0x00000103, // winnt

            /// <summary>
            ///     MessageId: StatusReparse
            ///     MessageText:
            ///     A reparse should be performed by the Object Manager since the name of the file resulted in a symbolic link.
            /// </summary>
            StatusReparse = 0x00000104,

            /// <summary>
            ///     MessageId: StatusMoreEntries
            ///     MessageText:
            ///     Returned by enumeration APIs to indicate more information is available to successive calls.
            /// </summary>
            StatusMoreEntries = 0x00000105,

            /// <summary>
            ///     MessageId: StatusNotAllAssigned
            ///     MessageText:
            ///     Indicates not all privileges or groups referenced are assigned to the caller.
            ///     This allows, for example, all privileges to be disabled without having to know exactly which privileges are
            ///     assigned.
            /// </summary>
            StatusNotAllAssigned = 0x00000106,

            /// <summary>
            ///     MessageId: StatusSomeNotMapped
            ///     MessageText:
            ///     Some of the information to be translated has not been translated.
            /// </summary>
            StatusSomeNotMapped = 0x00000107,

            /// <summary>
            ///     MessageId: StatusOplockBreakInProgress
            ///     MessageText:
            ///     An open/create operation completed while an oplock break is underway.
            /// </summary>
            StatusOplockBreakInProgress = 0x00000108,

            /// <summary>
            ///     MessageId: StatusVolumeMounted
            ///     MessageText:
            ///     A new volume has been mounted by a file system.
            /// </summary>
            StatusVolumeMounted = 0x00000109,

            /// <summary>
            ///     MessageId: StatusRxactCommitted
            ///     MessageText:
            ///     This success level status indicates that the transaction state already exists for the registry sub-tree, but that a
            ///     transaction commit was previously aborted. The commit has now been completed.
            /// </summary>
            StatusRxactCommitted = 0x0000010A,

            /// <summary>
            ///     MessageId: StatusNotifyCleanup
            ///     MessageText:
            ///     This indicates that a notify change request has been completed due to closing the handle which made the notify
            ///     change request.
            /// </summary>
            StatusNotifyCleanup = 0x0000010B,

            /// <summary>
            ///     MessageId: StatusNotifyEnumDir
            ///     MessageText:
            ///     This indicates that a notify change request is being completed and that the information is not being returned in
            ///     the caller's buffer.
            ///     The caller now needs to enumerate the files to find the changes.
            /// </summary>
            StatusNotifyEnumDir = 0x0000010C,

            /// <summary>
            ///     MessageId: StatusNoQuotasForAccount
            ///     MessageText:
            ///     {No Quotas}
            ///     No system quota limits are specifically set for this account.
            /// </summary>
            StatusNoQuotasForAccount = 0x0000010D,

            /// <summary>
            ///     MessageId: StatusPrimaryTransportConnectFailed
            ///     MessageText:
            ///     {Connect Failure on Primary Transport}
            ///     An attempt was made to connect to the remote server %hs on the primary transport, but the connection failed.
            ///     The computer Was able to connect on a secondary transport.
            /// </summary>
            StatusPrimaryTransportConnectFailed = 0x0000010E,

            /// <summary>
            ///     MessageId: StatusPageFaultTransition
            ///     MessageText:
            ///     Page fault was a transition fault.
            /// </summary>
            StatusPageFaultTransition = 0x00000110,

            /// <summary>
            ///     MessageId: StatusPageFaultDemandZero
            ///     MessageText:
            ///     Page fault was a demand zero fault.
            /// </summary>
            StatusPageFaultDemandZero = 0x00000111,

            /// <summary>
            ///     MessageId: StatusPageFaultCopyOnWrite
            ///     MessageText:
            ///     Page fault was a demand zero fault.
            /// </summary>
            StatusPageFaultCopyOnWrite = 0x00000112,

            /// <summary>
            ///     MessageId: StatusPageFaultGuardPage
            ///     MessageText:
            ///     Page fault was a demand zero fault.
            /// </summary>
            StatusPageFaultGuardPage = 0x00000113,

            /// <summary>
            ///     MessageId: StatusPageFaultPagingFile
            ///     MessageText:
            ///     Page fault was satisfied by reading from a secondary storage device.
            /// </summary>
            StatusPageFaultPagingFile = 0x00000114,

            /// <summary>
            ///     MessageId: StatusCachePageLocked
            ///     MessageText:
            ///     Cached page was locked during operation.
            /// </summary>
            StatusCachePageLocked = 0x00000115,

            /// <summary>
            ///     MessageId: StatusCrashDump
            ///     MessageText:
            ///     Crash dump exists in paging file.
            /// </summary>
            StatusCrashDump = 0x00000116,

            /// <summary>
            ///     MessageId: StatusBufferAllZeros
            ///     MessageText:
            ///     Specified buffer contains all zeros.
            /// </summary>
            StatusBufferAllZeros = 0x00000117,

            /// <summary>
            ///     MessageId: StatusReparseObject
            ///     MessageText:
            ///     A reparse should be performed by the Object Manager since the name of the file resulted in a symbolic link.
            /// </summary>
            StatusReparseObject = 0x00000118,

            /// <summary>
            ///     MessageId: StatusResourceRequirementsChanged
            ///     MessageText:
            ///     The device has succeeded a query-stop and its resource requirements have changed.
            /// </summary>
            StatusResourceRequirementsChanged = 0x00000119,

            /// <summary>
            ///     MessageId: StatusTranslationComplete
            ///     MessageText:
            ///     The translator has translated these resources into the global space and no further translations should be
            ///     performed.
            /// </summary>
            StatusTranslationComplete = 0x00000120,

            /// <summary>
            ///     MessageId: StatusDsMembershipEvaluatedLocally
            ///     MessageText:
            ///     The directory service evaluated group memberships locally, as it was unable to contact a global catalog server.
            /// </summary>
            StatusDsMembershipEvaluatedLocally = 0x00000121,

            /// <summary>
            ///     MessageId: StatusNothingToTerminate
            ///     MessageText:
            ///     A process being terminated has no threads to terminate.
            /// </summary>
            StatusNothingToTerminate = 0x00000122,

            /// <summary>
            ///     MessageId: StatusProcessNotInJob
            ///     MessageText:
            ///     The specified process is not part of a job.
            /// </summary>
            StatusProcessNotInJob = 0x00000123,

            /// <summary>
            ///     MessageId: StatusProcessInJob
            ///     MessageText:
            ///     The specified process is part of a job.
            /// </summary>
            StatusProcessInJob = 0x00000124,

            /// <summary>
            ///     MessageId: StatusVolsnapHibernateReady
            ///     MessageText:
            ///     {Volume Shadow Copy Service}
            ///     The system is now ready for hibernation.
            /// </summary>
            StatusVolsnapHibernateReady = 0x00000125,

            /// <summary>
            ///     MessageId: StatusFsfilterOpCompletedSuccessfully
            ///     MessageText:
            ///     A file system or file system filter driver has successfully completed an FsFilter operation.
            /// </summary>
            StatusFsfilterOpCompletedSuccessfully = 0x00000126,

            /// <summary>
            ///     MessageId: StatusInterruptVectorAlreadyConnected
            ///     MessageText:
            ///     The specified interrupt vector was already connected.
            /// </summary>
            StatusInterruptVectorAlreadyConnected = 0x00000127,

            /// <summary>
            ///     MessageId: StatusInterruptStillConnected
            ///     MessageText:
            ///     The specified interrupt vector is still connected.
            /// </summary>
            StatusInterruptStillConnected = 0x00000128,

            /// <summary>
            ///     MessageId: StatusProcessCloned
            ///     MessageText:
            ///     The current process is a cloned process.
            /// </summary>
            StatusProcessCloned = 0x00000129,

            /// <summary>
            ///     MessageId: StatusFileLockedWithOnlyReaders
            ///     MessageText:
            ///     The file was locked and all users of the file can only read.
            /// </summary>
            StatusFileLockedWithOnlyReaders = 0x0000012A,

            /// <summary>
            ///     MessageId: StatusFileLockedWithWriters
            ///     MessageText:
            ///     The file was locked and at least one user of the file can write.
            /// </summary>
            StatusFileLockedWithWriters = 0x0000012B,

            /// <summary>
            ///     MessageId: StatusResourcemanagerReadOnly
            ///     MessageText:
            ///     The specified ResourceManager made no changes or updates to the resource under this transaction.
            /// </summary>
            StatusResourcemanagerReadOnly = 0x00000202,

            /// <summary>
            ///     MessageId: StatusRingPreviouslyEmpty
            ///     MessageText:
            ///     The specified ring buffer was empty before the packet was successfully inserted.
            /// </summary>
            StatusRingPreviouslyEmpty = 0x00000210,

            /// <summary>
            ///     MessageId: StatusRingPreviouslyFull
            ///     MessageText:
            ///     The specified ring buffer was full before the packet was successfully removed.
            /// </summary>
            StatusRingPreviouslyFull = 0x00000211,

            /// <summary>
            ///     MessageId: StatusRingPreviouslyAboveQuota
            ///     MessageText:
            ///     The specified ring buffer has dropped below its quota of outstanding transactions.
            /// </summary>
            StatusRingPreviouslyAboveQuota = 0x00000212,

            /// <summary>
            ///     MessageId: StatusRingNewlyEmpty
            ///     MessageText:
            ///     The specified ring buffer has, with the removal of the current packet, now become empty.
            /// </summary>
            StatusRingNewlyEmpty = 0x00000213,

            /// <summary>
            ///     MessageId: StatusRingSignalOppositeEndpoint
            ///     MessageText:
            ///     The specified ring buffer was either previously empty or previously full which implies that the caller should
            ///     signal the opposite endpoint.
            /// </summary>
            StatusRingSignalOppositeEndpoint = 0x00000214,

            /// <summary>
            ///     MessageId: StatusOplockSwitchedToNewHandle
            ///     MessageText:
            ///     The oplock that was associated with this handle is now associated with a different handle.
            /// </summary>
            StatusOplockSwitchedToNewHandle = 0x00000215,

            /// <summary>
            ///     MessageId: StatusOplockHandleClosed
            ///     MessageText:
            ///     The handle with which this oplock was associated has been closed.  The oplock is now broken.
            /// </summary>
            StatusOplockHandleClosed = 0x00000216,

            /// <summary>
            ///     MessageId: StatusWaitForOplock
            ///     MessageText:
            ///     An operation is blocked waiting for an oplock.
            /// </summary>
            StatusWaitForOplock = 0x00000367,

            /// <summary>
            ///     MessageId: DbgExceptionHandled
            ///     MessageText:
            ///     Debugger handled exception
            /// </summary>
            DbgExceptionHandled = 0x00010001, // winnt

            /// <summary>
            ///     MessageId: DbgContinue
            ///     MessageText:
            ///     Debugger continued
            /// </summary>
            DbgContinue = 0x00010002, // winnt

            /// <summary>
            ///     MessageId: StatusFltIoComplete
            ///     MessageText:
            ///     The Io was completed by a filter.
            /// </summary>
            StatusFltIoComplete = 0x001C0001,

            /// <summary>
            ///     MessageId: StatusDisAttributeBuilt
            ///     MessageText:
            ///     An attribute was successfully built.
            /// </summary>
            StatusDisAttributeBuilt = 0x003C0001,

            /////////////////////////////////////////////////////////////////////////
            //
            // Standard Information values
            //
            /////////////////////////////////////////////////////////////////////////

            /// <summary>
            ///     MessageId: StatusObjectNameExists
            ///     MessageText:
            ///     {Object Exists}
            ///     An attempt was made to create an object and the object name already existed.
            /// </summary>
            StatusObjectNameExists = 0x40000000,

            /// <summary>
            ///     MessageId: StatusThreadWasSuspended
            ///     MessageText:
            ///     {Thread Suspended}
            ///     A thread termination occurred while the thread was suspended. The thread was resumed, and termination proceeded.
            /// </summary>
            StatusThreadWasSuspended = 0x40000001,

            /// <summary>
            ///     MessageId: StatusWorkingSetLimitRange
            ///     MessageText:
            ///     {Working Set Range Error}
            ///     An attempt was made to set the working set minimum or maximum to values which are outside of the allowable range.
            /// </summary>
            StatusWorkingSetLimitRange = 0x40000002,

            /// <summary>
            ///     MessageId: StatusImageNotAtBase
            ///     MessageText:
            ///     {Image Relocated}
            ///     An image file could not be mapped at the address specified in the image file. Local fixups must be performed on
            ///     this image.
            /// </summary>
            StatusImageNotAtBase = 0x40000003,

            /// <summary>
            ///     MessageId: StatusRxactStateCreated
            ///     MessageText:
            ///     This informational level status indicates that a specified registry sub-tree transaction state did not yet exist
            ///     and had to be created.
            /// </summary>
            StatusRxactStateCreated = 0x40000004,

            /// <summary>
            ///     MessageId: StatusSegmentNotification
            ///     MessageText:
            ///     {Segment Load}
            ///     A virtual Dos machine (Vdm, is loading, unloading, or moving an Ms-Dos or Win16 program segment image.
            ///     An exception is raised so a debugger can load, unload or track symbols and breakpoints within these 16-bit
            ///     segments.
            /// </summary>
            StatusSegmentNotification = 0x40000005, // winnt

            /// <summary>
            ///     MessageId: StatusLocalUserSessionKey
            ///     MessageText:
            ///     {Local Session Key}
            ///     A user session key was requested for a local Rpc connection. The session key returned is a constant value and not
            ///     unique to this connection.
            /// </summary>
            StatusLocalUserSessionKey = 0x40000006,

            /// <summary>
            ///     MessageId: StatusBadCurrentDirectory
            ///     MessageText:
            ///     {Invalid Current Directory}
            ///     The process cannot switch to the startup current directory %hs.
            ///     Select Ok to set current directory to %hs, or select Cancel to exit.
            /// </summary>
            StatusBadCurrentDirectory = 0x40000007,

            /// <summary>
            ///     MessageId: StatusSerialMoreWrites
            ///     MessageText:
            ///     {Serial Ioctl Complete}
            ///     A serial I/O operation was completed by another write to a serial port.
            ///     (The IoctlSerialXoffCounter reached zero.,
            /// </summary>
            StatusSerialMoreWrites = 0x40000008,

            /// <summary>
            ///     MessageId: StatusRegistryRecovered
            ///     MessageText:
            ///     {Registry Recovery}
            ///     One of the files containing the system's Registry data had to be recovered by use of a log or alternate copy. The
            ///     recovery was successful.
            /// </summary>
            StatusRegistryRecovered = 0x40000009,

            /// <summary>
            ///     MessageId: StatusFtReadRecoveryFromBackup
            ///     MessageText:
            ///     {Redundant Read}
            ///     To satisfy a read request, the Nt fault-tolerant file system successfully read the requested data from a redundant
            ///     copy.
            ///     This was done because the file system encountered a failure on a member of the fault-tolerant volume, but was
            ///     unable to reassign the failing area of the device.
            /// </summary>
            StatusFtReadRecoveryFromBackup = 0x4000000A,

            /// <summary>
            ///     MessageId: StatusFtWriteRecovery
            ///     MessageText:
            ///     {Redundant Write}
            ///     To satisfy a write request, the Nt fault-tolerant file system successfully wrote a redundant copy of the
            ///     information.
            ///     This was done because the file system encountered a failure on a member of the fault-tolerant volume, but was not
            ///     able to reassign the failing area of the device.
            /// </summary>
            StatusFtWriteRecovery = 0x4000000B,

            /// <summary>
            ///     MessageId: StatusSerialCounterTimeout
            ///     MessageText:
            ///     {Serial Ioctl Timeout}
            ///     A serial I/O operation completed because the time-out period expired. (The IoctlSerialXoffCounter had not reached
            ///     zero.,
            /// </summary>
            StatusSerialCounterTimeout = 0x4000000C,

            /// <summary>
            ///     MessageId: StatusNullLmPassword
            ///     MessageText:
            ///     {Password Too Complex}
            ///     The Windows password is too complex to be converted to a Lan Manager password. The Lan Manager password returned is
            ///     a Null string.
            /// </summary>
            StatusNullLmPassword = 0x4000000D,

            /// <summary>
            ///     MessageId: StatusImageMachineTypeMismatch
            ///     MessageText:
            ///     {Machine Type Mismatch}
            ///     The image file %hs is valid, but is for a machine type other than the current machine. Select Ok to continue, or
            ///     Cancel to fail the Dll load.
            /// </summary>
            StatusImageMachineTypeMismatch = 0x4000000E,

            /// <summary>
            ///     MessageId: StatusReceivePartial
            ///     MessageText:
            ///     {Partial Data Received}
            ///     The network transport returned partial data to its client. The remaining data will be sent later.
            /// </summary>
            StatusReceivePartial = 0x4000000F,

            /// <summary>
            ///     MessageId: StatusReceiveExpedited
            ///     MessageText:
            ///     {Expedited Data Received}
            ///     The network transport returned data to its client that was marked as expedited by the remote system.
            /// </summary>
            StatusReceiveExpedited = 0x40000010,

            /// <summary>
            ///     MessageId: StatusReceivePartialExpedited
            ///     MessageText:
            ///     {Partial Expedited Data Received}
            ///     The network transport returned partial data to its client and this data was marked as expedited by the remote
            ///     system. The remaining data will be sent later.
            /// </summary>
            StatusReceivePartialExpedited = 0x40000011,

            /// <summary>
            ///     MessageId: StatusEventDone
            ///     MessageText:
            ///     {Tdi Event Done}
            ///     The Tdi indication has completed successfully.
            /// </summary>
            StatusEventDone = 0x40000012,

            /// <summary>
            ///     MessageId: StatusEventPending
            ///     MessageText:
            ///     {Tdi Event Pending}
            ///     The Tdi indication has entered the pending state.
            /// </summary>
            StatusEventPending = 0x40000013,

            /// <summary>
            ///     MessageId: StatusCheckingFileSystem
            ///     MessageText:
            ///     Checking file system on %wZ
            /// </summary>
            StatusCheckingFileSystem = 0x40000014,

            /// <summary>
            ///     MessageId: StatusFatalAppExit
            ///     MessageText:
            ///     {Fatal Application Exit}
            ///     %hs
            /// </summary>
            StatusFatalAppExit = 0x40000015,

            /// <summary>
            ///     MessageId: StatusPredefinedHandle
            ///     MessageText:
            ///     The specified registry key is referenced by a predefined handle.
            /// </summary>
            StatusPredefinedHandle = 0x40000016,

            /// <summary>
            ///     MessageId: StatusWasUnlocked
            ///     MessageText:
            ///     {Page Unlocked}
            ///     The page protection of a locked page was changed to 'No Access' and the page was unlocked from memory and from the
            ///     process.
            /// </summary>
            StatusWasUnlocked = 0x40000017,

            /// <summary>
            ///     MessageId: StatusServiceNotification
            ///     MessageText:
            ///     %hs
            /// </summary>
            StatusServiceNotification = 0x40000018,

            /// <summary>
            ///     MessageId: StatusWasLocked
            ///     MessageText:
            ///     {Page Locked}
            ///     One of the pages to lock was already locked.
            /// </summary>
            StatusWasLocked = 0x40000019,

            /// <summary>
            ///     MessageId: StatusLogHardError
            ///     MessageText:
            ///     Application popup: %1 : %2
            /// </summary>
            StatusLogHardError = 0x4000001A,

            /// <summary>
            ///     MessageId: StatusAlreadyWin32
            ///     MessageText:
            ///     StatusAlreadyWin32
            /// </summary>
            StatusAlreadyWin32 = 0x4000001B,

            /// <summary>
            ///     MessageId: StatusWx86Unsimulate
            ///     MessageText:
            ///     Exception status code used by Win32 x86 emulation subsystem.
            /// </summary>
            StatusWx86Unsimulate = 0x4000001C,

            /// <summary>
            ///     MessageId: StatusWx86Continue
            ///     MessageText:
            ///     Exception status code used by Win32 x86 emulation subsystem.
            /// </summary>
            StatusWx86Continue = 0x4000001D,

            /// <summary>
            ///     MessageId: StatusWx86SingleStep
            ///     MessageText:
            ///     Exception status code used by Win32 x86 emulation subsystem.
            /// </summary>
            StatusWx86SingleStep = 0x4000001E,

            /// <summary>
            ///     MessageId: StatusWx86Breakpoint
            ///     MessageText:
            ///     Exception status code used by Win32 x86 emulation subsystem.
            /// </summary>
            StatusWx86Breakpoint = 0x4000001F,

            /// <summary>
            ///     MessageId: StatusWx86ExceptionContinue
            ///     MessageText:
            ///     Exception status code used by Win32 x86 emulation subsystem.
            /// </summary>
            StatusWx86ExceptionContinue = 0x40000020,

            /// <summary>
            ///     MessageId: StatusWx86ExceptionLastchance
            ///     MessageText:
            ///     Exception status code used by Win32 x86 emulation subsystem.
            /// </summary>
            StatusWx86ExceptionLastchance = 0x40000021,

            /// <summary>
            ///     MessageId: StatusWx86ExceptionChain
            ///     MessageText:
            ///     Exception status code used by Win32 x86 emulation subsystem.
            /// </summary>
            StatusWx86ExceptionChain = 0x40000022,

            /// <summary>
            ///     MessageId: StatusImageMachineTypeMismatchExe
            ///     MessageText:
            ///     {Machine Type Mismatch}
            ///     The image file %hs is valid, but is for a machine type other than the current machine.
            /// </summary>
            StatusImageMachineTypeMismatchExe = 0x40000023,

            /// <summary>
            ///     MessageId: StatusNoYieldPerformed
            ///     MessageText:
            ///     A yield execution was performed and no thread was available to run.
            /// </summary>
            StatusNoYieldPerformed = 0x40000024,

            /// <summary>
            ///     MessageId: StatusTimerResumeIgnored
            ///     MessageText:
            ///     The resumable flag to a timer Api was ignored.
            /// </summary>
            StatusTimerResumeIgnored = 0x40000025,

            /// <summary>
            ///     MessageId: StatusArbitrationUnhandled
            ///     MessageText:
            ///     The arbiter has deferred arbitration of these resources to its parent
            /// </summary>
            StatusArbitrationUnhandled = 0x40000026,

            /// <summary>
            ///     MessageId: StatusCardbusNotSupported
            ///     MessageText:
            ///     The device "%hs" has detected a CardBus card in its slot, but the firmware on this system is not configured to
            ///     allow the CardBus controller to be run in CardBus mode.
            ///     The operating system will currently accept only 16-bit (R2, pc-cards on this controller.
            /// </summary>
            StatusCardbusNotSupported = 0x40000027,

            /// <summary>
            ///     MessageId: StatusWx86Createwx86tib
            ///     MessageText:
            ///     Exception status code used by Win32 x86 emulation subsystem.
            /// </summary>
            StatusWx86Createwx86tib = 0x40000028,

            /// <summary>
            ///     MessageId: StatusMpProcessorMismatch
            ///     MessageText:
            ///     The CPUs in this multiprocessor system are not all the same revision level. To use all processors the operating
            ///     system restricts itself to the features of the least capable processor in the system. Should problems occur with
            ///     this system, contact the Cpu manufacturer to see if this mix of processors is supported.
            /// </summary>
            StatusMpProcessorMismatch = 0x40000029,

            /// <summary>
            ///     MessageId: StatusHibernated
            ///     MessageText:
            ///     The system was put into hibernation.
            /// </summary>
            StatusHibernated = 0x4000002A,

            /// <summary>
            ///     MessageId: StatusResumeHibernation
            ///     MessageText:
            ///     The system was resumed from hibernation.
            /// </summary>
            StatusResumeHibernation = 0x4000002B,

            /// <summary>
            ///     MessageId: StatusFirmwareUpdated
            ///     MessageText:
            ///     Windows has detected that the system firmware (Bios, was updated [previous firmware date = %2, current firmware
            ///     date %3].
            /// </summary>
            StatusFirmwareUpdated = 0x4000002C,

            /// <summary>
            ///     MessageId: StatusDriversLeakingLockedPages
            ///     MessageText:
            ///     A device driver is leaking locked I/O pages causing system degradation. The system has automatically enabled
            ///     tracking code in order to try and catch the culprit.
            /// </summary>
            StatusDriversLeakingLockedPages = 0x4000002D,

            /// <summary>
            ///     MessageId: StatusMessageRetrieved
            ///     MessageText:
            ///     The Alpc message being canceled has already been retrieved from the queue on the other side.
            /// </summary>
            StatusMessageRetrieved = 0x4000002E,

            /// <summary>
            ///     MessageId: StatusSystemPowerstateTransition
            ///     MessageText:
            ///     The system power state is transitioning from %2 to %3.
            /// </summary>
            StatusSystemPowerstateTransition = 0x4000002F,

            /// <summary>
            ///     MessageId: StatusAlpcCheckCompletionList
            ///     MessageText:
            ///     The receive operation was successful. Check the Alpc completion list for the received message.
            /// </summary>
            StatusAlpcCheckCompletionList = 0x40000030,

            /// <summary>
            ///     MessageId: StatusSystemPowerstateComplexTransition
            ///     MessageText:
            ///     The system power state is transitioning from %2 to %3 but could enter %4.
            /// </summary>
            StatusSystemPowerstateComplexTransition = 0x40000031,

            /// <summary>
            ///     MessageId: StatusAccessAuditByPolicy
            ///     MessageText:
            ///     Access to %1 is monitored by policy rule %2.
            /// </summary>
            StatusAccessAuditByPolicy = 0x40000032,

            /// <summary>
            ///     MessageId: StatusAbandonHiberfile
            ///     MessageText:
            ///     A valid hibernation file has been invalidated and should be abandoned.
            /// </summary>
            StatusAbandonHiberfile = 0x40000033,

            /// <summary>
            ///     MessageId: StatusBizrulesNotEnabled
            ///     MessageText:
            ///     Business rule scripts are disabled for the calling application.
            /// </summary>
            StatusBizrulesNotEnabled = 0x40000034,

            /// <summary>
            ///     MessageId: DbgReplyLater
            ///     MessageText:
            ///     Debugger will reply later.
            /// </summary>
            DbgReplyLater = 0x40010001,

            /// <summary>
            ///     MessageId: DbgUnableToProvideHandle
            ///     MessageText:
            ///     Debugger cannot provide handle.
            /// </summary>
            DbgUnableToProvideHandle = 0x40010002,

            /// <summary>
            ///     MessageId: DbgTerminateThread
            ///     MessageText:
            ///     Debugger terminated thread.
            /// </summary>
            DbgTerminateThread = 0x40010003, // winnt

            /// <summary>
            ///     MessageId: DbgTerminateProcess
            ///     MessageText:
            ///     Debugger terminated process.
            /// </summary>
            DbgTerminateProcess = 0x40010004, // winnt

            /// <summary>
            ///     MessageId: DbgControlC
            ///     MessageText:
            ///     Debugger got control C.
            /// </summary>
            DbgControlC = 0x40010005, // winnt

            /// <summary>
            ///     MessageId: DbgPrintexceptionC
            ///     MessageText:
            ///     Debugger printed exception on control C.
            /// </summary>
            DbgPrintexceptionC = 0x40010006, // winnt

            /// <summary>
            ///     MessageId: DbgRipexception
            ///     MessageText:
            ///     Debugger received Rip exception.
            /// </summary>
            DbgRipexception = 0x40010007, // winnt

            /// <summary>
            ///     MessageId: DbgControlBreak
            ///     MessageText:
            ///     Debugger received control break.
            /// </summary>
            DbgControlBreak = 0x40010008, // winnt

            /// <summary>
            ///     MessageId: DbgCommandException
            ///     MessageText:
            ///     Debugger command communication exception.
            /// </summary>
            DbgCommandException = 0x40010009, // winnt

            /// <summary>
            ///     MessageId: StatusHeuristicDamagePossible
            ///     MessageText:
            ///     The attempt to commit the Transaction completed, but it is possible that some portion of the transaction tree did
            ///     not commit successfully due to heuristics.  Therefore it is possible that some data modified in the transaction may
            ///     not have committed, resulting in transactional inconsistency.  If possible, check the consistency of the associated
            ///     data.
            /// </summary>
            StatusHeuristicDamagePossible = 0x40190001,

            /////////////////////////////////////////////////////////////////////////
            //
            // Standard Warning values
            //
            //
            // Note:  Do Not use the value = 0x80000000, as this is a non-portable value
            //        for the NtSuccess macro. Warning values start with a code of 1.
            //
            /////////////////////////////////////////////////////////////////////////

            /// <summary>
            ///     MessageId: StatusGuardPageViolation
            ///     MessageText:
            ///     {Exception}
            ///     Guard Page Exception
            ///     A page of memory that marks the end of a data structure, such as a stack or an array, has been accessed.
            /// </summary>
            StatusGuardPageViolation = 0x80000001, // winnt

            /// <summary>
            ///     MessageId: StatusDatatypeMisalignment
            ///     MessageText:
            ///     {Exception}
            ///     Alignment Fault
            ///     A datatype misalignment was detected in a load or store instruction.
            /// </summary>
            StatusDatatypeMisalignment = 0x80000002, // winnt

            /// <summary>
            ///     MessageId: StatusBreakpoint
            ///     MessageText:
            ///     {Exception}
            ///     Breakpoint
            ///     A breakpoint has been reached.
            /// </summary>
            StatusBreakpoint = 0x80000003, // winnt

            /// <summary>
            ///     MessageId: StatusSingleStep
            ///     MessageText:
            ///     {Exception}
            ///     Single Step
            ///     A single step or trace operation has just been completed.
            /// </summary>
            StatusSingleStep = 0x80000004, // winnt

            /// <summary>
            ///     MessageId: StatusBufferOverflow
            ///     MessageText:
            ///     {Buffer Overflow}
            ///     The data was too large to fit into the specified buffer.
            /// </summary>
            StatusBufferOverflow = 0x80000005,

            /// <summary>
            ///     MessageId: StatusNoMoreFiles
            ///     MessageText:
            ///     {No More Files}
            ///     No more files were found which match the file specification.
            /// </summary>
            StatusNoMoreFiles = 0x80000006,

            /// <summary>
            ///     MessageId: StatusWakeSystemDebugger
            ///     MessageText:
            ///     {Kernel Debugger Awakened}
            ///     the system debugger was awakened by an interrupt.
            /// </summary>
            StatusWakeSystemDebugger = 0x80000007,

            /// <summary>
            ///     MessageId: StatusHandlesClosed
            ///     MessageText:
            ///     {Handles Closed}
            ///     Handles to objects have been automatically closed as a result of the requested operation.
            /// </summary>
            StatusHandlesClosed = 0x8000000A,

            /// <summary>
            ///     MessageId: StatusNoInheritance
            ///     MessageText:
            ///     {Non-Inheritable Acl}
            ///     An access control list (Ac, contains no components that can be inherited.
            /// </summary>
            StatusNoInheritance = 0x8000000B,

            /// <summary>
            ///     MessageId: StatusGuidSubstitutionMade
            ///     MessageText:
            ///     {Guid Substitution}
            ///     During the translation of a global identifier (Guid, to a Windows security Id (Sid,, no administratively-defined
            ///     Guid prefix was found. A substitute prefix was used, which will not compromise system security. However, this may
            ///     provide a more restrictive access than intended.
            /// </summary>
            StatusGuidSubstitutionMade = 0x8000000C,

            /// <summary>
            ///     MessageId: StatusPartialCopy
            ///     MessageText:
            ///     {Partial Copy}
            ///     Due to protection conflicts not all the requested bytes could be copied.
            /// </summary>
            StatusPartialCopy = 0x8000000D,

            /// <summary>
            ///     MessageId: StatusDevicePaperEmpty
            ///     MessageText:
            ///     {Out of Paper}
            ///     The printer is out of paper.
            /// </summary>
            StatusDevicePaperEmpty = 0x8000000E,

            /// <summary>
            ///     MessageId: StatusDevicePoweredOff
            ///     MessageText:
            ///     {Device Power Is Off}
            ///     The printer power has been turned off.
            /// </summary>
            StatusDevicePoweredOff = 0x8000000F,

            /// <summary>
            ///     MessageId: StatusDeviceOffLine
            ///     MessageText:
            ///     {Device Offline}
            ///     The printer has been taken offline.
            /// </summary>
            StatusDeviceOffLine = 0x80000010,

            /// <summary>
            ///     MessageId: StatusDeviceBusy
            ///     MessageText:
            ///     {Device Busy}
            ///     The device is currently busy.
            /// </summary>
            StatusDeviceBusy = 0x80000011,

            /// <summary>
            ///     MessageId: StatusNoMoreEas
            ///     MessageText:
            ///     {No More EAs}
            ///     No more extended attributes (EAs, were found for the file.
            /// </summary>
            StatusNoMoreEas = 0x80000012,

            /// <summary>
            ///     MessageId: StatusInvalidEaName
            ///     MessageText:
            ///     {Illegal Ea}
            ///     The specified extended attribute (Ea, name contains at least one illegal character.
            /// </summary>
            StatusInvalidEaName = 0x80000013,

            /// <summary>
            ///     MessageId: StatusEaListInconsistent
            ///     MessageText:
            ///     {Inconsistent Ea List}
            ///     The extended attribute (Ea, list is inconsistent.
            /// </summary>
            StatusEaListInconsistent = 0x80000014,

            /// <summary>
            ///     MessageId: StatusInvalidEaFlag
            ///     MessageText:
            ///     {Invalid Ea Flag}
            ///     An invalid extended attribute (Ea, flag was set.
            /// </summary>
            StatusInvalidEaFlag = 0x80000015,

            /// <summary>
            ///     MessageId: StatusVerifyRequired
            ///     MessageText:
            ///     {Verifying Disk}
            ///     The media has changed and a verify operation is in progress so no reads or writes may be performed to the device,
            ///     except those used in the verify operation.
            /// </summary>
            StatusVerifyRequired = 0x80000016,

            /// <summary>
            ///     MessageId: StatusExtraneousInformation
            ///     MessageText:
            ///     {Too Much Information}
            ///     The specified access control list (Ac, contained more information than was expected.
            /// </summary>
            StatusExtraneousInformation = 0x80000017,

            /// <summary>
            ///     MessageId: StatusRxactCommitNecessary
            ///     MessageText:
            ///     This warning level status indicates that the transaction state already exists for the registry sub-tree, but that a
            ///     transaction commit was previously aborted.
            ///     The commit has Not been completed, but has not been rolled back either (so it may still be committed if desired,.
            /// </summary>
            StatusRxactCommitNecessary = 0x80000018,

            /// <summary>
            ///     MessageId: StatusNoMoreEntries
            ///     MessageText:
            ///     {No More Entries}
            ///     No more entries are available from an enumeration operation.
            /// </summary>
            StatusNoMoreEntries = 0x8000001A,

            /// <summary>
            ///     MessageId: StatusFilemarkDetected
            ///     MessageText:
            ///     {Filemark Found}
            ///     A filemark was detected.
            /// </summary>
            StatusFilemarkDetected = 0x8000001B,

            /// <summary>
            ///     MessageId: StatusMediaChanged
            ///     MessageText:
            ///     {Media Changed}
            ///     The media may have changed.
            /// </summary>
            StatusMediaChanged = 0x8000001C,

            /// <summary>
            ///     MessageId: StatusBusReset
            ///     MessageText:
            ///     {I/O Bus Reset}
            ///     An I/O bus reset was detected.
            /// </summary>
            StatusBusReset = 0x8000001D,

            /// <summary>
            ///     MessageId: StatusEndOfMedia
            ///     MessageText:
            ///     {End of Media}
            ///     The end of the media was encountered.
            /// </summary>
            StatusEndOfMedia = 0x8000001E,

            /// <summary>
            ///     MessageId: StatusBeginningOfMedia
            ///     MessageText:
            ///     Beginning of tape or partition has been detected.
            /// </summary>
            StatusBeginningOfMedia = 0x8000001F,

            /// <summary>
            ///     MessageId: StatusMediaCheck
            ///     MessageText:
            ///     {Media Changed}
            ///     The media may have changed.
            /// </summary>
            StatusMediaCheck = 0x80000020,

            /// <summary>
            ///     MessageId: StatusSetmarkDetected
            ///     MessageText:
            ///     A tape access reached a setmark.
            /// </summary>
            StatusSetmarkDetected = 0x80000021,

            /// <summary>
            ///     MessageId: StatusNoDataDetected
            ///     MessageText:
            ///     During a tape access, the end of the data written is reached.
            /// </summary>
            StatusNoDataDetected = 0x80000022,

            /// <summary>
            ///     MessageId: StatusRedirectorHasOpenHandles
            ///     MessageText:
            ///     The redirector is in use and cannot be unloaded.
            /// </summary>
            StatusRedirectorHasOpenHandles = 0x80000023,

            /// <summary>
            ///     MessageId: StatusServerHasOpenHandles
            ///     MessageText:
            ///     The server is in use and cannot be unloaded.
            /// </summary>
            StatusServerHasOpenHandles = 0x80000024,

            /// <summary>
            ///     MessageId: StatusAlreadyDisconnected
            ///     MessageText:
            ///     The specified connection has already been disconnected.
            /// </summary>
            StatusAlreadyDisconnected = 0x80000025,

            /// <summary>
            ///     MessageId: StatusLongjump
            ///     MessageText:
            ///     A long jump has been executed.
            /// </summary>
            StatusLongjump = 0x80000026, // winnt

            /// <summary>
            ///     MessageId: StatusCleanerCartridgeInstalled
            ///     MessageText:
            ///     A cleaner cartridge is present in the tape library.
            /// </summary>
            StatusCleanerCartridgeInstalled = 0x80000027,

            /// <summary>
            ///     MessageId: StatusPlugplayQueryVetoed
            ///     MessageText:
            ///     The Plug and Play query operation was not successful.
            /// </summary>
            StatusPlugplayQueryVetoed = 0x80000028,

            /// <summary>
            ///     MessageId: StatusUnwindConsolidate
            ///     MessageText:
            ///     A frame consolidation has been executed.
            /// </summary>
            StatusUnwindConsolidate = 0x80000029, // winnt

            /// <summary>
            ///     MessageId: StatusRegistryHiveRecovered
            ///     MessageText:
            ///     {Registry Hive Recovered}
            ///     Registry hive (file,:
            ///     %hs
            ///     was corrupted and it has been recovered. Some data might have been lost.
            /// </summary>
            StatusRegistryHiveRecovered = 0x8000002A,

            /// <summary>
            ///     MessageId: StatusDllMightBeInsecure
            ///     MessageText:
            ///     The application is attempting to run executable code from the module %hs. This may be insecure. An alternative,
            ///     %hs, is available. Should the application use the secure module %hs?
            /// </summary>
            StatusDllMightBeInsecure = 0x8000002B,

            /// <summary>
            ///     MessageId: StatusDllMightBeIncompatible
            ///     MessageText:
            ///     The application is loading executable code from the module %hs. This is secure, but may be incompatible with
            ///     previous releases of the operating system. An alternative, %hs, is available. Should the application use the secure
            ///     module %hs?
            /// </summary>
            StatusDllMightBeIncompatible = 0x8000002C,

            /// <summary>
            ///     MessageId: StatusStoppedOnSymlink
            ///     MessageText:
            ///     The create operation stopped after reaching a symbolic link.
            /// </summary>
            StatusStoppedOnSymlink = 0x8000002D,

            /// <summary>
            ///     MessageId: StatusCannotGrantRequestedOplock
            ///     MessageText:
            ///     An oplock of the requested level cannot be granted.  An oplock of a lower level may be available.
            /// </summary>
            StatusCannotGrantRequestedOplock = 0x8000002E,

            /// <summary>
            ///     MessageId: StatusNoAceCondition
            ///     MessageText:
            ///     {No Ace Condition}
            ///     The specified access control entry (Ace, does not contain a condition.
            /// </summary>
            StatusNoAceCondition = 0x8000002F,

            /// <summary>
            ///     MessageId: DbgExceptionNotHandled
            ///     MessageText:
            ///     Debugger did not handle the exception.
            /// </summary>
            DbgExceptionNotHandled = 0x80010001, // winnt

            /// <summary>
            ///     MessageId: StatusClusterNodeAlreadyUp
            ///     MessageText:
            ///     The cluster node is already up.
            /// </summary>
            StatusClusterNodeAlreadyUp = 0x80130001,

            /// <summary>
            ///     MessageId: StatusClusterNodeAlreadyDown
            ///     MessageText:
            ///     The cluster node is already down.
            /// </summary>
            StatusClusterNodeAlreadyDown = 0x80130002,

            /// <summary>
            ///     MessageId: StatusClusterNetworkAlreadyOnline
            ///     MessageText:
            ///     The cluster network is already online.
            /// </summary>
            StatusClusterNetworkAlreadyOnline = 0x80130003,

            /// <summary>
            ///     MessageId: StatusClusterNetworkAlreadyOffline
            ///     MessageText:
            ///     The cluster network is already offline.
            /// </summary>
            StatusClusterNetworkAlreadyOffline = 0x80130004,

            /// <summary>
            ///     MessageId: StatusClusterNodeAlreadyMember
            ///     MessageText:
            ///     The cluster node is already a member of the cluster.
            /// </summary>
            StatusClusterNodeAlreadyMember = 0x80130005,

            /// <summary>
            ///     MessageId: StatusFltBufferTooSmall
            ///     MessageText:
            ///     {Buffer too small}
            ///     The buffer is too small to contain the entry. No information has been written to the buffer.
            /// </summary>
            StatusFltBufferTooSmall = 0x801C0001,

            /// <summary>
            ///     MessageId: StatusFvePartialMetadata
            ///     MessageText:
            ///     Volume Metadata read or write is incomplete.
            /// </summary>
            StatusFvePartialMetadata = 0x80210001,

            /// <summary>
            ///     MessageId: StatusFveTransientState
            ///     MessageText:
            ///     BitLocker encryption keys were ignored because the volume was in a transient state.
            /// </summary>
            StatusFveTransientState = 0x80210002,

            /////////////////////////////////////////////////////////////////////////
            //
            //  Standard Error values
            //
            /////////////////////////////////////////////////////////////////////////

            /// <summary>
            ///     MessageId: StatusUnsuccessful
            ///     MessageText:
            ///     {Operation Failed}
            ///     The requested operation was unsuccessful.
            /// </summary>
            StatusUnsuccessful = 0xC0000001,

            /// <summary>
            ///     MessageId: StatusNotImplemented
            ///     MessageText:
            ///     {Not Implemented}
            ///     The requested operation is not implemented.
            /// </summary>
            StatusNotImplemented = 0xC0000002,

            /// <summary>
            ///     MessageId: StatusInvalidInfoClass
            ///     MessageText:
            ///     {Invalid Parameter}
            ///     The specified information class is not a valid information class for the specified object.
            /// </summary>
            StatusInvalidInfoClass = 0xC0000003, // ntsubauth

            /// <summary>
            ///     MessageId: StatusInfoLengthMismatch
            ///     MessageText:
            ///     The specified information record length does not match the length required for the specified information class.
            /// </summary>
            StatusInfoLengthMismatch = 0xC0000004,

            /// <summary>
            ///     MessageId: StatusAccessViolation
            ///     MessageText:
            ///     The instruction at = 0x%08lx referenced memory at = 0x%08lx. The memory could not be %s.
            /// </summary>
            StatusAccessViolation = 0xC0000005, // winnt

            /// <summary>
            ///     MessageId: StatusInPageError
            ///     MessageText:
            ///     The instruction at = 0x%p referenced memory at = 0x%p. The required data was not placed into memory because of an
            ///     I/O error status of = 0x%x.
            /// </summary>
            StatusInPageError = 0xC0000006, // winnt

            /// <summary>
            ///     MessageId: StatusPagefileQuota
            ///     MessageText:
            ///     The pagefile quota for the process has been exhausted.
            /// </summary>
            StatusPagefileQuota = 0xC0000007,

            /// <summary>
            ///     MessageId: StatusInvalidHandle
            ///     MessageText:
            ///     An invalid Handle was specified.
            /// </summary>
            StatusInvalidHandle = 0xC0000008, // winnt

            /// <summary>
            ///     MessageId: StatusBadInitialStack
            ///     MessageText:
            ///     An invalid initial stack was specified in a call to NtCreateThread.
            /// </summary>
            StatusBadInitialStack = 0xC0000009,

            /// <summary>
            ///     MessageId: StatusBadInitialPc
            ///     MessageText:
            ///     An invalid initial start address was specified in a call to NtCreateThread.
            /// </summary>
            StatusBadInitialPc = 0xC000000A,

            /// <summary>
            ///     MessageId: StatusInvalidCid
            ///     MessageText:
            ///     An invalid Client Id was specified.
            /// </summary>
            StatusInvalidCid = 0xC000000B,

            /// <summary>
            ///     MessageId: StatusTimerNotCanceled
            ///     MessageText:
            ///     An attempt was made to cancel or set a timer that has an associated Apc and the subject thread is not the thread
            ///     that originally set the timer with an associated Apc routine.
            /// </summary>
            StatusTimerNotCanceled = 0xC000000C,

            /// <summary>
            ///     MessageId: StatusInvalidParameter
            ///     MessageText:
            ///     An invalid parameter was passed to a service or function.
            /// </summary>
            StatusInvalidParameter = 0xC000000D, // winnt

            /// <summary>
            ///     MessageId: StatusNoSuchDevice
            ///     MessageText:
            ///     A device which does not exist was specified.
            /// </summary>
            StatusNoSuchDevice = 0xC000000E,

            /// <summary>
            ///     MessageId: StatusNoSuchFile
            ///     MessageText:
            ///     {File Not Found}
            ///     The file %hs does not exist.
            /// </summary>
            StatusNoSuchFile = 0xC000000F,

            /// <summary>
            ///     MessageId: StatusInvalidDeviceRequest
            ///     MessageText:
            ///     The specified request is not a valid operation for the target device.
            /// </summary>
            StatusInvalidDeviceRequest = 0xC0000010,

            /// <summary>
            ///     MessageId: StatusEndOfFile
            ///     MessageText:
            ///     The end-of-file marker has been reached. There is no valid data in the file beyond this marker.
            /// </summary>
            StatusEndOfFile = 0xC0000011,

            /// <summary>
            ///     MessageId: StatusWrongVolume
            ///     MessageText:
            ///     {Wrong Volume}
            ///     The wrong volume is in the drive.
            ///     Please insert volume %hs into drive %hs.
            /// </summary>
            StatusWrongVolume = 0xC0000012,

            /// <summary>
            ///     MessageId: StatusNoMediaInDevice
            ///     MessageText:
            ///     {No Disk}
            ///     There is no disk in the drive.
            ///     Please insert a disk into drive %hs.
            /// </summary>
            StatusNoMediaInDevice = 0xC0000013,

            /// <summary>
            ///     MessageId: StatusUnrecognizedMedia
            ///     MessageText:
            ///     {Unknown Disk Format}
            ///     The disk in drive %hs is not formatted properly.
            ///     Please check the disk, and reformat if necessary.
            /// </summary>
            StatusUnrecognizedMedia = 0xC0000014,

            /// <summary>
            ///     MessageId: StatusNonexistentSector
            ///     MessageText:
            ///     {Sector Not Found}
            ///     The specified sector does not exist.
            /// </summary>
            StatusNonexistentSector = 0xC0000015,

            /// <summary>
            ///     MessageId: StatusMoreProcessingRequired
            ///     MessageText:
            ///     {Still Busy}
            ///     The specified I/O request packet (Irp, cannot be disposed of because the I/O operation is not complete.
            /// </summary>
            StatusMoreProcessingRequired = 0xC0000016,

            /// <summary>
            ///     MessageId: StatusNoMemory
            ///     MessageText:
            ///     {Not Enough Quota}
            ///     Not enough virtual memory or paging file quota is available to complete the specified operation.
            /// </summary>
            StatusNoMemory = 0xC0000017, // winnt

            /// <summary>
            ///     MessageId: StatusConflictingAddresses
            ///     MessageText:
            ///     {Conflicting Address Range}
            ///     The specified address range conflicts with the address space.
            /// </summary>
            StatusConflictingAddresses = 0xC0000018,

            /// <summary>
            ///     MessageId: StatusNotMappedView
            ///     MessageText:
            ///     Address range to unmap is not a mapped view.
            /// </summary>
            StatusNotMappedView = 0xC0000019,

            /// <summary>
            ///     MessageId: StatusUnableToFreeVm
            ///     MessageText:
            ///     Virtual memory cannot be freed.
            /// </summary>
            StatusUnableToFreeVm = 0xC000001A,

            /// <summary>
            ///     MessageId: StatusUnableToDeleteSection
            ///     MessageText:
            ///     Specified section cannot be deleted.
            /// </summary>
            StatusUnableToDeleteSection = 0xC000001B,

            /// <summary>
            ///     MessageId: StatusInvalidSystemService
            ///     MessageText:
            ///     An invalid system service was specified in a system service call.
            /// </summary>
            StatusInvalidSystemService = 0xC000001C,

            /// <summary>
            ///     MessageId: StatusIllegalInstruction
            ///     MessageText:
            ///     {Exception}
            ///     Illegal Instruction
            ///     An attempt was made to execute an illegal instruction.
            /// </summary>
            StatusIllegalInstruction = 0xC000001D, // winnt

            /// <summary>
            ///     MessageId: StatusInvalidLockSequence
            ///     MessageText:
            ///     {Invalid Lock Sequence}
            ///     An attempt was made to execute an invalid lock sequence.
            /// </summary>
            StatusInvalidLockSequence = 0xC000001E,

            /// <summary>
            ///     MessageId: StatusInvalidViewSize
            ///     MessageText:
            ///     {Invalid Mapping}
            ///     An attempt was made to create a view for a section which is bigger than the section.
            /// </summary>
            StatusInvalidViewSize = 0xC000001F,

            /// <summary>
            ///     MessageId: StatusInvalidFileForSection
            ///     MessageText:
            ///     {Bad File}
            ///     The attributes of the specified mapping file for a section of memory cannot be read.
            /// </summary>
            StatusInvalidFileForSection = 0xC0000020,

            /// <summary>
            ///     MessageId: StatusAlreadyCommitted
            ///     MessageText:
            ///     {Already Committed}
            ///     The specified address range is already committed.
            /// </summary>
            StatusAlreadyCommitted = 0xC0000021,

            /// <summary>
            ///     MessageId: StatusAccessDenied
            ///     MessageText:
            ///     {Access Denied}
            ///     A process has requested access to an object, but has not been granted those access rights.
            /// </summary>
            StatusAccessDenied = 0xC0000022,

            /// <summary>
            ///     MessageId: StatusBufferTooSmall
            ///     MessageText:
            ///     {Buffer Too Small}
            ///     The buffer is too small to contain the entry. No information has been written to the buffer.
            /// </summary>
            StatusBufferTooSmall = 0xC0000023,

            /// <summary>
            ///     MessageId: StatusObjectTypeMismatch
            ///     MessageText:
            ///     {Wrong Type}
            ///     There is a mismatch between the type of object required by the requested operation and the type of object that is
            ///     specified in the request.
            /// </summary>
            StatusObjectTypeMismatch = 0xC0000024,

            /// <summary>
            ///     MessageId: StatusNoncontinuableException
            ///     MessageText:
            ///     {Exception}
            ///     Cannot Continue
            ///     Windows cannot continue from this exception.
            /// </summary>
            StatusNoncontinuableException = 0xC0000025, // winnt

            /// <summary>
            ///     MessageId: StatusInvalidDisposition
            ///     MessageText:
            ///     An invalid exception disposition was returned by an exception handler.
            /// </summary>
            StatusInvalidDisposition = 0xC0000026, // winnt

            /// <summary>
            ///     MessageId: StatusUnwind
            ///     MessageText:
            ///     Unwind exception code.
            /// </summary>
            StatusUnwind = 0xC0000027,

            /// <summary>
            ///     MessageId: StatusBadStack
            ///     MessageText:
            ///     An invalid or unaligned stack was encountered during an unwind operation.
            /// </summary>
            StatusBadStack = 0xC0000028,

            /// <summary>
            ///     MessageId: StatusInvalidUnwindTarget
            ///     MessageText:
            ///     An invalid unwind target was encountered during an unwind operation.
            /// </summary>
            StatusInvalidUnwindTarget = 0xC0000029,

            /// <summary>
            ///     MessageId: StatusNotLocked
            ///     MessageText:
            ///     An attempt was made to unlock a page of memory which was not locked.
            /// </summary>
            StatusNotLocked = 0xC000002A,

            /// <summary>
            ///     MessageId: StatusParityError
            ///     MessageText:
            ///     Device parity error on I/O operation.
            /// </summary>
            StatusParityError = 0xC000002B,

            /// <summary>
            ///     MessageId: StatusUnableToDecommitVm
            ///     MessageText:
            ///     An attempt was made to decommit uncommitted virtual memory.
            /// </summary>
            StatusUnableToDecommitVm = 0xC000002C,

            /// <summary>
            ///     MessageId: StatusNotCommitted
            ///     MessageText:
            ///     An attempt was made to change the attributes on memory that has not been committed.
            /// </summary>
            StatusNotCommitted = 0xC000002D,

            /// <summary>
            ///     MessageId: StatusInvalidPortAttributes
            ///     MessageText:
            ///     Invalid Object Attributes specified to NtCreatePort or invalid Port Attributes specified to NtConnectPort
            /// </summary>
            StatusInvalidPortAttributes = 0xC000002E,

            /// <summary>
            ///     MessageId: StatusPortMessageTooLong
            ///     MessageText:
            ///     Length of message passed to NtRequestPort or NtRequestWaitReplyPort was longer than the maximum message allowed by
            ///     the port.
            /// </summary>
            StatusPortMessageTooLong = 0xC000002F,

            /// <summary>
            ///     MessageId: StatusInvalidParameterMix
            ///     MessageText:
            ///     An invalid combination of parameters was specified.
            /// </summary>
            StatusInvalidParameterMix = 0xC0000030,

            /// <summary>
            ///     MessageId: StatusInvalidQuotaLower
            ///     MessageText:
            ///     An attempt was made to lower a quota limit below the current usage.
            /// </summary>
            StatusInvalidQuotaLower = 0xC0000031,

            /// <summary>
            ///     MessageId: StatusDiskCorruptError
            ///     MessageText:
            ///     {Corrupt Disk}
            ///     The file system structure on the disk is corrupt and unusable.
            ///     Please run the Chkdsk utility on the volume %hs.
            /// </summary>
            StatusDiskCorruptError = 0xC0000032,

            /// <summary>
            ///     MessageId: StatusObjectNameInvalid
            ///     MessageText:
            ///     Object Name invalid.
            /// </summary>
            StatusObjectNameInvalid = 0xC0000033,

            /// <summary>
            ///     MessageId: StatusObjectNameNotFound
            ///     MessageText:
            ///     Object Name not found.
            /// </summary>
            StatusObjectNameNotFound = 0xC0000034,

            /// <summary>
            ///     MessageId: StatusObjectNameCollision
            ///     MessageText:
            ///     Object Name already exists.
            /// </summary>
            StatusObjectNameCollision = 0xC0000035,

            /// <summary>
            ///     MessageId: StatusPortDisconnected
            ///     MessageText:
            ///     Attempt to send a message to a disconnected communication port.
            /// </summary>
            StatusPortDisconnected = 0xC0000037,

            /// <summary>
            ///     MessageId: StatusDeviceAlreadyAttached
            ///     MessageText:
            ///     An attempt was made to attach to a device that was already attached to another device.
            /// </summary>
            StatusDeviceAlreadyAttached = 0xC0000038,

            /// <summary>
            ///     MessageId: StatusObjectPathInvalid
            ///     MessageText:
            ///     Object Path Component was not a directory object.
            /// </summary>
            StatusObjectPathInvalid = 0xC0000039,

            /// <summary>
            ///     MessageId: StatusObjectPathNotFound
            ///     MessageText:
            ///     {Path Not Found}
            ///     The path %hs does not exist.
            /// </summary>
            StatusObjectPathNotFound = 0xC000003A,

            /// <summary>
            ///     MessageId: StatusObjectPathSyntaxBad
            ///     MessageText:
            ///     Object Path Component was not a directory object.
            /// </summary>
            StatusObjectPathSyntaxBad = 0xC000003B,

            /// <summary>
            ///     MessageId: StatusDataOverrun
            ///     MessageText:
            ///     {Data Overrun}
            ///     A data overrun error occurred.
            /// </summary>
            StatusDataOverrun = 0xC000003C,

            /// <summary>
            ///     MessageId: StatusDataLateError
            ///     MessageText:
            ///     {Data Late}
            ///     A data late error occurred.
            /// </summary>
            StatusDataLateError = 0xC000003D,

            /// <summary>
            ///     MessageId: StatusDataError
            ///     MessageText:
            ///     {Data Error}
            ///     An error in reading or writing data occurred.
            /// </summary>
            StatusDataError = 0xC000003E,

            /// <summary>
            ///     MessageId: StatusCrcError
            ///     MessageText:
            ///     {Bad Crc}
            ///     A cyclic redundancy check (Crc, checksum error occurred.
            /// </summary>
            StatusCrcError = 0xC000003F,

            /// <summary>
            ///     MessageId: StatusSectionTooBig
            ///     MessageText:
            ///     {Section Too Large}
            ///     The specified section is too big to map the file.
            /// </summary>
            StatusSectionTooBig = 0xC0000040,

            /// <summary>
            ///     MessageId: StatusPortConnectionRefused
            ///     MessageText:
            ///     The NtConnectPort request is refused.
            /// </summary>
            StatusPortConnectionRefused = 0xC0000041,

            /// <summary>
            ///     MessageId: StatusInvalidPortHandle
            ///     MessageText:
            ///     The type of port handle is invalid for the operation requested.
            /// </summary>
            StatusInvalidPortHandle = 0xC0000042,

            /// <summary>
            ///     MessageId: StatusSharingViolation
            ///     MessageText:
            ///     A file cannot be opened because the share access flags are incompatible.
            /// </summary>
            StatusSharingViolation = 0xC0000043,

            /// <summary>
            ///     MessageId: StatusQuotaExceeded
            ///     MessageText:
            ///     Insufficient quota exists to complete the operation
            /// </summary>
            StatusQuotaExceeded = 0xC0000044,

            /// <summary>
            ///     MessageId: StatusInvalidPageProtection
            ///     MessageText:
            ///     The specified page protection was not valid.
            /// </summary>
            StatusInvalidPageProtection = 0xC0000045,

            /// <summary>
            ///     MessageId: StatusMutantNotOwned
            ///     MessageText:
            ///     An attempt to release a mutant object was made by a thread that was not the owner of the mutant object.
            /// </summary>
            StatusMutantNotOwned = 0xC0000046,

            /// <summary>
            ///     MessageId: StatusSemaphoreLimitExceeded
            ///     MessageText:
            ///     An attempt was made to release a semaphore such that its maximum count would have been exceeded.
            /// </summary>
            StatusSemaphoreLimitExceeded = 0xC0000047,

            /// <summary>
            ///     MessageId: StatusPortAlreadySet
            ///     MessageText:
            ///     An attempt to set a process's DebugPort or ExceptionPort was made, but a port already exists in the process or an
            ///     attempt to set a file's CompletionPort made, but a port was already set in the file or an attempt to set an Alpc
            ///     port's associated completion port was made, but it is already set.
            /// </summary>
            StatusPortAlreadySet = 0xC0000048,

            /// <summary>
            ///     MessageId: StatusSectionNotImage
            ///     MessageText:
            ///     An attempt was made to query image information on a section which does not map an image.
            /// </summary>
            StatusSectionNotImage = 0xC0000049,

            /// <summary>
            ///     MessageId: StatusSuspendCountExceeded
            ///     MessageText:
            ///     An attempt was made to suspend a thread whose suspend count was at its maximum.
            /// </summary>
            StatusSuspendCountExceeded = 0xC000004A,

            /// <summary>
            ///     MessageId: StatusThreadIsTerminating
            ///     MessageText:
            ///     An attempt was made to access a thread that has begun termination.
            /// </summary>
            StatusThreadIsTerminating = 0xC000004B,

            /// <summary>
            ///     MessageId: StatusBadWorkingSetLimit
            ///     MessageText:
            ///     An attempt was made to set the working set limit to an invalid value (minimum greater than maximum, etc,.
            /// </summary>
            StatusBadWorkingSetLimit = 0xC000004C,

            /// <summary>
            ///     MessageId: StatusIncompatibleFileMap
            ///     MessageText:
            ///     A section was created to map a file which is not compatible to an already existing section which maps the same
            ///     file.
            /// </summary>
            StatusIncompatibleFileMap = 0xC000004D,

            /// <summary>
            ///     MessageId: StatusSectionProtection
            ///     MessageText:
            ///     A view to a section specifies a protection which is incompatible with the initial view's protection.
            /// </summary>
            StatusSectionProtection = 0xC000004E,

            /// <summary>
            ///     MessageId: StatusEasNotSupported
            ///     MessageText:
            ///     An operation involving EAs failed because the file system does not support EAs.
            /// </summary>
            StatusEasNotSupported = 0xC000004F,

            /// <summary>
            ///     MessageId: StatusEaTooLarge
            ///     MessageText:
            ///     An Ea operation failed because Ea set is too large.
            /// </summary>
            StatusEaTooLarge = 0xC0000050,

            /// <summary>
            ///     MessageId: StatusNonexistentEaEntry
            ///     MessageText:
            ///     An Ea operation failed because the name or Ea index is invalid.
            /// </summary>
            StatusNonexistentEaEntry = 0xC0000051,

            /// <summary>
            ///     MessageId: StatusNoEasOnFile
            ///     MessageText:
            ///     The file for which EAs were requested has no EAs.
            /// </summary>
            StatusNoEasOnFile = 0xC0000052,

            /// <summary>
            ///     MessageId: StatusEaCorruptError
            ///     MessageText:
            ///     The Ea is corrupt and non-readable.
            /// </summary>
            StatusEaCorruptError = 0xC0000053,

            /// <summary>
            ///     MessageId: StatusFileLockConflict
            ///     MessageText:
            ///     A requested read/write cannot be granted due to a conflicting file lock.
            /// </summary>
            StatusFileLockConflict = 0xC0000054,

            /// <summary>
            ///     MessageId: StatusLockNotGranted
            ///     MessageText:
            ///     A requested file lock cannot be granted due to other existing locks.
            /// </summary>
            StatusLockNotGranted = 0xC0000055,

            /// <summary>
            ///     MessageId: StatusDeletePending
            ///     MessageText:
            ///     A non close operation has been requested of a file object with a delete pending.
            /// </summary>
            StatusDeletePending = 0xC0000056,

            /// <summary>
            ///     MessageId: StatusCtlFileNotSupported
            ///     MessageText:
            ///     An attempt was made to set the control attribute on a file. This attribute is not supported in the target file
            ///     system.
            /// </summary>
            StatusCtlFileNotSupported = 0xC0000057,

            /// <summary>
            ///     MessageId: StatusUnknownRevision
            ///     MessageText:
            ///     Indicates a revision number encountered or specified is not one known by the service. It may be a more recent
            ///     revision than the service is aware of.
            /// </summary>
            StatusUnknownRevision = 0xC0000058,

            /// <summary>
            ///     MessageId: StatusRevisionMismatch
            ///     MessageText:
            ///     Indicates two revision levels are incompatible.
            /// </summary>
            StatusRevisionMismatch = 0xC0000059,

            /// <summary>
            ///     MessageId: StatusInvalidOwner
            ///     MessageText:
            ///     Indicates a particular Security Id may not be assigned as the owner of an object.
            /// </summary>
            StatusInvalidOwner = 0xC000005A,

            /// <summary>
            ///     MessageId: StatusInvalidPrimaryGroup
            ///     MessageText:
            ///     Indicates a particular Security Id may not be assigned as the primary group of an object.
            /// </summary>
            StatusInvalidPrimaryGroup = 0xC000005B,

            /// <summary>
            ///     MessageId: StatusNoImpersonationToken
            ///     MessageText:
            ///     An attempt has been made to operate on an impersonation token by a thread that is not currently impersonating a
            ///     client.
            /// </summary>
            StatusNoImpersonationToken = 0xC000005C,

            /// <summary>
            ///     MessageId: StatusCantDisableMandatory
            ///     MessageText:
            ///     A mandatory group may not be disabled.
            /// </summary>
            StatusCantDisableMandatory = 0xC000005D,

            /// <summary>
            ///     MessageId: StatusNoLogonServers
            ///     MessageText:
            ///     There are currently no logon servers available to service the logon request.
            /// </summary>
            StatusNoLogonServers = 0xC000005E,

            /// <summary>
            ///     MessageId: StatusNoSuchLogonSession
            ///     MessageText:
            ///     A specified logon session does not exist. It may already have been terminated.
            /// </summary>
            StatusNoSuchLogonSession = 0xC000005F,

            /// <summary>
            ///     MessageId: StatusNoSuchPrivilege
            ///     MessageText:
            ///     A specified privilege does not exist.
            /// </summary>
            StatusNoSuchPrivilege = 0xC0000060,

            /// <summary>
            ///     MessageId: StatusPrivilegeNotHeld
            ///     MessageText:
            ///     A required privilege is not held by the client.
            /// </summary>
            StatusPrivilegeNotHeld = 0xC0000061,

            /// <summary>
            ///     MessageId: StatusInvalidAccountName
            ///     MessageText:
            ///     The name provided is not a properly formed account name.
            /// </summary>
            StatusInvalidAccountName = 0xC0000062,

            /// <summary>
            ///     MessageId: StatusUserExists
            ///     MessageText:
            ///     The specified account already exists.
            /// </summary>
            StatusUserExists = 0xC0000063,

            /// <summary>
            ///     MessageId: StatusNoSuchUser
            ///     MessageText:
            ///     The specified account does not exist.
            /// </summary>
            StatusNoSuchUser = 0xC0000064, // ntsubauth

            /// <summary>
            ///     MessageId: StatusGroupExists
            ///     MessageText:
            ///     The specified group already exists.
            /// </summary>
            StatusGroupExists = 0xC0000065,

            /// <summary>
            ///     MessageId: StatusNoSuchGroup
            ///     MessageText:
            ///     The specified group does not exist.
            /// </summary>
            StatusNoSuchGroup = 0xC0000066,

            /// <summary>
            ///     MessageId: StatusMemberInGroup
            ///     MessageText:
            ///     The specified user account is already in the specified group account. Also used to indicate a group cannot be
            ///     deleted because it contains a member.
            /// </summary>
            StatusMemberInGroup = 0xC0000067,

            /// <summary>
            ///     MessageId: StatusMemberNotInGroup
            ///     MessageText:
            ///     The specified user account is not a member of the specified group account.
            /// </summary>
            StatusMemberNotInGroup = 0xC0000068,

            /// <summary>
            ///     MessageId: StatusLastAdmin
            ///     MessageText:
            ///     Indicates the requested operation would disable or delete the last remaining administration account.
            ///     This is not allowed to prevent creating a situation in which the system cannot be administrated.
            /// </summary>
            StatusLastAdmin = 0xC0000069,

            /// <summary>
            ///     MessageId: StatusWrongPassword
            ///     MessageText:
            ///     When trying to update a password, this return status indicates that the value provided as the current password is
            ///     not correct.
            /// </summary>
            StatusWrongPassword = 0xC000006A, // ntsubauth

            /// <summary>
            ///     MessageId: StatusIllFormedPassword
            ///     MessageText:
            ///     When trying to update a password, this return status indicates that the value provided for the new password
            ///     contains values that are not allowed in passwords.
            /// </summary>
            StatusIllFormedPassword = 0xC000006B,

            /// <summary>
            ///     MessageId: StatusPasswordRestriction
            ///     MessageText:
            ///     When trying to update a password, this status indicates that some password update rule has been violated. For
            ///     example, the password may not meet length criteria.
            /// </summary>
            StatusPasswordRestriction = 0xC000006C, // ntsubauth

            /// <summary>
            ///     MessageId: StatusLogonFailure
            ///     MessageText:
            ///     The attempted logon is invalid. This is either due to a bad username or authentication information.
            /// </summary>
            StatusLogonFailure = 0xC000006D, // ntsubauth

            /// <summary>
            ///     MessageId: StatusAccountRestriction
            ///     MessageText:
            ///     Indicates a referenced user name and authentication information are valid, but some user account restriction has
            ///     prevented successful authentication (such as time-of-day restrictions,.
            /// </summary>
            StatusAccountRestriction = 0xC000006E, // ntsubauth

            /// <summary>
            ///     MessageId: StatusInvalidLogonHours
            ///     MessageText:
            ///     The user account has time restrictions and may not be logged onto at this time.
            /// </summary>
            StatusInvalidLogonHours = 0xC000006F, // ntsubauth

            /// <summary>
            ///     MessageId: StatusInvalidWorkstation
            ///     MessageText:
            ///     The user account is restricted such that it may not be used to log on from the source workstation.
            /// </summary>
            StatusInvalidWorkstation = 0xC0000070, // ntsubauth

            /// <summary>
            ///     MessageId: StatusPasswordExpired
            ///     MessageText:
            ///     The user account's password has expired.
            /// </summary>
            StatusPasswordExpired = 0xC0000071, // ntsubauth

            /// <summary>
            ///     MessageId: StatusAccountDisabled
            ///     MessageText:
            ///     The referenced account is currently disabled and may not be logged on to.
            /// </summary>
            StatusAccountDisabled = 0xC0000072, // ntsubauth

            /// <summary>
            ///     MessageId: StatusNoneMapped
            ///     MessageText:
            ///     None of the information to be translated has been translated.
            /// </summary>
            StatusNoneMapped = 0xC0000073,

            /// <summary>
            ///     MessageId: StatusTooManyLuidsRequested
            ///     MessageText:
            ///     The number of LUIDs requested may not be allocated with a single allocation.
            /// </summary>
            StatusTooManyLuidsRequested = 0xC0000074,

            /// <summary>
            ///     MessageId: StatusLuidsExhausted
            ///     MessageText:
            ///     Indicates there are no more LUIDs to allocate.
            /// </summary>
            StatusLuidsExhausted = 0xC0000075,

            /// <summary>
            ///     MessageId: StatusInvalidSubAuthority
            ///     MessageText:
            ///     Indicates the sub-authority value is invalid for the particular use.
            /// </summary>
            StatusInvalidSubAuthority = 0xC0000076,

            /// <summary>
            ///     MessageId: StatusInvalidAcl
            ///     MessageText:
            ///     Indicates the Acl structure is not valid.
            /// </summary>
            StatusInvalidAcl = 0xC0000077,

            /// <summary>
            ///     MessageId: StatusInvalidSid
            ///     MessageText:
            ///     Indicates the Sid structure is not valid.
            /// </summary>
            StatusInvalidSid = 0xC0000078,

            /// <summary>
            ///     MessageId: StatusInvalidSecurityDescr
            ///     MessageText:
            ///     Indicates the SecurityDescriptor structure is not valid.
            /// </summary>
            StatusInvalidSecurityDescr = 0xC0000079,

            /// <summary>
            ///     MessageId: StatusProcedureNotFound
            ///     MessageText:
            ///     Indicates the specified procedure address cannot be found in the Dll.
            /// </summary>
            StatusProcedureNotFound = 0xC000007A,

            /// <summary>
            ///     MessageId: StatusInvalidImageFormat
            ///     MessageText:
            ///     {Bad Image}
            ///     %hs is either not designed to run on Windows or it contains an error. Try installing the program again using the
            ///     original installation media or contact your system administrator or the software vendor for support.
            /// </summary>
            StatusInvalidImageFormat = 0xC000007B,

            /// <summary>
            ///     MessageId: StatusNoToken
            ///     MessageText:
            ///     An attempt was made to reference a token that doesn't exist.
            ///     This is typically done by referencing the token associated with a thread when the thread is not impersonating a
            ///     client.
            /// </summary>
            StatusNoToken = 0xC000007C,

            /// <summary>
            ///     MessageId: StatusBadInheritanceAcl
            ///     MessageText:
            ///     Indicates that an attempt to build either an inherited Acl or Ace was not successful.
            ///     This can be caused by a number of things. One of the more probable causes is the replacement of a CreatorId with an
            ///     Sid that didn't fit into the Ace or Acl.
            /// </summary>
            StatusBadInheritanceAcl = 0xC000007D,

            /// <summary>
            ///     MessageId: StatusRangeNotLocked
            ///     MessageText:
            ///     The range specified in NtUnlockFile was not locked.
            /// </summary>
            StatusRangeNotLocked = 0xC000007E,

            /// <summary>
            ///     MessageId: StatusDiskFull
            ///     MessageText:
            ///     An operation failed because the disk was full.
            /// </summary>
            StatusDiskFull = 0xC000007F,

            /// <summary>
            ///     MessageId: StatusServerDisabled
            ///     MessageText:
            ///     The Guid allocation server is [already] disabled at the moment.
            /// </summary>
            StatusServerDisabled = 0xC0000080,

            /// <summary>
            ///     MessageId: StatusServerNotDisabled
            ///     MessageText:
            ///     The Guid allocation server is [already] enabled at the moment.
            /// </summary>
            StatusServerNotDisabled = 0xC0000081,

            /// <summary>
            ///     MessageId: StatusTooManyGuidsRequested
            ///     MessageText:
            ///     Too many GUIDs were requested from the allocation server at once.
            /// </summary>
            StatusTooManyGuidsRequested = 0xC0000082,

            /// <summary>
            ///     MessageId: StatusGuidsExhausted
            ///     MessageText:
            ///     The GUIDs could not be allocated because the Authority Agent was exhausted.
            /// </summary>
            StatusGuidsExhausted = 0xC0000083,

            /// <summary>
            ///     MessageId: StatusInvalidIdAuthority
            ///     MessageText:
            ///     The value provided was an invalid value for an identifier authority.
            /// </summary>
            StatusInvalidIdAuthority = 0xC0000084,

            /// <summary>
            ///     MessageId: StatusAgentsExhausted
            ///     MessageText:
            ///     There are no more authority agent values available for the given identifier authority value.
            /// </summary>
            StatusAgentsExhausted = 0xC0000085,

            /// <summary>
            ///     MessageId: StatusInvalidVolumeLabel
            ///     MessageText:
            ///     An invalid volume label has been specified.
            /// </summary>
            StatusInvalidVolumeLabel = 0xC0000086,

            /// <summary>
            ///     MessageId: StatusSectionNotExtended
            ///     MessageText:
            ///     A mapped section could not be extended.
            /// </summary>
            StatusSectionNotExtended = 0xC0000087,

            /// <summary>
            ///     MessageId: StatusNotMappedData
            ///     MessageText:
            ///     Specified section to flush does not map a data file.
            /// </summary>
            StatusNotMappedData = 0xC0000088,

            /// <summary>
            ///     MessageId: StatusResourceDataNotFound
            ///     MessageText:
            ///     Indicates the specified image file did not contain a resource section.
            /// </summary>
            StatusResourceDataNotFound = 0xC0000089,

            /// <summary>
            ///     MessageId: StatusResourceTypeNotFound
            ///     MessageText:
            ///     Indicates the specified resource type cannot be found in the image file.
            /// </summary>
            StatusResourceTypeNotFound = 0xC000008A,

            /// <summary>
            ///     MessageId: StatusResourceNameNotFound
            ///     MessageText:
            ///     Indicates the specified resource name cannot be found in the image file.
            /// </summary>
            StatusResourceNameNotFound = 0xC000008B,

            /// <summary>
            ///     MessageId: StatusArrayBoundsExceeded
            ///     MessageText:
            ///     {Exception}
            ///     Array bounds exceeded.
            /// </summary>
            StatusArrayBoundsExceeded = 0xC000008C, // winnt

            /// <summary>
            ///     MessageId: StatusFloatDenormalOperand
            ///     MessageText:
            ///     {Exception}
            ///     Floating-point denormal operand.
            /// </summary>
            StatusFloatDenormalOperand = 0xC000008D, // winnt

            /// <summary>
            ///     MessageId: StatusFloatDivideByZero
            ///     MessageText:
            ///     {Exception}
            ///     Floating-point division by zero.
            /// </summary>
            StatusFloatDivideByZero = 0xC000008E, // winnt

            /// <summary>
            ///     MessageId: StatusFloatInexactResult
            ///     MessageText:
            ///     {Exception}
            ///     Floating-point inexact result.
            /// </summary>
            StatusFloatInexactResult = 0xC000008F, // winnt

            /// <summary>
            ///     MessageId: StatusFloatInvalidOperation
            ///     MessageText:
            ///     {Exception}
            ///     Floating-point invalid operation.
            /// </summary>
            StatusFloatInvalidOperation = 0xC0000090, // winnt

            /// <summary>
            ///     MessageId: StatusFloatOverflow
            ///     MessageText:
            ///     {Exception}
            ///     Floating-point overflow.
            /// </summary>
            StatusFloatOverflow = 0xC0000091, // winnt

            /// <summary>
            ///     MessageId: StatusFloatStackCheck
            ///     MessageText:
            ///     {Exception}
            ///     Floating-point stack check.
            /// </summary>
            StatusFloatStackCheck = 0xC0000092, // winnt

            /// <summary>
            ///     MessageId: StatusFloatUnderflow
            ///     MessageText:
            ///     {Exception}
            ///     Floating-point underflow.
            /// </summary>
            StatusFloatUnderflow = 0xC0000093, // winnt

            /// <summary>
            ///     MessageId: StatusIntegerDivideByZero
            ///     MessageText:
            ///     {Exception}
            ///     Integer division by zero.
            /// </summary>
            StatusIntegerDivideByZero = 0xC0000094, // winnt

            /// <summary>
            ///     MessageId: StatusIntegerOverflow
            ///     MessageText:
            ///     {Exception}
            ///     Integer overflow.
            /// </summary>
            StatusIntegerOverflow = 0xC0000095, // winnt

            /// <summary>
            ///     MessageId: StatusPrivilegedInstruction
            ///     MessageText:
            ///     {Exception}
            ///     Privileged instruction.
            /// </summary>
            StatusPrivilegedInstruction = 0xC0000096, // winnt

            /// <summary>
            ///     MessageId: StatusTooManyPagingFiles
            ///     MessageText:
            ///     An attempt was made to install more paging files than the system supports.
            /// </summary>
            StatusTooManyPagingFiles = 0xC0000097,

            /// <summary>
            ///     MessageId: StatusFileInvalid
            ///     MessageText:
            ///     The volume for a file has been externally altered such that the opened file is no longer valid.
            /// </summary>
            StatusFileInvalid = 0xC0000098,

            /// <summary>
            ///     MessageId: StatusAllottedSpaceExceeded
            ///     MessageText:
            ///     When a block of memory is allotted for future updates, such as the memory allocated to hold discretionary access
            ///     control and primary group information, successive updates may exceed the amount of memory originally allotted.
            ///     Since quota may already have been charged to several processes which have handles to the object, it is not
            ///     reasonable to alter the size of the allocated memory.
            ///     Instead, a request that requires more memory than has been allotted must fail and the StatusAllotedSpaceExceeded
            ///     error returned.
            /// </summary>
            StatusAllottedSpaceExceeded = 0xC0000099,

            /// <summary>
            ///     MessageId: StatusInsufficientResources
            ///     MessageText:
            ///     Insufficient system resources exist to complete the Api.
            /// </summary>
            StatusInsufficientResources = 0xC000009A, // ntsubauth

            /// <summary>
            ///     MessageId: StatusDfsExitPathFound
            ///     MessageText:
            ///     An attempt has been made to open a Dfs exit path control file.
            /// </summary>
            StatusDfsExitPathFound = 0xC000009B,

            /// <summary>
            ///     MessageId: StatusDeviceDataError
            ///     MessageText:
            ///     StatusDeviceDataError
            /// </summary>
            StatusDeviceDataError = 0xC000009C,

            /// <summary>
            ///     MessageId: StatusDeviceNotConnected
            ///     MessageText:
            ///     StatusDeviceNotConnected
            /// </summary>
            StatusDeviceNotConnected = 0xC000009D,

            /// <summary>
            ///     MessageId: StatusDevicePowerFailure
            ///     MessageText:
            ///     StatusDevicePowerFailure
            /// </summary>
            StatusDevicePowerFailure = 0xC000009E,

            /// <summary>
            ///     MessageId: StatusFreeVmNotAtBase
            ///     MessageText:
            ///     Virtual memory cannot be freed as base address is not the base of the region and a region size of zero was
            ///     specified.
            /// </summary>
            StatusFreeVmNotAtBase = 0xC000009F,

            /// <summary>
            ///     MessageId: StatusMemoryNotAllocated
            ///     MessageText:
            ///     An attempt was made to free virtual memory which is not allocated.
            /// </summary>
            StatusMemoryNotAllocated = 0xC00000A0,

            /// <summary>
            ///     MessageId: StatusWorkingSetQuota
            ///     MessageText:
            ///     The working set is not big enough to allow the requested pages to be locked.
            /// </summary>
            StatusWorkingSetQuota = 0xC00000A1,

            /// <summary>
            ///     MessageId: StatusMediaWriteProtected
            ///     MessageText:
            ///     {Write Protect Error}
            ///     The disk cannot be written to because it is write protected. Please remove the write protection from the volume %hs
            ///     in drive %hs.
            /// </summary>
            StatusMediaWriteProtected = 0xC00000A2,

            /// <summary>
            ///     MessageId: StatusDeviceNotReady
            ///     MessageText:
            ///     {Drive Not Ready}
            ///     The drive is not ready for use; its door may be open. Please check drive %hs and make sure that a disk is inserted
            ///     and that the drive door is closed.
            /// </summary>
            StatusDeviceNotReady = 0xC00000A3,

            /// <summary>
            ///     MessageId: StatusInvalidGroupAttributes
            ///     MessageText:
            ///     The specified attributes are invalid, or incompatible with the attributes for the group as a whole.
            /// </summary>
            StatusInvalidGroupAttributes = 0xC00000A4,

            /// <summary>
            ///     MessageId: StatusBadImpersonationLevel
            ///     MessageText:
            ///     A specified impersonation level is invalid.
            ///     Also used to indicate a required impersonation level was not provided.
            /// </summary>
            StatusBadImpersonationLevel = 0xC00000A5,

            /// <summary>
            ///     MessageId: StatusCantOpenAnonymous
            ///     MessageText:
            ///     An attempt was made to open an Anonymous level token.
            ///     Anonymous tokens may not be opened.
            /// </summary>
            StatusCantOpenAnonymous = 0xC00000A6,

            /// <summary>
            ///     MessageId: StatusBadValidationClass
            ///     MessageText:
            ///     The validation information class requested was invalid.
            /// </summary>
            StatusBadValidationClass = 0xC00000A7,

            /// <summary>
            ///     MessageId: StatusBadTokenType
            ///     MessageText:
            ///     The type of a token object is inappropriate for its attempted use.
            /// </summary>
            StatusBadTokenType = 0xC00000A8,

            /// <summary>
            ///     MessageId: StatusBadMasterBootRecord
            ///     MessageText:
            ///     The type of a token object is inappropriate for its attempted use.
            /// </summary>
            StatusBadMasterBootRecord = 0xC00000A9,

            /// <summary>
            ///     MessageId: StatusInstructionMisalignment
            ///     MessageText:
            ///     An attempt was made to execute an instruction at an unaligned address and the host system does not support
            ///     unaligned instruction references.
            /// </summary>
            StatusInstructionMisalignment = 0xC00000AA,

            /// <summary>
            ///     MessageId: StatusInstanceNotAvailable
            ///     MessageText:
            ///     The maximum named pipe instance count has been reached.
            /// </summary>
            StatusInstanceNotAvailable = 0xC00000AB,

            /// <summary>
            ///     MessageId: StatusPipeNotAvailable
            ///     MessageText:
            ///     An instance of a named pipe cannot be found in the listening state.
            /// </summary>
            StatusPipeNotAvailable = 0xC00000AC,

            /// <summary>
            ///     MessageId: StatusInvalidPipeState
            ///     MessageText:
            ///     The named pipe is not in the connected or closing state.
            /// </summary>
            StatusInvalidPipeState = 0xC00000AD,

            /// <summary>
            ///     MessageId: StatusPipeBusy
            ///     MessageText:
            ///     The specified pipe is set to complete operations and there are current I/O operations queued so it cannot be
            ///     changed to queue operations.
            /// </summary>
            StatusPipeBusy = 0xC00000AE,

            /// <summary>
            ///     MessageId: StatusIllegalFunction
            ///     MessageText:
            ///     The specified handle is not open to the server end of the named pipe.
            /// </summary>
            StatusIllegalFunction = 0xC00000AF,

            /// <summary>
            ///     MessageId: StatusPipeDisconnected
            ///     MessageText:
            ///     The specified named pipe is in the disconnected state.
            /// </summary>
            StatusPipeDisconnected = 0xC00000B0,

            /// <summary>
            ///     MessageId: StatusPipeClosing
            ///     MessageText:
            ///     The specified named pipe is in the closing state.
            /// </summary>
            StatusPipeClosing = 0xC00000B1,

            /// <summary>
            ///     MessageId: StatusPipeConnected
            ///     MessageText:
            ///     The specified named pipe is in the connected state.
            /// </summary>
            StatusPipeConnected = 0xC00000B2,

            /// <summary>
            ///     MessageId: StatusPipeListening
            ///     MessageText:
            ///     The specified named pipe is in the listening state.
            /// </summary>
            StatusPipeListening = 0xC00000B3,

            /// <summary>
            ///     MessageId: StatusInvalidReadMode
            ///     MessageText:
            ///     The specified named pipe is not in message mode.
            /// </summary>
            StatusInvalidReadMode = 0xC00000B4,

            /// <summary>
            ///     MessageId: StatusIoTimeout
            ///     MessageText:
            ///     {Device Timeout}
            ///     The specified I/O operation on %hs was not completed before the time-out period expired.
            /// </summary>
            StatusIoTimeout = 0xC00000B5,

            /// <summary>
            ///     MessageId: StatusFileForcedClosed
            ///     MessageText:
            ///     The specified file has been closed by another process.
            /// </summary>
            StatusFileForcedClosed = 0xC00000B6,

            /// <summary>
            ///     MessageId: StatusProfilingNotStarted
            ///     MessageText:
            ///     Profiling not started.
            /// </summary>
            StatusProfilingNotStarted = 0xC00000B7,

            /// <summary>
            ///     MessageId: StatusProfilingNotStopped
            ///     MessageText:
            ///     Profiling not stopped.
            /// </summary>
            StatusProfilingNotStopped = 0xC00000B8,

            /// <summary>
            ///     MessageId: StatusCouldNotInterpret
            ///     MessageText:
            ///     The passed Acl did not contain the minimum required information.
            /// </summary>
            StatusCouldNotInterpret = 0xC00000B9,

            /// <summary>
            ///     MessageId: StatusFileIsADirectory
            ///     MessageText:
            ///     The file that was specified as a target is a directory and the caller specified that it could be anything but a
            ///     directory.
            /// </summary>
            StatusFileIsADirectory = 0xC00000BA,

            // Network specific errors.

            /// <summary>
            ///     MessageId: StatusNotSupported
            ///     MessageText:
            ///     The request is not supported.
            /// </summary>
            StatusNotSupported = 0xC00000BB,

            /// <summary>
            ///     MessageId: StatusRemoteNotListening
            ///     MessageText:
            ///     This remote computer is not listening.
            /// </summary>
            StatusRemoteNotListening = 0xC00000BC,

            /// <summary>
            ///     MessageId: StatusDuplicateName
            ///     MessageText:
            ///     A duplicate name exists on the network.
            /// </summary>
            StatusDuplicateName = 0xC00000BD,

            /// <summary>
            ///     MessageId: StatusBadNetworkPath
            ///     MessageText:
            ///     The network path cannot be located.
            /// </summary>
            StatusBadNetworkPath = 0xC00000BE,

            /// <summary>
            ///     MessageId: StatusNetworkBusy
            ///     MessageText:
            ///     The network is busy.
            /// </summary>
            StatusNetworkBusy = 0xC00000BF,

            /// <summary>
            ///     MessageId: StatusDeviceDoesNotExist
            ///     MessageText:
            ///     This device does not exist.
            /// </summary>
            StatusDeviceDoesNotExist = 0xC00000C0,

            /// <summary>
            ///     MessageId: StatusTooManyCommands
            ///     MessageText:
            ///     The network Bios command limit has been reached.
            /// </summary>
            StatusTooManyCommands = 0xC00000C1,

            /// <summary>
            ///     MessageId: StatusAdapterHardwareError
            ///     MessageText:
            ///     An I/O adapter hardware error has occurred.
            /// </summary>
            StatusAdapterHardwareError = 0xC00000C2,

            /// <summary>
            ///     MessageId: StatusInvalidNetworkResponse
            ///     MessageText:
            ///     The network responded incorrectly.
            /// </summary>
            StatusInvalidNetworkResponse = 0xC00000C3,

            /// <summary>
            ///     MessageId: StatusUnexpectedNetworkError
            ///     MessageText:
            ///     An unexpected network error occurred.
            /// </summary>
            StatusUnexpectedNetworkError = 0xC00000C4,

            /// <summary>
            ///     MessageId: StatusBadRemoteAdapter
            ///     MessageText:
            ///     The remote adapter is not compatible.
            /// </summary>
            StatusBadRemoteAdapter = 0xC00000C5,

            /// <summary>
            ///     MessageId: StatusPrintQueueFull
            ///     MessageText:
            ///     The printer queue is full.
            /// </summary>
            StatusPrintQueueFull = 0xC00000C6,

            /// <summary>
            ///     MessageId: StatusNoSpoolSpace
            ///     MessageText:
            ///     Space to store the file waiting to be printed is not available on the server.
            /// </summary>
            StatusNoSpoolSpace = 0xC00000C7,

            /// <summary>
            ///     MessageId: StatusPrintCancelled
            ///     MessageText:
            ///     The requested print file has been canceled.
            /// </summary>
            StatusPrintCancelled = 0xC00000C8,

            /// <summary>
            ///     MessageId: StatusNetworkNameDeleted
            ///     MessageText:
            ///     The network name was deleted.
            /// </summary>
            StatusNetworkNameDeleted = 0xC00000C9,

            /// <summary>
            ///     MessageId: StatusNetworkAccessDenied
            ///     MessageText:
            ///     Network access is denied.
            /// </summary>
            StatusNetworkAccessDenied = 0xC00000CA,

            /// <summary>
            ///     MessageId: StatusBadDeviceType
            ///     MessageText:
            ///     {Incorrect Network Resource Type}
            ///     The specified device type (Lpt, for example, conflicts with the actual device type on the remote resource.
            /// </summary>
            StatusBadDeviceType = 0xC00000CB,

            /// <summary>
            ///     MessageId: StatusBadNetworkName
            ///     MessageText:
            ///     {Network Name Not Found}
            ///     The specified share name cannot be found on the remote server.
            /// </summary>
            StatusBadNetworkName = 0xC00000CC,

            /// <summary>
            ///     MessageId: StatusTooManyNames
            ///     MessageText:
            ///     The name limit for the local computer network adapter card was exceeded.
            /// </summary>
            StatusTooManyNames = 0xC00000CD,

            /// <summary>
            ///     MessageId: StatusTooManySessions
            ///     MessageText:
            ///     The network Bios session limit was exceeded.
            /// </summary>
            StatusTooManySessions = 0xC00000CE,

            /// <summary>
            ///     MessageId: StatusSharingPaused
            ///     MessageText:
            ///     File sharing has been temporarily paused.
            /// </summary>
            StatusSharingPaused = 0xC00000CF,

            /// <summary>
            ///     MessageId: StatusRequestNotAccepted
            ///     MessageText:
            ///     No more connections can be made to this remote computer at this time because there are already as many connections
            ///     as the computer can accept.
            /// </summary>
            StatusRequestNotAccepted = 0xC00000D0,

            /// <summary>
            ///     MessageId: StatusRedirectorPaused
            ///     MessageText:
            ///     Print or disk redirection is temporarily paused.
            /// </summary>
            StatusRedirectorPaused = 0xC00000D1,

            /// <summary>
            ///     MessageId: StatusNetWriteFault
            ///     MessageText:
            ///     A network data fault occurred.
            /// </summary>
            StatusNetWriteFault = 0xC00000D2,

            /// <summary>
            ///     MessageId: StatusProfilingAtLimit
            ///     MessageText:
            ///     The number of active profiling objects is at the maximum and no more may be started.
            /// </summary>
            StatusProfilingAtLimit = 0xC00000D3,

            /// <summary>
            ///     MessageId: StatusNotSameDevice
            ///     MessageText:
            ///     {Incorrect Volume}
            ///     The target file of a rename request is located on a different device than the source of the rename request.
            /// </summary>
            StatusNotSameDevice = 0xC00000D4,

            /// <summary>
            ///     MessageId: StatusFileRenamed
            ///     MessageText:
            ///     The file specified has been renamed and thus cannot be modified.
            /// </summary>
            StatusFileRenamed = 0xC00000D5,

            /// <summary>
            ///     MessageId: StatusVirtualCircuitClosed
            ///     MessageText:
            ///     {Network Request Timeout}
            ///     The session with a remote server has been disconnected because the time-out interval for a request has expired.
            /// </summary>
            StatusVirtualCircuitClosed = 0xC00000D6,

            /// <summary>
            ///     MessageId: StatusNoSecurityOnObject
            ///     MessageText:
            ///     Indicates an attempt was made to operate on the security of an object that does not have security associated with
            ///     it.
            /// </summary>
            StatusNoSecurityOnObject = 0xC00000D7,

            /// <summary>
            ///     MessageId: StatusCantWait
            ///     MessageText:
            ///     Used to indicate that an operation cannot continue without blocking for I/O.
            /// </summary>
            StatusCantWait = 0xC00000D8,

            /// <summary>
            ///     MessageId: StatusPipeEmpty
            ///     MessageText:
            ///     Used to indicate that a read operation was done on an empty pipe.
            /// </summary>
            StatusPipeEmpty = 0xC00000D9,

            /// <summary>
            ///     MessageId: StatusCantAccessDomainInfo
            ///     MessageText:
            ///     Configuration information could not be read from the domain controller, either because the machine is unavailable,
            ///     or access has been denied.
            /// </summary>
            StatusCantAccessDomainInfo = 0xC00000DA,

            /// <summary>
            ///     MessageId: StatusCantTerminateSelf
            ///     MessageText:
            ///     Indicates that a thread attempted to terminate itself by default (called NtTerminateThread with Nul, and it was the
            ///     last thread in the current process.
            /// </summary>
            StatusCantTerminateSelf = 0xC00000DB,

            /// <summary>
            ///     MessageId: StatusInvalidServerState
            ///     MessageText:
            ///     Indicates the Sam Server was in the wrong state to perform the desired operation.
            /// </summary>
            StatusInvalidServerState = 0xC00000DC,

            /// <summary>
            ///     MessageId: StatusInvalidDomainState
            ///     MessageText:
            ///     Indicates the Domain was in the wrong state to perform the desired operation.
            /// </summary>
            StatusInvalidDomainState = 0xC00000DD,

            /// <summary>
            ///     MessageId: StatusInvalidDomainRole
            ///     MessageText:
            ///     This operation is only allowed for the Primary Domain Controller of the domain.
            /// </summary>
            StatusInvalidDomainRole = 0xC00000DE,

            /// <summary>
            ///     MessageId: StatusNoSuchDomain
            ///     MessageText:
            ///     The specified Domain did not exist.
            /// </summary>
            StatusNoSuchDomain = 0xC00000DF,

            /// <summary>
            ///     MessageId: StatusDomainExists
            ///     MessageText:
            ///     The specified Domain already exists.
            /// </summary>
            StatusDomainExists = 0xC00000E0,

            /// <summary>
            ///     MessageId: StatusDomainLimitExceeded
            ///     MessageText:
            ///     An attempt was made to exceed the limit on the number of domains per server for this release.
            /// </summary>
            StatusDomainLimitExceeded = 0xC00000E1,

            /// <summary>
            ///     MessageId: StatusOplockNotGranted
            ///     MessageText:
            ///     Error status returned when oplock request is denied.
            /// </summary>
            StatusOplockNotGranted = 0xC00000E2,

            /// <summary>
            ///     MessageId: StatusInvalidOplockProtocol
            ///     MessageText:
            ///     Error status returned when an invalid oplock acknowledgment is received by a file system.
            /// </summary>
            StatusInvalidOplockProtocol = 0xC00000E3,

            /// <summary>
            ///     MessageId: StatusInternalDbCorruption
            ///     MessageText:
            ///     This error indicates that the requested operation cannot be completed due to a catastrophic media failure or
            ///     on-disk data structure corruption.
            /// </summary>
            StatusInternalDbCorruption = 0xC00000E4,

            /// <summary>
            ///     MessageId: StatusInternalError
            ///     MessageText:
            ///     An internal error occurred.
            /// </summary>
            StatusInternalError = 0xC00000E5,

            /// <summary>
            ///     MessageId: StatusGenericNotMapped
            ///     MessageText:
            ///     Indicates generic access types were contained in an access mask which should already be mapped to non-generic
            ///     access types.
            /// </summary>
            StatusGenericNotMapped = 0xC00000E6,

            /// <summary>
            ///     MessageId: StatusBadDescriptorFormat
            ///     MessageText:
            ///     Indicates a security descriptor is not in the necessary format (absolute or self-relative,.
            /// </summary>
            StatusBadDescriptorFormat = 0xC00000E7,

            // Status codes raised by the Cache Manager which must be considered as
            // "expected" by its callers.

            /// <summary>
            ///     MessageId: StatusInvalidUserBuffer
            ///     MessageText:
            ///     An access to a user buffer failed at an "expected" point in time. This code is defined since the caller does not
            ///     want to accept StatusAccessViolation in its filter.
            /// </summary>
            StatusInvalidUserBuffer = 0xC00000E8,

            /// <summary>
            ///     MessageId: StatusUnexpectedIoError
            ///     MessageText:
            ///     If an I/O error is returned which is not defined in the standard FsRtl filter, it is converted to the following
            ///     error which is guaranteed to be in the filter. In this case information is lost, however, the filter correctly
            ///     handles the exception.
            /// </summary>
            StatusUnexpectedIoError = 0xC00000E9,

            /// <summary>
            ///     MessageId: StatusUnexpectedMmCreateErr
            ///     MessageText:
            ///     If an Mm error is returned which is not defined in the standard FsRtl filter, it is converted to one of the
            ///     following errors which is guaranteed to be in the filter. In this case information is lost, however, the filter
            ///     correctly handles the exception.
            /// </summary>
            StatusUnexpectedMmCreateErr = 0xC00000EA,

            /// <summary>
            ///     MessageId: StatusUnexpectedMmMapError
            ///     MessageText:
            ///     If an Mm error is returned which is not defined in the standard FsRtl filter, it is converted to one of the
            ///     following errors which is guaranteed to be in the filter. In this case information is lost, however, the filter
            ///     correctly handles the exception.
            /// </summary>
            StatusUnexpectedMmMapError = 0xC00000EB,

            /// <summary>
            ///     MessageId: StatusUnexpectedMmExtendErr
            ///     MessageText:
            ///     If an Mm error is returned which is not defined in the standard FsRtl filter, it is converted to one of the
            ///     following errors which is guaranteed to be in the filter. In this case information is lost, however, the filter
            ///     correctly handles the exception.
            /// </summary>
            StatusUnexpectedMmExtendErr = 0xC00000EC,

            /// <summary>
            ///     MessageId: StatusNotLogonProcess
            ///     MessageText:
            ///     The requested action is restricted for use by logon processes only. The calling process has not registered as a
            ///     logon process.
            /// </summary>
            StatusNotLogonProcess = 0xC00000ED,

            /// <summary>
            ///     MessageId: StatusLogonSessionExists
            ///     MessageText:
            ///     An attempt has been made to start a new session manager or Lsa logon session with an Id that is already in use.
            /// </summary>
            StatusLogonSessionExists = 0xC00000EE,

            /// <summary>
            ///     MessageId: StatusInvalidParameter1
            ///     MessageText:
            ///     An invalid parameter was passed to a service or function as the first argument.
            /// </summary>
            StatusInvalidParameter1 = 0xC00000EF,

            /// <summary>
            ///     MessageId: StatusInvalidParameter2
            ///     MessageText:
            ///     An invalid parameter was passed to a service or function as the second argument.
            /// </summary>
            StatusInvalidParameter2 = 0xC00000F0,

            /// <summary>
            ///     MessageId: StatusInvalidParameter3
            ///     MessageText:
            ///     An invalid parameter was passed to a service or function as the third argument.
            /// </summary>
            StatusInvalidParameter3 = 0xC00000F1,

            /// <summary>
            ///     MessageId: StatusInvalidParameter4
            ///     MessageText:
            ///     An invalid parameter was passed to a service or function as the fourth argument.
            /// </summary>
            StatusInvalidParameter4 = 0xC00000F2,

            /// <summary>
            ///     MessageId: StatusInvalidParameter5
            ///     MessageText:
            ///     An invalid parameter was passed to a service or function as the fifth argument.
            /// </summary>
            StatusInvalidParameter5 = 0xC00000F3,

            /// <summary>
            ///     MessageId: StatusInvalidParameter6
            ///     MessageText:
            ///     An invalid parameter was passed to a service or function as the sixth argument.
            /// </summary>
            StatusInvalidParameter6 = 0xC00000F4,

            /// <summary>
            ///     MessageId: StatusInvalidParameter7
            ///     MessageText:
            ///     An invalid parameter was passed to a service or function as the seventh argument.
            /// </summary>
            StatusInvalidParameter7 = 0xC00000F5,

            /// <summary>
            ///     MessageId: StatusInvalidParameter8
            ///     MessageText:
            ///     An invalid parameter was passed to a service or function as the eighth argument.
            /// </summary>
            StatusInvalidParameter8 = 0xC00000F6,

            /// <summary>
            ///     MessageId: StatusInvalidParameter9
            ///     MessageText:
            ///     An invalid parameter was passed to a service or function as the ninth argument.
            /// </summary>
            StatusInvalidParameter9 = 0xC00000F7,

            /// <summary>
            ///     MessageId: StatusInvalidParameter10
            ///     MessageText:
            ///     An invalid parameter was passed to a service or function as the tenth argument.
            /// </summary>
            StatusInvalidParameter10 = 0xC00000F8,

            /// <summary>
            ///     MessageId: StatusInvalidParameter11
            ///     MessageText:
            ///     An invalid parameter was passed to a service or function as the eleventh argument.
            /// </summary>
            StatusInvalidParameter11 = 0xC00000F9,

            /// <summary>
            ///     MessageId: StatusInvalidParameter12
            ///     MessageText:
            ///     An invalid parameter was passed to a service or function as the twelfth argument.
            /// </summary>
            StatusInvalidParameter12 = 0xC00000FA,

            /// <summary>
            ///     MessageId: StatusRedirectorNotStarted
            ///     MessageText:
            ///     An attempt was made to access a network file, but the network software was not yet started.
            /// </summary>
            StatusRedirectorNotStarted = 0xC00000FB,

            /// <summary>
            ///     MessageId: StatusRedirectorStarted
            ///     MessageText:
            ///     An attempt was made to start the redirector, but the redirector has already been started.
            /// </summary>
            StatusRedirectorStarted = 0xC00000FC,

            /// <summary>
            ///     MessageId: StatusStackOverflow
            ///     MessageText:
            ///     A new guard page for the stack cannot be created.
            /// </summary>
            StatusStackOverflow = 0xC00000FD, // winnt

            /// <summary>
            ///     MessageId: StatusNoSuchPackage
            ///     MessageText:
            ///     A specified authentication package is unknown.
            /// </summary>
            StatusNoSuchPackage = 0xC00000FE,

            /// <summary>
            ///     MessageId: StatusBadFunctionTable
            ///     MessageText:
            ///     A malformed function table was encountered during an unwind operation.
            /// </summary>
            StatusBadFunctionTable = 0xC00000FF,

            /// <summary>
            ///     MessageId: StatusVariableNotFound
            ///     MessageText:
            ///     Indicates the specified environment variable name was not found in the specified environment block.
            /// </summary>
            StatusVariableNotFound = 0xC0000100,

            /// <summary>
            ///     MessageId: StatusDirectoryNotEmpty
            ///     MessageText:
            ///     Indicates that the directory trying to be deleted is not empty.
            /// </summary>
            StatusDirectoryNotEmpty = 0xC0000101,

            /// <summary>
            ///     MessageId: StatusFileCorruptError
            ///     MessageText:
            ///     {Corrupt File}
            ///     The file or directory %hs is corrupt and unreadable.
            ///     Please run the Chkdsk utility.
            /// </summary>
            StatusFileCorruptError = 0xC0000102,

            /// <summary>
            ///     MessageId: StatusNotADirectory
            ///     MessageText:
            ///     A requested opened file is not a directory.
            /// </summary>
            StatusNotADirectory = 0xC0000103,

            /// <summary>
            ///     MessageId: StatusBadLogonSessionState
            ///     MessageText:
            ///     The logon session is not in a state that is consistent with the requested operation.
            /// </summary>
            StatusBadLogonSessionState = 0xC0000104,

            /// <summary>
            ///     MessageId: StatusLogonSessionCollision
            ///     MessageText:
            ///     An internal Lsa error has occurred. An authentication package has requested the creation of a Logon Session but the
            ///     Id of an already existing Logon Session has been specified.
            /// </summary>
            StatusLogonSessionCollision = 0xC0000105,

            /// <summary>
            ///     MessageId: StatusNameTooLong
            ///     MessageText:
            ///     A specified name string is too long for its intended use.
            /// </summary>
            StatusNameTooLong = 0xC0000106,

            /// <summary>
            ///     MessageId: StatusFilesOpen
            ///     MessageText:
            ///     The user attempted to force close the files on a redirected drive, but there were opened files on the drive, and
            ///     the user did not specify a sufficient level of force.
            /// </summary>
            StatusFilesOpen = 0xC0000107,

            /// <summary>
            ///     MessageId: StatusConnectionInUse
            ///     MessageText:
            ///     The user attempted to force close the files on a redirected drive, but there were opened directories on the drive,
            ///     and the user did not specify a sufficient level of force.
            /// </summary>
            StatusConnectionInUse = 0xC0000108,

            /// <summary>
            ///     MessageId: StatusMessageNotFound
            ///     MessageText:
            ///     RtlFindMessage could not locate the requested message Id in the message table resource.
            /// </summary>
            StatusMessageNotFound = 0xC0000109,

            /// <summary>
            ///     MessageId: StatusProcessIsTerminating
            ///     MessageText:
            ///     An attempt was made to access an exiting process.
            /// </summary>
            StatusProcessIsTerminating = 0xC000010A,

            /// <summary>
            ///     MessageId: StatusInvalidLogonType
            ///     MessageText:
            ///     Indicates an invalid value has been provided for the LogonType requested.
            /// </summary>
            StatusInvalidLogonType = 0xC000010B,

            /// <summary>
            ///     MessageId: StatusNoGuidTranslation
            ///     MessageText:
            ///     Indicates that an attempt was made to assign protection to a file system file or directory and one of the SIDs in
            ///     the security descriptor could not be translated into a Guid that could be stored by the file system.
            ///     This causes the protection attempt to fai, which may cause a file creation attempt to fail.
            /// </summary>
            StatusNoGuidTranslation = 0xC000010C,

            /// <summary>
            ///     MessageId: StatusCannotImpersonate
            ///     MessageText:
            ///     Indicates that an attempt has been made to impersonate via a named pipe that has not yet been read from.
            /// </summary>
            StatusCannotImpersonate = 0xC000010D,

            /// <summary>
            ///     MessageId: StatusImageAlreadyLoaded
            ///     MessageText:
            ///     Indicates that the specified image is already loaded.
            /// </summary>
            StatusImageAlreadyLoaded = 0xC000010E,

            // ============================================================
            // Note: The following Abios error code should be reserved on
            //       non Abios kernel. Eventually, I will remove the ifdef
            //       Abios.
            // ============================================================

            /// <summary>
            ///     MessageId: StatusAbiosNotPresent
            ///     MessageText:
            ///     StatusAbiosNotPresent
            /// </summary>
            StatusAbiosNotPresent = 0xC000010F,

            /// <summary>
            ///     MessageId: StatusAbiosLidNotExist
            ///     MessageText:
            ///     StatusAbiosLidNotExist
            /// </summary>
            StatusAbiosLidNotExist = 0xC0000110,

            /// <summary>
            ///     MessageId: StatusAbiosLidAlreadyOwned
            ///     MessageText:
            ///     StatusAbiosLidAlreadyOwned
            /// </summary>
            StatusAbiosLidAlreadyOwned = 0xC0000111,

            /// <summary>
            ///     MessageId: StatusAbiosNotLidOwner
            ///     MessageText:
            ///     StatusAbiosNotLidOwner
            /// </summary>
            StatusAbiosNotLidOwner = 0xC0000112,

            /// <summary>
            ///     MessageId: StatusAbiosInvalidCommand
            ///     MessageText:
            ///     StatusAbiosInvalidCommand
            /// </summary>
            StatusAbiosInvalidCommand = 0xC0000113,

            /// <summary>
            ///     MessageId: StatusAbiosInvalidLid
            ///     MessageText:
            ///     StatusAbiosInvalidLid
            /// </summary>
            StatusAbiosInvalidLid = 0xC0000114,

            /// <summary>
            ///     MessageId: StatusAbiosSelectorNotAvailable
            ///     MessageText:
            ///     StatusAbiosSelectorNotAvailable
            /// </summary>
            StatusAbiosSelectorNotAvailable = 0xC0000115,

            /// <summary>
            ///     MessageId: StatusAbiosInvalidSelector
            ///     MessageText:
            ///     StatusAbiosInvalidSelector
            /// </summary>
            StatusAbiosInvalidSelector = 0xC0000116,

            /// <summary>
            ///     MessageId: StatusNoLdt
            ///     MessageText:
            ///     Indicates that an attempt was made to change the size of the Ldt for a process that has no Ldt.
            /// </summary>
            StatusNoLdt = 0xC0000117,

            /// <summary>
            ///     MessageId: StatusInvalidLdtSize
            ///     MessageText:
            ///     Indicates that an attempt was made to grow an Ldt by setting its size, or that the size was not an even number of
            ///     selectors.
            /// </summary>
            StatusInvalidLdtSize = 0xC0000118,

            /// <summary>
            ///     MessageId: StatusInvalidLdtOffset
            ///     MessageText:
            ///     Indicates that the starting value for the Ldt information was not an integral multiple of the selector size.
            /// </summary>
            StatusInvalidLdtOffset = 0xC0000119,

            /// <summary>
            ///     MessageId: StatusInvalidLdtDescriptor
            ///     MessageText:
            ///     Indicates that the user supplied an invalid descriptor when trying to set up Ldt descriptors.
            /// </summary>
            StatusInvalidLdtDescriptor = 0xC000011A,

            /// <summary>
            ///     MessageId: StatusInvalidImageNeFormat
            ///     MessageText:
            ///     The specified image file did not have the correct format. It appears to be Ne format.
            /// </summary>
            StatusInvalidImageNeFormat = 0xC000011B,

            /// <summary>
            ///     MessageId: StatusRxactInvalidState
            ///     MessageText:
            ///     Indicates that the transaction state of a registry sub-tree is incompatible with the requested operation. For
            ///     example, a request has been made to start a new transaction with one already in progress, or a request has been
            ///     made to apply a transaction when one is not currently in progress.
            /// </summary>
            StatusRxactInvalidState = 0xC000011C,

            /// <summary>
            ///     MessageId: StatusRxactCommitFailure
            ///     MessageText:
            ///     Indicates an error has occurred during a registry transaction commit. The database has been left in an unknown, but
            ///     probably inconsistent, state. The state of the registry transaction is left as Committing.
            /// </summary>
            StatusRxactCommitFailure = 0xC000011D,

            /// <summary>
            ///     MessageId: StatusMappedFileSizeZero
            ///     MessageText:
            ///     An attempt was made to map a file of size zero with the maximum size specified as zero.
            /// </summary>
            StatusMappedFileSizeZero = 0xC000011E,

            /// <summary>
            ///     MessageId: StatusTooManyOpenedFiles
            ///     MessageText:
            ///     Too many files are opened on a remote server.
            ///     This error should only be returned by the Windows redirector on a remote drive.
            /// </summary>
            StatusTooManyOpenedFiles = 0xC000011F,

            /// <summary>
            ///     MessageId: StatusCancelled
            ///     MessageText:
            ///     The I/O request was canceled.
            /// </summary>
            StatusCancelled = 0xC0000120,

            /// <summary>
            ///     MessageId: StatusCannotDelete
            ///     MessageText:
            ///     An attempt has been made to remove a file or directory that cannot be deleted.
            /// </summary>
            StatusCannotDelete = 0xC0000121,

            /// <summary>
            ///     MessageId: StatusInvalidComputerName
            ///     MessageText:
            ///     Indicates a name specified as a remote computer name is syntactically invalid.
            /// </summary>
            StatusInvalidComputerName = 0xC0000122,

            /// <summary>
            ///     MessageId: StatusFileDeleted
            ///     MessageText:
            ///     An I/O request other than close was performed on a file after it has been deleted, which can only happen to a
            ///     request which did not complete before the last handle was closed via NtClose.
            /// </summary>
            StatusFileDeleted = 0xC0000123,

            /// <summary>
            ///     MessageId: StatusSpecialAccount
            ///     MessageText:
            ///     Indicates an operation has been attempted on a built-in (specia, Sam account which is incompatible with built-in
            ///     accounts. For example, built-in accounts cannot be deleted.
            /// </summary>
            StatusSpecialAccount = 0xC0000124,

            /// <summary>
            ///     MessageId: StatusSpecialGroup
            ///     MessageText:
            ///     The operation requested may not be performed on the specified group because it is a built-in special group.
            /// </summary>
            StatusSpecialGroup = 0xC0000125,

            /// <summary>
            ///     MessageId: StatusSpecialUser
            ///     MessageText:
            ///     The operation requested may not be performed on the specified user because it is a built-in special user.
            /// </summary>
            StatusSpecialUser = 0xC0000126,

            /// <summary>
            ///     MessageId: StatusMembersPrimaryGroup
            ///     MessageText:
            ///     Indicates a member cannot be removed from a group because the group is currently the member's primary group.
            /// </summary>
            StatusMembersPrimaryGroup = 0xC0000127,

            /// <summary>
            ///     MessageId: StatusFileClosed
            ///     MessageText:
            ///     An I/O request other than close and several other special case operations was attempted using a file object that
            ///     had already been closed.
            /// </summary>
            StatusFileClosed = 0xC0000128,

            /// <summary>
            ///     MessageId: StatusTooManyThreads
            ///     MessageText:
            ///     Indicates a process has too many threads to perform the requested action. For example, assignment of a primary
            ///     token may only be performed when a process has zero or one threads.
            /// </summary>
            StatusTooManyThreads = 0xC0000129,

            /// <summary>
            ///     MessageId: StatusThreadNotInProcess
            ///     MessageText:
            ///     An attempt was made to operate on a thread within a specific process, but the thread specified is not in the
            ///     process specified.
            /// </summary>
            StatusThreadNotInProcess = 0xC000012A,

            /// <summary>
            ///     MessageId: StatusTokenAlreadyInUse
            ///     MessageText:
            ///     An attempt was made to establish a token for use as a primary token but the token is already in use. A token can
            ///     only be the primary token of one process at a time.
            /// </summary>
            StatusTokenAlreadyInUse = 0xC000012B,

            /// <summary>
            ///     MessageId: StatusPagefileQuotaExceeded
            ///     MessageText:
            ///     Page file quota was exceeded.
            /// </summary>
            StatusPagefileQuotaExceeded = 0xC000012C,

            /// <summary>
            ///     MessageId: StatusCommitmentLimit
            ///     MessageText:
            ///     {Out of Virtual Memory}
            ///     Your system is low on virtual memory. To ensure that Windows runs properly, increase the size of your virtual
            ///     memory paging file. For more information, see Help.
            /// </summary>
            StatusCommitmentLimit = 0xC000012D,

            /// <summary>
            ///     MessageId: StatusInvalidImageLeFormat
            ///     MessageText:
            ///     The specified image file did not have the correct format, it appears to be Le format.
            /// </summary>
            StatusInvalidImageLeFormat = 0xC000012E,

            /// <summary>
            ///     MessageId: StatusInvalidImageNotMz
            ///     MessageText:
            ///     The specified image file did not have the correct format, it did not have an initial Mz.
            /// </summary>
            StatusInvalidImageNotMz = 0xC000012F,

            /// <summary>
            ///     MessageId: StatusInvalidImageProtect
            ///     MessageText:
            ///     The specified image file did not have the correct format, it did not have a proper e_lfarlc in the Mz header.
            /// </summary>
            StatusInvalidImageProtect = 0xC0000130,

            /// <summary>
            ///     MessageId: StatusInvalidImageWin16
            ///     MessageText:
            ///     The specified image file did not have the correct format, it appears to be a 16-bit Windows image.
            /// </summary>
            StatusInvalidImageWin16 = 0xC0000131,

            /// <summary>
            ///     MessageId: StatusLogonServerConflict
            ///     MessageText:
            ///     The Netlogon service cannot start because another Netlogon service running in the domain conflicts with the
            ///     specified role.
            /// </summary>
            StatusLogonServerConflict = 0xC0000132,

            /// <summary>
            ///     MessageId: StatusTimeDifferenceAtDc
            ///     MessageText:
            ///     The time at the Primary Domain Controller is different than the time at the Backup Domain Controller or member
            ///     server by too large an amount.
            /// </summary>
            StatusTimeDifferenceAtDc = 0xC0000133,

            /// <summary>
            ///     MessageId: StatusSynchronizationRequired
            ///     MessageText:
            ///     The Sam database on a Windows Server is significantly out of synchronization with the copy on the Domain
            ///     Controller. A complete synchronization is required.
            /// </summary>
            StatusSynchronizationRequired = 0xC0000134,

            /// <summary>
            ///     MessageId: StatusDllNotFound
            ///     MessageText:
            ///     The program can't start because %hs is missing from your computer. Try reinstalling the program to fix this
            ///     problem.
            /// </summary>
            StatusDllNotFound = 0xC0000135, // winnt

            /// <summary>
            ///     MessageId: StatusOpenFailed
            ///     MessageText:
            ///     The NtCreateFile Api failed. This error should never be returned to an application, it is a place holder for the
            ///     Windows Lan Manager Redirector to use in its internal error mapping routines.
            /// </summary>
            StatusOpenFailed = 0xC0000136,

            /// <summary>
            ///     MessageId: StatusIoPrivilegeFailed
            ///     MessageText:
            ///     {Privilege Failed}
            ///     The I/O permissions for the process could not be changed.
            /// </summary>
            StatusIoPrivilegeFailed = 0xC0000137,

            /// <summary>
            ///     MessageId: StatusOrdinalNotFound
            ///     MessageText:
            ///     {Ordinal Not Found}
            ///     The ordinal %ld could not be located in the dynamic link library %hs.
            /// </summary>
            StatusOrdinalNotFound = 0xC0000138, // winnt

            /// <summary>
            ///     MessageId: StatusEntrypointNotFound
            ///     MessageText:
            ///     {Entry Point Not Found}
            ///     The procedure entry point %hs could not be located in the dynamic link library %hs.
            /// </summary>
            StatusEntrypointNotFound = 0xC0000139, // winnt

            /// <summary>
            ///     MessageId: StatusControlCExit
            ///     MessageText:
            ///     {Application Exit by Ctrl+C}
            ///     The application terminated as a result of a Ctrl+C.
            /// </summary>
            StatusControlCExit = 0xC000013A, // winnt

            /// <summary>
            ///     MessageId: StatusLocalDisconnect
            ///     MessageText:
            ///     {Virtual Circuit Closed}
            ///     The network transport on your computer has closed a network connection. There may or may not be I/O requests
            ///     outstanding.
            /// </summary>
            StatusLocalDisconnect = 0xC000013B,

            /// <summary>
            ///     MessageId: StatusRemoteDisconnect
            ///     MessageText:
            ///     {Virtual Circuit Closed}
            ///     The network transport on a remote computer has closed a network connection. There may or may not be I/O requests
            ///     outstanding.
            /// </summary>
            StatusRemoteDisconnect = 0xC000013C,

            /// <summary>
            ///     MessageId: StatusRemoteResources
            ///     MessageText:
            ///     {Insufficient Resources on Remote Computer}
            ///     The remote computer has insufficient resources to complete the network request. For instance, there may not be
            ///     enough memory available on the remote computer to carry out the request at this time.
            /// </summary>
            StatusRemoteResources = 0xC000013D,

            /// <summary>
            ///     MessageId: StatusLinkFailed
            ///     MessageText:
            ///     {Virtual Circuit Closed}
            ///     An existing connection (virtual circuit, has been broken at the remote computer. There is probably something wrong
            ///     with the network software protocol or the network hardware on the remote computer.
            /// </summary>
            StatusLinkFailed = 0xC000013E,

            /// <summary>
            ///     MessageId: StatusLinkTimeout
            ///     MessageText:
            ///     {Virtual Circuit Closed}
            ///     The network transport on your computer has closed a network connection because it had to wait too long for a
            ///     response from the remote computer.
            /// </summary>
            StatusLinkTimeout = 0xC000013F,

            /// <summary>
            ///     MessageId: StatusInvalidConnection
            ///     MessageText:
            ///     The connection handle given to the transport was invalid.
            /// </summary>
            StatusInvalidConnection = 0xC0000140,

            /// <summary>
            ///     MessageId: StatusInvalidAddress
            ///     MessageText:
            ///     The address handle given to the transport was invalid.
            /// </summary>
            StatusInvalidAddress = 0xC0000141,

            /// <summary>
            ///     MessageId: StatusDllInitFailed
            ///     MessageText:
            ///     {Dll Initialization Failed}
            ///     Initialization of the dynamic link library %hs failed. The process is terminating abnormally.
            /// </summary>
            StatusDllInitFailed = 0xC0000142, // winnt

            /// <summary>
            ///     MessageId: StatusMissingSystemfile
            ///     MessageText:
            ///     {Missing System File}
            ///     The required system file %hs is bad or missing.
            /// </summary>
            StatusMissingSystemfile = 0xC0000143,

            /// <summary>
            ///     MessageId: StatusUnhandledException
            ///     MessageText:
            ///     {Application Error}
            ///     The exception %s (= 0x%08lx, occurred in the application at location = 0x%08lx.
            /// </summary>
            StatusUnhandledException = 0xC0000144,

            /// <summary>
            ///     MessageId: StatusAppInitFailure
            ///     MessageText:
            ///     {Application Error}
            ///     The application was unable to start correctly (= 0x%lx,. Click Ok to close the application.
            /// </summary>
            StatusAppInitFailure = 0xC0000145,

            /// <summary>
            ///     MessageId: StatusPagefileCreateFailed
            ///     MessageText:
            ///     {Unable to Create Paging File}
            ///     The creation of the paging file %hs failed (%lx,. The requested size was %ld.
            /// </summary>
            StatusPagefileCreateFailed = 0xC0000146,

            /// <summary>
            ///     MessageId: StatusNoPagefile
            ///     MessageText:
            ///     {No Paging File Specified}
            ///     No paging file was specified in the system configuration.
            /// </summary>
            StatusNoPagefile = 0xC0000147,

            /// <summary>
            ///     MessageId: StatusInvalidLevel
            ///     MessageText:
            ///     {Incorrect System Call Level}
            ///     An invalid level was passed into the specified system call.
            /// </summary>
            StatusInvalidLevel = 0xC0000148,

            /// <summary>
            ///     MessageId: StatusWrongPasswordCore
            ///     MessageText:
            ///     {Incorrect Password to Lan Manager Server}
            ///     You specified an incorrect password to a Lan Manager 2.x or Ms-Net server.
            /// </summary>
            StatusWrongPasswordCore = 0xC0000149,

            /// <summary>
            ///     MessageId: StatusIllegalFloatContext
            ///     MessageText:
            ///     {Exception}
            ///     A real-mode application issued a floating-point instruction and floating-point hardware is not present.
            /// </summary>
            StatusIllegalFloatContext = 0xC000014A,

            /// <summary>
            ///     MessageId: StatusPipeBroken
            ///     MessageText:
            ///     The pipe operation has failed because the other end of the pipe has been closed.
            /// </summary>
            StatusPipeBroken = 0xC000014B,

            /// <summary>
            ///     MessageId: StatusRegistryCorrupt
            ///     MessageText:
            ///     {The Registry Is Corrupt}
            ///     The structure of one of the files that contains Registry data is corrupt, or the image of the file in memory is
            ///     corrupt, or the file could not be recovered because the alternate copy or log was absent or corrupt.
            /// </summary>
            StatusRegistryCorrupt = 0xC000014C,

            /// <summary>
            ///     MessageId: StatusRegistryIoFailed
            ///     MessageText:
            ///     An I/O operation initiated by the Registry failed unrecoverably. The Registry could not read in, or write out, or
            ///     flush, one of the files that contain the system's image of the Registry.
            /// </summary>
            StatusRegistryIoFailed = 0xC000014D,

            /// <summary>
            ///     MessageId: StatusNoEventPair
            ///     MessageText:
            ///     An event pair synchronization operation was performed using the thread specific client/server event pair object,
            ///     but no event pair object was associated with the thread.
            /// </summary>
            StatusNoEventPair = 0xC000014E,

            /// <summary>
            ///     MessageId: StatusUnrecognizedVolume
            ///     MessageText:
            ///     The volume does not contain a recognized file system. Please make sure that all required file system drivers are
            ///     loaded and that the volume is not corrupt.
            /// </summary>
            StatusUnrecognizedVolume = 0xC000014F,

            /// <summary>
            ///     MessageId: StatusSerialNoDeviceInited
            ///     MessageText:
            ///     No serial device was successfully initialized. The serial driver will unload.
            /// </summary>
            StatusSerialNoDeviceInited = 0xC0000150,

            /// <summary>
            ///     MessageId: StatusNoSuchAlias
            ///     MessageText:
            ///     The specified local group does not exist.
            /// </summary>
            StatusNoSuchAlias = 0xC0000151,

            /// <summary>
            ///     MessageId: StatusMemberNotInAlias
            ///     MessageText:
            ///     The specified account name is not a member of the group.
            /// </summary>
            StatusMemberNotInAlias = 0xC0000152,

            /// <summary>
            ///     MessageId: StatusMemberInAlias
            ///     MessageText:
            ///     The specified account name is already a member of the group.
            /// </summary>
            StatusMemberInAlias = 0xC0000153,

            /// <summary>
            ///     MessageId: StatusAliasExists
            ///     MessageText:
            ///     The specified local group already exists.
            /// </summary>
            StatusAliasExists = 0xC0000154,

            /// <summary>
            ///     MessageId: StatusLogonNotGranted
            ///     MessageText:
            ///     A requested type of logon (e.g., Interactive, Network, Service, is not granted by the target system's local
            ///     security policy.
            ///     Please ask the system administrator to grant the necessary form of logon.
            /// </summary>
            StatusLogonNotGranted = 0xC0000155,

            /// <summary>
            ///     MessageId: StatusTooManySecrets
            ///     MessageText:
            ///     The maximum number of secrets that may be stored in a single system has been exceeded. The length and number of
            ///     secrets is limited to satisfy United States State Department export restrictions.
            /// </summary>
            StatusTooManySecrets = 0xC0000156,

            /// <summary>
            ///     MessageId: StatusSecretTooLong
            ///     MessageText:
            ///     The length of a secret exceeds the maximum length allowed. The length and number of secrets is limited to satisfy
            ///     United States State Department export restrictions.
            /// </summary>
            StatusSecretTooLong = 0xC0000157,

            /// <summary>
            ///     MessageId: StatusInternalDbError
            ///     MessageText:
            ///     The Local Security Authority (Lsa, database contains an internal inconsistency.
            /// </summary>
            StatusInternalDbError = 0xC0000158,

            /// <summary>
            ///     MessageId: StatusFullscreenMode
            ///     MessageText:
            ///     The requested operation cannot be performed in fullscreen mode.
            /// </summary>
            StatusFullscreenMode = 0xC0000159,

            /// <summary>
            ///     MessageId: StatusTooManyContextIds
            ///     MessageText:
            ///     During a logon attempt, the user's security context accumulated too many security IDs. This is a very unusual
            ///     situation. Remove the user from some global or local groups to reduce the number of security ids to incorporate
            ///     into the security context.
            /// </summary>
            StatusTooManyContextIds = 0xC000015A,

            /// <summary>
            ///     MessageId: StatusLogonTypeNotGranted
            ///     MessageText:
            ///     A user has requested a type of logon (e.g., interactive or network, that has not been granted. An administrator has
            ///     control over who may logon interactively and through the network.
            /// </summary>
            StatusLogonTypeNotGranted = 0xC000015B,

            /// <summary>
            ///     MessageId: StatusNotRegistryFile
            ///     MessageText:
            ///     The system has attempted to load or restore a file into the registry, and the specified file is not in the format
            ///     of a registry file.
            /// </summary>
            StatusNotRegistryFile = 0xC000015C,

            /// <summary>
            ///     MessageId: StatusNtCrossEncryptionRequired
            ///     MessageText:
            ///     An attempt was made to change a user password in the security account manager without providing the necessary
            ///     Windows cross-encrypted password.
            /// </summary>
            StatusNtCrossEncryptionRequired = 0xC000015D,

            /// <summary>
            ///     MessageId: StatusDomainCtrlrConfigError
            ///     MessageText:
            ///     A Windows Server has an incorrect configuration.
            /// </summary>
            StatusDomainCtrlrConfigError = 0xC000015E,

            /// <summary>
            ///     MessageId: StatusFtMissingMember
            ///     MessageText:
            ///     An attempt was made to explicitly access the secondary copy of information via a device control to the Fault
            ///     Tolerance driver and the secondary copy is not present in the system.
            /// </summary>
            StatusFtMissingMember = 0xC000015F,

            /// <summary>
            ///     MessageId: StatusIllFormedServiceEntry
            ///     MessageText:
            ///     A configuration registry node representing a driver service entry was ill-formed and did not contain required value
            ///     entries.
            /// </summary>
            StatusIllFormedServiceEntry = 0xC0000160,

            /// <summary>
            ///     MessageId: StatusIllegalCharacter
            ///     MessageText:
            ///     An illegal character was encountered. For a multi-byte character set this includes a lead byte without a succeeding
            ///     trail byte. For the Unicode character set this includes the characters = 0xFFFF and = 0xFFFE.
            /// </summary>
            StatusIllegalCharacter = 0xC0000161,

            /// <summary>
            ///     MessageId: StatusUnmappableCharacter
            ///     MessageText:
            ///     No mapping for the Unicode character exists in the target multi-byte code page.
            /// </summary>
            StatusUnmappableCharacter = 0xC0000162,

            /// <summary>
            ///     MessageId: StatusUndefinedCharacter
            ///     MessageText:
            ///     The Unicode character is not defined in the Unicode character set installed on the system.
            /// </summary>
            StatusUndefinedCharacter = 0xC0000163,

            /// <summary>
            ///     MessageId: StatusFloppyVolume
            ///     MessageText:
            ///     The paging file cannot be created on a floppy diskette.
            /// </summary>
            StatusFloppyVolume = 0xC0000164,

            /// <summary>
            ///     MessageId: StatusFloppyIdMarkNotFound
            ///     MessageText:
            ///     {Floppy Disk Error}
            ///     While accessing a floppy disk, an Id address mark was not found.
            /// </summary>
            StatusFloppyIdMarkNotFound = 0xC0000165,

            /// <summary>
            ///     MessageId: StatusFloppyWrongCylinder
            ///     MessageText:
            ///     {Floppy Disk Error}
            ///     While accessing a floppy disk, the track address from the sector Id field was found to be different than the track
            ///     address maintained by the controller.
            /// </summary>
            StatusFloppyWrongCylinder = 0xC0000166,

            /// <summary>
            ///     MessageId: StatusFloppyUnknownError
            ///     MessageText:
            ///     {Floppy Disk Error}
            ///     The floppy disk controller reported an error that is not recognized by the floppy disk driver.
            /// </summary>
            StatusFloppyUnknownError = 0xC0000167,

            /// <summary>
            ///     MessageId: StatusFloppyBadRegisters
            ///     MessageText:
            ///     {Floppy Disk Error}
            ///     While accessing a floppy-disk, the controller returned inconsistent results via its registers.
            /// </summary>
            StatusFloppyBadRegisters = 0xC0000168,

            /// <summary>
            ///     MessageId: StatusDiskRecalibrateFailed
            ///     MessageText:
            ///     {Hard Disk Error}
            ///     While accessing the hard disk, a recalibrate operation failed, even after retries.
            /// </summary>
            StatusDiskRecalibrateFailed = 0xC0000169,

            /// <summary>
            ///     MessageId: StatusDiskOperationFailed
            ///     MessageText:
            ///     {Hard Disk Error}
            ///     While accessing the hard disk, a disk operation failed even after retries.
            /// </summary>
            StatusDiskOperationFailed = 0xC000016A,

            /// <summary>
            ///     MessageId: StatusDiskResetFailed
            ///     MessageText:
            ///     {Hard Disk Error}
            ///     While accessing the hard disk, a disk controller reset was needed, but even that failed.
            /// </summary>
            StatusDiskResetFailed = 0xC000016B,

            /// <summary>
            ///     MessageId: StatusSharedIrqBusy
            ///     MessageText:
            ///     An attempt was made to open a device that was sharing an Irq with other devices.
            ///     At least one other device that uses that Irq was already opened.
            ///     Two concurrent opens of devices that share an Irq and only work via interrupts is not supported for the particular
            ///     bus type that the devices use.
            /// </summary>
            StatusSharedIrqBusy = 0xC000016C,

            /// <summary>
            ///     MessageId: StatusFtOrphaning
            ///     MessageText:
            ///     {Ft Orphaning}
            ///     A disk that is part of a fault-tolerant volume can no longer be accessed.
            /// </summary>
            StatusFtOrphaning = 0xC000016D,

            /// <summary>
            ///     MessageId: StatusBiosFailedToConnectInterrupt
            ///     MessageText:
            ///     The system bios failed to connect a system interrupt to the device or bus for which the device is connected.
            /// </summary>
            StatusBiosFailedToConnectInterrupt = 0xC000016E,

            /// <summary>
            ///     MessageId: StatusPartitionFailure
            ///     MessageText:
            ///     Tape could not be partitioned.
            /// </summary>
            StatusPartitionFailure = 0xC0000172,

            /// <summary>
            ///     MessageId: StatusInvalidBlockLength
            ///     MessageText:
            ///     When accessing a new tape of a multivolume partition, the current blocksize is incorrect.
            /// </summary>
            StatusInvalidBlockLength = 0xC0000173,

            /// <summary>
            ///     MessageId: StatusDeviceNotPartitioned
            ///     MessageText:
            ///     Tape partition information could not be found when loading a tape.
            /// </summary>
            StatusDeviceNotPartitioned = 0xC0000174,

            /// <summary>
            ///     MessageId: StatusUnableToLockMedia
            ///     MessageText:
            ///     Attempt to lock the eject media mechanism fails.
            /// </summary>
            StatusUnableToLockMedia = 0xC0000175,

            /// <summary>
            ///     MessageId: StatusUnableToUnloadMedia
            ///     MessageText:
            ///     Unload media fails.
            /// </summary>
            StatusUnableToUnloadMedia = 0xC0000176,

            /// <summary>
            ///     MessageId: StatusEomOverflow
            ///     MessageText:
            ///     Physical end of tape was detected.
            /// </summary>
            StatusEomOverflow = 0xC0000177,

            /// <summary>
            ///     MessageId: StatusNoMedia
            ///     MessageText:
            ///     {No Media}
            ///     There is no media in the drive. Please insert media into drive %hs.
            /// </summary>
            StatusNoMedia = 0xC0000178,

            /// <summary>
            ///     MessageId: StatusNoSuchMember
            ///     MessageText:
            ///     A member could not be added to or removed from the local group because the member does not exist.
            /// </summary>
            StatusNoSuchMember = 0xC000017A,

            /// <summary>
            ///     MessageId: StatusInvalidMember
            ///     MessageText:
            ///     A new member could not be added to a local group because the member has the wrong account type.
            /// </summary>
            StatusInvalidMember = 0xC000017B,

            /// <summary>
            ///     MessageId: StatusKeyDeleted
            ///     MessageText:
            ///     Illegal operation attempted on a registry key which has been marked for deletion.
            /// </summary>
            StatusKeyDeleted = 0xC000017C,

            /// <summary>
            ///     MessageId: StatusNoLogSpace
            ///     MessageText:
            ///     System could not allocate required space in a registry log.
            /// </summary>
            StatusNoLogSpace = 0xC000017D,

            /// <summary>
            ///     MessageId: StatusTooManySids
            ///     MessageText:
            ///     Too many Sids have been specified.
            /// </summary>
            StatusTooManySids = 0xC000017E,

            /// <summary>
            ///     MessageId: StatusLmCrossEncryptionRequired
            ///     MessageText:
            ///     An attempt was made to change a user password in the security account manager without providing the necessary Lm
            ///     cross-encrypted password.
            /// </summary>
            StatusLmCrossEncryptionRequired = 0xC000017F,

            /// <summary>
            ///     MessageId: StatusKeyHasChildren
            ///     MessageText:
            ///     An attempt was made to create a symbolic link in a registry key that already has subkeys or values.
            /// </summary>
            StatusKeyHasChildren = 0xC0000180,

            /// <summary>
            ///     MessageId: StatusChildMustBeVolatile
            ///     MessageText:
            ///     An attempt was made to create a Stable subkey under a Volatile parent key.
            /// </summary>
            StatusChildMustBeVolatile = 0xC0000181,

            /// <summary>
            ///     MessageId: StatusDeviceConfigurationError
            ///     MessageText:
            ///     The I/O device is configured incorrectly or the configuration parameters to the driver are incorrect.
            /// </summary>
            StatusDeviceConfigurationError = 0xC0000182,

            /// <summary>
            ///     MessageId: StatusDriverInternalError
            ///     MessageText:
            ///     An error was detected between two drivers or within an I/O driver.
            /// </summary>
            StatusDriverInternalError = 0xC0000183,

            /// <summary>
            ///     MessageId: StatusInvalidDeviceState
            ///     MessageText:
            ///     The device is not in a valid state to perform this request.
            /// </summary>
            StatusInvalidDeviceState = 0xC0000184,

            /// <summary>
            ///     MessageId: StatusIoDeviceError
            ///     MessageText:
            ///     The I/O device reported an I/O error.
            /// </summary>
            StatusIoDeviceError = 0xC0000185,

            /// <summary>
            ///     MessageId: StatusDeviceProtocolError
            ///     MessageText:
            ///     A protocol error was detected between the driver and the device.
            /// </summary>
            StatusDeviceProtocolError = 0xC0000186,

            /// <summary>
            ///     MessageId: StatusBackupController
            ///     MessageText:
            ///     This operation is only allowed for the Primary Domain Controller of the domain.
            /// </summary>
            StatusBackupController = 0xC0000187,

            /// <summary>
            ///     MessageId: StatusLogFileFull
            ///     MessageText:
            ///     Log file space is insufficient to support this operation.
            /// </summary>
            StatusLogFileFull = 0xC0000188,

            /// <summary>
            ///     MessageId: StatusTooLate
            ///     MessageText:
            ///     A write operation was attempted to a volume after it was dismounted.
            /// </summary>
            StatusTooLate = 0xC0000189,

            /// <summary>
            ///     MessageId: StatusNoTrustLsaSecret
            ///     MessageText:
            ///     The workstation does not have a trust secret for the primary domain in the local Lsa database.
            /// </summary>
            StatusNoTrustLsaSecret = 0xC000018A,

            /// <summary>
            ///     MessageId: StatusNoTrustSamAccount
            ///     MessageText:
            ///     The Sam database on the Windows Server does not have a computer account for this workstation trust relationship.
            /// </summary>
            StatusNoTrustSamAccount = 0xC000018B,

            /// <summary>
            ///     MessageId: StatusTrustedDomainFailure
            ///     MessageText:
            ///     The logon request failed because the trust relationship between the primary domain and the trusted domain failed.
            /// </summary>
            StatusTrustedDomainFailure = 0xC000018C,

            /// <summary>
            ///     MessageId: StatusTrustedRelationshipFailure
            ///     MessageText:
            ///     The logon request failed because the trust relationship between this workstation and the primary domain failed.
            /// </summary>
            StatusTrustedRelationshipFailure = 0xC000018D,

            /// <summary>
            ///     MessageId: StatusEventlogFileCorrupt
            ///     MessageText:
            ///     The Eventlog log file is corrupt.
            /// </summary>
            StatusEventlogFileCorrupt = 0xC000018E,

            /// <summary>
            ///     MessageId: StatusEventlogCantStart
            ///     MessageText:
            ///     No Eventlog log file could be opened. The Eventlog service did not start.
            /// </summary>
            StatusEventlogCantStart = 0xC000018F,

            /// <summary>
            ///     MessageId: StatusTrustFailure
            ///     MessageText:
            ///     The network logon failed. This may be because the validation authority can't be reached.
            /// </summary>
            StatusTrustFailure = 0xC0000190,

            /// <summary>
            ///     MessageId: StatusMutantLimitExceeded
            ///     MessageText:
            ///     An attempt was made to acquire a mutant such that its maximum count would have been exceeded.
            /// </summary>
            StatusMutantLimitExceeded = 0xC0000191,

            /// <summary>
            ///     MessageId: StatusNetlogonNotStarted
            ///     MessageText:
            ///     An attempt was made to logon, but the netlogon service was not started.
            /// </summary>
            StatusNetlogonNotStarted = 0xC0000192,

            /// <summary>
            ///     MessageId: StatusAccountExpired
            ///     MessageText:
            ///     The user's account has expired.
            /// </summary>
            StatusAccountExpired = 0xC0000193, // ntsubauth

            /// <summary>
            ///     MessageId: StatusPossibleDeadlock
            ///     MessageText:
            ///     {Exception}
            ///     Possible deadlock condition.
            /// </summary>
            StatusPossibleDeadlock = 0xC0000194,

            /// <summary>
            ///     MessageId: StatusNetworkCredentialConflict
            ///     MessageText:
            ///     Multiple connections to a server or shared resource by the same user, using more than one user name, are not
            ///     allowed. Disconnect all previous connections to the server or shared resource and try again.
            /// </summary>
            StatusNetworkCredentialConflict = 0xC0000195,

            /// <summary>
            ///     MessageId: StatusRemoteSessionLimit
            ///     MessageText:
            ///     An attempt was made to establish a session to a network server, but there are already too many sessions established
            ///     to that server.
            /// </summary>
            StatusRemoteSessionLimit = 0xC0000196,

            /// <summary>
            ///     MessageId: StatusEventlogFileChanged
            ///     MessageText:
            ///     The log file has changed between reads.
            /// </summary>
            StatusEventlogFileChanged = 0xC0000197,

            /// <summary>
            ///     MessageId: StatusNologonInterdomainTrustAccount
            ///     MessageText:
            ///     The account used is an Interdomain Trust account. Use your global user account or local user account to access this
            ///     server.
            /// </summary>
            StatusNologonInterdomainTrustAccount = 0xC0000198,

            /// <summary>
            ///     MessageId: StatusNologonWorkstationTrustAccount
            ///     MessageText:
            ///     The account used is a Computer Account. Use your global user account or local user account to access this server.
            /// </summary>
            StatusNologonWorkstationTrustAccount = 0xC0000199,

            /// <summary>
            ///     MessageId: StatusNologonServerTrustAccount
            ///     MessageText:
            ///     The account used is an Server Trust account. Use your global user account or local user account to access this
            ///     server.
            /// </summary>
            StatusNologonServerTrustAccount = 0xC000019A,

            /// <summary>
            ///     MessageId: StatusDomainTrustInconsistent
            ///     MessageText:
            ///     The name or Sid of the domain specified is inconsistent with the trust information for that domain.
            /// </summary>
            StatusDomainTrustInconsistent = 0xC000019B,

            /// <summary>
            ///     MessageId: StatusFsDriverRequired
            ///     MessageText:
            ///     A volume has been accessed for which a file system driver is required that has not yet been loaded.
            /// </summary>
            StatusFsDriverRequired = 0xC000019C,

            /// <summary>
            ///     MessageId: StatusImageAlreadyLoadedAsDll
            ///     MessageText:
            ///     Indicates that the specified image is already loaded as a Dll.
            /// </summary>
            StatusImageAlreadyLoadedAsDll = 0xC000019D,

            /// <summary>
            ///     MessageId: StatusIncompatibleWithGlobalShortNameRegistrySetting
            ///     MessageText:
            ///     Short name settings may not be changed on this volume due to the global registry setting.
            /// </summary>
            StatusIncompatibleWithGlobalShortNameRegistrySetting = 0xC000019E,

            /// <summary>
            ///     MessageId: StatusShortNamesNotEnabledOnVolume
            ///     MessageText:
            ///     Short names are not enabled on this volume.
            /// </summary>
            StatusShortNamesNotEnabledOnVolume = 0xC000019F,

            /// <summary>
            ///     MessageId: StatusSecurityStreamIsInconsistent
            ///     MessageText:
            ///     The security stream for the given volume is in an inconsistent state.
            ///     Please run Chkdsk on the volume.
            /// </summary>
            StatusSecurityStreamIsInconsistent = 0xC00001A0,

            /// <summary>
            ///     MessageId: StatusInvalidLockRange
            ///     MessageText:
            ///     A requested file lock operation cannot be processed due to an invalid byte range.
            /// </summary>
            StatusInvalidLockRange = 0xC00001A1,

            /// <summary>
            ///     MessageId: StatusInvalidAceCondition
            ///     MessageText:
            ///     {Invalid Ace Condition}
            ///     The specified access control entry (Ace, contains an invalid condition.
            /// </summary>
            StatusInvalidAceCondition = 0xC00001A2,

            /// <summary>
            ///     MessageId: StatusImageSubsystemNotPresent
            ///     MessageText:
            ///     The subsystem needed to support the image type is not present.
            /// </summary>
            StatusImageSubsystemNotPresent = 0xC00001A3,

            /// <summary>
            ///     MessageId: StatusNotificationGuidAlreadyDefined
            ///     MessageText:
            ///     {Invalid Ace Condition}
            ///     The specified file already has a notification Guid associated with it.
            /// </summary>
            StatusNotificationGuidAlreadyDefined = 0xC00001A4,

            // Available range of Ntstatus codes

            /// <summary>
            ///     MessageId: StatusNetworkOpenRestriction
            ///     MessageText:
            ///     A remote open failed because the network open restrictions were not satisfied.
            /// </summary>
            StatusNetworkOpenRestriction = 0xC0000201,

            /// <summary>
            ///     MessageId: StatusNoUserSessionKey
            ///     MessageText:
            ///     There is no user session key for the specified logon session.
            /// </summary>
            StatusNoUserSessionKey = 0xC0000202,

            /// <summary>
            ///     MessageId: StatusUserSessionDeleted
            ///     MessageText:
            ///     The remote user session has been deleted.
            /// </summary>
            StatusUserSessionDeleted = 0xC0000203,

            /// <summary>
            ///     MessageId: StatusResourceLangNotFound
            ///     MessageText:
            ///     Indicates the specified resource language Id cannot be found in the
            ///     image file.
            /// </summary>
            StatusResourceLangNotFound = 0xC0000204,

            /// <summary>
            ///     MessageId: StatusInsuffServerResources
            ///     MessageText:
            ///     Insufficient server resources exist to complete the request.
            /// </summary>
            StatusInsuffServerResources = 0xC0000205,

            /// <summary>
            ///     MessageId: StatusInvalidBufferSize
            ///     MessageText:
            ///     The size of the buffer is invalid for the specified operation.
            /// </summary>
            StatusInvalidBufferSize = 0xC0000206,

            /// <summary>
            ///     MessageId: StatusInvalidAddressComponent
            ///     MessageText:
            ///     The transport rejected the network address specified as invalid.
            /// </summary>
            StatusInvalidAddressComponent = 0xC0000207,

            /// <summary>
            ///     MessageId: StatusInvalidAddressWildcard
            ///     MessageText:
            ///     The transport rejected the network address specified due to an invalid use of a wildcard.
            /// </summary>
            StatusInvalidAddressWildcard = 0xC0000208,

            /// <summary>
            ///     MessageId: StatusTooManyAddresses
            ///     MessageText:
            ///     The transport address could not be opened because all the available addresses are in use.
            /// </summary>
            StatusTooManyAddresses = 0xC0000209,

            /// <summary>
            ///     MessageId: StatusAddressAlreadyExists
            ///     MessageText:
            ///     The transport address could not be opened because it already exists.
            /// </summary>
            StatusAddressAlreadyExists = 0xC000020A,

            /// <summary>
            ///     MessageId: StatusAddressClosed
            ///     MessageText:
            ///     The transport address is now closed.
            /// </summary>
            StatusAddressClosed = 0xC000020B,

            /// <summary>
            ///     MessageId: StatusConnectionDisconnected
            ///     MessageText:
            ///     The transport connection is now disconnected.
            /// </summary>
            StatusConnectionDisconnected = 0xC000020C,

            /// <summary>
            ///     MessageId: StatusConnectionReset
            ///     MessageText:
            ///     The transport connection has been reset.
            /// </summary>
            StatusConnectionReset = 0xC000020D,

            /// <summary>
            ///     MessageId: StatusTooManyNodes
            ///     MessageText:
            ///     The transport cannot dynamically acquire any more nodes.
            /// </summary>
            StatusTooManyNodes = 0xC000020E,

            /// <summary>
            ///     MessageId: StatusTransactionAborted
            ///     MessageText:
            ///     The transport aborted a pending transaction.
            /// </summary>
            StatusTransactionAborted = 0xC000020F,

            /// <summary>
            ///     MessageId: StatusTransactionTimedOut
            ///     MessageText:
            ///     The transport timed out a request waiting for a response.
            /// </summary>
            StatusTransactionTimedOut = 0xC0000210,

            /// <summary>
            ///     MessageId: StatusTransactionNoRelease
            ///     MessageText:
            ///     The transport did not receive a release for a pending response.
            /// </summary>
            StatusTransactionNoRelease = 0xC0000211,

            /// <summary>
            ///     MessageId: StatusTransactionNoMatch
            ///     MessageText:
            ///     The transport did not find a transaction matching the specific token.
            /// </summary>
            StatusTransactionNoMatch = 0xC0000212,

            /// <summary>
            ///     MessageId: StatusTransactionResponded
            ///     MessageText:
            ///     The transport had previously responded to a transaction request.
            /// </summary>
            StatusTransactionResponded = 0xC0000213,

            /// <summary>
            ///     MessageId: StatusTransactionInvalidId
            ///     MessageText:
            ///     The transport does not recognized the transaction request identifier specified.
            /// </summary>
            StatusTransactionInvalidId = 0xC0000214,

            /// <summary>
            ///     MessageId: StatusTransactionInvalidType
            ///     MessageText:
            ///     The transport does not recognize the transaction request type specified.
            /// </summary>
            StatusTransactionInvalidType = 0xC0000215,

            /// <summary>
            ///     MessageId: StatusNotServerSession
            ///     MessageText:
            ///     The transport can only process the specified request on the server side of a session.
            /// </summary>
            StatusNotServerSession = 0xC0000216,

            /// <summary>
            ///     MessageId: StatusNotClientSession
            ///     MessageText:
            ///     The transport can only process the specified request on the client side of a session.
            /// </summary>
            StatusNotClientSession = 0xC0000217,

            /// <summary>
            ///     MessageId: StatusCannotLoadRegistryFile
            ///     MessageText:
            ///     {Registry File Failure}
            ///     The registry cannot load the hive (file,:
            ///     %hs
            ///     or its log or alternate.
            ///     It is corrupt, absent, or not writable.
            /// </summary>
            StatusCannotLoadRegistryFile = 0xC0000218,

            /// <summary>
            ///     MessageId: StatusDebugAttachFailed
            ///     MessageText:
            ///     {Unexpected Failure in DebugActiveProcess}
            ///     An unexpected failure occurred while processing a DebugActiveProcess Api request. You may choose Ok to terminate
            ///     the process, or Cancel to ignore the error.
            /// </summary>
            StatusDebugAttachFailed = 0xC0000219,

            /// <summary>
            ///     MessageId: StatusSystemProcessTerminated
            ///     MessageText:
            ///     {Fatal System Error}
            ///     The %hs system process terminated unexpectedly with a status of = 0x%08x (= 0x%08x = 0x%08x,.
            ///     The system has been shut down.
            /// </summary>
            StatusSystemProcessTerminated = 0xC000021A,

            /// <summary>
            ///     MessageId: StatusDataNotAccepted
            ///     MessageText:
            ///     {Data Not Accepted}
            ///     The Tdi client could not handle the data received during an indication.
            /// </summary>
            StatusDataNotAccepted = 0xC000021B,

            /// <summary>
            ///     MessageId: StatusNoBrowserServersFound
            ///     MessageText:
            ///     {Unable to Retrieve Browser Server List}
            ///     The list of servers for this workgroup is not currently available.
            /// </summary>
            StatusNoBrowserServersFound = 0xC000021C,

            /// <summary>
            ///     MessageId: StatusVdmHardError
            ///     MessageText:
            ///     Ntvdm encountered a hard error.
            /// </summary>
            StatusVdmHardError = 0xC000021D,

            /// <summary>
            ///     MessageId: StatusDriverCancelTimeout
            ///     MessageText:
            ///     {Cancel Timeout}
            ///     The driver %hs failed to complete a cancelled I/O request in the allotted time.
            /// </summary>
            StatusDriverCancelTimeout = 0xC000021E,

            /// <summary>
            ///     MessageId: StatusReplyMessageMismatch
            ///     MessageText:
            ///     {Reply Message Mismatch}
            ///     An attempt was made to reply to an Lpc message, but the thread specified by the client Id in the message was not
            ///     waiting on that message.
            /// </summary>
            StatusReplyMessageMismatch = 0xC000021F,

            /// <summary>
            ///     MessageId: StatusMappedAlignment
            ///     MessageText:
            ///     {Mapped View Alignment Incorrect}
            ///     An attempt was made to map a view of a file, but either the specified base address or the offset into the file were
            ///     not aligned on the proper allocation granularity.
            /// </summary>
            StatusMappedAlignment = 0xC0000220,

            /// <summary>
            ///     MessageId: StatusImageChecksumMismatch
            ///     MessageText:
            ///     {Bad Image Checksum}
            ///     The image %hs is possibly corrupt. The header checksum does not match the computed checksum.
            /// </summary>
            StatusImageChecksumMismatch = 0xC0000221,

            /// <summary>
            ///     MessageId: StatusLostWritebehindData
            ///     MessageText:
            ///     {Delayed Write Failed}
            ///     Windows was unable to save all the data for the file %hs. The data has been lost. This error may be caused by a
            ///     failure of your computer hardware or network connection. Please try to save this file elsewhere.
            /// </summary>
            StatusLostWritebehindData = 0xC0000222,

            /// <summary>
            ///     MessageId: StatusClientServerParametersInvalid
            ///     MessageText:
            ///     The parameter(s, passed to the server in the client/server shared memory window were invalid. Too much data may
            ///     have been put in the shared memory window.
            /// </summary>
            StatusClientServerParametersInvalid = 0xC0000223,

            /// <summary>
            ///     MessageId: StatusPasswordMustChange
            ///     MessageText:
            ///     The user's password must be changed before logging on the first time.
            /// </summary>
            StatusPasswordMustChange = 0xC0000224, // ntsubauth

            /// <summary>
            ///     MessageId: StatusNotFound
            ///     MessageText:
            ///     The object was not found.
            /// </summary>
            StatusNotFound = 0xC0000225,

            /// <summary>
            ///     MessageId: StatusNotTinyStream
            ///     MessageText:
            ///     The stream is not a tiny stream.
            /// </summary>
            StatusNotTinyStream = 0xC0000226,

            /// <summary>
            ///     MessageId: StatusRecoveryFailure
            ///     MessageText:
            ///     A transaction recover failed.
            /// </summary>
            StatusRecoveryFailure = 0xC0000227,

            /// <summary>
            ///     MessageId: StatusStackOverflowRead
            ///     MessageText:
            ///     The request must be handled by the stack overflow code.
            /// </summary>
            StatusStackOverflowRead = 0xC0000228,

            /// <summary>
            ///     MessageId: StatusFailCheck
            ///     MessageText:
            ///     A consistency check failed.
            /// </summary>
            StatusFailCheck = 0xC0000229,

            /// <summary>
            ///     MessageId: StatusDuplicateObjectid
            ///     MessageText:
            ///     The attempt to insert the Id in the index failed because the Id is already in the index.
            /// </summary>
            StatusDuplicateObjectid = 0xC000022A,

            /// <summary>
            ///     MessageId: StatusObjectidExists
            ///     MessageText:
            ///     The attempt to set the object's Id failed because the object already has an Id.
            /// </summary>
            StatusObjectidExists = 0xC000022B,

            /// <summary>
            ///     MessageId: StatusConvertToLarge
            ///     MessageText:
            ///     Internal Ofs status codes indicating how an allocation operation is handled. Either it is retried after the
            ///     containing onode is moved or the extent stream is converted to a large stream.
            /// </summary>
            StatusConvertToLarge = 0xC000022C,

            /// <summary>
            ///     MessageId: StatusRetry
            ///     MessageText:
            ///     The request needs to be retried.
            /// </summary>
            StatusRetry = 0xC000022D,

            /// <summary>
            ///     MessageId: StatusFoundOutOfScope
            ///     MessageText:
            ///     The attempt to find the object found an object matching by Id on the volume but it is out of the scope of the
            ///     handle used for the operation.
            /// </summary>
            StatusFoundOutOfScope = 0xC000022E,

            /// <summary>
            ///     MessageId: StatusAllocateBucket
            ///     MessageText:
            ///     The bucket array must be grown. Retry transaction after doing so.
            /// </summary>
            StatusAllocateBucket = 0xC000022F,

            /// <summary>
            ///     MessageId: StatusPropsetNotFound
            ///     MessageText:
            ///     The property set specified does not exist on the object.
            /// </summary>
            StatusPropsetNotFound = 0xC0000230,

            /// <summary>
            ///     MessageId: StatusMarshallOverflow
            ///     MessageText:
            ///     The user/kernel marshalling buffer has overflowed.
            /// </summary>
            StatusMarshallOverflow = 0xC0000231,

            /// <summary>
            ///     MessageId: StatusInvalidVariant
            ///     MessageText:
            ///     The supplied variant structure contains invalid data.
            /// </summary>
            StatusInvalidVariant = 0xC0000232,

            /// <summary>
            ///     MessageId: StatusDomainControllerNotFound
            ///     MessageText:
            ///     Could not find a domain controller for this domain.
            /// </summary>
            StatusDomainControllerNotFound = 0xC0000233,

            /// <summary>
            ///     MessageId: StatusAccountLockedOut
            ///     MessageText:
            ///     The user account has been automatically locked because too many invalid logon attempts or password change attempts
            ///     have been requested.
            /// </summary>
            StatusAccountLockedOut = 0xC0000234, // ntsubauth

            /// <summary>
            ///     MessageId: StatusHandleNotClosable
            ///     MessageText:
            ///     NtClose was called on a handle that was protected from close via NtSetInformationObject.
            /// </summary>
            StatusHandleNotClosable = 0xC0000235,

            /// <summary>
            ///     MessageId: StatusConnectionRefused
            ///     MessageText:
            ///     The transport connection attempt was refused by the remote system.
            /// </summary>
            StatusConnectionRefused = 0xC0000236,

            /// <summary>
            ///     MessageId: StatusGracefulDisconnect
            ///     MessageText:
            ///     The transport connection was gracefully closed.
            /// </summary>
            StatusGracefulDisconnect = 0xC0000237,

            /// <summary>
            ///     MessageId: StatusAddressAlreadyAssociated
            ///     MessageText:
            ///     The transport endpoint already has an address associated with it.
            /// </summary>
            StatusAddressAlreadyAssociated = 0xC0000238,

            /// <summary>
            ///     MessageId: StatusAddressNotAssociated
            ///     MessageText:
            ///     An address has not yet been associated with the transport endpoint.
            /// </summary>
            StatusAddressNotAssociated = 0xC0000239,

            /// <summary>
            ///     MessageId: StatusConnectionInvalid
            ///     MessageText:
            ///     An operation was attempted on a nonexistent transport connection.
            /// </summary>
            StatusConnectionInvalid = 0xC000023A,

            /// <summary>
            ///     MessageId: StatusConnectionActive
            ///     MessageText:
            ///     An invalid operation was attempted on an active transport connection.
            /// </summary>
            StatusConnectionActive = 0xC000023B,

            /// <summary>
            ///     MessageId: StatusNetworkUnreachable
            ///     MessageText:
            ///     The remote network is not reachable by the transport.
            /// </summary>
            StatusNetworkUnreachable = 0xC000023C,

            /// <summary>
            ///     MessageId: StatusHostUnreachable
            ///     MessageText:
            ///     The remote system is not reachable by the transport.
            /// </summary>
            StatusHostUnreachable = 0xC000023D,

            /// <summary>
            ///     MessageId: StatusProtocolUnreachable
            ///     MessageText:
            ///     The remote system does not support the transport protocol.
            /// </summary>
            StatusProtocolUnreachable = 0xC000023E,

            /// <summary>
            ///     MessageId: StatusPortUnreachable
            ///     MessageText:
            ///     No service is operating at the destination port of the transport on the remote system.
            /// </summary>
            StatusPortUnreachable = 0xC000023F,

            /// <summary>
            ///     MessageId: StatusRequestAborted
            ///     MessageText:
            ///     The request was aborted.
            /// </summary>
            StatusRequestAborted = 0xC0000240,

            /// <summary>
            ///     MessageId: StatusConnectionAborted
            ///     MessageText:
            ///     The transport connection was aborted by the local system.
            /// </summary>
            StatusConnectionAborted = 0xC0000241,

            /// <summary>
            ///     MessageId: StatusBadCompressionBuffer
            ///     MessageText:
            ///     The specified buffer contains ill-formed data.
            /// </summary>
            StatusBadCompressionBuffer = 0xC0000242,

            /// <summary>
            ///     MessageId: StatusUserMappedFile
            ///     MessageText:
            ///     The requested operation cannot be performed on a file with a user mapped section open.
            /// </summary>
            StatusUserMappedFile = 0xC0000243,

            /// <summary>
            ///     MessageId: StatusAuditFailed
            ///     MessageText:
            ///     {Audit Failed}
            ///     An attempt to generate a security audit failed.
            /// </summary>
            StatusAuditFailed = 0xC0000244,

            /// <summary>
            ///     MessageId: StatusTimerResolutionNotSet
            ///     MessageText:
            ///     The timer resolution was not previously set by the current process.
            /// </summary>
            StatusTimerResolutionNotSet = 0xC0000245,

            /// <summary>
            ///     MessageId: StatusConnectionCountLimit
            ///     MessageText:
            ///     A connection to the server could not be made because the limit on the number of concurrent connections for this
            ///     account has been reached.
            /// </summary>
            StatusConnectionCountLimit = 0xC0000246,

            /// <summary>
            ///     MessageId: StatusLoginTimeRestriction
            ///     MessageText:
            ///     Attempting to login during an unauthorized time of day for this account.
            /// </summary>
            StatusLoginTimeRestriction = 0xC0000247,

            /// <summary>
            ///     MessageId: StatusLoginWkstaRestriction
            ///     MessageText:
            ///     The account is not authorized to login from this station.
            /// </summary>
            StatusLoginWkstaRestriction = 0xC0000248,

            /// <summary>
            ///     MessageId: StatusImageMpUpMismatch
            ///     MessageText:
            ///     {Up/Mp Image Mismatch}
            ///     The image %hs has been modified for use on a uniprocessor system, but you are running it on a multiprocessor
            ///     machine.
            ///     Please reinstall the image file.
            /// </summary>
            StatusImageMpUpMismatch = 0xC0000249,

            /// <summary>
            ///     MessageId: StatusInsufficientLogonInfo
            ///     MessageText:
            ///     There is insufficient account information to log you on.
            /// </summary>
            StatusInsufficientLogonInfo = 0xC0000250,

            /// <summary>
            ///     MessageId: StatusBadDllEntrypoint
            ///     MessageText:
            ///     {Invalid Dll Entrypoint}
            ///     The dynamic link library %hs is not written correctly. The stack pointer has been left in an inconsistent state.
            ///     The entrypoint should be declared as Winapi or Stdcall. Select Yes to fail the Dll load. Select No to continue
            ///     execution. Selecting No may cause the application to operate incorrectly.
            /// </summary>
            StatusBadDllEntrypoint = 0xC0000251,

            /// <summary>
            ///     MessageId: StatusBadServiceEntrypoint
            ///     MessageText:
            ///     {Invalid Service Callback Entrypoint}
            ///     The %hs service is not written correctly. The stack pointer has been left in an inconsistent state. The callback
            ///     entrypoint should be declared as Winapi or Stdcall. Selecting Ok will cause the service to continue operation.
            ///     However, the service process may operate incorrectly.
            /// </summary>
            StatusBadServiceEntrypoint = 0xC0000252,

            /// <summary>
            ///     MessageId: StatusLpcReplyLost
            ///     MessageText:
            ///     The server received the messages but did not send a reply.
            /// </summary>
            StatusLpcReplyLost = 0xC0000253,

            /// <summary>
            ///     MessageId: StatusIpAddressConflict1
            ///     MessageText:
            ///     There is an Ip address conflict with another system on the network
            /// </summary>
            StatusIpAddressConflict1 = 0xC0000254,

            /// <summary>
            ///     MessageId: StatusIpAddressConflict2
            ///     MessageText:
            ///     There is an Ip address conflict with another system on the network
            /// </summary>
            StatusIpAddressConflict2 = 0xC0000255,

            /// <summary>
            ///     MessageId: StatusRegistryQuotaLimit
            ///     MessageText:
            ///     {Low On Registry Space}
            ///     The system has reached the maximum size allowed for the system part of the registry. Additional storage requests
            ///     will be ignored.
            /// </summary>
            StatusRegistryQuotaLimit = 0xC0000256,

            /// <summary>
            ///     MessageId: StatusPathNotCovered
            ///     MessageText:
            ///     The contacted server does not support the indicated part of the Dfs namespace.
            /// </summary>
            StatusPathNotCovered = 0xC0000257,

            /// <summary>
            ///     MessageId: StatusNoCallbackActive
            ///     MessageText:
            ///     A callback return system service cannot be executed when no callback is active.
            /// </summary>
            StatusNoCallbackActive = 0xC0000258,

            /// <summary>
            ///     MessageId: StatusLicenseQuotaExceeded
            ///     MessageText:
            ///     The service being accessed is licensed for a particular number of connections. No more connections can be made to
            ///     the service at this time because there are already as many connections as the service can accept.
            /// </summary>
            StatusLicenseQuotaExceeded = 0xC0000259,

            /// <summary>
            ///     MessageId: StatusPwdTooShort
            ///     MessageText:
            ///     The password provided is too short to meet the policy of your user account. Please choose a longer password.
            /// </summary>
            StatusPwdTooShort = 0xC000025A,

            /// <summary>
            ///     MessageId: StatusPwdTooRecent
            ///     MessageText:
            ///     The policy of your user account does not allow you to change passwords too frequently. This is done to prevent
            ///     users from changing back to a familiar, but potentially discovered, password. If you feel your password has been
            ///     compromised then please contact your administrator immediately to have a new one assigned.
            /// </summary>
            StatusPwdTooRecent = 0xC000025B,

            /// <summary>
            ///     MessageId: StatusPwdHistoryConflict
            ///     MessageText:
            ///     You have attempted to change your password to one that you have used in the past. The policy of your user account
            ///     does not allow this. Please select a password that you have not previously used.
            /// </summary>
            StatusPwdHistoryConflict = 0xC000025C,

            /// <summary>
            ///     MessageId: StatusPlugplayNoDevice
            ///     MessageText:
            ///     You have attempted to load a legacy device driver while its device instance had been disabled.
            /// </summary>
            StatusPlugplayNoDevice = 0xC000025E,

            /// <summary>
            ///     MessageId: StatusUnsupportedCompression
            ///     MessageText:
            ///     The specified compression format is unsupported.
            /// </summary>
            StatusUnsupportedCompression = 0xC000025F,

            /// <summary>
            ///     MessageId: StatusInvalidHwProfile
            ///     MessageText:
            ///     The specified hardware profile configuration is invalid.
            /// </summary>
            StatusInvalidHwProfile = 0xC0000260,

            /// <summary>
            ///     MessageId: StatusInvalidPlugplayDevicePath
            ///     MessageText:
            ///     The specified Plug and Play registry device path is invalid.
            /// </summary>
            StatusInvalidPlugplayDevicePath = 0xC0000261,

            /// <summary>
            ///     MessageId: StatusDriverOrdinalNotFound
            ///     MessageText:
            ///     {Driver Entry Point Not Found}
            ///     The %hs device driver could not locate the ordinal %ld in driver %hs.
            /// </summary>
            StatusDriverOrdinalNotFound = 0xC0000262,

            /// <summary>
            ///     MessageId: StatusDriverEntrypointNotFound
            ///     MessageText:
            ///     {Driver Entry Point Not Found}
            ///     The %hs device driver could not locate the entry point %hs in driver %hs.
            /// </summary>
            StatusDriverEntrypointNotFound = 0xC0000263,

            /// <summary>
            ///     MessageId: StatusResourceNotOwned
            ///     MessageText:
            ///     {Application Error}
            ///     The application attempted to release a resource it did not own. Click Ok to terminate the application.
            /// </summary>
            StatusResourceNotOwned = 0xC0000264,

            /// <summary>
            ///     MessageId: StatusTooManyLinks
            ///     MessageText:
            ///     An attempt was made to create more links on a file than the file system supports.
            /// </summary>
            StatusTooManyLinks = 0xC0000265,

            /// <summary>
            ///     MessageId: StatusQuotaListInconsistent
            ///     MessageText:
            ///     The specified quota list is internally inconsistent with its descriptor.
            /// </summary>
            StatusQuotaListInconsistent = 0xC0000266,

            /// <summary>
            ///     MessageId: StatusFileIsOffline
            ///     MessageText:
            ///     The specified file has been relocated to offline storage.
            /// </summary>
            StatusFileIsOffline = 0xC0000267,

            /// <summary>
            ///     MessageId: StatusEvaluationExpiration
            ///     MessageText:
            ///     {Windows Evaluation Notification}
            ///     The evaluation period for this installation of Windows has expired. This system will shutdown in 1 hour. To restore
            ///     access to this installation of Windows, please upgrade this installation using a licensed distribution of this
            ///     product.
            /// </summary>
            StatusEvaluationExpiration = 0xC0000268,

            /// <summary>
            ///     MessageId: StatusIllegalDllRelocation
            ///     MessageText:
            ///     {Illegal System Dll Relocation}
            ///     The system Dll %hs was relocated in memory. The application will not run properly. The relocation occurred because
            ///     the Dll %hs occupied an address range reserved for Windows system DLLs. The vendor supplying the Dll should be
            ///     contacted for a new Dll.
            /// </summary>
            StatusIllegalDllRelocation = 0xC0000269,

            /// <summary>
            ///     MessageId: StatusLicenseViolation
            ///     MessageText:
            ///     {License Violation}
            ///     The system has detected tampering with your registered product type. This is a violation of your software license.
            ///     Tampering with product type is not permitted.
            /// </summary>
            StatusLicenseViolation = 0xC000026A,

            /// <summary>
            ///     MessageId: StatusDllInitFailedLogoff
            ///     MessageText:
            ///     {Dll Initialization Failed}
            ///     The application failed to initialize because the window station is shutting down.
            /// </summary>
            StatusDllInitFailedLogoff = 0xC000026B,

            /// <summary>
            ///     MessageId: StatusDriverUnableToLoad
            ///     MessageText:
            ///     {Unable to Load Device Driver}
            ///     %hs device driver could not be loaded.
            ///     Error Status was = 0x%x
            /// </summary>
            StatusDriverUnableToLoad = 0xC000026C,

            /// <summary>
            ///     MessageId: StatusDfsUnavailable
            ///     MessageText:
            ///     Dfs is unavailable on the contacted server.
            /// </summary>
            StatusDfsUnavailable = 0xC000026D,

            /// <summary>
            ///     MessageId: StatusVolumeDismounted
            ///     MessageText:
            ///     An operation was attempted to a volume after it was dismounted.
            /// </summary>
            StatusVolumeDismounted = 0xC000026E,

            /// <summary>
            ///     MessageId: StatusWx86InternalError
            ///     MessageText:
            ///     An internal error occurred in the Win32 x86 emulation subsystem.
            /// </summary>
            StatusWx86InternalError = 0xC000026F,

            /// <summary>
            ///     MessageId: StatusWx86FloatStackCheck
            ///     MessageText:
            ///     Win32 x86 emulation subsystem Floating-point stack check.
            /// </summary>
            StatusWx86FloatStackCheck = 0xC0000270,

            /// <summary>
            ///     MessageId: StatusValidateContinue
            ///     MessageText:
            ///     The validation process needs to continue on to the next step.
            /// </summary>
            StatusValidateContinue = 0xC0000271,

            /// <summary>
            ///     MessageId: StatusNoMatch
            ///     MessageText:
            ///     There was no match for the specified key in the index.
            /// </summary>
            StatusNoMatch = 0xC0000272,

            /// <summary>
            ///     MessageId: StatusNoMoreMatches
            ///     MessageText:
            ///     There are no more matches for the current index enumeration.
            /// </summary>
            StatusNoMoreMatches = 0xC0000273,

            /// <summary>
            ///     MessageId: StatusNotAReparsePoint
            ///     MessageText:
            ///     The Ntfs file or directory is not a reparse point.
            /// </summary>
            StatusNotAReparsePoint = 0xC0000275,

            /// <summary>
            ///     MessageId: StatusIoReparseTagInvalid
            ///     MessageText:
            ///     The Windows I/O reparse tag passed for the Ntfs reparse point is invalid.
            /// </summary>
            StatusIoReparseTagInvalid = 0xC0000276,

            /// <summary>
            ///     MessageId: StatusIoReparseTagMismatch
            ///     MessageText:
            ///     The Windows I/O reparse tag does not match the one present in the Ntfs reparse point.
            /// </summary>
            StatusIoReparseTagMismatch = 0xC0000277,

            /// <summary>
            ///     MessageId: StatusIoReparseDataInvalid
            ///     MessageText:
            ///     The user data passed for the Ntfs reparse point is invalid.
            /// </summary>
            StatusIoReparseDataInvalid = 0xC0000278,

            /// <summary>
            ///     MessageId: StatusIoReparseTagNotHandled
            ///     MessageText:
            ///     The layered file system driver for this Io tag did not handle it when needed.
            /// </summary>
            StatusIoReparseTagNotHandled = 0xC0000279,

            /// <summary>
            ///     MessageId: StatusReparsePointNotResolved
            ///     MessageText:
            ///     The Ntfs symbolic link could not be resolved even though the initial file name is valid.
            /// </summary>
            StatusReparsePointNotResolved = 0xC0000280,

            /// <summary>
            ///     MessageId: StatusDirectoryIsAReparsePoint
            ///     MessageText:
            ///     The Ntfs directory is a reparse point.
            /// </summary>
            StatusDirectoryIsAReparsePoint = 0xC0000281,

            /// <summary>
            ///     MessageId: StatusRangeListConflict
            ///     MessageText:
            ///     The range could not be added to the range list because of a conflict.
            /// </summary>
            StatusRangeListConflict = 0xC0000282,

            /// <summary>
            ///     MessageId: StatusSourceElementEmpty
            ///     MessageText:
            ///     The specified medium changer source element contains no media.
            /// </summary>
            StatusSourceElementEmpty = 0xC0000283,

            /// <summary>
            ///     MessageId: StatusDestinationElementFull
            ///     MessageText:
            ///     The specified medium changer destination element already contains media.
            /// </summary>
            StatusDestinationElementFull = 0xC0000284,

            /// <summary>
            ///     MessageId: StatusIllegalElementAddress
            ///     MessageText:
            ///     The specified medium changer element does not exist.
            /// </summary>
            StatusIllegalElementAddress = 0xC0000285,

            /// <summary>
            ///     MessageId: StatusMagazineNotPresent
            ///     MessageText:
            ///     The specified element is contained within a magazine that is no longer present.
            /// </summary>
            StatusMagazineNotPresent = 0xC0000286,

            /// <summary>
            ///     MessageId: StatusReinitializationNeeded
            ///     MessageText:
            ///     The device requires reinitialization due to hardware errors.
            /// </summary>
            StatusReinitializationNeeded = 0xC0000287,

            /// <summary>
            ///     MessageId: StatusDeviceRequiresCleaning
            ///     MessageText:
            ///     The device has indicated that cleaning is necessary.
            /// </summary>
            StatusDeviceRequiresCleaning = 0x80000288,

            /// <summary>
            ///     MessageId: StatusDeviceDoorOpen
            ///     MessageText:
            ///     The device has indicated that it's door is open. Further operations require it closed and secured.
            /// </summary>
            StatusDeviceDoorOpen = 0x80000289,

            /// <summary>
            ///     MessageId: StatusEncryptionFailed
            ///     MessageText:
            ///     The file encryption attempt failed.
            /// </summary>
            StatusEncryptionFailed = 0xC000028A,

            /// <summary>
            ///     MessageId: StatusDecryptionFailed
            ///     MessageText:
            ///     The file decryption attempt failed.
            /// </summary>
            StatusDecryptionFailed = 0xC000028B,

            /// <summary>
            ///     MessageId: StatusRangeNotFound
            ///     MessageText:
            ///     The specified range could not be found in the range list.
            /// </summary>
            StatusRangeNotFound = 0xC000028C,

            /// <summary>
            ///     MessageId: StatusNoRecoveryPolicy
            ///     MessageText:
            ///     There is no encryption recovery policy configured for this system.
            /// </summary>
            StatusNoRecoveryPolicy = 0xC000028D,

            /// <summary>
            ///     MessageId: StatusNoEfs
            ///     MessageText:
            ///     The required encryption driver is not loaded for this system.
            /// </summary>
            StatusNoEfs = 0xC000028E,

            /// <summary>
            ///     MessageId: StatusWrongEfs
            ///     MessageText:
            ///     The file was encrypted with a different encryption driver than is currently loaded.
            /// </summary>
            StatusWrongEfs = 0xC000028F,

            /// <summary>
            ///     MessageId: StatusNoUserKeys
            ///     MessageText:
            ///     There are no Efs keys defined for the user.
            /// </summary>
            StatusNoUserKeys = 0xC0000290,

            /// <summary>
            ///     MessageId: StatusFileNotEncrypted
            ///     MessageText:
            ///     The specified file is not encrypted.
            /// </summary>
            StatusFileNotEncrypted = 0xC0000291,

            /// <summary>
            ///     MessageId: StatusNotExportFormat
            ///     MessageText:
            ///     The specified file is not in the defined Efs export format.
            /// </summary>
            StatusNotExportFormat = 0xC0000292,

            /// <summary>
            ///     MessageId: StatusFileEncrypted
            ///     MessageText:
            ///     The specified file is encrypted and the user does not have the ability to decrypt it.
            /// </summary>
            StatusFileEncrypted = 0xC0000293,

            /// <summary>
            ///     MessageId: StatusWakeSystem
            ///     MessageText:
            ///     The system has awoken
            /// </summary>
            StatusWakeSystem = 0x40000294,

            /// <summary>
            ///     MessageId: StatusWmiGuidNotFound
            ///     MessageText:
            ///     The guid passed was not recognized as valid by a Wmi data provider.
            /// </summary>
            StatusWmiGuidNotFound = 0xC0000295,

            /// <summary>
            ///     MessageId: StatusWmiInstanceNotFound
            ///     MessageText:
            ///     The instance name passed was not recognized as valid by a Wmi data provider.
            /// </summary>
            StatusWmiInstanceNotFound = 0xC0000296,

            /// <summary>
            ///     MessageId: StatusWmiItemidNotFound
            ///     MessageText:
            ///     The data item id passed was not recognized as valid by a Wmi data provider.
            /// </summary>
            StatusWmiItemidNotFound = 0xC0000297,

            /// <summary>
            ///     MessageId: StatusWmiTryAgain
            ///     MessageText:
            ///     The Wmi request could not be completed and should be retried.
            /// </summary>
            StatusWmiTryAgain = 0xC0000298,

            /// <summary>
            ///     MessageId: StatusSharedPolicy
            ///     MessageText:
            ///     The policy object is shared and can only be modified at the root
            /// </summary>
            StatusSharedPolicy = 0xC0000299,

            /// <summary>
            ///     MessageId: StatusPolicyObjectNotFound
            ///     MessageText:
            ///     The policy object does not exist when it should
            /// </summary>
            StatusPolicyObjectNotFound = 0xC000029A,

            /// <summary>
            ///     MessageId: StatusPolicyOnlyInDs
            ///     MessageText:
            ///     The requested policy information only lives in the Ds
            /// </summary>
            StatusPolicyOnlyInDs = 0xC000029B,

            /// <summary>
            ///     MessageId: StatusVolumeNotUpgraded
            ///     MessageText:
            ///     The volume must be upgraded to enable this feature
            /// </summary>
            StatusVolumeNotUpgraded = 0xC000029C,

            /// <summary>
            ///     MessageId: StatusRemoteStorageNotActive
            ///     MessageText:
            ///     The remote storage service is not operational at this time.
            /// </summary>
            StatusRemoteStorageNotActive = 0xC000029D,

            /// <summary>
            ///     MessageId: StatusRemoteStorageMediaError
            ///     MessageText:
            ///     The remote storage service encountered a media error.
            /// </summary>
            StatusRemoteStorageMediaError = 0xC000029E,

            /// <summary>
            ///     MessageId: StatusNoTrackingService
            ///     MessageText:
            ///     The tracking (workstation, service is not running.
            /// </summary>
            StatusNoTrackingService = 0xC000029F,

            /// <summary>
            ///     MessageId: StatusServerSidMismatch
            ///     MessageText:
            ///     The server process is running under a Sid different than that required by client.
            /// </summary>
            StatusServerSidMismatch = 0xC00002A0,

            // Directory Service specific Errors

            /// <summary>
            ///     MessageId: StatusDsNoAttributeOrValue
            ///     MessageText:
            ///     The specified directory service attribute or value does not exist.
            /// </summary>
            StatusDsNoAttributeOrValue = 0xC00002A1,

            /// <summary>
            ///     MessageId: StatusDsInvalidAttributeSyntax
            ///     MessageText:
            ///     The attribute syntax specified to the directory service is invalid.
            /// </summary>
            StatusDsInvalidAttributeSyntax = 0xC00002A2,

            /// <summary>
            ///     MessageId: StatusDsAttributeTypeUndefined
            ///     MessageText:
            ///     The attribute type specified to the directory service is not defined.
            /// </summary>
            StatusDsAttributeTypeUndefined = 0xC00002A3,

            /// <summary>
            ///     MessageId: StatusDsAttributeOrValueExists
            ///     MessageText:
            ///     The specified directory service attribute or value already exists.
            /// </summary>
            StatusDsAttributeOrValueExists = 0xC00002A4,

            /// <summary>
            ///     MessageId: StatusDsBusy
            ///     MessageText:
            ///     The directory service is busy.
            /// </summary>
            StatusDsBusy = 0xC00002A5,

            /// <summary>
            ///     MessageId: StatusDsUnavailable
            ///     MessageText:
            ///     The directory service is not available.
            /// </summary>
            StatusDsUnavailable = 0xC00002A6,

            /// <summary>
            ///     MessageId: StatusDsNoRidsAllocated
            ///     MessageText:
            ///     The directory service was unable to allocate a relative identifier.
            /// </summary>
            StatusDsNoRidsAllocated = 0xC00002A7,

            /// <summary>
            ///     MessageId: StatusDsNoMoreRids
            ///     MessageText:
            ///     The directory service has exhausted the pool of relative identifiers.
            /// </summary>
            StatusDsNoMoreRids = 0xC00002A8,

            /// <summary>
            ///     MessageId: StatusDsIncorrectRoleOwner
            ///     MessageText:
            ///     The requested operation could not be performed because the directory service is not the master for that type of
            ///     operation.
            /// </summary>
            StatusDsIncorrectRoleOwner = 0xC00002A9,

            /// <summary>
            ///     MessageId: StatusDsRidmgrInitError
            ///     MessageText:
            ///     The directory service was unable to initialize the subsystem that allocates relative identifiers.
            /// </summary>
            StatusDsRidmgrInitError = 0xC00002AA,

            /// <summary>
            ///     MessageId: StatusDsObjClassViolation
            ///     MessageText:
            ///     The requested operation did not satisfy one or more constraints associated with the class of the object.
            /// </summary>
            StatusDsObjClassViolation = 0xC00002AB,

            /// <summary>
            ///     MessageId: StatusDsCantOnNonLeaf
            ///     MessageText:
            ///     The directory service can perform the requested operation only on a leaf object.
            /// </summary>
            StatusDsCantOnNonLeaf = 0xC00002AC,

            /// <summary>
            ///     MessageId: StatusDsCantOnRdn
            ///     MessageText:
            ///     The directory service cannot perform the requested operation on the Relatively Defined Name (Rdn, attribute of an
            ///     object.
            /// </summary>
            StatusDsCantOnRdn = 0xC00002AD,

            /// <summary>
            ///     MessageId: StatusDsCantModObjClass
            ///     MessageText:
            ///     The directory service detected an attempt to modify the object class of an object.
            /// </summary>
            StatusDsCantModObjClass = 0xC00002AE,

            /// <summary>
            ///     MessageId: StatusDsCrossDomMoveFailed
            ///     MessageText:
            ///     An error occurred while performing a cross domain move operation.
            /// </summary>
            StatusDsCrossDomMoveFailed = 0xC00002AF,

            /// <summary>
            ///     MessageId: StatusDsGcNotAvailable
            ///     MessageText:
            ///     Unable to Contact the Global Catalog Server.
            /// </summary>
            StatusDsGcNotAvailable = 0xC00002B0,

            /// <summary>
            ///     MessageId: StatusDirectoryServiceRequired
            ///     MessageText:
            ///     The requested operation requires a directory service, and none was available.
            /// </summary>
            StatusDirectoryServiceRequired = 0xC00002B1,

            /// <summary>
            ///     MessageId: StatusReparseAttributeConflict
            ///     MessageText:
            ///     The reparse attribute cannot be set as it is incompatible with an existing attribute.
            /// </summary>
            StatusReparseAttributeConflict = 0xC00002B2,

            /// <summary>
            ///     MessageId: StatusCantEnableDenyOnly
            ///     MessageText:
            ///     A group marked use for deny only cannot be enabled.
            /// </summary>
            StatusCantEnableDenyOnly = 0xC00002B3,

            /// <summary>
            ///     MessageId: StatusFloatMultipleFaults
            ///     MessageText:
            ///     {Exception}
            ///     Multiple floating point faults.
            /// </summary>
            StatusFloatMultipleFaults = 0xC00002B4, // winnt

            /// <summary>
            ///     MessageId: StatusFloatMultipleTraps
            ///     MessageText:
            ///     {Exception}
            ///     Multiple floating point traps.
            /// </summary>
            StatusFloatMultipleTraps = 0xC00002B5, // winnt

            /// <summary>
            ///     MessageId: StatusDeviceRemoved
            ///     MessageText:
            ///     The device has been removed.
            /// </summary>
            StatusDeviceRemoved = 0xC00002B6,

            /// <summary>
            ///     MessageId: StatusJournalDeleteInProgress
            ///     MessageText:
            ///     The volume change journal is being deleted.
            /// </summary>
            StatusJournalDeleteInProgress = 0xC00002B7,

            /// <summary>
            ///     MessageId: StatusJournalNotActive
            ///     MessageText:
            ///     The volume change journal is not active.
            /// </summary>
            StatusJournalNotActive = 0xC00002B8,

            /// <summary>
            ///     MessageId: StatusNointerface
            ///     MessageText:
            ///     The requested interface is not supported.
            /// </summary>
            StatusNointerface = 0xC00002B9,

            /// <summary>
            ///     MessageId: StatusDsAdminLimitExceeded
            ///     MessageText:
            ///     A directory service resource limit has been exceeded.
            /// </summary>
            StatusDsAdminLimitExceeded = 0xC00002C1,

            /// <summary>
            ///     MessageId: StatusDriverFailedSleep
            ///     MessageText:
            ///     {System Standby Failed}
            ///     The driver %hs does not support standby mode. Updating this driver may allow the system to go to standby mode.
            /// </summary>
            StatusDriverFailedSleep = 0xC00002C2,

            /// <summary>
            ///     MessageId: StatusMutualAuthenticationFailed
            ///     MessageText:
            ///     Mutual Authentication failed. The server's password is out of date at the domain controller.
            /// </summary>
            StatusMutualAuthenticationFailed = 0xC00002C3,

            /// <summary>
            ///     MessageId: StatusCorruptSystemFile
            ///     MessageText:
            ///     The system file %1 has become corrupt and has been replaced.
            /// </summary>
            StatusCorruptSystemFile = 0xC00002C4,

            /// <summary>
            ///     MessageId: StatusDatatypeMisalignmentError
            ///     MessageText:
            ///     {Exception}
            ///     Alignment Error
            ///     A datatype misalignment error was detected in a load or store instruction.
            /// </summary>
            StatusDatatypeMisalignmentError = 0xC00002C5,

            /// <summary>
            ///     MessageId: StatusWmiReadOnly
            ///     MessageText:
            ///     The Wmi data item or data block is read only.
            /// </summary>
            StatusWmiReadOnly = 0xC00002C6,

            /// <summary>
            ///     MessageId: StatusWmiSetFailure
            ///     MessageText:
            ///     The Wmi data item or data block could not be changed.
            /// </summary>
            StatusWmiSetFailure = 0xC00002C7,

            /// <summary>
            ///     MessageId: StatusCommitmentMinimum
            ///     MessageText:
            ///     {Virtual Memory Minimum Too Low}
            ///     Your system is low on virtual memory. Windows is increasing the size of your virtual memory paging file. During
            ///     this process, memory requests for some applications may be denied. For more information, see Help.
            /// </summary>
            StatusCommitmentMinimum = 0xC00002C8,

            /// <summary>
            ///     MessageId: StatusRegNatConsumption
            ///     MessageText:
            ///     {Exception}
            ///     Register NaT consumption faults.
            ///     A NaT value is consumed on a non speculative instruction.
            /// </summary>
            StatusRegNatConsumption = 0xC00002C9, // winnt

            /// <summary>
            ///     MessageId: StatusTransportFull
            ///     MessageText:
            ///     The medium changer's transport element contains media, which is causing the operation to fail.
            /// </summary>
            StatusTransportFull = 0xC00002CA,

            /// <summary>
            ///     MessageId: StatusDsSamInitFailure
            ///     MessageText:
            ///     Security Accounts Manager initialization failed because of the following error:
            ///     %hs
            ///     Error Status: = 0x%x.
            ///     Please shutdown this system and reboot into Directory Services Restore Mode, check the event log for more detailed
            ///     information.
            /// </summary>
            StatusDsSamInitFailure = 0xC00002CB,

            /// <summary>
            ///     MessageId: StatusOnlyIfConnected
            ///     MessageText:
            ///     This operation is supported only when you are connected to the server.
            /// </summary>
            StatusOnlyIfConnected = 0xC00002CC,

            /// <summary>
            ///     MessageId: StatusDsSensitiveGroupViolation
            ///     MessageText:
            ///     Only an administrator can modify the membership list of an administrative group.
            /// </summary>
            StatusDsSensitiveGroupViolation = 0xC00002CD,

            /// <summary>
            ///     MessageId: StatusPnpRestartEnumeration
            ///     MessageText:
            ///     A device was removed so enumeration must be restarted.
            /// </summary>
            StatusPnpRestartEnumeration = 0xC00002CE,

            /// <summary>
            ///     MessageId: StatusJournalEntryDeleted
            ///     MessageText:
            ///     The journal entry has been deleted from the journal.
            /// </summary>
            StatusJournalEntryDeleted = 0xC00002CF,

            /// <summary>
            ///     MessageId: StatusDsCantModPrimarygroupid
            ///     MessageText:
            ///     Cannot change the primary group Id of a domain controller account.
            /// </summary>
            StatusDsCantModPrimarygroupid = 0xC00002D0,

            /// <summary>
            ///     MessageId: StatusSystemImageBadSignature
            ///     MessageText:
            ///     {Fatal System Error}
            ///     The system image %s is not properly signed. The file has been replaced with the signed file. The system has been
            ///     shut down.
            /// </summary>
            StatusSystemImageBadSignature = 0xC00002D1,

            /// <summary>
            ///     MessageId: StatusPnpRebootRequired
            ///     MessageText:
            ///     Device will not start without a reboot.
            /// </summary>
            StatusPnpRebootRequired = 0xC00002D2,

            /// <summary>
            ///     MessageId: StatusPowerStateInvalid
            ///     MessageText:
            ///     Current device power state cannot support this request.
            /// </summary>
            StatusPowerStateInvalid = 0xC00002D3,

            /// <summary>
            ///     MessageId: StatusDsInvalidGroupType
            ///     MessageText:
            ///     The specified group type is invalid.
            /// </summary>
            StatusDsInvalidGroupType = 0xC00002D4,

            /// <summary>
            ///     MessageId: StatusDsNoNestGlobalgroupInMixeddomain
            ///     MessageText:
            ///     In mixed domain no nesting of global group if group is security enabled.
            /// </summary>
            StatusDsNoNestGlobalgroupInMixeddomain = 0xC00002D5,

            /// <summary>
            ///     MessageId: StatusDsNoNestLocalgroupInMixeddomain
            ///     MessageText:
            ///     In mixed domain, cannot nest local groups with other local groups, if the group is security enabled.
            /// </summary>
            StatusDsNoNestLocalgroupInMixeddomain = 0xC00002D6,

            /// <summary>
            ///     MessageId: StatusDsGlobalCantHaveLocalMember
            ///     MessageText:
            ///     A global group cannot have a local group as a member.
            /// </summary>
            StatusDsGlobalCantHaveLocalMember = 0xC00002D7,

            /// <summary>
            ///     MessageId: StatusDsGlobalCantHaveUniversalMember
            ///     MessageText:
            ///     A global group cannot have a universal group as a member.
            /// </summary>
            StatusDsGlobalCantHaveUniversalMember = 0xC00002D8,

            /// <summary>
            ///     MessageId: StatusDsUniversalCantHaveLocalMember
            ///     MessageText:
            ///     A universal group cannot have a local group as a member.
            /// </summary>
            StatusDsUniversalCantHaveLocalMember = 0xC00002D9,

            /// <summary>
            ///     MessageId: StatusDsGlobalCantHaveCrossdomainMember
            ///     MessageText:
            ///     A global group cannot have a cross domain member.
            /// </summary>
            StatusDsGlobalCantHaveCrossdomainMember = 0xC00002DA,

            /// <summary>
            ///     MessageId: StatusDsLocalCantHaveCrossdomainLocalMember
            ///     MessageText:
            ///     A local group cannot have another cross domain local group as a member.
            /// </summary>
            StatusDsLocalCantHaveCrossdomainLocalMember = 0xC00002DB,

            /// <summary>
            ///     MessageId: StatusDsHavePrimaryMembers
            ///     MessageText:
            ///     Cannot change to security disabled group because of having primary members in this group.
            /// </summary>
            StatusDsHavePrimaryMembers = 0xC00002DC,

            /// <summary>
            ///     MessageId: StatusWmiNotSupported
            ///     MessageText:
            ///     The Wmi operation is not supported by the data block or method.
            /// </summary>
            StatusWmiNotSupported = 0xC00002DD,

            /// <summary>
            ///     MessageId: StatusInsufficientPower
            ///     MessageText:
            ///     There is not enough power to complete the requested operation.
            /// </summary>
            StatusInsufficientPower = 0xC00002DE,

            /// <summary>
            ///     MessageId: StatusSamNeedBootkeyPassword
            ///     MessageText:
            ///     Security Account Manager needs to get the boot password.
            /// </summary>
            StatusSamNeedBootkeyPassword = 0xC00002DF,

            /// <summary>
            ///     MessageId: StatusSamNeedBootkeyFloppy
            ///     MessageText:
            ///     Security Account Manager needs to get the boot key from floppy disk.
            /// </summary>
            StatusSamNeedBootkeyFloppy = 0xC00002E0,

            /// <summary>
            ///     MessageId: StatusDsCantStart
            ///     MessageText:
            ///     Directory Service cannot start.
            /// </summary>
            StatusDsCantStart = 0xC00002E1,

            /// <summary>
            ///     MessageId: StatusDsInitFailure
            ///     MessageText:
            ///     Directory Services could not start because of the following error:
            ///     %hs
            ///     Error Status: = 0x%x.
            ///     Please shutdown this system and reboot into Directory Services Restore Mode, check the event log for more detailed
            ///     information.
            /// </summary>
            StatusDsInitFailure = 0xC00002E2,

            /// <summary>
            ///     MessageId: StatusSamInitFailure
            ///     MessageText:
            ///     Security Accounts Manager initialization failed because of the following error:
            ///     %hs
            ///     Error Status: = 0x%x.
            ///     Please click Ok to shutdown this system and reboot into Safe Mode, check the event log for more detailed
            ///     information.
            /// </summary>
            StatusSamInitFailure = 0xC00002E3,

            /// <summary>
            ///     MessageId: StatusDsGcRequired
            ///     MessageText:
            ///     The requested operation can be performed only on a global catalog server.
            /// </summary>
            StatusDsGcRequired = 0xC00002E4,

            /// <summary>
            ///     MessageId: StatusDsLocalMemberOfLocalOnly
            ///     MessageText:
            ///     A local group can only be a member of other local groups in the same domain.
            /// </summary>
            StatusDsLocalMemberOfLocalOnly = 0xC00002E5,

            /// <summary>
            ///     MessageId: StatusDsNoFpoInUniversalGroups
            ///     MessageText:
            ///     Foreign security principals cannot be members of universal groups.
            /// </summary>
            StatusDsNoFpoInUniversalGroups = 0xC00002E6,

            /// <summary>
            ///     MessageId: StatusDsMachineAccountQuotaExceeded
            ///     MessageText:
            ///     Your computer could not be joined to the domain. You have exceeded the maximum number of computer accounts you are
            ///     allowed to create in this domain. Contact your system administrator to have this limit reset or increased.
            /// </summary>
            StatusDsMachineAccountQuotaExceeded = 0xC00002E7,

            /// <summary>
            ///     MessageId: StatusMultipleFaultViolation
            ///     MessageText:
            ///     StatusMultipleFaultViolation
            /// </summary>
            StatusMultipleFaultViolation = 0xC00002E8,

            /// <summary>
            ///     MessageId: StatusCurrentDomainNotAllowed
            ///     MessageText:
            ///     This operation cannot be performed on the current domain.
            /// </summary>
            StatusCurrentDomainNotAllowed = 0xC00002E9,

            /// <summary>
            ///     MessageId: StatusCannotMake
            ///     MessageText:
            ///     The directory or file cannot be created.
            /// </summary>
            StatusCannotMake = 0xC00002EA,

            /// <summary>
            ///     MessageId: StatusSystemShutdown
            ///     MessageText:
            ///     The system is in the process of shutting down.
            /// </summary>
            StatusSystemShutdown = 0xC00002EB,

            /// <summary>
            ///     MessageId: StatusDsInitFailureConsole
            ///     MessageText:
            ///     Directory Services could not start because of the following error:
            ///     %hs
            ///     Error Status: = 0x%x.
            ///     Please click Ok to shutdown the system. You can use the recovery console to diagnose the system further.
            /// </summary>
            StatusDsInitFailureConsole = 0xC00002EC,

            /// <summary>
            ///     MessageId: StatusDsSamInitFailureConsole
            ///     MessageText:
            ///     Security Accounts Manager initialization failed because of the following error:
            ///     %hs
            ///     Error Status: = 0x%x.
            ///     Please click Ok to shutdown the system. You can use the recovery console to diagnose the system further.
            /// </summary>
            StatusDsSamInitFailureConsole = 0xC00002ED,

            /// <summary>
            ///     MessageId: StatusUnfinishedContextDeleted
            ///     MessageText:
            ///     A security context was deleted before the context was completed. This is considered a logon failure.
            /// </summary>
            StatusUnfinishedContextDeleted = 0xC00002EE,

            /// <summary>
            ///     MessageId: StatusNoTgtReply
            ///     MessageText:
            ///     The client is trying to negotiate a context and the server requires user-to-user but didn't send a Tgt reply.
            /// </summary>
            StatusNoTgtReply = 0xC00002EF,

            /// <summary>
            ///     MessageId: StatusObjectidNotFound
            ///     MessageText:
            ///     An object Id was not found in the file.
            /// </summary>
            StatusObjectidNotFound = 0xC00002F0,

            /// <summary>
            ///     MessageId: StatusNoIpAddresses
            ///     MessageText:
            ///     Unable to accomplish the requested task because the local machine does not have any Ip addresses.
            /// </summary>
            StatusNoIpAddresses = 0xC00002F1,

            /// <summary>
            ///     MessageId: StatusWrongCredentialHandle
            ///     MessageText:
            ///     The supplied credential handle does not match the credential associated with the security context.
            /// </summary>
            StatusWrongCredentialHandle = 0xC00002F2,

            /// <summary>
            ///     MessageId: StatusCryptoSystemInvalid
            ///     MessageText:
            ///     The crypto system or checksum function is invalid because a required function is unavailable.
            /// </summary>
            StatusCryptoSystemInvalid = 0xC00002F3,

            /// <summary>
            ///     MessageId: StatusMaxReferralsExceeded
            ///     MessageText:
            ///     The number of maximum ticket referrals has been exceeded.
            /// </summary>
            StatusMaxReferralsExceeded = 0xC00002F4,

            /// <summary>
            ///     MessageId: StatusMustBeKdc
            ///     MessageText:
            ///     The local machine must be a Kerberos Kdc (domain controller, and it is not.
            /// </summary>
            StatusMustBeKdc = 0xC00002F5,

            /// <summary>
            ///     MessageId: StatusStrongCryptoNotSupported
            ///     MessageText:
            ///     The other end of the security negotiation is requires strong crypto but it is not supported on the local machine.
            /// </summary>
            StatusStrongCryptoNotSupported = 0xC00002F6,

            /// <summary>
            ///     MessageId: StatusTooManyPrincipals
            ///     MessageText:
            ///     The Kdc reply contained more than one principal name.
            /// </summary>
            StatusTooManyPrincipals = 0xC00002F7,

            /// <summary>
            ///     MessageId: StatusNoPaData
            ///     MessageText:
            ///     Expected to find Pa data for a hint of what etype to use, but it was not found.
            /// </summary>
            StatusNoPaData = 0xC00002F8,

            /// <summary>
            ///     MessageId: StatusPkinitNameMismatch
            ///     MessageText:
            ///     The client certificate does not contain a valid Upn, or does not match the client name in the logon request. Please
            ///     contact your administrator.
            /// </summary>
            StatusPkinitNameMismatch = 0xC00002F9,

            /// <summary>
            ///     MessageId: StatusSmartcardLogonRequired
            ///     MessageText:
            ///     Smartcard logon is required and was not used.
            /// </summary>
            StatusSmartcardLogonRequired = 0xC00002FA,

            /// <summary>
            ///     MessageId: StatusKdcInvalidRequest
            ///     MessageText:
            ///     An invalid request was sent to the Kdc.
            /// </summary>
            StatusKdcInvalidRequest = 0xC00002FB,

            /// <summary>
            ///     MessageId: StatusKdcUnableToRefer
            ///     MessageText:
            ///     The Kdc was unable to generate a referral for the service requested.
            /// </summary>
            StatusKdcUnableToRefer = 0xC00002FC,

            /// <summary>
            ///     MessageId: StatusKdcUnknownEtype
            ///     MessageText:
            ///     The encryption type requested is not supported by the Kdc.
            /// </summary>
            StatusKdcUnknownEtype = 0xC00002FD,

            /// <summary>
            ///     MessageId: StatusShutdownInProgress
            ///     MessageText:
            ///     A system shutdown is in progress.
            /// </summary>
            StatusShutdownInProgress = 0xC00002FE,

            /// <summary>
            ///     MessageId: StatusServerShutdownInProgress
            ///     MessageText:
            ///     The server machine is shutting down.
            /// </summary>
            StatusServerShutdownInProgress = 0xC00002FF,

            /// <summary>
            ///     MessageId: StatusNotSupportedOnSbs
            ///     MessageText:
            ///     This operation is not supported on a computer running Windows Server 2003 for Small Business Server
            /// </summary>
            StatusNotSupportedOnSbs = 0xC0000300,

            /// <summary>
            ///     MessageId: StatusWmiGuidDisconnected
            ///     MessageText:
            ///     The Wmi Guid is no longer available
            /// </summary>
            StatusWmiGuidDisconnected = 0xC0000301,

            /// <summary>
            ///     MessageId: StatusWmiAlreadyDisabled
            ///     MessageText:
            ///     Collection or events for the Wmi Guid is already disabled.
            /// </summary>
            StatusWmiAlreadyDisabled = 0xC0000302,

            /// <summary>
            ///     MessageId: StatusWmiAlreadyEnabled
            ///     MessageText:
            ///     Collection or events for the Wmi Guid is already enabled.
            /// </summary>
            StatusWmiAlreadyEnabled = 0xC0000303,

            /// <summary>
            ///     MessageId: StatusMftTooFragmented
            ///     MessageText:
            ///     The Master File Table on the volume is too fragmented to complete this operation.
            /// </summary>
            StatusMftTooFragmented = 0xC0000304,

            /// <summary>
            ///     MessageId: StatusCopyProtectionFailure
            ///     MessageText:
            ///     Copy protection failure.
            /// </summary>
            StatusCopyProtectionFailure = 0xC0000305,

            /// <summary>
            ///     MessageId: StatusCssAuthenticationFailure
            ///     MessageText:
            ///     Copy protection error - Dvd Css Authentication failed.
            /// </summary>
            StatusCssAuthenticationFailure = 0xC0000306,

            /// <summary>
            ///     MessageId: StatusCssKeyNotPresent
            ///     MessageText:
            ///     Copy protection error - The given sector does not contain a valid key.
            /// </summary>
            StatusCssKeyNotPresent = 0xC0000307,

            /// <summary>
            ///     MessageId: StatusCssKeyNotEstablished
            ///     MessageText:
            ///     Copy protection error - Dvd session key not established.
            /// </summary>
            StatusCssKeyNotEstablished = 0xC0000308,

            /// <summary>
            ///     MessageId: StatusCssScrambledSector
            ///     MessageText:
            ///     Copy protection error - The read failed because the sector is encrypted.
            /// </summary>
            StatusCssScrambledSector = 0xC0000309,

            /// <summary>
            ///     MessageId: StatusCssRegionMismatch
            ///     MessageText:
            ///     Copy protection error - The given Dvd's region does not correspond to the
            ///     region setting of the drive.
            /// </summary>
            StatusCssRegionMismatch = 0xC000030A,

            /// <summary>
            ///     MessageId: StatusCssResetsExhausted
            ///     MessageText:
            ///     Copy protection error - The drive's region setting may be permanent.
            /// </summary>
            StatusCssResetsExhausted = 0xC000030B,

            /*++

             MessageId's = 0x030c - = 0x031f (inclusive, are reserved for future **Storage**
             copy protection errors.

            --*/

            /// <summary>
            ///     MessageId: StatusPkinitFailure
            ///     MessageText:
            ///     The Kerberos protocol encountered an error while validating the Kdc certificate during smartcard Logon. There is
            ///     more information in the system event log.
            /// </summary>
            StatusPkinitFailure = 0xC0000320,

            /// <summary>
            ///     MessageId: StatusSmartcardSubsystemFailure
            ///     MessageText:
            ///     The Kerberos protocol encountered an error while attempting to utilize the smartcard subsystem.
            /// </summary>
            StatusSmartcardSubsystemFailure = 0xC0000321,

            /// <summary>
            ///     MessageId: StatusNoKerbKey
            ///     MessageText:
            ///     The target server does not have acceptable Kerberos credentials.
            /// </summary>
            StatusNoKerbKey = 0xC0000322,

            /*++

             MessageId's = 0x0323 - = 0x034f (inclusive, are reserved for other future copy
             protection errors.

            --*/

            /// <summary>
            ///     MessageId: StatusHostDown
            ///     MessageText:
            ///     The transport determined that the remote system is down.
            /// </summary>
            StatusHostDown = 0xC0000350,

            /// <summary>
            ///     MessageId: StatusUnsupportedPreauth
            ///     MessageText:
            ///     An unsupported preauthentication mechanism was presented to the Kerberos package.
            /// </summary>
            StatusUnsupportedPreauth = 0xC0000351,

            /// <summary>
            ///     MessageId: StatusEfsAlgBlobTooBig
            ///     MessageText:
            ///     The encryption algorithm used on the source file needs a bigger key buffer than the one used on the destination
            ///     file.
            /// </summary>
            StatusEfsAlgBlobTooBig = 0xC0000352,

            /// <summary>
            ///     MessageId: StatusPortNotSet
            ///     MessageText:
            ///     An attempt to remove a process's DebugPort was made, but a port was not already associated with the process.
            /// </summary>
            StatusPortNotSet = 0xC0000353,

            /// <summary>
            ///     MessageId: StatusDebuggerInactive
            ///     MessageText:
            ///     Debugger Inactive: Windows may have been started without kernel debugging enabled.
            /// </summary>
            StatusDebuggerInactive = 0xC0000354,

            /// <summary>
            ///     MessageId: StatusDsVersionCheckFailure
            ///     MessageText:
            ///     This version of Windows is not compatible with the behavior version of directory forest, domain or domain
            ///     controller.
            /// </summary>
            StatusDsVersionCheckFailure = 0xC0000355,

            /// <summary>
            ///     MessageId: StatusAuditingDisabled
            ///     MessageText:
            ///     The specified event is currently not being audited.
            /// </summary>
            StatusAuditingDisabled = 0xC0000356,

            /// <summary>
            ///     MessageId: StatusPrent4MachineAccount
            ///     MessageText:
            ///     The machine account was created pre-Nt4. The account needs to be recreated.
            /// </summary>
            StatusPrent4MachineAccount = 0xC0000357,

            /// <summary>
            ///     MessageId: StatusDsAgCantHaveUniversalMember
            ///     MessageText:
            ///     A account group cannot have a universal group as a member.
            /// </summary>
            StatusDsAgCantHaveUniversalMember = 0xC0000358,

            /// <summary>
            ///     MessageId: StatusInvalidImageWin32
            ///     MessageText:
            ///     The specified image file did not have the correct format, it appears to be a 32-bit Windows image.
            /// </summary>
            StatusInvalidImageWin32 = 0xC0000359,

            /// <summary>
            ///     MessageId: StatusInvalidImageWin64
            ///     MessageText:
            ///     The specified image file did not have the correct format, it appears to be a 64-bit Windows image.
            /// </summary>
            StatusInvalidImageWin64 = 0xC000035A,

            /// <summary>
            ///     MessageId: StatusBadBindings
            ///     MessageText:
            ///     Client's supplied Sspi channel bindings were incorrect.
            /// </summary>
            StatusBadBindings = 0xC000035B,

            /// <summary>
            ///     MessageId: StatusNetworkSessionExpired
            ///     MessageText:
            ///     The client's session has expired, so the client must reauthenticate to continue accessing the remote resources.
            /// </summary>
            StatusNetworkSessionExpired = 0xC000035C,

            /// <summary>
            ///     MessageId: StatusApphelpBlock
            ///     MessageText:
            ///     AppHelp dialog canceled thus preventing the application from starting.
            /// </summary>
            StatusApphelpBlock = 0xC000035D,

            /// <summary>
            ///     MessageId: StatusAllSidsFiltered
            ///     MessageText:
            ///     The Sid filtering operation removed all SIDs.
            /// </summary>
            StatusAllSidsFiltered = 0xC000035E,

            /// <summary>
            ///     MessageId: StatusNotSafeModeDriver
            ///     MessageText:
            ///     The driver was not loaded because the system is booting into safe mode.
            /// </summary>
            StatusNotSafeModeDriver = 0xC000035F,

            /// <summary>
            ///     MessageId: StatusAccessDisabledByPolicyDefault
            ///     MessageText:
            ///     Access to %1 has been restricted by your Administrator by the default software restriction policy level.
            /// </summary>
            StatusAccessDisabledByPolicyDefault = 0xC0000361,

            /// <summary>
            ///     MessageId: StatusAccessDisabledByPolicyPath
            ///     MessageText:
            ///     Access to %1 has been restricted by your Administrator by location with policy rule %2 placed on path %3
            /// </summary>
            StatusAccessDisabledByPolicyPath = 0xC0000362,

            /// <summary>
            ///     MessageId: StatusAccessDisabledByPolicyPublisher
            ///     MessageText:
            ///     Access to %1 has been restricted by your Administrator by software publisher policy.
            /// </summary>
            StatusAccessDisabledByPolicyPublisher = 0xC0000363,

            /// <summary>
            ///     MessageId: StatusAccessDisabledByPolicyOther
            ///     MessageText:
            ///     Access to %1 has been restricted by your Administrator by policy rule %2.
            /// </summary>
            StatusAccessDisabledByPolicyOther = 0xC0000364,

            /// <summary>
            ///     MessageId: StatusFailedDriverEntry
            ///     MessageText:
            ///     The driver was not loaded because it failed it's initialization call.
            /// </summary>
            StatusFailedDriverEntry = 0xC0000365,

            /// <summary>
            ///     MessageId: StatusDeviceEnumerationError
            ///     MessageText:
            ///     The "%hs" encountered an error while applying power or reading the device configuration. This may be caused by a
            ///     failure of your hardware or by a poor connection.
            /// </summary>
            StatusDeviceEnumerationError = 0xC0000366,

            /// <summary>
            ///     MessageId: StatusMountPointNotResolved
            ///     MessageText:
            ///     The create operation failed because the name contained at least one mount point which resolves to a volume to which
            ///     the specified device object is not attached.
            /// </summary>
            StatusMountPointNotResolved = 0xC0000368,

            /// <summary>
            ///     MessageId: StatusInvalidDeviceObjectParameter
            ///     MessageText:
            ///     The device object parameter is either not a valid device object or is not attached to the volume specified by the
            ///     file name.
            /// </summary>
            StatusInvalidDeviceObjectParameter = 0xC0000369,

            /// <summary>
            ///     MessageId: StatusMcaOccured
            ///     MessageText:
            ///     A Machine Check Error has occurred. Please check the system eventlog for additional information.
            /// </summary>
            StatusMcaOccured = 0xC000036A,

            /// <summary>
            ///     MessageId: StatusDriverBlockedCritical
            ///     MessageText:
            ///     Driver %2 has been blocked from loading.
            /// </summary>
            StatusDriverBlockedCritical = 0xC000036B,

            /// <summary>
            ///     MessageId: StatusDriverBlocked
            ///     MessageText:
            ///     Driver %2 has been blocked from loading.
            /// </summary>
            StatusDriverBlocked = 0xC000036C,

            /// <summary>
            ///     MessageId: StatusDriverDatabaseError
            ///     MessageText:
            ///     There was error [%2] processing the driver database.
            /// </summary>
            StatusDriverDatabaseError = 0xC000036D,

            /// <summary>
            ///     MessageId: StatusSystemHiveTooLarge
            ///     MessageText:
            ///     System hive size has exceeded its limit.
            /// </summary>
            StatusSystemHiveTooLarge = 0xC000036E,

            /// <summary>
            ///     MessageId: StatusInvalidImportOfNonDll
            ///     MessageText:
            ///     A dynamic link library (Dl, referenced a module that was neither a Dll nor the process's executable image.
            /// </summary>
            StatusInvalidImportOfNonDll = 0xC000036F,

            /// <summary>
            ///     MessageId: StatusDsShuttingDown
            ///     MessageText:
            ///     The Directory Service is shutting down.
            /// </summary>
            StatusDsShuttingDown = 0x40000370,

            /// <summary>
            ///     MessageId: StatusNoSecrets
            ///     MessageText:
            ///     The local account store does not contain secret material for the specified account.
            /// </summary>
            StatusNoSecrets = 0xC0000371,

            /// <summary>
            ///     MessageId: StatusAccessDisabledNoSaferUiByPolicy
            ///     MessageText:
            ///     Access to %1 has been restricted by your Administrator by policy rule %2.
            /// </summary>
            StatusAccessDisabledNoSaferUiByPolicy = 0xC0000372,

            /// <summary>
            ///     MessageId: StatusFailedStackSwitch
            ///     MessageText:
            ///     The system was not able to allocate enough memory to perform a stack switch.
            /// </summary>
            StatusFailedStackSwitch = 0xC0000373,

            /// <summary>
            ///     MessageId: StatusHeapCorruption
            ///     MessageText:
            ///     A heap has been corrupted.
            /// </summary>
            StatusHeapCorruption = 0xC0000374,

            /// <summary>
            ///     MessageId: StatusSmartcardWrongPin
            ///     MessageText:
            ///     An incorrect Pin was presented to the smart card
            /// </summary>
            StatusSmartcardWrongPin = 0xC0000380,

            /// <summary>
            ///     MessageId: StatusSmartcardCardBlocked
            ///     MessageText:
            ///     The smart card is blocked
            /// </summary>
            StatusSmartcardCardBlocked = 0xC0000381,

            /// <summary>
            ///     MessageId: StatusSmartcardCardNotAuthenticated
            ///     MessageText:
            ///     No Pin was presented to the smart card
            /// </summary>
            StatusSmartcardCardNotAuthenticated = 0xC0000382,

            /// <summary>
            ///     MessageId: StatusSmartcardNoCard
            ///     MessageText:
            ///     No smart card available
            /// </summary>
            StatusSmartcardNoCard = 0xC0000383,

            /// <summary>
            ///     MessageId: StatusSmartcardNoKeyContainer
            ///     MessageText:
            ///     The requested key container does not exist on the smart card
            /// </summary>
            StatusSmartcardNoKeyContainer = 0xC0000384,

            /// <summary>
            ///     MessageId: StatusSmartcardNoCertificate
            ///     MessageText:
            ///     The requested certificate does not exist on the smart card
            /// </summary>
            StatusSmartcardNoCertificate = 0xC0000385,

            /// <summary>
            ///     MessageId: StatusSmartcardNoKeyset
            ///     MessageText:
            ///     The requested keyset does not exist
            /// </summary>
            StatusSmartcardNoKeyset = 0xC0000386,

            /// <summary>
            ///     MessageId: StatusSmartcardIoError
            ///     MessageText:
            ///     A communication error with the smart card has been detected.
            /// </summary>
            StatusSmartcardIoError = 0xC0000387,

            /// <summary>
            ///     MessageId: StatusDowngradeDetected
            ///     MessageText:
            ///     The system detected a possible attempt to compromise security. Please ensure that you can contact the server that
            ///     authenticated you.
            /// </summary>
            StatusDowngradeDetected = 0xC0000388,

            /// <summary>
            ///     MessageId: StatusSmartcardCertRevoked
            ///     MessageText:
            ///     The smartcard certificate used for authentication has been revoked. Please contact your system administrator. There
            ///     may be additional information in the event log.
            /// </summary>
            StatusSmartcardCertRevoked = 0xC0000389,

            /// <summary>
            ///     MessageId: StatusIssuingCaUntrusted
            ///     MessageText:
            ///     An untrusted certificate authority was detected While processing the smartcard certificate used for authentication.
            ///     Please contact your system administrator.
            /// </summary>
            StatusIssuingCaUntrusted = 0xC000038A,

            /// <summary>
            ///     MessageId: StatusRevocationOfflineC
            ///     MessageText:
            ///     The revocation status of the smartcard certificate used for authentication could not be determined. Please contact
            ///     your system administrator.
            /// </summary>
            StatusRevocationOfflineC = 0xC000038B,

            /// <summary>
            ///     MessageId: StatusPkinitClientFailure
            ///     MessageText:
            ///     The smartcard certificate used for authentication was not trusted. Please contact your system administrator.
            /// </summary>
            StatusPkinitClientFailure = 0xC000038C,

            /// <summary>
            ///     MessageId: StatusSmartcardCertExpired
            ///     MessageText:
            ///     The smartcard certificate used for authentication has expired. Please
            ///     contact your system administrator.
            /// </summary>
            StatusSmartcardCertExpired = 0xC000038D,

            /// <summary>
            ///     MessageId: StatusDriverFailedPriorUnload
            ///     MessageText:
            ///     The driver could not be loaded because a previous version of the driver is still in memory.
            /// </summary>
            StatusDriverFailedPriorUnload = 0xC000038E,

            /// <summary>
            ///     MessageId: StatusSmartcardSilentContext
            ///     MessageText:
            ///     The smartcard provider could not perform the action since the context was acquired as silent.
            /// </summary>
            StatusSmartcardSilentContext = 0xC000038F,

            /* MessageId up to = 0x400 is reserved for smart cards */

            /// <summary>
            ///     MessageId: StatusPerUserTrustQuotaExceeded
            ///     MessageText:
            ///     The current user's delegated trust creation quota has been exceeded.
            /// </summary>
            StatusPerUserTrustQuotaExceeded = 0xC0000401,

            /// <summary>
            ///     MessageId: StatusAllUserTrustQuotaExceeded
            ///     MessageText:
            ///     The total delegated trust creation quota has been exceeded.
            /// </summary>
            StatusAllUserTrustQuotaExceeded = 0xC0000402,

            /// <summary>
            ///     MessageId: StatusUserDeleteTrustQuotaExceeded
            ///     MessageText:
            ///     The current user's delegated trust deletion quota has been exceeded.
            /// </summary>
            StatusUserDeleteTrustQuotaExceeded = 0xC0000403,

            /// <summary>
            ///     MessageId: StatusDsNameNotUnique
            ///     MessageText:
            ///     The requested name already exists as a unique identifier.
            /// </summary>
            StatusDsNameNotUnique = 0xC0000404,

            /// <summary>
            ///     MessageId: StatusDsDuplicateIdFound
            ///     MessageText:
            ///     The requested object has a non-unique identifier and cannot be retrieved.
            /// </summary>
            StatusDsDuplicateIdFound = 0xC0000405,

            /// <summary>
            ///     MessageId: StatusDsGroupConversionError
            ///     MessageText:
            ///     The group cannot be converted due to attribute restrictions on the requested group type.
            /// </summary>
            StatusDsGroupConversionError = 0xC0000406,

            /// <summary>
            ///     MessageId: StatusVolsnapPrepareHibernate
            ///     MessageText:
            ///     {Volume Shadow Copy Service}
            ///     Please wait while the Volume Shadow Copy Service prepares volume %hs for hibernation.
            /// </summary>
            StatusVolsnapPrepareHibernate = 0xC0000407,

            /// <summary>
            ///     MessageId: StatusUser2userRequired
            ///     MessageText:
            ///     Kerberos sub-protocol User2User is required.
            /// </summary>
            StatusUser2userRequired = 0xC0000408,

            /// <summary>
            ///     MessageId: StatusStackBufferOverrun
            ///     MessageText:
            ///     The system detected an overrun of a stack-based buffer in this application. This overrun could potentially allow a
            ///     malicious user to gain control of this application.
            /// </summary>
            StatusStackBufferOverrun = 0xC0000409, // winnt

            /// <summary>
            ///     MessageId: StatusNoS4uProtSupport
            ///     MessageText:
            ///     The Kerberos subsystem encountered an error. A service for user protocol request was made against a domain
            ///     controller which does not support service for user.
            /// </summary>
            StatusNoS4uProtSupport = 0xC000040A,

            /// <summary>
            ///     MessageId: StatusCrossrealmDelegationFailure
            ///     MessageText:
            ///     An attempt was made by this server to make a Kerberos constrained delegation request for a target outside of the
            ///     server's realm. This is not supported, and indicates a misconfiguration on this server's allowed to delegate to
            ///     list. Please contact your administrator.
            /// </summary>
            StatusCrossrealmDelegationFailure = 0xC000040B,

            /// <summary>
            ///     MessageId: StatusRevocationOfflineKdc
            ///     MessageText:
            ///     The revocation status of the domain controller certificate used for smartcard authentication could not be
            ///     determined. There is additional information in the system event log. Please contact your system administrator.
            /// </summary>
            StatusRevocationOfflineKdc = 0xC000040C,

            /// <summary>
            ///     MessageId: StatusIssuingCaUntrustedKdc
            ///     MessageText:
            ///     An untrusted certificate authority was detected while processing the domain controller certificate used for
            ///     authentication. There is additional information in the system event log. Please contact your system administrator.
            /// </summary>
            StatusIssuingCaUntrustedKdc = 0xC000040D,

            /// <summary>
            ///     MessageId: StatusKdcCertExpired
            ///     MessageText:
            ///     The domain controller certificate used for smartcard logon has expired. Please contact your system administrator
            ///     with the contents of your system event log.
            /// </summary>
            StatusKdcCertExpired = 0xC000040E,

            /// <summary>
            ///     MessageId: StatusKdcCertRevoked
            ///     MessageText:
            ///     The domain controller certificate used for smartcard logon has been revoked. Please contact your system
            ///     administrator with the contents of your system event log.
            /// </summary>
            StatusKdcCertRevoked = 0xC000040F,

            /// <summary>
            ///     MessageId: StatusParameterQuotaExceeded
            ///     MessageText:
            ///     Data present in one of the parameters is more than the function can operate on.
            /// </summary>
            StatusParameterQuotaExceeded = 0xC0000410,

            /// <summary>
            ///     MessageId: StatusHibernationFailure
            ///     MessageText:
            ///     The system has failed to hibernate (The error code is %hs,. Hibernation will be disabled until the system is
            ///     restarted.
            /// </summary>
            StatusHibernationFailure = 0xC0000411,

            /// <summary>
            ///     MessageId: StatusDelayLoadFailed
            ///     MessageText:
            ///     An attempt to delay-load a .dll or get a function address in a delay-loaded .dll failed.
            /// </summary>
            StatusDelayLoadFailed = 0xC0000412,

            /// <summary>
            ///     MessageId: StatusAuthenticationFirewallFailed
            ///     MessageText:
            ///     Logon Failure: The machine you are logging onto is protected by an authentication firewall. The specified account
            ///     is not allowed to authenticate to the machine.
            /// </summary>
            StatusAuthenticationFirewallFailed = 0xC0000413,

            /// <summary>
            ///     MessageId: StatusVdmDisallowed
            ///     MessageText:
            ///     %hs is a 16-bit application. You do not have permissions to execute 16-bit applications. Check your permissions
            ///     with your system administrator.
            /// </summary>
            StatusVdmDisallowed = 0xC0000414,

            /// <summary>
            ///     MessageId: StatusHungDisplayDriverThread
            ///     MessageText:
            ///     {Display Driver Stopped Responding}
            ///     The %hs display driver has stopped working normally. Save your work and reboot the system to restore full display
            ///     functionality. The next time you reboot the machine a dialog will be displayed giving you a chance to report this
            ///     failure to Microsoft.
            /// </summary>
            StatusHungDisplayDriverThread = 0xC0000415,

            /// <summary>
            ///     MessageId: StatusInsufficientResourceForSpecifiedSharedSectionSize
            ///     MessageText:
            ///     The Desktop heap encountered an error while allocating session memory. There is more information in the system
            ///     event log.
            /// </summary>
            StatusInsufficientResourceForSpecifiedSharedSectionSize = 0xC0000416,

            /// <summary>
            ///     MessageId: StatusInvalidCruntimeParameter
            ///     MessageText:
            ///     An invalid parameter was passed to a C runtime function.
            /// </summary>
            StatusInvalidCruntimeParameter = 0xC0000417, // winnt

            /// <summary>
            ///     MessageId: StatusNtlmBlocked
            ///     MessageText:
            ///     The authentication failed since Ntlm was blocked.
            /// </summary>
            StatusNtlmBlocked = 0xC0000418,

            /// <summary>
            ///     MessageId: StatusDsSrcSidExistsInForest
            ///     MessageText:
            ///     The source object's Sid already exists in destination forest.
            /// </summary>
            StatusDsSrcSidExistsInForest = 0xC0000419,

            /// <summary>
            ///     MessageId: StatusDsDomainNameExistsInForest
            ///     MessageText:
            ///     The domain name of the trusted domain already exists in the forest.
            /// </summary>
            StatusDsDomainNameExistsInForest = 0xC000041A,

            /// <summary>
            ///     MessageId: StatusDsFlatNameExistsInForest
            ///     MessageText:
            ///     The flat name of the trusted domain already exists in the forest.
            /// </summary>
            StatusDsFlatNameExistsInForest = 0xC000041B,

            /// <summary>
            ///     MessageId: StatusInvalidUserPrincipalName
            ///     MessageText:
            ///     The User Principal Name (Upn, is invalid.
            /// </summary>
            StatusInvalidUserPrincipalName = 0xC000041C,

            /// <summary>
            ///     MessageId: StatusFatalUserCallbackException
            ///     MessageText:
            ///     An unhandled exception was encountered during a user callback.
            /// </summary>
            StatusFatalUserCallbackException = 0xC000041D,

            /// <summary>
            ///     MessageId: StatusAssertionFailure
            ///     MessageText:
            ///     An assertion failure has occurred.
            /// </summary>
            StatusAssertionFailure = 0xC0000420, // winnt

            /// <summary>
            ///     MessageId: StatusVerifierStop
            ///     MessageText:
            ///     Application verifier has found an error in the current process.
            /// </summary>
            StatusVerifierStop = 0xC0000421,

            /// <summary>
            ///     MessageId: StatusCallbackPopStack
            ///     MessageText:
            ///     An exception has occurred in a user mode callback and the kernel callback frame should be removed.
            /// </summary>
            StatusCallbackPopStack = 0xC0000423,

            /// <summary>
            ///     MessageId: StatusIncompatibleDriverBlocked
            ///     MessageText:
            ///     %2 has been blocked from loading due to incompatibility with this system. Please contact your software vendor for a
            ///     compatible version of the driver.
            /// </summary>
            StatusIncompatibleDriverBlocked = 0xC0000424,

            /// <summary>
            ///     MessageId: StatusHiveUnloaded
            ///     MessageText:
            ///     Illegal operation attempted on a registry key which has already been unloaded.
            /// </summary>
            StatusHiveUnloaded = 0xC0000425,

            /// <summary>
            ///     MessageId: StatusCompressionDisabled
            ///     MessageText:
            ///     Compression is disabled for this volume.
            /// </summary>
            StatusCompressionDisabled = 0xC0000426,

            /// <summary>
            ///     MessageId: StatusFileSystemLimitation
            ///     MessageText:
            ///     The requested operation could not be completed due to a file system limitation
            /// </summary>
            StatusFileSystemLimitation = 0xC0000427,

            /// <summary>
            ///     MessageId: StatusInvalidImageHash
            ///     MessageText:
            ///     Windows cannot verify the digital signature for this file. A recent hardware or software change might have
            ///     installed a file that is signed incorrectly or damaged, or that might be malicious software from an unknown source.
            /// </summary>
            StatusInvalidImageHash = 0xC0000428,

            /// <summary>
            ///     MessageId: StatusNotCapable
            ///     MessageText:
            ///     The implementation is not capable of performing the request.
            /// </summary>
            StatusNotCapable = 0xC0000429,

            /// <summary>
            ///     MessageId: StatusRequestOutOfSequence
            ///     MessageText:
            ///     The requested operation is out of order with respect to other operations.
            /// </summary>
            StatusRequestOutOfSequence = 0xC000042A,

            /// <summary>
            ///     MessageId: StatusImplementationLimit
            ///     MessageText:
            ///     An operation attempted to exceed an implementation-defined limit.
            /// </summary>
            StatusImplementationLimit = 0xC000042B,

            /// <summary>
            ///     MessageId: StatusElevationRequired
            ///     MessageText:
            ///     The requested operation requires elevation.
            /// </summary>
            StatusElevationRequired = 0xC000042C,

            /// <summary>
            ///     MessageId: StatusNoSecurityContext
            ///     MessageText:
            ///     The required security context does not exist.
            /// </summary>
            StatusNoSecurityContext = 0xC000042D,

            // MessageId = 0x042E is reserved and used in isolation lib as
            // MessageId== 0x042E Facility=System Severity=Error SymbolicName=StatusVersionParseError
            // Language=English
            // A version number could not be parsed.

            /// <summary>
            ///     MessageId: StatusPku2uCertFailure
            ///     MessageText:
            ///     The Pku2u protocol encountered an error while attempting to utilize the associated certificates.
            /// </summary>
            StatusPku2uCertFailure = 0xC000042F,

            /// <summary>
            ///     MessageId: StatusBeyondVdl
            ///     MessageText:
            ///     The operation was attempted beyond the valid data length of the file.
            /// </summary>
            StatusBeyondVdl = 0xC0000432,

            /// <summary>
            ///     MessageId: StatusEncounteredWriteInProgress
            ///     MessageText:
            ///     The attempted write operation encountered a write already in progress for some portion of the range.
            /// </summary>
            StatusEncounteredWriteInProgress = 0xC0000433,

            /// <summary>
            ///     MessageId: StatusPteChanged
            ///     MessageText:
            ///     The page fault mappings changed in the middle of processing a fault so the operation must be retried.
            /// </summary>
            StatusPteChanged = 0xC0000434,

            /// <summary>
            ///     MessageId: StatusPurgeFailed
            ///     MessageText:
            ///     The attempt to purge this file from memory failed to purge some or all the data from memory.
            /// </summary>
            StatusPurgeFailed = 0xC0000435,

            /// <summary>
            ///     MessageId: StatusCredRequiresConfirmation
            ///     MessageText:
            ///     The requested credential requires confirmation.
            /// </summary>
            StatusCredRequiresConfirmation = 0xC0000440,

            /// <summary>
            ///     MessageId: StatusCsEncryptionInvalidServerResponse
            ///     MessageText:
            ///     The remote server sent an invalid response for a file being opened with Client Side Encryption.
            /// </summary>
            StatusCsEncryptionInvalidServerResponse = 0xC0000441,

            /// <summary>
            ///     MessageId: StatusCsEncryptionUnsupportedServer
            ///     MessageText:
            ///     Client Side Encryption is not supported by the remote server even though it claims to support it.
            /// </summary>
            StatusCsEncryptionUnsupportedServer = 0xC0000442,

            /// <summary>
            ///     MessageId: StatusCsEncryptionExistingEncryptedFile
            ///     MessageText:
            ///     File is encrypted and should be opened in Client Side Encryption mode.
            /// </summary>
            StatusCsEncryptionExistingEncryptedFile = 0xC0000443,

            /// <summary>
            ///     MessageId: StatusCsEncryptionNewEncryptedFile
            ///     MessageText:
            ///     A new encrypted file is being created and a $Efs needs to be provided.
            /// </summary>
            StatusCsEncryptionNewEncryptedFile = 0xC0000444,

            /// <summary>
            ///     MessageId: StatusCsEncryptionFileNotCse
            ///     MessageText:
            ///     The Smb client requested a Cse Fsctl on a non-Cse file.
            /// </summary>
            StatusCsEncryptionFileNotCse = 0xC0000445,

            /// <summary>
            ///     MessageId: StatusInvalidLabel
            ///     MessageText:
            ///     Indicates a particular Security Id may not be assigned as the label of an object.
            /// </summary>
            StatusInvalidLabel = 0xC0000446,

            /// <summary>
            ///     MessageId: StatusDriverProcessTerminated
            ///     MessageText:
            ///     The process hosting the driver for this device has terminated.
            /// </summary>
            StatusDriverProcessTerminated = 0xC0000450,

            /// <summary>
            ///     MessageId: StatusAmbiguousSystemDevice
            ///     MessageText:
            ///     The requested system device cannot be identified due to multiple indistinguishable devices potentially matching the
            ///     identification criteria.
            /// </summary>
            StatusAmbiguousSystemDevice = 0xC0000451,

            /// <summary>
            ///     MessageId: StatusSystemDeviceNotFound
            ///     MessageText:
            ///     The requested system device cannot be found.
            /// </summary>
            StatusSystemDeviceNotFound = 0xC0000452,

            /// <summary>
            ///     MessageId: StatusRestartBootApplication
            ///     MessageText:
            ///     This boot application must be restarted.
            /// </summary>
            StatusRestartBootApplication = 0xC0000453,

            /// <summary>
            ///     MessageId: StatusInsufficientNvramResources
            ///     MessageText:
            ///     Insufficient Nvram resources exist to complete the Api.  A reboot might be required.
            /// </summary>
            StatusInsufficientNvramResources = 0xC0000454,

            /// <summary>
            ///     MessageId: StatusInvalidTaskName
            ///     MessageText:
            ///     The specified task name is invalid.
            /// </summary>
            StatusInvalidTaskName = 0xC0000500,

            /// <summary>
            ///     MessageId: StatusInvalidTaskIndex
            ///     MessageText:
            ///     The specified task index is invalid.
            /// </summary>
            StatusInvalidTaskIndex = 0xC0000501,

            /// <summary>
            ///     MessageId: StatusThreadAlreadyInTask
            ///     MessageText:
            ///     The specified thread is already joining a task.
            /// </summary>
            StatusThreadAlreadyInTask = 0xC0000502,

            /// <summary>
            ///     MessageId: StatusCallbackBypass
            ///     MessageText:
            ///     A callback has requested to bypass native code.
            /// </summary>
            StatusCallbackBypass = 0xC0000503,

            /// <summary>
            ///     MessageId: StatusFailFastException
            ///     MessageText:
            ///     {Fail Fast Exception}
            ///     A fail fast exception occurred. Exception handlers will not be invoked and the process will be terminated
            ///     immediately.
            /// </summary>
            StatusFailFastException = 0xC0000602,

            /// <summary>
            ///     MessageId: StatusImageCertRevoked
            ///     MessageText:
            ///     Windows cannot verify the digital signature for this file. The signing certificate for this file has been revoked.
            /// </summary>
            StatusImageCertRevoked = 0xC0000603,

            /// <summary>
            ///     MessageId: StatusPortClosed
            ///     MessageText:
            ///     The Alpc port is closed.
            /// </summary>
            StatusPortClosed = 0xC0000700,

            /// <summary>
            ///     MessageId: StatusMessageLost
            ///     MessageText:
            ///     The Alpc message requested is no longer available.
            /// </summary>
            StatusMessageLost = 0xC0000701,

            /// <summary>
            ///     MessageId: StatusInvalidMessage
            ///     MessageText:
            ///     The Alpc message supplied is invalid.
            /// </summary>
            StatusInvalidMessage = 0xC0000702,

            /// <summary>
            ///     MessageId: StatusRequestCanceled
            ///     MessageText:
            ///     The Alpc message has been canceled.
            /// </summary>
            StatusRequestCanceled = 0xC0000703,

            /// <summary>
            ///     MessageId: StatusRecursiveDispatch
            ///     MessageText:
            ///     Invalid recursive dispatch attempt.
            /// </summary>
            StatusRecursiveDispatch = 0xC0000704,

            /// <summary>
            ///     MessageId: StatusLpcReceiveBufferExpected
            ///     MessageText:
            ///     No receive buffer has been supplied in a synchrounus request.
            /// </summary>
            StatusLpcReceiveBufferExpected = 0xC0000705,

            /// <summary>
            ///     MessageId: StatusLpcInvalidConnectionUsage
            ///     MessageText:
            ///     The connection port is used in an invalid context.
            /// </summary>
            StatusLpcInvalidConnectionUsage = 0xC0000706,

            /// <summary>
            ///     MessageId: StatusLpcRequestsNotAllowed
            ///     MessageText:
            ///     The Alpc port does not accept new request messages.
            /// </summary>
            StatusLpcRequestsNotAllowed = 0xC0000707,

            /// <summary>
            ///     MessageId: StatusResourceInUse
            ///     MessageText:
            ///     The resource requested is already in use.
            /// </summary>
            StatusResourceInUse = 0xC0000708,

            /// <summary>
            ///     MessageId: StatusHardwareMemoryError
            ///     MessageText:
            ///     The hardware has reported an uncorrectable memory error.
            /// </summary>
            StatusHardwareMemoryError = 0xC0000709,

            /// <summary>
            ///     MessageId: StatusThreadpoolHandleException
            ///     MessageText:
            ///     Status = 0x%08x was returned, waiting on handle = 0x%x for wait = 0x%p, in waiter = 0x%p.
            /// </summary>
            StatusThreadpoolHandleException = 0xC000070A,

            /// <summary>
            ///     MessageId: StatusThreadpoolSetEventOnCompletionFailed
            ///     MessageText:
            ///     After a callback to = 0x%p(= 0x%p,, a completion call to SetEvent(= 0x%p, failed with status = 0x%08x.
            /// </summary>
            StatusThreadpoolSetEventOnCompletionFailed = 0xC000070B,

            /// <summary>
            ///     MessageId: StatusThreadpoolReleaseSemaphoreOnCompletionFailed
            ///     MessageText:
            ///     After a callback to = 0x%p(= 0x%p,, a completion call to ReleaseSemaphore(= 0x%p, %d, failed with status = 0x%08x.
            /// </summary>
            StatusThreadpoolReleaseSemaphoreOnCompletionFailed = 0xC000070C,

            /// <summary>
            ///     MessageId: StatusThreadpoolReleaseMutexOnCompletionFailed
            ///     MessageText:
            ///     After a callback to = 0x%p(= 0x%p,, a completion call to ReleaseMutex(%p, failed with status = 0x%08x.
            /// </summary>
            StatusThreadpoolReleaseMutexOnCompletionFailed = 0xC000070D,

            /// <summary>
            ///     MessageId: StatusThreadpoolFreeLibraryOnCompletionFailed
            ///     MessageText:
            ///     After a callback to = 0x%p(= 0x%p,, an completion call to FreeLibrary(%p, failed with status = 0x%08x.
            /// </summary>
            StatusThreadpoolFreeLibraryOnCompletionFailed = 0xC000070E,

            /// <summary>
            ///     MessageId: StatusThreadpoolReleasedDuringOperation
            ///     MessageText:
            ///     The threadpool = 0x%p was released while a thread was posting a callback to = 0x%p(= 0x%p, to it.
            /// </summary>
            StatusThreadpoolReleasedDuringOperation = 0xC000070F,

            /// <summary>
            ///     MessageId: StatusCallbackReturnedWhileImpersonating
            ///     MessageText:
            ///     A threadpool worker thread is impersonating a client, after a callback to = 0x%p(= 0x%p,.
            ///     This is unexpected, indicating that the callback is missing a call to revert the impersonation.
            /// </summary>
            StatusCallbackReturnedWhileImpersonating = 0xC0000710,

            /// <summary>
            ///     MessageId: StatusApcReturnedWhileImpersonating
            ///     MessageText:
            ///     A threadpool worker thread is impersonating a client, after executing an Apc.
            ///     This is unexpected, indicating that the Apc is missing a call to revert the impersonation.
            /// </summary>
            StatusApcReturnedWhileImpersonating = 0xC0000711,

            /// <summary>
            ///     MessageId: StatusProcessIsProtected
            ///     MessageText:
            ///     Either the target process, or the target thread's containing process, is a protected process.
            /// </summary>
            StatusProcessIsProtected = 0xC0000712,

            /// <summary>
            ///     MessageId: StatusMcaException
            ///     MessageText:
            ///     A Thread is getting dispatched with Mca Exception because of Mca.
            /// </summary>
            StatusMcaException = 0xC0000713,

            /// <summary>
            ///     MessageId: StatusCertificateMappingNotUnique
            ///     MessageText:
            ///     The client certificate account mapping is not unique.
            /// </summary>
            StatusCertificateMappingNotUnique = 0xC0000714,

            /// <summary>
            ///     MessageId: StatusSymlinkClassDisabled
            ///     MessageText:
            ///     The symbolic link cannot be followed because its type is disabled.
            /// </summary>
            StatusSymlinkClassDisabled = 0xC0000715,

            /// <summary>
            ///     MessageId: StatusInvalidIdnNormalization
            ///     MessageText:
            ///     Indicates that the specified string is not valid for Idn normalization.
            /// </summary>
            StatusInvalidIdnNormalization = 0xC0000716,

            /// <summary>
            ///     MessageId: StatusNoUnicodeTranslation
            ///     MessageText:
            ///     No mapping for the Unicode character exists in the target multi-byte code page.
            /// </summary>
            StatusNoUnicodeTranslation = 0xC0000717,

            /// <summary>
            ///     MessageId: StatusAlreadyRegistered
            ///     MessageText:
            ///     The provided callback is already registered.
            /// </summary>
            StatusAlreadyRegistered = 0xC0000718,

            /// <summary>
            ///     MessageId: StatusContextMismatch
            ///     MessageText:
            ///     The provided context did not match the target.
            /// </summary>
            StatusContextMismatch = 0xC0000719,

            /// <summary>
            ///     MessageId: StatusPortAlreadyHasCompletionList
            ///     MessageText:
            ///     The specified port already has a completion list.
            /// </summary>
            StatusPortAlreadyHasCompletionList = 0xC000071A,

            /// <summary>
            ///     MessageId: StatusCallbackReturnedThreadPriority
            ///     MessageText:
            ///     A threadpool worker thread enter a callback at thread base priority = 0x%x and exited at priority = 0x%x.
            ///     This is unexpected, indicating that the callback missed restoring the priority.
            /// </summary>
            StatusCallbackReturnedThreadPriority = 0xC000071B,

            /// <summary>
            ///     MessageId: StatusInvalidThread
            ///     MessageText:
            ///     An invalid thread, handle %p, is specified for this operation. Possibly, a threadpool worker thread was specified.
            /// </summary>
            StatusInvalidThread = 0xC000071C,

            /// <summary>
            ///     MessageId: StatusCallbackReturnedTransaction
            ///     MessageText:
            ///     A threadpool worker thread enter a callback, which left transaction state.
            ///     This is unexpected, indicating that the callback missed clearing the transaction.
            /// </summary>
            StatusCallbackReturnedTransaction = 0xC000071D,

            /// <summary>
            ///     MessageId: StatusCallbackReturnedLdrLock
            ///     MessageText:
            ///     A threadpool worker thread enter a callback, which left the loader lock held.
            ///     This is unexpected, indicating that the callback missed releasing the lock.
            /// </summary>
            StatusCallbackReturnedLdrLock = 0xC000071E,

            /// <summary>
            ///     MessageId: StatusCallbackReturnedLang
            ///     MessageText:
            ///     A threadpool worker thread enter a callback, which left with preferred languages set.
            ///     This is unexpected, indicating that the callback missed clearing them.
            /// </summary>
            StatusCallbackReturnedLang = 0xC000071F,

            /// <summary>
            ///     MessageId: StatusCallbackReturnedPriBack
            ///     MessageText:
            ///     A threadpool worker thread enter a callback, which left with background priorities set.
            ///     This is unexpected, indicating that the callback missed restoring the original priorities.
            /// </summary>
            StatusCallbackReturnedPriBack = 0xC0000720,

            /// <summary>
            ///     MessageId: StatusCallbackReturnedThreadAffinity
            ///     MessageText:
            ///     A threadpool worker thread enter a callback at thread affinity %p and exited at affinity %p.
            ///     This is unexpected, indicating that the callback missed restoring the priority.
            /// </summary>
            StatusCallbackReturnedThreadAffinity = 0xC0000721,

            /// <summary>
            ///     MessageId: StatusDiskRepairDisabled
            ///     MessageText:
            ///     The attempted operation required self healing to be enabled.
            /// </summary>
            StatusDiskRepairDisabled = 0xC0000800,

            /// <summary>
            ///     MessageId: StatusDsDomainRenameInProgress
            ///     MessageText:
            ///     The Directory Service cannot perform the requested operation because a domain rename operation is in progress.
            /// </summary>
            StatusDsDomainRenameInProgress = 0xC0000801,

            /// <summary>
            ///     MessageId: StatusDiskQuotaExceeded
            ///     MessageText:
            ///     The requested file operation failed because the storage quota was exceeded.
            ///     To free up disk space, move files to a different location or delete unnecessary files. For more information,
            ///     contact your system administrator.
            /// </summary>
            StatusDiskQuotaExceeded = 0xC0000802,

            /// <summary>
            ///     MessageId: StatusDataLostRepair
            ///     MessageText:
            ///     Windows discovered a corruption in the file "%hs".
            ///     This file has now been repaired.
            ///     Please check if any data in the file was lost because of the corruption.
            /// </summary>
            StatusDataLostRepair = 0x80000803,

            /// <summary>
            ///     MessageId: StatusContentBlocked
            ///     MessageText:
            ///     The requested file operation failed because the storage policy blocks that type of file. For more information,
            ///     contact your system administrator.
            /// </summary>
            StatusContentBlocked = 0xC0000804,

            /// <summary>
            ///     MessageId: StatusBadClusters
            ///     MessageText:
            ///     The operation could not be completed due to bad clusters on disk.
            /// </summary>
            StatusBadClusters = 0xC0000805,

            /// <summary>
            ///     MessageId: StatusVolumeDirty
            ///     MessageText:
            ///     The operation could not be completed because the volume is dirty. Please run chkdsk and try again.
            /// </summary>
            StatusVolumeDirty = 0xC0000806,

            /// <summary>
            ///     MessageId: StatusFileCheckedOut
            ///     MessageText:
            ///     This file is checked out or locked for editing by another user.
            /// </summary>
            StatusFileCheckedOut = 0xC0000901,

            /// <summary>
            ///     MessageId: StatusCheckoutRequired
            ///     MessageText:
            ///     The file must be checked out before saving changes.
            /// </summary>
            StatusCheckoutRequired = 0xC0000902,

            /// <summary>
            ///     MessageId: StatusBadFileType
            ///     MessageText:
            ///     The file type being saved or retrieved has been blocked.
            /// </summary>
            StatusBadFileType = 0xC0000903,

            /// <summary>
            ///     MessageId: StatusFileTooLarge
            ///     MessageText:
            ///     The file size exceeds the limit allowed and cannot be saved.
            /// </summary>
            StatusFileTooLarge = 0xC0000904,

            /// <summary>
            ///     MessageId: StatusFormsAuthRequired
            ///     MessageText:
            ///     Access Denied. Before opening files in this location, you must first browse to the web site and select the option
            ///     to login automatically.
            /// </summary>
            StatusFormsAuthRequired = 0xC0000905,

            /// <summary>
            ///     MessageId: StatusVirusInfected
            ///     MessageText:
            ///     Operation did not complete successfully because the file contains a virus.
            /// </summary>
            StatusVirusInfected = 0xC0000906,

            /// <summary>
            ///     MessageId: StatusVirusDeleted
            ///     MessageText:
            ///     This file contains a virus and cannot be opened. Due to the nature of this virus, the file has been removed from
            ///     this location.
            /// </summary>
            StatusVirusDeleted = 0xC0000907,

            /// <summary>
            ///     MessageId: StatusBadMcfgTable
            ///     MessageText:
            ///     The resources required for this device conflict with the Mcfg table.
            /// </summary>
            StatusBadMcfgTable = 0xC0000908,

            /// <summary>
            ///     MessageId: StatusCannotBreakOplock
            ///     MessageText:
            ///     The operation did not complete successfully because it would cause an oplock to be broken. The caller has requested
            ///     that existing oplocks not be broken.
            /// </summary>
            StatusCannotBreakOplock = 0xC0000909,

            /// <summary>
            ///     MessageId: StatusWowAssertion
            ///     MessageText:
            ///     Wow Assertion Error.
            /// </summary>
            StatusWowAssertion = 0xC0009898,

            /// <summary>
            ///     MessageId: StatusInvalidSignature
            ///     MessageText:
            ///     The cryptographic signature is invalid.
            /// </summary>
            StatusInvalidSignature = 0xC000A000,

            /// <summary>
            ///     MessageId: StatusHmacNotSupported
            ///     MessageText:
            ///     The cryptographic provider does not support Hmac.
            /// </summary>
            StatusHmacNotSupported = 0xC000A001,

            /// <summary>
            ///     MessageId: StatusAuthTagMismatch
            ///     MessageText:
            ///     The computed authentication tag did not match the input authentication tag.
            /// </summary>
            StatusAuthTagMismatch = 0xC000A002,

            /*++

             MessageId's = 0xa010 - = 0xa07f (inclusive, are reserved for Tcpip errors.

            --*/

            /// <summary>
            ///     MessageId: StatusIpsecQueueOverflow
            ///     MessageText:
            ///     The Ipsec queue overflowed.
            /// </summary>
            StatusIpsecQueueOverflow = 0xC000A010,

            /// <summary>
            ///     MessageId: StatusNdQueueOverflow
            ///     MessageText:
            ///     The neighbor discovery queue overflowed.
            /// </summary>
            StatusNdQueueOverflow = 0xC000A011,

            /// <summary>
            ///     MessageId: StatusHoplimitExceeded
            ///     MessageText:
            ///     An Icmp hop limit exceeded error was received.
            /// </summary>
            StatusHoplimitExceeded = 0xC000A012,

            /// <summary>
            ///     MessageId: StatusProtocolNotSupported
            ///     MessageText:
            ///     The protocol is not installed on the local machine.
            /// </summary>
            StatusProtocolNotSupported = 0xC000A013,

            /// <summary>
            ///     MessageId: StatusFastpathRejected
            ///     MessageText:
            ///     An operation or data has been rejected while on the network fast path.
            /// </summary>
            StatusFastpathRejected = 0xC000A014,

            /*++

             MessageId's = 0xa014 - = 0xa07f (inclusive, are reserved for Tcpip errors.

            --*/

            /// <summary>
            ///     MessageId: StatusLostWritebehindDataNetworkDisconnected
            ///     MessageText:
            ///     {Delayed Write Failed}
            ///     Windows was unable to save all the data for the file %hs; the data has been lost.
            ///     This error may be caused by network connectivity issues. Please try to save this file elsewhere.
            /// </summary>
            StatusLostWritebehindDataNetworkDisconnected = 0xC000A080,

            /// <summary>
            ///     MessageId: StatusLostWritebehindDataNetworkServerError
            ///     MessageText:
            ///     {Delayed Write Failed}
            ///     Windows was unable to save all the data for the file %hs; the data has been lost.
            ///     This error was returned by the server on which the file exists. Please try to save this file elsewhere.
            /// </summary>
            StatusLostWritebehindDataNetworkServerError = 0xC000A081,

            /// <summary>
            ///     MessageId: StatusLostWritebehindDataLocalDiskError
            ///     MessageText:
            ///     {Delayed Write Failed}
            ///     Windows was unable to save all the data for the file %hs; the data has been lost.
            ///     This error may be caused if the device has been removed or the media is write-protected.
            /// </summary>
            StatusLostWritebehindDataLocalDiskError = 0xC000A082,

            /// <summary>
            ///     MessageId: StatusXmlParseError
            ///     MessageText:
            ///     Windows was unable to parse the requested Xml data.
            /// </summary>
            StatusXmlParseError = 0xC000A083,

            /// <summary>
            ///     MessageId: StatusXmldsigError
            ///     MessageText:
            ///     An error was encountered while processing an Xml digital signature.
            /// </summary>
            StatusXmldsigError = 0xC000A084,

            /// <summary>
            ///     MessageId: StatusWrongCompartment
            ///     MessageText:
            ///     Indicates that the caller made the connection request in the wrong routing compartment.
            /// </summary>
            StatusWrongCompartment = 0xC000A085,

            /// <summary>
            ///     MessageId: StatusAuthipFailure
            ///     MessageText:
            ///     Indicates that there was an AuthIP failure when attempting to connect to the remote host.
            /// </summary>
            StatusAuthipFailure = 0xC000A086,

            /// <summary>
            ///     MessageId: StatusDsOidMappedGroupCantHaveMembers
            ///     MessageText:
            ///     Oid mapped groups cannot have members.
            /// </summary>
            StatusDsOidMappedGroupCantHaveMembers = 0xC000A087,

            /// <summary>
            ///     MessageId: StatusDsOidNotFound
            ///     MessageText:
            ///     The specified Oid cannot be found.
            /// </summary>
            StatusDsOidNotFound = 0xC000A088,

            /*++

             MessageId's = 0xa100 - = 0xa120 (inclusive, are for the Smb Hash Generation Service.

            --*/

            /// <summary>
            ///     MessageId: StatusHashNotSupported
            ///     MessageText:
            ///     Hash generation for the specified version and hash type is not enabled on server.
            /// </summary>
            StatusHashNotSupported = 0xC000A100,

            /// <summary>
            ///     MessageId: StatusHashNotPresent
            ///     MessageText:
            ///     The hash requests is not present or not up to date with the current file contents.
            /// </summary>
            StatusHashNotPresent = 0xC000A101,

            // Debugger error values

            /// <summary>
            ///     MessageId: DbgNoStateChange
            ///     MessageText:
            ///     Debugger did not perform a state change.
            /// </summary>
            DbgNoStateChange = 0xC0010001,

            /// <summary>
            ///     MessageId: DbgAppNotIdle
            ///     MessageText:
            ///     Debugger has found the application is not idle.
            /// </summary>
            DbgAppNotIdle = 0xC0010002,

            // Rpc error values

            /// <summary>
            ///     MessageId: RpcNtInvalidStringBinding
            ///     MessageText:
            ///     The string binding is invalid.
            /// </summary>
            RpcNtInvalidStringBinding = 0xC0020001,

            /// <summary>
            ///     MessageId: RpcNtWrongKindOfBinding
            ///     MessageText:
            ///     The binding handle is not the correct type.
            /// </summary>
            RpcNtWrongKindOfBinding = 0xC0020002,

            /// <summary>
            ///     MessageId: RpcNtInvalidBinding
            ///     MessageText:
            ///     The binding handle is invalid.
            /// </summary>
            RpcNtInvalidBinding = 0xC0020003,

            /// <summary>
            ///     MessageId: RpcNtProtseqNotSupported
            ///     MessageText:
            ///     The Rpc protocol sequence is not supported.
            /// </summary>
            RpcNtProtseqNotSupported = 0xC0020004,

            /// <summary>
            ///     MessageId: RpcNtInvalidRpcProtseq
            ///     MessageText:
            ///     The Rpc protocol sequence is invalid.
            /// </summary>
            RpcNtInvalidRpcProtseq = 0xC0020005,

            /// <summary>
            ///     MessageId: RpcNtInvalidStringUuid
            ///     MessageText:
            ///     The string Uuid is invalid.
            /// </summary>
            RpcNtInvalidStringUuid = 0xC0020006,

            /// <summary>
            ///     MessageId: RpcNtInvalidEndpointFormat
            ///     MessageText:
            ///     The endpoint format is invalid.
            /// </summary>
            RpcNtInvalidEndpointFormat = 0xC0020007,

            /// <summary>
            ///     MessageId: RpcNtInvalidNetAddr
            ///     MessageText:
            ///     The network address is invalid.
            /// </summary>
            RpcNtInvalidNetAddr = 0xC0020008,

            /// <summary>
            ///     MessageId: RpcNtNoEndpointFound
            ///     MessageText:
            ///     No endpoint was found.
            /// </summary>
            RpcNtNoEndpointFound = 0xC0020009,

            /// <summary>
            ///     MessageId: RpcNtInvalidTimeout
            ///     MessageText:
            ///     The timeout value is invalid.
            /// </summary>
            RpcNtInvalidTimeout = 0xC002000A,

            /// <summary>
            ///     MessageId: RpcNtObjectNotFound
            ///     MessageText:
            ///     The object Uuid was not found.
            /// </summary>
            RpcNtObjectNotFound = 0xC002000B,

            /// <summary>
            ///     MessageId: RpcNtAlreadyRegistered
            ///     MessageText:
            ///     The object Uuid has already been registered.
            /// </summary>
            RpcNtAlreadyRegistered = 0xC002000C,

            /// <summary>
            ///     MessageId: RpcNtTypeAlreadyRegistered
            ///     MessageText:
            ///     The type Uuid has already been registered.
            /// </summary>
            RpcNtTypeAlreadyRegistered = 0xC002000D,

            /// <summary>
            ///     MessageId: RpcNtAlreadyListening
            ///     MessageText:
            ///     The Rpc server is already listening.
            /// </summary>
            RpcNtAlreadyListening = 0xC002000E,

            /// <summary>
            ///     MessageId: RpcNtNoProtseqsRegistered
            ///     MessageText:
            ///     No protocol sequences have been registered.
            /// </summary>
            RpcNtNoProtseqsRegistered = 0xC002000F,

            /// <summary>
            ///     MessageId: RpcNtNotListening
            ///     MessageText:
            ///     The Rpc server is not listening.
            /// </summary>
            RpcNtNotListening = 0xC0020010,

            /// <summary>
            ///     MessageId: RpcNtUnknownMgrType
            ///     MessageText:
            ///     The manager type is unknown.
            /// </summary>
            RpcNtUnknownMgrType = 0xC0020011,

            /// <summary>
            ///     MessageId: RpcNtUnknownIf
            ///     MessageText:
            ///     The interface is unknown.
            /// </summary>
            RpcNtUnknownIf = 0xC0020012,

            /// <summary>
            ///     MessageId: RpcNtNoBindings
            ///     MessageText:
            ///     There are no bindings.
            /// </summary>
            RpcNtNoBindings = 0xC0020013,

            /// <summary>
            ///     MessageId: RpcNtNoProtseqs
            ///     MessageText:
            ///     There are no protocol sequences.
            /// </summary>
            RpcNtNoProtseqs = 0xC0020014,

            /// <summary>
            ///     MessageId: RpcNtCantCreateEndpoint
            ///     MessageText:
            ///     The endpoint cannot be created.
            /// </summary>
            RpcNtCantCreateEndpoint = 0xC0020015,

            /// <summary>
            ///     MessageId: RpcNtOutOfResources
            ///     MessageText:
            ///     Not enough resources are available to complete this operation.
            /// </summary>
            RpcNtOutOfResources = 0xC0020016,

            /// <summary>
            ///     MessageId: RpcNtServerUnavailable
            ///     MessageText:
            ///     The Rpc server is unavailable.
            /// </summary>
            RpcNtServerUnavailable = 0xC0020017,

            /// <summary>
            ///     MessageId: RpcNtServerTooBusy
            ///     MessageText:
            ///     The Rpc server is too busy to complete this operation.
            /// </summary>
            RpcNtServerTooBusy = 0xC0020018,

            /// <summary>
            ///     MessageId: RpcNtInvalidNetworkOptions
            ///     MessageText:
            ///     The network options are invalid.
            /// </summary>
            RpcNtInvalidNetworkOptions = 0xC0020019,

            /// <summary>
            ///     MessageId: RpcNtNoCallActive
            ///     MessageText:
            ///     There are no remote procedure calls active on this thread.
            /// </summary>
            RpcNtNoCallActive = 0xC002001A,

            /// <summary>
            ///     MessageId: RpcNtCallFailed
            ///     MessageText:
            ///     The remote procedure call failed.
            /// </summary>
            RpcNtCallFailed = 0xC002001B,

            /// <summary>
            ///     MessageId: RpcNtCallFailedDne
            ///     MessageText:
            ///     The remote procedure call failed and did not execute.
            /// </summary>
            RpcNtCallFailedDne = 0xC002001C,

            /// <summary>
            ///     MessageId: RpcNtProtocolError
            ///     MessageText:
            ///     An Rpc protocol error occurred.
            /// </summary>
            RpcNtProtocolError = 0xC002001D,

            /// <summary>
            ///     MessageId: RpcNtUnsupportedTransSyn
            ///     MessageText:
            ///     The transfer syntax is not supported by the Rpc server.
            /// </summary>
            RpcNtUnsupportedTransSyn = 0xC002001F,

            /// <summary>
            ///     MessageId: RpcNtUnsupportedType
            ///     MessageText:
            ///     The type Uuid is not supported.
            /// </summary>
            RpcNtUnsupportedType = 0xC0020021,

            /// <summary>
            ///     MessageId: RpcNtInvalidTag
            ///     MessageText:
            ///     The tag is invalid.
            /// </summary>
            RpcNtInvalidTag = 0xC0020022,

            /// <summary>
            ///     MessageId: RpcNtInvalidBound
            ///     MessageText:
            ///     The array bounds are invalid.
            /// </summary>
            RpcNtInvalidBound = 0xC0020023,

            /// <summary>
            ///     MessageId: RpcNtNoEntryName
            ///     MessageText:
            ///     The binding does not contain an entry name.
            /// </summary>
            RpcNtNoEntryName = 0xC0020024,

            /// <summary>
            ///     MessageId: RpcNtInvalidNameSyntax
            ///     MessageText:
            ///     The name syntax is invalid.
            /// </summary>
            RpcNtInvalidNameSyntax = 0xC0020025,

            /// <summary>
            ///     MessageId: RpcNtUnsupportedNameSyntax
            ///     MessageText:
            ///     The name syntax is not supported.
            /// </summary>
            RpcNtUnsupportedNameSyntax = 0xC0020026,

            /// <summary>
            ///     MessageId: RpcNtUuidNoAddress
            ///     MessageText:
            ///     No network address is available to use to construct a Uuid.
            /// </summary>
            RpcNtUuidNoAddress = 0xC0020028,

            /// <summary>
            ///     MessageId: RpcNtDuplicateEndpoint
            ///     MessageText:
            ///     The endpoint is a duplicate.
            /// </summary>
            RpcNtDuplicateEndpoint = 0xC0020029,

            /// <summary>
            ///     MessageId: RpcNtUnknownAuthnType
            ///     MessageText:
            ///     The authentication type is unknown.
            /// </summary>
            RpcNtUnknownAuthnType = 0xC002002A,

            /// <summary>
            ///     MessageId: RpcNtMaxCallsTooSmall
            ///     MessageText:
            ///     The maximum number of calls is too small.
            /// </summary>
            RpcNtMaxCallsTooSmall = 0xC002002B,

            /// <summary>
            ///     MessageId: RpcNtStringTooLong
            ///     MessageText:
            ///     The string is too long.
            /// </summary>
            RpcNtStringTooLong = 0xC002002C,

            /// <summary>
            ///     MessageId: RpcNtProtseqNotFound
            ///     MessageText:
            ///     The Rpc protocol sequence was not found.
            /// </summary>
            RpcNtProtseqNotFound = 0xC002002D,

            /// <summary>
            ///     MessageId: RpcNtProcnumOutOfRange
            ///     MessageText:
            ///     The procedure number is out of range.
            /// </summary>
            RpcNtProcnumOutOfRange = 0xC002002E,

            /// <summary>
            ///     MessageId: RpcNtBindingHasNoAuth
            ///     MessageText:
            ///     The binding does not contain any authentication information.
            /// </summary>
            RpcNtBindingHasNoAuth = 0xC002002F,

            /// <summary>
            ///     MessageId: RpcNtUnknownAuthnService
            ///     MessageText:
            ///     The authentication service is unknown.
            /// </summary>
            RpcNtUnknownAuthnService = 0xC0020030,

            /// <summary>
            ///     MessageId: RpcNtUnknownAuthnLevel
            ///     MessageText:
            ///     The authentication level is unknown.
            /// </summary>
            RpcNtUnknownAuthnLevel = 0xC0020031,

            /// <summary>
            ///     MessageId: RpcNtInvalidAuthIdentity
            ///     MessageText:
            ///     The security context is invalid.
            /// </summary>
            RpcNtInvalidAuthIdentity = 0xC0020032,

            /// <summary>
            ///     MessageId: RpcNtUnknownAuthzService
            ///     MessageText:
            ///     The authorization service is unknown.
            /// </summary>
            RpcNtUnknownAuthzService = 0xC0020033,

            /// <summary>
            ///     MessageId: EptNtInvalidEntry
            ///     MessageText:
            ///     The entry is invalid.
            /// </summary>
            EptNtInvalidEntry = 0xC0020034,

            /// <summary>
            ///     MessageId: EptNtCantPerformOp
            ///     MessageText:
            ///     The operation cannot be performed.
            /// </summary>
            EptNtCantPerformOp = 0xC0020035,

            /// <summary>
            ///     MessageId: EptNtNotRegistered
            ///     MessageText:
            ///     There are no more endpoints available from the endpoint mapper.
            /// </summary>
            EptNtNotRegistered = 0xC0020036,

            /// <summary>
            ///     MessageId: RpcNtNothingToExport
            ///     MessageText:
            ///     No interfaces have been exported.
            /// </summary>
            RpcNtNothingToExport = 0xC0020037,

            /// <summary>
            ///     MessageId: RpcNtIncompleteName
            ///     MessageText:
            ///     The entry name is incomplete.
            /// </summary>
            RpcNtIncompleteName = 0xC0020038,

            /// <summary>
            ///     MessageId: RpcNtInvalidVersOption
            ///     MessageText:
            ///     The version option is invalid.
            /// </summary>
            RpcNtInvalidVersOption = 0xC0020039,

            /// <summary>
            ///     MessageId: RpcNtNoMoreMembers
            ///     MessageText:
            ///     There are no more members.
            /// </summary>
            RpcNtNoMoreMembers = 0xC002003A,

            /// <summary>
            ///     MessageId: RpcNtNotAllObjsUnexported
            ///     MessageText:
            ///     There is nothing to unexport.
            /// </summary>
            RpcNtNotAllObjsUnexported = 0xC002003B,

            /// <summary>
            ///     MessageId: RpcNtInterfaceNotFound
            ///     MessageText:
            ///     The interface was not found.
            /// </summary>
            RpcNtInterfaceNotFound = 0xC002003C,

            /// <summary>
            ///     MessageId: RpcNtEntryAlreadyExists
            ///     MessageText:
            ///     The entry already exists.
            /// </summary>
            RpcNtEntryAlreadyExists = 0xC002003D,

            /// <summary>
            ///     MessageId: RpcNtEntryNotFound
            ///     MessageText:
            ///     The entry is not found.
            /// </summary>
            RpcNtEntryNotFound = 0xC002003E,

            /// <summary>
            ///     MessageId: RpcNtNameServiceUnavailable
            ///     MessageText:
            ///     The name service is unavailable.
            /// </summary>
            RpcNtNameServiceUnavailable = 0xC002003F,

            /// <summary>
            ///     MessageId: RpcNtInvalidNafId
            ///     MessageText:
            ///     The network address family is invalid.
            /// </summary>
            RpcNtInvalidNafId = 0xC0020040,

            /// <summary>
            ///     MessageId: RpcNtCannotSupport
            ///     MessageText:
            ///     The requested operation is not supported.
            /// </summary>
            RpcNtCannotSupport = 0xC0020041,

            /// <summary>
            ///     MessageId: RpcNtNoContextAvailable
            ///     MessageText:
            ///     No security context is available to allow impersonation.
            /// </summary>
            RpcNtNoContextAvailable = 0xC0020042,

            /// <summary>
            ///     MessageId: RpcNtInternalError
            ///     MessageText:
            ///     An internal error occurred in Rpc.
            /// </summary>
            RpcNtInternalError = 0xC0020043,

            /// <summary>
            ///     MessageId: RpcNtZeroDivide
            ///     MessageText:
            ///     The Rpc server attempted an integer divide by zero.
            /// </summary>
            RpcNtZeroDivide = 0xC0020044,

            /// <summary>
            ///     MessageId: RpcNtAddressError
            ///     MessageText:
            ///     An addressing error occurred in the Rpc server.
            /// </summary>
            RpcNtAddressError = 0xC0020045,

            /// <summary>
            ///     MessageId: RpcNtFpDivZero
            ///     MessageText:
            ///     A floating point operation at the Rpc server caused a divide by zero.
            /// </summary>
            RpcNtFpDivZero = 0xC0020046,

            /// <summary>
            ///     MessageId: RpcNtFpUnderflow
            ///     MessageText:
            ///     A floating point underflow occurred at the Rpc server.
            /// </summary>
            RpcNtFpUnderflow = 0xC0020047,

            /// <summary>
            ///     MessageId: RpcNtFpOverflow
            ///     MessageText:
            ///     A floating point overflow occurred at the Rpc server.
            /// </summary>
            RpcNtFpOverflow = 0xC0020048,

            /// <summary>
            ///     MessageId: RpcNtNoMoreEntries
            ///     MessageText:
            ///     The list of Rpc servers available for auto-handle binding has been exhausted.
            /// </summary>
            RpcNtNoMoreEntries = 0xC0030001,

            /// <summary>
            ///     MessageId: RpcNtSsCharTransOpenFail
            ///     MessageText:
            ///     The file designated by Dcerpcchartrans cannot be opened.
            /// </summary>
            RpcNtSsCharTransOpenFail = 0xC0030002,

            /// <summary>
            ///     MessageId: RpcNtSsCharTransShortFile
            ///     MessageText:
            ///     The file containing the character translation table has fewer than 512 bytes.
            /// </summary>
            RpcNtSsCharTransShortFile = 0xC0030003,

            /// <summary>
            ///     MessageId: RpcNtSsInNullContext
            ///     MessageText:
            ///     A null context handle is passed as an [in] parameter.
            /// </summary>
            RpcNtSsInNullContext = 0xC0030004,

            /// <summary>
            ///     MessageId: RpcNtSsContextMismatch
            ///     MessageText:
            ///     The context handle does not match any known context handles.
            /// </summary>
            RpcNtSsContextMismatch = 0xC0030005,

            /// <summary>
            ///     MessageId: RpcNtSsContextDamaged
            ///     MessageText:
            ///     The context handle changed during a call.
            /// </summary>
            RpcNtSsContextDamaged = 0xC0030006,

            /// <summary>
            ///     MessageId: RpcNtSsHandlesMismatch
            ///     MessageText:
            ///     The binding handles passed to a remote procedure call do not match.
            /// </summary>
            RpcNtSsHandlesMismatch = 0xC0030007,

            /// <summary>
            ///     MessageId: RpcNtSsCannotGetCallHandle
            ///     MessageText:
            ///     The stub is unable to get the call handle.
            /// </summary>
            RpcNtSsCannotGetCallHandle = 0xC0030008,

            /// <summary>
            ///     MessageId: RpcNtNullRefPointer
            ///     MessageText:
            ///     A null reference pointer was passed to the stub.
            /// </summary>
            RpcNtNullRefPointer = 0xC0030009,

            /// <summary>
            ///     MessageId: RpcNtEnumValueOutOfRange
            ///     MessageText:
            ///     The enumeration value is out of range.
            /// </summary>
            RpcNtEnumValueOutOfRange = 0xC003000A,

            /// <summary>
            ///     MessageId: RpcNtByteCountTooSmall
            ///     MessageText:
            ///     The byte count is too small.
            /// </summary>
            RpcNtByteCountTooSmall = 0xC003000B,

            /// <summary>
            ///     MessageId: RpcNtBadStubData
            ///     MessageText:
            ///     The stub received bad data.
            /// </summary>
            RpcNtBadStubData = 0xC003000C,

            /// <summary>
            ///     MessageId: RpcNtCallInProgress
            ///     MessageText:
            ///     A remote procedure call is already in progress for this thread.
            /// </summary>
            RpcNtCallInProgress = 0xC0020049,

            /// <summary>
            ///     MessageId: RpcNtNoMoreBindings
            ///     MessageText:
            ///     There are no more bindings.
            /// </summary>
            RpcNtNoMoreBindings = 0xC002004A,

            /// <summary>
            ///     MessageId: RpcNtGroupMemberNotFound
            ///     MessageText:
            ///     The group member was not found.
            /// </summary>
            RpcNtGroupMemberNotFound = 0xC002004B,

            /// <summary>
            ///     MessageId: EptNtCantCreate
            ///     MessageText:
            ///     The endpoint mapper database entry could not be created.
            /// </summary>
            EptNtCantCreate = 0xC002004C,

            /// <summary>
            ///     MessageId: RpcNtInvalidObject
            ///     MessageText:
            ///     The object Uuid is the nil Uuid.
            /// </summary>
            RpcNtInvalidObject = 0xC002004D,

            /// <summary>
            ///     MessageId: RpcNtNoInterfaces
            ///     MessageText:
            ///     No interfaces have been registered.
            /// </summary>
            RpcNtNoInterfaces = 0xC002004F,

            /// <summary>
            ///     MessageId: RpcNtCallCancelled
            ///     MessageText:
            ///     The remote procedure call was cancelled.
            /// </summary>
            RpcNtCallCancelled = 0xC0020050,

            /// <summary>
            ///     MessageId: RpcNtBindingIncomplete
            ///     MessageText:
            ///     The binding handle does not contain all required information.
            /// </summary>
            RpcNtBindingIncomplete = 0xC0020051,

            /// <summary>
            ///     MessageId: RpcNtCommFailure
            ///     MessageText:
            ///     A communications failure occurred during a remote procedure call.
            /// </summary>
            RpcNtCommFailure = 0xC0020052,

            /// <summary>
            ///     MessageId: RpcNtUnsupportedAuthnLevel
            ///     MessageText:
            ///     The requested authentication level is not supported.
            /// </summary>
            RpcNtUnsupportedAuthnLevel = 0xC0020053,

            /// <summary>
            ///     MessageId: RpcNtNoPrincName
            ///     MessageText:
            ///     No principal name registered.
            /// </summary>
            RpcNtNoPrincName = 0xC0020054,

            /// <summary>
            ///     MessageId: RpcNtNotRpcError
            ///     MessageText:
            ///     The error specified is not a valid Windows Rpc error code.
            /// </summary>
            RpcNtNotRpcError = 0xC0020055,

            /// <summary>
            ///     MessageId: RpcNtUuidLocalOnly
            ///     MessageText:
            ///     A Uuid that is valid only on this computer has been allocated.
            /// </summary>
            RpcNtUuidLocalOnly = 0x40020056,

            /// <summary>
            ///     MessageId: RpcNtSecPkgError
            ///     MessageText:
            ///     A security package specific error occurred.
            /// </summary>
            RpcNtSecPkgError = 0xC0020057,

            /// <summary>
            ///     MessageId: RpcNtNotCancelled
            ///     MessageText:
            ///     Thread is not cancelled.
            /// </summary>
            RpcNtNotCancelled = 0xC0020058,

            /// <summary>
            ///     MessageId: RpcNtInvalidEsAction
            ///     MessageText:
            ///     Invalid operation on the encoding/decoding handle.
            /// </summary>
            RpcNtInvalidEsAction = 0xC0030059,

            /// <summary>
            ///     MessageId: RpcNtWrongEsVersion
            ///     MessageText:
            ///     Incompatible version of the serializing package.
            /// </summary>
            RpcNtWrongEsVersion = 0xC003005A,

            /// <summary>
            ///     MessageId: RpcNtWrongStubVersion
            ///     MessageText:
            ///     Incompatible version of the Rpc stub.
            /// </summary>
            RpcNtWrongStubVersion = 0xC003005B,

            /// <summary>
            ///     MessageId: RpcNtInvalidPipeObject
            ///     MessageText:
            ///     The Rpc pipe object is invalid or corrupted.
            /// </summary>
            RpcNtInvalidPipeObject = 0xC003005C,

            /// <summary>
            ///     MessageId: RpcNtInvalidPipeOperation
            ///     MessageText:
            ///     An invalid operation was attempted on an Rpc pipe object.
            /// </summary>
            RpcNtInvalidPipeOperation = 0xC003005D,

            /// <summary>
            ///     MessageId: RpcNtWrongPipeVersion
            ///     MessageText:
            ///     Unsupported Rpc pipe version.
            /// </summary>
            RpcNtWrongPipeVersion = 0xC003005E,

            /// <summary>
            ///     MessageId: RpcNtPipeClosed
            ///     MessageText:
            ///     The Rpc pipe object has already been closed.
            /// </summary>
            RpcNtPipeClosed = 0xC003005F,

            /// <summary>
            ///     MessageId: RpcNtPipeDisciplineError
            ///     MessageText:
            ///     The Rpc call completed before all pipes were processed.
            /// </summary>
            RpcNtPipeDisciplineError = 0xC0030060,

            /// <summary>
            ///     MessageId: RpcNtPipeEmpty
            ///     MessageText:
            ///     No more data is available from the Rpc pipe.
            /// </summary>
            RpcNtPipeEmpty = 0xC0030061,

            /// <summary>
            ///     MessageId: RpcNtInvalidAsyncHandle
            ///     MessageText:
            ///     Invalid asynchronous remote procedure call handle.
            /// </summary>
            RpcNtInvalidAsyncHandle = 0xC0020062,

            /// <summary>
            ///     MessageId: RpcNtInvalidAsyncCall
            ///     MessageText:
            ///     Invalid asynchronous Rpc call handle for this operation.
            /// </summary>
            RpcNtInvalidAsyncCall = 0xC0020063,

            /// <summary>
            ///     MessageId: RpcNtProxyAccessDenied
            ///     MessageText:
            ///     Access to the Http proxy is denied.
            /// </summary>
            RpcNtProxyAccessDenied = 0xC0020064,

            /// <summary>
            ///     MessageId: RpcNtCookieAuthFailed
            ///     MessageText:
            ///     Http proxy server rejected the connection because the cookie authentication failed.
            /// </summary>
            RpcNtCookieAuthFailed = 0xC0020065,

            /// <summary>
            ///     MessageId: RpcNtSendIncomplete
            ///     MessageText:
            ///     Some data remains to be sent in the request buffer.
            /// </summary>
            RpcNtSendIncomplete = 0x400200AF,

            // Acpi error values

            /// <summary>
            ///     MessageId: StatusAcpiInvalidOpcode
            ///     MessageText:
            ///     An attempt was made to run an invalid Aml opcode
            /// </summary>
            StatusAcpiInvalidOpcode = 0xC0140001,

            /// <summary>
            ///     MessageId: StatusAcpiStackOverflow
            ///     MessageText:
            ///     The Aml Interpreter Stack has overflowed
            /// </summary>
            StatusAcpiStackOverflow = 0xC0140002,

            /// <summary>
            ///     MessageId: StatusAcpiAssertFailed
            ///     MessageText:
            ///     An inconsistent state has occurred
            /// </summary>
            StatusAcpiAssertFailed = 0xC0140003,

            /// <summary>
            ///     MessageId: StatusAcpiInvalidIndex
            ///     MessageText:
            ///     An attempt was made to access an array outside of its bounds
            /// </summary>
            StatusAcpiInvalidIndex = 0xC0140004,

            /// <summary>
            ///     MessageId: StatusAcpiInvalidArgument
            ///     MessageText:
            ///     A required argument was not specified
            /// </summary>
            StatusAcpiInvalidArgument = 0xC0140005,

            /// <summary>
            ///     MessageId: StatusAcpiFatal
            ///     MessageText:
            ///     A fatal error has occurred
            /// </summary>
            StatusAcpiFatal = 0xC0140006,

            /// <summary>
            ///     MessageId: StatusAcpiInvalidSupername
            ///     MessageText:
            ///     An invalid SuperName was specified
            /// </summary>
            StatusAcpiInvalidSupername = 0xC0140007,

            /// <summary>
            ///     MessageId: StatusAcpiInvalidArgtype
            ///     MessageText:
            ///     An argument with an incorrect type was specified
            /// </summary>
            StatusAcpiInvalidArgtype = 0xC0140008,

            /// <summary>
            ///     MessageId: StatusAcpiInvalidObjtype
            ///     MessageText:
            ///     An object with an incorrect type was specified
            /// </summary>
            StatusAcpiInvalidObjtype = 0xC0140009,

            /// <summary>
            ///     MessageId: StatusAcpiInvalidTargettype
            ///     MessageText:
            ///     A target with an incorrect type was specified
            /// </summary>
            StatusAcpiInvalidTargettype = 0xC014000A,

            /// <summary>
            ///     MessageId: StatusAcpiIncorrectArgumentCount
            ///     MessageText:
            ///     An incorrect number of arguments were specified
            /// </summary>
            StatusAcpiIncorrectArgumentCount = 0xC014000B,

            /// <summary>
            ///     MessageId: StatusAcpiAddressNotMapped
            ///     MessageText:
            ///     An address failed to translate
            /// </summary>
            StatusAcpiAddressNotMapped = 0xC014000C,

            /// <summary>
            ///     MessageId: StatusAcpiInvalidEventtype
            ///     MessageText:
            ///     An incorrect event type was specified
            /// </summary>
            StatusAcpiInvalidEventtype = 0xC014000D,

            /// <summary>
            ///     MessageId: StatusAcpiHandlerCollision
            ///     MessageText:
            ///     A handler for the target already exists
            /// </summary>
            StatusAcpiHandlerCollision = 0xC014000E,

            /// <summary>
            ///     MessageId: StatusAcpiInvalidData
            ///     MessageText:
            ///     Invalid data for the target was specified
            /// </summary>
            StatusAcpiInvalidData = 0xC014000F,

            /// <summary>
            ///     MessageId: StatusAcpiInvalidRegion
            ///     MessageText:
            ///     An invalid region for the target was specified
            /// </summary>
            StatusAcpiInvalidRegion = 0xC0140010,

            /// <summary>
            ///     MessageId: StatusAcpiInvalidAccessSize
            ///     MessageText:
            ///     An attempt was made to access a field outside of the defined range
            /// </summary>
            StatusAcpiInvalidAccessSize = 0xC0140011,

            /// <summary>
            ///     MessageId: StatusAcpiAcquireGlobalLock
            ///     MessageText:
            ///     The Global system lock could not be acquired
            /// </summary>
            StatusAcpiAcquireGlobalLock = 0xC0140012,

            /// <summary>
            ///     MessageId: StatusAcpiAlreadyInitialized
            ///     MessageText:
            ///     An attempt was made to reinitialize the Acpi subsystem
            /// </summary>
            StatusAcpiAlreadyInitialized = 0xC0140013,

            /// <summary>
            ///     MessageId: StatusAcpiNotInitialized
            ///     MessageText:
            ///     The Acpi subsystem has not been initialized
            /// </summary>
            StatusAcpiNotInitialized = 0xC0140014,

            /// <summary>
            ///     MessageId: StatusAcpiInvalidMutexLevel
            ///     MessageText:
            ///     An incorrect mutex was specified
            /// </summary>
            StatusAcpiInvalidMutexLevel = 0xC0140015,

            /// <summary>
            ///     MessageId: StatusAcpiMutexNotOwned
            ///     MessageText:
            ///     The mutex is not currently owned
            /// </summary>
            StatusAcpiMutexNotOwned = 0xC0140016,

            /// <summary>
            ///     MessageId: StatusAcpiMutexNotOwner
            ///     MessageText:
            ///     An attempt was made to access the mutex by a process that was not the owner
            /// </summary>
            StatusAcpiMutexNotOwner = 0xC0140017,

            /// <summary>
            ///     MessageId: StatusAcpiRsAccess
            ///     MessageText:
            ///     An error occurred during an access to Region Space
            /// </summary>
            StatusAcpiRsAccess = 0xC0140018,

            /// <summary>
            ///     MessageId: StatusAcpiInvalidTable
            ///     MessageText:
            ///     An attempt was made to use an incorrect table
            /// </summary>
            StatusAcpiInvalidTable = 0xC0140019,

            /// <summary>
            ///     MessageId: StatusAcpiRegHandlerFailed
            ///     MessageText:
            ///     The registration of an Acpi event failed
            /// </summary>
            StatusAcpiRegHandlerFailed = 0xC0140020,

            /// <summary>
            ///     MessageId: StatusAcpiPowerRequestFailed
            ///     MessageText:
            ///     An Acpi Power Object failed to transition state
            /// </summary>
            StatusAcpiPowerRequestFailed = 0xC0140021,

            // Terminal Server specific Errors

            /// <summary>
            ///     MessageId: StatusCtxWinstationNameInvalid
            ///     MessageText:
            ///     Session name %1 is invalid.
            /// </summary>
            StatusCtxWinstationNameInvalid = 0xC00A0001,

            /// <summary>
            ///     MessageId: StatusCtxInvalidPd
            ///     MessageText:
            ///     The protocol driver %1 is invalid.
            /// </summary>
            StatusCtxInvalidPd = 0xC00A0002,

            /// <summary>
            ///     MessageId: StatusCtxPdNotFound
            ///     MessageText:
            ///     The protocol driver %1 was not found in the system path.
            /// </summary>
            StatusCtxPdNotFound = 0xC00A0003,

            /// <summary>
            ///     MessageId: StatusCtxCdmConnect
            ///     MessageText:
            ///     The Client Drive Mapping Service Has Connected on Terminal Connection.
            /// </summary>
            StatusCtxCdmConnect = 0x400A0004,

            /// <summary>
            ///     MessageId: StatusCtxCdmDisconnect
            ///     MessageText:
            ///     The Client Drive Mapping Service Has Disconnected on Terminal Connection.
            /// </summary>
            StatusCtxCdmDisconnect = 0x400A0005,

            /// <summary>
            ///     MessageId: StatusCtxClosePending
            ///     MessageText:
            ///     A close operation is pending on the Terminal Connection.
            /// </summary>
            StatusCtxClosePending = 0xC00A0006,

            /// <summary>
            ///     MessageId: StatusCtxNoOutbuf
            ///     MessageText:
            ///     There are no free output buffers available.
            /// </summary>
            StatusCtxNoOutbuf = 0xC00A0007,

            /// <summary>
            ///     MessageId: StatusCtxModemInfNotFound
            ///     MessageText:
            ///     The Modem.Inf file was not found.
            /// </summary>
            StatusCtxModemInfNotFound = 0xC00A0008,

            /// <summary>
            ///     MessageId: StatusCtxInvalidModemname
            ///     MessageText:
            ///     The modem (%1, was not found in Modem.Inf.
            /// </summary>
            StatusCtxInvalidModemname = 0xC00A0009,

            /// <summary>
            ///     MessageId: StatusCtxResponseError
            ///     MessageText:
            ///     The modem did not accept the command sent to it.
            ///     Verify the configured modem name matches the attached modem.
            /// </summary>
            StatusCtxResponseError = 0xC00A000A,

            /// <summary>
            ///     MessageId: StatusCtxModemResponseTimeout
            ///     MessageText:
            ///     The modem did not respond to the command sent to it.
            ///     Verify the modem is properly cabled and powered on.
            /// </summary>
            StatusCtxModemResponseTimeout = 0xC00A000B,

            /// <summary>
            ///     MessageId: StatusCtxModemResponseNoCarrier
            ///     MessageText:
            ///     Carrier detect has failed or carrier has been dropped due to disconnect.
            /// </summary>
            StatusCtxModemResponseNoCarrier = 0xC00A000C,

            /// <summary>
            ///     MessageId: StatusCtxModemResponseNoDialtone
            ///     MessageText:
            ///     Dial tone not detected within required time.
            ///     Verify phone cable is properly attached and functional.
            /// </summary>
            StatusCtxModemResponseNoDialtone = 0xC00A000D,

            /// <summary>
            ///     MessageId: StatusCtxModemResponseBusy
            ///     MessageText:
            ///     Busy signal detected at remote site on callback.
            /// </summary>
            StatusCtxModemResponseBusy = 0xC00A000E,

            /// <summary>
            ///     MessageId: StatusCtxModemResponseVoice
            ///     MessageText:
            ///     Voice detected at remote site on callback.
            /// </summary>
            StatusCtxModemResponseVoice = 0xC00A000F,

            /// <summary>
            ///     MessageId: StatusCtxTdError
            ///     MessageText:
            ///     Transport driver error
            /// </summary>
            StatusCtxTdError = 0xC00A0010,

            /// <summary>
            ///     MessageId: StatusCtxLicenseClientInvalid
            ///     MessageText:
            ///     The client you are using is not licensed to use this system. Your logon request is denied.
            /// </summary>
            StatusCtxLicenseClientInvalid = 0xC00A0012,

            /// <summary>
            ///     MessageId: StatusCtxLicenseNotAvailable
            ///     MessageText:
            ///     The system has reached its licensed logon limit.
            ///     Please try again later.
            /// </summary>
            StatusCtxLicenseNotAvailable = 0xC00A0013,

            /// <summary>
            ///     MessageId: StatusCtxLicenseExpired
            ///     MessageText:
            ///     The system license has expired. Your logon request is denied.
            /// </summary>
            StatusCtxLicenseExpired = 0xC00A0014,

            /// <summary>
            ///     MessageId: StatusCtxWinstationNotFound
            ///     MessageText:
            ///     The specified session cannot be found.
            /// </summary>
            StatusCtxWinstationNotFound = 0xC00A0015,

            /// <summary>
            ///     MessageId: StatusCtxWinstationNameCollision
            ///     MessageText:
            ///     The specified session name is already in use.
            /// </summary>
            StatusCtxWinstationNameCollision = 0xC00A0016,

            /// <summary>
            ///     MessageId: StatusCtxWinstationBusy
            ///     MessageText:
            ///     The task you are trying to do can't be completed because Remote Desktop Services is currently busy. Please try
            ///     again in a few minutes. Other users should still be able to log on.
            /// </summary>
            StatusCtxWinstationBusy = 0xC00A0017,

            /// <summary>
            ///     MessageId: StatusCtxBadVideoMode
            ///     MessageText:
            ///     An attempt has been made to connect to a session whose video mode is not supported by the current client.
            /// </summary>
            StatusCtxBadVideoMode = 0xC00A0018,

            /// <summary>
            ///     MessageId: StatusCtxGraphicsInvalid
            ///     MessageText:
            ///     The application attempted to enable Dos graphics mode.
            ///     Dos graphics mode is not supported.
            /// </summary>
            StatusCtxGraphicsInvalid = 0xC00A0022,

            /// <summary>
            ///     MessageId: StatusCtxNotConsole
            ///     MessageText:
            ///     The requested operation can be performed only on the system console.
            ///     This is most often the result of a driver or system Dll requiring direct console access.
            /// </summary>
            StatusCtxNotConsole = 0xC00A0024,

            /// <summary>
            ///     MessageId: StatusCtxClientQueryTimeout
            ///     MessageText:
            ///     The client failed to respond to the server connect message.
            /// </summary>
            StatusCtxClientQueryTimeout = 0xC00A0026,

            /// <summary>
            ///     MessageId: StatusCtxConsoleDisconnect
            ///     MessageText:
            ///     Disconnecting the console session is not supported.
            /// </summary>
            StatusCtxConsoleDisconnect = 0xC00A0027,

            /// <summary>
            ///     MessageId: StatusCtxConsoleConnect
            ///     MessageText:
            ///     Reconnecting a disconnected session to the console is not supported.
            /// </summary>
            StatusCtxConsoleConnect = 0xC00A0028,

            /// <summary>
            ///     MessageId: StatusCtxShadowDenied
            ///     MessageText:
            ///     The request to control another session remotely was denied.
            /// </summary>
            StatusCtxShadowDenied = 0xC00A002A,

            /// <summary>
            ///     MessageId: StatusCtxWinstationAccessDenied
            ///     MessageText:
            ///     A process has requested access to a session, but has not been granted those access rights.
            /// </summary>
            StatusCtxWinstationAccessDenied = 0xC00A002B,

            /// <summary>
            ///     MessageId: StatusCtxInvalidWd
            ///     MessageText:
            ///     The Terminal Connection driver %1 is invalid.
            /// </summary>
            StatusCtxInvalidWd = 0xC00A002E,

            /// <summary>
            ///     MessageId: StatusCtxWdNotFound
            ///     MessageText:
            ///     The Terminal Connection driver %1 was not found in the system path.
            /// </summary>
            StatusCtxWdNotFound = 0xC00A002F,

            /// <summary>
            ///     MessageId: StatusCtxShadowInvalid
            ///     MessageText:
            ///     The requested session cannot be controlled remotely.
            ///     You cannot control your own session, a session that is trying to control your session,
            ///     a session that has no user logged on, nor control other sessions from the console.
            /// </summary>
            StatusCtxShadowInvalid = 0xC00A0030,

            /// <summary>
            ///     MessageId: StatusCtxShadowDisabled
            ///     MessageText:
            ///     The requested session is not configured to allow remote control.
            /// </summary>
            StatusCtxShadowDisabled = 0xC00A0031,

            /// <summary>
            ///     MessageId: StatusRdpProtocolError
            ///     MessageText:
            ///     The Rdp protocol component %2 detected an error in the protocol stream and has disconnected the client.
            /// </summary>
            StatusRdpProtocolError = 0xC00A0032,

            /// <summary>
            ///     MessageId: StatusCtxClientLicenseNotSet
            ///     MessageText:
            ///     Your request to connect to this Terminal server has been rejected.
            ///     Your Terminal Server Client license number has not been entered for this copy of the Terminal Client.
            ///     Please call your system administrator for help in entering a valid, unique license number for this Terminal Server
            ///     Client.
            ///     Click Ok to continue.
            /// </summary>
            StatusCtxClientLicenseNotSet = 0xC00A0033,

            /// <summary>
            ///     MessageId: StatusCtxClientLicenseInUse
            ///     MessageText:
            ///     Your request to connect to this Terminal server has been rejected.
            ///     Your Terminal Server Client license number is currently being used by another user.
            ///     Please call your system administrator to obtain a new copy of the Terminal Server Client with a valid, unique
            ///     license number.
            ///     Click Ok to continue.
            /// </summary>
            StatusCtxClientLicenseInUse = 0xC00A0034,

            /// <summary>
            ///     MessageId: StatusCtxShadowEndedByModeChange
            ///     MessageText:
            ///     The remote control of the console was terminated because the display mode was changed. Changing the display mode in
            ///     a remote control session is not supported.
            /// </summary>
            StatusCtxShadowEndedByModeChange = 0xC00A0035,

            /// <summary>
            ///     MessageId: StatusCtxShadowNotRunning
            ///     MessageText:
            ///     Remote control could not be terminated because the specified session is not currently being remotely controlled.
            /// </summary>
            StatusCtxShadowNotRunning = 0xC00A0036,

            /// <summary>
            ///     MessageId: StatusCtxLogonDisabled
            ///     MessageText:
            ///     Your interactive logon privilege has been disabled.
            ///     Please contact your system administrator.
            /// </summary>
            StatusCtxLogonDisabled = 0xC00A0037,

            /// <summary>
            ///     MessageId: StatusCtxSecurityLayerError
            ///     MessageText:
            ///     The Terminal Server security layer detected an error in the protocol stream and has disconnected the client.
            ///     Client Ip: %2.
            /// </summary>
            StatusCtxSecurityLayerError = 0xC00A0038,

            /// <summary>
            ///     MessageId: StatusTsIncompatibleSessions
            ///     MessageText:
            ///     The target session is incompatible with the current session.
            /// </summary>
            StatusTsIncompatibleSessions = 0xC00A0039,

            /// <summary>
            ///     MessageId: StatusTsVideoSubsystemError
            ///     MessageText:
            ///     Windows can't connect to your session because a problem occurred in the Windows video subsystem. Try connecting
            ///     again later, or contact the server administrator for assistance.
            /// </summary>
            StatusTsVideoSubsystemError = 0xC00A003A,

            // Io error values

            /// <summary>
            ///     MessageId: StatusPnpBadMpsTable
            ///     MessageText:
            ///     A device is missing in the system Bios Mps table. This device will not be used.
            ///     Please contact your system vendor for system Bios update.
            /// </summary>
            StatusPnpBadMpsTable = 0xC0040035,

            /// <summary>
            ///     MessageId: StatusPnpTranslationFailed
            ///     MessageText:
            ///     A translator failed to translate resources.
            /// </summary>
            StatusPnpTranslationFailed = 0xC0040036,

            /// <summary>
            ///     MessageId: StatusPnpIrqTranslationFailed
            ///     MessageText:
            ///     A Irq translator failed to translate resources.
            /// </summary>
            StatusPnpIrqTranslationFailed = 0xC0040037,

            /// <summary>
            ///     MessageId: StatusPnpInvalidId
            ///     MessageText:
            ///     Driver %2 returned invalid Id for a child device (%3,.
            /// </summary>
            StatusPnpInvalidId = 0xC0040038,

            /// <summary>
            ///     MessageId: StatusIoReissueAsCached
            ///     MessageText:
            ///     Reissue the given operation as a cached Io operation
            /// </summary>
            StatusIoReissueAsCached = 0xC0040039,

            // Mui error values

            /// <summary>
            ///     MessageId: StatusMuiFileNotFound
            ///     MessageText:
            ///     The resource loader failed to find Mui file.
            /// </summary>
            StatusMuiFileNotFound = 0xC00B0001,

            /// <summary>
            ///     MessageId: StatusMuiInvalidFile
            ///     MessageText:
            ///     The resource loader failed to load Mui file because the file fail to pass validation.
            /// </summary>
            StatusMuiInvalidFile = 0xC00B0002,

            /// <summary>
            ///     MessageId: StatusMuiInvalidRcConfig
            ///     MessageText:
            ///     The Rc Manifest is corrupted with garbage data or unsupported version or missing required item.
            /// </summary>
            StatusMuiInvalidRcConfig = 0xC00B0003,

            /// <summary>
            ///     MessageId: StatusMuiInvalidLocaleName
            ///     MessageText:
            ///     The Rc Manifest has invalid culture name.
            /// </summary>
            StatusMuiInvalidLocaleName = 0xC00B0004,

            /// <summary>
            ///     MessageId: StatusMuiInvalidUltimatefallbackName
            ///     MessageText:
            ///     The Rc Manifest has invalid ultimatefallback name.
            /// </summary>
            StatusMuiInvalidUltimatefallbackName = 0xC00B0005,

            /// <summary>
            ///     MessageId: StatusMuiFileNotLoaded
            ///     MessageText:
            ///     The resource loader cache doesn't have loaded Mui entry.
            /// </summary>
            StatusMuiFileNotLoaded = 0xC00B0006,

            /// <summary>
            ///     MessageId: StatusResourceEnumUserStop
            ///     MessageText:
            ///     User stopped resource enumeration.
            /// </summary>
            StatusResourceEnumUserStop = 0xC00B0007,

            /// <summary>
            ///     MessageId: StatusFltNoHandlerDefined
            ///     MessageText:
            ///     A handler was not defined by the filter for this operation.
            /// </summary>
            StatusFltNoHandlerDefined = 0xC01C0001,

            /// <summary>
            ///     MessageId: StatusFltContextAlreadyDefined
            ///     MessageText:
            ///     A context is already defined for this object.
            /// </summary>
            StatusFltContextAlreadyDefined = 0xC01C0002,

            /// <summary>
            ///     MessageId: StatusFltInvalidAsynchronousRequest
            ///     MessageText:
            ///     Asynchronous requests are not valid for this operation.
            /// </summary>
            StatusFltInvalidAsynchronousRequest = 0xC01C0003,

            /// <summary>
            ///     MessageId: StatusFltDisallowFastIo
            ///     MessageText:
            ///     Internal error code used by the filter manager to determine if a fastio operation should be forced down the Irp
            ///     path. Mini-filters should never return this value.
            /// </summary>
            StatusFltDisallowFastIo = 0xC01C0004,

            /// <summary>
            ///     MessageId: StatusFltInvalidNameRequest
            ///     MessageText:
            ///     An invalid name request was made. The name requested cannot be retrieved at this time.
            /// </summary>
            StatusFltInvalidNameRequest = 0xC01C0005,

            /// <summary>
            ///     MessageId: StatusFltNotSafeToPostOperation
            ///     MessageText:
            ///     Posting this operation to a worker thread for further processing is not safe at this time because it could lead to
            ///     a system deadlock.
            /// </summary>
            StatusFltNotSafeToPostOperation = 0xC01C0006,

            /// <summary>
            ///     MessageId: StatusFltNotInitialized
            ///     MessageText:
            ///     The Filter Manager was not initialized when a filter tried to register. Make sure that the Filter Manager is
            ///     getting loaded as a driver.
            /// </summary>
            StatusFltNotInitialized = 0xC01C0007,

            /// <summary>
            ///     MessageId: StatusFltFilterNotReady
            ///     MessageText:
            ///     The filter is not ready for attachment to volumes because it has not finished initializing (FltStartFiltering has
            ///     not been called,.
            /// </summary>
            StatusFltFilterNotReady = 0xC01C0008,

            /// <summary>
            ///     MessageId: StatusFltPostOperationCleanup
            ///     MessageText:
            ///     The filter must cleanup any operation specific context at this time because it is being removed from the system
            ///     before the operation is completed by the lower drivers.
            /// </summary>
            StatusFltPostOperationCleanup = 0xC01C0009,

            /// <summary>
            ///     MessageId: StatusFltInternalError
            ///     MessageText:
            ///     The Filter Manager had an internal error from which it cannot recover, therefore the operation has been failed.
            ///     This is usually the result of a filter returning an invalid value from a pre-operation callback.
            /// </summary>
            StatusFltInternalError = 0xC01C000A,

            /// <summary>
            ///     MessageId: StatusFltDeletingObject
            ///     MessageText:
            ///     The object specified for this action is in the process of being deleted, therefore the action requested cannot be
            ///     completed at this time.
            /// </summary>
            StatusFltDeletingObject = 0xC01C000B,

            /// <summary>
            ///     MessageId: StatusFltMustBeNonpagedPool
            ///     MessageText:
            ///     Non-paged pool must be used for this type of context.
            /// </summary>
            StatusFltMustBeNonpagedPool = 0xC01C000C,

            /// <summary>
            ///     MessageId: StatusFltDuplicateEntry
            ///     MessageText:
            ///     A duplicate handler definition has been provided for an operation.
            /// </summary>
            StatusFltDuplicateEntry = 0xC01C000D,

            /// <summary>
            ///     MessageId: StatusFltCbdqDisabled
            ///     MessageText:
            ///     The callback data queue has been disabled.
            /// </summary>
            StatusFltCbdqDisabled = 0xC01C000E,

            /// <summary>
            ///     MessageId: StatusFltDoNotAttach
            ///     MessageText:
            ///     Do not attach the filter to the volume at this time.
            /// </summary>
            StatusFltDoNotAttach = 0xC01C000F,

            /// <summary>
            ///     MessageId: StatusFltDoNotDetach
            ///     MessageText:
            ///     Do not detach the filter from the volume at this time.
            /// </summary>
            StatusFltDoNotDetach = 0xC01C0010,

            /// <summary>
            ///     MessageId: StatusFltInstanceAltitudeCollision
            ///     MessageText:
            ///     An instance already exists at this altitude on the volume specified.
            /// </summary>
            StatusFltInstanceAltitudeCollision = 0xC01C0011,

            /// <summary>
            ///     MessageId: StatusFltInstanceNameCollision
            ///     MessageText:
            ///     An instance already exists with this name on the volume specified.
            /// </summary>
            StatusFltInstanceNameCollision = 0xC01C0012,

            /// <summary>
            ///     MessageId: StatusFltFilterNotFound
            ///     MessageText:
            ///     The system could not find the filter specified.
            /// </summary>
            StatusFltFilterNotFound = 0xC01C0013,

            /// <summary>
            ///     MessageId: StatusFltVolumeNotFound
            ///     MessageText:
            ///     The system could not find the volume specified.
            /// </summary>
            StatusFltVolumeNotFound = 0xC01C0014,

            /// <summary>
            ///     MessageId: StatusFltInstanceNotFound
            ///     MessageText:
            ///     The system could not find the instance specified.
            /// </summary>
            StatusFltInstanceNotFound = 0xC01C0015,

            /// <summary>
            ///     MessageId: StatusFltContextAllocationNotFound
            ///     MessageText:
            ///     No registered context allocation definition was found for the given request.
            /// </summary>
            StatusFltContextAllocationNotFound = 0xC01C0016,

            /// <summary>
            ///     MessageId: StatusFltInvalidContextRegistration
            ///     MessageText:
            ///     An invalid parameter was specified during context registration.
            /// </summary>
            StatusFltInvalidContextRegistration = 0xC01C0017,

            /// <summary>
            ///     MessageId: StatusFltNameCacheMiss
            ///     MessageText:
            ///     The name requested was not found in Filter Manager's name cache and could not be retrieved from the file system.
            /// </summary>
            StatusFltNameCacheMiss = 0xC01C0018,

            /// <summary>
            ///     MessageId: StatusFltNoDeviceObject
            ///     MessageText:
            ///     The requested device object does not exist for the given volume.
            /// </summary>
            StatusFltNoDeviceObject = 0xC01C0019,

            /// <summary>
            ///     MessageId: StatusFltVolumeAlreadyMounted
            ///     MessageText:
            ///     The specified volume is already mounted.
            /// </summary>
            StatusFltVolumeAlreadyMounted = 0xC01C001A,

            /// <summary>
            ///     MessageId: StatusFltAlreadyEnlisted
            ///     MessageText:
            ///     The specified Transaction Context is already enlisted in a transaction
            /// </summary>
            StatusFltAlreadyEnlisted = 0xC01C001B,

            /// <summary>
            ///     MessageId: StatusFltContextAlreadyLinked
            ///     MessageText:
            ///     The specifiec context is already attached to another object
            /// </summary>
            StatusFltContextAlreadyLinked = 0xC01C001C,

            /// <summary>
            ///     MessageId: StatusFltNoWaiterForReply
            ///     MessageText:
            ///     No waiter is present for the filter's reply to this message.
            /// </summary>
            StatusFltNoWaiterForReply = 0xC01C0020,

            // Side-by-side (Sxs, error values

            /// <summary>
            ///     MessageId: StatusSxsSectionNotFound
            ///     MessageText:
            ///     The requested section is not present in the activation context.
            /// </summary>
            StatusSxsSectionNotFound = 0xC0150001,

            /// <summary>
            ///     MessageId: StatusSxsCantGenActctx
            ///     MessageText:
            ///     Windows was not able to process the application binding information.
            ///     Please refer to your System Event Log for further information.
            /// </summary>
            StatusSxsCantGenActctx = 0xC0150002,

            /// <summary>
            ///     MessageId: StatusSxsInvalidActctxdataFormat
            ///     MessageText:
            ///     The application binding data format is invalid.
            /// </summary>
            StatusSxsInvalidActctxdataFormat = 0xC0150003,

            /// <summary>
            ///     MessageId: StatusSxsAssemblyNotFound
            ///     MessageText:
            ///     The referenced assembly is not installed on your system.
            /// </summary>
            StatusSxsAssemblyNotFound = 0xC0150004,

            /// <summary>
            ///     MessageId: StatusSxsManifestFormatError
            ///     MessageText:
            ///     The manifest file does not begin with the required tag and format information.
            /// </summary>
            StatusSxsManifestFormatError = 0xC0150005,

            /// <summary>
            ///     MessageId: StatusSxsManifestParseError
            ///     MessageText:
            ///     The manifest file contains one or more syntax errors.
            /// </summary>
            StatusSxsManifestParseError = 0xC0150006,

            /// <summary>
            ///     MessageId: StatusSxsActivationContextDisabled
            ///     MessageText:
            ///     The application attempted to activate a disabled activation context.
            /// </summary>
            StatusSxsActivationContextDisabled = 0xC0150007,

            /// <summary>
            ///     MessageId: StatusSxsKeyNotFound
            ///     MessageText:
            ///     The requested lookup key was not found in any active activation context.
            /// </summary>
            StatusSxsKeyNotFound = 0xC0150008,

            /// <summary>
            ///     MessageId: StatusSxsVersionConflict
            ///     MessageText:
            ///     A component version required by the application conflicts with another component version already active.
            /// </summary>
            StatusSxsVersionConflict = 0xC0150009,

            /// <summary>
            ///     MessageId: StatusSxsWrongSectionType
            ///     MessageText:
            ///     The type requested activation context section does not match the query Api used.
            /// </summary>
            StatusSxsWrongSectionType = 0xC015000A,

            /// <summary>
            ///     MessageId: StatusSxsThreadQueriesDisabled
            ///     MessageText:
            ///     Lack of system resources has required isolated activation to be disabled for the current thread of execution.
            /// </summary>
            StatusSxsThreadQueriesDisabled = 0xC015000B,

            /// <summary>
            ///     MessageId: StatusSxsAssemblyMissing
            ///     MessageText:
            ///     The referenced assembly could not be found.
            /// </summary>
            StatusSxsAssemblyMissing = 0xC015000C,

            /// <summary>
            ///     MessageId: StatusSxsReleaseActivationContext
            ///     MessageText:
            ///     A kernel mode component is releasing a reference on an activation context.
            /// </summary>
            StatusSxsReleaseActivationContext = 0x4015000D,

            /// <summary>
            ///     MessageId: StatusSxsProcessDefaultAlreadySet
            ///     MessageText:
            ///     An attempt to set the process default activation context failed because the process default activation context was
            ///     already set.
            /// </summary>
            StatusSxsProcessDefaultAlreadySet = 0xC015000E,

            /// <summary>
            ///     MessageId: StatusSxsEarlyDeactivation
            ///     MessageText:
            ///     The activation context being deactivated is not the most recently activated one.
            /// </summary>
            StatusSxsEarlyDeactivation = 0xC015000F, // winnt

            /// <summary>
            ///     MessageId: StatusSxsInvalidDeactivation
            ///     MessageText:
            ///     The activation context being deactivated is not active for the current thread of execution.
            /// </summary>
            StatusSxsInvalidDeactivation = 0xC0150010, // winnt

            /// <summary>
            ///     MessageId: StatusSxsMultipleDeactivation
            ///     MessageText:
            ///     The activation context being deactivated has already been deactivated.
            /// </summary>
            StatusSxsMultipleDeactivation = 0xC0150011,

            /// <summary>
            ///     MessageId: StatusSxsSystemDefaultActivationContextEmpty
            ///     MessageText:
            ///     The activation context of system default assembly could not be generated.
            /// </summary>
            StatusSxsSystemDefaultActivationContextEmpty = 0xC0150012,

            /// <summary>
            ///     MessageId: StatusSxsProcessTerminationRequested
            ///     MessageText:
            ///     A component used by the isolation facility has requested to terminate the process.
            /// </summary>
            StatusSxsProcessTerminationRequested = 0xC0150013,

            /// <summary>
            ///     MessageId: StatusSxsCorruptActivationStack
            ///     MessageText:
            ///     The activation context activation stack for the running thread of execution is corrupt.
            /// </summary>
            StatusSxsCorruptActivationStack = 0xC0150014,

            /// <summary>
            ///     MessageId: StatusSxsCorruption
            ///     MessageText:
            ///     The application isolation metadata for this process or thread has become corrupt.
            /// </summary>
            StatusSxsCorruption = 0xC0150015,

            /// <summary>
            ///     MessageId: StatusSxsInvalidIdentityAttributeValue
            ///     MessageText:
            ///     The value of an attribute in an identity is not within the legal range.
            /// </summary>
            StatusSxsInvalidIdentityAttributeValue = 0xC0150016,

            /// <summary>
            ///     MessageId: StatusSxsInvalidIdentityAttributeName
            ///     MessageText:
            ///     The name of an attribute in an identity is not within the legal range.
            /// </summary>
            StatusSxsInvalidIdentityAttributeName = 0xC0150017,

            /// <summary>
            ///     MessageId: StatusSxsIdentityDuplicateAttribute
            ///     MessageText:
            ///     An identity contains two definitions for the same attribute.
            /// </summary>
            StatusSxsIdentityDuplicateAttribute = 0xC0150018,

            /// <summary>
            ///     MessageId: StatusSxsIdentityParseError
            ///     MessageText:
            ///     The identity string is malformed. This may be due to a trailing comma, more than two unnamed attributes, missing
            ///     attribute name or missing attribute value.
            /// </summary>
            StatusSxsIdentityParseError = 0xC0150019,

            /// <summary>
            ///     MessageId: StatusSxsComponentStoreCorrupt
            ///     MessageText:
            ///     The component store has been corrupted.
            /// </summary>
            StatusSxsComponentStoreCorrupt = 0xC015001A,

            /// <summary>
            ///     MessageId: StatusSxsFileHashMismatch
            ///     MessageText:
            ///     A component's file does not match the verification information present in the component manifest.
            /// </summary>
            StatusSxsFileHashMismatch = 0xC015001B,

            /// <summary>
            ///     MessageId: StatusSxsManifestIdentitySameButContentsDifferent
            ///     MessageText:
            ///     The identities of the manifests are identical but their contents are different.
            /// </summary>
            StatusSxsManifestIdentitySameButContentsDifferent = 0xC015001C,

            /// <summary>
            ///     MessageId: StatusSxsIdentitiesDifferent
            ///     MessageText:
            ///     The component identities are different.
            /// </summary>
            StatusSxsIdentitiesDifferent = 0xC015001D,

            /// <summary>
            ///     MessageId: StatusSxsAssemblyIsNotADeployment
            ///     MessageText:
            ///     The assembly is not a deployment.
            /// </summary>
            StatusSxsAssemblyIsNotADeployment = 0xC015001E,

            /// <summary>
            ///     MessageId: StatusSxsFileNotPartOfAssembly
            ///     MessageText:
            ///     The file is not a part of the assembly.
            /// </summary>
            StatusSxsFileNotPartOfAssembly = 0xC015001F,

            /// <summary>
            ///     MessageId: StatusAdvancedInstallerFailed
            ///     MessageText:
            ///     An advanced installer failed during setup or servicing.
            /// </summary>
            StatusAdvancedInstallerFailed = 0xC0150020,

            /// <summary>
            ///     MessageId: StatusXmlEncodingMismatch
            ///     MessageText:
            ///     The character encoding in the Xml declaration did not match the encoding used in the document.
            /// </summary>
            StatusXmlEncodingMismatch = 0xC0150021,

            /// <summary>
            ///     MessageId: StatusSxsManifestTooBig
            ///     MessageText:
            ///     The size of the manifest exceeds the maximum allowed.
            /// </summary>
            StatusSxsManifestTooBig = 0xC0150022,

            /// <summary>
            ///     MessageId: StatusSxsSettingNotRegistered
            ///     MessageText:
            ///     The setting is not registered.
            /// </summary>
            StatusSxsSettingNotRegistered = 0xC0150023,

            /// <summary>
            ///     MessageId: StatusSxsTransactionClosureIncomplete
            ///     MessageText:
            ///     One or more required members of the transaction are not present.
            /// </summary>
            StatusSxsTransactionClosureIncomplete = 0xC0150024,

            /// <summary>
            ///     MessageId: StatusSmiPrimitiveInstallerFailed
            ///     MessageText:
            ///     The Smi primitive installer failed during setup or servicing.
            /// </summary>
            StatusSmiPrimitiveInstallerFailed = 0xC0150025,

            /// <summary>
            ///     MessageId: StatusGenericCommandFailed
            ///     MessageText:
            ///     A generic command executable returned a result that indicates failure.
            /// </summary>
            StatusGenericCommandFailed = 0xC0150026,

            /// <summary>
            ///     MessageId: StatusSxsFileHashMissing
            ///     MessageText:
            ///     A component is missing file verification information in its manifest.
            /// </summary>
            StatusSxsFileHashMissing = 0xC0150027,

            // Cluster error values

            /// <summary>
            ///     MessageId: StatusClusterInvalidNode
            ///     MessageText:
            ///     The cluster node is not valid.
            /// </summary>
            StatusClusterInvalidNode = 0xC0130001,

            /// <summary>
            ///     MessageId: StatusClusterNodeExists
            ///     MessageText:
            ///     The cluster node already exists.
            /// </summary>
            StatusClusterNodeExists = 0xC0130002,

            /// <summary>
            ///     MessageId: StatusClusterJoinInProgress
            ///     MessageText:
            ///     A node is in the process of joining the cluster.
            /// </summary>
            StatusClusterJoinInProgress = 0xC0130003,

            /// <summary>
            ///     MessageId: StatusClusterNodeNotFound
            ///     MessageText:
            ///     The cluster node was not found.
            /// </summary>
            StatusClusterNodeNotFound = 0xC0130004,

            /// <summary>
            ///     MessageId: StatusClusterLocalNodeNotFound
            ///     MessageText:
            ///     The cluster local node information was not found.
            /// </summary>
            StatusClusterLocalNodeNotFound = 0xC0130005,

            /// <summary>
            ///     MessageId: StatusClusterNetworkExists
            ///     MessageText:
            ///     The cluster network already exists.
            /// </summary>
            StatusClusterNetworkExists = 0xC0130006,

            /// <summary>
            ///     MessageId: StatusClusterNetworkNotFound
            ///     MessageText:
            ///     The cluster network was not found.
            /// </summary>
            StatusClusterNetworkNotFound = 0xC0130007,

            /// <summary>
            ///     MessageId: StatusClusterNetinterfaceExists
            ///     MessageText:
            ///     The cluster network interface already exists.
            /// </summary>
            StatusClusterNetinterfaceExists = 0xC0130008,

            /// <summary>
            ///     MessageId: StatusClusterNetinterfaceNotFound
            ///     MessageText:
            ///     The cluster network interface was not found.
            /// </summary>
            StatusClusterNetinterfaceNotFound = 0xC0130009,

            /// <summary>
            ///     MessageId: StatusClusterInvalidRequest
            ///     MessageText:
            ///     The cluster request is not valid for this object.
            /// </summary>
            StatusClusterInvalidRequest = 0xC013000A,

            /// <summary>
            ///     MessageId: StatusClusterInvalidNetworkProvider
            ///     MessageText:
            ///     The cluster network provider is not valid.
            /// </summary>
            StatusClusterInvalidNetworkProvider = 0xC013000B,

            /// <summary>
            ///     MessageId: StatusClusterNodeDown
            ///     MessageText:
            ///     The cluster node is down.
            /// </summary>
            StatusClusterNodeDown = 0xC013000C,

            /// <summary>
            ///     MessageId: StatusClusterNodeUnreachable
            ///     MessageText:
            ///     The cluster node is not reachable.
            /// </summary>
            StatusClusterNodeUnreachable = 0xC013000D,

            /// <summary>
            ///     MessageId: StatusClusterNodeNotMember
            ///     MessageText:
            ///     The cluster node is not a member of the cluster.
            /// </summary>
            StatusClusterNodeNotMember = 0xC013000E,

            /// <summary>
            ///     MessageId: StatusClusterJoinNotInProgress
            ///     MessageText:
            ///     A cluster join operation is not in progress.
            /// </summary>
            StatusClusterJoinNotInProgress = 0xC013000F,

            /// <summary>
            ///     MessageId: StatusClusterInvalidNetwork
            ///     MessageText:
            ///     The cluster network is not valid.
            /// </summary>
            StatusClusterInvalidNetwork = 0xC0130010,

            /// <summary>
            ///     MessageId: StatusClusterNoNetAdapters
            ///     MessageText:
            ///     No network adapters are available.
            /// </summary>
            StatusClusterNoNetAdapters = 0xC0130011,

            /// <summary>
            ///     MessageId: StatusClusterNodeUp
            ///     MessageText:
            ///     The cluster node is up.
            /// </summary>
            StatusClusterNodeUp = 0xC0130012,

            /// <summary>
            ///     MessageId: StatusClusterNodePaused
            ///     MessageText:
            ///     The cluster node is paused.
            /// </summary>
            StatusClusterNodePaused = 0xC0130013,

            /// <summary>
            ///     MessageId: StatusClusterNodeNotPaused
            ///     MessageText:
            ///     The cluster node is not paused.
            /// </summary>
            StatusClusterNodeNotPaused = 0xC0130014,

            /// <summary>
            ///     MessageId: StatusClusterNoSecurityContext
            ///     MessageText:
            ///     No cluster security context is available.
            /// </summary>
            StatusClusterNoSecurityContext = 0xC0130015,

            /// <summary>
            ///     MessageId: StatusClusterNetworkNotInternal
            ///     MessageText:
            ///     The cluster network is not configured for internal cluster communication.
            /// </summary>
            StatusClusterNetworkNotInternal = 0xC0130016,

            /// <summary>
            ///     MessageId: StatusClusterPoisoned
            ///     MessageText:
            ///     The cluster node has been poisoned.
            /// </summary>
            StatusClusterPoisoned = 0xC0130017,

            /// <summary>
            ///     MessageId: StatusClusterNonCsvPath
            ///     MessageText:
            ///     The path does not belong to a cluster shared volume.
            /// </summary>
            StatusClusterNonCsvPath = 0xC0130018,

            /// <summary>
            ///     MessageId: StatusClusterCsvVolumeNotLocal
            ///     MessageText:
            ///     The cluster shared volume is not locally mounted.
            /// </summary>
            StatusClusterCsvVolumeNotLocal = 0xC0130019,

            // Transaction Manager error values

            /// <summary>
            ///     MessageId: StatusTransactionalConflict
            ///     MessageText:
            ///     The function attempted to use a name that is reserved for use by another transaction.
            /// </summary>
            StatusTransactionalConflict = 0xC0190001,

            /// <summary>
            ///     MessageId: StatusInvalidTransaction
            ///     MessageText:
            ///     The transaction handle associated with this operation is not valid.
            /// </summary>
            StatusInvalidTransaction = 0xC0190002,

            /// <summary>
            ///     MessageId: StatusTransactionNotActive
            ///     MessageText:
            ///     The requested operation was made in the context of a transaction that is no longer active.
            /// </summary>
            StatusTransactionNotActive = 0xC0190003,

            /// <summary>
            ///     MessageId: StatusTmInitializationFailed
            ///     MessageText:
            ///     The Transaction Manager was unable to be successfully initialized. Transacted operations are not supported.
            /// </summary>
            StatusTmInitializationFailed = 0xC0190004,

            /// <summary>
            ///     MessageId: StatusRmNotActive
            ///     MessageText:
            ///     Transaction support within the specified resource manager is not started or was shut down due to an error.
            /// </summary>
            StatusRmNotActive = 0xC0190005,

            /// <summary>
            ///     MessageId: StatusRmMetadataCorrupt
            ///     MessageText:
            ///     The metadata of the Rm has been corrupted. The Rm will not function.
            /// </summary>
            StatusRmMetadataCorrupt = 0xC0190006,

            /// <summary>
            ///     MessageId: StatusTransactionNotJoined
            ///     MessageText:
            ///     The resource manager has attempted to prepare a transaction that it has not successfully joined.
            /// </summary>
            StatusTransactionNotJoined = 0xC0190007,

            /// <summary>
            ///     MessageId: StatusDirectoryNotRm
            ///     MessageText:
            ///     The specified directory does not contain a file system resource manager.
            /// </summary>
            StatusDirectoryNotRm = 0xC0190008,

            /// <summary>
            ///     MessageId: StatusCouldNotResizeLog
            ///     MessageText:
            ///     The log could not be set to the requested size.
            /// </summary>
            StatusCouldNotResizeLog = 0x80190009,

            /// <summary>
            ///     MessageId: StatusTransactionsUnsupportedRemote
            ///     MessageText:
            ///     The remote server or share does not support transacted file operations.
            /// </summary>
            StatusTransactionsUnsupportedRemote = 0xC019000A,

            /// <summary>
            ///     MessageId: StatusLogResizeInvalidSize
            ///     MessageText:
            ///     The requested log size for the file system resource manager is invalid.
            /// </summary>
            StatusLogResizeInvalidSize = 0xC019000B,

            /// <summary>
            ///     MessageId: StatusRemoteFileVersionMismatch
            ///     MessageText:
            ///     The remote server sent mismatching version number or Fid for a file opened with transactions.
            /// </summary>
            StatusRemoteFileVersionMismatch = 0xC019000C,

            /// <summary>
            ///     MessageId: StatusCrmProtocolAlreadyExists
            ///     MessageText:
            ///     The Rm tried to register a protocol that already exists.
            /// </summary>
            StatusCrmProtocolAlreadyExists = 0xC019000F,

            /// <summary>
            ///     MessageId: StatusTransactionPropagationFailed
            ///     MessageText:
            ///     The attempt to propagate the Transaction failed.
            /// </summary>
            StatusTransactionPropagationFailed = 0xC0190010,

            /// <summary>
            ///     MessageId: StatusCrmProtocolNotFound
            ///     MessageText:
            ///     The requested propagation protocol was not registered as a Crm.
            /// </summary>
            StatusCrmProtocolNotFound = 0xC0190011,

            /// <summary>
            ///     MessageId: StatusTransactionSuperiorExists
            ///     MessageText:
            ///     The Transaction object already has a superior enlistment, and the caller attempted an operation that would have
            ///     created a new superior. Only a single superior enlistment is allowed.
            /// </summary>
            StatusTransactionSuperiorExists = 0xC0190012,

            /// <summary>
            ///     MessageId: StatusTransactionRequestNotValid
            ///     MessageText:
            ///     The requested operation is not valid on the Transaction object in its current state.
            /// </summary>
            StatusTransactionRequestNotValid = 0xC0190013,

            /// <summary>
            ///     MessageId: StatusTransactionNotRequested
            ///     MessageText:
            ///     The caller has called a response Api, but the response is not expected because the Tm did not issue the
            ///     corresponding request to the caller.
            /// </summary>
            StatusTransactionNotRequested = 0xC0190014,

            /// <summary>
            ///     MessageId: StatusTransactionAlreadyAborted
            ///     MessageText:
            ///     It is too late to perform the requested operation, since the Transaction has already been aborted.
            /// </summary>
            StatusTransactionAlreadyAborted = 0xC0190015,

            /// <summary>
            ///     MessageId: StatusTransactionAlreadyCommitted
            ///     MessageText:
            ///     It is too late to perform the requested operation, since the Transaction has already been committed.
            /// </summary>
            StatusTransactionAlreadyCommitted = 0xC0190016,

            /// <summary>
            ///     MessageId: StatusTransactionInvalidMarshallBuffer
            ///     MessageText:
            ///     The buffer passed in to NtPushTransaction or NtPullTransaction is not in a valid format.
            /// </summary>
            StatusTransactionInvalidMarshallBuffer = 0xC0190017,

            /// <summary>
            ///     MessageId: StatusCurrentTransactionNotValid
            ///     MessageText:
            ///     The current transaction context associated with the thread is not a valid handle to a transaction object.
            /// </summary>
            StatusCurrentTransactionNotValid = 0xC0190018,

            /// <summary>
            ///     MessageId: StatusLogGrowthFailed
            ///     MessageText:
            ///     An attempt to create space in the transactional resource manager's log failed. The failure status has been recorded
            ///     in the event log.
            /// </summary>
            StatusLogGrowthFailed = 0xC0190019,

            /// <summary>
            ///     MessageId: StatusObjectNoLongerExists
            ///     MessageText:
            ///     The object (file, stream, link, corresponding to the handle has been deleted by a transaction savepoint rollback.
            /// </summary>
            StatusObjectNoLongerExists = 0xC0190021,

            /// <summary>
            ///     MessageId: StatusStreamMiniversionNotFound
            ///     MessageText:
            ///     The specified file miniversion was not found for this transacted file open.
            /// </summary>
            StatusStreamMiniversionNotFound = 0xC0190022,

            /// <summary>
            ///     MessageId: StatusStreamMiniversionNotValid
            ///     MessageText:
            ///     The specified file miniversion was found but has been invalidated. Most likely cause is a transaction savepoint
            ///     rollback.
            /// </summary>
            StatusStreamMiniversionNotValid = 0xC0190023,

            /// <summary>
            ///     MessageId: StatusMiniversionInaccessibleFromSpecifiedTransaction
            ///     MessageText:
            ///     A miniversion may only be opened in the context of the transaction that created it.
            /// </summary>
            StatusMiniversionInaccessibleFromSpecifiedTransaction = 0xC0190024,

            /// <summary>
            ///     MessageId: StatusCantOpenMiniversionWithModifyIntent
            ///     MessageText:
            ///     It is not possible to open a miniversion with modify access.
            /// </summary>
            StatusCantOpenMiniversionWithModifyIntent = 0xC0190025,

            /// <summary>
            ///     MessageId: StatusCantCreateMoreStreamMiniversions
            ///     MessageText:
            ///     It is not possible to create any more miniversions for this stream.
            /// </summary>
            StatusCantCreateMoreStreamMiniversions = 0xC0190026,

            /// <summary>
            ///     MessageId: StatusHandleNoLongerValid
            ///     MessageText:
            ///     The handle has been invalidated by a transaction. The most likely cause is the presence of memory mapping on a file
            ///     or an open handle when the transaction ended or rolled back to savepoint.
            /// </summary>
            StatusHandleNoLongerValid = 0xC0190028,

            /// <summary>
            ///     MessageId: StatusNoTxfMetadata
            ///     MessageText:
            ///     There is no transaction metadata on the file.
            /// </summary>
            StatusNoTxfMetadata = 0x80190029,

            /// <summary>
            ///     MessageId: StatusLogCorruptionDetected
            ///     MessageText:
            ///     The log data is corrupt.
            /// </summary>
            StatusLogCorruptionDetected = 0xC0190030,

            /// <summary>
            ///     MessageId: StatusCantRecoverWithHandleOpen
            ///     MessageText:
            ///     The file can't be recovered because there is a handle still open on it.
            /// </summary>
            StatusCantRecoverWithHandleOpen = 0x80190031,

            /// <summary>
            ///     MessageId: StatusRmDisconnected
            ///     MessageText:
            ///     The transaction outcome is unavailable because the resource manager responsible for it has disconnected.
            /// </summary>
            StatusRmDisconnected = 0xC0190032,

            /// <summary>
            ///     MessageId: StatusEnlistmentNotSuperior
            ///     MessageText:
            ///     The request was rejected because the enlistment in question is not a superior enlistment.
            /// </summary>
            StatusEnlistmentNotSuperior = 0xC0190033,

            /// <summary>
            ///     MessageId: StatusRecoveryNotNeeded
            ///     MessageText:
            ///     The transactional resource manager is already consistent. Recovery is not needed.
            /// </summary>
            StatusRecoveryNotNeeded = 0x40190034,

            /// <summary>
            ///     MessageId: StatusRmAlreadyStarted
            ///     MessageText:
            ///     The transactional resource manager has already been started.
            /// </summary>
            StatusRmAlreadyStarted = 0x40190035,

            /// <summary>
            ///     MessageId: StatusFileIdentityNotPersistent
            ///     MessageText:
            ///     The file cannot be opened transactionally, because its identity depends on the outcome of an unresolved
            ///     transaction.
            /// </summary>
            StatusFileIdentityNotPersistent = 0xC0190036,

            /// <summary>
            ///     MessageId: StatusCantBreakTransactionalDependency
            ///     MessageText:
            ///     The operation cannot be performed because another transaction is depending on the fact that this property will not
            ///     change.
            /// </summary>
            StatusCantBreakTransactionalDependency = 0xC0190037,

            /// <summary>
            ///     MessageId: StatusCantCrossRmBoundary
            ///     MessageText:
            ///     The operation would involve a single file with two transactional resource managers and is therefore not allowed.
            /// </summary>
            StatusCantCrossRmBoundary = 0xC0190038,

            /// <summary>
            ///     MessageId: StatusTxfDirNotEmpty
            ///     MessageText:
            ///     The $Txf directory must be empty for this operation to succeed.
            /// </summary>
            StatusTxfDirNotEmpty = 0xC0190039,

            /// <summary>
            ///     MessageId: StatusIndoubtTransactionsExist
            ///     MessageText:
            ///     The operation would leave a transactional resource manager in an inconsistent state and is therefore not allowed.
            /// </summary>
            StatusIndoubtTransactionsExist = 0xC019003A,

            /// <summary>
            ///     MessageId: StatusTmVolatile
            ///     MessageText:
            ///     The operation could not be completed because the transaction manager does not have a log.
            /// </summary>
            StatusTmVolatile = 0xC019003B,

            /// <summary>
            ///     MessageId: StatusRollbackTimerExpired
            ///     MessageText:
            ///     A rollback could not be scheduled because a previously scheduled rollback has already executed or been queued for
            ///     execution.
            /// </summary>
            StatusRollbackTimerExpired = 0xC019003C,

            /// <summary>
            ///     MessageId: StatusTxfAttributeCorrupt
            ///     MessageText:
            ///     The transactional metadata attribute on the file or directory %hs is corrupt and unreadable.
            /// </summary>
            StatusTxfAttributeCorrupt = 0xC019003D,

            /// <summary>
            ///     MessageId: StatusEfsNotAllowedInTransaction
            ///     MessageText:
            ///     The encryption operation could not be completed because a transaction is active.
            /// </summary>
            StatusEfsNotAllowedInTransaction = 0xC019003E,

            /// <summary>
            ///     MessageId: StatusTransactionalOpenNotAllowed
            ///     MessageText:
            ///     This object is not allowed to be opened in a transaction.
            /// </summary>
            StatusTransactionalOpenNotAllowed = 0xC019003F,

            /// <summary>
            ///     MessageId: StatusTransactedMappingUnsupportedRemote
            ///     MessageText:
            ///     Memory mapping (creating a mapped section, a remote file under a transaction is not supported.
            /// </summary>
            StatusTransactedMappingUnsupportedRemote = 0xC0190040,

            /// <summary>
            ///     MessageId: StatusTxfMetadataAlreadyPresent
            ///     MessageText:
            ///     Transaction metadata is already present on this file and cannot be superseded.
            /// </summary>
            StatusTxfMetadataAlreadyPresent = 0x80190041,

            /// <summary>
            ///     MessageId: StatusTransactionScopeCallbacksNotSet
            ///     MessageText:
            ///     A transaction scope could not be entered because the scope handler has not been initialized.
            /// </summary>
            StatusTransactionScopeCallbacksNotSet = 0x80190042,

            /// <summary>
            ///     MessageId: StatusTransactionRequiredPromotion
            ///     MessageText:
            ///     Promotion was required in order to allow the resource manager to enlist, but the transaction was set to disallow
            ///     it.
            /// </summary>
            StatusTransactionRequiredPromotion = 0xC0190043,

            /// <summary>
            ///     MessageId: StatusCannotExecuteFileInTransaction
            ///     MessageText:
            ///     This file is open for modification in an unresolved transaction and may be opened for execute only by a transacted
            ///     reader.
            /// </summary>
            StatusCannotExecuteFileInTransaction = 0xC0190044,

            /// <summary>
            ///     MessageId: StatusTransactionsNotFrozen
            ///     MessageText:
            ///     The request to thaw frozen transactions was ignored because transactions had not previously been frozen.
            /// </summary>
            StatusTransactionsNotFrozen = 0xC0190045,

            /// <summary>
            ///     MessageId: StatusTransactionFreezeInProgress
            ///     MessageText:
            ///     Transactions cannot be frozen because a freeze is already in progress.
            /// </summary>
            StatusTransactionFreezeInProgress = 0xC0190046,

            /// <summary>
            ///     MessageId: StatusNotSnapshotVolume
            ///     MessageText:
            ///     The target volume is not a snapshot volume. This operation is only valid on a volume mounted as a snapshot.
            /// </summary>
            StatusNotSnapshotVolume = 0xC0190047,

            /// <summary>
            ///     MessageId: StatusNoSavepointWithOpenFiles
            ///     MessageText:
            ///     The savepoint operation failed because files are open on the transaction. This is not permitted.
            /// </summary>
            StatusNoSavepointWithOpenFiles = 0xC0190048,

            /// <summary>
            ///     MessageId: StatusSparseNotAllowedInTransaction
            ///     MessageText:
            ///     The sparse operation could not be completed because a transaction is active on the file.
            /// </summary>
            StatusSparseNotAllowedInTransaction = 0xC0190049,

            /// <summary>
            ///     MessageId: StatusTmIdentityMismatch
            ///     MessageText:
            ///     The call to create a TransactionManager object failed because the Tm Identity stored in the logfile does not match
            ///     the Tm Identity that was passed in as an argument.
            /// </summary>
            StatusTmIdentityMismatch = 0xC019004A,

            /// <summary>
            ///     MessageId: StatusFloatedSection
            ///     MessageText:
            ///     I/O was attempted on a section object that has been floated as a result of a transaction ending. There is no valid
            ///     data.
            /// </summary>
            StatusFloatedSection = 0xC019004B,

            /// <summary>
            ///     MessageId: StatusCannotAcceptTransactedWork
            ///     MessageText:
            ///     The transactional resource manager cannot currently accept transacted work due to a transient condition such as low
            ///     resources.
            /// </summary>
            StatusCannotAcceptTransactedWork = 0xC019004C,

            /// <summary>
            ///     MessageId: StatusCannotAbortTransactions
            ///     MessageText:
            ///     The transactional resource manager had too many tranactions outstanding that could not be aborted. The
            ///     transactional resource manger has been shut down.
            /// </summary>
            StatusCannotAbortTransactions = 0xC019004D,

            /// <summary>
            ///     MessageId: StatusTransactionNotFound
            ///     MessageText:
            ///     The specified Transaction was unable to be opened, because it was not found.
            /// </summary>
            StatusTransactionNotFound = 0xC019004E,

            /// <summary>
            ///     MessageId: StatusResourcemanagerNotFound
            ///     MessageText:
            ///     The specified ResourceManager was unable to be opened, because it was not found.
            /// </summary>
            StatusResourcemanagerNotFound = 0xC019004F,

            /// <summary>
            ///     MessageId: StatusEnlistmentNotFound
            ///     MessageText:
            ///     The specified Enlistment was unable to be opened, because it was not found.
            /// </summary>
            StatusEnlistmentNotFound = 0xC0190050,

            /// <summary>
            ///     MessageId: StatusTransactionmanagerNotFound
            ///     MessageText:
            ///     The specified TransactionManager was unable to be opened, because it was not found.
            /// </summary>
            StatusTransactionmanagerNotFound = 0xC0190051,

            /// <summary>
            ///     MessageId: StatusTransactionmanagerNotOnline
            ///     MessageText:
            ///     The object specified could not be created or opened, because its associated TransactionManager is not online.  The
            ///     TransactionManager must be brought fully Online by calling RecoverTransactionManager to recover to the end of its
            ///     LogFile before objects in its Transaction or ResourceManager namespaces can be opened.  In addition, errors in
            ///     writing records to its LogFile can cause a TransactionManager to go offline.
            /// </summary>
            StatusTransactionmanagerNotOnline = 0xC0190052,

            /// <summary>
            ///     MessageId: StatusTransactionmanagerRecoveryNameCollision
            ///     MessageText:
            ///     The specified TransactionManager was unable to create the objects contained in its logfile in the Ob namespace.
            ///     Therefore, the TransactionManager was unable to recover.
            /// </summary>
            StatusTransactionmanagerRecoveryNameCollision = 0xC0190053,

            /// <summary>
            ///     MessageId: StatusTransactionNotRoot
            ///     MessageText:
            ///     The call to create a superior Enlistment on this Transaction object could not be completed, because the Transaction
            ///     object specified for the enlistment is a subordinate branch of the Transaction. Only the root of the Transaction
            ///     can be enlisted on as a superior.
            /// </summary>
            StatusTransactionNotRoot = 0xC0190054,

            /// <summary>
            ///     MessageId: StatusTransactionObjectExpired
            ///     MessageText:
            ///     Because the associated transaction manager or resource manager has been closed, the handle is no longer valid.
            /// </summary>
            StatusTransactionObjectExpired = 0xC0190055,

            /// <summary>
            ///     MessageId: StatusCompressionNotAllowedInTransaction
            ///     MessageText:
            ///     The compression operation could not be completed because a transaction is active on the file.
            /// </summary>
            StatusCompressionNotAllowedInTransaction = 0xC0190056,

            /// <summary>
            ///     MessageId: StatusTransactionResponseNotEnlisted
            ///     MessageText:
            ///     The specified operation could not be performed on this Superior enlistment, because the enlistment was not created
            ///     with the corresponding completion response in the NotificationMask.
            /// </summary>
            StatusTransactionResponseNotEnlisted = 0xC0190057,

            /// <summary>
            ///     MessageId: StatusTransactionRecordTooLong
            ///     MessageText:
            ///     The specified operation could not be performed, because the record that would be logged was too long. This can
            ///     occur because of two conditions:  either there are too many Enlistments on this Transaction, or the combined
            ///     RecoveryInformation being logged on behalf of those Enlistments is too long.
            /// </summary>
            StatusTransactionRecordTooLong = 0xC0190058,

            /// <summary>
            ///     MessageId: StatusNoLinkTrackingInTransaction
            ///     MessageText:
            ///     The link tracking operation could not be completed because a transaction is active.
            /// </summary>
            StatusNoLinkTrackingInTransaction = 0xC0190059,

            /// <summary>
            ///     MessageId: StatusOperationNotSupportedInTransaction
            ///     MessageText:
            ///     This operation cannot be performed in a transaction.
            /// </summary>
            StatusOperationNotSupportedInTransaction = 0xC019005A,

            /// <summary>
            ///     MessageId: StatusTransactionIntegrityViolated
            ///     MessageText:
            ///     The kernel transaction manager had to abort or forget the transaction because it blocked forward progress.
            /// </summary>
            StatusTransactionIntegrityViolated = 0xC019005B,

            /// <summary>
            ///     MessageId: StatusTransactionmanagerIdentityMismatch
            ///     MessageText:
            ///     The TransactionManager identity that was supplied did not match the one recorded in the TransactionManager's log
            ///     file.
            /// </summary>
            StatusTransactionmanagerIdentityMismatch = 0xC019005C,

            /// <summary>
            ///     MessageId: StatusRmCannotBeFrozenForSnapshot
            ///     MessageText:
            ///     This snapshot operation cannot continue because a transactional resource manager cannot be frozen in its current
            ///     state.  Please try again.
            /// </summary>
            StatusRmCannotBeFrozenForSnapshot = 0xC019005D,

            /// <summary>
            ///     MessageId: StatusTransactionMustWritethrough
            ///     MessageText:
            ///     The transaction cannot be enlisted on with the specified EnlistmentMask, because the transaction has already
            ///     completed the PrePrepare phase.  In order to ensure correctness, the ResourceManager must switch to a write-through
            ///     mode and cease caching data within this transaction.  Enlisting for only subsequent transaction phases may still
            ///     succeed.
            /// </summary>
            StatusTransactionMustWritethrough = 0xC019005E,

            /// <summary>
            ///     MessageId: StatusTransactionNoSuperior
            ///     MessageText:
            ///     The transaction does not have a superior enlistment.
            /// </summary>
            StatusTransactionNoSuperior = 0xC019005F,

            /// <summary>
            ///     MessageId: StatusExpiredHandle
            ///     MessageText:
            ///     The handle is no longer properly associated with its transaction.  It may have been opened in a transactional
            ///     resource manager that was subsequently forced to restart.  Please close the handle and open a new one.
            /// </summary>
            StatusExpiredHandle = 0xC0190060,

            /// <summary>
            ///     MessageId: StatusTransactionNotEnlisted
            ///     MessageText:
            ///     The specified operation could not be performed because the resource manager is not enlisted in the transaction.
            /// </summary>
            StatusTransactionNotEnlisted = 0xC0190061,

            // Clfs (common log file system, error values

            /// <summary>
            ///     MessageId: StatusLogSectorInvalid
            ///     MessageText:
            ///     Log service found an invalid log sector.
            /// </summary>
            StatusLogSectorInvalid = 0xC01A0001,

            /// <summary>
            ///     MessageId: StatusLogSectorParityInvalid
            ///     MessageText:
            ///     Log service encountered a log sector with invalid block parity.
            /// </summary>
            StatusLogSectorParityInvalid = 0xC01A0002,

            /// <summary>
            ///     MessageId: StatusLogSectorRemapped
            ///     MessageText:
            ///     Log service encountered a remapped log sector.
            /// </summary>
            StatusLogSectorRemapped = 0xC01A0003,

            /// <summary>
            ///     MessageId: StatusLogBlockIncomplete
            ///     MessageText:
            ///     Log service encountered a partial or incomplete log block.
            /// </summary>
            StatusLogBlockIncomplete = 0xC01A0004,

            /// <summary>
            ///     MessageId: StatusLogInvalidRange
            ///     MessageText:
            ///     Log service encountered an attempt access data outside the active log range.
            /// </summary>
            StatusLogInvalidRange = 0xC01A0005,

            /// <summary>
            ///     MessageId: StatusLogBlocksExhausted
            ///     MessageText:
            ///     Log service user log marshalling buffers are exhausted.
            /// </summary>
            StatusLogBlocksExhausted = 0xC01A0006,

            /// <summary>
            ///     MessageId: StatusLogReadContextInvalid
            ///     MessageText:
            ///     Log service encountered an attempt read from a marshalling area with an invalid read context.
            /// </summary>
            StatusLogReadContextInvalid = 0xC01A0007,

            /// <summary>
            ///     MessageId: StatusLogRestartInvalid
            ///     MessageText:
            ///     Log service encountered an invalid log restart area.
            /// </summary>
            StatusLogRestartInvalid = 0xC01A0008,

            /// <summary>
            ///     MessageId: StatusLogBlockVersion
            ///     MessageText:
            ///     Log service encountered an invalid log block version.
            /// </summary>
            StatusLogBlockVersion = 0xC01A0009,

            /// <summary>
            ///     MessageId: StatusLogBlockInvalid
            ///     MessageText:
            ///     Log service encountered an invalid log block.
            /// </summary>
            StatusLogBlockInvalid = 0xC01A000A,

            /// <summary>
            ///     MessageId: StatusLogReadModeInvalid
            ///     MessageText:
            ///     Log service encountered an attempt to read the log with an invalid read mode.
            /// </summary>
            StatusLogReadModeInvalid = 0xC01A000B,

            /// <summary>
            ///     MessageId: StatusLogNoRestart
            ///     MessageText:
            ///     Log service encountered a log stream with no restart area.
            /// </summary>
            StatusLogNoRestart = 0x401A000C,

            /// <summary>
            ///     MessageId: StatusLogMetadataCorrupt
            ///     MessageText:
            ///     Log service encountered a corrupted metadata file.
            /// </summary>
            StatusLogMetadataCorrupt = 0xC01A000D,

            /// <summary>
            ///     MessageId: StatusLogMetadataInvalid
            ///     MessageText:
            ///     Log service encountered a metadata file that could not be created by the log file system.
            /// </summary>
            StatusLogMetadataInvalid = 0xC01A000E,

            /// <summary>
            ///     MessageId: StatusLogMetadataInconsistent
            ///     MessageText:
            ///     Log service encountered a metadata file with inconsistent data.
            /// </summary>
            StatusLogMetadataInconsistent = 0xC01A000F,

            /// <summary>
            ///     MessageId: StatusLogReservationInvalid
            ///     MessageText:
            ///     Log service encountered an attempt to erroneously allocate or dispose reservation space.
            /// </summary>
            StatusLogReservationInvalid = 0xC01A0010,

            /// <summary>
            ///     MessageId: StatusLogCantDelete
            ///     MessageText:
            ///     Log service cannot delete log file or file system container.
            /// </summary>
            StatusLogCantDelete = 0xC01A0011,

            /// <summary>
            ///     MessageId: StatusLogContainerLimitExceeded
            ///     MessageText:
            ///     Log service has reached the maximum allowable containers allocated to a log file.
            /// </summary>
            StatusLogContainerLimitExceeded = 0xC01A0012,

            /// <summary>
            ///     MessageId: StatusLogStartOfLog
            ///     MessageText:
            ///     Log service has attempted to read or write backwards past the start of the log.
            /// </summary>
            StatusLogStartOfLog = 0xC01A0013,

            /// <summary>
            ///     MessageId: StatusLogPolicyAlreadyInstalled
            ///     MessageText:
            ///     Log policy could not be installed because a policy of the same type is already present.
            /// </summary>
            StatusLogPolicyAlreadyInstalled = 0xC01A0014,

            /// <summary>
            ///     MessageId: StatusLogPolicyNotInstalled
            ///     MessageText:
            ///     Log policy in question was not installed at the time of the request.
            /// </summary>
            StatusLogPolicyNotInstalled = 0xC01A0015,

            /// <summary>
            ///     MessageId: StatusLogPolicyInvalid
            ///     MessageText:
            ///     The installed set of policies on the log is invalid.
            /// </summary>
            StatusLogPolicyInvalid = 0xC01A0016,

            /// <summary>
            ///     MessageId: StatusLogPolicyConflict
            ///     MessageText:
            ///     A policy on the log in question prevented the operation from completing.
            /// </summary>
            StatusLogPolicyConflict = 0xC01A0017,

            /// <summary>
            ///     MessageId: StatusLogPinnedArchiveTail
            ///     MessageText:
            ///     Log space cannot be reclaimed because the log is pinned by the archive tail.
            /// </summary>
            StatusLogPinnedArchiveTail = 0xC01A0018,

            /// <summary>
            ///     MessageId: StatusLogRecordNonexistent
            ///     MessageText:
            ///     Log record is not a record in the log file.
            /// </summary>
            StatusLogRecordNonexistent = 0xC01A0019,

            /// <summary>
            ///     MessageId: StatusLogRecordsReservedInvalid
            ///     MessageText:
            ///     Number of reserved log records or the adjustment of the number of reserved log records is invalid.
            /// </summary>
            StatusLogRecordsReservedInvalid = 0xC01A001A,

            /// <summary>
            ///     MessageId: StatusLogSpaceReservedInvalid
            ///     MessageText:
            ///     Reserved log space or the adjustment of the log space is invalid.
            /// </summary>
            StatusLogSpaceReservedInvalid = 0xC01A001B,

            /// <summary>
            ///     MessageId: StatusLogTailInvalid
            ///     MessageText:
            ///     A new or existing archive tail or base of the active log is invalid.
            /// </summary>
            StatusLogTailInvalid = 0xC01A001C,

            /// <summary>
            ///     MessageId: StatusLogFull
            ///     MessageText:
            ///     Log space is exhausted.
            /// </summary>
            StatusLogFull = 0xC01A001D,

            /// <summary>
            ///     MessageId: StatusLogMultiplexed
            ///     MessageText:
            ///     Log is multiplexed, no direct writes to the physical log is allowed.
            /// </summary>
            StatusLogMultiplexed = 0xC01A001E,

            /// <summary>
            ///     MessageId: StatusLogDedicated
            ///     MessageText:
            ///     The operation failed because the log is a dedicated log.
            /// </summary>
            StatusLogDedicated = 0xC01A001F,

            /// <summary>
            ///     MessageId: StatusLogArchiveNotInProgress
            ///     MessageText:
            ///     The operation requires an archive context.
            /// </summary>
            StatusLogArchiveNotInProgress = 0xC01A0020,

            /// <summary>
            ///     MessageId: StatusLogArchiveInProgress
            ///     MessageText:
            ///     Log archival is in progress.
            /// </summary>
            StatusLogArchiveInProgress = 0xC01A0021,

            /// <summary>
            ///     MessageId: StatusLogEphemeral
            ///     MessageText:
            ///     The operation requires a non-ephemeral log, but the log is ephemeral.
            /// </summary>
            StatusLogEphemeral = 0xC01A0022,

            /// <summary>
            ///     MessageId: StatusLogNotEnoughContainers
            ///     MessageText:
            ///     The log must have at least two containers before it can be read from or written to.
            /// </summary>
            StatusLogNotEnoughContainers = 0xC01A0023,

            /// <summary>
            ///     MessageId: StatusLogClientAlreadyRegistered
            ///     MessageText:
            ///     A log client has already registered on the stream.
            /// </summary>
            StatusLogClientAlreadyRegistered = 0xC01A0024,

            /// <summary>
            ///     MessageId: StatusLogClientNotRegistered
            ///     MessageText:
            ///     A log client has not been registered on the stream.
            /// </summary>
            StatusLogClientNotRegistered = 0xC01A0025,

            /// <summary>
            ///     MessageId: StatusLogFullHandlerInProgress
            ///     MessageText:
            ///     A request has already been made to handle the log full condition.
            /// </summary>
            StatusLogFullHandlerInProgress = 0xC01A0026,

            /// <summary>
            ///     MessageId: StatusLogContainerReadFailed
            ///     MessageText:
            ///     Log service encountered an error when attempting to read from a log container.
            /// </summary>
            StatusLogContainerReadFailed = 0xC01A0027,

            /// <summary>
            ///     MessageId: StatusLogContainerWriteFailed
            ///     MessageText:
            ///     Log service encountered an error when attempting to write to a log container.
            /// </summary>
            StatusLogContainerWriteFailed = 0xC01A0028,

            /// <summary>
            ///     MessageId: StatusLogContainerOpenFailed
            ///     MessageText:
            ///     Log service encountered an error when attempting open a log container.
            /// </summary>
            StatusLogContainerOpenFailed = 0xC01A0029,

            /// <summary>
            ///     MessageId: StatusLogContainerStateInvalid
            ///     MessageText:
            ///     Log service encountered an invalid container state when attempting a requested action.
            /// </summary>
            StatusLogContainerStateInvalid = 0xC01A002A,

            /// <summary>
            ///     MessageId: StatusLogStateInvalid
            ///     MessageText:
            ///     Log service is not in the correct state to perform a requested action.
            /// </summary>
            StatusLogStateInvalid = 0xC01A002B,

            /// <summary>
            ///     MessageId: StatusLogPinned
            ///     MessageText:
            ///     Log space cannot be reclaimed because the log is pinned.
            /// </summary>
            StatusLogPinned = 0xC01A002C,

            /// <summary>
            ///     MessageId: StatusLogMetadataFlushFailed
            ///     MessageText:
            ///     Log metadata flush failed.
            /// </summary>
            StatusLogMetadataFlushFailed = 0xC01A002D,

            /// <summary>
            ///     MessageId: StatusLogInconsistentSecurity
            ///     MessageText:
            ///     Security on the log and its containers is inconsistent.
            /// </summary>
            StatusLogInconsistentSecurity = 0xC01A002E,

            /// <summary>
            ///     MessageId: StatusLogAppendedFlushFailed
            ///     MessageText:
            ///     Records were appended to the log or reservation changes were made, but the log could not be flushed.
            /// </summary>
            StatusLogAppendedFlushFailed = 0xC01A002F,

            /// <summary>
            ///     MessageId: StatusLogPinnedReservation
            ///     MessageText:
            ///     The log is pinned due to reservation consuming most of the log space. Free some reserved records to make space
            ///     available.
            /// </summary>
            StatusLogPinnedReservation = 0xC01A0030,

            // Xddm Video Facility Error codes (videoprt.sys,

            /// <summary>
            ///     MessageId: StatusVideoHungDisplayDriverThread
            ///     MessageText:
            ///     {Display Driver Stopped Responding}
            ///     The %hs display driver has stopped working normally. Save your work and reboot the system to restore full display
            ///     functionality. The next time you reboot the machine a dialog will be displayed giving you a chance to upload data
            ///     about this failure to Microsoft.
            /// </summary>
            StatusVideoHungDisplayDriverThread = 0xC01B00EA,

            /// <summary>
            ///     MessageId: StatusVideoHungDisplayDriverThreadRecovered
            ///     MessageText:
            ///     {Display Driver Stopped Responding and recovered}
            ///     The %hs display driver has stopped working normally. The recovery had been performed.
            /// </summary>
            StatusVideoHungDisplayDriverThreadRecovered = 0x801B00EB,

            /// <summary>
            ///     MessageId: StatusVideoDriverDebugReportRequest
            ///     MessageText:
            ///     {Display Driver Recovered From Failure}
            ///     The %hs display driver has detected and recovered from a failure. Some graphical operations may have failed. The
            ///     next time you reboot the machine a dialog will be displayed giving you a chance to upload data about this failure
            ///     to Microsoft.
            /// </summary>
            StatusVideoDriverDebugReportRequest = 0x401B00EC,

            // Monitor Facility Error codes (monitor.sys,

            /// <summary>
            ///     MessageId: StatusMonitorNoDescriptor
            ///     MessageText:
            ///     Monitor descriptor could not be obtained.
            /// </summary>
            StatusMonitorNoDescriptor = 0xC01D0001,

            /// <summary>
            ///     MessageId: StatusMonitorUnknownDescriptorFormat
            ///     MessageText:
            ///     Format of the obtained monitor descriptor is not supported by this release.
            /// </summary>
            StatusMonitorUnknownDescriptorFormat = 0xC01D0002,

            /// <summary>
            ///     MessageId: StatusMonitorInvalidDescriptorChecksum
            ///     MessageText:
            ///     Checksum of the obtained monitor descriptor is invalid.
            /// </summary>
            StatusMonitorInvalidDescriptorChecksum = 0xC01D0003,

            /// <summary>
            ///     MessageId: StatusMonitorInvalidStandardTimingBlock
            ///     MessageText:
            ///     Monitor descriptor contains an invalid standard timing block.
            /// </summary>
            StatusMonitorInvalidStandardTimingBlock = 0xC01D0004,

            /// <summary>
            ///     MessageId: StatusMonitorWmiDatablockRegistrationFailed
            ///     MessageText:
            ///     Wmi data block registration failed for one of the MSMonitorClass Wmi subclasses.
            /// </summary>
            StatusMonitorWmiDatablockRegistrationFailed = 0xC01D0005,

            /// <summary>
            ///     MessageId: StatusMonitorInvalidSerialNumberMondscBlock
            ///     MessageText:
            ///     Provided monitor descriptor block is either corrupted or does not contain monitor's detailed serial number.
            /// </summary>
            StatusMonitorInvalidSerialNumberMondscBlock = 0xC01D0006,

            /// <summary>
            ///     MessageId: StatusMonitorInvalidUserFriendlyMondscBlock
            ///     MessageText:
            ///     Provided monitor descriptor block is either corrupted or does not contain monitor's user friendly name.
            /// </summary>
            StatusMonitorInvalidUserFriendlyMondscBlock = 0xC01D0007,

            /// <summary>
            ///     MessageId: StatusMonitorNoMoreDescriptorData
            ///     MessageText:
            ///     There is no monitor descriptor data at the specified (offset, size, region.
            /// </summary>
            StatusMonitorNoMoreDescriptorData = 0xC01D0008,

            /// <summary>
            ///     MessageId: StatusMonitorInvalidDetailedTimingBlock
            ///     MessageText:
            ///     Monitor descriptor contains an invalid detailed timing block.
            /// </summary>
            StatusMonitorInvalidDetailedTimingBlock = 0xC01D0009,

            /// <summary>
            ///     MessageId: StatusMonitorInvalidManufactureDate
            ///     MessageText:
            ///     Monitor descriptor contains invalid manufacture date.
            /// </summary>
            StatusMonitorInvalidManufactureDate = 0xC01D000A,

            // Graphics Facility Error codes (dxg.sys, dxgkrnl.sys,

            // Common Windows Graphics Kernel Subsystem status codes {= 0x0000..= 0x00ff}

            /// <summary>
            ///     MessageId: StatusGraphicsNotExclusiveModeOwner
            ///     MessageText:
            ///     Exclusive mode ownership is needed to create unmanaged primary allocation.
            /// </summary>
            StatusGraphicsNotExclusiveModeOwner = 0xC01E0000,

            /// <summary>
            ///     MessageId: StatusGraphicsInsufficientDmaBuffer
            ///     MessageText:
            ///     The driver needs more Dma buffer space in order to complete the requested operation.
            /// </summary>
            StatusGraphicsInsufficientDmaBuffer = 0xC01E0001,

            /// <summary>
            ///     MessageId: StatusGraphicsInvalidDisplayAdapter
            ///     MessageText:
            ///     Specified display adapter handle is invalid.
            /// </summary>
            StatusGraphicsInvalidDisplayAdapter = 0xC01E0002,

            /// <summary>
            ///     MessageId: StatusGraphicsAdapterWasReset
            ///     MessageText:
            ///     Specified display adapter and all of its state has been reset.
            /// </summary>
            StatusGraphicsAdapterWasReset = 0xC01E0003,

            /// <summary>
            ///     MessageId: StatusGraphicsInvalidDriverModel
            ///     MessageText:
            ///     The driver stack doesn't match the expected driver model.
            /// </summary>
            StatusGraphicsInvalidDriverModel = 0xC01E0004,

            /// <summary>
            ///     MessageId: StatusGraphicsPresentModeChanged
            ///     MessageText:
            ///     Present happened but ended up into the changed desktop mode
            /// </summary>
            StatusGraphicsPresentModeChanged = 0xC01E0005,

            /// <summary>
            ///     MessageId: StatusGraphicsPresentOccluded
            ///     MessageText:
            ///     Nothing to present due to desktop occlusion
            /// </summary>
            StatusGraphicsPresentOccluded = 0xC01E0006,

            /// <summary>
            ///     MessageId: StatusGraphicsPresentDenied
            ///     MessageText:
            ///     Not able to present due to denial of desktop access
            /// </summary>
            StatusGraphicsPresentDenied = 0xC01E0007,

            /// <summary>
            ///     MessageId: StatusGraphicsCannotcolorconvert
            ///     MessageText:
            ///     Not able to present with color convertion
            /// </summary>
            StatusGraphicsCannotcolorconvert = 0xC01E0008,

            /// <summary>
            ///     MessageId: StatusGraphicsDriverMismatch
            ///     MessageText:
            ///     The kernel driver detected a version mismatch between it and the user mode driver.
            /// </summary>
            StatusGraphicsDriverMismatch = 0xC01E0009,

            /// <summary>
            ///     MessageId: StatusGraphicsPartialDataPopulated
            ///     MessageText:
            ///     Specified buffer is not big enough to contain entire requested dataset. Partial data populated upto the size of the
            ///     buffer. Caller needs to provide buffer of size as specified in the partially populated buffer's content (interface
            ///     specific,.
            /// </summary>
            StatusGraphicsPartialDataPopulated = 0x401E000A,

            /// <summary>
            ///     MessageId: StatusGraphicsPresentRedirectionDisabled
            ///     MessageText:
            ///     Present redirection is disabled (desktop windowing management subsystem is off,.
            /// </summary>
            StatusGraphicsPresentRedirectionDisabled = 0xC01E000B,

            /// <summary>
            ///     MessageId: StatusGraphicsPresentUnoccluded
            ///     MessageText:
            ///     Previous exclusive VidPn source owner has released its ownership
            /// </summary>
            StatusGraphicsPresentUnoccluded = 0xC01E000C,

            // Video Memory Manager (VidMM, specific status codes {= 0x0100..= 0x01ff}

            /// <summary>
            ///     MessageId: StatusGraphicsNoVideoMemory
            ///     MessageText:
            ///     Not enough video memory available to complete the operation.
            /// </summary>
            StatusGraphicsNoVideoMemory = 0xC01E0100,

            /// <summary>
            ///     MessageId: StatusGraphicsCantLockMemory
            ///     MessageText:
            ///     Couldn't probe and lock the underlying memory of an allocation.
            /// </summary>
            StatusGraphicsCantLockMemory = 0xC01E0101,

            /// <summary>
            ///     MessageId: StatusGraphicsAllocationBusy
            ///     MessageText:
            ///     The allocation is currently busy.
            /// </summary>
            StatusGraphicsAllocationBusy = 0xC01E0102,

            /// <summary>
            ///     MessageId: StatusGraphicsTooManyReferences
            ///     MessageText:
            ///     An object being referenced has already reached the maximum reference count and can't be referenced any further.
            /// </summary>
            StatusGraphicsTooManyReferences = 0xC01E0103,

            /// <summary>
            ///     MessageId: StatusGraphicsTryAgainLater
            ///     MessageText:
            ///     A problem couldn't be solved due to some currently existing condition. The problem should be tried again later.
            /// </summary>
            StatusGraphicsTryAgainLater = 0xC01E0104,

            /// <summary>
            ///     MessageId: StatusGraphicsTryAgainNow
            ///     MessageText:
            ///     A problem couldn't be solved due to some currently existing condition. The problem should be tried again
            ///     immediately.
            /// </summary>
            StatusGraphicsTryAgainNow = 0xC01E0105,

            /// <summary>
            ///     MessageId: StatusGraphicsAllocationInvalid
            ///     MessageText:
            ///     The allocation is invalid.
            /// </summary>
            StatusGraphicsAllocationInvalid = 0xC01E0106,

            /// <summary>
            ///     MessageId: StatusGraphicsUnswizzlingApertureUnavailable
            ///     MessageText:
            ///     No more unswizzling aperture are currently available.
            /// </summary>
            StatusGraphicsUnswizzlingApertureUnavailable = 0xC01E0107,

            /// <summary>
            ///     MessageId: StatusGraphicsUnswizzlingApertureUnsupported
            ///     MessageText:
            ///     The current allocation can't be unswizzled by an aperture.
            /// </summary>
            StatusGraphicsUnswizzlingApertureUnsupported = 0xC01E0108,

            /// <summary>
            ///     MessageId: StatusGraphicsCantEvictPinnedAllocation
            ///     MessageText:
            ///     The request failed because a pinned allocation can't be evicted.
            /// </summary>
            StatusGraphicsCantEvictPinnedAllocation = 0xC01E0109,

            /// <summary>
            ///     MessageId: StatusGraphicsInvalidAllocationUsage
            ///     MessageText:
            ///     The allocation can't be used from it's current segment location for the specified operation.
            /// </summary>
            StatusGraphicsInvalidAllocationUsage = 0xC01E0110,

            /// <summary>
            ///     MessageId: StatusGraphicsCantRenderLockedAllocation
            ///     MessageText:
            ///     A locked allocation can't be used in the current command buffer.
            /// </summary>
            StatusGraphicsCantRenderLockedAllocation = 0xC01E0111,

            /// <summary>
            ///     MessageId: StatusGraphicsAllocationClosed
            ///     MessageText:
            ///     The allocation being referenced has been closed permanently.
            /// </summary>
            StatusGraphicsAllocationClosed = 0xC01E0112,

            /// <summary>
            ///     MessageId: StatusGraphicsInvalidAllocationInstance
            ///     MessageText:
            ///     An invalid allocation instance is being referenced.
            /// </summary>
            StatusGraphicsInvalidAllocationInstance = 0xC01E0113,

            /// <summary>
            ///     MessageId: StatusGraphicsInvalidAllocationHandle
            ///     MessageText:
            ///     An invalid allocation handle is being referenced.
            /// </summary>
            StatusGraphicsInvalidAllocationHandle = 0xC01E0114,

            /// <summary>
            ///     MessageId: StatusGraphicsWrongAllocationDevice
            ///     MessageText:
            ///     The allocation being referenced doesn't belong to the current device.
            /// </summary>
            StatusGraphicsWrongAllocationDevice = 0xC01E0115,

            /// <summary>
            ///     MessageId: StatusGraphicsAllocationContentLost
            ///     MessageText:
            ///     The specified allocation lost its content.
            /// </summary>
            StatusGraphicsAllocationContentLost = 0xC01E0116,

            // Video Gpu Scheduler (VidSch, specific status codes {= 0x0200..= 0x02ff}

            /// <summary>
            ///     MessageId: StatusGraphicsGpuExceptionOnDevice
            ///     MessageText:
            ///     Gpu exception is detected on the given device. The device is not able to be scheduled.
            /// </summary>
            StatusGraphicsGpuExceptionOnDevice = 0xC01E0200,

            // Video Present Network Management (VidPNMgr, specific status codes {= 0x0300..= 0x03ff}

            /// <summary>
            ///     MessageId: StatusGraphicsInvalidVidpnTopology
            ///     MessageText:
            ///     Specified VidPN topology is invalid.
            /// </summary>
            StatusGraphicsInvalidVidpnTopology = 0xC01E0300,

            /// <summary>
            ///     MessageId: StatusGraphicsVidpnTopologyNotSupported
            ///     MessageText:
            ///     Specified VidPN topology is valid but is not supported by this model of the display adapter.
            /// </summary>
            StatusGraphicsVidpnTopologyNotSupported = 0xC01E0301,

            /// <summary>
            ///     MessageId: StatusGraphicsVidpnTopologyCurrentlyNotSupported
            ///     MessageText:
            ///     Specified VidPN topology is valid but is not supported by the display adapter at this time, due to current
            ///     allocation of its resources.
            /// </summary>
            StatusGraphicsVidpnTopologyCurrentlyNotSupported = 0xC01E0302,

            /// <summary>
            ///     MessageId: StatusGraphicsInvalidVidpn
            ///     MessageText:
            ///     Specified VidPN handle is invalid.
            /// </summary>
            StatusGraphicsInvalidVidpn = 0xC01E0303,

            /// <summary>
            ///     MessageId: StatusGraphicsInvalidVideoPresentSource
            ///     MessageText:
            ///     Specified video present source is invalid.
            /// </summary>
            StatusGraphicsInvalidVideoPresentSource = 0xC01E0304,

            /// <summary>
            ///     MessageId: StatusGraphicsInvalidVideoPresentTarget
            ///     MessageText:
            ///     Specified video present target is invalid.
            /// </summary>
            StatusGraphicsInvalidVideoPresentTarget = 0xC01E0305,

            /// <summary>
            ///     MessageId: StatusGraphicsVidpnModalityNotSupported
            ///     MessageText:
            ///     Specified VidPN modality is not supported (e.g. at least two of the pinned modes are not cofunctiona,.
            /// </summary>
            StatusGraphicsVidpnModalityNotSupported = 0xC01E0306,

            /// <summary>
            ///     MessageId: StatusGraphicsModeNotPinned
            ///     MessageText:
            ///     No mode is pinned on the specified VidPN source/target.
            /// </summary>
            StatusGraphicsModeNotPinned = 0x401E0307,

            /// <summary>
            ///     MessageId: StatusGraphicsInvalidVidpnSourcemodeset
            ///     MessageText:
            ///     Specified VidPN source mode set is invalid.
            /// </summary>
            StatusGraphicsInvalidVidpnSourcemodeset = 0xC01E0308,

            /// <summary>
            ///     MessageId: StatusGraphicsInvalidVidpnTargetmodeset
            ///     MessageText:
            ///     Specified VidPN target mode set is invalid.
            /// </summary>
            StatusGraphicsInvalidVidpnTargetmodeset = 0xC01E0309,

            /// <summary>
            ///     MessageId: StatusGraphicsInvalidFrequency
            ///     MessageText:
            ///     Specified video signal frequency is invalid.
            /// </summary>
            StatusGraphicsInvalidFrequency = 0xC01E030A,

            /// <summary>
            ///     MessageId: StatusGraphicsInvalidActiveRegion
            ///     MessageText:
            ///     Specified video signal active region is invalid.
            /// </summary>
            StatusGraphicsInvalidActiveRegion = 0xC01E030B,

            /// <summary>
            ///     MessageId: StatusGraphicsInvalidTotalRegion
            ///     MessageText:
            ///     Specified video signal total region is invalid.
            /// </summary>
            StatusGraphicsInvalidTotalRegion = 0xC01E030C,

            /// <summary>
            ///     MessageId: StatusGraphicsInvalidVideoPresentSourceMode
            ///     MessageText:
            ///     Specified video present source mode is invalid.
            /// </summary>
            StatusGraphicsInvalidVideoPresentSourceMode = 0xC01E0310,

            /// <summary>
            ///     MessageId: StatusGraphicsInvalidVideoPresentTargetMode
            ///     MessageText:
            ///     Specified video present target mode is invalid.
            /// </summary>
            StatusGraphicsInvalidVideoPresentTargetMode = 0xC01E0311,

            /// <summary>
            ///     MessageId: StatusGraphicsPinnedModeMustRemainInSet
            ///     MessageText:
            ///     Pinned mode must remain in the set on VidPN's cofunctional modality enumeration.
            /// </summary>
            StatusGraphicsPinnedModeMustRemainInSet = 0xC01E0312,

            /// <summary>
            ///     MessageId: StatusGraphicsPathAlreadyInTopology
            ///     MessageText:
            ///     Specified video present path is already in VidPN's topology.
            /// </summary>
            StatusGraphicsPathAlreadyInTopology = 0xC01E0313,

            /// <summary>
            ///     MessageId: StatusGraphicsModeAlreadyInModeset
            ///     MessageText:
            ///     Specified mode is already in the mode set.
            /// </summary>
            StatusGraphicsModeAlreadyInModeset = 0xC01E0314,

            /// <summary>
            ///     MessageId: StatusGraphicsInvalidVideopresentsourceset
            ///     MessageText:
            ///     Specified video present source set is invalid.
            /// </summary>
            StatusGraphicsInvalidVideopresentsourceset = 0xC01E0315,

            /// <summary>
            ///     MessageId: StatusGraphicsInvalidVideopresenttargetset
            ///     MessageText:
            ///     Specified video present target set is invalid.
            /// </summary>
            StatusGraphicsInvalidVideopresenttargetset = 0xC01E0316,

            /// <summary>
            ///     MessageId: StatusGraphicsSourceAlreadyInSet
            ///     MessageText:
            ///     Specified video present source is already in the video present source set.
            /// </summary>
            StatusGraphicsSourceAlreadyInSet = 0xC01E0317,

            /// <summary>
            ///     MessageId: StatusGraphicsTargetAlreadyInSet
            ///     MessageText:
            ///     Specified video present target is already in the video present target set.
            /// </summary>
            StatusGraphicsTargetAlreadyInSet = 0xC01E0318,

            /// <summary>
            ///     MessageId: StatusGraphicsInvalidVidpnPresentPath
            ///     MessageText:
            ///     Specified VidPN present path is invalid.
            /// </summary>
            StatusGraphicsInvalidVidpnPresentPath = 0xC01E0319,

            /// <summary>
            ///     MessageId: StatusGraphicsNoRecommendedVidpnTopology
            ///     MessageText:
            ///     Miniport has no recommendation for augmentation of the specified VidPN's topology.
            /// </summary>
            StatusGraphicsNoRecommendedVidpnTopology = 0xC01E031A,

            /// <summary>
            ///     MessageId: StatusGraphicsInvalidMonitorFrequencyrangeset
            ///     MessageText:
            ///     Specified monitor frequency range set is invalid.
            /// </summary>
            StatusGraphicsInvalidMonitorFrequencyrangeset = 0xC01E031B,

            /// <summary>
            ///     MessageId: StatusGraphicsInvalidMonitorFrequencyrange
            ///     MessageText:
            ///     Specified monitor frequency range is invalid.
            /// </summary>
            StatusGraphicsInvalidMonitorFrequencyrange = 0xC01E031C,

            /// <summary>
            ///     MessageId: StatusGraphicsFrequencyrangeNotInSet
            ///     MessageText:
            ///     Specified frequency range is not in the specified monitor frequency range set.
            /// </summary>
            StatusGraphicsFrequencyrangeNotInSet = 0xC01E031D,

            /// <summary>
            ///     MessageId: StatusGraphicsNoPreferredMode
            ///     MessageText:
            ///     Specified mode set does not specify preference for one of its modes.
            /// </summary>
            StatusGraphicsNoPreferredMode = 0x401E031E,

            /// <summary>
            ///     MessageId: StatusGraphicsFrequencyrangeAlreadyInSet
            ///     MessageText:
            ///     Specified frequency range is already in the specified monitor frequency range set.
            /// </summary>
            StatusGraphicsFrequencyrangeAlreadyInSet = 0xC01E031F,

            /// <summary>
            ///     MessageId: StatusGraphicsStaleModeset
            ///     MessageText:
            ///     Specified mode set is stale. Please reacquire the new mode set.
            /// </summary>
            StatusGraphicsStaleModeset = 0xC01E0320,

            /// <summary>
            ///     MessageId: StatusGraphicsInvalidMonitorSourcemodeset
            ///     MessageText:
            ///     Specified monitor source mode set is invalid.
            /// </summary>
            StatusGraphicsInvalidMonitorSourcemodeset = 0xC01E0321,

            /// <summary>
            ///     MessageId: StatusGraphicsInvalidMonitorSourceMode
            ///     MessageText:
            ///     Specified monitor source mode is invalid.
            /// </summary>
            StatusGraphicsInvalidMonitorSourceMode = 0xC01E0322,

            /// <summary>
            ///     MessageId: StatusGraphicsNoRecommendedFunctionalVidpn
            ///     MessageText:
            ///     Miniport does not have any recommendation regarding the request to provide a functional VidPN given the current
            ///     display adapter configuration.
            /// </summary>
            StatusGraphicsNoRecommendedFunctionalVidpn = 0xC01E0323,

            /// <summary>
            ///     MessageId: StatusGraphicsModeIdMustBeUnique
            ///     MessageText:
            ///     Id of the specified mode is already used by another mode in the set.
            /// </summary>
            StatusGraphicsModeIdMustBeUnique = 0xC01E0324,

            /// <summary>
            ///     MessageId: StatusGraphicsEmptyAdapterMonitorModeSupportIntersection
            ///     MessageText:
            ///     System failed to determine a mode that is supported by both the display adapter and the monitor connected to it.
            /// </summary>
            StatusGraphicsEmptyAdapterMonitorModeSupportIntersection = 0xC01E0325,

            /// <summary>
            ///     MessageId: StatusGraphicsVideoPresentTargetsLessThanSources
            ///     MessageText:
            ///     Number of video present targets must be greater than or equal to the number of video present sources.
            /// </summary>
            StatusGraphicsVideoPresentTargetsLessThanSources = 0xC01E0326,

            /// <summary>
            ///     MessageId: StatusGraphicsPathNotInTopology
            ///     MessageText:
            ///     Specified present path is not in VidPN's topology.
            /// </summary>
            StatusGraphicsPathNotInTopology = 0xC01E0327,

            /// <summary>
            ///     MessageId: StatusGraphicsAdapterMustHaveAtLeastOneSource
            ///     MessageText:
            ///     Display adapter must have at least one video present source.
            /// </summary>
            StatusGraphicsAdapterMustHaveAtLeastOneSource = 0xC01E0328,

            /// <summary>
            ///     MessageId: StatusGraphicsAdapterMustHaveAtLeastOneTarget
            ///     MessageText:
            ///     Display adapter must have at least one video present target.
            /// </summary>
            StatusGraphicsAdapterMustHaveAtLeastOneTarget = 0xC01E0329,

            /// <summary>
            ///     MessageId: StatusGraphicsInvalidMonitordescriptorset
            ///     MessageText:
            ///     Specified monitor descriptor set is invalid.
            /// </summary>
            StatusGraphicsInvalidMonitordescriptorset = 0xC01E032A,

            /// <summary>
            ///     MessageId: StatusGraphicsInvalidMonitordescriptor
            ///     MessageText:
            ///     Specified monitor descriptor is invalid.
            /// </summary>
            StatusGraphicsInvalidMonitordescriptor = 0xC01E032B,

            /// <summary>
            ///     MessageId: StatusGraphicsMonitordescriptorNotInSet
            ///     MessageText:
            ///     Specified descriptor is not in the specified monitor descriptor set.
            /// </summary>
            StatusGraphicsMonitordescriptorNotInSet = 0xC01E032C,

            /// <summary>
            ///     MessageId: StatusGraphicsMonitordescriptorAlreadyInSet
            ///     MessageText:
            ///     Specified descriptor is already in the specified monitor descriptor set.
            /// </summary>
            StatusGraphicsMonitordescriptorAlreadyInSet = 0xC01E032D,

            /// <summary>
            ///     MessageId: StatusGraphicsMonitordescriptorIdMustBeUnique
            ///     MessageText:
            ///     Id of the specified monitor descriptor is already used by another descriptor in the set.
            /// </summary>
            StatusGraphicsMonitordescriptorIdMustBeUnique = 0xC01E032E,

            /// <summary>
            ///     MessageId: StatusGraphicsInvalidVidpnTargetSubsetType
            ///     MessageText:
            ///     Specified video present target subset type is invalid.
            /// </summary>
            StatusGraphicsInvalidVidpnTargetSubsetType = 0xC01E032F,

            /// <summary>
            ///     MessageId: StatusGraphicsResourcesNotRelated
            ///     MessageText:
            ///     Two or more of the specified resources are not related to each other, as defined by the interface semantics.
            /// </summary>
            StatusGraphicsResourcesNotRelated = 0xC01E0330,

            /// <summary>
            ///     MessageId: StatusGraphicsSourceIdMustBeUnique
            ///     MessageText:
            ///     Id of the specified video present source is already used by another source in the set.
            /// </summary>
            StatusGraphicsSourceIdMustBeUnique = 0xC01E0331,

            /// <summary>
            ///     MessageId: StatusGraphicsTargetIdMustBeUnique
            ///     MessageText:
            ///     Id of the specified video present target is already used by another target in the set.
            /// </summary>
            StatusGraphicsTargetIdMustBeUnique = 0xC01E0332,

            /// <summary>
            ///     MessageId: StatusGraphicsNoAvailableVidpnTarget
            ///     MessageText:
            ///     Specified VidPN source cannot be used because there is no available VidPN target to connect it to.
            /// </summary>
            StatusGraphicsNoAvailableVidpnTarget = 0xC01E0333,

            /// <summary>
            ///     MessageId: StatusGraphicsMonitorCouldNotBeAssociatedWithAdapter
            ///     MessageText:
            ///     Newly arrived monitor could not be associated with a display adapter.
            /// </summary>
            StatusGraphicsMonitorCouldNotBeAssociatedWithAdapter = 0xC01E0334,

            /// <summary>
            ///     MessageId: StatusGraphicsNoVidpnmgr
            ///     MessageText:
            ///     Display adapter in question does not have an associated VidPN manager.
            /// </summary>
            StatusGraphicsNoVidpnmgr = 0xC01E0335,

            /// <summary>
            ///     MessageId: StatusGraphicsNoActiveVidpn
            ///     MessageText:
            ///     VidPN manager of the display adapter in question does not have an active VidPN.
            /// </summary>
            StatusGraphicsNoActiveVidpn = 0xC01E0336,

            /// <summary>
            ///     MessageId: StatusGraphicsStaleVidpnTopology
            ///     MessageText:
            ///     Specified VidPN topology is stale. Please reacquire the new topology.
            /// </summary>
            StatusGraphicsStaleVidpnTopology = 0xC01E0337,

            /// <summary>
            ///     MessageId: StatusGraphicsMonitorNotConnected
            ///     MessageText:
            ///     There is no monitor connected on the specified video present target.
            /// </summary>
            StatusGraphicsMonitorNotConnected = 0xC01E0338,

            /// <summary>
            ///     MessageId: StatusGraphicsSourceNotInTopology
            ///     MessageText:
            ///     Specified source is not part of the specified VidPN's topology.
            /// </summary>
            StatusGraphicsSourceNotInTopology = 0xC01E0339,

            /// <summary>
            ///     MessageId: StatusGraphicsInvalidPrimarysurfaceSize
            ///     MessageText:
            ///     Specified primary surface size is invalid.
            /// </summary>
            StatusGraphicsInvalidPrimarysurfaceSize = 0xC01E033A,

            /// <summary>
            ///     MessageId: StatusGraphicsInvalidVisibleregionSize
            ///     MessageText:
            ///     Specified visible region size is invalid.
            /// </summary>
            StatusGraphicsInvalidVisibleregionSize = 0xC01E033B,

            /// <summary>
            ///     MessageId: StatusGraphicsInvalidStride
            ///     MessageText:
            ///     Specified stride is invalid.
            /// </summary>
            StatusGraphicsInvalidStride = 0xC01E033C,

            /// <summary>
            ///     MessageId: StatusGraphicsInvalidPixelformat
            ///     MessageText:
            ///     Specified pixel format is invalid.
            /// </summary>
            StatusGraphicsInvalidPixelformat = 0xC01E033D,

            /// <summary>
            ///     MessageId: StatusGraphicsInvalidColorbasis
            ///     MessageText:
            ///     Specified color basis is invalid.
            /// </summary>
            StatusGraphicsInvalidColorbasis = 0xC01E033E,

            /// <summary>
            ///     MessageId: StatusGraphicsInvalidPixelvalueaccessmode
            ///     MessageText:
            ///     Specified pixel value access mode is invalid.
            /// </summary>
            StatusGraphicsInvalidPixelvalueaccessmode = 0xC01E033F,

            /// <summary>
            ///     MessageId: StatusGraphicsTargetNotInTopology
            ///     MessageText:
            ///     Specified target is not part of the specified VidPN's topology.
            /// </summary>
            StatusGraphicsTargetNotInTopology = 0xC01E0340,

            /// <summary>
            ///     MessageId: StatusGraphicsNoDisplayModeManagementSupport
            ///     MessageText:
            ///     Failed to acquire display mode management interface.
            /// </summary>
            StatusGraphicsNoDisplayModeManagementSupport = 0xC01E0341,

            /// <summary>
            ///     MessageId: StatusGraphicsVidpnSourceInUse
            ///     MessageText:
            ///     Specified VidPN source is already owned by a Dmm client and cannot be used until that client releases it.
            /// </summary>
            StatusGraphicsVidpnSourceInUse = 0xC01E0342,

            /// <summary>
            ///     MessageId: StatusGraphicsCantAccessActiveVidpn
            ///     MessageText:
            ///     Specified VidPN is active and cannot be accessed.
            /// </summary>
            StatusGraphicsCantAccessActiveVidpn = 0xC01E0343,

            /// <summary>
            ///     MessageId: StatusGraphicsInvalidPathImportanceOrdinal
            ///     MessageText:
            ///     Specified VidPN present path importance ordinal is invalid.
            /// </summary>
            StatusGraphicsInvalidPathImportanceOrdinal = 0xC01E0344,

            /// <summary>
            ///     MessageId: StatusGraphicsInvalidPathContentGeometryTransformation
            ///     MessageText:
            ///     Specified VidPN present path content geometry transformation is invalid.
            /// </summary>
            StatusGraphicsInvalidPathContentGeometryTransformation = 0xC01E0345,

            /// <summary>
            ///     MessageId: StatusGraphicsPathContentGeometryTransformationNotSupported
            ///     MessageText:
            ///     Specified content geometry transformation is not supported on the respective VidPN present path.
            /// </summary>
            StatusGraphicsPathContentGeometryTransformationNotSupported = 0xC01E0346,

            /// <summary>
            ///     MessageId: StatusGraphicsInvalidGammaRamp
            ///     MessageText:
            ///     Specified gamma ramp is invalid.
            /// </summary>
            StatusGraphicsInvalidGammaRamp = 0xC01E0347,

            /// <summary>
            ///     MessageId: StatusGraphicsGammaRampNotSupported
            ///     MessageText:
            ///     Specified gamma ramp is not supported on the respective VidPN present path.
            /// </summary>
            StatusGraphicsGammaRampNotSupported = 0xC01E0348,

            /// <summary>
            ///     MessageId: StatusGraphicsMultisamplingNotSupported
            ///     MessageText:
            ///     Multi-sampling is not supported on the respective VidPN present path.
            /// </summary>
            StatusGraphicsMultisamplingNotSupported = 0xC01E0349,

            /// <summary>
            ///     MessageId: StatusGraphicsModeNotInModeset
            ///     MessageText:
            ///     Specified mode is not in the specified mode set.
            /// </summary>
            StatusGraphicsModeNotInModeset = 0xC01E034A,

            /// <summary>
            ///     MessageId: StatusGraphicsDatasetIsEmpty
            ///     MessageText:
            ///     Specified data set (e.g. mode set, frequency range set, descriptor set, topology, etc., is empty.
            /// </summary>
            StatusGraphicsDatasetIsEmpty = 0x401E034B,

            /// <summary>
            ///     MessageId: StatusGraphicsNoMoreElementsInDataset
            ///     MessageText:
            ///     Specified data set (e.g. mode set, frequency range set, descriptor set, topology, etc., does not contain any more
            ///     elements.
            /// </summary>
            StatusGraphicsNoMoreElementsInDataset = 0x401E034C,

            /// <summary>
            ///     MessageId: StatusGraphicsInvalidVidpnTopologyRecommendationReason
            ///     MessageText:
            ///     Specified VidPN topology recommendation reason is invalid.
            /// </summary>
            StatusGraphicsInvalidVidpnTopologyRecommendationReason = 0xC01E034D,

            /// <summary>
            ///     MessageId: StatusGraphicsInvalidPathContentType
            ///     MessageText:
            ///     Specified VidPN present path content type is invalid.
            /// </summary>
            StatusGraphicsInvalidPathContentType = 0xC01E034E,

            /// <summary>
            ///     MessageId: StatusGraphicsInvalidCopyprotectionType
            ///     MessageText:
            ///     Specified VidPN present path copy protection type is invalid.
            /// </summary>
            StatusGraphicsInvalidCopyprotectionType = 0xC01E034F,

            /// <summary>
            ///     MessageId: StatusGraphicsUnassignedModesetAlreadyExists
            ///     MessageText:
            ///     No more than one unassigned mode set can exist at any given time for a given VidPN source/target.
            /// </summary>
            StatusGraphicsUnassignedModesetAlreadyExists = 0xC01E0350,

            /// <summary>
            ///     MessageId: StatusGraphicsPathContentGeometryTransformationNotPinned
            ///     MessageText:
            ///     Specified content transformation is not pinned on the specified VidPN present path.
            /// </summary>
            StatusGraphicsPathContentGeometryTransformationNotPinned = 0x401E0351,

            /// <summary>
            ///     MessageId: StatusGraphicsInvalidScanlineOrdering
            ///     MessageText:
            ///     Specified scanline ordering type is invalid.
            /// </summary>
            StatusGraphicsInvalidScanlineOrdering = 0xC01E0352,

            /// <summary>
            ///     MessageId: StatusGraphicsTopologyChangesNotAllowed
            ///     MessageText:
            ///     Topology changes are not allowed for the specified VidPN.
            /// </summary>
            StatusGraphicsTopologyChangesNotAllowed = 0xC01E0353,

            /// <summary>
            ///     MessageId: StatusGraphicsNoAvailableImportanceOrdinals
            ///     MessageText:
            ///     All available importance ordinals are already used in specified topology.
            /// </summary>
            StatusGraphicsNoAvailableImportanceOrdinals = 0xC01E0354,

            /// <summary>
            ///     MessageId: StatusGraphicsIncompatiblePrivateFormat
            ///     MessageText:
            ///     Specified primary surface has a different private format attribute than the current primary surface
            /// </summary>
            StatusGraphicsIncompatiblePrivateFormat = 0xC01E0355,

            /// <summary>
            ///     MessageId: StatusGraphicsInvalidModePruningAlgorithm
            ///     MessageText:
            ///     Specified mode pruning algorithm is invalid
            /// </summary>
            StatusGraphicsInvalidModePruningAlgorithm = 0xC01E0356,

            /// <summary>
            ///     MessageId: StatusGraphicsInvalidMonitorCapabilityOrigin
            ///     MessageText:
            ///     Specified monitor capability origin is invalid.
            /// </summary>
            StatusGraphicsInvalidMonitorCapabilityOrigin = 0xC01E0357,

            /// <summary>
            ///     MessageId: StatusGraphicsInvalidMonitorFrequencyrangeConstraint
            ///     MessageText:
            ///     Specified monitor frequency range constraint is invalid.
            /// </summary>
            StatusGraphicsInvalidMonitorFrequencyrangeConstraint = 0xC01E0358,

            /// <summary>
            ///     MessageId: StatusGraphicsMaxNumPathsReached
            ///     MessageText:
            ///     Maximum supported number of present paths has been reached.
            /// </summary>
            StatusGraphicsMaxNumPathsReached = 0xC01E0359,

            /// <summary>
            ///     MessageId: StatusGraphicsCancelVidpnTopologyAugmentation
            ///     MessageText:
            ///     Miniport requested that augmentation be cancelled for the specified source of the specified VidPN's topology.
            /// </summary>
            StatusGraphicsCancelVidpnTopologyAugmentation = 0xC01E035A,

            /// <summary>
            ///     MessageId: StatusGraphicsInvalidClientType
            ///     MessageText:
            ///     Specified client type was not recognized.
            /// </summary>
            StatusGraphicsInvalidClientType = 0xC01E035B,

            /// <summary>
            ///     MessageId: StatusGraphicsClientvidpnNotSet
            ///     MessageText:
            ///     Client VidPN is not set on this adapter (e.g. no user mode initiated mode changes took place on this adapter yet,.
            /// </summary>
            StatusGraphicsClientvidpnNotSet = 0xC01E035C,

            // Port specific status codes {= 0x0400..= 0x04ff}

            /// <summary>
            ///     MessageId: StatusGraphicsSpecifiedChildAlreadyConnected
            ///     MessageText:
            ///     Specified display adapter child device already has an external device connected to it.
            /// </summary>
            StatusGraphicsSpecifiedChildAlreadyConnected = 0xC01E0400,

            /// <summary>
            ///     MessageId: StatusGraphicsChildDescriptorNotSupported
            ///     MessageText:
            ///     Specified display adapter child device does not support descriptor exposure.
            /// </summary>
            StatusGraphicsChildDescriptorNotSupported = 0xC01E0401,

            /// <summary>
            ///     MessageId: StatusGraphicsUnknownChildStatus
            ///     MessageText:
            ///     Child device presence was not reliably detected.
            /// </summary>
            StatusGraphicsUnknownChildStatus = 0x401E042F,

            /// <summary>
            ///     MessageId: StatusGraphicsNotALinkedAdapter
            ///     MessageText:
            ///     The display adapter is not linked to any other adapters.
            /// </summary>
            StatusGraphicsNotALinkedAdapter = 0xC01E0430,

            /// <summary>
            ///     MessageId: StatusGraphicsLeadlinkNotEnumerated
            ///     MessageText:
            ///     Lead adapter in a linked configuration was not enumerated yet.
            /// </summary>
            StatusGraphicsLeadlinkNotEnumerated = 0xC01E0431,

            /// <summary>
            ///     MessageId: StatusGraphicsChainlinksNotEnumerated
            ///     MessageText:
            ///     Some chain adapters in a linked configuration were not enumerated yet.
            /// </summary>
            StatusGraphicsChainlinksNotEnumerated = 0xC01E0432,

            /// <summary>
            ///     MessageId: StatusGraphicsAdapterChainNotReady
            ///     MessageText:
            ///     The chain of linked adapters is not ready to start because of an unknown failure.
            /// </summary>
            StatusGraphicsAdapterChainNotReady = 0xC01E0433,

            /// <summary>
            ///     MessageId: StatusGraphicsChainlinksNotStarted
            ///     MessageText:
            ///     An attempt was made to start a lead link display adapter when the chain links were not started yet.
            /// </summary>
            StatusGraphicsChainlinksNotStarted = 0xC01E0434,

            /// <summary>
            ///     MessageId: StatusGraphicsChainlinksNotPoweredOn
            ///     MessageText:
            ///     An attempt was made to power up a lead link display adapter when the chain links were powered down.
            /// </summary>
            StatusGraphicsChainlinksNotPoweredOn = 0xC01E0435,

            /// <summary>
            ///     MessageId: StatusGraphicsInconsistentDeviceLinkState
            ///     MessageText:
            ///     The adapter link was found to be in an inconsistent state. Not all adapters are in an expected Pnp/Power state.
            /// </summary>
            StatusGraphicsInconsistentDeviceLinkState = 0xC01E0436,

            /// <summary>
            ///     MessageId: StatusGraphicsLeadlinkStartDeferred
            ///     MessageText:
            ///     Starting the leadlink adapter has been deferred temporarily.
            /// </summary>
            StatusGraphicsLeadlinkStartDeferred = 0x401E0437,

            /// <summary>
            ///     MessageId: StatusGraphicsNotPostDeviceDriver
            ///     MessageText:
            ///     The driver trying to start is not the same as the driver for the POSTed display adapter.
            /// </summary>
            StatusGraphicsNotPostDeviceDriver = 0xC01E0438,

            /// <summary>
            ///     MessageId: StatusGraphicsPollingTooFrequently
            ///     MessageText:
            ///     The display adapter is being polled for children too frequently at the same polling level.
            /// </summary>
            StatusGraphicsPollingTooFrequently = 0x401E0439,

            /// <summary>
            ///     MessageId: StatusGraphicsStartDeferred
            ///     MessageText:
            ///     Starting the adapter has been deferred temporarily.
            /// </summary>
            StatusGraphicsStartDeferred = 0x401E043A,

            /// <summary>
            ///     MessageId: StatusGraphicsAdapterAccessNotExcluded
            ///     MessageText:
            ///     An operation is being attempted that requires the display adapter to be in a quiescent state.
            /// </summary>
            StatusGraphicsAdapterAccessNotExcluded = 0xC01E043B,

            // Opm, Pvp and Uab status codes {= 0x0500..= 0x057F}

            /// <summary>
            ///     MessageId: StatusGraphicsOpmNotSupported
            ///     MessageText:
            ///     The driver does not support Opm.
            /// </summary>
            StatusGraphicsOpmNotSupported = 0xC01E0500,

            /// <summary>
            ///     MessageId: StatusGraphicsCoppNotSupported
            ///     MessageText:
            ///     The driver does not support Copp.
            /// </summary>
            StatusGraphicsCoppNotSupported = 0xC01E0501,

            /// <summary>
            ///     MessageId: StatusGraphicsUabNotSupported
            ///     MessageText:
            ///     The driver does not support Uab.
            /// </summary>
            StatusGraphicsUabNotSupported = 0xC01E0502,

            /// <summary>
            ///     MessageId: StatusGraphicsOpmInvalidEncryptedParameters
            ///     MessageText:
            ///     The specified encrypted parameters are invalid.
            /// </summary>
            StatusGraphicsOpmInvalidEncryptedParameters = 0xC01E0503,

            /// <summary>
            ///     MessageId: StatusGraphicsOpmNoProtectedOutputsExist
            ///     MessageText:
            ///     The Gdi display device passed to this function does not have any active protected outputs.
            /// </summary>
            StatusGraphicsOpmNoProtectedOutputsExist = 0xC01E0505,

            /// <summary>
            ///     MessageId: StatusGraphicsOpmInternalError
            ///     MessageText:
            ///     An internal error caused an operation to fail.
            /// </summary>
            StatusGraphicsOpmInternalError = 0xC01E050B,

            /// <summary>
            ///     MessageId: StatusGraphicsOpmInvalidHandle
            ///     MessageText:
            ///     The function failed because the caller passed in an invalid Opm user mode handle.
            /// </summary>
            StatusGraphicsOpmInvalidHandle = 0xC01E050C,

            /// <summary>
            ///     MessageId: StatusGraphicsPvpInvalidCertificateLength
            ///     MessageText:
            ///     A certificate could not be returned because the certificate buffer passed to the function was too small.
            /// </summary>
            StatusGraphicsPvpInvalidCertificateLength = 0xC01E050E,

            /// <summary>
            ///     MessageId: StatusGraphicsOpmSpanningModeEnabled
            ///     MessageText:
            ///     The DxgkDdiOpmCreateProtectedOutput function could not create a protected output because the Video Present Target
            ///     is in spanning mode.
            /// </summary>
            StatusGraphicsOpmSpanningModeEnabled = 0xC01E050F,

            /// <summary>
            ///     MessageId: StatusGraphicsOpmTheaterModeEnabled
            ///     MessageText:
            ///     The DxgkDdiOpmCreateProtectedOutput function could not create a protected output because the Video Present Target
            ///     is in theater mode.
            /// </summary>
            StatusGraphicsOpmTheaterModeEnabled = 0xC01E0510,

            /// <summary>
            ///     MessageId: StatusGraphicsPvpHfsFailed
            ///     MessageText:
            ///     The function failed because the display adapter's Hardware Functionality Scan failed to validate the graphics
            ///     hardware.
            /// </summary>
            StatusGraphicsPvpHfsFailed = 0xC01E0511,

            /// <summary>
            ///     MessageId: StatusGraphicsOpmInvalidSrm
            ///     MessageText:
            ///     The Hdcp System Renewability Message passed to this function did not comply with section 5 of the Hdcp 1.1
            ///     specification.
            /// </summary>
            StatusGraphicsOpmInvalidSrm = 0xC01E0512,

            /// <summary>
            ///     MessageId: StatusGraphicsOpmOutputDoesNotSupportHdcp
            ///     MessageText:
            ///     The protected output cannot enable the High-bandwidth Digital Content Protection (Hdcp, System because it does not
            ///     support Hdcp.
            /// </summary>
            StatusGraphicsOpmOutputDoesNotSupportHdcp = 0xC01E0513,

            /// <summary>
            ///     MessageId: StatusGraphicsOpmOutputDoesNotSupportAcp
            ///     MessageText:
            ///     The protected output cannot enable Analogue Copy Protection (Acp, because it does not support Acp.
            /// </summary>
            StatusGraphicsOpmOutputDoesNotSupportAcp = 0xC01E0514,

            /// <summary>
            ///     MessageId: StatusGraphicsOpmOutputDoesNotSupportCgmsa
            ///     MessageText:
            ///     The protected output cannot enable the Content Generation Management System Analogue (Cgms-A, protection technology
            ///     because it does not support Cgms-A.
            /// </summary>
            StatusGraphicsOpmOutputDoesNotSupportCgmsa = 0xC01E0515,

            /// <summary>
            ///     MessageId: StatusGraphicsOpmHdcpSrmNeverSet
            ///     MessageText:
            ///     The DxgkDdiOPMGetInformation function cannot return the version of the Srm being used because the application never
            ///     successfully passed an Srm to the protected output.
            /// </summary>
            StatusGraphicsOpmHdcpSrmNeverSet = 0xC01E0516,

            /// <summary>
            ///     MessageId: StatusGraphicsOpmResolutionTooHigh
            ///     MessageText:
            ///     The DxgkDdiOPMConfigureProtectedOutput function cannot enable the specified output protection technology because
            ///     the output's screen resolution is too high.
            /// </summary>
            StatusGraphicsOpmResolutionTooHigh = 0xC01E0517,

            /// <summary>
            ///     MessageId: StatusGraphicsOpmAllHdcpHardwareAlreadyInUse
            ///     MessageText:
            ///     The DxgkDdiOPMConfigureProtectedOutput function cannot enable Hdcp because the display adapter's Hdcp hardware is
            ///     already being used by other physical outputs.
            /// </summary>
            StatusGraphicsOpmAllHdcpHardwareAlreadyInUse = 0xC01E0518,

            /// <summary>
            ///     MessageId: StatusGraphicsOpmProtectedOutputNoLongerExists
            ///     MessageText:
            ///     The operating system asynchronously destroyed this Opm protected output because the operating system's state
            ///     changed. This error typically occurs because the monitor Pdo associated with this protected output was removed, the
            ///     monitor Pdo associated with this protected output was stopped, or the protected output's session became a
            ///     non-console session.
            /// </summary>
            StatusGraphicsOpmProtectedOutputNoLongerExists = 0xC01E051A,

            /// <summary>
            ///     MessageId: StatusGraphicsOpmProtectedOutputDoesNotHaveCoppSemantics
            ///     MessageText:
            ///     Either the DxgkDdiOPMGetCOPPCompatibleInformation, DxgkDdiOPMGetInformation, or DxgkDdiOPMConfigureProtectedOutput
            ///     function failed. This error is returned when the caller tries to use a Copp specific command while the protected
            ///     output has Opm semantics only.
            /// </summary>
            StatusGraphicsOpmProtectedOutputDoesNotHaveCoppSemantics = 0xC01E051C,

            /// <summary>
            ///     MessageId: StatusGraphicsOpmInvalidInformationRequest
            ///     MessageText:
            ///     The DxgkDdiOPMGetInformation and DxgkDdiOPMGetCOPPCompatibleInformation functions return this error code if the
            ///     passed in sequence number is not the expected sequence number or the passed in Omac value is invalid.
            /// </summary>
            StatusGraphicsOpmInvalidInformationRequest = 0xC01E051D,

            /// <summary>
            ///     MessageId: StatusGraphicsOpmDriverInternalError
            ///     MessageText:
            ///     The function failed because an unexpected error occurred inside of a display driver.
            /// </summary>
            StatusGraphicsOpmDriverInternalError = 0xC01E051E,

            /// <summary>
            ///     MessageId: StatusGraphicsOpmProtectedOutputDoesNotHaveOpmSemantics
            ///     MessageText:
            ///     Either the DxgkDdiOPMGetCOPPCompatibleInformation, DxgkDdiOPMGetInformation, or DxgkDdiOPMConfigureProtectedOutput
            ///     function failed. This error is returned when the caller tries to use an Opm specific command while the protected
            ///     output has Copp semantics only.
            /// </summary>
            StatusGraphicsOpmProtectedOutputDoesNotHaveOpmSemantics = 0xC01E051F,

            /// <summary>
            ///     MessageId: StatusGraphicsOpmSignalingNotSupported
            ///     MessageText:
            ///     The DxgkDdiOPMGetCOPPCompatibleInformation and DxgkDdiOPMConfigureProtectedOutput functions return this error if
            ///     the display driver does not support the DxgkmdtOpmGetAcpAndCgmsaSignaling and DxgkmdtOpmSetAcpAndCgmsaSignaling
            ///     GUIDs.
            /// </summary>
            StatusGraphicsOpmSignalingNotSupported = 0xC01E0520,

            /// <summary>
            ///     MessageId: StatusGraphicsOpmInvalidConfigurationRequest
            ///     MessageText:
            ///     The DxgkDdiOPMConfigureProtectedOutput function returns this error code if the passed in sequence number is not the
            ///     expected sequence number or the passed in Omac value is invalid.
            /// </summary>
            StatusGraphicsOpmInvalidConfigurationRequest = 0xC01E0521,

            // Monitor Configuration Api status codes {= 0x0580..= 0x05DF}

            /// <summary>
            ///     MessageId: StatusGraphicsI2cNotSupported
            ///     MessageText:
            ///     The monitor connected to the specified video output does not have an I2c bus.
            /// </summary>
            StatusGraphicsI2cNotSupported = 0xC01E0580,

            /// <summary>
            ///     MessageId: StatusGraphicsI2cDeviceDoesNotExist
            ///     MessageText:
            ///     No device on the I2c bus has the specified address.
            /// </summary>
            StatusGraphicsI2cDeviceDoesNotExist = 0xC01E0581,

            /// <summary>
            ///     MessageId: StatusGraphicsI2cErrorTransmittingData
            ///     MessageText:
            ///     An error occurred while transmitting data to the device on the I2c bus.
            /// </summary>
            StatusGraphicsI2cErrorTransmittingData = 0xC01E0582,

            /// <summary>
            ///     MessageId: StatusGraphicsI2cErrorReceivingData
            ///     MessageText:
            ///     An error occurred while receiving data from the device on the I2c bus.
            /// </summary>
            StatusGraphicsI2cErrorReceivingData = 0xC01E0583,

            /// <summary>
            ///     MessageId: StatusGraphicsDdcciVcpNotSupported
            ///     MessageText:
            ///     The monitor does not support the specified Vcp code.
            /// </summary>
            StatusGraphicsDdcciVcpNotSupported = 0xC01E0584,

            /// <summary>
            ///     MessageId: StatusGraphicsDdcciInvalidData
            ///     MessageText:
            ///     The data received from the monitor is invalid.
            /// </summary>
            StatusGraphicsDdcciInvalidData = 0xC01E0585,

            /// <summary>
            ///     MessageId: StatusGraphicsDdcciMonitorReturnedInvalidTimingStatusByte
            ///     MessageText:
            ///     The function failed because a monitor returned an invalid Timing Status byte when the operating system used the
            ///     Ddc/Ci Get Timing Report and Timing Message command to get a timing report from a monitor.
            /// </summary>
            StatusGraphicsDdcciMonitorReturnedInvalidTimingStatusByte = 0xC01E0586,

            /// <summary>
            ///     MessageId: StatusGraphicsDdcciInvalidCapabilitiesString
            ///     MessageText:
            ///     A monitor returned a Ddc/Ci capabilities string which did not comply with the Access.bus 3.0, Ddc/Ci 1.1, or Mccs 2
            ///     Revision 1 specification.
            /// </summary>
            StatusGraphicsDdcciInvalidCapabilitiesString = 0xC01E0587,

            /// <summary>
            ///     MessageId: StatusGraphicsMcaInternalError
            ///     MessageText:
            ///     An internal error caused an operation to fail.
            /// </summary>
            StatusGraphicsMcaInternalError = 0xC01E0588,

            /// <summary>
            ///     MessageId: StatusGraphicsDdcciInvalidMessageCommand
            ///     MessageText:
            ///     An operation failed because a Ddc/Ci message had an invalid value in its command field.
            /// </summary>
            StatusGraphicsDdcciInvalidMessageCommand = 0xC01E0589,

            /// <summary>
            ///     MessageId: StatusGraphicsDdcciInvalidMessageLength
            ///     MessageText:
            ///     An error occurred because the field length of a Ddc/Ci message contained an invalid value.
            /// </summary>
            StatusGraphicsDdcciInvalidMessageLength = 0xC01E058A,

            /// <summary>
            ///     MessageId: StatusGraphicsDdcciInvalidMessageChecksum
            ///     MessageText:
            ///     An error occurred because the checksum field in a Ddc/Ci message did not match the message's computed checksum
            ///     value. This error implies that the data was corrupted while it was being transmitted from a monitor to a computer.
            /// </summary>
            StatusGraphicsDdcciInvalidMessageChecksum = 0xC01E058B,

            /// <summary>
            ///     MessageId: StatusGraphicsInvalidPhysicalMonitorHandle
            ///     MessageText:
            ///     This function failed because an invalid monitor handle was passed to it.
            /// </summary>
            StatusGraphicsInvalidPhysicalMonitorHandle = 0xC01E058C,

            /// <summary>
            ///     MessageId: StatusGraphicsMonitorNoLongerExists
            ///     MessageText:
            ///     The operating system asynchronously destroyed the monitor which corresponds to this handle because the operating
            ///     system's state changed. This error typically occurs because the monitor Pdo associated with this handle was
            ///     removed, the monitor Pdo associated with this handle was stopped, or a display mode change occurred. A display mode
            ///     change occurs when windows sends a WmDisplaychange windows message to applications.
            /// </summary>
            StatusGraphicsMonitorNoLongerExists = 0xC01E058D,

            // Opm, Uab, Pvp and Ddc/Ci shared status codes {= 0x25E0..= 0x25FF}

            /// <summary>
            ///     MessageId: StatusGraphicsOnlyConsoleSessionSupported
            ///     MessageText:
            ///     This function can only be used if a program is running in the local console session. It cannot be used if a program
            ///     is running on a remote desktop session or on a terminal server session.
            /// </summary>
            StatusGraphicsOnlyConsoleSessionSupported = 0xC01E05E0,

            /// <summary>
            ///     MessageId: StatusGraphicsNoDisplayDeviceCorrespondsToName
            ///     MessageText:
            ///     This function cannot find an actual Gdi display device which corresponds to the specified Gdi display device name.
            /// </summary>
            StatusGraphicsNoDisplayDeviceCorrespondsToName = 0xC01E05E1,

            /// <summary>
            ///     MessageId: StatusGraphicsDisplayDeviceNotAttachedToDesktop
            ///     MessageText:
            ///     The function failed because the specified Gdi display device was not attached to the Windows desktop.
            /// </summary>
            StatusGraphicsDisplayDeviceNotAttachedToDesktop = 0xC01E05E2,

            /// <summary>
            ///     MessageId: StatusGraphicsMirroringDevicesNotSupported
            ///     MessageText:
            ///     This function does not support Gdi mirroring display devices because Gdi mirroring display devices do not have any
            ///     physical monitors associated with them.
            /// </summary>
            StatusGraphicsMirroringDevicesNotSupported = 0xC01E05E3,

            /// <summary>
            ///     MessageId: StatusGraphicsInvalidPointer
            ///     MessageText:
            ///     The function failed because an invalid pointer parameter was passed to it. A pointer parameter is invalid if it is
            ///     Nul, it points to an invalid address, it points to a kernel mode address or it is not correctly aligned.
            /// </summary>
            StatusGraphicsInvalidPointer = 0xC01E05E4,

            /// <summary>
            ///     MessageId: StatusGraphicsNoMonitorsCorrespondToDisplayDevice
            ///     MessageText:
            ///     This function failed because the Gdi device passed to it did not have any monitors associated with it.
            /// </summary>
            StatusGraphicsNoMonitorsCorrespondToDisplayDevice = 0xC01E05E5,

            /// <summary>
            ///     MessageId: StatusGraphicsParameterArrayTooSmall
            ///     MessageText:
            ///     An array passed to the function cannot hold all of the data that the function must copy into the array.
            /// </summary>
            StatusGraphicsParameterArrayTooSmall = 0xC01E05E6,

            /// <summary>
            ///     MessageId: StatusGraphicsInternalError
            ///     MessageText:
            ///     An internal error caused an operation to fail.
            /// </summary>
            StatusGraphicsInternalError = 0xC01E05E7,

            /// <summary>
            ///     MessageId: StatusGraphicsSessionTypeChangeInProgress
            ///     MessageText:
            ///     The function failed because the current session is changing its type. This function cannot be called when the
            ///     current session is changing its type. There are currently three types of sessions: console, disconnected and
            ///     remote.
            /// </summary>
            StatusGraphicsSessionTypeChangeInProgress = 0xC01E05E8,

            // Full Volume Encryption Error codes (fvevol.sys,

            /// <summary>
            ///     MessageId: StatusFveLockedVolume
            ///     MessageText:
            ///     This volume is locked by BitLocker Drive Encryption.
            /// </summary>
            StatusFveLockedVolume = 0xC0210000,

            /// <summary>
            ///     MessageId: StatusFveNotEncrypted
            ///     MessageText:
            ///     The volume is not encrypted, no key is available.
            /// </summary>
            StatusFveNotEncrypted = 0xC0210001,

            /// <summary>
            ///     MessageId: StatusFveBadInformation
            ///     MessageText:
            ///     The control block for the encrypted volume is not valid.
            /// </summary>
            StatusFveBadInformation = 0xC0210002,

            /// <summary>
            ///     MessageId: StatusFveTooSmall
            ///     MessageText:
            ///     The volume cannot be encrypted because it does not have enough free space.
            /// </summary>
            StatusFveTooSmall = 0xC0210003,

            /// <summary>
            ///     MessageId: StatusFveFailedWrongFs
            ///     MessageText:
            ///     The volume cannot be encrypted because the file system is not supported.
            /// </summary>
            StatusFveFailedWrongFs = 0xC0210004,

            /// <summary>
            ///     MessageId: StatusFveBadPartitionSize
            ///     MessageText:
            ///     The file system size is larger than the partition size in the partition table.
            /// </summary>
            StatusFveBadPartitionSize = 0xC0210005,

            /// <summary>
            ///     MessageId: StatusFveFsNotExtended
            ///     MessageText:
            ///     The file system does not extend to the end of the volume.
            /// </summary>
            StatusFveFsNotExtended = 0xC0210006,

            /// <summary>
            ///     MessageId: StatusFveFsMounted
            ///     MessageText:
            ///     This operation cannot be performed while a file system is mounted on the volume.
            /// </summary>
            StatusFveFsMounted = 0xC0210007,

            /// <summary>
            ///     MessageId: StatusFveNoLicense
            ///     MessageText:
            ///     BitLocker Drive Encryption is not included with this version of Windows.
            /// </summary>
            StatusFveNoLicense = 0xC0210008,

            /// <summary>
            ///     MessageId: StatusFveActionNotAllowed
            ///     MessageText:
            ///     Requested action not allowed in the current volume state.
            /// </summary>
            StatusFveActionNotAllowed = 0xC0210009,

            /// <summary>
            ///     MessageId: StatusFveBadData
            ///     MessageText:
            ///     Data supplied is malformed.
            /// </summary>
            StatusFveBadData = 0xC021000A,

            /// <summary>
            ///     MessageId: StatusFveVolumeNotBound
            ///     MessageText:
            ///     The volume is not bound to the system.
            /// </summary>
            StatusFveVolumeNotBound = 0xC021000B,

            /// <summary>
            ///     MessageId: StatusFveNotDataVolume
            ///     MessageText:
            ///     That volume is not a data volume.
            /// </summary>
            StatusFveNotDataVolume = 0xC021000C,

            /// <summary>
            ///     MessageId: StatusFveConvReadError
            ///     MessageText:
            ///     A read operation failed while converting the volume.
            /// </summary>
            StatusFveConvReadError = 0xC021000D,

            /// <summary>
            ///     MessageId: StatusFveConvWriteError
            ///     MessageText:
            ///     A write operation failed while converting the volume.
            /// </summary>
            StatusFveConvWriteError = 0xC021000E,

            /// <summary>
            ///     MessageId: StatusFveOverlappedUpdate
            ///     MessageText:
            ///     The control block for the encrypted volume was updated by another thread. Try again.
            /// </summary>
            StatusFveOverlappedUpdate = 0xC021000F,

            /// <summary>
            ///     MessageId: StatusFveFailedSectorSize
            ///     MessageText:
            ///     The encryption algorithm does not support the sector size of that volume.
            /// </summary>
            StatusFveFailedSectorSize = 0xC0210010,

            /// <summary>
            ///     MessageId: StatusFveFailedAuthentication
            ///     MessageText:
            ///     BitLocker recovery authentication failed.
            /// </summary>
            StatusFveFailedAuthentication = 0xC0210011,

            /// <summary>
            ///     MessageId: StatusFveNotOsVolume
            ///     MessageText:
            ///     That volume is not the Os volume.
            /// </summary>
            StatusFveNotOsVolume = 0xC0210012,

            /// <summary>
            ///     MessageId: StatusFveKeyfileNotFound
            ///     MessageText:
            ///     The BitLocker startup key or recovery password could not be read from external media.
            /// </summary>
            StatusFveKeyfileNotFound = 0xC0210013,

            /// <summary>
            ///     MessageId: StatusFveKeyfileInvalid
            ///     MessageText:
            ///     The BitLocker startup key or recovery password file is corrupt or invalid.
            /// </summary>
            StatusFveKeyfileInvalid = 0xC0210014,

            /// <summary>
            ///     MessageId: StatusFveKeyfileNoVmk
            ///     MessageText:
            ///     The BitLocker encryption key could not be obtained from the startup key or recovery password.
            /// </summary>
            StatusFveKeyfileNoVmk = 0xC0210015,

            /// <summary>
            ///     MessageId: StatusFveTpmDisabled
            ///     MessageText:
            ///     The Trusted Platform Module (Tpm, is disabled.
            /// </summary>
            StatusFveTpmDisabled = 0xC0210016,

            /// <summary>
            ///     MessageId: StatusFveTpmSrkAuthNotZero
            ///     MessageText:
            ///     The authorization data for the Storage Root Key (Srk, of the Trusted Platform Module (Tpm, is not zero.
            /// </summary>
            StatusFveTpmSrkAuthNotZero = 0xC0210017,

            /// <summary>
            ///     MessageId: StatusFveTpmInvalidPcr
            ///     MessageText:
            ///     The system boot information changed or the Trusted Platform Module (Tpm, locked out access to BitLocker encryption
            ///     keys until the computer is restarted.
            /// </summary>
            StatusFveTpmInvalidPcr = 0xC0210018,

            /// <summary>
            ///     MessageId: StatusFveTpmNoVmk
            ///     MessageText:
            ///     The BitLocker encryption key could not be obtained from the Trusted Platform Module (Tpm,.
            /// </summary>
            StatusFveTpmNoVmk = 0xC0210019,

            /// <summary>
            ///     MessageId: StatusFvePinInvalid
            ///     MessageText:
            ///     The BitLocker encryption key could not be obtained from the Trusted Platform Module (Tpm, and Pin.
            /// </summary>
            StatusFvePinInvalid = 0xC021001A,

            /// <summary>
            ///     MessageId: StatusFveAuthInvalidApplication
            ///     MessageText:
            ///     A boot application hash does not match the hash computed when BitLocker was turned on.
            /// </summary>
            StatusFveAuthInvalidApplication = 0xC021001B,

            /// <summary>
            ///     MessageId: StatusFveAuthInvalidConfig
            ///     MessageText:
            ///     The Boot Configuration Data (Bcd, settings are not supported or have changed since BitLocker was enabled.
            /// </summary>
            StatusFveAuthInvalidConfig = 0xC021001C,

            /// <summary>
            ///     MessageId: StatusFveDebuggerEnabled
            ///     MessageText:
            ///     Boot debugging is enabled. Run bcdedit to turn it off.
            /// </summary>
            StatusFveDebuggerEnabled = 0xC021001D,

            /// <summary>
            ///     MessageId: StatusFveDryRunFailed
            ///     MessageText:
            ///     The BitLocker encryption key could not be obtained.
            /// </summary>
            StatusFveDryRunFailed = 0xC021001E,

            /// <summary>
            ///     MessageId: StatusFveBadMetadataPointer
            ///     MessageText:
            ///     The metadata disk region pointer is incorrect.
            /// </summary>
            StatusFveBadMetadataPointer = 0xC021001F,

            /// <summary>
            ///     MessageId: StatusFveOldMetadataCopy
            ///     MessageText:
            ///     The backup copy of the metadata is out of date.
            /// </summary>
            StatusFveOldMetadataCopy = 0xC0210020,

            /// <summary>
            ///     MessageId: StatusFveRebootRequired
            ///     MessageText:
            ///     No action was taken as a system reboot is required.
            /// </summary>
            StatusFveRebootRequired = 0xC0210021,

            /// <summary>
            ///     MessageId: StatusFveRawAccess
            ///     MessageText:
            ///     No action was taken as BitLocker Drive Encryption is in Raw access mode.
            /// </summary>
            StatusFveRawAccess = 0xC0210022,

            /// <summary>
            ///     MessageId: StatusFveRawBlocked
            ///     MessageText:
            ///     BitLocker Drive Encryption cannot enter raw access mode for this volume.
            /// </summary>
            StatusFveRawBlocked = 0xC0210023,

            /// <summary>
            ///     MessageId: StatusFveNoAutounlockMasterKey
            ///     MessageText:
            ///     The auto-unlock master key was not available from the operating system volume. Retry the operation using the
            ///     BitLocker Wmi interface.
            /// </summary>
            StatusFveNoAutounlockMasterKey = 0xC0210024,

            /// <summary>
            ///     MessageId: StatusFveMorFailed
            ///     MessageText:
            ///     The system firmware failed to enable clearing of system memory on reboot.
            /// </summary>
            StatusFveMorFailed = 0xC0210025,

            /// <summary>
            ///     MessageId: StatusFveNoFeatureLicense
            ///     MessageText:
            ///     This feature of BitLocker Drive Encryption is not included with this version of Windows.
            /// </summary>
            StatusFveNoFeatureLicense = 0xC0210026,

            /// <summary>
            ///     MessageId: StatusFvePolicyUserDisableRdvNotAllowed
            ///     MessageText:
            ///     Group policy does not permit turning off BitLocker Drive Encryption on roaming data volumes.
            /// </summary>
            StatusFvePolicyUserDisableRdvNotAllowed = 0xC0210027,

            /// <summary>
            ///     MessageId: StatusFveConvRecoveryFailed
            ///     MessageText:
            ///     Bitlocker Drive Encryption failed to recover from aborted conversion. This could be due to either all conversion
            ///     logs being corrupted or the media being write-protected.
            /// </summary>
            StatusFveConvRecoveryFailed = 0xC0210028,

            /// <summary>
            ///     MessageId: StatusFveVirtualizedSpaceTooBig
            ///     MessageText:
            ///     The requested virtualization size is too big.
            /// </summary>
            StatusFveVirtualizedSpaceTooBig = 0xC0210029,

            /// <summary>
            ///     MessageId: StatusFveInvalidDatumType
            ///     MessageText:
            ///     The management information stored on the drive contained an unknown type. If you are using an old version of
            ///     Windows, try accessing the drive from the latest version.
            /// </summary>
            StatusFveInvalidDatumType = 0xC021002A,

            /// <summary>
            ///     MessageId: StatusFveVolumeTooSmall
            ///     MessageText:
            ///     The drive is too small to be protected using BitLocker Drive Encryption.
            /// </summary>
            StatusFveVolumeTooSmall = 0xC0210030,

            /// <summary>
            ///     MessageId: StatusFveEnhPinInvalid
            ///     MessageText:
            ///     The BitLocker encryption key could not be obtained from the Trusted Platform Module (Tpm, and enhanced Pin. Try
            ///     using a Pin containing only numerals.
            /// </summary>
            StatusFveEnhPinInvalid = 0xC0210031,

            // Fwp error codes (fwpkclnt.sys,

            /// <summary>
            ///     MessageId: StatusFwpCalloutNotFound
            ///     MessageText:
            ///     The callout does not exist.
            /// </summary>
            StatusFwpCalloutNotFound = 0xC0220001,

            /// <summary>
            ///     MessageId: StatusFwpConditionNotFound
            ///     MessageText:
            ///     The filter condition does not exist.
            /// </summary>
            StatusFwpConditionNotFound = 0xC0220002,

            /// <summary>
            ///     MessageId: StatusFwpFilterNotFound
            ///     MessageText:
            ///     The filter does not exist.
            /// </summary>
            StatusFwpFilterNotFound = 0xC0220003,

            /// <summary>
            ///     MessageId: StatusFwpLayerNotFound
            ///     MessageText:
            ///     The layer does not exist.
            /// </summary>
            StatusFwpLayerNotFound = 0xC0220004,

            /// <summary>
            ///     MessageId: StatusFwpProviderNotFound
            ///     MessageText:
            ///     The provider does not exist.
            /// </summary>
            StatusFwpProviderNotFound = 0xC0220005,

            /// <summary>
            ///     MessageId: StatusFwpProviderContextNotFound
            ///     MessageText:
            ///     The provider context does not exist.
            /// </summary>
            StatusFwpProviderContextNotFound = 0xC0220006,

            /// <summary>
            ///     MessageId: StatusFwpSublayerNotFound
            ///     MessageText:
            ///     The sublayer does not exist.
            /// </summary>
            StatusFwpSublayerNotFound = 0xC0220007,

            /// <summary>
            ///     MessageId: StatusFwpNotFound
            ///     MessageText:
            ///     The object does not exist.
            /// </summary>
            StatusFwpNotFound = 0xC0220008,

            /// <summary>
            ///     MessageId: StatusFwpAlreadyExists
            ///     MessageText:
            ///     An object with that Guid or Luid already exists.
            /// </summary>
            StatusFwpAlreadyExists = 0xC0220009,

            /// <summary>
            ///     MessageId: StatusFwpInUse
            ///     MessageText:
            ///     The object is referenced by other objects so cannot be deleted.
            /// </summary>
            StatusFwpInUse = 0xC022000A,

            /// <summary>
            ///     MessageId: StatusFwpDynamicSessionInProgress
            ///     MessageText:
            ///     The call is not allowed from within a dynamic session.
            /// </summary>
            StatusFwpDynamicSessionInProgress = 0xC022000B,

            /// <summary>
            ///     MessageId: StatusFwpWrongSession
            ///     MessageText:
            ///     The call was made from the wrong session so cannot be completed.
            /// </summary>
            StatusFwpWrongSession = 0xC022000C,

            /// <summary>
            ///     MessageId: StatusFwpNoTxnInProgress
            ///     MessageText:
            ///     The call must be made from within an explicit transaction.
            /// </summary>
            StatusFwpNoTxnInProgress = 0xC022000D,

            /// <summary>
            ///     MessageId: StatusFwpTxnInProgress
            ///     MessageText:
            ///     The call is not allowed from within an explicit transaction.
            /// </summary>
            StatusFwpTxnInProgress = 0xC022000E,

            /// <summary>
            ///     MessageId: StatusFwpTxnAborted
            ///     MessageText:
            ///     The explicit transaction has been forcibly cancelled.
            /// </summary>
            StatusFwpTxnAborted = 0xC022000F,

            /// <summary>
            ///     MessageId: StatusFwpSessionAborted
            ///     MessageText:
            ///     The session has been cancelled.
            /// </summary>
            StatusFwpSessionAborted = 0xC0220010,

            /// <summary>
            ///     MessageId: StatusFwpIncompatibleTxn
            ///     MessageText:
            ///     The call is not allowed from within a read-only transaction.
            /// </summary>
            StatusFwpIncompatibleTxn = 0xC0220011,

            /// <summary>
            ///     MessageId: StatusFwpTimeout
            ///     MessageText:
            ///     The call timed out while waiting to acquire the transaction lock.
            /// </summary>
            StatusFwpTimeout = 0xC0220012,

            /// <summary>
            ///     MessageId: StatusFwpNetEventsDisabled
            ///     MessageText:
            ///     Collection of network diagnostic events is disabled.
            /// </summary>
            StatusFwpNetEventsDisabled = 0xC0220013,

            /// <summary>
            ///     MessageId: StatusFwpIncompatibleLayer
            ///     MessageText:
            ///     The operation is not supported by the specified layer.
            /// </summary>
            StatusFwpIncompatibleLayer = 0xC0220014,

            /// <summary>
            ///     MessageId: StatusFwpKmClientsOnly
            ///     MessageText:
            ///     The call is allowed for kernel-mode callers only.
            /// </summary>
            StatusFwpKmClientsOnly = 0xC0220015,

            /// <summary>
            ///     MessageId: StatusFwpLifetimeMismatch
            ///     MessageText:
            ///     The call tried to associate two objects with incompatible lifetimes.
            /// </summary>
            StatusFwpLifetimeMismatch = 0xC0220016,

            /// <summary>
            ///     MessageId: StatusFwpBuiltinObject
            ///     MessageText:
            ///     The object is built in so cannot be deleted.
            /// </summary>
            StatusFwpBuiltinObject = 0xC0220017,

            /// <summary>
            ///     MessageId: StatusFwpTooManyCallouts
            ///     MessageText:
            ///     The maximum number of callouts has been reached.
            /// </summary>
            StatusFwpTooManyCallouts = 0xC0220018,

            /// <summary>
            ///     MessageId: StatusFwpNotificationDropped
            ///     MessageText:
            ///     A notification could not be delivered because a message queue is at its maximum capacity.
            /// </summary>
            StatusFwpNotificationDropped = 0xC0220019,

            /// <summary>
            ///     MessageId: StatusFwpTrafficMismatch
            ///     MessageText:
            ///     The traffic parameters do not match those for the security association context.
            /// </summary>
            StatusFwpTrafficMismatch = 0xC022001A,

            /// <summary>
            ///     MessageId: StatusFwpIncompatibleSaState
            ///     MessageText:
            ///     The call is not allowed for the current security association state.
            /// </summary>
            StatusFwpIncompatibleSaState = 0xC022001B,

            /// <summary>
            ///     MessageId: StatusFwpNullPointer
            ///     MessageText:
            ///     A required pointer is null.
            /// </summary>
            StatusFwpNullPointer = 0xC022001C,

            /// <summary>
            ///     MessageId: StatusFwpInvalidEnumerator
            ///     MessageText:
            ///     An enumerator is not valid.
            /// </summary>
            StatusFwpInvalidEnumerator = 0xC022001D,

            /// <summary>
            ///     MessageId: StatusFwpInvalidFlags
            ///     MessageText:
            ///     The flags field contains an invalid value.
            /// </summary>
            StatusFwpInvalidFlags = 0xC022001E,

            /// <summary>
            ///     MessageId: StatusFwpInvalidNetMask
            ///     MessageText:
            ///     A network mask is not valid.
            /// </summary>
            StatusFwpInvalidNetMask = 0xC022001F,

            /// <summary>
            ///     MessageId: StatusFwpInvalidRange
            ///     MessageText:
            ///     An FwpRange is not valid.
            /// </summary>
            StatusFwpInvalidRange = 0xC0220020,

            /// <summary>
            ///     MessageId: StatusFwpInvalidInterval
            ///     MessageText:
            ///     The time interval is not valid.
            /// </summary>
            StatusFwpInvalidInterval = 0xC0220021,

            /// <summary>
            ///     MessageId: StatusFwpZeroLengthArray
            ///     MessageText:
            ///     An array that must contain at least one element is zero length.
            /// </summary>
            StatusFwpZeroLengthArray = 0xC0220022,

            /// <summary>
            ///     MessageId: StatusFwpNullDisplayName
            ///     MessageText:
            ///     The displayData.name field cannot be null.
            /// </summary>
            StatusFwpNullDisplayName = 0xC0220023,

            /// <summary>
            ///     MessageId: StatusFwpInvalidActionType
            ///     MessageText:
            ///     The action type is not one of the allowed action types for a filter.
            /// </summary>
            StatusFwpInvalidActionType = 0xC0220024,

            /// <summary>
            ///     MessageId: StatusFwpInvalidWeight
            ///     MessageText:
            ///     The filter weight is not valid.
            /// </summary>
            StatusFwpInvalidWeight = 0xC0220025,

            /// <summary>
            ///     MessageId: StatusFwpMatchTypeMismatch
            ///     MessageText:
            ///     A filter condition contains a match type that is not compatible with the operands.
            /// </summary>
            StatusFwpMatchTypeMismatch = 0xC0220026,

            /// <summary>
            ///     MessageId: StatusFwpTypeMismatch
            ///     MessageText:
            ///     An FwpValue or FwpmConditionValue is of the wrong type.
            /// </summary>
            StatusFwpTypeMismatch = 0xC0220027,

            /// <summary>
            ///     MessageId: StatusFwpOutOfBounds
            ///     MessageText:
            ///     An integer value is outside the allowed range.
            /// </summary>
            StatusFwpOutOfBounds = 0xC0220028,

            /// <summary>
            ///     MessageId: StatusFwpReserved
            ///     MessageText:
            ///     A reserved field is non-zero.
            /// </summary>
            StatusFwpReserved = 0xC0220029,

            /// <summary>
            ///     MessageId: StatusFwpDuplicateCondition
            ///     MessageText:
            ///     A filter cannot contain multiple conditions operating on a single field.
            /// </summary>
            StatusFwpDuplicateCondition = 0xC022002A,

            /// <summary>
            ///     MessageId: StatusFwpDuplicateKeymod
            ///     MessageText:
            ///     A policy cannot contain the same keying module more than once.
            /// </summary>
            StatusFwpDuplicateKeymod = 0xC022002B,

            /// <summary>
            ///     MessageId: StatusFwpActionIncompatibleWithLayer
            ///     MessageText:
            ///     The action type is not compatible with the layer.
            /// </summary>
            StatusFwpActionIncompatibleWithLayer = 0xC022002C,

            /// <summary>
            ///     MessageId: StatusFwpActionIncompatibleWithSublayer
            ///     MessageText:
            ///     The action type is not compatible with the sublayer.
            /// </summary>
            StatusFwpActionIncompatibleWithSublayer = 0xC022002D,

            /// <summary>
            ///     MessageId: StatusFwpContextIncompatibleWithLayer
            ///     MessageText:
            ///     The raw context or the provider context is not compatible with the layer.
            /// </summary>
            StatusFwpContextIncompatibleWithLayer = 0xC022002E,

            /// <summary>
            ///     MessageId: StatusFwpContextIncompatibleWithCallout
            ///     MessageText:
            ///     The raw context or the provider context is not compatible with the callout.
            /// </summary>
            StatusFwpContextIncompatibleWithCallout = 0xC022002F,

            /// <summary>
            ///     MessageId: StatusFwpIncompatibleAuthMethod
            ///     MessageText:
            ///     The authentication method is not compatible with the policy type.
            /// </summary>
            StatusFwpIncompatibleAuthMethod = 0xC0220030,

            /// <summary>
            ///     MessageId: StatusFwpIncompatibleDhGroup
            ///     MessageText:
            ///     The Diffie-Hellman group is not compatible with the policy type.
            /// </summary>
            StatusFwpIncompatibleDhGroup = 0xC0220031,

            /// <summary>
            ///     MessageId: StatusFwpEmNotSupported
            ///     MessageText:
            ///     An Ike policy cannot contain an Extended Mode policy.
            /// </summary>
            StatusFwpEmNotSupported = 0xC0220032,

            /// <summary>
            ///     MessageId: StatusFwpNeverMatch
            ///     MessageText:
            ///     The enumeration template or subscription will never match any objects.
            /// </summary>
            StatusFwpNeverMatch = 0xC0220033,

            /// <summary>
            ///     MessageId: StatusFwpProviderContextMismatch
            ///     MessageText:
            ///     The provider context is of the wrong type.
            /// </summary>
            StatusFwpProviderContextMismatch = 0xC0220034,

            /// <summary>
            ///     MessageId: StatusFwpInvalidParameter
            ///     MessageText:
            ///     The parameter is incorrect.
            /// </summary>
            StatusFwpInvalidParameter = 0xC0220035,

            /// <summary>
            ///     MessageId: StatusFwpTooManySublayers
            ///     MessageText:
            ///     The maximum number of sublayers has been reached.
            /// </summary>
            StatusFwpTooManySublayers = 0xC0220036,

            /// <summary>
            ///     MessageId: StatusFwpCalloutNotificationFailed
            ///     MessageText:
            ///     The notification function for a callout returned an error.
            /// </summary>
            StatusFwpCalloutNotificationFailed = 0xC0220037,

            /// <summary>
            ///     MessageId: StatusFwpInvalidAuthTransform
            ///     MessageText:
            ///     The IPsec authentication transform is not valid.
            /// </summary>
            StatusFwpInvalidAuthTransform = 0xC0220038,

            /// <summary>
            ///     MessageId: StatusFwpInvalidCipherTransform
            ///     MessageText:
            ///     The IPsec cipher transform is not valid.
            /// </summary>
            StatusFwpInvalidCipherTransform = 0xC0220039,

            /// <summary>
            ///     MessageId: StatusFwpIncompatibleCipherTransform
            ///     MessageText:
            ///     The IPsec cipher transform is not compatible with the policy.
            /// </summary>
            StatusFwpIncompatibleCipherTransform = 0xC022003A,

            /// <summary>
            ///     MessageId: StatusFwpInvalidTransformCombination
            ///     MessageText:
            ///     The combination of IPsec transform types is not valid.
            /// </summary>
            StatusFwpInvalidTransformCombination = 0xC022003B,

            /// <summary>
            ///     MessageId: StatusFwpDuplicateAuthMethod
            ///     MessageText:
            ///     A policy cannot contain the same auth method more than once.
            /// </summary>
            StatusFwpDuplicateAuthMethod = 0xC022003C,

            /// <summary>
            ///     MessageId: StatusFwpTcpipNotReady
            ///     MessageText:
            ///     The Tcp/Ip stack is not ready.
            /// </summary>
            StatusFwpTcpipNotReady = 0xC0220100,

            /// <summary>
            ///     MessageId: StatusFwpInjectHandleClosing
            ///     MessageText:
            ///     The injection handle is being closed by another thread.
            /// </summary>
            StatusFwpInjectHandleClosing = 0xC0220101,

            /// <summary>
            ///     MessageId: StatusFwpInjectHandleStale
            ///     MessageText:
            ///     The injection handle is stale.
            /// </summary>
            StatusFwpInjectHandleStale = 0xC0220102,

            /// <summary>
            ///     MessageId: StatusFwpCannotPend
            ///     MessageText:
            ///     The classify cannot be pended.
            /// </summary>
            StatusFwpCannotPend = 0xC0220103,

            /// <summary>
            ///     MessageId: StatusFwpDropNoicmp
            ///     MessageText:
            ///     The packet should be dropped, no Icmp should be sent.
            /// </summary>
            StatusFwpDropNoicmp = 0xC0220104,

            // Ndis error codes (ndis.sys,

            /// <summary>
            ///     MessageId: StatusNdisClosing
            ///     MessageText:
            ///     The binding to the network interface is being closed.
            /// </summary>
            StatusNdisClosing = 0xC0230002,

            /// <summary>
            ///     MessageId: StatusNdisBadVersion
            ///     MessageText:
            ///     An invalid version was specified.
            /// </summary>
            StatusNdisBadVersion = 0xC0230004,

            /// <summary>
            ///     MessageId: StatusNdisBadCharacteristics
            ///     MessageText:
            ///     An invalid characteristics table was used.
            /// </summary>
            StatusNdisBadCharacteristics = 0xC0230005,

            /// <summary>
            ///     MessageId: StatusNdisAdapterNotFound
            ///     MessageText:
            ///     Failed to find the network interface or network interface is not ready.
            /// </summary>
            StatusNdisAdapterNotFound = 0xC0230006,

            /// <summary>
            ///     MessageId: StatusNdisOpenFailed
            ///     MessageText:
            ///     Failed to open the network interface.
            /// </summary>
            StatusNdisOpenFailed = 0xC0230007,

            /// <summary>
            ///     MessageId: StatusNdisDeviceFailed
            ///     MessageText:
            ///     Network interface has encountered an internal unrecoverable failure.
            /// </summary>
            StatusNdisDeviceFailed = 0xC0230008,

            /// <summary>
            ///     MessageId: StatusNdisMulticastFull
            ///     MessageText:
            ///     The multicast list on the network interface is full.
            /// </summary>
            StatusNdisMulticastFull = 0xC0230009,

            /// <summary>
            ///     MessageId: StatusNdisMulticastExists
            ///     MessageText:
            ///     An attempt was made to add a duplicate multicast address to the list.
            /// </summary>
            StatusNdisMulticastExists = 0xC023000A,

            /// <summary>
            ///     MessageId: StatusNdisMulticastNotFound
            ///     MessageText:
            ///     At attempt was made to remove a multicast address that was never added.
            /// </summary>
            StatusNdisMulticastNotFound = 0xC023000B,

            /// <summary>
            ///     MessageId: StatusNdisRequestAborted
            ///     MessageText:
            ///     Netowork interface aborted the request.
            /// </summary>
            StatusNdisRequestAborted = 0xC023000C,

            /// <summary>
            ///     MessageId: StatusNdisResetInProgress
            ///     MessageText:
            ///     Network interface can not process the request because it is being reset.
            /// </summary>
            StatusNdisResetInProgress = 0xC023000D,

            /// <summary>
            ///     MessageId: StatusNdisNotSupported
            ///     MessageText:
            ///     Netword interface does not support this request.
            /// </summary>
            StatusNdisNotSupported = 0xC02300BB,

            /// <summary>
            ///     MessageId: StatusNdisInvalidPacket
            ///     MessageText:
            ///     An attempt was made to send an invalid packet on a network interface.
            /// </summary>
            StatusNdisInvalidPacket = 0xC023000F,

            /// <summary>
            ///     MessageId: StatusNdisAdapterNotReady
            ///     MessageText:
            ///     Network interface is not ready to complete this operation.
            /// </summary>
            StatusNdisAdapterNotReady = 0xC0230011,

            /// <summary>
            ///     MessageId: StatusNdisInvalidLength
            ///     MessageText:
            ///     The length of the buffer submitted for this operation is not valid.
            /// </summary>
            StatusNdisInvalidLength = 0xC0230014,

            /// <summary>
            ///     MessageId: StatusNdisInvalidData
            ///     MessageText:
            ///     The data used for this operation is not valid.
            /// </summary>
            StatusNdisInvalidData = 0xC0230015,

            /// <summary>
            ///     MessageId: StatusNdisBufferTooShort
            ///     MessageText:
            ///     The length of buffer submitted for this operation is too small.
            /// </summary>
            StatusNdisBufferTooShort = 0xC0230016,

            /// <summary>
            ///     MessageId: StatusNdisInvalidOid
            ///     MessageText:
            ///     Network interface does not support this Oid (Object Identifier,
            /// </summary>
            StatusNdisInvalidOid = 0xC0230017,

            /// <summary>
            ///     MessageId: StatusNdisAdapterRemoved
            ///     MessageText:
            ///     The network interface has been removed.
            /// </summary>
            StatusNdisAdapterRemoved = 0xC0230018,

            /// <summary>
            ///     MessageId: StatusNdisUnsupportedMedia
            ///     MessageText:
            ///     Network interface does not support this media type.
            /// </summary>
            StatusNdisUnsupportedMedia = 0xC0230019,

            /// <summary>
            ///     MessageId: StatusNdisGroupAddressInUse
            ///     MessageText:
            ///     An attempt was made to remove a token ring group address that is in use by other components.
            /// </summary>
            StatusNdisGroupAddressInUse = 0xC023001A,

            /// <summary>
            ///     MessageId: StatusNdisFileNotFound
            ///     MessageText:
            ///     An attempt was made to map a file that can not be found.
            /// </summary>
            StatusNdisFileNotFound = 0xC023001B,

            /// <summary>
            ///     MessageId: StatusNdisErrorReadingFile
            ///     MessageText:
            ///     An error occured while Ndis tried to map the file.
            /// </summary>
            StatusNdisErrorReadingFile = 0xC023001C,

            /// <summary>
            ///     MessageId: StatusNdisAlreadyMapped
            ///     MessageText:
            ///     An attempt was made to map a file that is alreay mapped.
            /// </summary>
            StatusNdisAlreadyMapped = 0xC023001D,

            /// <summary>
            ///     MessageId: StatusNdisResourceConflict
            ///     MessageText:
            ///     An attempt to allocate a hardware resource failed because the resource is used by another component.
            /// </summary>
            StatusNdisResourceConflict = 0xC023001E,

            /// <summary>
            ///     MessageId: StatusNdisMediaDisconnected
            ///     MessageText:
            ///     The I/O operation failed because network media is disconnected or wireless access point is out of range.
            /// </summary>
            StatusNdisMediaDisconnected = 0xC023001F,

            /// <summary>
            ///     MessageId: StatusNdisInvalidAddress
            ///     MessageText:
            ///     The network address used in the request is invalid.
            /// </summary>
            StatusNdisInvalidAddress = 0xC0230022,

            /// <summary>
            ///     MessageId: StatusNdisInvalidDeviceRequest
            ///     MessageText:
            ///     The specified request is not a valid operation for the target device.
            /// </summary>
            StatusNdisInvalidDeviceRequest = 0xC0230010,

            /// <summary>
            ///     MessageId: StatusNdisPaused
            ///     MessageText:
            ///     The offload operation on the network interface has been paused.
            /// </summary>
            StatusNdisPaused = 0xC023002A,

            /// <summary>
            ///     MessageId: StatusNdisInterfaceNotFound
            ///     MessageText:
            ///     Network interface was not found.
            /// </summary>
            StatusNdisInterfaceNotFound = 0xC023002B,

            /// <summary>
            ///     MessageId: StatusNdisUnsupportedRevision
            ///     MessageText:
            ///     The revision number specified in the structure is not supported.
            /// </summary>
            StatusNdisUnsupportedRevision = 0xC023002C,

            /// <summary>
            ///     MessageId: StatusNdisInvalidPort
            ///     MessageText:
            ///     The specified port does not exist on this network interface.
            /// </summary>
            StatusNdisInvalidPort = 0xC023002D,

            /// <summary>
            ///     MessageId: StatusNdisInvalidPortState
            ///     MessageText:
            ///     The current state of the specified port on this network interface does not support the requested operation.
            /// </summary>
            StatusNdisInvalidPortState = 0xC023002E,

            /// <summary>
            ///     MessageId: StatusNdisLowPowerState
            ///     MessageText:
            ///     The miniport adapter is in lower power state.
            /// </summary>
            StatusNdisLowPowerState = 0xC023002F,

            // Ndis error codes (802.11 wireless Lan,

            /// <summary>
            ///     MessageId: StatusNdisDot11AutoConfigEnabled
            ///     MessageText:
            ///     The wireless local area network interface is in auto configuration mode and doesn't support the requested parameter
            ///     change operation.
            /// </summary>
            StatusNdisDot11AutoConfigEnabled = 0xC0232000,

            /// <summary>
            ///     MessageId: StatusNdisDot11MediaInUse
            ///     MessageText:
            ///     The wireless local area network interface is busy and can not perform the requested operation.
            /// </summary>
            StatusNdisDot11MediaInUse = 0xC0232001,

            /// <summary>
            ///     MessageId: StatusNdisDot11PowerStateInvalid
            ///     MessageText:
            ///     The wireless local area network interface is powered down and doesn't support the requested operation.
            /// </summary>
            StatusNdisDot11PowerStateInvalid = 0xC0232002,

            /// <summary>
            ///     MessageId: StatusNdisPmWolPatternListFull
            ///     MessageText:
            ///     The list of wake on Lan patterns is full.
            /// </summary>
            StatusNdisPmWolPatternListFull = 0xC0232003,

            /// <summary>
            ///     MessageId: StatusNdisPmProtocolOffloadListFull
            ///     MessageText:
            ///     The list of low power protocol offloads is full.
            /// </summary>
            StatusNdisPmProtocolOffloadListFull = 0xC0232004,

            // Ndis informational codes(ndis.sys,

            /// <summary>
            ///     MessageId: StatusNdisIndicationRequired
            ///     MessageText:
            ///     The request will be completed later by Ndis status indication.
            /// </summary>
            StatusNdisIndicationRequired = 0x40230001,

            // Ndis Chimney Offload codes (ndis.sys,

            /// <summary>
            ///     MessageId: StatusNdisOffloadPolicy
            ///     MessageText:
            ///     The Tcp connection is not offloadable because of a local policy setting.
            /// </summary>
            StatusNdisOffloadPolicy = 0xC023100F,

            /// <summary>
            ///     MessageId: StatusNdisOffloadConnectionRejected
            ///     MessageText:
            ///     The Tcp connection is not offloadable by the Chimney offload target.
            /// </summary>
            StatusNdisOffloadConnectionRejected = 0xC0231012,

            /// <summary>
            ///     MessageId: StatusNdisOffloadPathRejected
            ///     MessageText:
            ///     The Ip Path object is not in an offloadable state.
            /// </summary>
            StatusNdisOffloadPathRejected = 0xC0231013,

            // Hypervisor error codes - changes to these codes must be reflected in HvStatus.h

            /// <summary>
            ///     MessageId: StatusHvInvalidHypercallCode
            ///     MessageText:
            ///     The hypervisor does not support the operation because the specified hypercall code is not supported.
            /// </summary>
            StatusHvInvalidHypercallCode = 0xC0350002,

            /// <summary>
            ///     MessageId: StatusHvInvalidHypercallInput
            ///     MessageText:
            ///     The hypervisor does not support the operation because the encoding for the hypercall input register is not
            ///     supported.
            /// </summary>
            StatusHvInvalidHypercallInput = 0xC0350003,

            /// <summary>
            ///     MessageId: StatusHvInvalidAlignment
            ///     MessageText:
            ///     The hypervisor could not perform the operation beacuse a parameter has an invalid alignment.
            /// </summary>
            StatusHvInvalidAlignment = 0xC0350004,

            /// <summary>
            ///     MessageId: StatusHvInvalidParameter
            ///     MessageText:
            ///     The hypervisor could not perform the operation beacuse an invalid parameter was specified.
            /// </summary>
            StatusHvInvalidParameter = 0xC0350005,

            /// <summary>
            ///     MessageId: StatusHvAccessDenied
            ///     MessageText:
            ///     Access to the specified object was denied.
            /// </summary>
            StatusHvAccessDenied = 0xC0350006,

            /// <summary>
            ///     MessageId: StatusHvInvalidPartitionState
            ///     MessageText:
            ///     The hypervisor could not perform the operation because the partition is entering or in an invalid state.
            /// </summary>
            StatusHvInvalidPartitionState = 0xC0350007,

            /// <summary>
            ///     MessageId: StatusHvOperationDenied
            ///     MessageText:
            ///     The operation is not allowed in the current state.
            /// </summary>
            StatusHvOperationDenied = 0xC0350008,

            /// <summary>
            ///     MessageId: StatusHvUnknownProperty
            ///     MessageText:
            ///     The hypervisor does not recognize the specified partition property.
            /// </summary>
            StatusHvUnknownProperty = 0xC0350009,

            /// <summary>
            ///     MessageId: StatusHvPropertyValueOutOfRange
            ///     MessageText:
            ///     The specified value of a partition property is out of range or violates an invariant.
            /// </summary>
            StatusHvPropertyValueOutOfRange = 0xC035000A,

            /// <summary>
            ///     MessageId: StatusHvInsufficientMemory
            ///     MessageText:
            ///     There is not enough memory in the hypervisor pool to complete the operation.
            /// </summary>
            StatusHvInsufficientMemory = 0xC035000B,

            /// <summary>
            ///     MessageId: StatusHvPartitionTooDeep
            ///     MessageText:
            ///     The maximum partition depth has been exceeded for the partition hierarchy.
            /// </summary>
            StatusHvPartitionTooDeep = 0xC035000C,

            /// <summary>
            ///     MessageId: StatusHvInvalidPartitionId
            ///     MessageText:
            ///     A partition with the specified partition Id does not exist.
            /// </summary>
            StatusHvInvalidPartitionId = 0xC035000D,

            /// <summary>
            ///     MessageId: StatusHvInvalidVpIndex
            ///     MessageText:
            ///     The hypervisor could not perform the operation because the specified Vp index is invalid.
            /// </summary>
            StatusHvInvalidVpIndex = 0xC035000E,

            /// <summary>
            ///     MessageId: StatusHvInvalidPortId
            ///     MessageText:
            ///     The hypervisor could not perform the operation because the specified port identifier is invalid.
            /// </summary>
            StatusHvInvalidPortId = 0xC0350011,

            /// <summary>
            ///     MessageId: StatusHvInvalidConnectionId
            ///     MessageText:
            ///     The hypervisor could not perform the operation because the specified connection identifier is invalid.
            /// </summary>
            StatusHvInvalidConnectionId = 0xC0350012,

            /// <summary>
            ///     MessageId: StatusHvInsufficientBuffers
            ///     MessageText:
            ///     Not enough buffers were supplied to send a message.
            /// </summary>
            StatusHvInsufficientBuffers = 0xC0350013,

            /// <summary>
            ///     MessageId: StatusHvNotAcknowledged
            ///     MessageText:
            ///     The previous virtual interrupt has not been acknowledged.
            /// </summary>
            StatusHvNotAcknowledged = 0xC0350014,

            /// <summary>
            ///     MessageId: StatusHvAcknowledged
            ///     MessageText:
            ///     The previous virtual interrupt has already been acknowledged.
            /// </summary>
            StatusHvAcknowledged = 0xC0350016,

            /// <summary>
            ///     MessageId: StatusHvInvalidSaveRestoreState
            ///     MessageText:
            ///     The indicated partition is not in a valid state for saving or restoring.
            /// </summary>
            StatusHvInvalidSaveRestoreState = 0xC0350017,

            /// <summary>
            ///     MessageId: StatusHvInvalidSynicState
            ///     MessageText:
            ///     The hypervisor could not complete the operation because a required feature of the synthetic interrupt controller
            ///     (SynIC, was disabled.
            /// </summary>
            StatusHvInvalidSynicState = 0xC0350018,

            /// <summary>
            ///     MessageId: StatusHvObjectInUse
            ///     MessageText:
            ///     The hypervisor could not perform the operation because the object or value was either already in use or being used
            ///     for a purpose that would not permit completing the operation.
            /// </summary>
            StatusHvObjectInUse = 0xC0350019,

            /// <summary>
            ///     MessageId: StatusHvInvalidProximityDomainInfo
            ///     MessageText:
            ///     The proximity domain information is invalid.
            /// </summary>
            StatusHvInvalidProximityDomainInfo = 0xC035001A,

            /// <summary>
            ///     MessageId: StatusHvNoData
            ///     MessageText:
            ///     An attempt to retrieve debugging data failed because none was available.
            /// </summary>
            StatusHvNoData = 0xC035001B,

            /// <summary>
            ///     MessageId: StatusHvInactive
            ///     MessageText:
            ///     The physical connection being used for debuggging has not recorded any receive activity since the last operation.
            /// </summary>
            StatusHvInactive = 0xC035001C,

            /// <summary>
            ///     MessageId: StatusHvNoResources
            ///     MessageText:
            ///     There are not enough resources to complete the operation.
            /// </summary>
            StatusHvNoResources = 0xC035001D,

            /// <summary>
            ///     MessageId: StatusHvFeatureUnavailable
            ///     MessageText:
            ///     A hypervisor feature is not available to the user.
            /// </summary>
            StatusHvFeatureUnavailable = 0xC035001E,

            /// <summary>
            ///     MessageId: StatusHvNotPresent
            ///     MessageText:
            ///     No hypervisor is present on this system.
            /// </summary>
            StatusHvNotPresent = 0xC0351000,

            // Virtualization status codes - these codes are used by the Virtualization Infrustructure Driver (Vid, and other components
            // of the virtualization stack.

            /// <summary>
            ///     MessageId: StatusVidDuplicateHandler
            ///     MessageText:
            ///     The handler for the virtualization infrastructure driver is already registered. Restarting the virtual machine may
            ///     fix the problem. If the problem persists, try restarting the physical computer.
            /// </summary>
            StatusVidDuplicateHandler = 0xC0370001,

            /// <summary>
            ///     MessageId: StatusVidTooManyHandlers
            ///     MessageText:
            ///     The number of registered handlers for the virtualization infrastructure driver exceeded the maximum. Restarting the
            ///     virtual machine may fix the problem. If the problem persists, try restarting the physical computer.
            /// </summary>
            StatusVidTooManyHandlers = 0xC0370002,

            /// <summary>
            ///     MessageId: StatusVidQueueFull
            ///     MessageText:
            ///     The message queue for the virtualization infrastructure driver is full and cannot accept new messages. Restarting
            ///     the virtual machine may fix the problem. If the problem persists, try restarting the physical computer.
            /// </summary>
            StatusVidQueueFull = 0xC0370003,

            /// <summary>
            ///     MessageId: StatusVidHandlerNotPresent
            ///     MessageText:
            ///     No handler exists to handle the message for the virtualization infrastructure driver. Restarting the virtual
            ///     machine may fix the problem. If the problem persists, try restarting the physical computer.
            /// </summary>
            StatusVidHandlerNotPresent = 0xC0370004,

            /// <summary>
            ///     MessageId: StatusVidInvalidObjectName
            ///     MessageText:
            ///     The name of the partition or message queue for the virtualization infrastructure driver is invalid. Restarting the
            ///     virtual machine may fix the problem. If the problem persists, try restarting the physical computer.
            /// </summary>
            StatusVidInvalidObjectName = 0xC0370005,

            /// <summary>
            ///     MessageId: StatusVidPartitionNameTooLong
            ///     MessageText:
            ///     The partition name of the virtualization infrastructure driver exceeds the maximum.
            /// </summary>
            StatusVidPartitionNameTooLong = 0xC0370006,

            /// <summary>
            ///     MessageId: StatusVidMessageQueueNameTooLong
            ///     MessageText:
            ///     The message queue name of the virtualization infrastructure driver exceeds the maximum.
            /// </summary>
            StatusVidMessageQueueNameTooLong = 0xC0370007,

            /// <summary>
            ///     MessageId: StatusVidPartitionAlreadyExists
            ///     MessageText:
            ///     Cannot create the partition for the virtualization infrastructure driver because another partition with the same
            ///     name already exists.
            /// </summary>
            StatusVidPartitionAlreadyExists = 0xC0370008,

            /// <summary>
            ///     MessageId: StatusVidPartitionDoesNotExist
            ///     MessageText:
            ///     The virtualization infrastructure driver has encountered an error. The requested partition does not exist.
            ///     Restarting the virtual machine may fix the problem. If the problem persists, try restarting the physical computer.
            /// </summary>
            StatusVidPartitionDoesNotExist = 0xC0370009,

            /// <summary>
            ///     MessageId: StatusVidPartitionNameNotFound
            ///     MessageText:
            ///     The virtualization infrastructure driver has encountered an error. Could not find the requested partition.
            ///     Restarting the virtual machine may fix the problem. If the problem persists, try restarting the physical computer.
            /// </summary>
            StatusVidPartitionNameNotFound = 0xC037000A,

            /// <summary>
            ///     MessageId: StatusVidMessageQueueAlreadyExists
            ///     MessageText:
            ///     A message queue with the same name already exists for the virtualization infrastructure driver.
            /// </summary>
            StatusVidMessageQueueAlreadyExists = 0xC037000B,

            /// <summary>
            ///     MessageId: StatusVidExceededMbpEntryMapLimit
            ///     MessageText:
            ///     The memory block page for the virtualization infrastructure driver cannot be mapped because the page map limit has
            ///     been reached. Restarting the virtual machine may fix the problem. If the problem persists, try restarting the
            ///     physical computer.
            /// </summary>
            StatusVidExceededMbpEntryMapLimit = 0xC037000C,

            /// <summary>
            ///     MessageId: StatusVidMbStillReferenced
            ///     MessageText:
            ///     The memory block for the virtualization infrastructure driver is still being used and cannot be destroyed.
            /// </summary>
            StatusVidMbStillReferenced = 0xC037000D,

            /// <summary>
            ///     MessageId: StatusVidChildGpaPageSetCorrupted
            ///     MessageText:
            ///     Cannot unlock the page array for the guest operating system memory address because it does not match a previous
            ///     lock request. Restarting the virtual machine may fix the problem. If the problem persists, try restarting the
            ///     physical computer.
            /// </summary>
            StatusVidChildGpaPageSetCorrupted = 0xC037000E,

            /// <summary>
            ///     MessageId: StatusVidInvalidNumaSettings
            ///     MessageText:
            ///     The non-uniform memory access (Numa, node settings do not match the system Numa topology. In order to start the
            ///     virtual machine, you will need to modify the Numa configuration. For detailed information, see
            ///     http://go.microsoft.com/fwlink/?LinkId=92362.
            /// </summary>
            StatusVidInvalidNumaSettings = 0xC037000F,

            /// <summary>
            ///     MessageId: StatusVidInvalidNumaNodeIndex
            ///     MessageText:
            ///     The non-uniform memory access (Numa, node index does not match a valid index in the system Numa topology.
            /// </summary>
            StatusVidInvalidNumaNodeIndex = 0xC0370010,

            /// <summary>
            ///     MessageId: StatusVidNotificationQueueAlreadyAssociated
            ///     MessageText:
            ///     The memory block for the virtualization infrastructure driver is already associated with a message queue.
            /// </summary>
            StatusVidNotificationQueueAlreadyAssociated = 0xC0370011,

            /// <summary>
            ///     MessageId: StatusVidInvalidMemoryBlockHandle
            ///     MessageText:
            ///     The handle is not a valid memory block handle for the virtualization infrastructure driver.
            /// </summary>
            StatusVidInvalidMemoryBlockHandle = 0xC0370012,

            /// <summary>
            ///     MessageId: StatusVidPageRangeOverflow
            ///     MessageText:
            ///     The request exceeded the memory block page limit for the virtualization infrastructure driver. Restarting the
            ///     virtual machine may fix the problem. If the problem persists, try restarting the physical computer.
            /// </summary>
            StatusVidPageRangeOverflow = 0xC0370013,

            /// <summary>
            ///     MessageId: StatusVidInvalidMessageQueueHandle
            ///     MessageText:
            ///     The handle is not a valid message queue handle for the virtualization infrastructure driver.
            /// </summary>
            StatusVidInvalidMessageQueueHandle = 0xC0370014,

            /// <summary>
            ///     MessageId: StatusVidInvalidGpaRangeHandle
            ///     MessageText:
            ///     The handle is not a valid page range handle for the virtualization infrastructure driver.
            /// </summary>
            StatusVidInvalidGpaRangeHandle = 0xC0370015,

            /// <summary>
            ///     MessageId: StatusVidNoMemoryBlockNotificationQueue
            ///     MessageText:
            ///     Cannot install client notifications because no message queue for the virtualization infrastructure driver is
            ///     associated with the memory block.
            /// </summary>
            StatusVidNoMemoryBlockNotificationQueue = 0xC0370016,

            /// <summary>
            ///     MessageId: StatusVidMemoryBlockLockCountExceeded
            ///     MessageText:
            ///     The request to lock or map a memory block page failed because the virtualization infrastructure driver memory block
            ///     limit has been reached. Restarting the virtual machine may fix the problem. If the problem persists, try restarting
            ///     the physical computer.
            /// </summary>
            StatusVidMemoryBlockLockCountExceeded = 0xC0370017,

            /// <summary>
            ///     MessageId: StatusVidInvalidPpmHandle
            ///     MessageText:
            ///     The handle is not a valid parent partition mapping handle for the virtualization infrastructure driver.
            /// </summary>
            StatusVidInvalidPpmHandle = 0xC0370018,

            /// <summary>
            ///     MessageId: StatusVidMbpsAreLocked
            ///     MessageText:
            ///     Notifications cannot be created on the memory block because it is use.
            /// </summary>
            StatusVidMbpsAreLocked = 0xC0370019,

            /// <summary>
            ///     MessageId: StatusVidMessageQueueClosed
            ///     MessageText:
            ///     The message queue for the virtualization infrastructure driver has been closed. Restarting the virtual machine may
            ///     fix the problem. If the problem persists, try restarting the physical computer.
            /// </summary>
            StatusVidMessageQueueClosed = 0xC037001A,

            /// <summary>
            ///     MessageId: StatusVidVirtualProcessorLimitExceeded
            ///     MessageText:
            ///     Cannot add a virtual processor to the partition because the maximum has been reached.
            /// </summary>
            StatusVidVirtualProcessorLimitExceeded = 0xC037001B,

            /// <summary>
            ///     MessageId: StatusVidStopPending
            ///     MessageText:
            ///     Cannot stop the virtual processor immediately because of a pending intercept.
            /// </summary>
            StatusVidStopPending = 0xC037001C,

            /// <summary>
            ///     MessageId: StatusVidInvalidProcessorState
            ///     MessageText:
            ///     Invalid state for the virtual processor. Restarting the virtual machine may fix the problem. If the problem
            ///     persists, try restarting the physical computer.
            /// </summary>
            StatusVidInvalidProcessorState = 0xC037001D,

            /// <summary>
            ///     MessageId: StatusVidExceededKmContextCountLimit
            ///     MessageText:
            ///     The maximum number of kernel mode clients for the virtualization infrastructure driver has been reached. Restarting
            ///     the virtual machine may fix the problem. If the problem persists, try restarting the physical computer.
            /// </summary>
            StatusVidExceededKmContextCountLimit = 0xC037001E,

            /// <summary>
            ///     MessageId: StatusVidKmInterfaceAlreadyInitialized
            ///     MessageText:
            ///     This kernel mode interface for the virtualization infrastructure driver has already been initialized. Restarting
            ///     the virtual machine may fix the problem. If the problem persists, try restarting the physical computer.
            /// </summary>
            StatusVidKmInterfaceAlreadyInitialized = 0xC037001F,

            /// <summary>
            ///     MessageId: StatusVidMbPropertyAlreadySetReset
            ///     MessageText:
            ///     Cannot set or reset the memory block property more than once for the virtualization infrastructure driver.
            ///     Restarting the virtual machine may fix the problem. If the problem persists, try restarting the physical computer.
            /// </summary>
            StatusVidMbPropertyAlreadySetReset = 0xC0370020,

            /// <summary>
            ///     MessageId: StatusVidMmioRangeDestroyed
            ///     MessageText:
            ///     The memory mapped I/O for this page range no longer exists. Restarting the virtual machine may fix the problem. If
            ///     the problem persists, try restarting the physical computer.
            /// </summary>
            StatusVidMmioRangeDestroyed = 0xC0370021,

            /// <summary>
            ///     MessageId: StatusVidInvalidChildGpaPageSet
            ///     MessageText:
            ///     The lock or unlock request uses an invalid guest operating system memory address. Restarting the virtual machine
            ///     may fix the problem. If the problem persists, try restarting the physical computer.
            /// </summary>
            StatusVidInvalidChildGpaPageSet = 0xC0370022,

            /// <summary>
            ///     MessageId: StatusVidReservePageSetIsBeingUsed
            ///     MessageText:
            ///     Cannot destroy or reuse the reserve page set for the virtualization infrastructure driver because it is in use.
            ///     Restarting the virtual machine may fix the problem. If the problem persists, try restarting the physical computer.
            /// </summary>
            StatusVidReservePageSetIsBeingUsed = 0xC0370023,

            /// <summary>
            ///     MessageId: StatusVidReservePageSetTooSmall
            ///     MessageText:
            ///     The reserve page set for the virtualization infrastructure driver is too small to use in the lock request.
            ///     Restarting the virtual machine may fix the problem. If the problem persists, try restarting the physical computer.
            /// </summary>
            StatusVidReservePageSetTooSmall = 0xC0370024,

            /// <summary>
            ///     MessageId: StatusVidMbpAlreadyLockedUsingReservedPage
            ///     MessageText:
            ///     Cannot lock or map the memory block page for the virtualization infrastructure driver because it has already been
            ///     locked using a reserve page set page. Restarting the virtual machine may fix the problem. If the problem persists,
            ///     try restarting the physical computer.
            /// </summary>
            StatusVidMbpAlreadyLockedUsingReservedPage = 0xC0370025,

            /// <summary>
            ///     MessageId: StatusVidMbpCountExceededLimit
            ///     MessageText:
            ///     Cannot create the memory block for the virtualization infrastructure driver because the requested number of pages
            ///     exceeded the limit. Restarting the virtual machine may fix the problem. If the problem persists, try restarting the
            ///     physical computer.
            /// </summary>
            StatusVidMbpCountExceededLimit = 0xC0370026,

            /// <summary>
            ///     MessageId: StatusVidSavedStateCorrupt
            ///     MessageText:
            ///     Cannot restore this virtual machine because the saved state data cannot be read. Delete the saved state data and
            ///     then try to start the virtual machine.
            /// </summary>
            StatusVidSavedStateCorrupt = 0xC0370027,

            /// <summary>
            ///     MessageId: StatusVidSavedStateUnrecognizedItem
            ///     MessageText:
            ///     Cannot restore this virtual machine because an item read from the saved state data is not recognized. Delete the
            ///     saved state data and then try to start the virtual machine.
            /// </summary>
            StatusVidSavedStateUnrecognizedItem = 0xC0370028,

            /// <summary>
            ///     MessageId: StatusVidSavedStateIncompatible
            ///     MessageText:
            ///     Cannot restore this virtual machine to the saved state because of hypervisor incompatibility. Delete the saved
            ///     state data and then try to start the virtual machine.
            /// </summary>
            StatusVidSavedStateIncompatible = 0xC0370029,

            /// <summary>
            ///     MessageId: StatusVidRemoteNodeParentGpaPagesUsed
            ///     MessageText:
            ///     A virtual machine is running with its memory allocated across multiple Numa nodes. This does not indicate a problem
            ///     unless the performance of your virtual machine is unusually slow. If you are experiencing performance problems, you
            ///     may need to modify the Numa configuration. For detailed information, see
            ///     http://go.microsoft.com/fwlink/?LinkId=92362.
            /// </summary>
            StatusVidRemoteNodeParentGpaPagesUsed = 0x80370001,

            // Ipsec error codes (tcpip.sys,

            /// <summary>
            ///     MessageId: StatusIpsecBadSpi
            ///     MessageText:
            ///     The Spi in the packet does not match a valid IPsec Sa.
            /// </summary>
            StatusIpsecBadSpi = 0xC0360001,

            /// <summary>
            ///     MessageId: StatusIpsecSaLifetimeExpired
            ///     MessageText:
            ///     Packet was received on an IPsec Sa whose lifetime has expired.
            /// </summary>
            StatusIpsecSaLifetimeExpired = 0xC0360002,

            /// <summary>
            ///     MessageId: StatusIpsecWrongSa
            ///     MessageText:
            ///     Packet was received on an IPsec Sa that does not match the packet characteristics.
            /// </summary>
            StatusIpsecWrongSa = 0xC0360003,

            /// <summary>
            ///     MessageId: StatusIpsecReplayCheckFailed
            ///     MessageText:
            ///     Packet sequence number replay check failed.
            /// </summary>
            StatusIpsecReplayCheckFailed = 0xC0360004,

            /// <summary>
            ///     MessageId: StatusIpsecInvalidPacket
            ///     MessageText:
            ///     IPsec header and/or trailer in the packet is invalid.
            /// </summary>
            StatusIpsecInvalidPacket = 0xC0360005,

            /// <summary>
            ///     MessageId: StatusIpsecIntegrityCheckFailed
            ///     MessageText:
            ///     IPsec integrity check failed.
            /// </summary>
            StatusIpsecIntegrityCheckFailed = 0xC0360006,

            /// <summary>
            ///     MessageId: StatusIpsecClearTextDrop
            ///     MessageText:
            ///     IPsec dropped a clear text packet.
            /// </summary>
            StatusIpsecClearTextDrop = 0xC0360007,

            /// <summary>
            ///     MessageId: StatusIpsecAuthFirewallDrop
            ///     MessageText:
            ///     IPsec dropped an incoming Esp packet in authenticated firewall mode. This drop is benign.
            /// </summary>
            StatusIpsecAuthFirewallDrop = 0xC0360008,

            /// <summary>
            ///     MessageId: StatusIpsecThrottleDrop
            ///     MessageText:
            ///     IPsec dropped a packet due to DoS throttling.
            /// </summary>
            StatusIpsecThrottleDrop = 0xC0360009,

            /// <summary>
            ///     MessageId: StatusIpsecDospBlock
            ///     MessageText:
            ///     IPsec DoS Protection matched an explicit block rule.
            /// </summary>
            StatusIpsecDospBlock = 0xC0368000,

            /// <summary>
            ///     MessageId: StatusIpsecDospReceivedMulticast
            ///     MessageText:
            ///     IPsec DoS Protection received an IPsec specific multicast packet which is not allowed.
            /// </summary>
            StatusIpsecDospReceivedMulticast = 0xC0368001,

            /// <summary>
            ///     MessageId: StatusIpsecDospInvalidPacket
            ///     MessageText:
            ///     IPsec DoS Protection received an incorrectly formatted packet.
            /// </summary>
            StatusIpsecDospInvalidPacket = 0xC0368002,

            /// <summary>
            ///     MessageId: StatusIpsecDospStateLookupFailed
            ///     MessageText:
            ///     IPsec DoS Protection failed to look up state.
            /// </summary>
            StatusIpsecDospStateLookupFailed = 0xC0368003,

            /// <summary>
            ///     MessageId: StatusIpsecDospMaxEntries
            ///     MessageText:
            ///     IPsec DoS Protection failed to create state because the maximum number of entries allowed by policy has been
            ///     reached.
            /// </summary>
            StatusIpsecDospMaxEntries = 0xC0368004,

            /// <summary>
            ///     MessageId: StatusIpsecDospKeymodNotAllowed
            ///     MessageText:
            ///     IPsec DoS Protection received an IPsec negotiation packet for a keying module which is not allowed by policy.
            /// </summary>
            StatusIpsecDospKeymodNotAllowed = 0xC0368005,

            /// <summary>
            ///     MessageId: StatusIpsecDospMaxPerIpRatelimitQueues
            ///     MessageText:
            ///     IPsec DoS Protection failed to create a per internal Ip rate limit queue because the maximum number of queues
            ///     allowed by policy has been reached.
            /// </summary>
            StatusIpsecDospMaxPerIpRatelimitQueues = 0xC0368006,

            // Volume manager status codes (volmgr.sys and volmgrx.sys,

            /// <summary>
            ///     MessageId: StatusVolmgrIncompleteRegeneration
            ///     MessageText:
            ///     The regeneration operation was not able to copy all data from the active plexes due to bad sectors.
            /// </summary>
            StatusVolmgrIncompleteRegeneration = 0x80380001,

            /// <summary>
            ///     MessageId: StatusVolmgrIncompleteDiskMigration
            ///     MessageText:
            ///     One or more disks were not fully migrated to the target pack. They may or may not require reimport after fixing the
            ///     hardware problems.
            /// </summary>
            StatusVolmgrIncompleteDiskMigration = 0x80380002,

            /// <summary>
            ///     MessageId: StatusVolmgrDatabaseFull
            ///     MessageText:
            ///     The configuration database is full.
            /// </summary>
            StatusVolmgrDatabaseFull = 0xC0380001,

            /// <summary>
            ///     MessageId: StatusVolmgrDiskConfigurationCorrupted
            ///     MessageText:
            ///     The configuration data on the disk is corrupted.
            /// </summary>
            StatusVolmgrDiskConfigurationCorrupted = 0xC0380002,

            /// <summary>
            ///     MessageId: StatusVolmgrDiskConfigurationNotInSync
            ///     MessageText:
            ///     The configuration on the disk is not insync with the in-memory configuration.
            /// </summary>
            StatusVolmgrDiskConfigurationNotInSync = 0xC0380003,

            /// <summary>
            ///     MessageId: StatusVolmgrPackConfigUpdateFailed
            ///     MessageText:
            ///     A majority of disks failed to be updated with the new configuration.
            /// </summary>
            StatusVolmgrPackConfigUpdateFailed = 0xC0380004,

            /// <summary>
            ///     MessageId: StatusVolmgrDiskContainsNonSimpleVolume
            ///     MessageText:
            ///     The disk contains non-simple volumes.
            /// </summary>
            StatusVolmgrDiskContainsNonSimpleVolume = 0xC0380005,

            /// <summary>
            ///     MessageId: StatusVolmgrDiskDuplicate
            ///     MessageText:
            ///     The same disk was specified more than once in the migration list.
            /// </summary>
            StatusVolmgrDiskDuplicate = 0xC0380006,

            /// <summary>
            ///     MessageId: StatusVolmgrDiskDynamic
            ///     MessageText:
            ///     The disk is already dynamic.
            /// </summary>
            StatusVolmgrDiskDynamic = 0xC0380007,

            /// <summary>
            ///     MessageId: StatusVolmgrDiskIdInvalid
            ///     MessageText:
            ///     The specified disk id is invalid. There are no disks with the specified disk id.
            /// </summary>
            StatusVolmgrDiskIdInvalid = 0xC0380008,

            /// <summary>
            ///     MessageId: StatusVolmgrDiskInvalid
            ///     MessageText:
            ///     The specified disk is an invalid disk. Operation cannot complete on an invalid disk.
            /// </summary>
            StatusVolmgrDiskInvalid = 0xC0380009,

            /// <summary>
            ///     MessageId: StatusVolmgrDiskLastVoter
            ///     MessageText:
            ///     The specified disk(s, cannot be removed since it is the last remaining voter.
            /// </summary>
            StatusVolmgrDiskLastVoter = 0xC038000A,

            /// <summary>
            ///     MessageId: StatusVolmgrDiskLayoutInvalid
            ///     MessageText:
            ///     The specified disk has an invalid disk layout.
            /// </summary>
            StatusVolmgrDiskLayoutInvalid = 0xC038000B,

            /// <summary>
            ///     MessageId: StatusVolmgrDiskLayoutNonBasicBetweenBasicPartitions
            ///     MessageText:
            ///     The disk layout contains non-basic partitions which appear after basic paritions. This is an invalid disk layout.
            /// </summary>
            StatusVolmgrDiskLayoutNonBasicBetweenBasicPartitions = 0xC038000C,

            /// <summary>
            ///     MessageId: StatusVolmgrDiskLayoutNotCylinderAligned
            ///     MessageText:
            ///     The disk layout contains partitions which are not cylinder aligned.
            /// </summary>
            StatusVolmgrDiskLayoutNotCylinderAligned = 0xC038000D,

            /// <summary>
            ///     MessageId: StatusVolmgrDiskLayoutPartitionsTooSmall
            ///     MessageText:
            ///     The disk layout contains partitions which are samller than the minimum size.
            /// </summary>
            StatusVolmgrDiskLayoutPartitionsTooSmall = 0xC038000E,

            /// <summary>
            ///     MessageId: StatusVolmgrDiskLayoutPrimaryBetweenLogicalPartitions
            ///     MessageText:
            ///     The disk layout contains primary partitions in between logical drives. This is an invalid disk layout.
            /// </summary>
            StatusVolmgrDiskLayoutPrimaryBetweenLogicalPartitions = 0xC038000F,

            /// <summary>
            ///     MessageId: StatusVolmgrDiskLayoutTooManyPartitions
            ///     MessageText:
            ///     The disk layout contains more than the maximum number of supported partitions.
            /// </summary>
            StatusVolmgrDiskLayoutTooManyPartitions = 0xC0380010,

            /// <summary>
            ///     MessageId: StatusVolmgrDiskMissing
            ///     MessageText:
            ///     The specified disk is missing. The operation cannot complete on a missing disk.
            /// </summary>
            StatusVolmgrDiskMissing = 0xC0380011,

            /// <summary>
            ///     MessageId: StatusVolmgrDiskNotEmpty
            ///     MessageText:
            ///     The specified disk is not empty.
            /// </summary>
            StatusVolmgrDiskNotEmpty = 0xC0380012,

            /// <summary>
            ///     MessageId: StatusVolmgrDiskNotEnoughSpace
            ///     MessageText:
            ///     There is not enough usable space for this operation.
            /// </summary>
            StatusVolmgrDiskNotEnoughSpace = 0xC0380013,

            /// <summary>
            ///     MessageId: StatusVolmgrDiskRevectoringFailed
            ///     MessageText:
            ///     The force revectoring of bad sectors failed.
            /// </summary>
            StatusVolmgrDiskRevectoringFailed = 0xC0380014,

            /// <summary>
            ///     MessageId: StatusVolmgrDiskSectorSizeInvalid
            ///     MessageText:
            ///     The specified disk has an invalid sector size.
            /// </summary>
            StatusVolmgrDiskSectorSizeInvalid = 0xC0380015,

            /// <summary>
            ///     MessageId: StatusVolmgrDiskSetNotContained
            ///     MessageText:
            ///     The specified disk set contains volumes which exist on disks outside of the set.
            /// </summary>
            StatusVolmgrDiskSetNotContained = 0xC0380016,

            /// <summary>
            ///     MessageId: StatusVolmgrDiskUsedByMultipleMembers
            ///     MessageText:
            ///     A disk in the volume layout provides extents to more than one member of a plex.
            /// </summary>
            StatusVolmgrDiskUsedByMultipleMembers = 0xC0380017,

            /// <summary>
            ///     MessageId: StatusVolmgrDiskUsedByMultiplePlexes
            ///     MessageText:
            ///     A disk in the volume layout provides extents to more than one plex.
            /// </summary>
            StatusVolmgrDiskUsedByMultiplePlexes = 0xC0380018,

            /// <summary>
            ///     MessageId: StatusVolmgrDynamicDiskNotSupported
            ///     MessageText:
            ///     Dynamic disks are not supported on this system.
            /// </summary>
            StatusVolmgrDynamicDiskNotSupported = 0xC0380019,

            /// <summary>
            ///     MessageId: StatusVolmgrExtentAlreadyUsed
            ///     MessageText:
            ///     The specified extent is already used by other volumes.
            /// </summary>
            StatusVolmgrExtentAlreadyUsed = 0xC038001A,

            /// <summary>
            ///     MessageId: StatusVolmgrExtentNotContiguous
            ///     MessageText:
            ///     The specified volume is retained and can only be extended into a contiguous extent. The specified extent to grow
            ///     the volume is not contiguous with the specified volume.
            /// </summary>
            StatusVolmgrExtentNotContiguous = 0xC038001B,

            /// <summary>
            ///     MessageId: StatusVolmgrExtentNotInPublicRegion
            ///     MessageText:
            ///     The specified volume extent is not within the public region of the disk.
            /// </summary>
            StatusVolmgrExtentNotInPublicRegion = 0xC038001C,

            /// <summary>
            ///     MessageId: StatusVolmgrExtentNotSectorAligned
            ///     MessageText:
            ///     The specifed volume extent is not sector aligned.
            /// </summary>
            StatusVolmgrExtentNotSectorAligned = 0xC038001D,

            /// <summary>
            ///     MessageId: StatusVolmgrExtentOverlapsEbrPartition
            ///     MessageText:
            ///     The specified parition overlaps an Ebr (the first track of an extended partition on a Mbr disks,.
            /// </summary>
            StatusVolmgrExtentOverlapsEbrPartition = 0xC038001E,

            /// <summary>
            ///     MessageId: StatusVolmgrExtentVolumeLengthsDoNotMatch
            ///     MessageText:
            ///     The specified extent lengths cannot be used to construct a volume with specified length.
            /// </summary>
            StatusVolmgrExtentVolumeLengthsDoNotMatch = 0xC038001F,

            /// <summary>
            ///     MessageId: StatusVolmgrFaultTolerantNotSupported
            ///     MessageText:
            ///     The system does not support fault tolerant volumes.
            /// </summary>
            StatusVolmgrFaultTolerantNotSupported = 0xC0380020,

            /// <summary>
            ///     MessageId: StatusVolmgrInterleaveLengthInvalid
            ///     MessageText:
            ///     The specified interleave length is invalid.
            /// </summary>
            StatusVolmgrInterleaveLengthInvalid = 0xC0380021,

            /// <summary>
            ///     MessageId: StatusVolmgrMaximumRegisteredUsers
            ///     MessageText:
            ///     There is already a maximum number of registered users.
            /// </summary>
            StatusVolmgrMaximumRegisteredUsers = 0xC0380022,

            /// <summary>
            ///     MessageId: StatusVolmgrMemberInSync
            ///     MessageText:
            ///     The specified member is already in-sync with the other active members. It does not need to be regenerated.
            /// </summary>
            StatusVolmgrMemberInSync = 0xC0380023,

            /// <summary>
            ///     MessageId: StatusVolmgrMemberIndexDuplicate
            ///     MessageText:
            ///     The same member index was specified more than once.
            /// </summary>
            StatusVolmgrMemberIndexDuplicate = 0xC0380024,

            /// <summary>
            ///     MessageId: StatusVolmgrMemberIndexInvalid
            ///     MessageText:
            ///     The specified member index is greater or equal than the number of members in the volume plex.
            /// </summary>
            StatusVolmgrMemberIndexInvalid = 0xC0380025,

            /// <summary>
            ///     MessageId: StatusVolmgrMemberMissing
            ///     MessageText:
            ///     The specified member is missing. It cannot be regenerated.
            /// </summary>
            StatusVolmgrMemberMissing = 0xC0380026,

            /// <summary>
            ///     MessageId: StatusVolmgrMemberNotDetached
            ///     MessageText:
            ///     The specified member is not detached. Cannot replace a member which is not detached.
            /// </summary>
            StatusVolmgrMemberNotDetached = 0xC0380027,

            /// <summary>
            ///     MessageId: StatusVolmgrMemberRegenerating
            ///     MessageText:
            ///     The specified member is already regenerating.
            /// </summary>
            StatusVolmgrMemberRegenerating = 0xC0380028,

            /// <summary>
            ///     MessageId: StatusVolmgrAllDisksFailed
            ///     MessageText:
            ///     All disks belonging to the pack failed.
            /// </summary>
            StatusVolmgrAllDisksFailed = 0xC0380029,

            /// <summary>
            ///     MessageId: StatusVolmgrNoRegisteredUsers
            ///     MessageText:
            ///     There are currently no registered users for notifications. The task number is irrelevant unless there are
            ///     registered users.
            /// </summary>
            StatusVolmgrNoRegisteredUsers = 0xC038002A,

            /// <summary>
            ///     MessageId: StatusVolmgrNoSuchUser
            ///     MessageText:
            ///     The specified notification user does not exist. Failed to unregister user for notifications.
            /// </summary>
            StatusVolmgrNoSuchUser = 0xC038002B,

            /// <summary>
            ///     MessageId: StatusVolmgrNotificationReset
            ///     MessageText:
            ///     The notifications have been reset. Notifications for the current user are invalid. Unregister and re-register for
            ///     notifications.
            /// </summary>
            StatusVolmgrNotificationReset = 0xC038002C,

            /// <summary>
            ///     MessageId: StatusVolmgrNumberOfMembersInvalid
            ///     MessageText:
            ///     The specified number of members is invalid.
            /// </summary>
            StatusVolmgrNumberOfMembersInvalid = 0xC038002D,

            /// <summary>
            ///     MessageId: StatusVolmgrNumberOfPlexesInvalid
            ///     MessageText:
            ///     The specified number of plexes is invalid.
            /// </summary>
            StatusVolmgrNumberOfPlexesInvalid = 0xC038002E,

            /// <summary>
            ///     MessageId: StatusVolmgrPackDuplicate
            ///     MessageText:
            ///     The specified source and target packs are identical.
            /// </summary>
            StatusVolmgrPackDuplicate = 0xC038002F,

            /// <summary>
            ///     MessageId: StatusVolmgrPackIdInvalid
            ///     MessageText:
            ///     The specified pack id is invalid. There are no packs with the specified pack id.
            /// </summary>
            StatusVolmgrPackIdInvalid = 0xC0380030,

            /// <summary>
            ///     MessageId: StatusVolmgrPackInvalid
            ///     MessageText:
            ///     The specified pack is the invalid pack. The operation cannot complete with the invalid pack.
            /// </summary>
            StatusVolmgrPackInvalid = 0xC0380031,

            /// <summary>
            ///     MessageId: StatusVolmgrPackNameInvalid
            ///     MessageText:
            ///     The specified pack name is invalid.
            /// </summary>
            StatusVolmgrPackNameInvalid = 0xC0380032,

            /// <summary>
            ///     MessageId: StatusVolmgrPackOffline
            ///     MessageText:
            ///     The specified pack is offline.
            /// </summary>
            StatusVolmgrPackOffline = 0xC0380033,

            /// <summary>
            ///     MessageId: StatusVolmgrPackHasQuorum
            ///     MessageText:
            ///     The specified pack already has a quorum of healthy disks.
            /// </summary>
            StatusVolmgrPackHasQuorum = 0xC0380034,

            /// <summary>
            ///     MessageId: StatusVolmgrPackWithoutQuorum
            ///     MessageText:
            ///     The pack does not have a quorum of healthy disks.
            /// </summary>
            StatusVolmgrPackWithoutQuorum = 0xC0380035,

            /// <summary>
            ///     MessageId: StatusVolmgrPartitionStyleInvalid
            ///     MessageText:
            ///     The specified disk has an unsupported partition style. Only Mbr and Gpt partition styles are supported.
            /// </summary>
            StatusVolmgrPartitionStyleInvalid = 0xC0380036,

            /// <summary>
            ///     MessageId: StatusVolmgrPartitionUpdateFailed
            ///     MessageText:
            ///     Failed to update the disk's partition layout.
            /// </summary>
            StatusVolmgrPartitionUpdateFailed = 0xC0380037,

            /// <summary>
            ///     MessageId: StatusVolmgrPlexInSync
            ///     MessageText:
            ///     The specified plex is already in-sync with the other active plexes. It does not need to be regenerated.
            /// </summary>
            StatusVolmgrPlexInSync = 0xC0380038,

            /// <summary>
            ///     MessageId: StatusVolmgrPlexIndexDuplicate
            ///     MessageText:
            ///     The same plex index was specified more than once.
            /// </summary>
            StatusVolmgrPlexIndexDuplicate = 0xC0380039,

            /// <summary>
            ///     MessageId: StatusVolmgrPlexIndexInvalid
            ///     MessageText:
            ///     The specified plex index is greater or equal than the number of plexes in the volume.
            /// </summary>
            StatusVolmgrPlexIndexInvalid = 0xC038003A,

            /// <summary>
            ///     MessageId: StatusVolmgrPlexLastActive
            ///     MessageText:
            ///     The specified plex is the last active plex in the volume. The plex cannot be removed or else the volume will go
            ///     offline.
            /// </summary>
            StatusVolmgrPlexLastActive = 0xC038003B,

            /// <summary>
            ///     MessageId: StatusVolmgrPlexMissing
            ///     MessageText:
            ///     The specified plex is missing.
            /// </summary>
            StatusVolmgrPlexMissing = 0xC038003C,

            /// <summary>
            ///     MessageId: StatusVolmgrPlexRegenerating
            ///     MessageText:
            ///     The specified plex is currently regenerating.
            /// </summary>
            StatusVolmgrPlexRegenerating = 0xC038003D,

            /// <summary>
            ///     MessageId: StatusVolmgrPlexTypeInvalid
            ///     MessageText:
            ///     The specified plex type is invalid.
            /// </summary>
            StatusVolmgrPlexTypeInvalid = 0xC038003E,

            /// <summary>
            ///     MessageId: StatusVolmgrPlexNotRaid5
            ///     MessageText:
            ///     The operation is only supported on Raid-5 plexes.
            /// </summary>
            StatusVolmgrPlexNotRaid5 = 0xC038003F,

            /// <summary>
            ///     MessageId: StatusVolmgrPlexNotSimple
            ///     MessageText:
            ///     The operation is only supported on simple plexes.
            /// </summary>
            StatusVolmgrPlexNotSimple = 0xC0380040,

            /// <summary>
            ///     MessageId: StatusVolmgrStructureSizeInvalid
            ///     MessageText:
            ///     The Size fields in the VmVolumeLayout input structure are incorrectly set.
            /// </summary>
            StatusVolmgrStructureSizeInvalid = 0xC0380041,

            /// <summary>
            ///     MessageId: StatusVolmgrTooManyNotificationRequests
            ///     MessageText:
            ///     There is already a pending request for notifications. Wait for the existing request to return before requesting for
            ///     more notifications.
            /// </summary>
            StatusVolmgrTooManyNotificationRequests = 0xC0380042,

            /// <summary>
            ///     MessageId: StatusVolmgrTransactionInProgress
            ///     MessageText:
            ///     There is currently a transaction in process.
            /// </summary>
            StatusVolmgrTransactionInProgress = 0xC0380043,

            /// <summary>
            ///     MessageId: StatusVolmgrUnexpectedDiskLayoutChange
            ///     MessageText:
            ///     An unexpected layout change occurred outside of the volume manager.
            /// </summary>
            StatusVolmgrUnexpectedDiskLayoutChange = 0xC0380044,

            /// <summary>
            ///     MessageId: StatusVolmgrVolumeContainsMissingDisk
            ///     MessageText:
            ///     The specified volume contains a missing disk.
            /// </summary>
            StatusVolmgrVolumeContainsMissingDisk = 0xC0380045,

            /// <summary>
            ///     MessageId: StatusVolmgrVolumeIdInvalid
            ///     MessageText:
            ///     The specified volume id is invalid. There are no volumes with the specified volume id.
            /// </summary>
            StatusVolmgrVolumeIdInvalid = 0xC0380046,

            /// <summary>
            ///     MessageId: StatusVolmgrVolumeLengthInvalid
            ///     MessageText:
            ///     The specified volume length is invalid.
            /// </summary>
            StatusVolmgrVolumeLengthInvalid = 0xC0380047,

            /// <summary>
            ///     MessageId: StatusVolmgrVolumeLengthNotSectorSizeMultiple
            ///     MessageText:
            ///     The specified size for the volume is not a multiple of the sector size.
            /// </summary>
            StatusVolmgrVolumeLengthNotSectorSizeMultiple = 0xC0380048,

            /// <summary>
            ///     MessageId: StatusVolmgrVolumeNotMirrored
            ///     MessageText:
            ///     The operation is only supported on mirrored volumes.
            /// </summary>
            StatusVolmgrVolumeNotMirrored = 0xC0380049,

            /// <summary>
            ///     MessageId: StatusVolmgrVolumeNotRetained
            ///     MessageText:
            ///     The specified volume does not have a retain partition.
            /// </summary>
            StatusVolmgrVolumeNotRetained = 0xC038004A,

            /// <summary>
            ///     MessageId: StatusVolmgrVolumeOffline
            ///     MessageText:
            ///     The specified volume is offline.
            /// </summary>
            StatusVolmgrVolumeOffline = 0xC038004B,

            /// <summary>
            ///     MessageId: StatusVolmgrVolumeRetained
            ///     MessageText:
            ///     The specified volume already has a retain partition.
            /// </summary>
            StatusVolmgrVolumeRetained = 0xC038004C,

            /// <summary>
            ///     MessageId: StatusVolmgrNumberOfExtentsInvalid
            ///     MessageText:
            ///     The specified number of extents is invalid.
            /// </summary>
            StatusVolmgrNumberOfExtentsInvalid = 0xC038004D,

            /// <summary>
            ///     MessageId: StatusVolmgrDifferentSectorSize
            ///     MessageText:
            ///     All disks participating to the volume must have the same sector size.
            /// </summary>
            StatusVolmgrDifferentSectorSize = 0xC038004E,

            /// <summary>
            ///     MessageId: StatusVolmgrBadBootDisk
            ///     MessageText:
            ///     The boot disk experienced failures.
            /// </summary>
            StatusVolmgrBadBootDisk = 0xC038004F,

            /// <summary>
            ///     MessageId: StatusVolmgrPackConfigOffline
            ///     MessageText:
            ///     The configuration of the pack is offline.
            /// </summary>
            StatusVolmgrPackConfigOffline = 0xC0380050,

            /// <summary>
            ///     MessageId: StatusVolmgrPackConfigOnline
            ///     MessageText:
            ///     The configuration of the pack is online.
            /// </summary>
            StatusVolmgrPackConfigOnline = 0xC0380051,

            /// <summary>
            ///     MessageId: StatusVolmgrNotPrimaryPack
            ///     MessageText:
            ///     The specified pack is not the primary pack.
            /// </summary>
            StatusVolmgrNotPrimaryPack = 0xC0380052,

            /// <summary>
            ///     MessageId: StatusVolmgrPackLogUpdateFailed
            ///     MessageText:
            ///     All disks failed to be updated with the new content of the log.
            /// </summary>
            StatusVolmgrPackLogUpdateFailed = 0xC0380053,

            /// <summary>
            ///     MessageId: StatusVolmgrNumberOfDisksInPlexInvalid
            ///     MessageText:
            ///     The specified number of disks in a plex is invalid.
            /// </summary>
            StatusVolmgrNumberOfDisksInPlexInvalid = 0xC0380054,

            /// <summary>
            ///     MessageId: StatusVolmgrNumberOfDisksInMemberInvalid
            ///     MessageText:
            ///     The specified number of disks in a plex member is invalid.
            /// </summary>
            StatusVolmgrNumberOfDisksInMemberInvalid = 0xC0380055,

            /// <summary>
            ///     MessageId: StatusVolmgrVolumeMirrored
            ///     MessageText:
            ///     The operation is not supported on mirrored volumes.
            /// </summary>
            StatusVolmgrVolumeMirrored = 0xC0380056,

            /// <summary>
            ///     MessageId: StatusVolmgrPlexNotSimpleSpanned
            ///     MessageText:
            ///     The operation is only supported on simple and spanned plexes.
            /// </summary>
            StatusVolmgrPlexNotSimpleSpanned = 0xC0380057,

            /// <summary>
            ///     MessageId: StatusVolmgrNoValidLogCopies
            ///     MessageText:
            ///     The pack has no valid log copies.
            /// </summary>
            StatusVolmgrNoValidLogCopies = 0xC0380058,

            /// <summary>
            ///     MessageId: StatusVolmgrPrimaryPackPresent
            ///     MessageText:
            ///     A primary pack is already present.
            /// </summary>
            StatusVolmgrPrimaryPackPresent = 0xC0380059,

            /// <summary>
            ///     MessageId: StatusVolmgrNumberOfDisksInvalid
            ///     MessageText:
            ///     The specified number of disks is invalid.
            /// </summary>
            StatusVolmgrNumberOfDisksInvalid = 0xC038005A,

            /// <summary>
            ///     MessageId: StatusVolmgrMirrorNotSupported
            ///     MessageText:
            ///     The system does not support mirrored volumes.
            /// </summary>
            StatusVolmgrMirrorNotSupported = 0xC038005B,

            /// <summary>
            ///     MessageId: StatusVolmgrRaid5NotSupported
            ///     MessageText:
            ///     The system does not support Raid-5 volumes.
            /// </summary>
            StatusVolmgrRaid5NotSupported = 0xC038005C,

            // Boot Code Data (Bcd, status codes

            /// <summary>
            ///     MessageId: StatusBcdNotAllEntriesImported
            ///     MessageText:
            ///     Some Bcd entries were not imported correctly from the Bcd store.
            /// </summary>
            StatusBcdNotAllEntriesImported = 0x80390001,

            /// <summary>
            ///     MessageId: StatusBcdTooManyElements
            ///     MessageText:
            ///     Entries enumerated have exceeded the allowed threshold.
            /// </summary>
            StatusBcdTooManyElements = 0xC0390002,

            /// <summary>
            ///     MessageId: StatusBcdNotAllEntriesSynchronized
            ///     MessageText:
            ///     Some Bcd entries were not synchronized correctly with the firmware.
            /// </summary>
            StatusBcdNotAllEntriesSynchronized = 0x80390003,

            // vhdparser error codes (vhdparser.sys,

            /// <summary>
            ///     MessageId: StatusVhdDriveFooterMissing
            ///     MessageText:
            ///     The virtual hard disk is corrupted. The virtual hard disk drive footer is missing.
            /// </summary>
            StatusVhdDriveFooterMissing = 0xC03A0001,

            /// <summary>
            ///     MessageId: StatusVhdDriveFooterChecksumMismatch
            ///     MessageText:
            ///     The virtual hard disk is corrupted. The virtual hard disk drive footer checksum does not match the on-disk
            ///     checksum.
            /// </summary>
            StatusVhdDriveFooterChecksumMismatch = 0xC03A0002,

            /// <summary>
            ///     MessageId: StatusVhdDriveFooterCorrupt
            ///     MessageText:
            ///     The virtual hard disk is corrupted. The virtual hard disk drive footer in the virtual hard disk is corrupted.
            /// </summary>
            StatusVhdDriveFooterCorrupt = 0xC03A0003,

            /// <summary>
            ///     MessageId: StatusVhdFormatUnknown
            ///     MessageText:
            ///     The system does not recognize the file format of this virtual hard disk.
            /// </summary>
            StatusVhdFormatUnknown = 0xC03A0004,

            /// <summary>
            ///     MessageId: StatusVhdFormatUnsupportedVersion
            ///     MessageText:
            ///     The version does not support this version of the file format.
            /// </summary>
            StatusVhdFormatUnsupportedVersion = 0xC03A0005,

            /// <summary>
            ///     MessageId: StatusVhdSparseHeaderChecksumMismatch
            ///     MessageText:
            ///     The virtual hard disk is corrupted. The sparse header checksum does not match the on-disk checksum.
            /// </summary>
            StatusVhdSparseHeaderChecksumMismatch = 0xC03A0006,

            /// <summary>
            ///     MessageId: StatusVhdSparseHeaderUnsupportedVersion
            ///     MessageText:
            ///     The system does not support this version of the virtual hard disk.This version of the sparse header is not
            ///     supported.
            /// </summary>
            StatusVhdSparseHeaderUnsupportedVersion = 0xC03A0007,

            /// <summary>
            ///     MessageId: StatusVhdSparseHeaderCorrupt
            ///     MessageText:
            ///     The virtual hard disk is corrupted. The sparse header in the virtual hard disk is corrupt.
            /// </summary>
            StatusVhdSparseHeaderCorrupt = 0xC03A0008,

            /// <summary>
            ///     MessageId: StatusVhdBlockAllocationFailure
            ///     MessageText:
            ///     Failed to write to the virtual hard disk failed because the system failed to allocate a new block in the virtual
            ///     hard disk.
            /// </summary>
            StatusVhdBlockAllocationFailure = 0xC03A0009,

            /// <summary>
            ///     MessageId: StatusVhdBlockAllocationTableCorrupt
            ///     MessageText:
            ///     The virtual hard disk is corrupted. The block allocation table in the virtual hard disk is corrupt.
            /// </summary>
            StatusVhdBlockAllocationTableCorrupt = 0xC03A000A,

            /// <summary>
            ///     MessageId: StatusVhdInvalidBlockSize
            ///     MessageText:
            ///     The system does not support this version of the virtual hard disk. The block size is invalid.
            /// </summary>
            StatusVhdInvalidBlockSize = 0xC03A000B,

            /// <summary>
            ///     MessageId: StatusVhdBitmapMismatch
            ///     MessageText:
            ///     The virtual hard disk is corrupted. The block bitmap does not match with the block data present in the virtual hard
            ///     disk.
            /// </summary>
            StatusVhdBitmapMismatch = 0xC03A000C,

            /// <summary>
            ///     MessageId: StatusVhdParentVhdNotFound
            ///     MessageText:
            ///     The chain of virtual hard disks is broken. The system cannot locate the parent virtual hard disk for the
            ///     differencing disk.
            /// </summary>
            StatusVhdParentVhdNotFound = 0xC03A000D,

            /// <summary>
            ///     MessageId: StatusVhdChildParentIdMismatch
            ///     MessageText:
            ///     The chain of virtual hard disks is corrupted. There is a mismatch in the identifiers of the parent virtual hard
            ///     disk and differencing disk.
            /// </summary>
            StatusVhdChildParentIdMismatch = 0xC03A000E,

            /// <summary>
            ///     MessageId: StatusVhdChildParentTimestampMismatch
            ///     MessageText:
            ///     The chain of virtual hard disks is corrupted. The time stamp of the parent virtual hard disk does not match the
            ///     time stamp of the differencing disk.
            /// </summary>
            StatusVhdChildParentTimestampMismatch = 0xC03A000F,

            /// <summary>
            ///     MessageId: StatusVhdMetadataReadFailure
            ///     MessageText:
            ///     Failed to read the metadata of the virtual hard disk.
            /// </summary>
            StatusVhdMetadataReadFailure = 0xC03A0010,

            /// <summary>
            ///     MessageId: StatusVhdMetadataWriteFailure
            ///     MessageText:
            ///     Failed to write to the metadata of the virtual hard disk.
            /// </summary>
            StatusVhdMetadataWriteFailure = 0xC03A0011,

            /// <summary>
            ///     MessageId: StatusVhdInvalidSize
            ///     MessageText:
            ///     The size of the virtual hard disk is not valid.
            /// </summary>
            StatusVhdInvalidSize = 0xC03A0012,

            /// <summary>
            ///     MessageId: StatusVhdInvalidFileSize
            ///     MessageText:
            ///     The file size of this virtual hard disk is not valid.
            /// </summary>
            StatusVhdInvalidFileSize = 0xC03A0013,

            /// <summary>
            ///     MessageId: StatusVirtdiskProviderNotFound
            ///     MessageText:
            ///     A virtual disk support provider for the specified file was not found.
            /// </summary>
            StatusVirtdiskProviderNotFound = 0xC03A0014,

            /// <summary>
            ///     MessageId: StatusVirtdiskNotVirtualDisk
            ///     MessageText:
            ///     The specified disk is not a virtual disk.
            /// </summary>
            StatusVirtdiskNotVirtualDisk = 0xC03A0015,

            /// <summary>
            ///     MessageId: StatusVhdParentVhdAccessDenied
            ///     MessageText:
            ///     The chain of virtual hard disks is inaccessible. The process has not been granted access rights to the parent
            ///     virtual hard disk for the differencing disk.
            /// </summary>
            StatusVhdParentVhdAccessDenied = 0xC03A0016,

            /// <summary>
            ///     MessageId: StatusVhdChildParentSizeMismatch
            ///     MessageText:
            ///     The chain of virtual hard disks is corrupted. There is a mismatch in the virtual sizes of the parent virtual hard
            ///     disk and differencing disk.
            /// </summary>
            StatusVhdChildParentSizeMismatch = 0xC03A0017,

            /// <summary>
            ///     MessageId: StatusVhdDifferencingChainCycleDetected
            ///     MessageText:
            ///     The chain of virtual hard disks is corrupted. A differencing disk is indicated in its own parent chain.
            /// </summary>
            StatusVhdDifferencingChainCycleDetected = 0xC03A0018,

            /// <summary>
            ///     MessageId: StatusVhdDifferencingChainErrorInParent
            ///     MessageText:
            ///     The chain of virtual hard disks is inaccessible. There was an error opening a virtual hard disk further up the
            ///     chain.
            /// </summary>
            StatusVhdDifferencingChainErrorInParent = 0xC03A0019,

            /// <summary>
            ///     MessageId: StatusVirtualDiskLimitation
            ///     MessageText:
            ///     The requested operation could not be completed due to a virtual disk system limitation.  Virtual disks are only
            ///     supported on Ntfs volumes and must be both uncompressed and unencrypted.
            /// </summary>
            StatusVirtualDiskLimitation = 0xC03A001A,

            /// <summary>
            ///     MessageId: StatusVhdInvalidType
            ///     MessageText:
            ///     The requested operation cannot be performed on a virtual disk of this type.
            /// </summary>
            StatusVhdInvalidType = 0xC03A001B,

            /// <summary>
            ///     MessageId: StatusVhdInvalidState
            ///     MessageText:
            ///     The requested operation cannot be performed on the virtual disk in its current state.
            /// </summary>
            StatusVhdInvalidState = 0xC03A001C,

            /// <summary>
            ///     MessageId: StatusVirtdiskUnsupportedDiskSectorSize
            ///     MessageText:
            ///     The sector size of the physical disk on which the virtual disk resides is not supported.
            /// </summary>
            StatusVirtdiskUnsupportedDiskSectorSize = 0xC03A001D,

            // Vhd warnings.

            /// <summary>
            ///     MessageId: StatusQueryStorageError
            ///     MessageText:
            ///     The virtualization storage subsystem has generated an error.
            /// </summary>
            StatusQueryStorageError = 0x803A0001,

            // Derived Indexed Store (Dis, error messages.

            /// <summary>
            ///     MessageId: StatusDisNotPresent
            ///     MessageText:
            ///     The Derived Indexed Store is not present (or currently loaded, on this system.
            /// </summary>
            StatusDisNotPresent = 0xC03C0001,

            /// <summary>
            ///     MessageId: StatusDisAttributeNotFound
            ///     MessageText:
            ///     The Attribute was not found in the store for a given object.
            /// </summary>
            StatusDisAttributeNotFound = 0xC03C0002,

            /// <summary>
            ///     MessageId: StatusDisUnrecognizedAttribute
            ///     MessageText:
            ///     This is not a recognized built-in attribute.
            /// </summary>
            StatusDisUnrecognizedAttribute = 0xC03C0003,

            /// <summary>
            ///     MessageId: StatusDisPartialData
            ///     MessageText:
            ///     Partial data was successfully returned, some attributes need to be calculated from elsewhere.
            /// </summary>
            StatusDisPartialData = 0xC03C0004
        }

        // END copy //depot/winmain/sdktools/DevicePath/Interop
        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public readonly struct BY_HANDLE_FILE_INFORMATION
        {
            private readonly uint FileAttributes;
            private readonly System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
            private readonly System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
            private readonly System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
            private readonly uint VolumeSerialNumber;
            private readonly uint FileSizeHigh;
            private readonly uint FileSizeLow;
            public readonly uint NumberOfLinks;
            public readonly uint FileIndexHigh;
            public readonly uint FileIndexLow;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetFileInformationByHandle(
            SafeFileHandle hFile,
            out BY_HANDLE_FILE_INFORMATION lpFileInformation);
    }

    // ReSharper restore UnusedMember.Global
    // ReSharper restore InconsistentNaming
}
