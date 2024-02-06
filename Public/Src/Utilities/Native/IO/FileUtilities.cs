// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Native.IO.Windows;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Core.Tasks;
using Microsoft.Win32.SafeHandles;
using static BuildXL.Utilities.Core.FormattableStringEx;
using UnixIO = BuildXL.Interop.Unix.IO;
using BuildXL.Utilities.Collections;

namespace BuildXL.Native.IO
{
    /// <summary>
    /// Due to the clr's various static initialization order of members this one has to be held
    /// in a separate static class so that setting this does not intiialize the members that depend on the
    /// overwritten version of the LoggingContext
    /// </summary>
    public static class FileUtilitiesStaticLoggingContext
    {
        /// <summary>
        /// The loggingcontext to use for file operations.
        /// Anybody using the static filesystem MUST override this static member before calling any methods;
        /// </summary>
        public static LoggingContext LoggingContext = new LoggingContext("FileUtilities");
    }

    /// <summary>
    /// Static facade with utilities for manipulating files and directories. Also offers functions for directly calling filesystem level functionality.
    /// Serves as an entry point for direct I/O throughout BuildXL's code base and proxies its calls to platform specific implementations of IFileSystem and IFileUtilities.
    /// </summary>
    public static class FileUtilities
    {
        private static LoggingContext LoggingContext => FileUtilitiesStaticLoggingContext.LoggingContext;

        /// <summary>
        /// A platform specific concrete implementation of I/O helpers and utilities
        /// </summary>
        /// <remarks>
        /// When running on Windows but inside the CoreCLR, we use the same concrete implementation
        /// as the vanilla BuildXL build for Windows and skip Unix implementations completely
        /// </remarks>
        internal static readonly IFileUtilities OsFileUtilities = OperatingSystemHelper.IsUnixOS
            ? new Unix.FileUtilitiesUnix()
            : new Windows.FileUtilitiesWin(LoggingContext);

        /// <summary>
        /// A platform specific concrete implementation of the file system layer functions
        /// </summary>
        /// <remarks>
        /// When running on Windows but inside the CoreCLR, we use the same concrete implementation
        /// as the vanilla BuildXL build for Windows and skip Unix implementations completely
        /// </remarks>
        internal static readonly IFileSystem OsFileSystem = OperatingSystemHelper.IsUnixOS
            ? ((Unix.FileUtilitiesUnix)OsFileUtilities).FileSystem
            : ((Windows.FileUtilitiesWin)OsFileUtilities).FileSystem;

        /// <summary>
        /// Directory separator as string.
        /// </summary>
        public static readonly string DirectorySeparatorString = Path.DirectorySeparatorChar.ToString();

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
        /// This method also throws an Exception when the target to the directory symlink does not exist, this can lead to a failure in directory creation.
        /// </exception>
        public static void CreateDirectory(string path)
        {
            Contract.Requires(!string.IsNullOrEmpty(path));
            OsFileSystem.CreateDirectory(path);
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
                    CreateDirectory(path);
                    return true;
                });

