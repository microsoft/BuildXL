// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using BuildXL.Native.Streams;
using BuildXL.Native.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
using Microsoft.Win32.SafeHandles;
using static BuildXL.Native.IO.FileUtilities;
using static BuildXL.Utilities.FormattableStringEx;
using Overlapped = BuildXL.Native.Streams.Overlapped;

#pragma warning disable 1591   // disabling warning about missing API documentation; TODO: Remove this line and write documentation!
#pragma warning disable CA1823 // Unused field
#pragma warning disable SA1203 // Constant fields must appear before non-constant fields
#pragma warning disable SA1139 // Use literal suffix notation instead of casting

namespace BuildXL.Native.IO.Windows
{
    /// <summary>
    /// FileSystem related native implementations for Windows based systems
    /// </summary>
    public sealed class FileSystemWin : IFileSystem
    {
        #region Constants

        /// <summary>
        /// Long path prefix.
        /// </summary>
        public const string LongPathPrefix = @"\\?\";

        /// <summary>
        /// Long UNC path prefix.
        /// </summary>
        public const string LongUNCPathPrefix = @"\\?\UNC\";

        /// <summary>
        /// NT path prefix.
        /// </summary>
        public const string NtPathPrefix = @"\??\";

        /// <summary>
        /// Local device prefix.
        /// </summary>
        public const string LocalDevicePrefix = @"\\.\";

        private const int DefaultBufferSize = 4096;

        #endregion

        #region PInvoke and structs

        /// <summary>
        /// A value representing INVALID_HANDLE_VALUE.
        /// </summary>
        private static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        /// <summary>
        /// OSVERSIONINFOEX
        /// See http://msdn.microsoft.com/en-us/library/windows/desktop/ms724833(v=vs.85).aspx
        /// </summary>
        /// <remarks>
        /// This definition is taken with minor modifications from the BCL.
        /// </remarks>
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private sealed class OsVersionInfoEx
        {
            public static readonly int Size = Marshal.SizeOf<OsVersionInfoEx>();

            public OsVersionInfoEx()
            {
                // This must be set to Size before use, since it is validated by consumers such as VerifyVersionInfo.
                OSVersionInfoSize = Size;
            }

            public int OSVersionInfoSize;
            public int MajorVersion;
            public int MinorVersion;
            public int BuildNumber;
            public int PlatformId;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string CSDVersion;
            public ushort ServicePackMajor;
            public ushort ServicePackMinor;
            public short SuiteMask;
            public byte ProductType;
            public byte Reserved;
        }

