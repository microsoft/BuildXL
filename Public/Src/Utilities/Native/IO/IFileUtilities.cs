// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.AccessControl;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Native.Streams;
using BuildXL.Utilities;
using BuildXL.Utilities.Tasks;
using Microsoft.Win32.SafeHandles;

namespace BuildXL.Native.IO
{
    /// <summary>
    /// File timestamps.
    /// </summary>
    public readonly struct FileTimestamps
    {
        /// <nodoc />
        public readonly DateTime CreationTime;

        /// <nodoc />
        public readonly DateTime AccessTime;

        /// <nodoc />
        public readonly DateTime LastWriteTime;

        /// <nodoc />
        public readonly DateTime LastChangeTime;

        /// <nodoc />
        public FileTimestamps(DateTime creationTime, DateTime accessTime, DateTime lastWriteTime, DateTime lastChangeTime)
        {
            CreationTime = creationTime;
            AccessTime = accessTime;
            LastWriteTime = lastWriteTime;
            LastChangeTime = lastChangeTime;
        }

        /// <nodoc />
        public FileTimestamps(DateTime sameTimeForEverything)
            : this(sameTimeForEverything, sameTimeForEverything, sameTimeForEverything, sameTimeForEverything)
        {
        }
    }

    /// <summary>
    /// Utilities and helpers for manipulating files (I/O) leveraging IFileSystem facilities
    /// </summary>
    public interface IFileUtilities
    {
        #region Directory specific utilities

        /// <summary>
        /// Deletes all contents of a directory and optionally deletes the directory itself.
        /// </summary>
        /// <exception cref="BuildXLException">
        /// Thrown if the directory deletion fails in a recoverable manner (e.g. access denied).
        /// </exception>
        /// <remarks>
        /// Supports paths beyond MAX_PATH being nested within the path, but not having the argument greater than MAX_PATH
        /// Throws if the directory requested to be deleted does not exist
        /// </remarks>
        /// <param name="path">absolute path of directory to delete</param>
        /// <param name="deleteRootDirectory">whether to also delete the root directory</param>
        /// <param name="shouldDelete">a function which returns true if file should be deleted and false otherwise.</param>
        /// <param name="tempDirectoryCleaner">provides and cleans a temp directory for move-deleting files</param>
        /// <param name="cancellationToken">provides cancelation capability.</param>
        void DeleteDirectoryContents(
            string path, 
            bool deleteRootDirectory, 
            Func<string, bool> shouldDelete, 
            ITempCleaner tempDirectoryCleaner = null, 
            CancellationToken? cancellationToken = default);

        /// <summary>
        /// Recursively enumerates the contents of a directory along with any open handles.
        /// </summary>
        /// <param name="directoryPath">
        /// The directory to search recursively for open handles
        /// </param>
        /// <param name="pathsPossiblyPendingDelete">
        /// Any paths within the directory that are known to be deleted, but may be in the pending
        /// delete state; see remarks.
        /// </param>
        /// <remarks>
        /// In the case that the path is pending deletion, no open handles will be reported, even
        /// if there is a handle open somewhere preventing the delete from completing.
        /// </remarks>
        string FindAllOpenHandlesInDirectory(string directoryPath, HashSet<string> pathsPossiblyPendingDelete = null);

        #endregion

        #region File specific utilities

        /// <summary>
        /// Copies the file 'source' to 'destination'.
        /// If <paramref name="predicate" /> is provided, it is expected to return a bool indicating if the copy should proceed
        /// (given a (source handle, destination handle) pair).
        /// If <paramref name="onCompletion" /> is provided, it is called after the copy is complete (with a (source handle,
        /// destination handle) pair).
        /// This implementation uses <see cref="CreateReplacementFile"/> and so is robust to deny-write ACLs / read-only attribute on the destination.
        /// </summary>
        /// <remarks>
        /// The two callbacks allow safely performing copy elision:
        /// - Predicate allows checking if the destination is up to date w.r.t. the source.
        /// - OnCompletion allows recording destination info for later change detection.
        /// Note that Predicate may be called with destination as <c>null</c> if the destination did not yet exist.
        ///
        /// Does not support paths longer than MAX_PATH
        /// </remarks>
        /// <returns>
        /// If true is returned, the copy proceeded and completed. Otherwise, the copy was skipped due to a false
        /// <paramref name="predicate" />.
        /// </returns>
        /// <exception cref="BuildXLException">
        /// Thrown if the file copy fails in a recoverable manner.
        /// </exception>
        Task<bool> CopyFileAsync(string source, string destination, Func<SafeFileHandle, SafeFileHandle, bool> predicate, Action<SafeFileHandle, SafeFileHandle> onCompletion);

