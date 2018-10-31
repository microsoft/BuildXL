// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Native.IO.Windows;
using BuildXL.Native.Streams;
using System.Security.AccessControl;
using BuildXL.Utilities;
using BuildXL.Utilities.Tasks;
using BuildXL.Utilities.Tracing;
using Microsoft.Win32.SafeHandles;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Native.IO
{
    /// <summary>
    /// Static facade with utilities for manipulating files and directories. Also offers functions for directly calling filesystem level functionality.
    /// Serves as an entry point for direct I/O throughout BuildXLs code base and proxies its calls to platform specific implementations of IFileSystem and IFileUtilities.
    /// </summary>
    public static class FileUtilities
    {
        /// <summary>
        /// A platform specific concrete implementation of the file system layer functions
        /// </summary>
        /// <remarks>
        /// When running on Windows but inside the CoreCLR, we use the same concrete implementation
        /// as the vanilla BuildXL build for Windos and skip Unix implementations completely
        /// </remarks>
        private static readonly IFileSystem s_fileSystem = OperatingSystemHelper.IsUnixOS
            ? (IFileSystem) new Unix.FileSystemUnix()
            : (IFileSystem) new Windows.FileSystemWin();

        /// <summary>
        /// A platform specific concrete implementation of I/O helpers and utilities
        /// </summary>
        /// <remarks>
        /// When running on Windows but inside the CoreCLR, we use the same concrete implementation
        /// as the vanilla BuildXL build for Windos and skip Unix implementations completely
        /// </remarks>
        private static readonly IFileUtilities s_fileUtilities = OperatingSystemHelper.IsUnixOS
            ? (IFileUtilities) new Unix.FileUtilitiesUnix()
            : (IFileUtilities) new Windows.FileUtilitiesWin();

        private static readonly ObjectPool<List<StringSegment>> StringSegmentListPool = Pools.CreateListPool<StringSegment>();

        /// <summary>
        /// Counters
        /// </summary>
        /// <remarks>
        /// As it is static, we reset it to prevent from having cumulative values between server-mode builds
        /// </remarks>
        public static CounterCollection<StorageCounters> Counters { get; private set; }

        /// <summary>
        /// Create a new collection of the storage counters.
        /// </summary>
        public static void CreateCounters()
        {
            Counters = new CounterCollection<StorageCounters>();
        }

        /// <summary>
        /// LocalLow user folder
        /// </summary>
        /// <remarks>
        /// http://msdn.microsoft.com/en-us/library/windows/desktop/dd378457(v=vs.85).aspx
        /// </remarks>
        [SuppressMessage("Microsoft.Usage", "CA2211")]
        public static Guid KnownFolderLocalLow = new Guid("A520A1A4-1780-4FF6-BD18-167343C5AF16");

        #region Directory related functions

        /// <summary>
        /// Creates all directories up to the given path
        /// </summary>
        /// <exception cref="BuildXLException">
        /// Thrown if the directory creation fails in a recoverable manner (e.g. access denied).
        /// </exception>
        public static void CreateDirectory(string path)
        {
            Contract.Requires(!string.IsNullOrEmpty(path));

            s_fileSystem.CreateDirectory(path);
        }

        /// <summary>
        /// Creates all directories up to the given path and retries a few times in case of failure
        /// </summary>
        /// <exception cref="BuildXLException">
        /// Thrown if the directory creation unrecoverably fails after exhaustive retry attempts.
        /// </exception>
        public static void CreateDirectoryWithRetry(string path)
        {
            Contract.Requires(!string.IsNullOrEmpty(path));

            bool success = Helpers.RetryOnFailure(
                finalRound =>
                {
                    try
                    {
                        CreateDirectory(path);
                        return true;
                    }
                    catch (Exception)
                    {
                        return false;
                    }
                });

            if (!success)
            {
                throw new BuildXLException("Create directory failed after exhausting retries. " + path);
            }
        }

        /// <see cref="IFileSystem.RemoveDirectory(string)"/>
        public static void RemoveDirectory(string path)
        {
            s_fileSystem.RemoveDirectory(path);
        }

        /// <see cref="IFileSystem.TryRemoveDirectory(string, out int)"/>
        public static bool TryRemoveDirectory(string path, out int hr)
        {
            return s_fileSystem.TryRemoveDirectory(path, out hr);
        }

        /// <see cref="IFileUtilities.DeleteDirectoryContents(string, bool, Func{string, bool}, ITempDirectoryCleaner)"/>
        public static void DeleteDirectoryContents(string path, bool deleteRootDirectory = false, Func<string, bool> shouldDelete = null, ITempDirectoryCleaner tempDirectoryCleaner = null) =>
            s_fileUtilities.DeleteDirectoryContents(path, deleteRootDirectory, shouldDelete, tempDirectoryCleaner);

        /// <see cref="IFileSystem.EnumerateDirectoryEntries(string, bool, Action{string, string, FileAttributes})"/>
        public static EnumerateDirectoryResult EnumerateDirectoryEntries(
            string directoryPath,
            bool recursive,
            Action<string, string, FileAttributes> handleEntry)
        {
            return s_fileSystem.EnumerateDirectoryEntries(directoryPath, recursive, handleEntry);
        }

        /// <see cref="IFileSystem.EnumerateDirectoryEntries(string, bool, string, Action{string, string, FileAttributes})"/>
        public static EnumerateDirectoryResult EnumerateDirectoryEntries(
            string directoryPath,
            bool recursive,
            string pattern,
            Action<string, string, FileAttributes> handleEntry)
        {
            return s_fileSystem.EnumerateDirectoryEntries(directoryPath, recursive, pattern, handleEntry);
        }

        /// <see cref="IFileSystem.EnumerateDirectoryEntries(string, Action{string, FileAttributes})"/>
        public static EnumerateDirectoryResult EnumerateDirectoryEntries(string directoryPath, Action<string, FileAttributes> handleEntry)
        {
            return EnumerateDirectoryEntries(directoryPath, false, (currentDirectory, fileName, fileAttributes) => handleEntry(fileName, fileAttributes));
        }

        /// <see cref="IFileSystem.EnumerateDirectoryEntries(string, bool, string, uint, bool, IDirectoryEntriesAccumulator)"/>
        public static EnumerateDirectoryResult EnumerateDirectoryEntries(
            string directoryPath,
            bool enumerateDirectory,
            string pattern,
            uint directoriesToSkipRecursively,
            bool recursive,
            IDirectoryEntriesAccumulator accumulators)
        {
            return s_fileSystem.EnumerateDirectoryEntries(directoryPath, enumerateDirectory, pattern, directoriesToSkipRecursively, recursive, accumulators);
        }

        /// <see cref="IFileUtilities.FindAllOpenHandlesInDirectory(string, HashSet{string})"/>
        public static string FindAllOpenHandlesInDirectory(string directoryPath, HashSet<string> pathsPossiblyPendingDelete = null) =>
            s_fileUtilities.FindAllOpenHandlesInDirectory(directoryPath, pathsPossiblyPendingDelete);

        /// <see cref="IFileSystem.TryOpenDirectory(string, FileDesiredAccess, FileShare, FileFlagsAndAttributes, out SafeFileHandle)"/>
        public static OpenFileResult TryOpenDirectory(
            string directoryPath,
            FileDesiredAccess desiredAccess,
            FileShare shareMode,
            FileFlagsAndAttributes flagsAndAttributes,
            out SafeFileHandle handle)
        {
            return s_fileSystem.TryOpenDirectory(directoryPath, desiredAccess, shareMode, flagsAndAttributes, out handle);
        }

        /// <see cref="IFileSystem.TryOpenDirectory(string, FileShare, out SafeFileHandle)"/>
        public static OpenFileResult TryOpenDirectory(string directoryPath, FileShare shareMode, out SafeFileHandle handle)
        {
            return s_fileSystem.TryOpenDirectory(directoryPath, shareMode, out handle);
        }

        #endregion

        #region File related functions

        /// <see cref="IFileUtilities.CopyFileAsync(string, string, Func{SafeFileHandle, SafeFileHandle, bool}, Action{SafeFileHandle, SafeFileHandle})"/>
        public static Task<bool> CopyFileAsync(
            string source,
            string destination,
            Func<SafeFileHandle, SafeFileHandle, bool> predicate = null,
            Action<SafeFileHandle, SafeFileHandle> onCompletion = null) => s_fileUtilities.CopyFileAsync(source, destination, predicate, onCompletion);

        /// <see cref="IFileUtilities.MoveFileAsync(string, string, bool)"/>
        public static Task<bool> MoveFileAsync(
            string source,
            string destination,
            bool replaceExisting = false) => s_fileUtilities.MoveFileAsync(source, destination, replaceExisting);

        /// <see cref="IFileUtilities.CreateReplacementFile(string, FileShare, bool, bool)"/>
        public static FileStream CreateReplacementFile(
            string path,
            FileShare fileShare,
            bool openAsync = true,
            bool allowExcludeFileShareDelete = false) => s_fileUtilities.CreateReplacementFile(path, fileShare, openAsync, allowExcludeFileShareDelete);

        /// <see cref="IFileUtilities.DeleteFile(string, bool, ITempDirectoryCleaner)"/>
        public static void DeleteFile(string path, bool waitUntilDeletionFinished = true, ITempDirectoryCleaner tempDirectoryCleaner = null) =>
            s_fileUtilities.DeleteFile(path, waitUntilDeletionFinished, tempDirectoryCleaner);

        /// <see cref="IFileUtilities.SkipPosixDelete"/>
        public static bool SkipPosixDelete
        {
            get { return s_fileUtilities.SkipPosixDelete; }
            set { s_fileUtilities.SkipPosixDelete = value; }
        }

        /// <see cref="IFileUtilities.TryDeleteFile(string, bool, ITempDirectoryCleaner)"/>
        public static Possible<Unit, RecoverableExceptionFailure> TryDeleteFile(string path, bool waitUntilDeletionFinished = true, ITempDirectoryCleaner tempDirectoryCleaner = null) =>
            s_fileUtilities.TryDeleteFile(path, waitUntilDeletionFinished, tempDirectoryCleaner);

        /// <summary>
        /// Tries to delete file or directory if exists.
        /// </summary>
        /// <param name="fileOrDirectoryPath">Path to file or directory to be deleted, if exists.</param>
        /// <param name="tempDirectoryCleaner">Temporary directory cleaner.</param>
        public static Possible<Unit, Failure> TryDeletePathIfExists(string fileOrDirectoryPath, ITempDirectoryCleaner tempDirectoryCleaner = null)
        {
            if (FileExistsNoFollow(fileOrDirectoryPath))
            {
                Possible<Unit, RecoverableExceptionFailure> possibleDeletion = TryDeleteFile(
                    fileOrDirectoryPath,
                    waitUntilDeletionFinished: true,
                    tempDirectoryCleaner: tempDirectoryCleaner);

                if (!possibleDeletion.Succeeded)
                {
                    return possibleDeletion.WithGenericFailure();
                }
            }
            else if (DirectoryExistsNoFollow(fileOrDirectoryPath))
            {
                DeleteDirectoryContents(fileOrDirectoryPath, deleteRootDirectory: true, tempDirectoryCleaner: tempDirectoryCleaner);
            }

            return Unit.Void;
        }

        /// <summary>
        /// Returns true if given file attributes denote a real directory.
        /// A symlink pointing to a directory (Directory | ReparsePoint) is not considered a directory.
        /// </summary>
        public static bool IsDirectoryNoFollow(FileAttributes attributes)
        {
            return
                ((attributes & FileAttributes.Directory) == FileAttributes.Directory) &&
                ((attributes & FileAttributes.ReparsePoint) == 0);
        }

        /// <summary>
        /// Checks if a directory exists.
        /// </summary>
        /// <remarks>
        /// Doesn't follow the symlink if <paramref name="path"/> is a symlink.  If you want the behavior
        /// that follows symlinks, use <see cref="Directory.Exists(string)"/>.
        /// </remarks>
        public static bool DirectoryExistsNoFollow(string path)
        {
            var maybeExistence = TryProbePathExistence(path, followSymlink: false);
            return maybeExistence.Succeeded && maybeExistence.Result == PathExistence.ExistsAsDirectory;
        }

        /// <summary>
        /// Checks if a file exists.
        /// </summary>
        /// <remarks>
        /// Doesn't follow the symlink if <paramref name="path"/> is a symlink.
        /// </remarks>
        public static bool FileExistsNoFollow(string path)
        {
            var maybeExistence = TryProbePathExistence(path, followSymlink: false);
            return maybeExistence.Succeeded && maybeExistence.Result == PathExistence.ExistsAsFile;
        }

        /// <see cref="IFileUtilities.TryMoveDelete(string, string)"/>
        public static bool TryMoveDelete(string path, string deletionTempDirectory) => s_fileUtilities.TryMoveDelete(path, deletionTempDirectory);

        /// <see cref="IFileUtilities.GetFileName(string)"/>
        public static Possible<string> GetFileName(string path) => s_fileUtilities.GetFileName(path);

        /// <see cref="IFileUtilities.GetFileTimestamps"/>
        public static FileTimestamps GetFileTimestamps(string path, bool followSymlink = false)
            => s_fileUtilities.GetFileTimestamps(path, followSymlink);

        /// <see cref="IFileUtilities.SetFileTimestamps"/>
        public static void SetFileTimestamps(string path, FileTimestamps timestamps, bool followSymlink = false)
            => s_fileUtilities.SetFileTimestamps(path, timestamps, followSymlink);

        /// <see cref="IFileUtilities.WriteAllTextAsync(string, string, Encoding)"/>
        public static Task WriteAllTextAsync(
            string filePath,
            string text,
            Encoding encoding) => s_fileUtilities.WriteAllTextAsync(filePath, text, encoding);

        /// <see cref="IFileUtilities.WriteAllBytesAsync(string, byte[], Func{SafeFileHandle, bool}, Action{SafeFileHandle})"/>
        public static Task<bool> WriteAllBytesAsync(
            string filePath,
            byte[] bytes,
            Func<SafeFileHandle, bool> predicate = null,
            Action<SafeFileHandle> onCompletion = null) => s_fileUtilities.WriteAllBytesAsync(filePath, bytes, predicate, onCompletion);

        /// <see cref="IFileUtilities.TryFindOpenHandlesToFile(string, out string)"/>
        public static bool TryFindOpenHandlesToFile(string filePath, out string diagnosticInfo) => s_fileUtilities.TryFindOpenHandlesToFile(filePath, out diagnosticInfo);

        /// <see cref="IFileUtilities.GetHardLinkCount(string)"/>
        public static uint GetHardLinkCount(string path) => s_fileUtilities.GetHardLinkCount(path);

        /// <see cref="IFileUtilities.HasWritableAccessControl(string)"/>
        public static bool HasWritableAccessControl(string path) => s_fileUtilities.HasWritableAccessControl(path);

        /// <see cref="IFileUtilities.CreateFileStream(string, FileMode, FileAccess, FileShare, FileOptions, bool, bool)"/>
        public static FileStream CreateFileStream(
            string path,
            FileMode fileMode,
            FileAccess fileAccess,
            FileShare fileShare,
            FileOptions options = FileOptions.None,
            bool force = false,
            bool allowExcludeFileShareDelete = false)
        {
            return s_fileUtilities.CreateFileStream(path, fileMode, fileAccess, fileShare, options, force, allowExcludeFileShareDelete);
        }

        /// <see cref="IFileUtilities.CreateAsyncFileStream(string, FileMode, FileAccess, FileShare, FileOptions, bool, bool)"/>
        public static FileStream CreateAsyncFileStream(
            string path,
            FileMode fileMode,
            FileAccess fileAccess,
            FileShare fileShare,
            FileOptions options = FileOptions.None,
            bool force = false,
            bool allowExcludeFileShareDelete = false)
        {
            return s_fileUtilities.CreateAsyncFileStream(path, fileMode, fileAccess, fileShare, options, force, allowExcludeFileShareDelete);
        }

        /// <see cref="IFileUtilities.UsingFileHandleAndFileLength"/>
        public static TResult UsingFileHandleAndFileLength<TResult>(
            string path,
            FileDesiredAccess desiredAccess,
            FileShare shareMode,
            FileMode creationDisposition,
            FileFlagsAndAttributes flagsAndAttributes,
            Func<SafeFileHandle, long, TResult> handleStream)
            =>
                s_fileUtilities.UsingFileHandleAndFileLength(
                    path,
                    desiredAccess,
                    shareMode,
                    creationDisposition,
                    flagsAndAttributes,
                    handleStream);

        /// <summary>
        /// Tries to duplicate a file.
        /// </summary>
        public static Task<FileDuplicationResult> TryDuplicateOneFileAsync(string sourcePath, string destinationPath)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(sourcePath));
            Contract.Requires(!string.IsNullOrWhiteSpace(destinationPath));

            if (string.Compare(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase) == 0)
            {
                return Task.FromResult(FileDuplicationResult.Existed); // Nothing to do.
            }

            return ExceptionUtilities.HandleRecoverableIOExceptionAsync(
                async () =>
                {
                    var destinationDirectory = Path.GetDirectoryName(destinationPath);
                    if (DirectoryExistsNoFollow(destinationDirectory))
                    {
                        if (FileExistsNoFollow(destinationPath))
                        {
                            DeleteFile(destinationPath);
                        }
                    }
                    else
                    {
                        CreateDirectory(destinationDirectory);
                    }

                    // Our implementation on macOS requires APFS which has copy on write. There's no advantage to using
                    // a hardlink over a file copy. VSTS 1333283
                    if (!OperatingSystemHelper.IsUnixOS)
                    {
                        var hardlinkResult = s_fileSystem.TryCreateHardLinkViaSetInformationFile(destinationPath, sourcePath);

                        if (hardlinkResult == CreateHardLinkStatus.Success)
                        {
                            return FileDuplicationResult.Hardlinked;
                        }
                    }

                    await CopyFileAsync(sourcePath, destinationPath);
                    return FileDuplicationResult.Copied;
                },
                ex => { throw new BuildXLException(ex.Message); });
        }

        /// <see cref="IFileSystem.TryCreateOrOpenFile(string, FileDesiredAccess, FileShare, FileMode, FileFlagsAndAttributes, out SafeFileHandle)"/>
        public static OpenFileResult TryCreateOrOpenFile(
            string path,
            FileDesiredAccess desiredAccess,
            FileShare shareMode,
            FileMode creationDisposition,
            FileFlagsAndAttributes flagsAndAttributes,
            out SafeFileHandle handle)
        {
            return s_fileSystem.TryCreateOrOpenFile(path, desiredAccess, shareMode, creationDisposition, flagsAndAttributes, out handle);
        }

        /// <see cref="IFileSystem.TryOpenFileById(SafeFileHandle, FileId, FileDesiredAccess, FileShare, FileFlagsAndAttributes, out SafeFileHandle)"/>
        public static OpenFileResult TryOpenFileById(
            SafeFileHandle existingHandleOnVolume,
            FileId fileId,
            FileDesiredAccess desiredAccess,
            FileShare shareMode,
            FileFlagsAndAttributes flagsAndAttributes,
            out SafeFileHandle handle)
        {
            return s_fileSystem.TryOpenFileById(existingHandleOnVolume, fileId, desiredAccess, shareMode, flagsAndAttributes, out handle);
        }

        /// <see cref="IFileSystem.TryReOpenFile(SafeFileHandle, FileDesiredAccess, FileShare, FileFlagsAndAttributes, out SafeFileHandle)"/>
        public static ReOpenFileStatus TryReOpenFile(
            SafeFileHandle existing,
            FileDesiredAccess desiredAccess,
            FileShare shareMode,
            FileFlagsAndAttributes flagsAndAttributes,
            out SafeFileHandle reopenedHandle)
        {
            return s_fileSystem.TryReOpenFile(existing, desiredAccess, shareMode, flagsAndAttributes, out reopenedHandle);
        }

        /// <see cref="IFileSystem.TryPosixDelete(string, out OpenFileResult)"/>
        public static unsafe bool TryPosixDelete(string pathToDelete, out OpenFileResult openFileResult)
        {
            return s_fileSystem.TryPosixDelete(pathToDelete, out openFileResult);
        }

        /// <see cref="IFileSystem.TrySetDeletionDisposition(SafeFileHandle)"/>
        public static unsafe bool TrySetDeletionDisposition(SafeFileHandle handle)
        {
            return s_fileSystem.TrySetDeletionDisposition(handle);
        }

        /// <see cref="IFileSystem.GetFileFlagsAndAttributesForPossibleReparsePoint"/>
        public static FileFlagsAndAttributes GetFileFlagsAndAttributesForPossibleReparsePoint(string expandedPath)
        {
            using (Counters?.StartStopwatch(StorageCounters.GetFileFlagsAndAttributesForPossibleReparsePointDuration))
            {
                return s_fileSystem.GetFileFlagsAndAttributesForPossibleReparsePoint(expandedPath);
            }
        }

        /// <see cref="IFileSystem.GetFileAttributesByHandle(SafeFileHandle)"/>
        public static unsafe FileAttributes GetFileAttributesByHandle(SafeFileHandle fileHandle)
        {
            using (Counters?.StartStopwatch(StorageCounters.GetFileAttributesByHandleDuration))
            {
                return s_fileSystem.GetFileAttributesByHandle(fileHandle);
            }
        }

        /// <see cref="IFileSystem.SetFileAttributes(string, FileAttributes)"/>
        public static void SetFileAttributes(string path, FileAttributes attributes)
        {
            s_fileSystem.SetFileAttributes(path, attributes);
        }

        /// <see cref="IFileUtilities.SetFileAccessControl(string, FileSystemRights, bool)"/>
        public static void SetFileAccessControl(string path, FileSystemRights fileSystemRights, bool allow)
        {
            s_fileUtilities.SetFileAccessControl(path, fileSystemRights, allow);
        }

        #endregion

        #region General file and directory utilities

        /// <see cref="IFileUtilities.Exists(string)"/>
        public static bool Exists(string path) => s_fileUtilities.Exists(path);

        /// <see cref="IFileUtilities.DoesLogicalDriveHaveSeekPenalty(char)"/>
        public static bool? DoesLogicalDriveHaveSeekPenalty(char driveLetter) => s_fileUtilities.DoesLogicalDriveHaveSeekPenalty(driveLetter);

        /// <see cref="IFileUtilities.GetKnownFolderPath(Guid)"/>
        public static string GetKnownFolderPath(Guid knownFolder) => s_fileUtilities.GetKnownFolderPath(knownFolder);

        /// <see cref="IFileUtilities.GetUserSettingsFolder(string)"/>
        public static string GetUserSettingsFolder(string appName) => s_fileUtilities.GetUserSettingsFolder(appName);

        #endregion

        #region Soft- (Junction) and Hardlink functions

        /// <see cref="IFileSystem.CreateJunction(string, string)"/>
        public static void CreateJunction(string junctionPoint, string targetDir)
        {
            s_fileSystem.CreateJunction(junctionPoint, targetDir);
        }

        /// <see cref="IFileSystem.TryCreateSymbolicLink(string, string, bool)"/>
        public static bool TryCreateSymbolicLink(string symLinkFileName, string targetFileName, bool isTargetFile)
        {
            return s_fileSystem.TryCreateSymbolicLink(symLinkFileName, targetFileName, isTargetFile);
        }

        /// <summary>
        /// Tries to create a symlink if it does not exist or targets do not match.
        /// </summary>
        public static bool TryCreateSymlinkIfNotExistsOrTargetsDoNotMatch(string symlink, string symlinkTarget, bool isTargetFile, out bool created)
        {
            created = false;
            var possibleReparsePoint = TryGetReparsePointType(symlink);

            bool shouldCreate = true;

            if (possibleReparsePoint.Succeeded && IsReparsePointActionable(possibleReparsePoint.Result))
            {
                var openResult = TryCreateOrOpenFile(
                    symlink,
                    FileDesiredAccess.GenericRead,
                    FileShare.Read | FileShare.Delete,
                    FileMode.Open,
                    FileFlagsAndAttributes.FileFlagOverlapped | FileFlagsAndAttributes.FileFlagOpenReparsePoint,
                    out SafeFileHandle handle);

                if (openResult.Succeeded)
                {
                    using (handle)
                    {
                        // Do not attempt to convert the target path to absolute path - always compare raw targets
                        var possibleExistingSymlinkTarget = TryGetReparsePointTarget(handle, symlink);

                        if (possibleExistingSymlinkTarget.Succeeded)
                        {
                            shouldCreate = !string.Equals(symlinkTarget, possibleExistingSymlinkTarget.Result, StringComparison.OrdinalIgnoreCase);
                        }
                    }
                }
            }

            if (shouldCreate)
            {
                s_fileUtilities.DeleteFile(symlink, waitUntilDeletionFinished: true);
                CreateDirectory(Path.GetDirectoryName(symlink));

                if (!TryCreateSymbolicLink(symlink, symlinkTarget, isTargetFile: isTargetFile))
                {
                    return false;
                }

                created = true;
            }

            return true;
        }

        /// <see cref="IFileSystem.TryCreateHardLink(string, string)"/>
        public static CreateHardLinkStatus TryCreateHardLink(string link, string linkTarget)
        {
            return s_fileSystem.TryCreateHardLink(link, linkTarget);
        }

        /// <see cref="IFileSystem.TryCreateHardLinkViaSetInformationFile(string, string, bool)"/>
        public static CreateHardLinkStatus TryCreateHardLinkViaSetInformationFile(string link, string linkTarget, bool replaceExisting = true)
        {
            return s_fileSystem.TryCreateHardLinkViaSetInformationFile(link, linkTarget, replaceExisting);
        }

        /// <see cref="IFileSystem.IsReparsePointActionable(ReparsePointType)"/>
        public static bool IsReparsePointActionable(ReparsePointType reparsePointType)
        {
            return s_fileSystem.IsReparsePointActionable(reparsePointType);
        }

        /// <see cref="IFileSystem.TryGetReparsePointType(string)"/>
        public static Possible<ReparsePointType> TryGetReparsePointType(string path)
        {
            using (Counters?.StartStopwatch(StorageCounters.GetReparsePointTypeDuration))
            {
                return s_fileSystem.TryGetReparsePointType(path);
            }
        }

        /// <see cref="IFileSystem.IsWciReparsePoint(string)"/>
        public static bool IsWciReparsePoint(string path)
        {
            return s_fileSystem.IsWciReparsePoint(path);
        }

        /// <see cref="IFileSystem.GetChainOfReparsePoints(SafeFileHandle, string, IList{string})"/>
        public static void GetChainOfReparsePoints(SafeFileHandle handle, string sourcePath, IList<string> chainOfReparsePoints)
        {
            s_fileSystem.GetChainOfReparsePoints(handle, sourcePath, chainOfReparsePoints);
        }

        /// <see cref="IFileSystem.TryGetReparsePointTarget(SafeFileHandle, string)"/>
        public static Possible<string> TryGetReparsePointTarget(SafeFileHandle handle, string sourcePath)
        {
            return s_fileSystem.TryGetReparsePointTarget(handle, sourcePath);
        }