        /// <summary>
        /// Request structure indicating this program's supported version range of Usn records.
        /// See http://msdn.microsoft.com/en-us/library/windows/desktop/hh802705(v=vs.85).aspx
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct ReadFileUsnData
        {
            /// <summary>
            /// Size of this structure (there are no variable length fields).
            /// </summary>
            public static readonly int Size = Marshal.SizeOf<ReadFileUsnData>();

            /// <summary>
            /// Indicates that FSCTL_READ_FILE_USN_DATA should return either V2 or V3 records (those with NTFS or ReFS-sized file IDs respectively).
            /// </summary>
            /// <remarks>
            /// This request should work on Windows 8 / Server 2012 and above.
            /// </remarks>
            public static readonly ReadFileUsnData NtfsAndReFSCompatible = new ReadFileUsnData()
            {
                MinMajorVersion = 2,
                MaxMajorVersion = 3,
            };

            /// <summary>
            /// Indicates that FSCTL_READ_FILE_USN_DATA should return only V2 records (those with NTFS file IDs, even if using ReFS).
            /// </summary>
            /// <remarks>
            /// This request should work on Windows 8 / Server 2012 and above.
            /// </remarks>
            public static readonly ReadFileUsnData NtfsCompatible = new ReadFileUsnData()
            {
                MinMajorVersion = 2,
                MaxMajorVersion = 2,
            };

            public ushort MinMajorVersion;
            public ushort MaxMajorVersion;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FileRenameInfo
        {
            public byte ReplaceIfExists;
            public IntPtr RootDirectory;

            /// <summary>
            /// Length of the string starting at <see cref="FileName"/> in *bytes* (not characters).
            /// </summary>
            public int FileNameLengthInBytes;

            /// <summary>
            /// First character of filename; this is a variable length array as determined by FileNameLength.
            /// </summary>
            public readonly char FileName;
        }

        /// <summary>
        /// Union tag for <see cref="FileIdDescriptor"/>.
        /// </summary>
        /// <remarks>
        /// http://msdn.microsoft.com/en-us/library/windows/desktop/aa364227(v=vs.85).aspx
        /// </remarks>
        private enum FileIdDescriptorType
        {
            FileId = 0,

            // ObjectId = 1, - Not supported
            ExtendedFileId = 2,
        }

        /// <summary>
        /// Structure to specify a file ID to <see cref="OpenFileById"/>.
        /// </summary>
        /// <remarks>
        /// On the native side, the ID field is a union of a 64-bit file ID, a 128-bit file ID,
        /// and an object ID (GUID). Since we only pass this in to <see cref="OpenFileById"/>
        /// we simply specify the ID part to C# as a 128-bit file ID and ensure that the high bytes are
        /// empty when we are specifying a 64-bit ID.
        /// Note that since downlevel the union members are a GUID and a 64-bit file ID (extended file ID unsupported),
        /// the structure size is fortunately same in all cases (because the object ID GUID is 16 bytes / 128-bits).
        /// See http://msdn.microsoft.com/en-us/library/windows/desktop/aa364227(v=vs.85).aspx
        /// </remarks>
        [StructLayout(LayoutKind.Sequential)]
        private readonly struct FileIdDescriptor
        {
            private static readonly int s_size = Marshal.SizeOf<FileIdDescriptor>();

            public readonly int Size;
            public readonly FileIdDescriptorType Type;
            public readonly FileId ExtendedFileId;

            public FileIdDescriptor(FileId fileId)
            {
                if (IsExtendedFileIdSupported())
                {
                    Type = FileIdDescriptorType.ExtendedFileId;
                }
                else
                {
                    Contract.Assume(fileId.High == 0, "File ID should not have high bytes when extended IDs are not supported on the underlying OS");
                    Type = FileIdDescriptorType.FileId;
                }

                Size = s_size;
                ExtendedFileId = fileId;
            }
        }

        /// <summary>
        /// Header data in common between USN_RECORD_V2 and USN_RECORD_V3. These fields are needed to determine how to interpret a returned record.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private readonly struct NativeUsnRecordHeader
        {
            /// <summary>
            /// Size of the record header in bytes.
            /// </summary>
            public static readonly int Size = Marshal.SizeOf<NativeUsnRecordHeader>();

            public readonly int RecordLength;
            public readonly ushort MajorVersion;
            public readonly ushort MinorVersion;
        }

        /// <summary>
        /// USN_RECORD_V3
        /// See http://msdn.microsoft.com/en-us/library/windows/desktop/hh802708(v=vs.85).aspx
        /// </summary>
        /// <remarks>
        /// The Size is explicitly set to the actual used size + the needing padding to 8-byte alignment
        /// (for Usn, Timestamp, etc.). Two of those padding bytes are actually the first character of the filename.
        /// </remarks>
        [StructLayout(LayoutKind.Sequential, Size = 0x50)]
        private readonly struct NativeUsnRecordV3
        {
            /// <summary>
            /// Size of a record with two filename characters (starting at WCHAR FileName[1]; not modeled in the C# struct),
            /// or one filename character and two bytes of then-needed padding (zero-length filenames are disallowed).
            /// This is the minimum size that should ever be returned.
            /// </summary>
            public static readonly int MinimumSize = Marshal.SizeOf<NativeUsnRecordV3>();

            /// <summary>
            /// Maximum size of a single V3 record, assuming the NTFS / ReFS 255 character file name length limit.
            /// </summary>
            /// <remarks>
            /// ( (MaximumComponentLength - 1) * sizeof(WCHAR) + sizeof(USN_RECORD_V3)
            /// See http://msdn.microsoft.com/en-us/library/windows/desktop/hh802708(v=vs.85).aspx
            /// Due to padding this is perhaps an overestimate.
            /// </remarks>
            public static readonly int MaximumSize = MinimumSize + (254 * 2);

            public readonly NativeUsnRecordHeader Header;
            public readonly FileId FileReferenceNumber;
            public readonly FileId ParentFileReferenceNumber;
            public readonly Usn Usn;
            public readonly long TimeStamp;
            public readonly uint Reason;
            public readonly uint SourceInfo;
            public readonly uint SecurityId;
            public readonly uint FileAttributes;
            public readonly ushort FileNameLength;
            public readonly ushort FileNameOffset;

            // WCHAR FileName[1];
        }

        /// <summary>
        /// TODO: this is not documented by WDG yet.
        /// TODO: OpenSource
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct FileDispositionInfoEx
        {
            public FileDispositionFlags Flags;
        }

        /// <summary>
        /// TODO: this is not properly documented by WDG yet.
        /// TODO: OpenSource
        /// </summary>
        [Flags]
        private enum FileDispositionFlags : uint
        {
#pragma warning disable CA1008 // Enums should have zero value
            DoNotDelete = 0x00000000,
#pragma warning restore CA1008 // Enums should have zero value
            Delete = 0x00000001,

            /// <summary>
            /// NTFS default behavior on link removal is when the last handle is closed on that link, the link is physically gone.
            /// The link is marked for deletion when the FILE_FLAG_DELETE_ON_CLOSE is specified on open or FileDispositionInfo is called.
            /// Although, the link is marked as deleted until the last handle on that link is closed,
            /// it can not be re-purposed as it physically exists.
            /// This is also true for superseded rename case where the target cannot be deleted if other handles are opened on that link.
            /// This makes Windows distinct in nature than how Linux works handling the links where the link name is freed
            /// and can be re-purposed as soon as you deleted/rename the link by closing the handle that requested the delete/rename
            /// regardless of other handles are opened on that link.
            /// FileDispositionInfoEx and FileRenameInfoEx implement the POSIX style delete/rename behavior.
            /// For POSIX style superseded rename, the target needs to be opened with FILE_SHARE_DELETE access by other openers.
            /// </summary>
            PosixSemantics = 0x00000002,
            ForceImageSectionCheck = 0x00000004,
            OnClose = 0x00000008,
        }

        /// <summary>
        /// USN_RECORD_V2
        /// See http://msdn.microsoft.com/en-us/library/windows/desktop/aa365722(v=vs.85).aspx
        /// </summary>
        /// <remarks>
        /// The Size is explicitly set to the actual used size + the needing padding to 8-byte alignment
        /// (for Usn, Timestamp, etc.). Two of those padding bytes are actually the first character of the filename.
        /// </remarks>
        [StructLayout(LayoutKind.Sequential, Size = 0x40)]
        private struct NativeUsnRecordV2
        {
            /// <summary>
            /// Size of a record with two filename characters (starting at WCHAR FileName[1]; not modeled in the C# struct),
            /// or one filename character and two bytes of then-needed padding (zero-length filenames are disallowed).
            /// This is the minimum size that should ever be returned.
            /// </summary>
            public static readonly int MinimumSize = Marshal.SizeOf<NativeUsnRecordV2>();

            /// <summary>
            /// Maximum size of a single V2 record, assuming the NTFS / ReFS 255 character file name length limit.
            /// </summary>
            /// <remarks>
            /// ( (MaximumComponentLength - 1) * sizeof(WCHAR) + sizeof(USN_RECORD_V2)
            /// See http://msdn.microsoft.com/en-us/library/windows/desktop/aa365722(v=vs.85).aspx
            /// Due to padding this is perhaps an overestimate.
            /// </remarks>
            public static readonly int MaximumSize = MinimumSize + (254 * 2);

            public readonly NativeUsnRecordHeader Header;
            public readonly ulong FileReferenceNumber;
            public readonly ulong ParentFileReferenceNumber;
            public readonly Usn Usn;
            public readonly long TimeStamp;
            public readonly uint Reason;
            public readonly uint SourceInfo;
            public readonly uint SecurityId;
            public readonly uint FileAttributes;
            public readonly ushort FileNameLength;
            public readonly ushort FileNameOffset;

            // WCHAR FileName[1];
        }

        /// <summary>
        /// FILE_INFO_BY_HANDLE_CLASS for GetFileInformationByHandleEx.
        /// See http://msdn.microsoft.com/en-us/library/windows/desktop/aa364953(v=vs.85).aspx
        /// </summary>
        private enum FileInfoByHandleClass : uint
        {
            FileBasicInfo = 0x0,
            FileStandardInfo = 0x1,
            FileNameInfo = 0x2,
            FileRenameInfo = 0x3,
            FileDispositionInfo = 0x4,
            FileAllocationInfo = 0x5,
            FileEndOfFileInfo = 0x6,
            FileStreamInfo = 0x7,
            FileCompressionInfo = 0x8,
            FileAttributeTagInfo = 0x9,
            FileIdBothDirectoryInfo = 0xa,
            FileIdBothDirectoryRestartInfo = 0xb,
            FileRemoteProtocolInfo = 0xd,
            FileFullDirectoryInfo = 0xe,
            FileFullDirectoryRestartInfo = 0xf,
            FileStorageInfo = 0x10,
            FileAlignmentInfo = 0x11,
            FileIdInfo = 0x12,
            FileIdExtdDirectoryInfo = 0x13,
            FileIdExtdDirectoryRestartInfo = 0x14,
            FileDispositionInfoEx = 0x15,
            FileRenameInfoEx = 0x16,
        }

        /// <summary>
        /// Whether the hresult status is one that should be treated as a nonexistent file
        /// </summary>
        /// <remarks>
        /// This must be in sync with the code in static bool IsPathNonexistent(DWORD error) function on the Detours side in FileAccessHelper.cpp.
        ///
        /// Also keep this in sync with <see cref="OpenFileStatusExtensions.IsNonexistent(OpenFileStatus)"/>
        /// NotReadyDevice is treated as non-existent probe.
        /// BitLocker locked volume is treated as non-existent probe.
        /// </remarks>
        public static bool IsHresultNonesixtent(int hr)
        {
            return hr == NativeIOConstants.ErrorFileNotFound
                || hr == NativeIOConstants.ErrorPathNotFound
                || hr == NativeIOConstants.ErrorNotReady
                || hr == NativeIOConstants.FveLockedVolume
                || hr == NativeIOConstants.ErrorCantAccessFile;
        }

        /// <summary>
        /// <c>FILE_BASIC_INFO</c>
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
        public struct FileBasicInfo
        {
            /// <summary>
            /// UTC FILETIME of the file's creation.
            /// </summary>
            public ulong CreationTime;

            /// <summary>
            /// UTC FILETIME of the last access to the file.
            /// </summary>
            public ulong LastAccessTime;

            /// <summary>
            /// UTC FILETIME of the last write to the file.
            /// </summary>
            public ulong LastWriteTime;

            /// <summary>
            /// UTC FILETIME of the last change to the file (e.g. attribute change or a write)
            /// </summary>
            public ulong ChangeTime;

            /// <summary>
            /// File attributes
            /// </summary>
            public FileAttributes Attributes;
        }

        /// <summary>
        /// <c>FILE_STANDARD_INFO</c>
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
        public struct FileStandardInfo
        {
            /// <summary>
            /// The amount of space that is allocated for the file.
            /// </summary>
            public ulong AllocationSize;

            /// <summary>
            /// The end of the file.
            /// </summary>
            public ulong EndOfFile;

            /// <summary>
            /// The number of links to the file.
            /// </summary>
            public uint NumberOfLinks;

            /// <summary>
            /// TRUE if the file in the delete queue; otherwise, false.
            /// </summary>
            public bool DeletePending;

            /// <summary>
            /// TRUE if the file is a directory; otherwise, false.
            /// </summary>
            public bool Directory;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern SafeFileHandle CreateFileW(
            string lpFileName,
            FileDesiredAccess dwDesiredAccess,
            FileShare dwShareMode,
            IntPtr lpSecurityAttributes,
            FileMode dwCreationDisposition,
            FileFlagsAndAttributes dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern SafeFileHandle ReOpenFile(
            SafeFileHandle hOriginalFile,
            FileDesiredAccess dwDesiredAccess,
            FileShare dwShareMode,
            FileFlagsAndAttributes dwFlagsAndAttributes);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, BestFitMapping = false)]
        internal static extern bool CreateDirectoryW(string path, IntPtr lpSecurityAttributes);


        [Flags]
        private enum FlushFileBuffersFlags : uint
        {
            /// <summary>
            /// Corresponds to <c>FLUSH_FLAGS_FILE_DATA_ONLY</c>.
            /// If set, this operation will write the data for the given file from the
            /// Windows in-memory cache.  This will NOT commit any associated metadata
            /// changes.  This will NOT send a SYNC to the storage device to flush its
            /// cache.  Not supported on volume handles.  Only supported by the NTFS
            /// filesystem.
            /// </summary>
            FileDataOnly = 0x00000001,

            /// <summary>
            /// Corresponds to <c>FLUSH_FLAGS_NO_SYNC</c>.
            /// If set, this operation will commit both the data and metadata changes for
            /// the given file from the Windows in-memory cache.  This will NOT send a SYNC
            /// to the storage device to flush its cache.  Not supported on volume handles.
            /// Only supported by the NTFS filesystem.
            /// </summary>
            NoSync = 0x00000002,
        }

        /// <summary>
        /// Lower-level file-flush facility, like <c>FlushFileBuffers</c>. Allows cache-only flushes without sending an expensive 'sync' command to the underlying disk.
        /// See https://msdn.microsoft.com/en-us/library/windows/hardware/hh967720(v=vs.85).aspx
        /// </summary>
        [DllImport("ntdll.dll", SetLastError = false, CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern unsafe NtStatus NtFlushBuffersFileEx(
            SafeFileHandle handle,
            FlushFileBuffersFlags mode,
            void* parameters,
            int parametersSize,
            IoStatusBlock* ioStatusBlock);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern SafeFileHandle OpenFileById(
            SafeFileHandle hFile, // Any handle on the relevant volume
            [In] FileIdDescriptor lpFileId,
            FileDesiredAccess dwDesiredAccess,
            FileShare dwShareMode,
            IntPtr lpSecurityAttributes,
            FileFlagsAndAttributes dwFlagsAndAttributes);

        /// <summary>
        /// Creates an I/O completion port or associates an existing port with a file handle.
        /// </summary>
        /// <remarks>
        /// http://msdn.microsoft.com/en-us/library/windows/desktop/aa363862(v=vs.85).aspx
        /// We marshal the result as an IntPtr since, given an <paramref name="existingCompletionPort"/>,
        /// we get back the same handle value. Wrapping the same handle value again would result in double-frees on finalize.
        /// </remarks>
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateIoCompletionPort(
            SafeFileHandle handle,
            SafeIOCompletionPortHandle existingCompletionPort,
            IntPtr completionKey,
            int numberOfConcurrentThreads);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern unsafe bool GetOverlappedResult(
            SafeFileHandle hFile,
            Overlapped* lpOverlapped,
            int* lpNumberOfBytesTransferred,
            [MarshalAs(UnmanagedType.Bool)] bool bWait);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        [SuppressMessage("Microsoft.Interoperability", "CA1415:DeclarePInvokesCorrectly", Justification = "Overlapped intentionally redefined.")]
        private static extern unsafe bool GetQueuedCompletionStatus(
            SafeIOCompletionPortHandle hCompletionPort,
            int* lpNumberOfBytes,
            IntPtr* lpCompletionKey,
            Overlapped** lpOverlapped,
            int dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        [SuppressMessage("Microsoft.Interoperability", "CA1415:DeclarePInvokesCorrectly", Justification = "Overlapped intentionally redefined.")]
        private static extern unsafe bool PostQueuedCompletionStatus(
            SafeIOCompletionPortHandle hCompletionPort,
            int dwNumberOfBytesTransferred,
            IntPtr dwCompletionKey,
            Overlapped* lpOverlapped);

        [Flags]
        private enum FileCompletionMode
        {
            FileSkipCompletionPortOnSuccess = 0x1,
            FileSkipSetEventOnHandle = 0x2,
        }

        /// <summary>
        /// Sets the mode for dispatching IO completions on the given file handle.
        /// </summary>
        /// <remarks>
        /// Skipping completion port queueing on success (i.e., synchronous completion) avoids wasted thread handoffs but requires an aware caller
        /// (that does not assume <c>ERROR_IO_PENDING</c>).
        /// Skipping the signaling of the file object itself via <see cref="FileCompletionMode.FileSkipSetEventOnHandle"/> can avoid some
        /// wasted work and locking in the event there's not a specific event provided in the corresponding <c>OVERLAPPED</c> structure.
        /// See http://blogs.technet.com/b/winserverperformance/archive/2008/06/26/designing-applications-for-high-performance-part-iii.aspx
        /// </remarks>
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetFileCompletionNotificationModes(SafeFileHandle handle, FileCompletionMode mode);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        [SuppressMessage("Microsoft.Interoperability", "CA1415:DeclarePInvokesCorrectly", Justification = "Overlapped intentionally redefined.")]
        private static extern unsafe bool ReadFile(
            SafeFileHandle hFile,
            byte* lpBuffer,
            int nNumberOfBytesToRead,
            int* lpNumberOfBytesRead,
            Overlapped* lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        [SuppressMessage("Microsoft.Interoperability", "CA1415:DeclarePInvokesCorrectly", Justification = "Overlapped intentionally redefined.")]
        private static extern unsafe bool WriteFile(
            SafeFileHandle hFile,
            byte* lpBuffer,
            int nNumberOfBytesToWrite,
            int* lpNumberOfBytesWritten,
            Overlapped* lpOverlapped);

        [SuppressMessage("Microsoft.Globalization", "CA2101:SpecifyMarshalingForPInvokeStringArguments", MessageId = "OsVersionInfoEx.CSDVersion",
            Justification = "This appears impossible to satisfy.")]
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, BestFitMapping = false, ThrowOnUnmappableChar = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool VerifyVersionInfo(
            [In] OsVersionInfoEx versionInfo,
            uint typeMask,
            ulong conditionMask);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern ulong VerSetConditionMask(
            ulong existingMask,
            uint typeMask,
            byte conditionMask);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeviceIoControl(
            SafeFileHandle deviceHandle,
            uint ioControlCode,
            IntPtr inputBuffer,
            int inputBufferSize,
            IntPtr outputBuffer,
            int outputBufferSize,
            out int bytesReturned,
            IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeviceIoControl(
            SafeFileHandle deviceHandle,
            uint ioControlCode,
            IntPtr inputBuffer,
            int inputBufferSize,
            [Out] QueryUsnJournalData outputBuffer,
            int outputBufferSize,
            out int bytesReturned,
            IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            uint ioControlCode,
            ref STORAGE_PROPERTY_QUERY inputBuffer,
            int inputBufferSize,
            out DEVICE_SEEK_PENALTY_DESCRIPTOR outputBuffer,
            int outputBufferSize,
            out uint bytesReturned,
            IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileInformationByHandleEx(
            SafeFileHandle deviceHandle,
            uint fileInformationClass,
            IntPtr outputFileInformationBuffer,
            int outputBufferSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetFileInformationByHandle(
              SafeFileHandle hFile,
              uint fileInformationClass,
              IntPtr lpFileInformation,
              int bufferSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetFileSizeEx(
            SafeFileHandle handle,
            out long size);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetVolumeInformationByHandleW(
            SafeFileHandle fileHandle,
            [Out] StringBuilder volumeNameBuffer, // Buffer for volume name (if not null)
            int volumeNameBufferSize,
            IntPtr volumeSerial, // Optional pointer to a DWORD to be populated with the volume serial number
            IntPtr maximumComponentLength, // Optional pointer to a DWORD to be populated with the max component length.
            IntPtr fileSystemFlags, // Optional pointer to a DWORD to be populated with flags of supported features on the volume (e.g. hardlinks)
            [Out] StringBuilder fileSystemNameBuffer, // Buffer for volume FS, e.g. "NTFS" (if not null)
            int fileSystemNameBufferSize);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern SafeFindVolumeHandle FindFirstVolumeW(
            [Out] StringBuilder volumeNameBuffer,
            int volumeNameBufferLength);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FindNextVolumeW(
            SafeFindVolumeHandle findVolumeHandle,
            [Out] StringBuilder volumeNameBuffer,
            int volumeNameBufferLength);

        /// <summary>
        /// Disposes a <see cref="SafeFindVolumeHandle"/>
        /// </summary>
        /// <remarks>
        /// Since this is used by <see cref="SafeFindVolumeHandle"/> itself, we expose
        /// the inner <see cref="IntPtr"/> (rather than trying to marshal the handle wrapper
        /// from within its own release method).
        /// </remarks>
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool FindVolumeClose(IntPtr findVolumeHandle);

        /// <summary>
        /// Disposes a typical handle.
        /// </summary>
        /// <remarks>
        /// Since this is used by safe handle wrappers (e.g. <see cref="SafeIOCompletionPortHandle"/>), we expose
        /// the inner <see cref="IntPtr"/> (rather than trying to marshal the handle wrapper
        /// from within its own release method).
        /// </remarks>
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CreateHardLinkW(string lpFileName, string lpExistingFileName, IntPtr reservedSecurityAttributes);

        /// <summary>
        /// Symbolic link target.
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1008:EnumsShouldHaveZeroValue")]
        [SuppressMessage("Microsoft.Naming", "CA1714:FlagsEnumsShouldHavePluralNames")]
        [Flags]
        public enum SymbolicLinkTarget : uint
        {
            /// <summary>
            /// The link target is a file.
            /// </summary>
            File = 0x0,

            /// <summary>
            /// The link target is a directory.
            /// </summary>
            Directory = 0x1,

            /// <summary>
            /// Specify this flag to allow creation of symbolic links when the process is not elevated. 
            /// </summary>
            AllowUnprivilegedCreate = 0x2
        }

        /// <summary>
        /// WinAPI for creating symlinks.
        /// Although the documentation says "If the function succeeds, the return value is nonzero",
        /// it's not entirely true --- if the call succeeds, the return value is non-negative!
        /// </summary>
        /// <remarks>
        /// For the reason stated above, we cannot MarshalAs boolean because all negative values would be converted to 'true'.
        /// SYMBOLIC_LINK_FLAG_ALLOW_UNPRIVILEGED_CREATE mentioned in the doc does not really do what the doc says it should do:
        /// it allows symlinks to be created from non-elevated process ONLY if a process is run under Windows 10 (14972) AND
        /// a user enabled Developer Mode. If any of these conditions is not met - the flag is simply ignored.
        /// https://docs.microsoft.com/en-us/windows/desktop/api/winbase/nf-winbase-createsymboliclinkw
        /// </remarks>
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        internal static extern int CreateSymbolicLinkW(string lpSymlinkFileName, string lpTargetFileName, SymbolicLinkTarget dwFlags);

        /// <summary>
        /// When this flag is set on the process or thread error mode, 'the system does not display the critical-error-handler message box'.
        /// In this context, we don't want a weird message box prompting to insert a CD / floppy when querying volume information.
        /// </summary>
        /// <remarks>
        /// Seriously?!
        /// Corresponds to SEM_FAILCRITICALERRORS
        /// </remarks>
        private const int SemFailCriticalErrors = 1;

        /// <os>Windows 7+</os>
        [DllImport("kernel32.dll", SetLastError = false)]
        private static extern int GetThreadErrorMode();

        /// <os>Windows 7+</os>
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetThreadErrorMode(int newErrorMode, out int oldErrorMode);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern int GetFinalPathNameByHandleW(SafeFileHandle hFile, [Out] StringBuilder filePathBuffer, int filePathBufferSize, int flags);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool RemoveDirectoryW(
            string lpPathName);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [SuppressMessage("Microsoft.Interoperability", "CA1401:PInvokesShouldNotBeVisible", Justification = "Needed for custom enumeration.")]
        public static extern SafeFindFileHandle FindFirstFileW(
            string lpFileName,
            out WIN32_FIND_DATA lpFindFileData);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        [SuppressMessage("Microsoft.Interoperability", "CA1401:PInvokesShouldNotBeVisible", Justification = "Needed for custom enumeration.")]
        public static extern bool FindNextFileW(SafeHandle hFindFile, out WIN32_FIND_DATA lpFindFileData);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool FindClose(IntPtr findFileHandle);

        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        [SuppressMessage("Microsoft.Interoperability", "CA1401:PInvokesShouldNotBeVisible", Justification = "Needed for creating symlinks.")]
        public static extern bool PathMatchSpecW([In] string pszFileParam, [In] string pszSpec);

        /// <summary>
        /// Values for the DwReserved0 member of the WIN32_FIND_DATA struct.
        /// </summary>
        public enum DwReserved0Flag : uint
        {
            [SuppressMessage("Microsoft.Naming", "CA1700:DoNotNameEnumValuesReserved")]
            [SuppressMessage("Microsoft.Naming", "CA1707:RemoveUnderscoresFromMemberName")]
            IO_REPARSE_TAG_RESERVED_ZERO = 0x00000000, // Reserved reparse tag value.
            [SuppressMessage("Microsoft.Naming", "CA1700:DoNotNameEnumValuesReserved")]
            [SuppressMessage("Microsoft.Naming", "CA1707:RemoveUnderscoresFromMemberName")]
            IO_REPARSE_TAG_RESERVED_ONE = 0x00000001, // Reserved reparse tag value.
            [SuppressMessage("Microsoft.Naming", "CA1700:DoNotNameEnumValuesReserved")]
            [SuppressMessage("Microsoft.Naming", "CA1707:RemoveUnderscoresFromMemberName")]
            IO_REPARSE_TAG_MOUNT_POINT = 0xA0000003, // Used for mount point support, specified in section 2.1.2.5.
            [SuppressMessage("Microsoft.Naming", "CA1700:DoNotNameEnumValuesReserved")]
            [SuppressMessage("Microsoft.Naming", "CA1707:RemoveUnderscoresFromMemberName")]
            IO_REPARSE_TAG_HSM = 0xC0000004, // Obsolete.Used by legacy Hierarchical Storage Manager Product.
            [SuppressMessage("Microsoft.Naming", "CA1700:DoNotNameEnumValuesReserved")]
            [SuppressMessage("Microsoft.Naming", "CA1707:RemoveUnderscoresFromMemberName")]
            IO_REPARSE_TAG_HSM2 = 0x80000006, // Obsolete.Used by legacy Hierarchical Storage Manager Product.
            [SuppressMessage("Microsoft.Naming", "CA1700:DoNotNameEnumValuesReserved")]
            [SuppressMessage("Microsoft.Naming", "CA1707:RemoveUnderscoresFromMemberName")]
            IO_REPARSE_TAG_DRIVER_EXTENDER = 0x80000005, // Home server drive extender.<3>
            [SuppressMessage("Microsoft.Naming", "CA1700:DoNotNameEnumValuesReserved")]
            [SuppressMessage("Microsoft.Naming", "CA1707:RemoveUnderscoresFromMemberName")]
            IO_REPARSE_TAG_SIS = 0x80000007, // Used by single-instance storage (SIS) filter driver.Server-side interpretation only, not meaningful over the wire.
            [SuppressMessage("Microsoft.Naming", "CA1700:DoNotNameEnumValuesReserved")]
            [SuppressMessage("Microsoft.Naming", "CA1707:RemoveUnderscoresFromMemberName")]
            IO_REPARSE_TAG_DFS = 0x8000000A, // Used by the DFS filter.The DFS is described in the Distributed File System (DFS): Referral Protocol Specification[MS - DFSC]. Server-side interpretation only, not meaningful over the wire.
            [SuppressMessage("Microsoft.Naming", "CA1700:DoNotNameEnumValuesReserved")]
            [SuppressMessage("Microsoft.Naming", "CA1707:RemoveUnderscoresFromMemberName")]
            IO_REPARSE_TAG_DFSR = 0x80000012, // Used by the DFS filter.The DFS is described in [MS-DFSC]. Server-side interpretation only, not meaningful over the wire.
            [SuppressMessage("Microsoft.Naming", "CA1700:DoNotNameEnumValuesReserved")]
            [SuppressMessage("Microsoft.Naming", "CA1707:RemoveUnderscoresFromMemberName")]
            IO_REPARSE_TAG_FILTER_MANAGER = 0x8000000B, // Used by filter manager test harness.<4>
            [SuppressMessage("Microsoft.Naming", "CA1700:DoNotNameEnumValuesReserved")]
            [SuppressMessage("Microsoft.Naming", "CA1707:RemoveUnderscoresFromMemberName")]
            IO_REPARSE_TAG_SYMLINK = 0xA000000C, // Used for symbolic link support. See section 2.1.2.4.
            [SuppressMessage("Microsoft.Naming", "CA1700:DoNotNameEnumValuesReserved")]
            [SuppressMessage("Microsoft.Naming", "CA1707:RemoveUnderscoresFromMemberName")]
            IO_REPARSE_TAG_WCIFS = 0x80000018, // The tag for a WCI reparse point
            [SuppressMessage("Microsoft.Naming", "CA1700:DoNotNameEnumValuesReserved")]
            [SuppressMessage("Microsoft.Naming", "CA1707:RemoveUnderscoresFromMemberName")]
            IO_REPARSE_TAG_WCIFS_TOMBSTONE = 0xA000001F, // The tag for a WCI tombstone file
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        [SuppressMessage("Microsoft.Usage", "CA2205:UseManagedEquivalentsOfWin32Api",
            Justification = "We explicitly need to call the native SetFileAttributes as the managed one does not support long paths.")]
        internal static extern bool SetFileAttributesW(
            string lpFileName,
            FileAttributes dwFileAttributes);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.U4)]
        [SuppressMessage("Microsoft.Usage", "CA2205:UseManagedEquivalentsOfWin32Api",
            Justification = "We explicitly need to call the native GetFileAttributes as the managed one does not support long paths.")]
        internal static extern uint GetFileAttributesW(
            string lpFileName);

        /// <summary>
        /// Storage property query
        /// https://msdn.microsoft.com/en-us/library/windows/desktop/ff800840(v=vs.85).aspx
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct STORAGE_PROPERTY_QUERY
        {
            public uint PropertyId;
            public uint QueryType;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
            public byte[] AdditionalParameters;
        }

        private const uint StorageDeviceSeekPenaltyProperty = 7;
        private const uint PropertyStandardQuery = 0;

        /// <summary>
        /// Specifies whether a device has a seek penalty.
        /// https://msdn.microsoft.com/en-us/library/ff552549.aspx
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct DEVICE_SEEK_PENALTY_DESCRIPTOR
        {
            public readonly uint Version;
            public readonly uint Size;
            [MarshalAs(UnmanagedType.U1)]
            public readonly bool IncursSeekPenalty;
        }

        // Consts from sdk\inc\winioctl.h
        private const uint METHOD_BUFFERED = 0;
        private const uint FILE_ANY_ACCESS = 0;
        private const uint FILE_DEVICE_MASS_STORAGE = 0x0000002d;
        private const uint IOCTL_STORAGE_BASE = FILE_DEVICE_MASS_STORAGE;
        private static readonly uint IOCTL_STORAGE_QUERY_PROPERTY = CTL_CODE(IOCTL_STORAGE_BASE, 0x500, METHOD_BUFFERED, FILE_ANY_ACCESS);

        private static uint CTL_CODE(uint deviceType, uint function, uint method, uint access)
        {
            return (deviceType << 16) | (access << 14) | (function << 2) | method;
        }

        /// <summary>
        /// Reparse data buffer - from ntifs.h.
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct REPARSE_DATA_BUFFER
        {
            public DwReserved0Flag ReparseTag;

            public ushort ReparseDataLength;

            public readonly ushort Reserved;

            public ushort SubstituteNameOffset;

            public ushort SubstituteNameLength;

            public ushort PrintNameOffset;

            public ushort PrintNameLength;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 0x3FF0)]
            public byte[] PathBuffer;
        }

        private const int FSCTL_SET_REPARSE_POINT = 0x000900A4;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        static extern uint GetFullPathNameW(string lpFileName, uint nBufferLength, [Out] StringBuilder lpBuffer, IntPtr lpFilePart);

        [Flags]
        private enum MoveFileFlags
        {
            MOVEFILE_REPLACE_EXISTING = 0x00000001,
            MOVEFILE_COPY_ALLOWED = 0x00000002,
            MOVEFILE_DELAY_UNTIL_REBOOT = 0x00000004,
            MOVEFILE_WRITE_THROUGH = 0x00000008,
            MOVEFILE_CREATE_HARDLINK = 0x00000010,
            MOVEFILE_FAIL_IF_NOT_TRACKABLE = 0x00000020
        }

        [return: MarshalAs(UnmanagedType.Bool)]
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool MoveFileEx(string lpExistingFileName, string lpNewFileName, MoveFileFlags dwFlags);

        #endregion

        /// <summary>
        /// <see cref="StaticIsOSVersionGreaterOrEqual(int, int)"/>
        /// </summary>
        public static bool StaticIsOSVersionGreaterOrEqual(Version version)
        {
            return StaticIsOSVersionGreaterOrEqual(version.Major, version.Minor);
        }

        /// <summary>
        /// Calls VerifyVersionInfo to determine if the running OS's version meets or exceeded the given major.minor version.
        /// </summary>
        /// <remarks>
        /// Unlike <see cref="Environment.OSVersion"/>, this works for Windows 8.1 and above.
        /// See the deprecation warnings at http://msdn.microsoft.com/en-us/library/windows/desktop/ms724451(v=vs.85).aspx
        /// </remarks>
        public static bool StaticIsOSVersionGreaterOrEqual(int major, int minor)
        {
            const uint ErrorOldWinVersion = 0x47e; // ERROR_OLD_WIN_VERSION
            const uint MajorVersion = 0x2; // VER_MAJOR_VERSION
            const uint MinorVersion = 0x1; // VER_MINOR_VERSION
            const byte CompareGreaterOrEqual = 0x3; // VER_GREATER_EQUAL

            ulong conditionMask = VerSetConditionMask(0, MajorVersion, CompareGreaterOrEqual);
            conditionMask = VerSetConditionMask(conditionMask, MinorVersion, CompareGreaterOrEqual);

            OsVersionInfoEx comparand = new OsVersionInfoEx { OSVersionInfoSize = OsVersionInfoEx.Size, MajorVersion = major, MinorVersion = minor };
            bool satisfied = VerifyVersionInfo(comparand, MajorVersion | MinorVersion, conditionMask);
            int hr = Marshal.GetLastWin32Error();

            if (!satisfied && hr != ErrorOldWinVersion)
            {
                throw ThrowForNativeFailure(hr, "VerifyVersionInfo");
            }

            return satisfied;
        }

        /// <nodoc />
        public static readonly Version MinWindowsVersionThatSupportsLongPaths = new Version(major: 6, minor: 2);

        /// <nodoc />
        public static readonly Version MinWindowsVersionThatSupportsNestedJobs = new Version(major: 6, minor: 2);

        /// <nodoc />
        public static readonly Version MinWindowsVersionThatSupportsWow64Processes = new Version(major: 5, minor: 1);

        /// <nodoc />
        public static readonly int MaxDirectoryPathOld = 130;

        /// <nodoc />
        public static readonly int MaxDirectoryPathNew = 260;

        /// <inheritdoc />
        public int MaxDirectoryPathLength()
        {
            return StaticIsOSVersionGreaterOrEqual(MinWindowsVersionThatSupportsLongPaths)
                ? MaxDirectoryPathNew
                : MaxDirectoryPathOld;
        }

        private readonly Lazy<bool> m_supportUnprivilegedCreateSymbolicLinkFlag = default;

        /// <summary>
        /// Checks if the OS supports SYMBOLIC_LINK_FLAG_ALLOW_UNPRIVILEGED_CREATE flag for creating symlink.
        /// </summary>
        /// <remarks>
        /// Not all Win 10 versions support this flag. Checking OS version for this feature is currently not advised
        /// because the OS may have had new features added in a redistributable DLL. 
        /// See: https://docs.microsoft.com/en-us/windows/desktop/SysInfo/operating-system-version
        /// </remarks>
        private bool CheckSupportUnprivilegedCreateSymbolicLinkFlag()
        {
            var tempTarget = Path.GetTempFileName();
            var tempLink = Path.GetTempFileName();
            DeleteFile(tempLink, true);
            CreateSymbolicLinkW(tempLink, tempTarget, SymbolicLinkTarget.File | SymbolicLinkTarget.AllowUnprivilegedCreate);
            int lastError = Marshal.GetLastWin32Error();
            DeleteFile(tempTarget, true);
            DeleteFile(tempLink, true);

            return lastError != NativeIOConstants.ErrorInvalidParameter;
        }

        /// <summary>
        /// Creates an instance of <see cref="FileSystemWin"/>.
        /// </summary>
        public FileSystemWin()
        {
            m_supportUnprivilegedCreateSymbolicLinkFlag = new Lazy<bool>(CheckSupportUnprivilegedCreateSymbolicLinkFlag);
        }

        /// <summary>
        /// Disposable struct to push / pop a thread-local error mode (e.g. <see cref="SemFailCriticalErrors"/>) within a 'using' block.
        /// This context must be created and disposed on the same thread.
        /// </summary>
        private readonly struct ErrorModeContext : IDisposable
        {
            private readonly bool m_isValid;
            private readonly int m_oldErrorMode;
            private readonly int m_thisErrorMode;
            private readonly int m_threadId;

            /// <summary>
            /// Creates an error mode context that represent pushing <paramref name="thisErrorMode"/> on top of the current <paramref name="oldErrorMode"/>
            /// </summary>
            private ErrorModeContext(int oldErrorMode, int thisErrorMode)
            {
                m_isValid = true;
                m_oldErrorMode = oldErrorMode;
                m_thisErrorMode = thisErrorMode;
                m_threadId = Thread.CurrentThread.ManagedThreadId;
            }

            /// <summary>
            /// Pushes an error mode context which is the current mode with the given extra flags set.
            /// (i.e., we push <c><see cref="GetThreadErrorMode"/> | <paramref name="additionalFlags"/></c>)
            /// </summary>
            public static ErrorModeContext PushWithAddedFlags(int additionalFlags)
            {
                int currentErrorMode = GetThreadErrorMode();
                int thisErrorMode = currentErrorMode | additionalFlags;

                int oldErrorModeViaSet;
                if (!SetThreadErrorMode(thisErrorMode, out oldErrorModeViaSet))
                {
                    int hr = Marshal.GetLastWin32Error();
                    throw ThrowForNativeFailure(hr, "SetThreadErrorMode");
                }

                Contract.Assume(currentErrorMode == oldErrorModeViaSet, "Thread error mode should only be change from calls on this thread");

                return new ErrorModeContext(oldErrorMode: currentErrorMode, thisErrorMode: thisErrorMode);
            }

            /// <summary>
            /// Sets <c>SEM_FAILCRITICALERRORS</c> in the thread's error mode (if it is not set already).
            /// The returned <see cref="ErrorModeContext"/> must be disposed to restore the prior error mode (and the disposal must occur on the same thread).
            /// </summary>
            /// <remarks>
            /// The intended effect is to avoid a blocking message box if a file path on a CD / floppy drive letter is poked without media inserted.
            /// This is neccessary before using volume management functions such as <see cref="ListVolumeGuidPathsAndSerials"/>
            /// See http://msdn.microsoft.com/en-us/library/windows/desktop/ms680621(v=vs.85).aspx
            /// </remarks>
            public static ErrorModeContext DisableMessageBoxForRemovableMedia()
            {
                return PushWithAddedFlags(SemFailCriticalErrors);
            }

            /// <summary>
            /// Pops this error mode context off of the thread's error mode stack.
            /// </summary>
            public void Dispose()
            {
                Contract.Assume(m_isValid);
                Contract.Assume(m_threadId == Thread.CurrentThread.ManagedThreadId, "An ErrorModeContext must be disposed on the same thread on which it was created");

                int errorModeBeforeRestore;
                if (!SetThreadErrorMode(m_oldErrorMode, out errorModeBeforeRestore))
                {
                    int hr = Marshal.GetLastWin32Error();
                    throw ThrowForNativeFailure(hr, "SetThreadErrorMode");
                }

                Contract.Assume(errorModeBeforeRestore == m_thisErrorMode, "The thread error mode changed within the ErrorModeContext, but was not restored before popping this context.");
            }
        }

        /// <inheritdoc />
        public unsafe NtStatus FlushPageCacheToFilesystem(SafeFileHandle handle)
        {
            IoStatusBlock iosb = default(IoStatusBlock);
            NtStatus status = NtFlushBuffersFileEx(handle, FlushFileBuffersFlags.FileDataOnly, null, 0, &iosb);
            return status;
        }

        /// <inheritdoc />
        public unsafe MiniUsnRecord? ReadFileUsnByHandle(SafeFileHandle fileHandle, bool forceJournalVersion2 = false)
        {
            Contract.Requires(fileHandle != null);

            int bytesReturned;

            // We support V2 and V3 records. V3 records (with ReFS length FileIds) are larger, so we allocate a buffer on that assumption.
            int recordBufferLength = NativeUsnRecordV3.MaximumSize;
            byte* recordBuffer = stackalloc byte[recordBufferLength];

            ReadFileUsnData readOptions = forceJournalVersion2 ? ReadFileUsnData.NtfsCompatible : ReadFileUsnData.NtfsAndReFSCompatible;

            if (!DeviceIoControl(
                    fileHandle,
                    ioControlCode: NativeIOConstants.FsctlReadFileUsnData,
                    inputBuffer: (IntPtr)(&readOptions),
                    inputBufferSize: ReadFileUsnData.Size,
                    outputBuffer: (IntPtr)recordBuffer,
                    outputBufferSize: recordBufferLength,
                    bytesReturned: out bytesReturned,
                    overlapped: IntPtr.Zero))
            {
                int error = Marshal.GetLastWin32Error();
                if (error == NativeIOConstants.ErrorJournalDeleteInProgress ||
                    error == NativeIOConstants.ErrorJournalNotActive ||
                    error == NativeIOConstants.ErrorInvalidFunction ||
                    error == NativeIOConstants.ErrorOnlyIfConnected ||
                    error == NativeIOConstants.ErrorAccessDenied ||
                    error == NativeIOConstants.ErrorNotSupported)
                {
                    return null;
                }

                throw ThrowForNativeFailure(error, "DeviceIoControl(FSCTL_READ_FILE_USN_DATA)");
            }

            NativeUsnRecordHeader* recordHeader = (NativeUsnRecordHeader*)recordBuffer;

            Contract.Assume(
                bytesReturned >= NativeUsnRecordHeader.Size,
                "Not enough data returned for a valid USN record header");

            Contract.Assume(
                bytesReturned == recordHeader->RecordLength,
                "RecordLength field disagrees from number of bytes actually returned; but we were expecting exactly one record.");

            MiniUsnRecord resultRecord;
            if (recordHeader->MajorVersion == 3)
            {
                Contract.Assume(!forceJournalVersion2);

                Contract.Assume(
                    bytesReturned >= NativeUsnRecordV3.MinimumSize && bytesReturned <= NativeUsnRecordV3.MaximumSize,
                    "FSCTL_READ_FILE_USN_DATA returned an amount of data that does not correspond to a valid USN_RECORD_V3.");

                NativeUsnRecordV3* record = (NativeUsnRecordV3*)recordBuffer;

                Contract.Assume(
                    record->Reason == 0 && record->TimeStamp == 0 && record->SourceInfo == 0,
                    "FSCTL_READ_FILE_USN_DATA scrubs these fields. Marshalling issue?");

                resultRecord = new MiniUsnRecord(record->FileReferenceNumber, record->Usn);
            }
            else if (recordHeader->MajorVersion == 2)
            {
                Contract.Assume(
                    bytesReturned >= NativeUsnRecordV2.MinimumSize && bytesReturned <= NativeUsnRecordV2.MaximumSize,
                    "FSCTL_READ_FILE_USN_DATA returned an amount of data that does not correspond to a valid USN_RECORD_V2.");

                NativeUsnRecordV2* record = (NativeUsnRecordV2*)recordBuffer;

                Contract.Assume(
                    record->Reason == 0 && record->TimeStamp == 0 && record->SourceInfo == 0,
                    "FSCTL_READ_FILE_USN_DATA scrubs these fields. Marshalling issue?");

                resultRecord = new MiniUsnRecord(new FileId(0, record->FileReferenceNumber), record->Usn);
            }
            else
            {
                Contract.Assume(false, "An unrecognized record version was returned, even though version 2 or 3 was requested.");
                throw new InvalidOperationException("Unreachable");
            }

            Logger.Log.StorageReadUsn(Events.StaticContext, resultRecord.FileId.High, resultRecord.FileId.Low, resultRecord.Usn.Value);
            return resultRecord;
        }

        /// <inheritdoc />
        public unsafe ReadUsnJournalResult TryReadUsnJournal(
            SafeFileHandle volumeHandle,
            byte[] buffer,
            ulong journalId,
            Usn startUsn = default(Usn),
            bool forceJournalVersion2 = false,
            bool isJournalUnprivileged = false)
        {
            Contract.Requires(volumeHandle != null);
            Contract.Requires(buffer != null && buffer.Length > 0);
            Contract.Ensures(Contract.Result<ReadUsnJournalResult>() != null);

            var readOptions = new ReadUsnJournalData
                              {
                                  MinMajorVersion = 2,
                                  MaxMajorVersion = forceJournalVersion2 ? (ushort) 2 : (ushort) 3,
                                  StartUsn = startUsn,
                                  Timeout = 0,
                                  BytesToWaitFor = 0,
                                  ReasonMask = uint.MaxValue, // TODO: Filter this!
                                  ReturnOnlyOnClose = 0,
                                  UsnJournalID = journalId,
                              };

            int bytesReturned;
            bool ioctlSuccess;
            int error;

            fixed (byte* pRecordBuffer = buffer)
            {
                ioctlSuccess = DeviceIoControl(
                    volumeHandle,
                    ioControlCode: isJournalUnprivileged ? NativeIOConstants.FsctlReadUnprivilegedUsnJournal : NativeIOConstants.FsctlReadUsnJournal,
                    inputBuffer: (IntPtr) (&readOptions),
                    inputBufferSize: ReadUsnJournalData.Size,
                    outputBuffer: (IntPtr) pRecordBuffer,
                    outputBufferSize: buffer.Length,
                    bytesReturned: out bytesReturned,
                    overlapped: IntPtr.Zero);
                error = Marshal.GetLastWin32Error();
            }

            if (!ioctlSuccess)
            {
                ReadUsnJournalStatus errorStatus;
                switch ((uint) error)
                {
                    case NativeIOConstants.ErrorJournalNotActive:
                        errorStatus = ReadUsnJournalStatus.JournalNotActive;
                        break;
                    case NativeIOConstants.ErrorJournalDeleteInProgress:
                        errorStatus = ReadUsnJournalStatus.JournalDeleteInProgress;
                        break;
                    case NativeIOConstants.ErrorJournalEntryDeleted:
                        errorStatus = ReadUsnJournalStatus.JournalEntryDeleted;
                        break;
                    case NativeIOConstants.ErrorInvalidParameter:
                        errorStatus = ReadUsnJournalStatus.InvalidParameter;
                        break;
                    case NativeIOConstants.ErrorInvalidFunction:
                        errorStatus = ReadUsnJournalStatus.VolumeDoesNotSupportChangeJournals;
                        break;
                    default:
                        throw ThrowForNativeFailure(error, "DeviceIoControl(FSCTL_READ_USN_JOURNAL)");
                }

                return new ReadUsnJournalResult(errorStatus, nextUsn: new Usn(0), records: null);
            }

            Contract.Assume(
                bytesReturned >= sizeof(ulong),
                "The output buffer should always contain the updated USN cursor (even if no records were returned)");

            var recordsToReturn = new List<UsnRecord>();
            ulong nextUsn;
            fixed (byte* recordBufferBase = buffer)
            {
                nextUsn = *(ulong*) recordBufferBase;
                byte* currentRecordBase = recordBufferBase + sizeof(ulong);
                Contract.Assume(currentRecordBase != null);

                // One past the end of the record part of the buffer
                byte* recordsEnd = recordBufferBase + bytesReturned;

                while (currentRecordBase < recordsEnd)
                {
                    Contract.Assume(
                        currentRecordBase + NativeUsnRecordHeader.Size <= recordsEnd,
                        "Not enough data returned for a valid USN record header");

                    NativeUsnRecordHeader* currentRecordHeader = (NativeUsnRecordHeader*) currentRecordBase;

                    Contract.Assume(
                        currentRecordBase + currentRecordHeader->RecordLength <= recordsEnd,
                        "RecordLength field advances beyond the buffer");

                    if (currentRecordHeader->MajorVersion == 3)
                    {
                        Contract.Assume(!forceJournalVersion2);

                        if (!(currentRecordHeader->RecordLength >= NativeUsnRecordV3.MinimumSize &&
                             currentRecordHeader->RecordLength <= NativeUsnRecordV3.MaximumSize))
                        {
                            Contract.Assert(false, "Size in record header does not correspond to a valid USN_RECORD_V3. Header record length: " + currentRecordHeader->RecordLength);
                        }

                        NativeUsnRecordV3* record = (NativeUsnRecordV3*) currentRecordBase;
                        recordsToReturn.Add(
                            new UsnRecord(
                                record->FileReferenceNumber,
                                record->ParentFileReferenceNumber,
                                record->Usn,
                                (UsnChangeReasons) record->Reason));
                    }
                    else if (currentRecordHeader->MajorVersion == 2)
                    {
                        if (!(currentRecordHeader->RecordLength >= NativeUsnRecordV2.MinimumSize &&
                              currentRecordHeader->RecordLength <= NativeUsnRecordV2.MaximumSize))
                        {
                            Contract.Assert(false, "Size in record header does not correspond to a valid USN_RECORD_V2. Header record length: " + currentRecordHeader->RecordLength);
                        }

                        NativeUsnRecordV2* record = (NativeUsnRecordV2*) currentRecordBase;
                        recordsToReturn.Add(
                            new UsnRecord(
                                new FileId(0, record->FileReferenceNumber),
                                new FileId(0, record->ParentFileReferenceNumber),
                                record->Usn,
                                (UsnChangeReasons) record->Reason));
                    }
                    else
                    {
                        Contract.Assume(
                            false,
                            "An unrecognized record version was returned, even though version 2 or 3 was requested.");
                        throw new InvalidOperationException("Unreachable");
                    }

                    currentRecordBase += currentRecordHeader->RecordLength;
                }
            }

            return new ReadUsnJournalResult(ReadUsnJournalStatus.Success, new Usn(nextUsn), recordsToReturn);
        }

        /// <inheritdoc />
        public QueryUsnJournalResult TryQueryUsnJournal(SafeFileHandle volumeHandle)
        {
            Contract.Requires(volumeHandle != null);
            Contract.Ensures(Contract.Result<QueryUsnJournalResult>() != null);

            var data = new QueryUsnJournalData();

            bool ioctlSuccess = DeviceIoControl(
                volumeHandle,
                ioControlCode: NativeIOConstants.FsctlQueryUsnJournal,
                inputBuffer: IntPtr.Zero,
                inputBufferSize: 0,
                outputBuffer: data,
                outputBufferSize: QueryUsnJournalData.Size,
                bytesReturned: out int bytesReturned,
                overlapped: IntPtr.Zero);
            int error = Marshal.GetLastWin32Error();

            if (!ioctlSuccess)
            {
                QueryUsnJournalStatus errorStatus;
                switch ((uint)error)
                {
                    case NativeIOConstants.ErrorJournalNotActive:
                        errorStatus = QueryUsnJournalStatus.JournalNotActive;
                        break;
                    case NativeIOConstants.ErrorJournalDeleteInProgress:
                        errorStatus = QueryUsnJournalStatus.JournalDeleteInProgress;
                        break;
                    case NativeIOConstants.ErrorInvalidFunction:
                        errorStatus = QueryUsnJournalStatus.VolumeDoesNotSupportChangeJournals;
                        break;
                    case NativeIOConstants.ErrorInvalidParameter:
                        errorStatus = QueryUsnJournalStatus.InvalidParameter;
                        break;
                    case NativeIOConstants.ErrorAccessDenied:
                        errorStatus = QueryUsnJournalStatus.AccessDenied;
                        break;
                    default:
                        throw ThrowForNativeFailure(error, "DeviceIoControl(FSCTL_QUERY_USN_JOURNAL)");
                }

                return new QueryUsnJournalResult(errorStatus, data: null);
            }

            Contract.Assume(bytesReturned == QueryUsnJournalData.Size, "Output buffer size mismatched (not all fields populated?)");

            return new QueryUsnJournalResult(QueryUsnJournalStatus.Success, data);
        }

        /// <inheritdoc />
        public unsafe Usn? TryWriteUsnCloseRecordByHandle(SafeFileHandle fileHandle)
        {
            Contract.Requires(fileHandle != null);

            ulong writtenUsn;

            if (!DeviceIoControl(
                    fileHandle,
                    ioControlCode: NativeIOConstants.FsctlWriteUsnCloseRecord,
                    inputBuffer: IntPtr.Zero,
                    inputBufferSize: 0,
                    outputBuffer: (IntPtr)(&writtenUsn),
                    outputBufferSize: sizeof(ulong),
                    bytesReturned: out int bytesReturned,
                    overlapped: IntPtr.Zero))
            {
                int error = Marshal.GetLastWin32Error();

                if (error == NativeIOConstants.ErrorJournalDeleteInProgress ||
                    error == NativeIOConstants.ErrorJournalNotActive ||
                    error == NativeIOConstants.ErrorWriteProtect)
                {
                    return null;
                }

                throw ThrowForNativeFailure(error, "DeviceIoControl(FSCTL_WRITE_USN_CLOSE_RECORD)");
            }

            Contract.Assume(bytesReturned == sizeof(ulong));
            Logger.Log.StorageCheckpointUsn(Events.StaticContext, writtenUsn);

            return new Usn(writtenUsn);
        }

        /// <summary>
        /// Indicates if the running OS is at least Windows 8.0 / Server 2012
        /// (which is the first version to support nested jobs, hence <see cref="FileSystemWin.MinWindowsVersionThatSupportsNestedJobs"/>)
        /// </summary>
        private static readonly bool s_runningWindows8OrAbove = StaticIsOSVersionGreaterOrEqual(FileSystemWin.MinWindowsVersionThatSupportsNestedJobs);

        /// <summary>
        /// Indicates if the extended (128-bit) file ID type is supported on this running OS.
        /// http://msdn.microsoft.com/en-us/library/windows/desktop/aa364227(v=vs.85).aspx
        /// </summary>
        [Pure]
        private static bool IsExtendedFileIdSupported()
        {
            return s_runningWindows8OrAbove;
        }

        /// <inheritdoc />
        public unsafe FileIdAndVolumeId? TryGetFileIdAndVolumeIdByHandle(SafeFileHandle fileHandle)
        {
            Contract.Requires(fileHandle != null);

            var info = default(FileIdAndVolumeId);
            if (!GetFileInformationByHandleEx(fileHandle, (uint)FileInfoByHandleClass.FileIdInfo, (IntPtr)(&info), FileIdAndVolumeId.Size))
            {
                return null;
            }

            return info;
        }

        /// <inheritdoc />
        public unsafe FileAttributes GetFileAttributesByHandle(SafeFileHandle fileHandle)
        {
            Contract.Requires(fileHandle != null);

            var info = default(FileBasicInfo);
            if (!GetFileInformationByHandleEx(fileHandle, (uint)FileInfoByHandleClass.FileBasicInfo, (IntPtr)(&info), sizeof(FileBasicInfo)))
            {
                int hr = Marshal.GetLastWin32Error();
                ThrowForNativeFailure(hr, "GetFileInformationByHandleEx");
            }

            return info.Attributes;
        }

        /// <inheritdoc />
        public unsafe bool IsPendingDelete(SafeFileHandle fileHandle)
        {
            Contract.Requires(fileHandle != null);

            var info = default(FileStandardInfo);
            if (!GetFileInformationByHandleEx(fileHandle, (uint)FileInfoByHandleClass.FileStandardInfo, (IntPtr)(&info), sizeof(FileStandardInfo)))
            {
                int hr = Marshal.GetLastWin32Error();
                ThrowForNativeFailure(hr, "GetFileInformationByHandleEx");
            }

            return info.DeletePending;
        }

        /// <summary>
        /// Queries the current length (end-of-file position) of an open file.
        /// </summary>
        public static long GetFileLengthByHandle(SafeFileHandle fileHandle)
        {
            Contract.Requires(fileHandle != null);

            if (!GetFileSizeEx(fileHandle, out long size))
            {
                int hr = Marshal.GetLastWin32Error();
                ThrowForNativeFailure(hr, "GetFileSizeEx");
            }

            return size;
        }

        /// <inheritdoc />
        [SuppressMessage("Microsoft.Naming", "CA1720:IdentifiersShouldNotContainTypeNames", MessageId = "short")]
        public unsafe uint GetShortVolumeSerialNumberByHandle(SafeFileHandle fileHandle)
        {
            uint serial = 0;
            bool success = GetVolumeInformationByHandleW(
                fileHandle,
                volumeNameBuffer: null,
                volumeNameBufferSize: 0,
                volumeSerial: (IntPtr)(&serial),
                maximumComponentLength: IntPtr.Zero,
                fileSystemFlags: IntPtr.Zero,
                fileSystemNameBuffer: null,
                fileSystemNameBufferSize: 0);
            if (!success)
            {
                int hr = Marshal.GetLastWin32Error();
                throw ThrowForNativeFailure(hr, "GetVolumeInformationByHandleW");
            }

            return serial;
        }

        /// <inheritdoc />
        public ulong GetVolumeSerialNumberByHandle(SafeFileHandle fileHandle)
        {
            FileIdAndVolumeId? maybeInfo = TryGetFileIdAndVolumeIdByHandle(fileHandle);
            if (maybeInfo.HasValue)
            {
                return maybeInfo.Value.VolumeSerialNumber;
            }

            return GetShortVolumeSerialNumberByHandle(fileHandle);
        }

        /// <inheritdoc />
        public unsafe bool TrySetDeletionDisposition(SafeFileHandle handle)
        {
            byte delete = 1;
            return SetFileInformationByHandle(handle, (uint)FileInfoByHandleClass.FileDispositionInfo, (IntPtr)(&delete), sizeof(byte));
        }

        /// <inheritdoc />
        public unsafe bool TryRename(SafeFileHandle handle, string destination, bool replaceExisting)
        {
            destination = ToLongPathIfExceedMaxPath(destination);

            // FileRenameInfo as we've defined it contains one character which is enough for a terminating null byte. Then, we need room for the real characters.
            int fileNameLengthInBytesExcludingNull = destination.Length * sizeof(char);
            int structSizeIncludingDestination = sizeof(FileRenameInfo) + fileNameLengthInBytesExcludingNull;

            var buffer = new byte[structSizeIncludingDestination];

            fixed (byte* b = buffer)
            {
                var renameInfo = (FileRenameInfo*)b;
                renameInfo->ReplaceIfExists = replaceExisting ? (byte)1 : (byte)0;
                renameInfo->RootDirectory = IntPtr.Zero;
                renameInfo->FileNameLengthInBytes = fileNameLengthInBytesExcludingNull + sizeof(char);

                char* filenameBuffer = &renameInfo->FileName;
                for (int i = 0; i < destination.Length; i++)
                {
                    filenameBuffer[i] = destination[i];
                }

                filenameBuffer[destination.Length] = (char)0;
                Contract.Assume(buffer.Length > 2 && b[buffer.Length - 1] == 0 && b[buffer.Length - 2] == 0);

                return SetFileInformationByHandle(handle, (uint)FileInfoByHandleClass.FileRenameInfo, (IntPtr)renameInfo, structSizeIncludingDestination);
            }
        }

        /// <inheritdoc />
        internal unsafe void SetFileTimestampsByHandle(SafeFileHandle handle, DateTime creationTime, DateTime accessTime, DateTime lastWriteTime, DateTime lastChangeTime)
        {
            var newInfo = default(FileBasicInfo);
            newInfo.Attributes = (FileAttributes)0;
            newInfo.CreationTime = unchecked((ulong)creationTime.ToFileTimeUtc());
            newInfo.LastAccessTime = unchecked((ulong)accessTime.ToFileTimeUtc());
            newInfo.LastWriteTime = unchecked((ulong)lastWriteTime.ToFileTimeUtc());
            newInfo.ChangeTime = unchecked((ulong)lastChangeTime.ToFileTimeUtc());

            if (!SetFileInformationByHandle(handle, (uint)FileInfoByHandleClass.FileBasicInfo, (IntPtr)(&newInfo), sizeof(FileBasicInfo)))
            {
                ThrowForNativeFailure(Marshal.GetLastWin32Error(), nameof(SetFileInformationByHandle));
            }
        }

        /// <inheritdoc />
        internal unsafe void GetFileTimestampsByHandle(SafeFileHandle handle, out DateTime creationTime, out DateTime accessTime, out DateTime lastWriteTime, out DateTime lastChangeTime)
        {
            var info = default(FileBasicInfo);

            if (!GetFileInformationByHandleEx(handle, (uint)FileInfoByHandleClass.FileBasicInfo, (IntPtr)(&info), sizeof(FileBasicInfo)))
            {
                ThrowForNativeFailure(Marshal.GetLastWin32Error(), nameof(GetFileInformationByHandleEx));
            }

            creationTime = DateTime.FromFileTimeUtc(unchecked((long) info.CreationTime));
            accessTime = DateTime.FromFileTimeUtc(unchecked((long)info.LastAccessTime));
            lastWriteTime = DateTime.FromFileTimeUtc(unchecked((long)info.LastWriteTime));
            lastChangeTime = DateTime.FromFileTimeUtc(unchecked((long)info.ChangeTime));
        }

        /// <inheritdoc />
        public unsafe bool TryPosixDelete(string pathToDelete, out OpenFileResult openFileResult)
        {
            SafeFileHandle handle = CreateFileW(
                ToLongPathIfExceedMaxPath(pathToDelete),
                FileDesiredAccess.Delete,
                FileShare.Delete | FileShare.Read | FileShare.Write,
                IntPtr.Zero,
                FileMode.Open,
                FileFlagsAndAttributes.FileFlagBackupSemantics | FileFlagsAndAttributes.FileFlagOpenReparsePoint,
                IntPtr.Zero);

            using (handle)
            {
                int hr = Marshal.GetLastWin32Error();
                if (handle.IsInvalid)
                {
                    Logger.Log.StorageTryOpenOrCreateFileFailure(Events.StaticContext, pathToDelete, (int)FileMode.Open, hr);
                    openFileResult = OpenFileResult.Create(pathToDelete, hr, FileMode.Open, handleIsValid: false);
                    return false;
                }

                // handle will not be actually valid after this function terminates,
                // but it was at this time, and this is what we are reporting.
                openFileResult = OpenFileResult.Create(pathToDelete, hr, FileMode.Open, handleIsValid: true);
                FileDispositionInfoEx fdi;
                fdi.Flags = FileDispositionFlags.Delete | FileDispositionFlags.PosixSemantics;

                // this is an optimistic call that might fail, so we are not calling Marshal.GetLastWin32Error() after it, just
                // relying on return value.
                bool deleted = SetFileInformationByHandle(
                    handle,
                    (uint)FileInfoByHandleClass.FileDispositionInfoEx,
                    (IntPtr)(&fdi),
                    sizeof(FileDispositionInfoEx));
                return deleted;
            }
        }

        /// <inheritdoc />
        public FileSystemType GetVolumeFileSystemByHandle(SafeFileHandle fileHandle)
        {
            var fileSystemNameBuffer = new StringBuilder(32);
            bool success = GetVolumeInformationByHandleW(
                fileHandle,
                volumeNameBuffer: null,
                volumeNameBufferSize: 0,
                volumeSerial: IntPtr.Zero,
                maximumComponentLength: IntPtr.Zero,
                fileSystemFlags: IntPtr.Zero,
                fileSystemNameBuffer: fileSystemNameBuffer,
                fileSystemNameBufferSize: fileSystemNameBuffer.Capacity);
            if (!success)
            {
                int hr = Marshal.GetLastWin32Error();
                throw ThrowForNativeFailure(hr, "GetVolumeInformationByHandleW");
            }

            string fileSystemName = fileSystemNameBuffer.ToString();
            switch (fileSystemName)
            {
                case "NTFS":
                    return FileSystemType.NTFS;
                case "ReFS":
                    return FileSystemType.ReFS;
                default:
                    return FileSystemType.Unknown;
            }
        }

        /// <inheritdoc />
        public OpenFileResult TryOpenDirectory(
            string directoryPath,
            FileDesiredAccess desiredAccess,
            FileShare shareMode,
            FileFlagsAndAttributes flagsAndAttributes,
            out SafeFileHandle handle)
        {
            Contract.Requires(!string.IsNullOrEmpty(directoryPath));
            Contract.Ensures(Contract.Result<OpenFileResult>().Succeeded == (Contract.ValueAtReturn(out handle) != null));
            Contract.Ensures(!Contract.Result<OpenFileResult>().Succeeded || !Contract.ValueAtReturn(out handle).IsInvalid);

            return TryOpenDirectory(directoryPath, desiredAccess, shareMode, FileMode.Open, flagsAndAttributes, out handle);
        }

        /// <inheritdoc />
        /// <remarks>
        /// This code is adapted from <see cref="Directory.CreateDirectory(string)"/>.
        /// This code assumes that the directory path has been canonicalized by calling <see cref="GetFullPath(string, out int)"/>.
        /// </remarks>
        public void CreateDirectory(string directoryPath)
        {
            Contract.Requires(!string.IsNullOrEmpty(directoryPath));

            int length = directoryPath.Length;

            if (length >= 2 && IsDirectorySeparator(directoryPath[length - 1]))
            {
                // Skip ending directory separator without trimming the path.
                --length;
            }

            int rootLength = GetRootLength(directoryPath);

            if (Directory.Exists(directoryPath))
            {
                // Short cut if directory exists
                return;
            }

            // Now collect directory path and its parents. We must ensure
            // that the parents exist before creating the requested directory path.
            var stackDirs = new Stack<string>();

            bool parentPathExists = false;

            if (length > rootLength)
            {
                // We are traversing the path bottom up to collect non-existent parents, and push them to stack.
                // Thus, the top-most non-existent parent will be created first later.
                int i = length - 1;

                while (i >= rootLength && !parentPathExists)
                {
                    string dir = directoryPath.Substring(0, i + 1);
                    if (!Directory.Exists(dir))
                    {
                        stackDirs.Push(dir);
                    }
                    else
                    {
                        // Some parent path exists, stop traversal.
                        parentPathExists = true;
                    }

                    // Skip directory separators.
                    while (i > rootLength && !IsDirectorySeparator(directoryPath[i]))
                    {
                        --i;
                    }

                    --i;
                }
            }

            // Now start creating directories from the top-most non-existent parent.
            bool result = true;
            int firstFoundError = NativeIOConstants.ErrorSuccess;

            while (stackDirs.Count > 0)
            {
                string dir = stackDirs.Pop();
                result = CreateDirectoryW(ToLongPathIfExceedMaxPath(dir), IntPtr.Zero);

                if (!result && (firstFoundError == NativeIOConstants.ErrorSuccess))
                {
                    int currentError = Marshal.GetLastWin32Error();

                    if (currentError != NativeIOConstants.ErrorAlreadyExists)
                    {
                        // Another thread may have been created directory or its parents.
                        firstFoundError = currentError;
                    }
                    else
                    {
                        if (FileExistsNoFollow(dir) || (DirectoryExistsNoFollow(dir) && currentError == NativeIOConstants.ErrorAccessDenied))
                        {
                            // The directory or its parents may have existed as files or creation results in denied access.
                            firstFoundError = currentError;
                        }
                    }
                }
            }

            // Only throw an exception if creating the exact directory failed.
            if (!result && firstFoundError != NativeIOConstants.ErrorSuccess)
            {
                throw new BuildXLException(I($"Failed to create directory '{directoryPath}'"), CreateWin32Exception(firstFoundError, "CreateDirectoryW"));
            }
        }

        private OpenFileResult TryOpenDirectory(
            string directoryPath,
            FileDesiredAccess desiredAccess,
            FileShare shareMode,
            FileMode fileMode,
            FileFlagsAndAttributes flagsAndAttributes,
            out SafeFileHandle handle)
        {
            Contract.Requires(!string.IsNullOrEmpty(directoryPath));

            handle = CreateFileW(
                ToLongPathIfExceedMaxPath(directoryPath),
                desiredAccess | FileDesiredAccess.Synchronize,
                shareMode,
                lpSecurityAttributes: IntPtr.Zero,
                dwCreationDisposition: fileMode,
                dwFlagsAndAttributes: flagsAndAttributes | FileFlagsAndAttributes.FileFlagBackupSemantics,
                hTemplateFile: IntPtr.Zero);
            int hr = Marshal.GetLastWin32Error();

            if (handle.IsInvalid)
            {
                Logger.Log.StorageTryOpenDirectoryFailure(Events.StaticContext, directoryPath, hr);
                handle = null;
                Contract.Assume(hr != 0);
                var result = OpenFileResult.Create(directoryPath, hr, fileMode, handleIsValid: false);
                Contract.Assume(!result.Succeeded);
                return result;
            }
            else
            {
                var result = OpenFileResult.Create(directoryPath, hr, fileMode, handleIsValid: true);
                Contract.Assume(result.Succeeded);
                return result;
            }
        }

        /// <inheritdoc />
        public OpenFileResult TryOpenDirectory(string directoryPath, FileShare shareMode, out SafeFileHandle handle)
        {
            Contract.Requires(!string.IsNullOrEmpty(directoryPath));
            Contract.Ensures(Contract.Result<OpenFileResult>().Succeeded == (Contract.ValueAtReturn(out handle) != null));
            Contract.Ensures(!Contract.Result<OpenFileResult>().Succeeded || !Contract.ValueAtReturn(out handle).IsInvalid);

            return TryOpenDirectory(directoryPath, FileDesiredAccess.None, shareMode, FileFlagsAndAttributes.None, out handle);
        }

        /// <inheritdoc />
        public OpenFileResult TryCreateOrOpenFile(
            string path,
            FileDesiredAccess desiredAccess,
            FileShare shareMode,
            FileMode creationDisposition,
            FileFlagsAndAttributes flagsAndAttributes,
            out SafeFileHandle handle)
        {
            handle = CreateFileW(
                ToLongPathIfExceedMaxPath(path),
                desiredAccess,
                shareMode,
                lpSecurityAttributes: IntPtr.Zero,
                dwCreationDisposition: creationDisposition,
                dwFlagsAndAttributes: flagsAndAttributes,
                hTemplateFile: IntPtr.Zero);
            int hr = Marshal.GetLastWin32Error();

            if (handle.IsInvalid)
            {
                Logger.Log.StorageTryOpenOrCreateFileFailure(Events.StaticContext, path, (int)creationDisposition, hr);
                handle = null;
                Contract.Assume(hr != 0);
                var result = OpenFileResult.Create(path, hr, creationDisposition, handleIsValid: false);
                Contract.Assume(!result.Succeeded);
                return result;
            }
            else
            {
                var result = OpenFileResult.Create(path, hr, creationDisposition, handleIsValid: true);
                Contract.Assume(result.Succeeded);
                return result;
            }
        }

        /// <inheritdoc />
        public ReOpenFileStatus TryReOpenFile(
            SafeFileHandle existing,
            FileDesiredAccess desiredAccess,
            FileShare shareMode,
            FileFlagsAndAttributes flagsAndAttributes,
            out SafeFileHandle reopenedHandle)
        {
            Contract.Requires(existing != null);
            Contract.Ensures((Contract.Result<ReOpenFileStatus>() == ReOpenFileStatus.Success) == (Contract.ValueAtReturn(out reopenedHandle) != null));
            Contract.Ensures((Contract.Result<ReOpenFileStatus>() != ReOpenFileStatus.Success) || !Contract.ValueAtReturn(out reopenedHandle).IsInvalid);

            SafeFileHandle newHandle = ReOpenFile(existing, desiredAccess, shareMode, flagsAndAttributes);
            int hr = Marshal.GetLastWin32Error();
            if (newHandle.IsInvalid)
            {
                reopenedHandle = null;
                Contract.Assume(hr != NativeIOConstants.ErrorSuccess, "Invalid handle should imply an error.");
                switch (hr)
                {
                    case NativeIOConstants.ErrorSharingViolation:
                        return ReOpenFileStatus.SharingViolation;
                    case NativeIOConstants.ErrorAccessDenied:
                        return ReOpenFileStatus.AccessDenied;
                    default:
                        throw ThrowForNativeFailure(hr, "ReOpenFile");
                }
            }
            else
            {
                reopenedHandle = newHandle;
                return ReOpenFileStatus.Success;
            }
        }

        /// <inheritdoc />
        public FileStream CreateFileStream(
            string path,
            FileMode fileMode,
            FileAccess fileAccess,
            FileShare fileShare,
            FileOptions options,
            bool force)
        {
            // The bufferSize of 4096 bytes is the default as used by the other FileStream constructors
            // http://index/mscorlib/system/io/filestream.cs.html
            return ExceptionUtilities.HandleRecoverableIOException(
                () =>
                {
                    string streamPath = ToLongPathIfExceedMaxPath(path);

                    try
                    {
                        return new FileStream(streamPath, fileMode, fileAccess, fileShare, bufferSize: DefaultBufferSize, options: options);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // This is a workaround to allow write access to a file that is marked as readonly. It is
                        // exercised when hashing the output files of pips that create readonly files. The hashing currently
                        // opens files as write
                        if (force)
                        {
                            if (!TryGetFileAttributes(streamPath, out FileAttributes fileAttributes, out int hrGet)
                                || !TrySetFileAttributes(streamPath, fileAttributes & ~FileAttributes.ReadOnly, out int hrSet))
                            {
                                throw;
                            }

                            return new FileStream(streamPath, fileMode, fileAccess, fileShare, bufferSize: DefaultBufferSize, options: options);
                        }

                        throw;
                    }
                },
                ex =>
                {
                    throw new BuildXLException(I($"Failed to open path '{path}' with mode='{fileMode}', access='{fileAccess}', share='{fileShare}'"), ex);
                });
        }

        /// <summary>
        /// Creates a new IO completion port.
        /// </summary>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope",
            Justification = "Handles are either null/invalid or intentionally returned to caller")]
        public static SafeIOCompletionPortHandle CreateIOCompletionPort()
        {
            IntPtr rawHandle = CreateIoCompletionPort(
                handle: new SafeFileHandle(new IntPtr(-1), ownsHandle: false),
                existingCompletionPort: SafeIOCompletionPortHandle.CreateInvalid(),
                completionKey: IntPtr.Zero,
                numberOfConcurrentThreads: 0);

            int error = Marshal.GetLastWin32Error();
            var handle = new SafeIOCompletionPortHandle(rawHandle);

            if (handle.IsInvalid)
            {
                throw ThrowForNativeFailure(error, "CreateIoCompletionPort");
            }

            return handle;
        }

        /// <summary>
        /// Binds a file handle to the given IO completion port. The file must have been opened with <see cref="FileFlagsAndAttributes.FileFlagOverlapped"/>.
        /// Future completed IO operations for this handle will be queued to the specified port.
        /// </summary>
        /// <remarks>
        /// Along with binding to the port, this function also sets the handle's completion mode to <c>FILE_SKIP_COMPLETION_PORT_ON_SUCCESS</c>.
        /// This means that the caller should respect <c>ERROR_SUCCESS</c> (don't assume <c>ERROR_IO_PENDING</c>).
        /// </remarks>
        [SuppressMessage("Microsoft.Reliability", "CA2001:AvoidCallingProblematicMethods")]
        public static void BindFileHandleToIOCompletionPort(SafeFileHandle handle, SafeIOCompletionPortHandle port, IntPtr completionKey)
        {
            Contract.Requires(handle != null && !handle.IsInvalid);
            Contract.Requires(port != null && !port.IsInvalid);

            IntPtr returnedHandle = CreateIoCompletionPort(
                handle: handle,
                existingCompletionPort: port,
                completionKey: completionKey,
                numberOfConcurrentThreads: 0);

            if (returnedHandle == IntPtr.Zero || returnedHandle == INVALID_HANDLE_VALUE)
            {
                throw ThrowForNativeFailure(Marshal.GetLastWin32Error(), "CreateIoCompletionPort");
            }

            // Note that we do not wrap returnedHandle as a safe handle. This is because we would otherwise have two safe handles
            // wrapping the same underlying handle value, and could then double-free it.
            Contract.Assume(returnedHandle == port.DangerousGetHandle());

            // TODO:454491: We could also set FileSkipSetEventOnHandle here, such that the file's internal event is not cleared / signaled by the IO manager.
            //       However, this is a compatibility problem for existing usages of e.g. DeviceIoControl that do not specify an OVERLAPPED (which
            //       may wait on the file to be signaled). Ideally, we never depend on signaling a file handle used for async I/O, since we may
            //       to issue concurrent operations on the handle (and without the IO manager serializing requests as with sync handles, depending
            //       on signaling and waiting the file handle is simply unsafe).
            // We need unchecked here. The issue is that the SetFileCompletionNotificationModes native function returns BOOL, which is actually an int8.
            // When marshaling to Bool, if the highest bit is set we can get overflow error.
            bool success = unchecked(SetFileCompletionNotificationModes(
                handle,
                FileCompletionMode.FileSkipCompletionPortOnSuccess));

            if (!success)
            {
                throw ThrowForNativeFailure(Marshal.GetLastWin32Error(), "SetFileCompletionNotificationModes");
            }
        }

        /// <summary>
        /// Issues an async read via <c>ReadFile</c>. The eventual completion will possibly be sent to an I/O completion port, associated with <see cref="Windows.FileSystemWin.BindFileHandleToIOCompletionPort"/>.
        /// Note that <paramref name="pinnedBuffer"/> must be pinned on a callstack that lives until I/O completion or with a pinning <see cref="System.Runtime.InteropServices.GCHandle"/>,
        /// similarly with the provided <paramref name="pinnedOverlapped" />; both are accessed by the kernel as the request is processed in the background.
        /// </summary>
        public static unsafe FileAsyncIOResult ReadFileOverlapped(SafeFileHandle handle, byte* pinnedBuffer, int bytesToRead, long fileOffset, Overlapped* pinnedOverlapped)
        {
            Contract.Requires(handle != null && !handle.IsInvalid);

            pinnedOverlapped->Offset = fileOffset;

            bool success = ReadFile(handle, pinnedBuffer, bytesToRead, lpNumberOfBytesRead: (int*)IntPtr.Zero, lpOverlapped: pinnedOverlapped);
            return CreateFileAsyncIOResult(handle, pinnedOverlapped, success);
        }

        /// <summary>
        /// Issues an async write via <c>WriteFile</c>. The eventual completion will possibly be sent to an I/O completion port, associated with <see cref="BindFileHandleToIOCompletionPort"/>.
        /// Note that <paramref name="pinnedBuffer"/> must be pinned on a callstack that lives until I/O completion or with a pinning <see cref="GCHandle"/>,
        /// similarly with the provided <paramref name="pinnedOverlapped" />; both are accessed by the kernel as the request is processed in the background.
        /// </summary>
        public static unsafe FileAsyncIOResult WriteFileOverlapped(SafeFileHandle handle, byte* pinnedBuffer, int bytesToWrite, long fileOffset, Overlapped* pinnedOverlapped)
        {
            Contract.Requires(handle != null && !handle.IsInvalid);

            pinnedOverlapped->Offset = fileOffset;

            bool success = WriteFile(handle, pinnedBuffer, bytesToWrite, lpNumberOfBytesWritten: (int*)IntPtr.Zero, lpOverlapped: pinnedOverlapped);
            return CreateFileAsyncIOResult(handle, pinnedOverlapped, success);
        }

        /// <summary>
        /// Common conversion from an overlapped <c>ReadFile</c> or <c>WriteFile</c> result to a <see cref="FileAsyncIOResult"/>.
        /// This must be called immediately after the IO operation such that <see cref="Marshal.GetLastWin32Error"/> is still valid.
        /// </summary>
        [SuppressMessage("Microsoft.Interoperability", "CA1404:CallGetLastErrorImmediatelyAfterPInvoke", Justification = "Intentionally wrapping GetLastWin32Error")]
        private static unsafe FileAsyncIOResult CreateFileAsyncIOResult(SafeFileHandle handle, Overlapped* pinnedOverlapped, bool success)
        {
            if (success)
            {
                // Success: IO completed synchronously and we will assume no completion packet is coming (due to FileCompletionMode.FileSkipCompletionPortOnSuccess).
                GetCompletedOverlappedResult(handle, pinnedOverlapped, out int error, out int bytesTransferred);
                Contract.Assume(error == NativeIOConstants.ErrorSuccess, "IO operation indicated success, but the completed OVERLAPPED did not contain ERROR_SUCCESS");
                return new FileAsyncIOResult(FileAsyncIOStatus.Succeeded, bytesTransferred: bytesTransferred, error: NativeIOConstants.ErrorSuccess);
            }
            else
            {
                // Pending (a completion packet is expected) or synchronous failure.
                int error = Marshal.GetLastWin32Error();
                Contract.Assume(error != NativeIOConstants.ErrorSuccess);

                bool completedSynchronously = error != NativeIOConstants.ErrorIOPending;
                return new FileAsyncIOResult(
                    completedSynchronously ? FileAsyncIOStatus.Failed : FileAsyncIOStatus.Pending,
                    bytesTransferred: 0,
                    error: error);
            }
        }

        /// <summary>
        /// Unpacks a completed <c>OVERLAPPED</c> structure into the number of bytes transferred and error code for the completed operation.
        /// Fails if the given overlapped structure indicates that the IO operation has not yet completed.
        /// </summary>
        public static unsafe void GetCompletedOverlappedResult(SafeFileHandle handle, Overlapped* overlapped, out int error, out int bytesTransferred)
        {
            int bytesTransferredTemp = 0;
            if (!GetOverlappedResult(handle, overlapped, &bytesTransferredTemp, bWait: false))
            {
                bytesTransferred = 0;
                error = Marshal.GetLastWin32Error();
                if (error == NativeIOConstants.ErrorIOIncomplete)
                {
                    throw ThrowForNativeFailure(error, "GetOverlappedResult");
                }
            }
            else
            {
                bytesTransferred = bytesTransferredTemp;
                error = NativeIOConstants.ErrorSuccess;
            }
        }

        /// <summary>
        /// Status of dequeueing an I/O completion packet from a port. Indepenent from success / failure in the packet itself.
        /// </summary>
        public enum IOCompletionPortDequeueStatus
        {
            /// <summary>
            /// A packet was dequeued.
            /// </summary>
            Succeeded,

            /// <summary>
            /// The completion port has been closed, so further dequeues cannot proceed.
            /// </summary>
            CompletionPortClosed,
        }

        /// <summary>
        /// Result of dequeueing an I/O completion packet from a port.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
        public unsafe readonly struct IOCompletionPortDequeueResult
        {
            /// <summary>
            /// Dequeue status (for the dequeue operation itself).
            /// </summary>
            public readonly IOCompletionPortDequeueStatus Status;
            private readonly FileAsyncIOResult m_completedIO;
            private readonly IntPtr m_completionKey;
            private readonly Overlapped* m_dequeuedOverlapped;

            internal IOCompletionPortDequeueResult(FileAsyncIOResult completedIO, Overlapped* dequeuedOverlapped, IntPtr completionKey)
            {
                Contract.Requires(completedIO.Status == FileAsyncIOStatus.Succeeded || completedIO.Status == FileAsyncIOStatus.Failed);
                Status = IOCompletionPortDequeueStatus.Succeeded;
                m_completedIO = completedIO;
                m_completionKey = completionKey;
                m_dequeuedOverlapped = dequeuedOverlapped;
            }

            internal IOCompletionPortDequeueResult(IOCompletionPortDequeueStatus status)
            {
                Contract.Requires(status != IOCompletionPortDequeueStatus.Succeeded);
                Status = status;
                m_completedIO = default(FileAsyncIOResult);
                m_completionKey = default(IntPtr);
                m_dequeuedOverlapped = null;
            }

            /// <summary>
            /// Result of the asynchronous I/O that completed. Available only if the status is <see cref="IOCompletionPortDequeueStatus.Succeeded"/>,
            /// meaning that a packet was actually dequeued.
            /// </summary>
            public FileAsyncIOResult CompletedIO
            {
                get
                {
                    Contract.Requires(Status == IOCompletionPortDequeueStatus.Succeeded);
                    Contract.Ensures(Contract.Result<FileAsyncIOResult>().Status != FileAsyncIOStatus.Pending);
                    return m_completedIO;
                }
            }

            /// <summary>
            /// Completion key (handle unique identifier) of the completed I/O. Available only if the status is <see cref="IOCompletionPortDequeueStatus.Succeeded"/>,
            /// meaning that a packet was actually dequeued.
            /// </summary>
            public IntPtr CompletionKey
            {
                get
                {
                    Contract.Requires(Status == IOCompletionPortDequeueStatus.Succeeded);
                    return m_completionKey;
                }
            }

            /// <summary>
            /// Pointer to the overlapped originally used to isse the completed I/O. Available only if the status is <see cref="IOCompletionPortDequeueStatus.Succeeded"/>,
            /// meaning that a packet was actually dequeued.
            /// </summary>
            public Overlapped* DequeuedOverlapped
            {
                get
                {
                    Contract.Requires(Status == IOCompletionPortDequeueStatus.Succeeded);
                    return m_dequeuedOverlapped;
                }
            }
        }

        /// <summary>
        /// Attempts to dequeue a completion packet from a completion port. The result indicates whether or not a packet
        /// was dequeued, and if so the packet's contents.
        /// </summary>
        [SuppressMessage("Microsoft.Interoperability", "CA1404:CallGetLastErrorImmediatelyAfterPInvoke", Justification = "Incorrect analysis")]
        public static unsafe IOCompletionPortDequeueResult GetQueuedCompletionStatus(SafeIOCompletionPortHandle completionPort)
        {
            // Possible indications:
            //   dequeuedOverlapped == null && !result: dequeue failed. Maybe ERROR_ABANDONED_WAIT_0 (port closed)?
            //   dequeuedOverlapped != null && !result: Dequeue succeeded. IO failed.
            //   dequeuedOverlapped != null && result: Dequeue succeeded. IO succeeded.
            //   dequeuedOverlapped == null && result: PostQueuedCompletionStatus with null OVERLAPPED
            // See https://msdn.microsoft.com/en-us/library/windows/desktop/aa364986%28v=vs.85%29.aspx
            Overlapped* dequeuedOverlapped = null;
            int bytesTransferred = 0;
            IntPtr completionKey = default(IntPtr);
            bool result = GetQueuedCompletionStatus(completionPort, &bytesTransferred, &completionKey, &dequeuedOverlapped, NativeIOConstants.Infinite);

            if (result || dequeuedOverlapped != null)
            {
                // Latter three cases; dequeue succeeded.
                int error = NativeIOConstants.ErrorSuccess;
                if (!result)
                {
                    error = Marshal.GetLastWin32Error();
                    Contract.Assume(error != NativeIOConstants.ErrorSuccess);
                }

                return new IOCompletionPortDequeueResult(
                    new FileAsyncIOResult(
                        result ? FileAsyncIOStatus.Succeeded : FileAsyncIOStatus.Failed,
                        // GetQueueCompletionStatus can return false but still store non-0 value into 'bytesTransferred' argument.
                        bytesTransferred: bytesTransferred,
                        error: error),
                    dequeuedOverlapped,
                    completionKey);
            }
            else
            {
                // Dequeue failed: dequeuedOverlapped == null && !result
                int error = Marshal.GetLastWin32Error();

                if (error == NativeIOConstants.ErrorAbandonedWait0)
                {
                    return new IOCompletionPortDequeueResult(IOCompletionPortDequeueStatus.CompletionPortClosed);
                }
                else
                {
                    throw ThrowForNativeFailure(error, "GetQueuedCompletionStatus");
                }
            }
        }

        /// <summary>
        /// Queues a caller-defined completion packet to a completion port.
        /// </summary>
        public static unsafe void PostQueuedCompletionStatus(SafeIOCompletionPortHandle completionPort, IntPtr completionKey)
        {
            if (!PostQueuedCompletionStatus(completionPort, dwNumberOfBytesTransferred: 0, dwCompletionKey: completionKey, lpOverlapped: null))
            {
                throw ThrowForNativeFailure(Marshal.GetLastWin32Error(), "PostQueuedCompletionStatus");
            }
        }

        /// <inheritdoc />
        public CreateHardLinkStatus TryCreateHardLink(string link, string linkTarget)
        {
            bool result = CreateHardLinkW(ToLongPathIfExceedMaxPath(link), ToLongPathIfExceedMaxPath(linkTarget), IntPtr.Zero);
            if (result)
            {
                return CreateHardLinkStatus.Success;
            }

            switch (Marshal.GetLastWin32Error())
            {
                case NativeIOConstants.ErrorNotSameDevice:
                    return CreateHardLinkStatus.FailedSinceDestinationIsOnDifferentVolume;
                case NativeIOConstants.ErrorTooManyLinks:
                    return CreateHardLinkStatus.FailedDueToPerFileLinkLimit;
                case NativeIOConstants.ErrorNotSupported:
                    return CreateHardLinkStatus.FailedSinceNotSupportedByFilesystem;
                case NativeIOConstants.ErrorAccessDenied:
                    return CreateHardLinkStatus.FailedAccessDenied;
                default:
                    return CreateHardLinkStatus.Failed;
            }
        }

        /// <inheritdoc />
        public CreateHardLinkStatus TryCreateHardLinkViaSetInformationFile(string link, string linkTarget, bool replaceExisting = true)
        {
            // Please note, that this method does not support long paths: FileLinkInformation struct hard codes the file name lengths to 264.
            using (FileStream handle = CreateFileStream(linkTarget, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, FileOptions.None, false))
            {
                FileLinkInformation fileLinkInformation = new FileLinkInformation(NtPathPrefix + link, replaceExisting);
                var status = NtSetInformationFile(handle.SafeFileHandle, out _, fileLinkInformation, (uint)Marshal.SizeOf(fileLinkInformation), FileInformationClass.FileLinkInformation);
                var result = Marshal.GetLastWin32Error();
                if (status.IsSuccessful)
                {
                    return CreateHardLinkStatus.Success;
                }
                else
                {
                    switch (result)
                    {
                        case NativeIOConstants.ErrorTooManyLinks:
                            return CreateHardLinkStatus.FailedDueToPerFileLinkLimit;
                        case NativeIOConstants.ErrorNotSameDevice:
                            return CreateHardLinkStatus.FailedSinceDestinationIsOnDifferentVolume;
                        case NativeIOConstants.ErrorAccessDenied:
                            return CreateHardLinkStatus.FailedAccessDenied;
                        case NativeIOConstants.ErrorNotSupported:
                            return CreateHardLinkStatus.FailedSinceNotSupportedByFilesystem;
                        default:
                            return CreateHardLinkStatus.Failed;
                    }
                }
            }
        }

        /// <inheritdoc />
        public Possible<Unit> TryCreateSymbolicLink(string symLinkFileName, string targetFileName, bool isTargetFile)
        {
            SymbolicLinkTarget creationFlag = isTargetFile ? SymbolicLinkTarget.File : SymbolicLinkTarget.Directory;

            if (m_supportUnprivilegedCreateSymbolicLinkFlag.Value)
            {
                creationFlag |= SymbolicLinkTarget.AllowUnprivilegedCreate;
            }

            int res = CreateSymbolicLinkW(symLinkFileName, targetFileName, creationFlag);

            // The return value of CreateSymbolicLinkW is underspecified in its documentation.
            // In non-admin mode where Developer mode is not enabled, the return value can be greater than zero, but the last error
            // is ERROR_PRIVILEGE_NOT_HELD, and consequently the symlink is not created. We strenghten the return value by 
            // also checking that the last error is ERROR_SUCCESS.
            int lastError = Marshal.GetLastWin32Error();
            if (res > 0 && lastError == NativeIOConstants.ErrorSuccess)
            {
                return Unit.Void;
            }

            return new NativeFailure(lastError, I($"{nameof(CreateSymbolicLinkW)} returns '{res}'"));
        }

        /// <inheritdoc />
        public void CreateJunction(string junctionPoint, string targetDir)
        {
            if (!Directory.Exists(ToLongPathIfExceedMaxPath(targetDir)))
            {
                throw new IOException(I($"Target path '{targetDir}' does not exist or is not a directory."));
            }

            SafeFileHandle handle;
            var openReparsePoint = TryOpenReparsePoint(junctionPoint, FileDesiredAccess.GenericWrite, out handle);

            if (!openReparsePoint.Succeeded)
            {
                openReparsePoint.ThrowForError();
            }

            using (handle)
            {
                string fullTargetDirPath = GetFullPath(targetDir, out int hr);

                if (fullTargetDirPath == null)
                {
                    throw CreateWin32Exception(hr, "GetFullPathName");
                }

                byte[] targetDirBytes = Encoding.Unicode.GetBytes(NtPathPrefix + fullTargetDirPath);

                REPARSE_DATA_BUFFER reparseDataBuffer = new REPARSE_DATA_BUFFER
                                                        {
                                                            ReparseTag = DwReserved0Flag.IO_REPARSE_TAG_MOUNT_POINT,
                                                            ReparseDataLength = (ushort)(targetDirBytes.Length + 12),
                                                            SubstituteNameOffset = 0,
                                                            SubstituteNameLength = (ushort)targetDirBytes.Length,
                                                            PrintNameOffset = (ushort)(targetDirBytes.Length + 2),
                                                            PrintNameLength = 0,
                                                            PathBuffer = new byte[0x3ff0],
                                                        };

                Array.Copy(targetDirBytes, reparseDataBuffer.PathBuffer, targetDirBytes.Length);

                int inBufferSize = Marshal.SizeOf(reparseDataBuffer);
                IntPtr inBuffer = Marshal.AllocHGlobal(inBufferSize);

                try
                {
                    Marshal.StructureToPtr(reparseDataBuffer, inBuffer, false);

                    bool success = DeviceIoControl(
                        handle,
                        FSCTL_SET_REPARSE_POINT,
                        inBuffer,
                        targetDirBytes.Length + 20,
                        IntPtr.Zero,
                        0,
                        out _,
                        IntPtr.Zero);

                    if (!success)
                    {
                        throw CreateWin32Exception(Marshal.GetLastWin32Error(), "DeviceIoControl");
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(inBuffer);
                }
            }
        }

        private static OpenFileResult TryOpenReparsePoint(string reparsePoint, FileDesiredAccess accessMode, out SafeFileHandle reparsePointHandle)
        {
            reparsePointHandle = CreateFileW(
                reparsePoint,
                accessMode,
                FileShare.Read | FileShare.Write | FileShare.Delete,
                IntPtr.Zero,
                FileMode.Open,
                FileFlagsAndAttributes.FileFlagBackupSemantics | FileFlagsAndAttributes.FileFlagOpenReparsePoint,
                IntPtr.Zero);

            int hr = Marshal.GetLastWin32Error();

            if (reparsePointHandle.IsInvalid)
            {
                reparsePointHandle = null;
                Contract.Assume(hr != 0);
                var result = OpenFileResult.Create(reparsePoint, hr, FileMode.Open, handleIsValid: false);
                Contract.Assume(!result.Succeeded);
                return result;
            }
            else
            {
                var result = OpenFileResult.Create(reparsePoint, hr, FileMode.Open, handleIsValid: true);
                Contract.Assume(result.Succeeded);
                return result;
            }
        }

        /// <inheritdoc />
        public List<Tuple<VolumeGuidPath, ulong>> ListVolumeGuidPathsAndSerials()
        {
            Contract.Ensures(Contract.Result<List<Tuple<VolumeGuidPath, ulong>>>().Count > 0);
            Contract.Ensures(Contract.ForAll(Contract.Result<List<Tuple<VolumeGuidPath, ulong>>>(), t => t.Item1.IsValid));

            var volumeList = new List<Tuple<VolumeGuidPath, ulong>>();

            // We don't want funky message boxes for poking removable media, e.g. a CD drive without a disk.
            // By observation, these drives *may* be returned when enumerating volumes. Run 'wmic volume get DeviceId,Name'
            // when an empty floppy / cd drive is visible in explorer.
            using (ErrorModeContext.DisableMessageBoxForRemovableMedia())
            {
                var volumeNameBuffer = new StringBuilder(capacity: NativeIOConstants.MaxPath + 1);
                using (SafeFindVolumeHandle findVolumeHandle = FindFirstVolumeW(volumeNameBuffer, volumeNameBuffer.Capacity))
                {
                    {
                        int hr = Marshal.GetLastWin32Error();

                        // The docs say we'll see an invalid handle if it 'fails to find any volumes'. It's very hard to run this program without a volume, though.
                        // http://msdn.microsoft.com/en-us/library/windows/desktop/aa364425(v=vs.85).aspx
                        if (findVolumeHandle.IsInvalid)
                        {
                            throw ThrowForNativeFailure(hr, "FindNextVolumeW");
                        }
                    }

                    do
                    {
                        string volumeGuidPathString = volumeNameBuffer.ToString();
                        volumeNameBuffer.Clear();

                        Contract.Assume(!string.IsNullOrEmpty(volumeGuidPathString) && volumeGuidPathString[volumeGuidPathString.Length - 1] == '\\');
                        bool volumeGuidPathParsed = VolumeGuidPath.TryCreate(volumeGuidPathString, out VolumeGuidPath volumeGuidPath);
                        Contract.Assume(volumeGuidPathParsed, "FindFirstVolume / FindNextVolume promise to return volume GUID paths");

                        if (TryOpenDirectory(volumeGuidPathString, FileShare.Delete | FileShare.Read | FileShare.Write, out SafeFileHandle volumeRoot).Succeeded)
                        {
                            ulong serial;
                            using (volumeRoot)
                            {
                                serial = GetVolumeSerialNumberByHandle(volumeRoot);
                            }

                            Logger.Log.StorageFoundVolume(Events.StaticContext, volumeGuidPathString, serial);
                            volumeList.Add(Tuple.Create(volumeGuidPath, serial));
                        }
                    }
                    while (FindNextVolumeW(findVolumeHandle, volumeNameBuffer, volumeNameBuffer.Capacity));

                    // FindNextVolumeW returned false; hopefully for the right reason.
                    {
                        int hr = Marshal.GetLastWin32Error();
                        if (hr != NativeIOConstants.ErrorNoMoreFiles)
                        {
                            throw ThrowForNativeFailure(hr, "FindNextVolumeW");
                        }
                    }
                }
            }

            return volumeList;
        }

        /// <inheritdoc />
        public OpenFileResult TryOpenFileById(
            SafeFileHandle existingHandleOnVolume,
            FileId fileId,
            FileDesiredAccess desiredAccess,
            FileShare shareMode,
            FileFlagsAndAttributes flagsAndAttributes,
            out SafeFileHandle handle)
        {
            Contract.Requires(existingHandleOnVolume != null && !existingHandleOnVolume.IsInvalid);

            var fileIdDescriptor = new FileIdDescriptor(fileId);
            handle = OpenFileById(
                existingHandleOnVolume,
                fileIdDescriptor,
                desiredAccess,
                shareMode,
                lpSecurityAttributes: IntPtr.Zero,
                dwFlagsAndAttributes: flagsAndAttributes);
            int hr = Marshal.GetLastWin32Error();

            if (handle.IsInvalid)
            {
                Logger.Log.StorageTryOpenFileByIdFailure(Events.StaticContext, fileId.High, fileId.Low, GetVolumeSerialNumberByHandle(existingHandleOnVolume), hr);
                handle = null;
                Contract.Assume(hr != 0);
                
                var result = OpenFileResult.CreateForOpeningById(hr, FileMode.Open, handleIsValid: false);
                Contract.Assume(!result.Succeeded);
                return result;
            }
            else
            {
                var result = OpenFileResult.CreateForOpeningById(hr, FileMode.Open, handleIsValid: true);
                Contract.Assume(result.Succeeded);
                return result;
            }
        }

        // SymLink target support
        // Constants
        private const int INITIAL_REPARSE_DATA_BUFFER_SIZE = 1024;
        private const int FSCTL_GET_REPARSE_POINT = 0x000900a8;

        private const int ERROR_INSUFFICIENT_BUFFER = 0x7A;
        private const int ERROR_MORE_DATA = 0xEA;
        private const int ERROR_SUCCESS = 0x0;
        private const int SYMLINK_FLAG_RELATIVE = 0x1;

        /// <inheritdoc />
        public void GetChainOfReparsePoints(SafeFileHandle handle, string sourcePath, IList<string> chainOfReparsePoints)
        {
            Contract.Requires(!handle.IsInvalid);
            Contract.Requires(!string.IsNullOrWhiteSpace(sourcePath));
            Contract.Requires(chainOfReparsePoints != null);

            SafeFileHandle originalHandle = handle;
            chainOfReparsePoints.Add(sourcePath);

            do
            {
                if (!TryGetFileAttributes(sourcePath, out FileAttributes attributes, out int hr))
                {
                    if (handle != originalHandle)
                    {
                        handle.Dispose();
                    }

                    return;
                }

                if ((attributes & FileAttributes.ReparsePoint) == 0)
                {
                    if (handle != originalHandle)
                    {
                        handle.Dispose();
                    }

                    return;
                }

                var possibleNextTarget = TryGetReparsePointTarget(handle, sourcePath);

                if (!possibleNextTarget.Succeeded)
                {
                    if (handle != originalHandle)
                    {
                        handle.Dispose();
                    }

                    return;
                }

                if (handle != originalHandle)
                {
                    handle.Dispose();
                }

                var maybeResolvedTarget = ResolveSymlinkTarget(sourcePath, possibleNextTarget.Result);

                if (!maybeResolvedTarget.Succeeded)
                {
                    return;
                }

                sourcePath = maybeResolvedTarget.Result;
                chainOfReparsePoints.Add(sourcePath);

                var openResult = TryOpenDirectory(
                        sourcePath,
                        FileDesiredAccess.GenericRead,
                        FileShare.ReadWrite | FileShare.Delete,
                        FileFlagsAndAttributes.FileFlagOverlapped | FileFlagsAndAttributes.FileFlagOpenReparsePoint,
                        out handle);

                if (!openResult.Succeeded)
                {
                    return;
                }

            } while (!handle.IsInvalid);
        }

        /// <inheritdoc />
        public Possible<string> TryGetReparsePointTarget(SafeFileHandle handle, string sourcePath)
        {
            try
            {
                if (handle == null || handle.IsInvalid)
                {
                    var openResult = TryCreateOrOpenFile(
                        sourcePath,
                        FileDesiredAccess.GenericRead,
                        FileShare.Read | FileShare.Delete,
                        FileMode.Open,
                        FileFlagsAndAttributes.FileFlagOpenReparsePoint | FileFlagsAndAttributes.FileFlagBackupSemantics,
                        out SafeFileHandle symlinkHandle);

                    if (!openResult.Succeeded)
                    {
                        return openResult.CreateFailureForError();
                    }

                    using (symlinkHandle)
                    {
                        return GetReparsePointTarget(symlinkHandle);
                    }
                }
                else
                {
                    return GetReparsePointTarget(handle);
                }
            }
            catch (NativeWin32Exception e)
            {
                return new RecoverableExceptionFailure(new BuildXLException("Failed to get reparse point target", e));
            }
            catch (NotSupportedException e)
            {
                return new RecoverableExceptionFailure(new BuildXLException("Failed to get reparse point target", e));
            }
        }

        private unsafe string GetReparsePointTarget(SafeFileHandle handle)
        {
            string targetPath = string.Empty;

            int bufferSize = INITIAL_REPARSE_DATA_BUFFER_SIZE;
            int errorCode = ERROR_INSUFFICIENT_BUFFER;

            byte[] buffer = null;
            while (errorCode == ERROR_MORE_DATA || errorCode == ERROR_INSUFFICIENT_BUFFER)
            {
                buffer = new byte[bufferSize];
                bool success = false;

                fixed (byte* pBuffer = buffer)
                {
                    int bufferReturnedSize;
                    success = DeviceIoControl(
                        handle,
                        FSCTL_GET_REPARSE_POINT,
                        IntPtr.Zero,
                        0,
                        (IntPtr)pBuffer,
                        bufferSize,
                        out bufferReturnedSize,
                        IntPtr.Zero);
                }

                bufferSize *= 2;
                errorCode = success ? 0 : Marshal.GetLastWin32Error();
            }

            if (errorCode != 0)
            {
                throw ThrowForNativeFailure(errorCode, "DeviceIoControl(FSCTL_GET_REPARSE_POINT)");
            }

            // Now get the offsets in the REPARSE_DATA_BUFFER buffer string based on
            // the offsets for the different type of reparse points.

            const uint PrintNameOffsetIndex = 12;
            const uint PrintNameLengthIndex = 14;
            const uint SubsNameOffsetIndex = 8;
            const uint SubsNameLengthIndex = 10;

            fixed (byte* pBuffer = buffer)
            {
                uint reparsePointTag = *(uint*)(pBuffer);

                if (reparsePointTag != (uint)DwReserved0Flag.IO_REPARSE_TAG_SYMLINK
                    && reparsePointTag != (uint)DwReserved0Flag.IO_REPARSE_TAG_MOUNT_POINT)
                {
                    throw new NotSupportedException(I($"Reparse point tag {reparsePointTag:X} not supported"));
                }

                uint pathBufferOffsetIndex = (uint)((reparsePointTag == (uint) DwReserved0Flag.IO_REPARSE_TAG_SYMLINK) ? 20 : 16);
                char* nameStartPtr = (char*)(pBuffer + pathBufferOffsetIndex);
                int nameOffset = *(short*)(pBuffer + PrintNameOffsetIndex) / 2;
                int nameLength = *(short*)(pBuffer + PrintNameLengthIndex) / 2;
                targetPath = new string(nameStartPtr, nameOffset, nameLength);

                if (string.IsNullOrWhiteSpace(targetPath))
                {
                    nameOffset = *(short*)(pBuffer + SubsNameOffsetIndex) / 2;
                    nameLength = *(short*)(pBuffer + SubsNameLengthIndex) / 2;
                    targetPath = new string(nameStartPtr, nameOffset, nameLength);
                }
            }

            return targetPath;
        }

        /// <inheritdoc />
        public string GetFinalPathNameByHandle(SafeFileHandle handle, bool volumeGuidPath = false)
        {
            const int VolumeNameGuid = 0x1;

            var pathBuffer = new StringBuilder(NativeIOConstants.MaxPath);

            int neededSize = NativeIOConstants.MaxPath;
            do
            {
                pathBuffer.EnsureCapacity(neededSize);
                neededSize = GetFinalPathNameByHandleW(handle, pathBuffer, pathBuffer.Capacity, flags: volumeGuidPath ? VolumeNameGuid : 0);
                if (neededSize == 0)
                {
                    int hr = Marshal.GetLastWin32Error();

                    // ERROR_PATH_NOT_FOUND
                    if (hr == 0x3)
                    {
                        // This can happen if the volume
                        Contract.Assume(!volumeGuidPath);
                        return GetFinalPathNameByHandle(handle, volumeGuidPath: true);
                    }
                    else
                    {
                        throw ThrowForNativeFailure(hr, "GetFinalPathNameByHandleW");
                    }
                }

                Contract.Assume(neededSize < NativeIOConstants.MaxLongPath);
            }
            while (neededSize > pathBuffer.Capacity);

            const string ExpectedPrefix = LongPathPrefix;
            Contract.Assume(pathBuffer.Length >= ExpectedPrefix.Length, "Expected a long-path prefix");
            for (int i = 0; i < ExpectedPrefix.Length; i++)
            {
                Contract.Assume(pathBuffer[i] == ExpectedPrefix[i], "Expected a long-path prefix");
            }

            if (volumeGuidPath)
            {
                return pathBuffer.ToString();
            }
            else
            {
                return pathBuffer.ToString(startIndex: ExpectedPrefix.Length, length: pathBuffer.Length - ExpectedPrefix.Length);
            }
        }

        /// <inheritdoc />
        public bool TryRemoveDirectory(
            string path,
            out int hr)
        {
            Contract.Ensures(Contract.Result<bool>() ^ Contract.ValueAtReturn(out hr) != 0);

            if (!RemoveDirectoryW(ToLongPathIfExceedMaxPath(path)))
            {
                hr = Marshal.GetLastWin32Error();
                return false;
            }

            hr = 0;
            return true;
        }

        /// <inheritdoc />
        public void RemoveDirectory(string path)
        {
            if (!TryRemoveDirectory(path, out int hr))
            {
                ThrowForNativeFailure(hr, "RemoveDirectoryW");
            }
        }

        /// <summary>
        /// Thin wrapper for native SetFileAttributesW that checks the win32 error upon failure
        /// </summary>
        public bool TrySetFileAttributes(string path, FileAttributes attributes, out int hr)
        {
            Contract.Ensures(Contract.Result<bool>() ^ Contract.ValueAtReturn(out hr) != 0);

            if (!SetFileAttributesW(ToLongPathIfExceedMaxPath(path), attributes))
            {
                hr = Marshal.GetLastWin32Error();
                return false;
            }

            hr = 0;
            return true;
        }

        /// <inheritdoc />
        public void SetFileAttributes(string path, FileAttributes attributes)
        {
            if (!TrySetFileAttributes(path, attributes, out int hr))
            {
                ThrowForNativeFailure(hr, "SetFileAttributesW");
            }
        }

        private bool TryGetFileAttributes(string path, out FileAttributes attributes, out int hr)
        {
            return TryGetFileAttributesViaGetFileAttributes(path, out attributes, out hr)
                || TryGetFileAttributesViaFindFirstFile(path, out attributes, out hr);
        }

        private bool TryGetFileAttributesViaGetFileAttributes(string path, out FileAttributes attributes, out int hr)
        {
            Contract.Ensures(Contract.Result<bool>() ^ Contract.ValueAtReturn<int>(out hr) != 0);

            var fileAttributes = GetFileAttributesW(ToLongPathIfExceedMaxPath(path));

            if (fileAttributes == NativeIOConstants.InvalidFileAttributes)
            {
                hr = Marshal.GetLastWin32Error();
                attributes = FileAttributes.Normal;
                return false;
            }

            hr = 0;
            attributes = (FileAttributes)fileAttributes;
            return true;
        }

        private bool TryGetFileAttributesViaFindFirstFile(string path, out FileAttributes attributes, out int hr)
        {
            WIN32_FIND_DATA findResult;

            using (SafeFindFileHandle findHandle = FindFirstFileW(ToLongPathIfExceedMaxPath(path), out findResult))
            {
                if (findHandle.IsInvalid)
                {
                    hr = Marshal.GetLastWin32Error();
                    attributes = FileAttributes.Normal;
                    return false;
                }

                hr = 0;
                attributes = findResult.DwFileAttributes;
                return true;
            }
        }

        /// <inheritdoc />
        public Possible<PathExistence, NativeFailure> TryProbePathExistence(string path, bool followSymlink)
        {
            if (!TryGetFileAttributesViaGetFileAttributes(path, out FileAttributes fileAttributes, out int hr))
            {
                if (IsHresultNonesixtent(hr))
                {
                    return PathExistence.Nonexistent;
                }
                else
                {
                    // Fall back using more expensive FindFirstFile.
                    // Getting file attributes for probing file existence with GetFileAttributesW sometimes results in "access denied". 
                    // This causes problem especially during file materialization. Because such a probe is interpreted as probing non-existent path, 
                    // the materialization target is not deleted. However, cache, using .NET File.Exist, is able to determine that the file exists. 
                    // Thus, cache refuses to materialize the file
                    if (!TryGetFileAttributesViaFindFirstFile(path, out fileAttributes, out hr))
                    {
                        if (IsHresultNonesixtent(hr))
                        {
                            return PathExistence.Nonexistent;
                        }
                        else
                        {
                            return new NativeFailure(hr);
                        }
                    }
                }
            }

            var attrs = checked((FileAttributes)fileAttributes);
            bool hasDirectoryFlag = ((attrs & FileAttributes.Directory) != 0);

            if (followSymlink)
            {
                // when following symlinks --> implement the same behavior as .NET File.Exists() and Directory.Exists()
                return hasDirectoryFlag
                    ? PathExistence.ExistsAsDirectory
                    : PathExistence.ExistsAsFile;
            }
            else
            {
                // when not following symlinks --> treat symlinks as files regardless of what they point to
                bool hasSymlinkFlag = ((attrs & FileAttributes.ReparsePoint) != 0);
                return
                    hasSymlinkFlag ? PathExistence.ExistsAsFile :
                    hasDirectoryFlag ? PathExistence.ExistsAsDirectory :
                    PathExistence.ExistsAsFile;
            }
        }

        /// <summary>
        /// Gets file name.
        /// </summary>
        public string GetFileName(string path)
        {
            WIN32_FIND_DATA findResult;

            using (SafeFindFileHandle findHandle = FindFirstFileW(ToLongPathIfExceedMaxPath(path), out findResult))
            {
                if (!findHandle.IsInvalid)
                {
                    return findResult.CFileName;
                }

                ThrowForNativeFailure(Marshal.GetLastWin32Error(), nameof(FindFirstFileW));
            }

            return null;
        }

        public bool PathMatchPattern(string path, string pattern)
        {
            return PathMatchSpecW(path, pattern);
        }

        /// <inheritdoc />
        public unsafe uint GetHardLinkCountByHandle(SafeFileHandle handle)
        {
            var info = default(FileStandardInfo);

            if (!GetFileInformationByHandleEx(handle, (uint)FileInfoByHandleClass.FileStandardInfo, (IntPtr)(&info), sizeof(FileStandardInfo)))
            {
                ThrowForNativeFailure(Marshal.GetLastWin32Error(), nameof(GetFileInformationByHandleEx));
            }

            return info.NumberOfLinks;
        }

        /// <inheritdoc />
        public FileAttributes GetFileAttributes(string path)
        {
            if (!TryGetFileAttributes(path, out FileAttributes attributes, out int hr))
            {
                ThrowForNativeFailure(hr, "FindFirstFileW", nameof(GetFileAttributes));
            }

            return attributes;
        }

        /// <inheritdoc />
        public EnumerateDirectoryResult EnumerateDirectoryEntries(
            string directoryPath,
            bool recursive,
            Action<string /*filePath*/, string /*fileName*/, FileAttributes /*attributes*/> handleEntry,
            bool isEnumerationForDirectoryDeletion = false)
        {
            return EnumerateDirectoryEntries(directoryPath, recursive, "*", handleEntry, isEnumerationForDirectoryDeletion);
        }

        /// <inheritdoc />
        public EnumerateDirectoryResult EnumerateDirectoryEntries(
            string directoryPath,
            bool recursive,
            string pattern,
            Action<string /*filePath*/, string /*fileName*/, FileAttributes /*attributes*/> handleEntry,
            bool isEnumerationForDirectoryDeletion = false,
            bool followSymlinksToDirectories = false)
        {
            // directoryPath may be passed by users, so don't modify it (e.g., TrimEnd '\') because it's going to be part of the returned result.
            var searchDirectoryPath = Path.Combine(ToLongPathIfExceedMaxPath(directoryPath), "*");

            using (SafeFindFileHandle findHandle = FindFirstFileW(searchDirectoryPath, out WIN32_FIND_DATA findResult))
            {
                if (findHandle.IsInvalid)
                {
                    int hr = Marshal.GetLastWin32Error();
                    Contract.Assume(hr != NativeIOConstants.ErrorSuccess);
                    return EnumerateDirectoryResult.CreateFromHResult(directoryPath, hr);
                }

                while (true)
                {
                    // There will be entries for the current and parent directories. Ignore those.
                    if (((findResult.DwFileAttributes & FileAttributes.Directory) == 0) ||
                        (findResult.CFileName != "." && findResult.CFileName != ".."))
                    {
                        if (PathMatchSpecW(findResult.CFileName, pattern))
                        {
                            handleEntry(directoryPath, findResult.CFileName, findResult.DwFileAttributes);
                        }

                        if (recursive && (findResult.DwFileAttributes & FileAttributes.Directory) != 0)
                        {
                            var recursiveResult = EnumerateDirectoryEntries(
                                Path.Combine(directoryPath, findResult.CFileName),
                                recursive: true,
                                pattern,
                                handleEntry: handleEntry);

                            if (!recursiveResult.Succeeded)
                            {
                                return recursiveResult;
                            }
                        }
                    }

                    if (!FindNextFileW(findHandle, out findResult))
                    {
                        int hr = Marshal.GetLastWin32Error();
                        if (hr == NativeIOConstants.ErrorNoMoreFiles)
                        {
                            // Graceful completion of enumeration.
                            return new EnumerateDirectoryResult(directoryPath, EnumerateDirectoryStatus.Success, hr);
                        }
                        else
                        {
                            Contract.Assume(hr != NativeIOConstants.ErrorSuccess);

                            // Maybe we can fail ACLs in the middle of enumerating. Do we nead FILE_READ_ATTRIBUTES on each file? That would be surprising
                            // since the security descriptors aren't in the directory file. All other canonical statuses have to do with beginning enumeration
                            // rather than continuing (can we open the search directory?)
                            // So, let's assume that this failure is esoteric and use the 'unknown error' catchall.
                            return new EnumerateDirectoryResult(directoryPath, EnumerateDirectoryStatus.UnknownError, hr);
                        }
                    }
                }
            }
        }

        private EnumerateDirectoryResult EnumerateEntries(
            string directoryPath,
            bool recursive,
            string pattern,
            Action<string /*filePath*/, string /*fileName*/, FileAttributes /*attributes*/, long /*fileSize*/> handleFileEntry)
        {
            // directoryPath may be passed by users, so don't modify it (e.g., TrimEnd '\') because it's going to be part of the returned result.
            var searchDirectoryPath = Path.Combine(ToLongPathIfExceedMaxPath(directoryPath), "*");

            using (SafeFindFileHandle findHandle = FindFirstFileW(searchDirectoryPath, out WIN32_FIND_DATA findResult))
            {
                if (findHandle.IsInvalid)
                {
                    int hr = Marshal.GetLastWin32Error();
                    Contract.Assume(hr != NativeIOConstants.ErrorSuccess);
                    return EnumerateDirectoryResult.CreateFromHResult(directoryPath, hr);
                }

                while (true)
                {
                    // There will be entries for the current and parent directories. Ignore those.
                    if (((findResult.DwFileAttributes & FileAttributes.Directory) == 0) ||
                        (findResult.CFileName != "." && findResult.CFileName != ".."))
                    {
                        if (PathMatchSpecW(findResult.CFileName, pattern))
                        {
                            handleFileEntry(directoryPath, findResult.CFileName, findResult.DwFileAttributes, findResult.GetFileSize());
                        }

                        if (recursive && (findResult.DwFileAttributes & FileAttributes.Directory) != 0)
                        {
                            var recursiveResult = EnumerateFiles(
                                Path.Combine(directoryPath, findResult.CFileName),
                                recursive: true,
                                pattern,
                                handleFileEntry: handleFileEntry);

                            if (!recursiveResult.Succeeded)
                            {
                                return recursiveResult;
                            }
                        }
                    }

                    if (!FindNextFileW(findHandle, out findResult))
                    {
                        int hr = Marshal.GetLastWin32Error();
                        if (hr == NativeIOConstants.ErrorNoMoreFiles)
                        {
                            // Graceful completion of enumeration.
                            return new EnumerateDirectoryResult(directoryPath, EnumerateDirectoryStatus.Success, hr);
                        }
                        else
                        {
                            Contract.Assume(hr != NativeIOConstants.ErrorSuccess);

                            // Maybe we can fail ACLs in the middle of enumerating. Do we need FILE_READ_ATTRIBUTES on each file? That would be surprising
                            // since the security descriptors aren't in the directory file. All other canonical statuses have to do with beginning enumeration
                            // rather than continuing (can we open the search directory?)
                            // So, let's assume that this failure is esoteric and use the 'unknown error' catchall.
                            return new EnumerateDirectoryResult(directoryPath, EnumerateDirectoryStatus.UnknownError, hr);
                        }
                    }
                }
            }
        }

        /// <inheritdoc />
        public EnumerateDirectoryResult EnumerateFiles(
            string directoryPath,
            bool recursive,
            string pattern,
            Action<string /*filePath*/, string /*fileName*/, FileAttributes /*attributes*/, long /*fileSize*/> handleFileEntry)
        {
            return EnumerateEntries(directoryPath, recursive, pattern,
                (filePath, fileName, attributes, fileSize) =>
                {
                    if ((attributes & FileAttributes.Directory) == 0)
                    {
                        handleFileEntry(filePath, fileName, attributes, fileSize);
                    }
                });
        }

        /// <inheritdoc />
        public EnumerateDirectoryResult EnumerateDirectoryEntries(
            string directoryPath,
            bool enumerateDirectory,
            string pattern,
            uint directoriesToSkipRecursively,
            bool recursive,
            IDirectoryEntriesAccumulator accumulators,
            bool isEnumerationForDirectoryDeletion = false)
        {
            // directoryPath may be passed by users, so don't modify it (e.g., TrimEnd '\') because it's going to be part of the returned result.
            var searchDirectoryPath = Path.Combine(ToLongPathIfExceedMaxPath(directoryPath), "*");

            using (SafeFindFileHandle findHandle = FindFirstFileW(searchDirectoryPath, out WIN32_FIND_DATA findResult))
            {
                if (findHandle.IsInvalid)
                {
                    int hr = Marshal.GetLastWin32Error();
                    Contract.Assume(hr != NativeIOConstants.ErrorFileNotFound);
                    var result = EnumerateDirectoryResult.CreateFromHResult(directoryPath, hr);
                    accumulators.Current.Succeeded = false;
                    return result;
                }

                var accumulator = accumulators.Current;

                while (true)
                {
                    bool isDirectory = (findResult.DwFileAttributes & FileAttributes.Directory) != 0;

                    // There will be entries for the current and parent directories. Ignore those.
                    if (!isDirectory || (findResult.CFileName != "." && findResult.CFileName != ".."))
                    {
                        if (PathMatchSpecW(findResult.CFileName, pattern))
                        {
                            if (!(enumerateDirectory ^ isDirectory) && directoriesToSkipRecursively == 0)
                            {
                                accumulator.AddFile(findResult.CFileName);
                            }
                        }

                        accumulator.AddTrackFile(findResult.CFileName, findResult.DwFileAttributes);

                        if ((recursive || directoriesToSkipRecursively > 0) && isDirectory)
                        {
                            accumulators.AddNew(accumulator, findResult.CFileName);
                            var recurs = EnumerateDirectoryEntries(
                                Path.Combine(directoryPath, findResult.CFileName),
                                enumerateDirectory,
                                pattern,
                                directoriesToSkipRecursively == 0 ? 0 : directoriesToSkipRecursively - 1,
                                recursive,
                                accumulators);

                            if (!recurs.Succeeded)
                            {
                                return recurs;
                            }
                        }
                    }

                    if (!FindNextFileW(findHandle, out findResult))
                    {
                        int hr = Marshal.GetLastWin32Error();
                        if (hr == NativeIOConstants.ErrorNoMoreFiles)
                        {
                            // Graceful completion of enumeration.
                            return new EnumerateDirectoryResult(
                                directoryPath,
                                EnumerateDirectoryStatus.Success,
                                hr);
                        }

                        Contract.Assume(hr != NativeIOConstants.ErrorSuccess);
                        return new EnumerateDirectoryResult(
                            directoryPath,
                            EnumerateDirectoryStatus.UnknownError,
                            hr);
                    }
                }
            }
        }

        /// <inheritdoc />
        public FileFlagsAndAttributes GetFileFlagsAndAttributesForPossibleReparsePoint(string expandedPath)
        {
            Possible<ReparsePointType> reparsePointType = TryGetReparsePointType(expandedPath);
            var isActionableReparsePoint = false;

            if (reparsePointType.Succeeded)
            {
                isActionableReparsePoint = IsReparsePointActionable(reparsePointType.Result);
            }

            var openFlags = FileFlagsAndAttributes.FileFlagOverlapped;

            if (isActionableReparsePoint)
            {
                openFlags = openFlags | FileFlagsAndAttributes.FileFlagOpenReparsePoint;
            }

            return openFlags;
        }

        /// <inheritdoc />
        public EnumerateDirectoryResult EnumerateDirectoryEntries(string directoryPath, Action<string, FileAttributes> handleEntry, bool isEnumerationForDirectoryDeletion = false)
        {
            return EnumerateDirectoryEntries(directoryPath, recursive: false, handleEntry: (currentDirectory, fileName, fileAttributes) => handleEntry(fileName, fileAttributes), isEnumerationForDirectoryDeletion);
        }

        /// <summary>
        /// Throws an exception for the unexpected failure of a native API.
        /// </summary>
        /// <remarks>
        /// We don't want native failure checks erased at any contract-rewriting setting.
        /// The return type is <see cref="Exception"/> to facilitate a pattern of <c>throw ThrowForNativeFailure(...)</c> which informs csc's flow control analysis.
        /// </remarks>
        internal static Exception ThrowForNativeFailure(int error, string nativeApiName, [CallerMemberName] string managedApiName = "<unknown>")
        {
            Contract.Requires(!string.IsNullOrEmpty(nativeApiName) && !string.IsNullOrEmpty(managedApiName));

            throw CreateWin32Exception(error, nativeApiName, managedApiName);
        }

        /// <summary>
        /// Creates a Win32 exception for an HResult
        /// </summary>
        internal static NativeWin32Exception CreateWin32Exception(int error, string nativeApiName, [CallerMemberName] string managedApiName = "<unknown>")
        {
            Contract.Requires(!string.IsNullOrEmpty(nativeApiName) && !string.IsNullOrEmpty(managedApiName));

            return new NativeWin32Exception(error, I($"{nativeApiName} for {managedApiName} failed"));
        }

        /// <summary>
        /// Throws an exception for the unexpected failure of a native API.
        /// </summary>
        /// <remarks>
        /// We don't want native failure checks erased at any contract-rewriting setting.
        /// The return type is <see cref="Exception"/> to facilitate a pattern of <c>throw ThrowForNativeFailure(...)</c> which informs csc's flow control analysis.
        /// </remarks>
        internal static Exception ThrowForNativeFailure(NtStatus status, string nativeApiName, [CallerMemberName] string managedApiName = "<unknown>")
        {
            Contract.Requires(!string.IsNullOrEmpty(nativeApiName) && !string.IsNullOrEmpty(managedApiName));

            throw CreateNtException(status, nativeApiName, managedApiName);
        }

        /// <summary>
        /// Creates an NT exception for an NTSTATUS
        /// </summary>
        internal static NativeNtException CreateNtException(NtStatus status, string nativeApiName, [CallerMemberName] string managedApiName = "<unknown>")
        {
            Contract.Requires(!string.IsNullOrEmpty(nativeApiName) && !string.IsNullOrEmpty(managedApiName));

            return new NativeNtException(status, I($"{nativeApiName} for {managedApiName} failed"));
        }

        /// <inheritdoc />
        public bool TryReadSeekPenaltyProperty(SafeFileHandle driveHandle, out bool hasSeekPenalty, out int error)
        {
            Contract.Requires(driveHandle != null);
            Contract.Requires(!driveHandle.IsInvalid);

            hasSeekPenalty = true;
            STORAGE_PROPERTY_QUERY storagePropertyQuery = default(STORAGE_PROPERTY_QUERY);
            storagePropertyQuery.PropertyId = StorageDeviceSeekPenaltyProperty;
            storagePropertyQuery.QueryType = PropertyStandardQuery;

            DEVICE_SEEK_PENALTY_DESCRIPTOR seekPropertyDescriptor = default(DEVICE_SEEK_PENALTY_DESCRIPTOR);

            bool ioctlSuccess = DeviceIoControl(
                driveHandle,
                IOCTL_STORAGE_QUERY_PROPERTY,
                ref storagePropertyQuery,
                Marshal.SizeOf<STORAGE_PROPERTY_QUERY>(),
                out seekPropertyDescriptor,
                Marshal.SizeOf<DEVICE_SEEK_PENALTY_DESCRIPTOR>(),
                out uint bytesReturned,
                IntPtr.Zero);
            error = Marshal.GetLastWin32Error();

            if (ioctlSuccess)
            {
                Contract.Assume(bytesReturned >= Marshal.SizeOf<DEVICE_SEEK_PENALTY_DESCRIPTOR>(), "Query returned fewer bytes than length of output data");
                hasSeekPenalty = seekPropertyDescriptor.IncursSeekPenalty;
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <inheritdoc />
        public bool IsReparsePointActionable(ReparsePointType reparsePointType)
        {
            return reparsePointType == ReparsePointType.SymLink || reparsePointType == ReparsePointType.MountPoint;
        }

        /// <inheritdoc />
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA2204:Literals should be spelled correctly", MessageId = "GetFileAttributesW")]
        public Possible<ReparsePointType> TryGetReparsePointType(string path)
        {
            if (!TryGetFileAttributes(path, out FileAttributes attributes, out int hr))
            {
                return new Possible<ReparsePointType>(new NativeFailure(hr));
            }

            if ((attributes & FileAttributes.ReparsePoint) == 0)
            {
                return ReparsePointType.None;
            }

            using (SafeFindFileHandle findHandle = FindFirstFileW(ToLongPathIfExceedMaxPath(path), out WIN32_FIND_DATA findResult))
            {
                if (!findHandle.IsInvalid)
                {
                    if (findResult.DwReserved0 == (uint)DwReserved0Flag.IO_REPARSE_TAG_SYMLINK ||
                        findResult.DwReserved0 == (uint)DwReserved0Flag.IO_REPARSE_TAG_MOUNT_POINT)
                    {
                        return findResult.DwReserved0 == (uint)DwReserved0Flag.IO_REPARSE_TAG_SYMLINK
                            ? ReparsePointType.SymLink
                            : ReparsePointType.MountPoint;
                    }

                    return ReparsePointType.NonActionable;
                }
            }

            return ReparsePointType.None;
        }

        /// <inheritdoc/>
        public bool IsWciReparseArtifact(string path)
        {
            return IsWCIReparsePointWithTag(path, DwReserved0Flag.IO_REPARSE_TAG_WCIFS, DwReserved0Flag.IO_REPARSE_TAG_WCIFS_TOMBSTONE);
        }

        /// <inheritdoc/>
        public bool IsWciReparsePoint(string path)
        {
            return IsWCIReparsePointWithTag(path, DwReserved0Flag.IO_REPARSE_TAG_WCIFS);
        }

        /// <inheritdoc/>
        public bool IsWciTombstoneFile(string path)
        {
            return IsWCIReparsePointWithTag(path, DwReserved0Flag.IO_REPARSE_TAG_WCIFS_TOMBSTONE);
        }

        /// <summary>
        /// Whether the given path contains any of the given tags
        /// </summary>
        private bool IsWCIReparsePointWithTag(string path, DwReserved0Flag tag1, DwReserved0Flag tag2 = DwReserved0Flag.IO_REPARSE_TAG_RESERVED_ZERO)
        {
            Contract.Requires(!string.IsNullOrEmpty(path));

            // GetFileAttributes doesn't seem to see WCI reparse points as such. So we go to FindFirstFile
            // directly
            using (SafeFindFileHandle findHandle = FindFirstFileW(ToLongPathIfExceedMaxPath(path), out WIN32_FIND_DATA findResult))
            {
                if (!findHandle.IsInvalid)
                {
                    return
                        (findResult.DwFileAttributes & FileAttributes.ReparsePoint) != 0 &&
                        (findResult.DwReserved0 == (uint)tag1 || (tag2 == DwReserved0Flag.IO_REPARSE_TAG_RESERVED_ZERO || findResult.DwReserved0 == (uint)tag2));
                }

                return false;
            }
        }

        [DllImport("ntdll.dll", ExactSpelling = true)]
        internal static extern NtStatus NtSetInformationFile(
            SafeFileHandle fileHandle,
            out IoStatusBlock ioStatusBlock,
#pragma warning disable 0618
            [MarshalAs(UnmanagedType.AsAny)] object fileInformation,
#pragma warning restore 0618
            uint length,
            FileInformationClass fileInformationClass);

        /// <inheritdoc/>
        public bool IsVolumeMapped(string volume)
        {
            Contract.Requires(!string.IsNullOrEmpty(volume));

            // QueryDosDevice needs a volume name without trailing slashes
            volume = volume.TrimEnd('\\');

            var sb = new StringBuilder(259);
            if (QueryDosDevice(volume, sb, sb.Capacity) != 0)
            {
                // If the volume was mapped, then it starts with '\??\'
                return sb.ToString().StartsWith(NtPathPrefix);
            }

            // QueryDosDevice failed, so we assume this is not a mapped volume
            // TODO: consider logging this case
            return false;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int QueryDosDevice(string devname, StringBuilder buffer, int bufSize);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal readonly struct FileLinkInformation
        {
            private readonly byte m_replaceIfExists;
            private readonly IntPtr m_rootDirectoryHandle;
            private readonly uint m_fileNameLength;

            /// <summary>
            ///     Allocates a constant-sized buffer for the FileName.  MAX_PATH for the path, 4 for the DosToNtPathPrefix.
            /// </summary>
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260 + 4)]
            private readonly string m_filenameName;

            // ReSharper restore PrivateFieldCanBeConvertedToLocalVariable
            public FileLinkInformation(string destinationPath, bool replaceIfExists)
            {
                m_filenameName = destinationPath;
                m_fileNameLength = (uint)(2 * m_filenameName.Length);
                m_rootDirectoryHandle = IntPtr.Zero;
                m_replaceIfExists = (byte)(replaceIfExists ? 1 : 0);
            }
        }

        /// <summary>
        ///     Enumeration of the various file information classes.
        ///     See wdm.h.
        /// </summary>
        public enum FileInformationClass
        {
            None = 0,
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
            FileMaximumInformation,
        }

        /// <summary>
        /// Gets the full path of the specified path.
        /// </summary>
        /// <remarks>
        /// This method functions like <see cref="Path.GetFullPath(string)"/>, i.e., it merges the name of the current drive and directory with
        /// a specified file name to determine the full path of a specified file.
        /// </remarks>
        public string GetFullPath(string path, out int hr)
        {
            hr = 0;
            string toFullPath = ToLongPathIfExceedMaxPath(path);

            int bufferSize = NativeIOConstants.MaxPath;
            StringBuilder sbFull = new StringBuilder(bufferSize);

            uint u = GetFullPathNameW(toFullPath, (uint)bufferSize, sbFull, IntPtr.Zero);

            if (u == 0)
            {
                hr = Marshal.GetLastWin32Error();
                return null;
            }

            if (u > bufferSize)
            {
                bufferSize = (int)u + 10;
                sbFull.Clear();
                sbFull.EnsureCapacity(bufferSize);
                u = GetFullPathNameW(toFullPath, (uint)bufferSize, sbFull, IntPtr.Zero);
            }

            return sbFull.ToString();
        }

        /// <summary>
        /// Moves file to a new location.
        /// </summary>
        public bool MoveFile(string existingFileName, string newFileName, bool replaceExisting)
        {
            existingFileName = ToLongPathIfExceedMaxPath(existingFileName);
            newFileName = ToLongPathIfExceedMaxPath(newFileName);
            MoveFileFlags moveFlags = replaceExisting ? MoveFileFlags.MOVEFILE_REPLACE_EXISTING : MoveFileFlags.MOVEFILE_COPY_ALLOWED;

            return MoveFileEx(existingFileName, newFileName, moveFlags);
        }

        /// <summary>
        /// Win32 absolute path type.
        /// </summary>
        private enum Win32AbsolutePathType
        {
            /// <summary>
            /// Invalid type.
            /// </summary>
            Invalid,

            /// <summary>
            /// E.g., X:\ABC\DEF.
            /// </summary>
            LocalDrive,

            /// <summary>
            /// E.g., \\server\share\ABC\DEF.
            /// </summary>
            UNC,

            /// <summary>
            /// E.g., \\.\COM20, \\.\pipe\mypipe.
            /// </summary>
            LocalDevice,

            /// <summary>
            /// E.g., \\?\X:\ABC\DEF.
            /// </summary>
            LongPathPrefixed,

            /// <summary>
            /// E.g., \??\X:\ABC\DEF.
            /// </summary>
            NtPrefixed,
        }

        private static Win32AbsolutePathType GetPathType(string path)
        {
            if (path.Length >= 3
                && char.IsLetter(path[0])
                && path[1] == Path.VolumeSeparatorChar
                && IsDirectorySeparatorCore(path[2]))
            {
                return Win32AbsolutePathType.LocalDrive;
            }

            if (path.Length >= 2 && (path[0] == '\\' || path[0] == '/'))
            {
                char path0 = path[0];

                if (path.Length >= 4 && path[3] == path0)
                {
                    if (path[1] == path0)
                    {
                        if (path[2] == '?')
                        {
                            return Win32AbsolutePathType.LongPathPrefixed;
                        }
                        else if (path[2] == '.')
                        {
                            return Win32AbsolutePathType.LocalDevice;
                        }
                    }
                    else if (path[1] == '?' && path[2] == '?')
                    {
                        return Win32AbsolutePathType.NtPrefixed;
                    }
                }

                if (path[1] == path0)
                {
                    return Win32AbsolutePathType.UNC;
                }
            }

            return Win32AbsolutePathType.Invalid;
        }

        /// <summary>
        /// Returns a path with a long path prefix if the given path exceeds a short max path length.
        /// </summary>
        public static string ToLongPathIfExceedMaxPath(string path)
        {
            Contract.Requires(path != null);

            if (path.Length < NativeIOConstants.MaxDirectoryPath)
            {
                return path;
            }

            switch (GetPathType(path))
            {
                case Win32AbsolutePathType.Invalid:
                case Win32AbsolutePathType.LocalDevice:
                case Win32AbsolutePathType.LongPathPrefixed:
                case Win32AbsolutePathType.NtPrefixed:
                    return path;
                case Win32AbsolutePathType.LocalDrive:
                    return LongPathPrefix + path;
                case Win32AbsolutePathType.UNC:
                    return LongUNCPathPrefix + path.Substring(2);
                default:
                    return path;
            }
        }

        /// <inheritdoc />
        public FileIdAndVolumeId? TryGetFileIdentityByHandle(SafeFileHandle fileHandle)
        {
            FileIdAndVolumeId? maybeIds = TryGetFileIdAndVolumeIdByHandle(fileHandle);

            if (maybeIds.HasValue)
            {
                return maybeIds.Value;
            }

            ulong volumeSerial = GetShortVolumeSerialNumberByHandle(fileHandle);
            var usnRecord = ReadFileUsnByHandle(fileHandle);

            return usnRecord.HasValue
                ? new FileIdAndVolumeId(volumeSerial, usnRecord.Value.FileId)
                : default(FileIdAndVolumeId?);
        }

        /// <inheritdoc />
        public (FileIdAndVolumeId, Usn)? TryGetVersionedFileIdentityByHandle(SafeFileHandle fileHandle)
        {
            MiniUsnRecord? usnRecord = ReadFileUsnByHandle(fileHandle);

            if (usnRecord.HasValue && usnRecord.Value.Usn.IsZero)
            {
                Usn? maybeNewUsn = TryWriteUsnCloseRecordByHandle(fileHandle);
                if (maybeNewUsn.HasValue)
                {
                    usnRecord = new MiniUsnRecord(usnRecord.Value.FileId, maybeNewUsn.Value);
                }
            }

            // If usnRecord is null or 0, then fail!
            if (!usnRecord.HasValue || usnRecord.Value.Usn.IsZero)
            {
                return null;
            }

            FileIdAndVolumeId? maybeIds = TryGetFileIdAndVolumeIdByHandle(fileHandle);

            // A short volume serial isn't the first choice (fewer random bits), but we fall back to it if the long serial is unavailable.
            var volumeSerial = maybeIds.HasValue ? maybeIds.Value.VolumeSerialNumber : GetShortVolumeSerialNumberByHandle(fileHandle);

            return (new FileIdAndVolumeId(volumeSerial, usnRecord.Value.FileId), usnRecord.Value.Usn);
        }

        /// <inheritdoc />
        public (FileIdAndVolumeId, Usn)? TryEstablishVersionedFileIdentityByHandle(SafeFileHandle fileHandle, bool flushPageCache)
        {
            // Before writing a CLOSE record, we might want to ensure that all dirtied cache pages have been handed back to the filesystem.
            // Otherwise, at some point in the future, the dirty pages will get lazy-written back to the filesystem, thus generating
            // a DATA OVERWRITE change reason after our CLOSE. This can happen if a file was memory-mapped for writing.
            // Note that this does NOT ensure data is crash-safe, i.e., it may still be in some cache such as one on the disk device itself;
            // we just need NTFS / ReFS up to date on what writes have supposedly happened.
            if (flushPageCache)
            {
                // This flush operation is best effort.
                FlushPageCacheToFilesystem(fileHandle);
            }

            Usn? maybeNewUsn = TryWriteUsnCloseRecordByHandle(fileHandle);
            if (!maybeNewUsn.HasValue)
            {
                return null;
            }

            Usn newUsn = maybeNewUsn.Value;

            FileIdAndVolumeId? maybeIds = TryGetFileIdAndVolumeIdByHandle(fileHandle);

            ulong volumeSerial;
            FileId fileId;
            if (maybeIds.HasValue)
            {
                volumeSerial = maybeIds.Value.VolumeSerialNumber;
                fileId = maybeIds.Value.FileId;
            }
            else
            {
                // A short volume serial isn't the first choice (fewer random bits), but we fall back to it if the long serial is unavailable.
                volumeSerial = GetShortVolumeSerialNumberByHandle(fileHandle);
                var usnRecord = ReadFileUsnByHandle(fileHandle);

                if (usnRecord.HasValue)
                {
                    fileId = usnRecord.Value.FileId;
                }
                else
                {
                    return null;
                }
            }

            return (new FileIdAndVolumeId(volumeSerial, fileId), newUsn);
        }

        /// <inheritdoc />
        public bool IsPreciseFileVersionSupportedByEnlistmentVolume
        {
            get => true;
            set
            {
                // Do nothing.
            }
        }

        /// <inheritdoc />
        public bool CheckIfVolumeSupportsPreciseFileVersionByHandle(SafeFileHandle fileHandle) => true;

        /// <inheritdoc />
        public bool IsCopyOnWriteSupportedByEnlistmentVolume
        {
            get => false;
            set
            {
                // Do nothing.
            }
        }

        /// <inheritdoc />
        public bool CheckIfVolumeSupportsCopyOnWriteByHandle(SafeFileHandle fileHandle) => false;

        /// <inheritdoc />
        public bool IsPathRooted(string path)
        {
            return GetRootLength(path) != 0;
        }

        /// <inheritdoc />
        public int GetRootLength(string path)
        {
            int i = 0;
            int volumeSeparatorLength = 2;  // Length to the colon "C:"
            int uncRootLength = 2;          // Length to the start of the server name "\\"

            bool extendedSyntax = path.StartsWith(LongPathPrefix, StringComparison.Ordinal);
            bool extendedUncSyntax = path.StartsWith(LongUNCPathPrefix, StringComparison.Ordinal);

            if (extendedSyntax)
            {
                // Shift the position we look for the root from to account for the extended prefix
                if (extendedUncSyntax)
                {
                    // "\\" -> "\\?\UNC\"
                    uncRootLength = LongUNCPathPrefix.Length;
                }
                else
                {
                    // "C:" -> "\\?\C:"
                    volumeSeparatorLength += LongPathPrefix.Length;
                }
            }

            if ((!extendedSyntax || extendedUncSyntax) && path.Length > 0 && IsDirectorySeparator(path[0]))
            {
                // UNC or simple rooted path (e.g. "\foo", NOT "\\?\C:\foo")

                i = 1; //  Drive rooted (\foo) is one character
                if (extendedUncSyntax || (path.Length > 1 && IsDirectorySeparator(path[1])))
                {
                    // UNC (\\?\UNC\ or \\), scan past the next two directory separators at most
                    // (e.g. to \\?\UNC\Server\Share or \\Server\Share\)
                    i = uncRootLength;
                    int n = 2;
                    while (i < path.Length && (!IsDirectorySeparator(path[i]) || --n > 0))
                    {
                        ++i;
                    }
                }
            }
            else if (path.Length >= volumeSeparatorLength && path[volumeSeparatorLength - 1] == Path.VolumeSeparatorChar)
            {
                // Path is at least longer than where we expect a colon, and has a colon (\\?\A:, A:)
                // If the colon is followed by a directory separator, move past it
                i = volumeSeparatorLength;
                if (path.Length >= volumeSeparatorLength + 1 && IsDirectorySeparator(path[volumeSeparatorLength]))
                {
                    ++i;
                }
            }

            return i;
        }

        /// <inheritdoc />
        public bool IsDirectorySeparator(char c) => IsDirectorySeparatorCore(c);

        private static bool IsDirectorySeparatorCore(char c) => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar;

        /// <inheritdoc />
        private Possible<string> TryGetFinalPathByHandle(string path)
        {
            SafeFileHandle handle = CreateFileW(
                ToLongPathIfExceedMaxPath(path),
                FileDesiredAccess.None,
                FileShare.None,
                lpSecurityAttributes: IntPtr.Zero,
                dwCreationDisposition: FileMode.Open,
                dwFlagsAndAttributes: FileFlagsAndAttributes.FileFlagBackupSemantics,
                hTemplateFile: IntPtr.Zero);
            int hr = Marshal.GetLastWin32Error();

            if (handle.IsInvalid)
            {
                return new NativeFailure(hr);
            }

            using (handle)
            {
                try
                {
                    return GetFinalPathNameByHandle(handle);
                }
                catch (NativeWin32Exception e)
                {
                    return NativeFailure.CreateFromException(e);
                }
            }
        }

        /// <summary>
        /// Resolves the reparse points with relative target. 
        /// </summary>
        /// <remarks>
        /// This method resolves reparse points that occur in the path prefix. This method should only be called when path itself
        /// is an actionable reparse point whose target is a relative path. 
        /// This method traverses each prefix starting from the shortest one. Every time it encounters a directory symlink, it uses GetFinalPathNameByHandle to get the final path. 
        /// However, if the prefix itself is a junction, then it leaves the current resolved path intact. We cannot call GetFinalPathNameByHandle on the whole path because
        /// that function resolves junctions to their target paths.
        /// The following example show the needs for this method as a prerequisite in getting 
        /// the immediate target of a reparse point. Suppose that we have the following file system layout:
        ///
        ///    repo
        ///    |
        ///    +---intermediate
        ///    |   \---current
        ///    |         symlink1.link ==> ..\..\target\file1.txt
        ///    |         symlink2.link ==> ..\target\file2.txt
        ///    |
        ///    +---source ==> intermediate\current (case 1: directory symlink, case 2: junction)
        ///    |
        ///    \---target
        ///          file1.txt
        ///          file2.txt
        ///
        /// **CASE 1**: source ==> intermediate\current is a directory symlink. 
        ///
        /// If a tool accesses repo\source\symlink1.link (say 'type repo\source\symlink1.link'), then the tool should get the content of repo\target\file1.txt.
        /// If the tool accesses repo\source\symlink2.link, then the tool should get path-not-found error because the resolved path will be repo\intermediate\target\file2.txt.
        /// Now, if we try to resolve repo\source\symlink1.link by simply combining it with ..\..\target\file1.txt, then we end up with target\file1.txt (not repo\target\file1.txt),
        /// which is a non-existent path. To resolve repo\source\symlink1, we need to resolve the reparse points of its prefix, i.e., repo\source. For directory symlinks,
        /// we need to resolve the prefix to its target. I.e., repo\source is resolved to repo\intermediate\current, and so, given repo\source\symlink1.link, this method returns
        /// repo\intermediate\current\symlink1.link. Combining repo\intermediate\current\symlink1.link with ..\..\target\file1.txt will give the correct path, i.e., repo\target\file1.txt.
        /// 
        /// Similarly, given repo\source\symlink2.link, the method returns repo\intermediate\current\symlink2.link, and combining it with ..\target\file2.txt, will give us
        /// repo\intermediate\target\file2.txt, which is a non-existent path. This corresponds to the behavior of symlink accesses above.
        ///
        /// **CASE 2**: source ==> intermediate\current is a junction.
        ///
        /// If a tool accesses repo\source\symlink1.link (say 'type repo\source\symlink1.link'), then the tool should get path-not-found error because the resolve path will be target\file1.txt (not repo\target\file1).
        /// If the tool accesses repo\source\symlink2.link, then the tool should the content of repo\target\file2.txt.
        /// Unlike directory symlinks, when we try to resolve repo\source\symlink2.link, the prefix repo\source is left intact because it is a junction. Thus, combining repo\source\symlink2.link
        /// with ..\target\file2.txt results in a correct path, i.e., repo\target\file2.txt. The same reasoning can be given for repo\source\symlink1.link, and its resolution results in
        /// a non-existent path target\file1.txt.
        /// </remarks>
        public Possible<string> TryResolveReparsePointRelativeTarget(string path, string relativeTarget)
        {
            var needToBeProcessed = new Stack<string>();
            var processed = new Stack<string>();

            using (var sbWrapper = Pools.GetStringBuilder())
            {
                StringBuilder result = sbWrapper.Instance;

                FileUtilities.SplitPathsReverse(path, needToBeProcessed);

                while (needToBeProcessed.Count != 0)
                {
                    string atom = needToBeProcessed.Pop();
                    processed.Push(atom);

                    if (result.Length > 0)
                    {
                        if (!IsDirectorySeparator(result[result.Length - 1]) && !IsDirectorySeparator(atom[0]))
                        {
                            result.Append(Path.DirectorySeparatorChar);
                        }
                    }

                    result.Append(atom);

                    if (needToBeProcessed.Count == 0)
                    {
                        // The last atom is the one that we are going to replace.
                        break;
                    }

                    string resultSoFar = result.ToString();

                    var maybeReparsePointType = TryGetReparsePointType(resultSoFar);

                    if (!maybeReparsePointType.Succeeded)
                    {
                        return maybeReparsePointType.Failure;
                    }

                    if (maybeReparsePointType.Result == ReparsePointType.SymLink)
                    {
                        var maybeTarget = TryGetReparsePointTarget(null, resultSoFar);

                        if (!maybeTarget.Succeeded)
                        {
                            return maybeTarget.Failure;
                        }

                        if (IsPathRooted(maybeTarget.Result))
                        {
                            // Target is an absolute path -> restart symlink resolution.
                            result.Clear();
                            processed.Clear();
                            FileUtilities.SplitPathsReverse(maybeTarget.Result, needToBeProcessed);
                        }
                        else
                        {
                            // Target is a relative path.
                            var maybeResolveRelative = FileUtilities.TryResolveRelativeTarget(resultSoFar, maybeTarget.Result, processed, needToBeProcessed);

                            if (!maybeResolveRelative.Succeeded)
                            {
                                return maybeResolveRelative.Failure;
                            }

                            result.Clear();
                            result.Append(maybeResolveRelative.Result);
                        }
                    }
                }

                var maybeResolveFinalRelative = FileUtilities.TryResolveRelativeTarget(result.ToString(), relativeTarget, null, null);

                if (!maybeResolveFinalRelative.Succeeded)
                {
                    return maybeResolveFinalRelative.Failure;
                }

                return maybeResolveFinalRelative;
            }
        }
    }
}

#pragma warning restore CA1823 // Unused field
