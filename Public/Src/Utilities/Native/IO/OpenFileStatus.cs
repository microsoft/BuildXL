// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Native.IO
{
    /// <summary>
    /// Normalized status indication (derived from a native error code and the creation disposition).
    /// </summary>
    /// <remarks>
    /// This is useful for two reasons: it is an enum for which we can know all cases are handled, and successful opens
    /// are always <see cref="OpenFileStatus.Success"/> (the distinction between opening / creating files is moved to
    /// <see cref="OpenFileResult.OpenedOrTruncatedExistingFile"/>)
    /// </remarks>
    public enum OpenFileStatus : byte
    {
        /// <summary>
        /// The file was opened (a valid handle was obtained).
        /// </summary>
        /// <remarks>
        /// The <see cref="OpenFileResult.NativeErrorCode"/> may be something other than <c>ERROR_SUCCESS</c>,
        /// since some open modes indicate if a file existed already or was created new via a special error code.
        /// </remarks>
        Success,

        /// <summary>
        /// The file was not found, and no handle was obtained.
        /// </summary>
        FileNotFound,

        /// <summary>
        /// Some directory component in the path was not found, and no handle was obtained.
        /// </summary>
        PathNotFound,

        /// <summary>
        /// The file was opened already with an incompatible share mode, and no handle was obtained.
        /// </summary>
        SharingViolation,

        /// <summary>
        /// The file cannot be opened with the requested access level, and no handle was obtained.
        /// </summary>
        AccessDenied,

        /// <summary>
        /// The file already exists (and the open mode specifies failure for existent files); no handle was obtained.
        /// </summary>
        FileAlreadyExists,

        /// <summary>
        /// The device the file is on is not ready. Should be treated as a nonexistent file.
        /// </summary>
        ErrorNotReady,

        /// <summary>
        /// The volume the file is on is locked. Should be treated as a nonexistent file.
        /// </summary>
        FveLockedVolume,

        /// <summary>
        /// The operaiton timed out. This generally occurs because of remote file materialization taking too long in the
        /// filter driver stack. Waiting and retrying may help.
        /// </summary>
        Timeout,

        /// <summary>
        /// The file cannot be accessed by the system. Should be treated as a nonexistent file.
        /// </summary>
        CannotAccessFile,

        /// <summary>
        /// The specified path is invalid. (from 'winerror.h')
        /// </summary>
        BadPathname,

        /// <summary>
        /// See <see cref="OpenFileResult.NativeErrorCode"/>
        /// </summary>
        UnknownError,
    }

    /// <summary>
    /// Extensions to OpenFileStatus
    /// </summary>
#pragma warning disable SA1649 // File name should match first type name
    public static class OpenFileStatusExtensions
#pragma warning restore SA1649
    {
        /// <summary>
        /// Whether the status is one that should be treated as a nonexistent file
        /// </summary>
        /// <remarks>
        /// CODESYNC: <see cref="Windows.FileSystemWin.IsHresultNonexistent(int)"/>
        /// </remarks>
        public static bool IsNonexistent(this OpenFileStatus status)
        {
            return status == OpenFileStatus.FileNotFound
                || status == OpenFileStatus.PathNotFound
                || status == OpenFileStatus.ErrorNotReady
                || status == OpenFileStatus.FveLockedVolume
                || status == OpenFileStatus.CannotAccessFile
                || status == OpenFileStatus.BadPathname;
        }

        /// <summary>
        /// Whether the status is one that implies other process blocking the handle.
        /// </summary>
        public static bool ImpliesOtherProcessBlockingHandle(this OpenFileStatus status)
        {
            return status == OpenFileStatus.SharingViolation || status == OpenFileStatus.AccessDenied;
        }
    }
}