        /// <summary>
        /// Moves the file 'source' to 'destination'. An exception will be thrown if 'destination' already exists, unless
        /// <paramref name="replaceExisting" /> is true
        /// </summary>
        /// <remarks>
        /// Does not support paths longer than MAX_PATH
        /// </remarks>
        /// <exception cref="BuildXLException">
        /// Thrown if the file move fails in a recoverable manner, including if the destination
        /// already exists and <paramref name="replaceExisting" /> is set to false or if the source doesn't exist
        /// </exception>
        /// <param name="source">Full path of the source</param>
        /// <param name="destination">Full path of the destination</param>
        /// <param name="replaceExisting">whether to replace an existing file at the destination</param>
        Task MoveFileAsync(string source, string destination, bool replaceExisting);

        /// <summary>
        /// Creates a copy on write clone of files if supported by the underlying OS.
        /// </summary>
        /// <param name="source"></param>
        /// <param name="destination"></param>
        /// <param name="followSymlink"></param>
        /// <exception cref="NativeWin32Exception">Throw native exception upon failure.</exception>
        void CloneFile(string source, string destination, bool followSymlink);

        /// <summary>
        /// Returns a new <see cref="FileStream" /> with the share mode. The target path is always deleted (if present) and re-created.
        /// Note that <c>CreateFile</c> with <c>CREATE_ALWAYS</c> (the naive approach to this) actually truncates existing writable files;
        /// this is never safe in the event of hardlinks, since a hardlinked file may be writable, and we cannot atomically truncate a file
        /// contingent upon it having link count == 1. Therefore, we never truncate.
        /// This implementation is robust to replacing files with deny-write ACLs or a readonly flag, and can safely replace hardlink
        /// files that may happen to be writable.
        /// </summary>
        /// <remarks>
        /// Does not support paths longer than MAX_PATH.
        /// The returned stream is opened with read-write access.
        /// </remarks>
        /// <exception cref="BuildXLException">
        /// Thrown if a recoverable error occurs while deleting or recreating the file.
        /// </exception>
        /// <param name="path">Path of file</param>
        /// <param name="fileShare">FileShare</param>
        /// <param name="openAsync">Indicates if the replacement should be opened with FILE_FLAG_OVERLAPPED.</param>
        /// <param name="allowExcludeFileShareDelete">Indicates willful omission of FILE_SHARE_DELETE.</param>
        FileStream CreateReplacementFile(string path, FileShare fileShare, bool openAsync, bool allowExcludeFileShareDelete);

        /// <summary>
        /// Deletes the specified file.
        /// </summary>
        /// <remarks>
        /// This function is tolerant to files marked read-only, or in use via other hardlinks.
        /// Strategies (stop at first that succeeds):
        /// - Normal deletion
        /// - Clear read-only (maybe it was set) and try again
        /// - Move-delete (see TryDeleteViaMoveReplacement)
        ///
        /// Supports paths greater than MAX_PATH if "\\?\" prefix is used
        /// </remarks>
        /// <exception cref="BuildXLException">
        /// Thrown if the file deletion fails in a recoverable manner (e.g. access denied).
        /// </exception>
        void DeleteFile(string path, bool waitUntilDeletionFinished, ITempCleaner tempDirectoryCleaner = null);

        /// <summary>
        /// Controls the applicability of POSIX delete.
        /// </summary>
        PosixDeleteMode PosixDeleteMode { get; set; }

        /// <summary>
        /// Variant of <see cref="DeleteFile"/> returning a <see cref="Possible{TResult,TOtherwise}"/> rather than throwing.
        /// </summary>
        Possible<Unit, RecoverableExceptionFailure> TryDeleteFile(string path, bool waitUntilDeletionFinished, ITempCleaner tempDirectoryCleaner = null);

        /// <summary>
        /// Attempts to move file to a temporary directory that will be garbage collected in the future.
        /// This is a defensive attempt to cover unknown cases. There are no known cases where all other delete file attempts fail, but this works.
        /// </summary>
        bool TryMoveDelete(string path, string deletionTempDirectory);