#endregion

#region Journaling functions

        /// <see cref="IFileSystem.ReadFileUsnByHandle(SafeFileHandle, bool)"/>
        public static unsafe MiniUsnRecord? ReadFileUsnByHandle(SafeFileHandle fileHandle, bool forceJournalVersion2 = false)
        {
            using (Counters?.StartStopwatch(StorageCounters.ReadFileUsnByHandleDuration))
            {
                return s_fileSystem.ReadFileUsnByHandle(fileHandle, forceJournalVersion2);
            }
        }

        /// <see cref="IFileSystem.TryReadUsnJournal(SafeFileHandle, byte[], ulong, Usn, bool, bool)"/>
        public static unsafe ReadUsnJournalResult TryReadUsnJournal(
            SafeFileHandle volumeHandle,
            byte[] buffer,
            ulong journalId,
            Usn startUsn = default(Usn),
            bool forceJournalVersion2 = false,
            bool isJournalUnprivileged = false)
        {
            using (Counters?.StartStopwatch(StorageCounters.ReadUsnJournalDuration))
            {
                return s_fileSystem.TryReadUsnJournal(volumeHandle, buffer, journalId, startUsn, forceJournalVersion2, isJournalUnprivileged);
            }
        }

        /// <see cref="IFileSystem.TryQueryUsnJournal(SafeFileHandle)"/>
        public static QueryUsnJournalResult TryQueryUsnJournal(SafeFileHandle volumeHandle)
        {
            return s_fileSystem.TryQueryUsnJournal(volumeHandle);
        }

        /// <see cref="IFileSystem.TryWriteUsnCloseRecordByHandle(SafeFileHandle)"/>
        public static unsafe Usn? TryWriteUsnCloseRecordByHandle(SafeFileHandle fileHandle)
        {
            using (Counters?.StartStopwatch(StorageCounters.WriteUsnCloseRecordByHandleDuration))
            {
                return s_fileSystem.TryWriteUsnCloseRecordByHandle(fileHandle);
            }
        }

