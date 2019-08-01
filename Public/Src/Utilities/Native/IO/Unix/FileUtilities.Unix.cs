// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities;
using BuildXL.Utilities.Tasks;
using Microsoft.Win32.SafeHandles;
using static BuildXL.Interop.MacOS.IO;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Native.IO.Unix
{
    /// <inheritdoc />
    public sealed class FileUtilitiesUnix : IFileUtilities
    {
        /// <summary>
        /// A concrete native FileSystem implementation based on Unix APIs
        /// </summary>
        private static readonly FileSystemUnix s_fileSystem = new FileSystemUnix();

        /// <inheritdoc />
        public PosixDeleteMode PosixDeleteMode { get; set; }

        /// <summary>
        /// Creates a concrete FileUtilities instance
        /// </summary>
        public FileUtilitiesUnix()
        {
            PosixDeleteMode = PosixDeleteMode.RunFirst;
        }

        /// <inheritdoc />
        public bool? DoesLogicalDriveHaveSeekPenalty(char driveLetter) => false;

        /// <inheritdoc />
        public void DeleteDirectoryContents(
            string path,
            bool deleteRootDirectory = false,
            Func<string, bool> shouldDelete = null,
            ITempCleaner tempDirectoryCleaner = null,
            CancellationToken? cancellationToken = default)
        {
            DeleteDirectoryContentsInternal(path, deleteRootDirectory, shouldDelete, tempDirectoryCleaner, cancellationToken);
        }

        private int DeleteDirectoryContentsInternal(
            string path,
            bool deleteRootDirectory,
            Func<string, bool> shouldDelete,
            ITempCleaner tempDirectoryCleaner,
            CancellationToken? cancellationToken)
        {
            int remainingChildCount = 0;

            if (!Directory.Exists(path))
            {
                return remainingChildCount;
            }

            shouldDelete = shouldDelete ?? (p => true);

            EnumerateDirectoryResult result = s_fileSystem.EnumerateDirectoryEntries(
                path,
                (name, attributes) =>
                {
                    cancellationToken?.ThrowIfCancellationRequested();

                    var isDirectory = FileUtilities.IsDirectoryNoFollow(attributes);
                    string childPath = Path.Combine(path, name);

                    if (isDirectory)
                    {
                        int subDirectoryCount = DeleteDirectoryContentsInternal(
                            childPath,
                            deleteRootDirectory: true,
                            shouldDelete: shouldDelete,
                            tempDirectoryCleaner: tempDirectoryCleaner,
                            cancellationToken: cancellationToken);

                        if (subDirectoryCount > 0)
                        {
                            ++remainingChildCount;
                        }
                    }
                    else
                    {
                        if (shouldDelete(childPath))
                        {
                            // This method already has retry logic, so no need to do retry in DeleteFile
                            DeleteFile(childPath, waitUntilDeletionFinished: true, tempDirectoryCleaner: tempDirectoryCleaner);
                        }
                        else
                        {
                            ++remainingChildCount;
                        }
                    }
                }, isEnumerationForDirectoryDeletion: true);

            if (deleteRootDirectory && remainingChildCount == 0)
            {
                bool success = false;

                success = Helpers.RetryOnFailure(
                    finalRound =>
                    {
                        // An exception will be thrown on failure, which will trigger a retry, this deletes the path itself
                        // and any file or dir still in recursively through the 'true' flag
                        Directory.Delete(path, true);

                        // Only reached if there are no exceptions
                        return true;
                    });

                if (!success)
                {
                    throw new BuildXLException(path);
                }
            }

            return remainingChildCount;
        }

        /// <inheritdoc />
        public string FindAllOpenHandlesInDirectory(string directoryPath, HashSet<string> pathsPossiblyPendingDelete = null) => throw new NotImplementedException();

        /// <inheritdoc />
        public Possible<Unit, RecoverableExceptionFailure> TryDeleteFile(
            string path,
            bool waitUntilDeletionFinished = true,
            ITempCleaner tempDirectoryCleaner = null)
        {
            try
            {
                DeleteFile(path, waitUntilDeletionFinished, tempDirectoryCleaner);
                return Unit.Void;
            }
            catch (BuildXLException ex)
            {
                return new RecoverableExceptionFailure(ex);
            }
        }

        /// <inheritdoc />
        public bool Exists(string path)
        {
            var maybeExistence = s_fileSystem.TryProbePathExistence(path, followSymlink: false);
            return maybeExistence.Succeeded && maybeExistence.Result != PathExistence.Nonexistent;
        }

        /// <inheritdoc />
        public void DeleteFile(
            string path,
            bool waitUntilDeletionFinished = true,
            ITempCleaner tempDirectoryCleaner = null)
        {
            Contract.Requires(!string.IsNullOrEmpty(path));
            bool successfullyDeletedFile = false;

            if (!Exists(path))
            {
                // Skip deletion all together if nothing exists at the specified path
                return;
            }

            Action<string> delete =
                (string pathToDelete) =>
                {
                    var isDirectory = FileUtilities.DirectoryExistsNoFollow(pathToDelete);
                    if (isDirectory)
                    {
                        DeleteDirectoryContents(pathToDelete, deleteRootDirectory: true);
                    }
                    else
                    {
                        File.Delete(pathToDelete);
                    }
                };

            if (waitUntilDeletionFinished)
            {
                successfullyDeletedFile = Helpers.RetryOnFailure(
                    attempt =>
                    {
                        delete(path);
                        return true;
                    });
            }
            else
            {
                try
                {
                    delete(path);
                    successfullyDeletedFile = true;
                }
#pragma warning disable ERP022 // Unobserved exception in generic exception handler
                catch
                {
                }
#pragma warning restore ERP022 // Unobserved exception in generic exception handler
            }

            if (!successfullyDeletedFile)
            {
                throw new BuildXLException("Deleting file '" + path + "' failed!");
            }
        }

        /// <inheritdoc />
        public bool TryMoveDelete(string path, string deletionTempDirectory)
        {
            try
            {
                DeleteFile(path);
                return true;
            }
            catch (Exception ex)
            {
                throw new BuildXLException("Deleting file '" + path + "' failed, reason: " + ex.Message ?? ex.InnerException.Message);
            }
        }

        /// <inheritdoc />
        public void SetFileTimestamps(string path, FileTimestamps timestamps, bool followSymlink)
        {
            Contract.Requires(timestamps.CreationTime >= UnixEpoch);
            Contract.Requires(timestamps.AccessTime >= UnixEpoch);
            Contract.Requires(timestamps.LastWriteTime >= UnixEpoch);
            Contract.Requires(timestamps.LastChangeTime >= UnixEpoch);

            var statBuffer = new StatBuffer();

            unsafe
            {
                Timespec creationTime = Timespec.CreateFromUtcDateTime(timestamps.CreationTime);
                Timespec lastAccessTime = Timespec.CreateFromUtcDateTime(timestamps.AccessTime);
                Timespec lastModificationTime = Timespec.CreateFromUtcDateTime(timestamps.LastWriteTime);
                Timespec lastStatusChangeTime = Timespec.CreateFromUtcDateTime(timestamps.LastChangeTime);

                statBuffer.TimeCreation = creationTime.Tv_sec;
                statBuffer.TimeNSecCreation = creationTime.Tv_nsec;

                statBuffer.TimeLastAccess = lastAccessTime.Tv_sec;
                statBuffer.TimeNSecLastAccess = lastAccessTime.Tv_nsec;

                statBuffer.TimeLastModification = lastModificationTime.Tv_sec;
                statBuffer.TimeNSecLastModification = lastModificationTime.Tv_nsec;

                statBuffer.TimeLastStatusChange = lastStatusChangeTime.Tv_sec;
                statBuffer.TimeNSecLastStatusChange = lastStatusChangeTime.Tv_nsec;

                int result = SetTimeStampsForFilePath(path, followSymlink, statBuffer);

                if (result != 0)
                {
                    throw new BuildXLException("Failed to open a file to set its timestamps - error: " + Marshal.GetLastWin32Error());
                }
            }
        }

        /// <inheritdoc />
        public FileTimestamps GetFileTimestamps(string path, bool followSymlink)
        {
            var statBuffer = new StatBuffer();

            unsafe
            {
                if (StatFile(path, followSymlink, ref statBuffer) != 0)
                {
                    throw new BuildXLException(I($"Failed to stat file '{path}' to get its timestamps - error: {Marshal.GetLastWin32Error()}"));
                }
            }

            var creationTime = new Timespec() { Tv_sec = statBuffer.TimeCreation, Tv_nsec = statBuffer.TimeNSecCreation };
            var lastChangeTimeTime = new Timespec() { Tv_sec = statBuffer.TimeLastStatusChange, Tv_nsec = statBuffer.TimeNSecLastStatusChange };
            var lastWriteTime = new Timespec() { Tv_sec = statBuffer.TimeLastModification, Tv_nsec = statBuffer.TimeNSecLastModification };
            var accessTime = new Timespec() { Tv_sec = statBuffer.TimeLastAccess, Tv_nsec = statBuffer.TimeNSecLastAccess };

            return new FileTimestamps(
                creationTime: creationTime.ToUtcTime(),
                lastChangeTime: lastChangeTimeTime.ToUtcTime(),
                lastWriteTime: lastWriteTime.ToUtcTime(),
                accessTime: accessTime.ToUtcTime());
        }

        /// <summary>
        /// Gets device and inode number.
        /// </summary>
        public int GetDeviceAndInodeNumbers(string path, bool followSymlink, out ulong device, out ulong inode)
        {
            var statBuffer = new StatBuffer();

            unsafe
            {
                int result = StatFile(path, followSymlink, ref statBuffer);

                device = unchecked((ulong) statBuffer.DeviceID);
                inode = unchecked((ulong) statBuffer.InodeNumber);

                return result;
            }
        }

        /// <inheritdoc />
        public Task WriteAllTextAsync(
            string filePath,
            string text,
            Encoding encoding)
        {
            Contract.Requires(!string.IsNullOrEmpty(filePath));
            Contract.Requires(text != null);
            Contract.Requires(encoding != null);

            byte[] bytes = encoding.GetBytes(text);
            return WriteAllBytesAsync(filePath, bytes);
        }

        /// <inheritdoc />
        public Task<bool> WriteAllBytesAsync(
            string filePath,
            byte[] bytes,
            Func<SafeFileHandle, bool> predicate = null,
            Action<SafeFileHandle> onCompletion = null)
        {
            Contract.Requires(!string.IsNullOrEmpty(filePath));
            Contract.Requires(bytes != null);

            return ExceptionUtilities.HandleRecoverableIOExceptionAsync(
                async () =>
                {
                    if (predicate != null)
                    {
                        SafeFileHandle destinationHandle;
                        OpenFileResult predicateQueryOpenResult = s_fileSystem.TryCreateOrOpenFile(
                            filePath,
                            FileDesiredAccess.GenericRead,
                            FileShare.Read | FileShare.Delete,
                            FileMode.OpenOrCreate,
                            FileFlagsAndAttributes.None,
                            out destinationHandle);
                        using (destinationHandle)
                        {
                            if (!predicateQueryOpenResult.Succeeded)
                            {
                                throw new BuildXLException(
                                    I($"Failed to open file '{filePath}' to check its version"),
                                    predicateQueryOpenResult.CreateExceptionForError());
                            }

                            if (!predicate(predicateQueryOpenResult.OpenedOrTruncatedExistingFile ? destinationHandle : null))
                            {
                                return false;
                            }
                        }
                    }

                    using (FileStream stream = CreateReplacementFile(filePath, FileShare.Delete, openAsync: true))
                    {
                        await stream.WriteAsync(bytes, 0, bytes.Length);

                        onCompletion?.Invoke(stream.SafeFileHandle);
                    }

                    return true;
                },
                ex => { throw new BuildXLException("File write failed", ex); });
        }

        /// <inheritdoc />
        public Task<bool> CopyFileAsync(
            string source,
            string destination,
            Func<SafeFileHandle, SafeFileHandle, bool> predicate = null,
            Action<SafeFileHandle, SafeFileHandle> onCompletion = null)
        {
            Contract.Requires(!string.IsNullOrEmpty(source));
            Contract.Requires(!string.IsNullOrEmpty(destination));

            return ExceptionUtilities.HandleRecoverableIOExceptionAsync(
                async () =>
                {
                    using (FileStream sourceStream = CreateAsyncFileStream(
                        source,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read | FileShare.Delete))
                    {
                        if (predicate != null)
                        {
                            SafeFileHandle destinationHandle;
                            OpenFileResult predicateQueryOpenResult = s_fileSystem.TryCreateOrOpenFile(
                                destination,
                                FileDesiredAccess.GenericRead,
                                FileShare.Read | FileShare.Delete,
                                FileMode.OpenOrCreate,
                                FileFlagsAndAttributes.None,
                                out destinationHandle);
                            using (destinationHandle)
                            {
                                if (!predicateQueryOpenResult.Succeeded)
                                {
                                    throw new BuildXLException(
                                        I($"Failed to open a copy destination '{destination}' to check its version"),
                                        predicateQueryOpenResult.CreateExceptionForError());
                                }

                                if (!predicate(sourceStream.SafeFileHandle, predicateQueryOpenResult.OpenedOrTruncatedExistingFile ? destinationHandle : null))
                                {
                                    return false;
                                }
                            }
                        }

                        using (FileStream destinationStream = CreateReplacementFile(destination, FileShare.Delete, openAsync: true))
                        {
                            await sourceStream.CopyToAsync(destinationStream);
                            onCompletion?.Invoke(sourceStream.SafeFileHandle, destinationStream.SafeFileHandle);
                        }
                    }

                    return true;
                },
                ex => { throw new BuildXLException("File copy failed", ex); });
        }

        /// <inheritdoc />
        public Task<bool> MoveFileAsync(
            string source,
            string destination,
            bool replaceExisting = false)
        {
            Contract.Requires(!string.IsNullOrEmpty(source));
            Contract.Requires(!string.IsNullOrEmpty(destination));

            return Task.Run<bool>(
                () => ExceptionUtilities.HandleRecoverableIOException(
                    () =>
                    {
                        if (replaceExisting)
                        {
                            DeleteFile(destination);
                        }

                        File.Move(source, destination);
                        return true;
                    },
                    ex => { throw new BuildXLException("File move failed", ex); }));
        }

        /// <inheritdoc />
        public void CloneFile(string source, string destination, bool followSymlink)
        {
            var flags = followSymlink ? CloneFileFlags.CLONE_NONE : CloneFileFlags.CLONE_NOFOLLOW;

            unsafe
            {
                int result = Interop.MacOS.IO.CloneFile(source, destination, flags);

                if (result != 0)
                {
                    throw new NativeWin32Exception(Marshal.GetLastWin32Error(), I($"Failed to clone '{source}' to '{destination}'"));
                }
            }
        }

        /// <inheritdoc />
        public TResult UsingFileHandleAndFileLength<TResult>(
            string path,
            FileDesiredAccess desiredAccess,
            FileShare shareMode,
            FileMode creationDisposition,
            FileFlagsAndAttributes flagsAndAttributes,
            Func<SafeFileHandle, long, TResult> handleStream)
        {
            SafeFileHandle handle;
            var openResult = s_fileSystem.TryCreateOrOpenFile(
                       path,
                       desiredAccess,
                       shareMode,
                       creationDisposition,
                       flagsAndAttributes,
                       out handle);

            if (!openResult.Succeeded)
            {
                openResult
                    .CreateFailureForError()
                    .Annotate($"{nameof(s_fileSystem.TryCreateOrOpenFile)} failed in {nameof(UsingFileHandleAndFileLength)}")
                    .Throw();
            }

            using (handle)
            {
                Contract.Assert(handle != null && !handle.IsInvalid);
                var maybeTarget = s_fileSystem.TryGetReparsePointTarget(path);
                var length = maybeTarget.Succeeded ? maybeTarget.Result.Length : new FileInfo(path).Length;
                return handleStream(handle, length);
            }
        }

        /// <inheritdoc />
        public FileStream CreateReplacementFile(
            string path,
            FileShare fileShare,
            bool openAsync = true,
            bool allowExcludeFileShareDelete = false)
        {
            Contract.Requires(allowExcludeFileShareDelete || ((fileShare & FileShare.Delete) != 0));
            Contract.Requires(path != null);

            // We are immediately re-creating this path, therefore it is vital to ensure the delete is totally done
            // (otherwise, we may transiently fail to recreate the path with ERROR_ACCESS_DENIED; see DeleteFile remarks)
            DeleteFile(path, waitUntilDeletionFinished: true);
            return openAsync
                ? CreateAsyncFileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, fileShare, allowExcludeFileShareDelete: allowExcludeFileShareDelete)
                : CreateFileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, fileShare, allowExcludeFileShareDelete: allowExcludeFileShareDelete);
        }

        /// <inheritdoc />
        public Possible<string> GetFileName(string path) => Path.GetFileName(path);

        /// <inheritdoc />
        public string GetKnownFolderPath(Guid knownFolder) => throw new NotImplementedException();

        /// <inheritdoc />
        public string GetUserSettingsFolder(string appName)
        {
            Contract.Requires(!string.IsNullOrEmpty(appName));

            var homeFolder = Environment.GetEnvironmentVariable("HOME");
            if (string.IsNullOrEmpty(homeFolder))
            {
                throw new BuildXLException("Missing environment variable 'HOME'.");
            }

            var settingsFolder = Path.Combine(homeFolder, "." + ToCamelCase(appName));

            s_fileSystem.CreateDirectory(settingsFolder);
            return settingsFolder;
        }

        private static string ToCamelCase(string name)
        {
            return char.IsLower(name[0])
                ? name
                : char.ToLowerInvariant(name[0]) + name.Substring(1);
        }

        /// <inheritdoc />
        public bool TryFindOpenHandlesToFile(string filePath, out string diagnosticInfo, bool printCurrentFilePath) => throw new NotImplementedException();

        /// <inheritdoc />
        public uint GetHardLinkCount(string path)
        {
            var statBuffer = new StatBuffer();

            unsafe
            {
                if (StatFile(path, true, ref statBuffer) != 0)
                {
                    throw new BuildXLException(I($"Failed to stat file '{path}' to get its hardlink count - error: {Marshal.GetLastWin32Error()}"));
                }

                // TODO: Change hardlink count return type to ulong.
                return unchecked((uint) statBuffer.HardLinks);
            }
        }

        /// <inheritdoc />
        public FileStream CreateAsyncFileStream(
            string path,
            FileMode fileMode,
            FileAccess fileAccess,
            FileShare fileShare,
            FileOptions options = FileOptions.None,
            bool force = false,
            bool allowExcludeFileShareDelete = false)
        {
            Contract.Requires(!string.IsNullOrEmpty(path));
            Contract.Requires(allowExcludeFileShareDelete || ((fileShare & FileShare.Delete) != 0));
            Contract.EnsuresOnThrow<BuildXLException>(true);

            return CreateFileStream(
                path,
                fileMode,
                fileAccess,
                fileShare,
                options | FileOptions.Asynchronous,
                force: force,
                allowExcludeFileShareDelete: allowExcludeFileShareDelete);
        }

        /// <inheritdoc />
        public FileStream CreateFileStream(
            string path,
            FileMode fileMode,
            FileAccess fileAccess,
            FileShare fileShare,
            FileOptions options = FileOptions.None,
            bool force = false,
            bool allowExcludeFileShareDelete = false)
        {
            Contract.Requires(allowExcludeFileShareDelete || ((fileShare & FileShare.Delete) != 0));
            Contract.EnsuresOnThrow<BuildXLException>(true);

            return s_fileSystem.CreateFileStream(path, fileMode, fileAccess, fileShare, options, force);
        }

        /// <inheritdoc />
        public bool HasWritableAccessControl(string path)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(path));

            int result = s_fileSystem.GetFilePermission(path, false, true);

            FilePermissions permissions = checked((FilePermissions) result);
            return permissions.HasFlag(FilePermissions.S_IWUSR)
                || permissions.HasFlag(FilePermissions.S_IWGRP)
                || permissions.HasFlag(FilePermissions.S_IWOTH);
        }

        /// <inheritdoc />
        public void SetFileAccessControl(string path, FileSystemRights fileSystemRights, bool allow)
        {
            FilePermissions permissions = 0;

            if (fileSystemRights.HasFlag(FileSystemRights.AppendData) ||
                fileSystemRights.HasFlag(FileSystemRights.WriteData))
            {
                permissions |= FilePermissions.S_IWGRP | FilePermissions.S_IWOTH;
            }

            if (fileSystemRights.HasFlag(FileSystemRights.Read) ||
                fileSystemRights.HasFlag(FileSystemRights.ReadData))
            {
                permissions |= FilePermissions.S_IRGRP | FilePermissions.S_IROTH;
            }

            if (fileSystemRights.HasFlag(FileSystemRights.ExecuteFile))
            {
                permissions |= FilePermissions.S_IXGRP | FilePermissions.S_IXOTH;
            }

            int result = s_fileSystem.GetFilePermission(path, false, true);

            FilePermissions currentPermissions = checked((FilePermissions) result);

            if (allow)
            {
                currentPermissions |= permissions;
            }
            else
            {
                currentPermissions &= ~permissions;
            }

            result = SetFilePermissionsForFilePath(path, currentPermissions);

            if (result < 0)
            {
                throw new BuildXLException(I($"Failed to set permissions for file '{path}' - error: {Marshal.GetLastWin32Error()}"));
            }
        }
    }
}