            if (!success)
            {
                throw new BuildXLException("Create directory failed after exhausting retries. " + path);
            }
        }

        /// <see cref="IFileSystem.RemoveDirectory(string)"/>
        public static void RemoveDirectory(string path)
        {
            OsFileSystem.RemoveDirectory(path);
        }

        /// <see cref="IFileSystem.TryRemoveDirectory(string, out int)"/>
        public static bool TryRemoveDirectory(string path, out int hr)
        {
            return OsFileSystem.TryRemoveDirectory(path, out hr);
        }

        /// <see cref="IFileUtilities.DeleteDirectoryContents(string, bool, Func{string, bool, bool}, ITempCleaner, bool, CancellationToken?)"/>
        public static void DeleteDirectoryContents(
            string path,
            bool deleteRootDirectory = false,
            Func<string, bool, bool> shouldDelete = null,
            ITempCleaner tempDirectoryCleaner = null,
            bool bestEffort = false,
            CancellationToken? cancellationToken = default) =>
            OsFileUtilities.DeleteDirectoryContents(path, deleteRootDirectory, shouldDelete, tempDirectoryCleaner, bestEffort, cancellationToken);

        /// <see cref="IFileSystem.EnumerateDirectoryEntries(string, bool, Action{string, string, FileAttributes}, bool)"/>
        public static EnumerateDirectoryResult EnumerateDirectoryEntries(
            string directoryPath,
            bool recursive,
            Action<string, string, FileAttributes> handleEntry)
        {
            return OsFileSystem.EnumerateDirectoryEntries(directoryPath, recursive, handleEntry);
        }

        /// <see cref="IFileSystem.EnumerateDirectoryEntries(string, bool, string, Action{string, string, FileAttributes}, bool, bool)"/>
        public static EnumerateDirectoryResult EnumerateDirectoryEntries(
            string directoryPath,
            bool recursive,
            string pattern,
            Action<string, string, FileAttributes> handleEntry)
        {
            return OsFileSystem.EnumerateDirectoryEntries(directoryPath, recursive, pattern, handleEntry, followSymlinksToDirectories: true);
        }

        /// <see cref="IFileSystem.EnumerateFiles(string, bool, string, Action{string, string, FileAttributes, long})"/>
        public static EnumerateDirectoryResult EnumerateFiles(
            string directoryPath,
            bool recursive,
            string pattern,
            Action<string /*filePath*/, string /*fileName*/, FileAttributes /*attributes*/, long /*fileSize*/> handleFileEntry)
        {
            return OsFileSystem.EnumerateFiles(directoryPath, recursive, pattern, handleFileEntry);
        }

        /// <see cref="IFileSystem.EnumerateDirectoryEntries(string, Action{string, FileAttributes}, bool)"/>
        public static EnumerateDirectoryResult EnumerateDirectoryEntries(string directoryPath, Action<string, FileAttributes> handleEntry)
        {
            return EnumerateDirectoryEntries(directoryPath, false, (currentDirectory, fileName, fileAttributes) => handleEntry(fileName, fileAttributes));
        }

        /// <see cref="IFileSystem.EnumerateDirectoryEntries(string, bool, string, uint, bool, IDirectoryEntriesAccumulator, bool)"/>
        public static EnumerateDirectoryResult EnumerateDirectoryEntries(
            string directoryPath,
            bool enumerateDirectory,
            string pattern,
            uint directoriesToSkipRecursively,
            bool recursive,
            IDirectoryEntriesAccumulator accumulators)
        {
            return OsFileSystem.EnumerateDirectoryEntries(directoryPath, enumerateDirectory, pattern, directoriesToSkipRecursively, recursive, accumulators);
        }

        /// <see cref="IFileUtilities.FindAllOpenHandlesInDirectory(string, HashSet{string},Func{String, bool, bool})"/>
        public static string FindAllOpenHandlesInDirectory(string directoryPath, HashSet<string> pathsPossiblyPendingDelete = null) =>
            OsFileUtilities.FindAllOpenHandlesInDirectory(directoryPath, pathsPossiblyPendingDelete);

        /// <see cref="IFileSystem.TryOpenDirectory(string, FileDesiredAccess, FileShare, FileFlagsAndAttributes, out SafeFileHandle)"/>
        public static OpenFileResult TryOpenDirectory(
            string directoryPath,
            FileDesiredAccess desiredAccess,
            FileShare shareMode,
            FileFlagsAndAttributes flagsAndAttributes,
            out SafeFileHandle handle)
        {
            return OsFileSystem.TryOpenDirectory(directoryPath, desiredAccess, shareMode, flagsAndAttributes, out handle);
        }

        /// <see cref="IFileSystem.TryOpenDirectory(string, FileShare, out SafeFileHandle)"/>
        public static OpenFileResult TryOpenDirectory(string directoryPath, FileShare shareMode, out SafeFileHandle handle)
        {
            return OsFileSystem.TryOpenDirectory(directoryPath, shareMode, out handle);
        }

        #endregion

        #region File related functions

        /// <see cref="IFileUtilities.CopyFileAsync(string, string, Func{SafeFileHandle, SafeFileHandle, bool}, Action{SafeFileHandle, SafeFileHandle})"/>
        public static Task<bool> CopyFileAsync(
            string source,
            string destination,
            Func<SafeFileHandle, SafeFileHandle, bool> predicate = null,
            Action<SafeFileHandle, SafeFileHandle> onCompletion = null) => OsFileUtilities.CopyFileAsync(source, destination, predicate, onCompletion);

        /// <see cref="IFileUtilities.MoveFileAsync(string, string, bool)"/>
        public static Task MoveFileAsync(
            string source,
            string destination,
            bool replaceExisting = false) => OsFileUtilities.MoveFileAsync(source, destination, replaceExisting);

        /// <summary>
        /// Tries to copy using <see cref="IFileUtilities.InKernelFileCopy(string, string, bool)"/>.
        /// </summary>
        public static Possible<Unit> TryInKernelFileCopy(string source, string destination, bool followSymlink)
        {
            try
            {
                using (Counters?.StartStopwatch(StorageCounters.InKernelFileCopyDuration))
                {
                    Counters?.IncrementCounter(StorageCounters.InKernelFileCopyCount);
                    Possible<Unit> result = OsFileUtilities.InKernelFileCopy(source, destination, followSymlink);
                    if (result.Succeeded)
                    {
                        Counters?.IncrementCounter(StorageCounters.SuccessfulInKernelFileCopyCount);
                    }

                    return result;
                }
            }
            catch (NativeWin32Exception ex)
            {
                return NativeFailure.CreateFromException(ex);
            }
            catch (Exception ex)
            {
                return new NativeFailure((int)BuildXL.Interop.Unix.IO.Errno.ENOSYS, ex.ToString());
            }
        }

        /// <see cref="IFileUtilities.CreateReplacementFile(string, FileShare, bool, bool)"/>
        public static FileStream CreateReplacementFile(
            string path,
            FileShare fileShare,
            bool openAsync = true,
            bool allowExcludeFileShareDelete = false) => OsFileUtilities.CreateReplacementFile(path, fileShare, openAsync, allowExcludeFileShareDelete);

        /// <see cref="IFileUtilities.DeleteFile(string, bool, ITempCleaner)"/>
        public static void DeleteFile(string path, bool retryOnFailure = true, ITempCleaner tempDirectoryCleaner = null) =>
            OsFileUtilities.DeleteFile(path, retryOnFailure, tempDirectoryCleaner);

        /// <see cref="IFileUtilities.PosixDeleteMode"/>
        public static PosixDeleteMode PosixDeleteMode
        {
            get { return OsFileUtilities.PosixDeleteMode; }
            set { OsFileUtilities.PosixDeleteMode = value; }
        }

        /// <summary>
        /// If set to true, then <see cref="PosixDeleteMode"/> the value will be <see cref="PosixDeleteMode.NoRun"/>,
        /// otherwise, for the sake of backward compatibility, the value will be <see cref="PosixDeleteMode.RunFirst"/>.
        /// </summary>
        public static bool SkipPosixDelete
        {
            get
            {
                return OsFileUtilities.PosixDeleteMode == PosixDeleteMode.NoRun;
            }

            set
            {
                if (value)
                {
                    OsFileUtilities.PosixDeleteMode = PosixDeleteMode.NoRun;
                }
                else
                {
                    OsFileUtilities.PosixDeleteMode = PosixDeleteMode.RunFirst;
                }
            }
        }

        /// <see cref="IFileUtilities.TryDeleteFile(string, bool, ITempCleaner)"/>
        public static Possible<string, DeletionFailure> TryDeleteFile(string path, bool retryOnFailure = true, ITempCleaner tempDirectoryCleaner = null) =>
            OsFileUtilities.TryDeleteFile(path, retryOnFailure, tempDirectoryCleaner);

        /// <summary>
        /// Tries to delete file or directory if exists.
        /// </summary>
        /// <param name="fileOrDirectoryPath">Path to file or directory to be deleted, if exists.</param>
        /// <param name="tempDirectoryCleaner">Temporary directory cleaner.</param>
        public static Possible<string, Failure> TryDeletePathIfExists(string fileOrDirectoryPath, ITempCleaner tempDirectoryCleaner = null)
        {
            var maybeExistence = TryProbePathExistence(fileOrDirectoryPath, followSymlink: false);
            if (!maybeExistence.Succeeded)
            {
                return maybeExistence.Failure;
            }

            var existence = maybeExistence.Result;
            if (existence == PathExistence.ExistsAsFile)
            {
                var possibleDeletion = TryDeleteFile(
                    fileOrDirectoryPath,
                    retryOnFailure: true,
                    tempDirectoryCleaner: tempDirectoryCleaner);

                if (!possibleDeletion.Succeeded)
                {
                    return possibleDeletion.WithGenericFailure();
                }
            }
            else if (existence == PathExistence.ExistsAsDirectory)
            {
                DeleteDirectoryContents(fileOrDirectoryPath, deleteRootDirectory: true, tempDirectoryCleaner: tempDirectoryCleaner);
            }

            return fileOrDirectoryPath;
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
        /// Returns true if given file attributes denote a reparse point that points to a directory.
        /// </summary>
        public static bool IsDirectorySymlinkOrJunction(FileAttributes attributes)
        {
            return
                    ((attributes & FileAttributes.Directory) == FileAttributes.Directory) &&
                    ((attributes & FileAttributes.ReparsePoint) != 0);
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

        /// <summary>
        /// Checks if an artifact (file or directory) exists.
        /// </summary>
        /// <remarks>
        /// Doesn't follow the symlink if <paramref name="path"/> is a symlink.
        /// </remarks>
        public static bool ArtifactExistsNoFollow(string path)
        {
            var maybeExistence = TryProbePathExistence(path, followSymlink: false);
            return maybeExistence.Succeeded && maybeExistence.Result == PathExistence.ExistsAsFile || maybeExistence.Result == PathExistence.ExistsAsDirectory;
        }

        /// <see cref="IFileUtilities.TryMoveDelete(string, string)"/>
        public static bool TryMoveDelete(string path, string deletionTempDirectory) => OsFileUtilities.TryMoveDelete(path, deletionTempDirectory);

        /// <see cref="IFileUtilities.GetFileName(string)"/>
        public static Possible<string> GetFileName(string path) => OsFileUtilities.GetFileName(path);

        /// <see cref="IFileUtilities.GetFileTimestamps"/>
        public static FileTimestamps GetFileTimestamps(string path, bool followSymlink = false)
            => OsFileUtilities.GetFileTimestamps(path, followSymlink);

        /// <see cref="IFileUtilities.SetFileTimestamps"/>
        public static void SetFileTimestamps(string path, FileTimestamps timestamps, bool followSymlink = false)
            => OsFileUtilities.SetFileTimestamps(path, timestamps, followSymlink);

        /// <see cref="IFileUtilities.WriteAllTextAsync(string, string, Encoding)"/>
        public static Task WriteAllTextAsync(
            string filePath,
            string text,
            Encoding encoding) => OsFileUtilities.WriteAllTextAsync(filePath, text, encoding);

        /// <see cref="IFileUtilities.WriteAllBytesAsync(string, byte[], Func{SafeFileHandle, bool}, Action{SafeFileHandle})"/>
        public static Task<bool> WriteAllBytesAsync(
            string filePath,
            byte[] bytes,
            Func<SafeFileHandle, bool> predicate = null,
            Action<SafeFileHandle> onCompletion = null) => OsFileUtilities.WriteAllBytesAsync(filePath, bytes, predicate, onCompletion);

        /// <see cref="IFileUtilities.TryFindOpenHandlesToFile"/>
        public static bool TryFindOpenHandlesToFile(string filePath, out string diagnosticInfo, bool printCurrentFilePath = true)
            => OsFileUtilities.TryFindOpenHandlesToFile(filePath, out diagnosticInfo, printCurrentFilePath);

        /// <see cref="IFileUtilities.GetHardLinkCount(string)"/>
        public static uint GetHardLinkCount(string path) => OsFileUtilities.GetHardLinkCount(path);

        /// <see cref="IFileUtilities.HasWritableAccessControl(string)"/>
        public static bool HasWritableAccessControl(string path) => OsFileUtilities.HasWritableAccessControl(path);

        /// <see cref="IFileUtilities.HasWritableAttributeAccessControl(string)"/>
        public static bool HasWritableAttributeAccessControl(string path) => OsFileUtilities.HasWritableAttributeAccessControl(path);

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
            return OsFileUtilities.CreateFileStream(path, fileMode, fileAccess, fileShare, options, force, allowExcludeFileShareDelete);
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
            return OsFileUtilities.CreateAsyncFileStream(path, fileMode, fileAccess, fileShare, options, force, allowExcludeFileShareDelete);
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
                OsFileUtilities.UsingFileHandleAndFileLength(
                    path,
                    desiredAccess,
                    shareMode,
                    creationDisposition,
                    flagsAndAttributes,
                    handleStream);

        /// <see cref="IFileSystem.TryCreateOrOpenFile(string, FileDesiredAccess, FileShare, FileMode, FileFlagsAndAttributes, out SafeFileHandle)"/>
        public static OpenFileResult TryCreateOrOpenFile(
            string path,
            FileDesiredAccess desiredAccess,
            FileShare shareMode,
            FileMode creationDisposition,
            FileFlagsAndAttributes flagsAndAttributes,
            out SafeFileHandle handle)
        {
            return OsFileSystem.TryCreateOrOpenFile(path, desiredAccess, shareMode, creationDisposition, flagsAndAttributes, out handle);
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
            return OsFileSystem.TryOpenFileById(existingHandleOnVolume, fileId, desiredAccess, shareMode, flagsAndAttributes, out handle);
        }

        /// <see cref="IFileSystem.TryReOpenFile(SafeFileHandle, FileDesiredAccess, FileShare, FileFlagsAndAttributes, out SafeFileHandle)"/>
        public static ReOpenFileStatus TryReOpenFile(
            SafeFileHandle existing,
            FileDesiredAccess desiredAccess,
            FileShare shareMode,
            FileFlagsAndAttributes flagsAndAttributes,
            out SafeFileHandle reopenedHandle)
        {
            return OsFileSystem.TryReOpenFile(existing, desiredAccess, shareMode, flagsAndAttributes, out reopenedHandle);
        }

        /// <see cref="IFileSystem.TryPosixDelete(string, out OpenFileResult)"/>
        public static unsafe bool TryPosixDelete(string pathToDelete, out OpenFileResult openFileResult)
        {
            return OsFileSystem.TryPosixDelete(pathToDelete, out openFileResult);
        }

        /// <see cref="IFileSystem.TrySetDeletionDisposition(SafeFileHandle)"/>
        public static unsafe bool TrySetDeletionDisposition(SafeFileHandle handle)
        {
            return OsFileSystem.TrySetDeletionDisposition(handle);
        }

        /// <see cref="IFileSystem.GetFileFlagsAndAttributesForPossibleReparsePoint"/>
        public static FileFlagsAndAttributes GetFileFlagsAndAttributesForPossibleReparsePoint(string expandedPath)
        {
            using (Counters?.StartStopwatch(StorageCounters.GetFileFlagsAndAttributesForPossibleReparsePointDuration))
            {
                return OsFileSystem.GetFileFlagsAndAttributesForPossibleReparsePoint(expandedPath);
            }
        }

        /// <see cref="IFileSystem.GetFileAttributesByHandle(SafeFileHandle)"/>
        public static unsafe FileAttributes GetFileAttributesByHandle(SafeFileHandle fileHandle)
        {
            using (Counters?.StartStopwatch(StorageCounters.GetFileAttributesByHandleDuration))
            {
                return OsFileSystem.GetFileAttributesByHandle(fileHandle);
            }
        }

        /// <see cref="IFileSystem.GetFileAttributes(string)"/>
        public static FileAttributes GetFileAttributes(string path) => OsFileSystem.GetFileAttributes(path);

        /// <see cref="IFileSystem.SetFileAttributes(string, FileAttributes)"/>
        public static void SetFileAttributes(string path, FileAttributes attributes)
        {
            OsFileSystem.SetFileAttributes(path, attributes);
        }

        /// <see cref="IFileUtilities.SetFileAccessControl(string, FileSystemRights, bool, bool)"/>
        public static void SetFileAccessControl(string path, FileSystemRights fileSystemRights, bool allow, bool disableInheritance = false)
        {
            OsFileUtilities.SetFileAccessControl(path, fileSystemRights, allow, disableInheritance);
        }

        /// <see cref="IFileSystem.TryWriteFileSync(SafeFileHandle, byte[], out int)"/>
        public static bool TryWriteFileSync(SafeFileHandle handle, byte[] content, out int nativeErrorCode)
        {
            return OsFileSystem.TryWriteFileSync(handle, content, out nativeErrorCode);
        }

        /// <see cref="IFileUtilities.DisableAuditRuleInheritance(string)"/>
        public static void DisableAuditRuleInheritance(string path)
        {
            OsFileUtilities.DisableAuditRuleInheritance(path);
        }

        /// <inheritdoc />
        public static bool IsFileAccessRuleInheritanceDisabled(string path)
        {
            return OsFileUtilities.IsAclInheritanceDisabled(path);
        }

        /// <summary>
        /// Query the file system and get the exact path name (with respect to casing) for a given path (with arbitrary casing).
        /// The path isn't assumed to exist, but see 'guaranteedExistence' below.
        /// This method isn't cheap. The optional parameters allow for some optimizations: 
        ///     - guaranteedExistence can be set to true when the path is known to exist, saving some existence checks.
        ///     - cacheAncestorsOnly: the given path is not cached, only its ancestors. Useful for only caching directories and not files.
        ///     - A dictionary of known resolutions can be given: This dictionary is modified by this method to include the resolutions carried out in its execution,
        ///       so using it in subsequent calls will allow for short-circuiting while traversing parent directories all the way to the root.
        ///       This is useful when invoking this method for many paths under the same directory cone.
        /// </summary>
        /// <remarks>
        /// On OSs where path comparison is case sensitive, this method is a no-op.
        /// </remarks>
        public static Possible<string> GetPathWithExactCasing(string path, bool guaranteedExistence = false, ConcurrentBigMap<string, string> knownResolutions = null)
        {
            if (OperatingSystemHelper.IsPathComparisonCaseSensitive)
            {
                return path;
            }

            if (knownResolutions?.TryGetValue(path, out var result) ?? false)
            {
                return result;
            }

            string resolved;

            try
            {
                bool pathExists = false;

                // If the path points to a file, it's still okay to construct a directory info,
                // it's just that 'di.Exists' will be false.
                var di = new DirectoryInfo(path);
                if (di.Parent != null)
                {
                    string resolvedAtom = di.Name;

                    // Check if the parent path exists (while propagating guaranteedExistence upwards so we only check once)
                    // If the parent does not exist, then this path doesn't either, so we will keep the provided name of this component
                    // (assigned in the declaration above) but we will still crawl up the ancestors to try to get the correct casing
                    // for them.
                    if (guaranteedExistence || (guaranteedExistence = di.Parent.Exists))
                    {
                        var infos = di.Parent.GetFileSystemInfos(di.Name);
                        if (infos.Length > 0)
                        {
                            resolvedAtom = infos[0].Name;
                            pathExists = true;
                        }
                    }

                    var possibleParent = GetPathWithExactCasing(di.Parent.FullName, guaranteedExistence, knownResolutions);

                    if (!possibleParent.Succeeded)
                    {
                        return possibleParent;
                    }

                    resolved = Path.Combine(
                        possibleParent.Result,
                        resolvedAtom);
                }
                else
                {
                    // di.Parent == null means we are at the root (i.e., drive letter) 
                    pathExists = true;
                    resolved = di.Name;
                }

                if (pathExists)
                {
                    knownResolutions?.TryAdd(path, resolved);
                }
            }
            catch (Exception e)
            {
                return new Failure<Exception>(e);
            }

            return resolved;
        }

        #endregion

        #region General file and directory utilities

        /// <see cref="IFileUtilities.Exists(string)"/>
        public static bool Exists(string path) => OsFileUtilities.Exists(path);

        /// <see cref="IFileUtilities.DoesLogicalDriveHaveSeekPenalty(char)"/>
        public static bool? DoesLogicalDriveHaveSeekPenalty(char driveLetter) => OsFileUtilities.DoesLogicalDriveHaveSeekPenalty(driveLetter);

        /// <see cref="IFileUtilities.GetKnownFolderPath(Guid)"/>
        public static string GetKnownFolderPath(Guid knownFolder) => OsFileUtilities.GetKnownFolderPath(knownFolder);

        /// <see cref="IFileUtilities.GetUserSettingsFolder(string)"/>
        public static string GetUserSettingsFolder(string appName) => OsFileUtilities.GetUserSettingsFolder(appName);

        /// <see cref="IFileUtilities.TryTakeOwnershipAndSetWriteable(string)"/>
        public static bool TryTakeOwnershipAndSetWriteable(string path) => OsFileUtilities.TryTakeOwnershipAndSetWriteable(path);

        /// <summary>
        /// Returns a relative path from one path to another.
        /// </summary>
        public static string GetRelativePath(string relativeTo, string path)
        {
#if NETCOREAPP
            return Path.GetRelativePath(relativeTo, path);
#else // NET 472 - get relative path is not available
            return GetRelativePathForNet472(relativeTo, path);
#endif
        }

        /// <summary>
        /// Returns a relative path from one path to another.
        /// </summary>
        /// <remarks>
        /// This functionality is available starting with .netCore2.0 as Path.GetRelativePath. This is an explicit implementation for .net472. Please use the standard libraries when
        /// possible.
        /// </remarks>
        /// <param name="relativeTo">The source path the result should be relative to. This path is always considered to be a directory.</param>
        /// <param name="path">The destination path.</param>
        /// <returns>The relative path, or path if the paths don't share the same root.</returns>
        internal static string GetRelativePathForNet472(string relativeTo, string path)
        {
            Contract.Requires(path != null);
            Contract.Requires(relativeTo != null);

            static string addSeparatorIfNeeded(string path) => !path.EndsWith(Path.DirectorySeparatorChar.ToString())
                ? path + Path.DirectorySeparatorChar
                : path;

            // Make sure both paths end with a separator so the relative computation on URIs works
            var fromUri = new Uri(addSeparatorIfNeeded(relativeTo));
            var toUri = new Uri(addSeparatorIfNeeded(path));

            if (fromUri.Scheme != toUri.Scheme)
            {
                return path;
            }

            var relativeUri = fromUri.MakeRelativeUri(toUri);
            string relativePath = Uri.UnescapeDataString(relativeUri.ToString());

            if (string.Equals(toUri.Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase))
            {
                relativePath = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            }

            // If the original path didn't end with a separator, remove it from the result as well
            if (!path.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                return relativePath.Substring(0, relativePath.Length - 1);
            }

            return relativePath;
        }

            
        #endregion

        #region Soft- (Junction) and Hardlink functions

        /// <see cref="IFileSystem.CreateJunction(string, string, bool, bool)"/>
        public static void CreateJunction(string junctionPoint, string targetDir, bool createDirectoryForJunction = true, bool allowNonExistentTarget = false)
        {
            OsFileSystem.CreateJunction(junctionPoint, targetDir, createDirectoryForJunction, allowNonExistentTarget);
        }

        /// <see cref="IFileSystem.TryCreateSymbolicLink(string, string, bool)"/>
        public static Possible<Unit> TryCreateSymbolicLink(string symLinkFileName, string targetFileName, bool isTargetFile)
        {
            return OsFileSystem.TryCreateSymbolicLink(symLinkFileName, targetFileName, isTargetFile);
        }

        /// <summary>
        /// Tries to create a reparse point if targets do not match.
        /// </summary>
        /// <remarks>
        /// The first parameter should be a path to an existing reparse point 
        /// </remarks>
        public static Possible<Unit> TryCreateReparsePointIfTargetsDoNotMatch(string reparsePoint, string reparsePointTarget, ReparsePointType type, out bool reparsePointUnchanged)
        {
            reparsePointUnchanged = false;
            bool shouldCreate = true;
            if (IsReparsePointActionable(type))
            {
                var openResult = TryCreateOrOpenFile(
                    reparsePoint,
                    FileDesiredAccess.GenericRead,
                    FileShare.Read | FileShare.Delete,
                    FileMode.Open,
                    FileFlagsAndAttributes.FileFlagOverlapped | FileFlagsAndAttributes.FileFlagOpenReparsePoint | FileFlagsAndAttributes.FileFlagBackupSemantics,
                    out SafeFileHandle handle);

                if (openResult.Succeeded)
                {
                    using (handle)
                    {
                        // Do not attempt to convert the target path to absolute path - always compare raw targets
                        var possibleExistingSymlinkTarget = TryGetReparsePointTarget(handle, reparsePoint);
                        var possibleExistingSymlinkType = TryGetReparsePointType(reparsePoint);

                        if (possibleExistingSymlinkTarget.Succeeded && possibleExistingSymlinkType.Succeeded)
                        {
                            string normalizedTarget = OperatingSystemHelper.IsWindowsOS ? possibleExistingSymlinkTarget.Result.Replace(FileSystemWin.NtPathPrefix, "") : possibleExistingSymlinkTarget.Result;
                            shouldCreate = !string.Equals(reparsePointTarget, normalizedTarget, OperatingSystemHelper.PathComparison) || type != possibleExistingSymlinkType.Result;
                        }
                    }
                }
            }

            if (shouldCreate)
            {
                OsFileUtilities.DeleteFile(reparsePoint, retryOnFailure: true);
                return TryCreateReparsePoint(reparsePoint, reparsePointTarget, type);
            }

            reparsePointUnchanged = true;
            return Unit.Void;
        }

        /// <summary>
        /// Tries to create a reparse point in the indicated path
        /// </summary>
        /// <remarks>
        /// The path should be absent before calling this method
        /// </remarks>
        public static Possible<Unit> TryCreateReparsePoint(string path, string reparsePointTarget, ReparsePointType type)
        {
            if (type == ReparsePointType.Junction)
            {
                try
                {
                    OsFileSystem.CreateJunction(path, reparsePointTarget, allowNonExistentTarget: true);
                }
                catch (Exception e)
                {
                    return new Failure<Exception>(e);
                }
            }
            else
            {
                // Adding this exception handling block to handle errors in directory creation.
                try
                {
                    CreateDirectory(Path.GetDirectoryName(path));

                    var maybeSymbolicLink = OsFileSystem.TryCreateSymbolicLink(path, reparsePointTarget, isTargetFile: type != ReparsePointType.DirectorySymlink);
                    if (!maybeSymbolicLink.Succeeded)
                    {
                        return maybeSymbolicLink.Failure;
                    }
                }
                catch (BuildXLException e)
                {
                    return new Failure<BuildXLException>(e);
                }
            }

            return Unit.Void;
        }

        /// <see cref="IFileSystem.TryCreateHardLink(string, string)"/>
        public static CreateHardLinkStatus TryCreateHardLink(string link, string linkTarget)
        {
            return OsFileSystem.TryCreateHardLink(link, linkTarget);
        }

        /// <see cref="IFileSystem.TryCreateHardLinkViaSetInformationFile(string, string, bool)"/>
        public static CreateHardLinkStatus TryCreateHardLinkViaSetInformationFile(string link, string linkTarget, bool replaceExisting = true)
        {
            return OsFileSystem.TryCreateHardLinkViaSetInformationFile(link, linkTarget, replaceExisting);
        }

        /// <see cref="IFileSystem.IsReparsePointActionable(ReparsePointType)"/>
        public static bool IsReparsePointActionable(ReparsePointType reparsePointType)
        {
            return OsFileSystem.IsReparsePointActionable(reparsePointType);
        }

        /// <see cref="IFileSystem.TryGetReparsePointType(string)"/>
        public static Possible<ReparsePointType> TryGetReparsePointType(string path)
        {
            using (Counters?.StartStopwatch(StorageCounters.GetReparsePointTypeDuration))
            {
                return OsFileSystem.TryGetReparsePointType(path);
            }
        }

        /// <see cref="IFileSystem.IsWciReparseArtifact(string)"/>
        public static bool IsWciReparseArtifact(string path)
        {
            return OsFileSystem.IsWciReparseArtifact(path);
        }

        /// <see cref="IFileSystem.IsWciReparsePoint(string)"/>
        public static bool IsWciReparsePoint(string path)
        {
            return OsFileSystem.IsWciReparsePoint(path);
        }

        /// <see cref="IFileSystem.IsWciTombstoneFile(string)"/>
        public static bool IsWciTombstoneFile(string path)
        {
            return OsFileSystem.IsWciTombstoneFile(path);
        }

        /// <see cref="IFileSystem.GetChainOfReparsePoints(SafeFileHandle, string, IList{string})"/>
        public static void GetChainOfReparsePoints(SafeFileHandle handle, string sourcePath, IList<string> chainOfReparsePoints)
        {
            OsFileSystem.GetChainOfReparsePoints(handle, sourcePath, chainOfReparsePoints);
        }

        /// <see cref="IFileSystem.TryGetReparsePointTarget(SafeFileHandle, string)"/>
        public static Possible<string> TryGetReparsePointTarget(SafeFileHandle handle, string sourcePath)
        {
            return OsFileSystem.TryGetReparsePointTarget(handle, sourcePath);
        }

        /// <summary>
        /// Returns the last element of a reparse point chain. If the source path is not a reparse point
        /// it returns the same path.
        /// </summary>
        /// <param name="handle">Handle to the source path. Can be null, in which case is not used</param>
        /// <param name="sourcePath">Path to the artifact</param>
        public static Possible<string> TryGetLastReparsePointTargetInChain([MaybeNull]SafeFileHandle handle, string sourcePath)
        {
            Contract.RequiresNotNullOrEmpty(sourcePath);

            if (handle == null)
            {
                var openResult = FileUtilities.TryOpenDirectory(
                                                sourcePath,
                                                FileDesiredAccess.GenericRead,
                                                FileShare.Read | FileShare.Delete,
                                                FileFlagsAndAttributes.FileFlagOverlapped | FileFlagsAndAttributes.FileFlagOpenReparsePoint,
                                                out handle);
                if (!openResult.Succeeded)
                {
                    return openResult.CreateFailureForError();
                }
            }

            using (handle)
            {
                var symlinkChainElements = new List<string>();
                FileUtilities.GetChainOfReparsePoints(handle, sourcePath, symlinkChainElements);
                return symlinkChainElements[symlinkChainElements.Count - 1];
            }
        }

        /// <see cref="IFileSystem.IsDirectorySymlinkOrJunction(string)"/>
        public static bool IsDirectorySymlinkOrJunction(string path) => OsFileSystem.IsDirectorySymlinkOrJunction(path);

        /// <see cref="IFileSystem.GetFullPath(string)"/>
        public static string GetFullPath(string path) => OsFileSystem.GetFullPath(path);

        /// <summary>
        /// Returns a unique temporary file name, and creates a 0-byte file by that name on disk.
        /// </summary>
        /// <remarks>
        /// This method functions like <see cref="Path.GetTempFileName()"/>, i.e., it creates a unqiue temp file and returns its name with full path.
        /// <see cref="Path.GetTempFileName()"/> uses the combination a hardcoded prefix and a 4-letter random number as the file name. 
        /// If the file already exist, it will loop to create a new random number until it finds a name of a file doesn't exist. 
        /// This API is shared. If any of the managed process doesn't clean up their temp files, it will affect our performance and we might possibly get access denial. 
        /// So we implement this API to replace <see cref="Path.GetTempFileName()"/>. 
        /// We use Guid.NewGuid().ToString() as part of the file name to make sure the uniqueness.
        /// </remarks>
        public static string GetTempFileName()
        {
            var path = GetTempPath();
            using var fileStream = File.Create(path);
            fileStream.Close();
            return path;
        }

        /// <summary>
        /// Returns a unique temporary file path without creating a file at that location.
        /// <seealso cref="GetTempFileName" />
        /// </summary>
        public static string GetTempPath()
        {
            return Path.Combine(Path.GetTempPath(), "bxl_" + Guid.NewGuid().ToString() + ".tmp");
        }

        /// <see cref="IFileSystem.SupportsCreationDate"/>
        public static bool SupportsCreationDate() => OsFileSystem.SupportsCreationDate();

        #endregion

        #region Journaling functions

        /// <see cref="IFileSystem.ReadFileUsnByHandle(SafeFileHandle, bool)"/>
        public static unsafe MiniUsnRecord? ReadFileUsnByHandle(SafeFileHandle fileHandle, bool forceJournalVersion2 = false)
        {
            using (Counters?.StartStopwatch(StorageCounters.ReadFileUsnByHandleDuration))
            {
                return OsFileSystem.ReadFileUsnByHandle(fileHandle, forceJournalVersion2);
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
                return OsFileSystem.TryReadUsnJournal(volumeHandle, buffer, journalId, startUsn, forceJournalVersion2, isJournalUnprivileged);
            }
        }

        /// <see cref="IFileSystem.TryQueryUsnJournal(SafeFileHandle)"/>
        public static QueryUsnJournalResult TryQueryUsnJournal(SafeFileHandle volumeHandle)
        {
            return OsFileSystem.TryQueryUsnJournal(volumeHandle);
        }

        /// <see cref="IFileSystem.TryWriteUsnCloseRecordByHandle(SafeFileHandle)"/>
        public static unsafe Usn? TryWriteUsnCloseRecordByHandle(SafeFileHandle fileHandle)
        {
            using (Counters?.StartStopwatch(StorageCounters.WriteUsnCloseRecordByHandleDuration))
            {
                return OsFileSystem.TryWriteUsnCloseRecordByHandle(fileHandle);
            }
        }

        #endregion

        #region Volume handling functions

        /// <see cref="IFileSystem.ListVolumeGuidPathsAndSerials"/>
        public static List<Tuple<VolumeGuidPath, ulong>> ListVolumeGuidPathsAndSerials()
        {
            return OsFileSystem.ListVolumeGuidPathsAndSerials();
        }

        /// <see cref="IFileSystem.GetVolumeFileSystemByHandle(SafeFileHandle)"/>
        public static FileSystemType GetVolumeFileSystemByHandle(SafeFileHandle fileHandle)
        {
            return OsFileSystem.GetVolumeFileSystemByHandle(fileHandle);
        }

        /// <see cref="IFileSystem.GetShortVolumeSerialNumberByHandle(SafeFileHandle)"/>
        public static unsafe uint GetShortVolumeSerialNumberByHandle(SafeFileHandle fileHandle)
        {
            return OsFileSystem.GetShortVolumeSerialNumberByHandle(fileHandle);
        }

        /// <see cref="IFileSystem.GetVolumeSerialNumberByHandle(SafeFileHandle)"/>
        public static ulong GetVolumeSerialNumberByHandle(SafeFileHandle fileHandle)
        {
            return OsFileSystem.GetVolumeSerialNumberByHandle(fileHandle);
        }

        /// <see cref="IFileSystem.TryGetFileIdAndVolumeIdByHandle(SafeFileHandle)"/>
        public static unsafe FileIdAndVolumeId? TryGetFileIdAndVolumeIdByHandle(SafeFileHandle fileHandle)
        {
            return OsFileSystem.TryGetFileIdAndVolumeIdByHandle(fileHandle);
        }

        /// <see cref="IFileSystem.IsVolumeMapped(string)"/>
        public static bool IsVolumeMapped(string volume) => OsFileSystem.IsVolumeMapped(volume);

        #endregion

        #region File identity and version

        /// <see cref="IFileSystem.TryGetFileIdentityByHandle(SafeFileHandle)"/>
        public static unsafe FileIdAndVolumeId? TryGetFileIdentityByHandle(SafeFileHandle fileHandle) => OsFileSystem.TryGetFileIdentityByHandle(fileHandle);

        /// <see cref="IFileSystem.TryGetVersionedFileIdentityByHandle(SafeFileHandle)"/>
        public static unsafe (FileIdAndVolumeId, Usn)? TryGetVersionedFileIdentityByHandle(SafeFileHandle fileHandle) => OsFileSystem.TryGetVersionedFileIdentityByHandle(fileHandle);

        /// <see cref="IFileSystem.TryEstablishVersionedFileIdentityByHandle(SafeFileHandle,bool)"/>
        public static unsafe (FileIdAndVolumeId, Usn)? TryEstablishVersionedFileIdentityByHandle(SafeFileHandle fileHandle, bool flushPageCache)
            => OsFileSystem.TryEstablishVersionedFileIdentityByHandle(fileHandle, flushPageCache);

        /// <see cref="IFileSystem.CheckIfVolumeSupportsPreciseFileVersionByHandle(SafeFileHandle)"/>
        public static bool CheckIfVolumeSupportsPreciseFileVersionByHandle(SafeFileHandle fileHandle) => OsFileSystem.CheckIfVolumeSupportsPreciseFileVersionByHandle(fileHandle);

        /// <see cref="IFileSystem.IsPreciseFileVersionSupportedByEnlistmentVolume"/>
        public static bool IsPreciseFileVersionSupportedByEnlistmentVolume
        {
            get => OsFileSystem.IsPreciseFileVersionSupportedByEnlistmentVolume;

            set
            {
                OsFileSystem.IsPreciseFileVersionSupportedByEnlistmentVolume = value;
            }
        }

        #endregion

        #region Generic file system helpers
        /// <see cref="IFileSystem.MaxDirectoryPathLength"/>
        public static int MaxDirectoryPathLength()
        {
            return OsFileSystem.MaxDirectoryPathLength();
        }

        /// <see cref="IFileSystem.TryProbePathExistence(string, bool, out bool)"/>
        public static Possible<PathExistence, NativeFailure> TryProbePathExistence(string path, bool followSymlink)
        {
            return OsFileSystem.TryProbePathExistence(path, followSymlink, out _);
        }

        /// <see cref="IFileSystem.TryProbePathExistence(string, bool, out bool)"/>
        public static Possible<PathExistence, NativeFailure> TryProbePathExistence(string path, bool followSymlink, out bool isReparsePoint)
        {
            return OsFileSystem.TryProbePathExistence(path, followSymlink, out isReparsePoint);
        }

        /// <see cref="IFileSystem.PathMatchPattern"/>
        public static bool PathMatchPattern(string path, string pattern)
        {
            return OsFileSystem.PathMatchPattern(path, pattern);
        }

        /// <see cref="IFileSystem.IsPendingDelete(SafeFileHandle)"/>
        public static unsafe bool IsPendingDelete(SafeFileHandle fileHandle)
        {
            return OsFileSystem.IsPendingDelete(fileHandle);
        }

        /// <see cref="IFileSystem.GetFinalPathNameByHandle(SafeFileHandle, bool)"/>
        public static string GetFinalPathNameByHandle(SafeFileHandle handle, bool volumeGuidPath = false)
        {
            return OsFileSystem.GetFinalPathNameByHandle(handle, volumeGuidPath);
        }

        /// <see cref="IFileSystem.TryGetFinalPathNameByPath(string, out string, out int, bool)"/>
        public static bool TryGetFinalPathNameByPath(string path, out string finalPath, out int nativeErrorCode, bool volumeGuidPath = false)
        {
            return OsFileSystem.TryGetFinalPathNameByPath(path, out finalPath, out nativeErrorCode, volumeGuidPath);
        }

        /// <see cref="IFileSystem.FlushPageCacheToFilesystem(SafeFileHandle)"/>
        public static unsafe NtStatus FlushPageCacheToFilesystem(SafeFileHandle handle)
        {
            return OsFileSystem.FlushPageCacheToFilesystem(handle);
        }

        /// <see cref="IFileSystem.IsInKernelCopyingSupportedByHostSystem"/>
        public static bool IsInKernelCopyingSupportedByHostSystem
        {
            get => OsFileSystem.IsInKernelCopyingSupportedByHostSystem;
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
                string pathWithUpperCase = Path.Combine(Path.GetTempPath(), "BUILDXL_CASESENSITIVE_TEST" + Guid.NewGuid().ToString("N"));
                using (new FileStream(pathWithUpperCase, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 0x1000, FileOptions.DeleteOnClose))
                {
                    string lowerCased = pathWithUpperCase.ToLowerInvariant();
                    return !FileExistsNoFollow(lowerCased);
                }
            }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
            catch
            {
                // In case something goes terribly wrong, we don't want to fail just because
                // of a casing test, so we assume case-insensitive.
                return false;
            }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
        }

        /// <summary>
        /// Resolves symlink target.
        /// </summary>
        /// <remarks>
        /// If <paramref name="targetPath"/> is an absolute path, then this method simply returns <paramref name="targetPath"/>.
        /// If <paramref name="targetPath"/> is a relative path, then it first resolves the prefixes of <paramref name="symlinkPath"/>
        /// using <see cref="IFileSystem.TryResolveReparsePointRelativeTarget(string, string)"/> (see its documentation for details) before combining the resolved
        /// path with <paramref name="targetPath"/>.
        /// </remarks>
        public static Possible<string> ResolveSymlinkTarget(string symlinkPath, string targetPath = null)
        {
            Contract.Requires(OsFileSystem.IsPathRooted(symlinkPath));

            if (targetPath == null)
            {
                var maybeTarget = OsFileSystem.TryGetReparsePointTarget(null, symlinkPath);
                if (!maybeTarget.Succeeded)
                {
                    return maybeTarget.Failure;
                }

                targetPath = maybeTarget.Result;
            }

            if (OsFileSystem.IsPathRooted(targetPath))
            {
                // If symlink target is an absolute path, then simply returns that path.
                return targetPath;
            }

            // Symlink target is a relative path.
            var maybeResolvedRelative = OsFileSystem.TryResolveReparsePointRelativeTarget(symlinkPath, targetPath);

            if (!maybeResolvedRelative.Succeeded)
            {
                return maybeResolvedRelative.Failure;
            }

            return maybeResolvedRelative;
        }

        /// <summary>
        /// Resolve a reparse point path with respect to its relative target.
        /// </summary>
        /// <remarks>
        /// Given a reparse point path A\B\C and its relative target D\E\F, where D and E can be '.' or '..',
        /// this method simply combines A\B with D\E\F and normalizes the result, i.e., removes '.' and '..'.
        /// </remarks>
        public static Possible<string> TryResolveRelativeTarget(string path, string relativeTarget)
        {
            string parent = Path.GetDirectoryName(path);
            if (parent == null)
            {
                return new Failure<string>($"Failed to resolve relative target for path {path} with target {relativeTarget}");
            }

            return Path.GetFullPath(Path.Combine(parent, relativeTarget));
        }

        /// <summary>
        /// Splits path into atoms and push it into the stack such that the top stack contains the last atom.
        /// </summary>
        public static void SplitPathsReverse(string path, Stack<string> atoms)
        {
            string nextPath = path;

            do
            {
                path = nextPath;
                string name = Path.GetFileName(path);
                AddAtom(name);
                nextPath = Path.GetDirectoryName(path);
            }
            while (!string.IsNullOrEmpty(nextPath));

            if (!string.IsNullOrEmpty(path))
            {
                AddAtom(path);
            }

            void AddAtom(string atom)
            {
                if (!string.IsNullOrEmpty(atom))
                {
                    atoms.Push(atom);
                }
            }
        }

        /// <summary>
        /// Makes an exclusive link for a file.
        /// </summary>
        /// <param name="originalPath">File path.</param>
        /// <param name="optionalTemporaryFileName">Temporary file name that users can supply.</param>
        /// <param name="preserveOriginalTimestamp">Whether or not the original timestamp should be preserved.</param>
        public static async Task<Possible<Unit>> TryMakeExclusiveLinkAsync(string originalPath, string optionalTemporaryFileName = null, bool preserveOriginalTimestamp = true)
        {
            if (!FileExistsNoFollow(originalPath))
            {
                return new Failure<string>(I($"Failed to make exclusive link for '{originalPath}' because the file does not exist"));
            }

            // Construct temporary path.
            string directoryName = Path.GetDirectoryName(originalPath);
            string temporaryPath = Path.Combine(directoryName, optionalTemporaryFileName ?? Guid.NewGuid().ToString());

            if (!await CopyFileAsync(originalPath, temporaryPath))
            {
                return new Failure<string>(I($"Failed to make exclusive link for '{originalPath}' because copying it to '{temporaryPath}' failed"));
            }

            if (preserveOriginalTimestamp)
            {
                // Preserve original timestamp if requested.
                var timestamps = GetFileTimestamps(originalPath);
                SetFileTimestamps(temporaryPath, timestamps);
            }

            await MoveFileAsync(temporaryPath, originalPath, replaceExisting: true);

            return Unit.Void;
        }

        /// <summary>
        /// Infers subst source and subst target from a given reference path.
        /// </summary>
        /// <param name="referenceFullPath">Rooted reference path.</param>
        /// <param name="substSource">Output subst source.</param>
        /// <param name="substTarget">Output subst target.</param>
        /// <param name="errorMessage">Error message when this method failed to get the subst source/target.</param>
        /// <returns>
        /// Returns true if the function was able to successfully determine whether subst is used on the referenced path and subst is used. If not, then this function will return false
        /// along with an error message set if an error occured.
        /// On a Unix OS, this will return false because subst is not supported, and the errorMessage will be null.
        /// </returns>
        /// <remarks>
        /// This method calls <code>GetFinalPathByHandle</code> which is only applicable on Windows.
        /// </remarks>
        public static bool TryGetSubstSourceAndTarget(string referenceFullPath, out string substSource, out string substTarget, out string errorMessage)
        {
            Contract.Requires(Path.IsPathRooted(referenceFullPath));

            substSource = null;
            substTarget = null;
            errorMessage = null;

            if (OperatingSystemHelper.IsUnixOS)
            {
                // There is currently no subst in non-Windows OS.
                return false;
            }

            OpenFileResult directoryOpenResult = TryOpenDirectory(
                referenceFullPath,
                FileShare.Read | FileShare.Write | FileShare.Delete,
                out SafeFileHandle directoryHandle);

            if (!directoryOpenResult.Succeeded)
            {
                errorMessage = directoryOpenResult.CreateExceptionForError().Message;
                return false;
            }

            string directoryHandlePath = GetFinalPathNameByHandle(directoryHandle, volumeGuidPath: false);

            if (!string.Equals(referenceFullPath, directoryHandlePath, OperatingSystemHelper.PathComparison))
            {
                string commonPath = referenceFullPath.Substring(2); // Include '\' of '<Drive>:\'  for searching.
                substTarget = referenceFullPath.Substring(0, 3);    // Include '\' of '<Drive>:\' in the substTarget.
                int commonIndex = directoryHandlePath.IndexOf(commonPath, 0, OperatingSystemHelper.PathComparison);

                if (commonIndex == -1)
                {
                    substTarget = null;
                }
                else
                {
                    substSource = directoryHandlePath.Substring(0, commonIndex + 1);
                }
            }

            return !string.IsNullOrWhiteSpace(substSource) && !string.IsNullOrWhiteSpace(substTarget);
        }

        /// <summary>
        /// Unix only (no-op on windows): sets u+x on <paramref name="fileName"/>.
        /// </summary>
        /// <returns>
        /// success with true value iff the file execute permission is set successfully,
        /// or with false value if the file execute permission has already been set; otherwise failure.
        /// </returns>
        public static Possible<bool> SetExecutePermissionIfNeeded(string fileName)
        {
            if (!OperatingSystemHelper.IsUnixOS)
            {
                return false;
            }

            var maybePermissions = GetFilePermissionsForFile(fileName);

            if (!maybePermissions.Succeeded)
            {
                return maybePermissions.Failure;
            }

            var filePermissions = maybePermissions.Result;

            if (!filePermissions.HasFlag(UnixIO.FilePermissions.S_IXUSR))
            {
                var result = UnixIO.SetFilePermissionsForFilePath(fileName, (filePermissions | UnixIO.FilePermissions.S_IXUSR));

                if (result < 0)
                {
                    return new NativeFailure(result, $"Could not set file permissions: File '{fileName}'.");
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// Unix only (always false on Windows): gets S_IXUSR on <paramref name="filename"/>.  Returns a failure on error.
        /// </summary>
        public static Possible<bool> CheckForExecutePermission(string filename) => 
            GetFilePermissionsForFile(filename).Then(permissions => permissions.HasFlag(UnixIO.FilePermissions.S_IXUSR));

        /// <summary>
        /// Unix only (always 0x0 on Windows): gets file permissions on <paramref name="filename"/>.  Returns a failure on error.
        /// </summary>
        public static Possible<UnixIO.FilePermissions> GetFilePermissionsForFile(string filename)
        {
            if (OperatingSystemHelper.IsUnixOS)
            {
                var mode = UnixIO.GetFilePermissionsForFilePath(filename, followSymlink: false);
                if (mode < 0)
                {
                    return new NativeFailure(Marshal.GetLastWin32Error(), $"Could not retrieve file permissions: File '{filename}' returned mode '{mode}'");
                }

                return checked((UnixIO.FilePermissions)mode);
            }

            return default(UnixIO.FilePermissions);
        }

        /// <summary>
        /// Gets subst drive and path from subst source and target.
        /// </summary>
        public static (string drive, string path) GetSubstDriveAndPath(string substSource, string substTarget)
        {
            Contract.Requires(Path.IsPathRooted(substSource));
            Contract.Requires(Path.IsPathRooted(substTarget));

            string substDrive = Path.GetPathRoot(substTarget).TrimEnd(Path.DirectorySeparatorChar);
            string substPath = substSource.TrimEnd(Path.DirectorySeparatorChar);

            return (substDrive, substPath);
        }

        #endregion
    }
}