#endregion

#region Volume handling functions

        /// <see cref="IFileSystem.ListVolumeGuidPathsAndSerials"/>
        public static List<Tuple<VolumeGuidPath, ulong>> ListVolumeGuidPathsAndSerials()
        {
            return s_fileSystem.ListVolumeGuidPathsAndSerials();
        }

        /// <see cref="IFileSystem.GetVolumeFileSystemByHandle(SafeFileHandle)"/>
        public static FileSystemType GetVolumeFileSystemByHandle(SafeFileHandle fileHandle)
        {
            return s_fileSystem.GetVolumeFileSystemByHandle(fileHandle);
        }

        /// <see cref="IFileSystem.GetShortVolumeSerialNumberByHandle(SafeFileHandle)"/>
        public static unsafe uint GetShortVolumeSerialNumberByHandle(SafeFileHandle fileHandle)
        {
            return s_fileSystem.GetShortVolumeSerialNumberByHandle(fileHandle);
        }

        /// <see cref="IFileSystem.GetVolumeFileSystemByHandle(SafeFileHandle)"/>
        public static ulong GetVolumeSerialNumberByHandle(SafeFileHandle fileHandle)
        {
            return s_fileSystem.GetVolumeSerialNumberByHandle(fileHandle);
        }

        /// <see cref="IFileSystem.TryGetFileIdAndVolumeIdByHandle(SafeFileHandle)"/>
        public static unsafe FileIdAndVolumeId? TryGetFileIdAndVolumeIdByHandle(SafeFileHandle fileHandle)
        {
            return s_fileSystem.TryGetFileIdAndVolumeIdByHandle(fileHandle);
        }

        /// <see cref="IFileSystem.IsVolumeMapped(string)"/>
        public static bool IsVolumeMapped(string volume) => s_fileSystem.IsVolumeMapped(volume);