        /// <summary>
        /// Returns file name of the path with casing matching that of the file system.
        /// </summary>
        /// <param name="path">The path to the file.</param>
        /// <returns>The file name.</returns>
        Possible<string> GetFileName(string path);

        /// <summary>
        /// Sets the time stamp of a specified file.
        /// </summary>
        /// <remarks>
        /// Supports paths greater than MAX_PATH if "\\?\" prefix is used
        /// </remarks>
        /// <exception cref="BuildXLException">
        /// Thrown if the operation fails in a recoverable manner (e.g. access denied).
        /// </exception>
        void SetFileTimestamps(string path, FileTimestamps timestamps, bool followSymlink = false);

        /// <summary>
        /// Gets the time stamp of a specified file.
        /// </summary>
        /// <remarks>
        /// Supports paths greater than MAX_PATH if "\\?\" prefix is used
        /// </remarks>
        /// <exception cref="BuildXLException">
        /// Thrown if the operation fails in a recoverable manner (e.g. access denied).
        /// </exception>
        FileTimestamps GetFileTimestamps(string path, bool followSymlink = false);

        /// <summary>
        /// Writes the 'text' with the given 'encoding' to the file 'filePath'.
        /// </summary>
        /// <remarks>
        /// Does not support paths longer than MAX_PATH
        /// </remarks>
        /// <exception cref="BuildXLException">
        /// Thrown if the file write fails in a recoverable manner.
        /// </exception>
        Task WriteAllTextAsync(string filePath, string text, Encoding encoding);

        /// <summary>
        /// Writes the given bytes to the file 'filePath'.
        /// If <paramref name="predicate" /> is provided, it is expected to return a bool indicating if the copy should proceed
        /// (given the opened stream).
        /// If <paramref name="onCompletion" /> is provided, it is called after the write is complete (given the opened stream).
        /// This implementation uses <see cref="CreateReplacementFile"/> and so is robust to deny-write ACLs / read-only attribute on the destination.
        /// </summary>
        /// <remarks>
        /// The two callbacks allow safely performing write elision without dropping handles.
        /// - Predicate allows checking if the destination is up to date w.r.t. desired content
        /// - OnCompletion allows recording destination info for later change detection.
        /// Note that Predicate may be called with destination as <c>null</c> if the destination did not yet exist.
        ///
        /// Does not support paths longer than MAX_PATH
        /// </remarks>
        /// <returns>
        /// If true is returned, the write proceeded and completed. Otherwise, the copy was skipped due to a false
        /// <paramref name="predicate" />.
        /// </returns>
        /// <exception cref="BuildXLException">
        /// Thrown if the file write fails in a recoverable manner.
        /// </exception>
        Task<bool> WriteAllBytesAsync(string filePath, byte[] bytes, Func<SafeFileHandle, bool> predicate, Action<SafeFileHandle> onCompletion);

        /// <summary>
        /// Tries to get debugging information for open handles to a path. Current implementation relies on handle.exe
        /// </summary>
        /// <param name="filePath">The absolute path to the file</param>
        /// <param name="diagnosticInfo">Diagnostic information for what might have an open handle to the file</param>
        /// <param name="printCurrentFilePath">If true, the output message would contain a path to a current file.</param>
        /// <returns>true if diagnostic info is available</returns>
        bool TryFindOpenHandlesToFile(string filePath, out string diagnosticInfo, bool printCurrentFilePath = true);

        /// <summary>
        /// Gets hard link count.
        /// </summary>
        /// <param name="path">The path to the file.</param>
        /// <returns>Number of hard links to the file.</returns>
        uint GetHardLinkCount(string path);

        /// <summary>
        /// Checks the ACL for writable access control.
        /// </summary>
        /// <param name="path">The path to the file.</param>
        bool HasWritableAccessControl(string path);

        /// <summary>
        /// Checks the ACL for writable attribute access control.
        /// </summary>
        /// <param name="path">The path to the file.</param>
        bool HasWritableAttributeAccessControl(string path);

