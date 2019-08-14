// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Native.IO
{
    /// <summary>
    /// Constants that are used for file I/O inside the Native layer
    /// </summary>
    public static class NativeIOConstants
    {
        /// <summary>
        /// FSCTL_READ_FILE_USN_DATA
        /// </summary>
        public const uint FsctlReadFileUsnData = 0x900eb;

        /// <summary>
        /// FSCTL_WRITE_USN_CLOSE_RECORD
        /// </summary>
        public const uint FsctlWriteUsnCloseRecord = 0x900ef;

        /// <summary>
        /// FSCTL_QUERY_USN_JOURNAL
        /// </summary>
        public const uint FsctlQueryUsnJournal = 0x900f4;

        /// <summary>
        /// FSCTL_READ_USN_JOURNAL
        /// </summary>
        public const uint FsctlReadUsnJournal = 0x900bb;

        /// <summary>
        /// FSCTL_READ_UNPRIVILEGED_USN_JOURNAL
        /// </summary>
        public const uint FsctlReadUnprivilegedUsnJournal = 0x903ab;

        /// <summary>
        /// FVE_LOCKED_VOLUME
        /// </summary>
#pragma warning disable SA1139 // Use literal suffix notation instead of casting
        public const int FveLockedVolume = unchecked((int)0x80310000);
#pragma warning restore SA1139

        /// <summary>
        /// INVALID_FILE_ATTRIBUTES
        /// </summary>
        public const uint InvalidFileAttributes = 0xFFFFFFFF;

        /// <summary>
        /// ERROR_JOURNAL_NOT_ACTIVE
        /// </summary>
        public const uint ErrorJournalNotActive = 0x49B;

        /// <summary>
        ///  ERROR_JOURNAL_DELETE_IN_PROGRESS
        /// </summary>
        public const uint ErrorJournalDeleteInProgress = 0x49A;

        /// <summary>
        ///  ERROR_JOURNAL_ENTRY_DELETED
        /// </summary>
        public const uint ErrorJournalEntryDeleted = 0x49D;

        /// <summary>
        /// ERROR_NO_MORE_FILES
        /// </summary>
        public const uint ErrorNoMoreFiles = 0x12;

        /// <summary>
        /// ERROR_WRITE_PROTECT
        /// </summary>
        public const uint ErrorWriteProtect = 0x13;

        /// <summary>
        /// ERROR_INVALID_PARAMETER
        /// </summary>
        public const int ErrorInvalidParameter = 0x57;

        /// <summary>
        /// ERROR_INVALID_FUNCTION
        /// </summary>
        public const uint ErrorInvalidFunction = 0x1;

        /// <summary>
        /// ERROR_ONLY_IF_CONNECTED
        /// </summary>
        public const uint ErrorOnlyIfConnected = 0x4E3;

        /// <summary>
        /// ERROR_SUCCESS
        /// </summary>
        public const int ErrorSuccess = 0x0;

        /// <summary>
        /// ERROR_ACCESS_DENIED
        /// </summary>
        public const int ErrorAccessDenied = 0x5;

        /// <summary>
        /// ERROR_SHARING_VIOLATION
        /// </summary>
        public const int ErrorSharingViolation = 0x20;

        /// <summary>
        /// ERROR_TOO_MANY_LINKS
        /// </summary>
        public const int ErrorTooManyLinks = 0x476;

        /// <summary>
        /// ERROR_NOT_SAME_DEVICE
        /// </summary>
        public const int ErrorNotSameDevice = 0x11;

        /// <summary>
        /// ERROR_NOT_SUPPORTED
        /// </summary>
        public const int ErrorNotSupported = 0x32;

        /// <summary>
        /// ERROR_FILE_NOT_FOUND
        /// </summary>
        public const int ErrorFileNotFound = 0x2;

        /// <summary>
        /// ERROR_FILE_EXISTS
        /// </summary>
        public const int ErrorFileExists = 0x50;

        /// <summary>
        /// ERROR_FILE_ALREADY_EXISTS
        /// </summary>
        public const int ErrorAlreadyExists = 0xB7;

        /// <summary>
        /// ERROR_PATH_NOT_FOUND
        /// </summary>
        public const int ErrorPathNotFound = 0x3;

        /// <summary>
        /// ERROR_NOT_READY
        /// </summary>
        public const int ErrorNotReady = 0x15;

        /// <summary>
        /// ERROR_DIR_NOT_EMPTY
        /// </summary>
        public const int ErrorDirNotEmpty = 0x91;

        /// <summary>
        /// ERROR_DIRECTORY
        /// </summary>
        public const int ErrorDirectory = 0x10b;

        /// <summary>
        /// ERROR_PARTIAL_COPY
        /// </summary>
        public const int ErrorPartialCopy = 0x12b;

        /// <summary>
        /// ERROR_IO_PENDING
        /// </summary>
        public const int ErrorIOPending = 0x3E5;

        /// <summary>
        /// ERROR_IO_INCOMPLETE
        /// </summary>
        public const int ErrorIOIncomplete = 0x3E4;

        /// <summary>
        /// ERROR_ABANDONED_WAIT_0
        /// </summary>
        public const int ErrorAbandonedWait0 = 0x2DF;

        /// <summary>
        /// ERROR_HANDLE_EOF
        /// </summary>
        public const int ErrorHandleEof = 0x26;

        /// <summary>
        /// ERROR_TIMEOUT
        /// </summary>
        public const int ErrorTimeout = 0x5B4;

        /// <summary>
        /// Infinite timeout.
        /// </summary>
        public const int Infinite = -1;

#if PLATFORM_WIN
        /// <summary>
        /// Maximum path length.
        /// </summary>
        public const int MaxPath = 260;
#else
        /// <summary>
        /// Maximum path length.
        /// </summary>
        public const int MaxPath = 1024;
#endif

        /// <summary>
        /// Maximum path length for \\?\ style paths.
        /// </summary>
        public const int MaxLongPath = 32767;

        /// <summary>
        /// Maximum path length for directory.
        /// </summary>
        public const int MaxDirectoryPath = 248;

        /// <summary>
        /// ERROR_CANT_ACCESS_FILE
        /// </summary>
        public const int ErrorCantAccessFile = 0x780;

        /// <summary>
        /// ERROR_BAD_PATHNAME
        /// </summary>
        public const int ErrorBadPathname = 0xA1;
    }
}