#endregion

#region Generic file system helpers
        /// <see cref="IFileSystem.MaxDirectoryPathLength"/>
        public static int MaxDirectoryPathLength()
        {
            return s_fileSystem.MaxDirectoryPathLength();
        }

        /// <see cref="IFileSystem.TryProbePathExistence(string, bool)"/>
        public static Possible<PathExistence, NativeFailure> TryProbePathExistence(string path, bool followSymlink)
        {
            return s_fileSystem.TryProbePathExistence(path, followSymlink);
        }

        /// <see cref="IFileSystem.PathMatchPattern"/>
        public static bool PathMatchPattern(string path, string pattern)
        {
            return s_fileSystem.PathMatchPattern(path, pattern);
        }

        /// <see cref="IFileSystem.IsPendingDelete(SafeFileHandle)"/>
        public static unsafe bool IsPendingDelete(SafeFileHandle fileHandle)
        {
            return s_fileSystem.IsPendingDelete(fileHandle);
        }

        /// <see cref="IFileSystem.GetFinalPathNameByHandle(SafeFileHandle, bool)"/>
        public static string GetFinalPathNameByHandle(SafeFileHandle handle, bool volumeGuidPath = false)
        {
            return s_fileSystem.GetFinalPathNameByHandle(handle, volumeGuidPath);
        }

        /// <see cref="IFileSystem.FlushPageCacheToFilesystem(SafeFileHandle)"/>
        public static unsafe NtStatus FlushPageCacheToFilesystem(SafeFileHandle handle)
        {
            return s_fileSystem.FlushPageCacheToFilesystem(handle);
        }

        /// <summary>
        /// Determines whether the file system is case sensitive.
        /// </summary>
        /// <remarks>
        /// Ideally we'd use something like pathconf with _PC_CASE_SENSITIVE, but that is non-portable,
        /// not supported on Windows or Linux, etc. For now, this function creates a tmp file with capital letters
        /// and then tests for its existence with lower-case letters.  This could return invalid results in corner
        /// cases where, for example, different file systems are mounted with differing sensitivities.
        /// See: https://github.com/dotnet/corefx/blob/1bff7880bfa949e8c5e46039808ec412640bbb5e/src/Common/src/System/IO/PathInternal.CaseSensitivity.cs#L41
        /// </remarks>
        public static bool IsFileSystemCaseSensitive()
        {
            try
            {
                string pathWithUpperCase = Path.Combine(Path.GetTempPath(), "CASESENSITIVETEST" + Guid.NewGuid().ToString("N"));
                using (new FileStream(pathWithUpperCase, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 0x1000, FileOptions.DeleteOnClose))
                {
                    string lowerCased = pathWithUpperCase.ToLowerInvariant();
                    return !FileExistsNoFollow(lowerCased);
                }
            }
            catch (Exception)
            {
                // In case something goes terribly wrong, we don't want to fail just because
                // of a casing test, so we assume case-insensitive.
                return false;
            }
        }

        /// <summary>
        /// Tries to convert a reparse point target path to its absolute path representation. If a supplied
        /// <paramref name="reparsePointTargetPath"/> is not a relative path, it's returned unchanged.
        /// </summary>
        public static string ConvertReparsePointTargetPathToAbsolutePath(string reparsePointPath, string reparsePointTargetPath)
        {
            // NOTE: Don't use anything from .NET Path that causes failure in handling long path.

            bool startWithDotSlash = reparsePointTargetPath.Length >= 2
                                     && reparsePointTargetPath[0] == '.'
                                     && (reparsePointTargetPath[1] == Path.DirectorySeparatorChar
                                         || reparsePointTargetPath[1] == Path.AltDirectorySeparatorChar);
            bool startWithDotDotSlash = reparsePointTargetPath.Length >= 3
                                        && reparsePointTargetPath[0] == '.'
                                        && reparsePointTargetPath[1] == '.'
                                        && (reparsePointTargetPath[2] == Path.DirectorySeparatorChar
                                            || reparsePointTargetPath[2] == Path.AltDirectorySeparatorChar);

            if (!startWithDotSlash && !startWithDotDotSlash)
            {
                // reparsePointTargetPath is already an absolute path
                return reparsePointTargetPath;
            }

            var possibleVolumeBoundary = VerifyReparsePointPathAndIdentifyVolumeBoundary(reparsePointPath);
            if (!possibleVolumeBoundary.Succeeded)
            {
                throw new BuildXLException(CreateErrorMessage(possibleVolumeBoundary.Failure.Describe()));
            }

            int volumeBoundary = possibleVolumeBoundary.Result;

            StringSegment reparsePoint = reparsePointPath;
            StringSegment reparsePointTarget = reparsePointTargetPath;

            using (var wrapper = StringSegmentListPool.GetInstance())
            {
                List<StringSegment> components = wrapper.Instance;

                var possibleResult = ParseAndAddPathComponents(components, CharSpan.Skip(reparsePoint, volumeBoundary));
                if (!possibleResult.Succeeded)
                {
                    throw new BuildXLException(CreateErrorMessage(possibleResult.Failure.Describe()));
                }

                // there must be at least one path component
                Contract.Assert(components.Count > 0);

                // we need to remove the last path component (we are 'replacing' it with target path components)
                components.RemoveAt(components.Count - 1);

                possibleResult = ParseAndAddPathComponents(components, reparsePointTarget);
                if (!possibleResult.Succeeded)
                {
                    throw new BuildXLException(CreateErrorMessage(possibleResult.Failure.Describe()));
                }

                using (var sbWrapper = Pools.GetStringBuilder())
                {
                    StringBuilder builder = sbWrapper.Instance;

                    reparsePoint.Subsegment(0, volumeBoundary).CopyTo(builder);

                    foreach (var pathSegment in components)
                    {
                        builder.Append(Path.DirectorySeparatorChar);
                        pathSegment.CopyTo(builder);
                    }

                    return builder.ToString();
                }
            }

            string CreateErrorMessage(string detailedMessage)
            {
                return I($"Failed to convert reparse point target path to absolute path ('{reparsePointPath}' -> '{reparsePointTargetPath}'). {detailedMessage}");
            }
        }

        /// <summary>
        /// Tries to identify the boundary of a volume in a given path. Volume here is a path component that does not have a parent.
        /// </summary>
        private static Possible<int> VerifyReparsePointPathAndIdentifyVolumeBoundary(string reparsePointPath)
        {
            // Unix path
            if (OperatingSystemHelper.IsUnixOS && reparsePointPath.Length >= 1 && reparsePointPath[0] == Path.VolumeSeparatorChar)
            {
                return 0;
            }

            // Local drive letter path (e.g., C:\)
            if (reparsePointPath.Length >= 3
                     && char.IsLetter(reparsePointPath[0])
                     && reparsePointPath[1] == Path.VolumeSeparatorChar
                     && (reparsePointPath[2] == Path.DirectorySeparatorChar || reparsePointPath[2] == Path.AltDirectorySeparatorChar))
            {
                return 2;
            }

            // Prefixed path (\\?\, \??\, \\.\) or UNC path
            if (reparsePointPath.Length > 4
                     && (reparsePointPath[0] == Path.DirectorySeparatorChar || reparsePointPath[0] == Path.AltDirectorySeparatorChar))
            {
                if (reparsePointPath[0] == reparsePointPath[3]
                    && (reparsePointPath[1] == reparsePointPath[0] && reparsePointPath[2] == '?'        // \\?\
                        || reparsePointPath[1] == reparsePointPath[2] && reparsePointPath[2] == '?'     // \??\
                        || reparsePointPath[1] == reparsePointPath[0] && reparsePointPath[2] == '.'))   // \\.\
                {
                    // \\?\C:\
                    //       ^
                    int volumeBoundary = reparsePointPath.IndexOf(reparsePointPath[0], 4);
                    if (volumeBoundary == -1)
                    {
                        return new Failure<string>("Failed to identify a volume boundary in a prefixed path.");
                    }

                    return volumeBoundary;
                }

                if (reparsePointPath[0] == reparsePointPath[1])
                {
                    // \\server\share\
                    //               ^
                    int volumeBoundary = reparsePointPath.IndexOf(reparsePointPath[0], 2);                  // server boundary
                    if (volumeBoundary == -1)
                    {
                        return new Failure<string>("Failed to identify a server boundary in a prefixed path.");
                    }

                    volumeBoundary = reparsePointPath.IndexOf(reparsePointPath[0], volumeBoundary + 1); // share boundary
                    if (volumeBoundary == -1)
                    {
                        return new Failure<string>("Failed to identify a share boundary in a prefixed path.");
                    }

                    return volumeBoundary;
                }
            }

            return new Failure<string>("Invalid reparse point path.");
        }

        /// <summary>
        /// Splits a given path into components and adds them into given list. For every path component that is parent directory (i.e., '..'),
        /// removes a corresponding element from the list.
        /// </summary>
        /// <remarks>This method was adopted from BuildXL.Utilities.RelativePath.TryCreate</remarks>
        private static Possible<Unit> ParseAndAddPathComponents(List<StringSegment> pathComponents, StringSegment path)
        {
            int index = 0;
            int start = 0;
            while (index < path.Length)
            {
                var ch = path[index];

                if (ch == '\\' || ch == '/')
                {
                    // found a component separator
                    if (index > start)
                    {
                        // make a path component out of [start..index]
                        pathComponents.Add(path.Subsegment(start, index - start));
                    }

                    // skip over the slash
                    index++;
                    start = index;
                    continue;
                }

                if (ch == '.' && index == start)
                {
                    // component starts with a .
                    if ((index == path.Length - 1)
                        || (path[index + 1] == '\\')
                        || (path[index + 1] == '/'))
                    {
                        // component is a sole . so skip it
                        index += 2;
                        start = index;
                        continue;
                    }

                    if (path[index + 1] == '.')
                    {
                        // component starts with ..
                        if ((index == path.Length - 2)
                            || (path[index + 2] == '\\')
                            || (path[index + 2] == '/'))
                        {
                            // component is a sole .. so try to go up
                            if (pathComponents.Count == 0)
                            {
                                return new Failure<string>("Attempted to navigate above the volume.");
                            }

                            pathComponents.RemoveAt(pathComponents.Count - 1);

                            index += 3;
                            start = index;
                            continue;
                        }
                    }
                }

                index++;
            }

            if (index > start)
            {
                // make a path component out of [start..index]
                pathComponents.Add(path.Subsegment(start, index - start));
            }

            return Unit.Void;
        }

        #endregion
    }
}