        /// <summary>
        /// Returns a new <see cref="FileStream" /> with the given creation mode, access level, and sharing.
        /// </summary>
        /// <remarks>
        /// This factory exists purely because FileStream cannot be constructed in async mode without also specifying a buffer
        /// size.
        /// We go ahead and wrap recoverable errors as BuildXLExceptions as well.
        ///
        /// Does not support paths longer than MAX_PATH
        /// </remarks>
        /// <exception cref="BuildXLException">
        /// Thrown if a recoverable error occurs while opening the stream (see
        /// <see cref="ExceptionUtilities.HandleRecoverableIOException{T}" />)
        /// </exception>
        /// <param name="path">Path of file</param>
        /// <param name="fileMode">FileMode</param>
        /// <param name="fileAccess">FileAccess</param>
        /// <param name="fileShare">FileShare</param>
        /// <param name="options">FileOptions</param>
        /// <param name="force">Clears the readonly attribute if necessary</param>
        /// <param name="allowExcludeFileShareDelete">Indicates willful omission of FILE_SHARE_DELETE.</param>
        FileStream CreateFileStream(
            string path,
            FileMode fileMode,
            FileAccess fileAccess,
            FileShare fileShare,
            FileOptions options = FileOptions.None,
            bool force = false,
            bool allowExcludeFileShareDelete = false);

        /// <summary>
        /// Returns a new <see cref="FileStream" /> with the given creation mode, access level, and sharing.
        /// The returned file stream is opened in 'async' mode, and so its ReadAsync / WriteAsync methods
        /// will dispatch to the managed thread pool's I/O completion port.
        /// </summary>
        /// <remarks>
        /// This factory exists purely because FileStream cannot be constructed in async mode without also specifying a buffer
        /// size.
        /// We go ahead and wrap recoverable errors as BuildXLExceptions as well. See <see cref="CreateFileStream" />.
        ///
        /// Does not support paths longer than MAX_PATH
        /// </remarks>
        /// <exception cref="BuildXLException">
        /// Thrown if a recoverable error occurs while opening the stream (see
        /// <see cref="ExceptionUtilities.HandleRecoverableIOException{T}" />)
        /// </exception>
        /// <param name="path">Path of file</param>
        /// <param name="fileMode">FileMode</param>
        /// <param name="fileAccess">FileAccess</param>
        /// <param name="fileShare">FileShare</param>
        /// <param name="options">FileOptions</param>
        /// <param name="force">Clears the readonly attribute if necessary</param>
        /// <param name="allowExcludeFileShareDelete">Indicates willful omission of FILE_SHARE_DELETE.</param>
        FileStream CreateAsyncFileStream(
            string path,
            FileMode fileMode,
            FileAccess fileAccess,
            FileShare fileShare,
            FileOptions options = FileOptions.None,
            bool force = false,
            bool allowExcludeFileShareDelete = false);

        /// <summary>
        /// Opens an <see cref="AsyncFileStream"/> and uses the given <paramref name="handleStream"/> delegate to process and get the result.
        /// </summary>
        TResult UsingFileHandleAndFileLength<TResult>(
            string path,
            FileDesiredAccess desiredAccess,
            FileShare shareMode,
            FileMode creationDisposition,
            FileFlagsAndAttributes flagsAndAttributes,
            Func<SafeFileHandle, long, TResult> handleStream);

        #endregion

        #region General file and directory utilities

        /// <summary>
        /// A more robust version of <see cref="File.Exists(string)"/>. File.Exists gives false negatives (says files do not exist when they do)
        /// if the path is invalid, the path is a directory, caller does not have read permissions, or any other exceptions are raised.
        /// File and path naming rules: https://msdn.microsoft.com/en-us/library/windows/desktop/aa365247(v=vs.85).aspx
        /// </summary>
        bool Exists(string path);

        /// <summary>
        /// Checks to see if a logical drive has the seek penalty property set
        /// </summary>
        bool? DoesLogicalDriveHaveSeekPenalty(char driveLetter);

        /// <summary>
        /// Helper to get Windows Special folders by Guid
        /// </summary>
        string GetKnownFolderPath(Guid knownFolder);

        /// <summary>
        /// Gets the common folder location to store app specific user settings.
        /// </summary>
        /// <remarks>
        /// This Api ensures that the directory exists after calling this api.
        /// </remarks>
        string GetUserSettingsFolder(string appName);

        /// <summary>
        /// Helper to set file access rights for a file.
        /// </summary>
        /// <param name="path">Path to a file.</param>
        /// <param name="fileSystemRights">Writes to modify.</param>
        /// <param name="allow">Whether the given rights are to be enabled to disabled.</param>
        void SetFileAccessControl(string path, FileSystemRights fileSystemRights, bool allow);
        #endregion
    }
}
