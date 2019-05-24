// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Synchronization.Internal;
using BuildXL.Native.IO;
using BuildXL.Utilities;
using Microsoft.Win32.SafeHandles;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;
using FileInfo = BuildXL.Cache.ContentStore.Interfaces.FileSystem.FileInfo;
using static BuildXL.Cache.ContentStore.FileSystem.NativeMethods;

namespace BuildXL.Cache.ContentStore.FileSystem
{
    /// <summary>
    ///     IFileSystem implementation that uses the System.IO API for accessing
    ///     local and UNC directories and files.
    /// </summary>
    public sealed class PassThroughFileSystem : IAbsFileSystem
    {
        private const string SequentialScanOnOpenStreamThreshold = "SequentialScanOnOpenStreamThresholdBytes";
        private const string SequentialScanOnOpenStreamThresholdEnvVariableName = "CloudStore" + SequentialScanOnOpenStreamThreshold;
        private const long DefaultSequentialScanOnOpenThreshold = 100 * 1024 * 1024;

        /// <summary>
        ///     Semaphore to throttle concurrent disk accesses.
        /// </summary>
        private static readonly SemaphoreSlim ConcurrentAccess = new SemaphoreSlim(Environment.ProcessorCount, Environment.ProcessorCount);

        /// <summary>
        ///     File size, over which FileOptions.SequentialScan is used to open files.
        /// </summary>
        private readonly long _sequentialScanOnOpenThreshold = DefaultSequentialScanOnOpenThreshold;

        /// <summary>
        ///     Initializes a new instance of the <see cref="PassThroughFileSystem"/> class.
        /// </summary>
        /// <remarks>
        /// The <paramref name="logger"/> is optional.
        /// </remarks>
        public PassThroughFileSystem(ILogger logger)
        {
            if (GetSequentialScanOnOpenStreamThresholdEnvVariable(out _sequentialScanOnOpenThreshold))
            {
                logger?.Debug($"{nameof(PassThroughFileSystem)}.{SequentialScanOnOpenStreamThreshold}={_sequentialScanOnOpenThreshold}");
            }
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="PassThroughFileSystem"/> class.
        /// </summary>
        public PassThroughFileSystem()
            : this(null)
        {
        }

        /// <inheritdoc />
        public void Dispose()
        {
            // Nothing to dispose
        }

        /// <inheritdoc />
        public bool DirectoryExists(AbsolutePath path)
        {
            path.ThrowIfPathTooLong();
            return Directory.Exists(path.Path);
        }

        /// <inheritdoc />
        public bool FileExists(AbsolutePath path)
        {
            path.ThrowIfPathTooLong();
            return File.Exists(path.Path);
        }

        /// <inheritdoc />
        public byte[] ReadAllBytes(AbsolutePath path)
        {
            path.ThrowIfPathTooLong();
            return File.ReadAllBytes(path.Path);
        }

        /// <inheritdoc />
        public void CreateDirectory(AbsolutePath path)
        {
            path.ThrowIfPathTooLong();
            Directory.CreateDirectory(path.Path);
        }

        /// <summary>
        ///     Copy a file from one path to another synchronously.
        /// </summary>
        public void CopyFile(AbsolutePath sourcePath, AbsolutePath destinationPath, bool replaceExisting)
        {
            sourcePath.ThrowIfPathTooLong();
            destinationPath.ThrowIfPathTooLong();

            if (!FileUtilities.FileExistsNoFollow(sourcePath.Path))
            {
                var message = string.Format(CultureInfo.InvariantCulture, "missing source file=[{0}]", sourcePath);
                throw new FileNotFoundException(message, sourcePath.Path);
            }

            if (FileUtilities.IsCopyOnWriteSupportedByEnlistmentVolume)
            {
                var possiblyDeleteExistingDestination = FileUtilities.TryDeletePathIfExists(destinationPath.Path);
                if (!possiblyDeleteExistingDestination.Succeeded)
                {
                    throw possiblyDeleteExistingDestination.Failure.CreateException();
                }

                CreateDirectory(destinationPath.Parent);

                var possiblyCreateCopyOnWrite = FileUtilities.TryCreateCopyOnWrite(sourcePath.Path, destinationPath.Path, followSymlink: false);

                if (possiblyCreateCopyOnWrite.Succeeded)
                {
                    return;
                }
            }

            File.Copy(sourcePath.Path, destinationPath.Path, replaceExisting);
        }

        /// <inheritdoc />
        public ulong GetFileId(AbsolutePath path)
        {
            path.ThrowIfPathTooLong();

            if (BuildXL.Utilities.OperatingSystemHelper.IsUnixOS)
            {
                var createOrOpenResult = FileUtilities.TryCreateOrOpenFile(path.ToString(), FileDesiredAccess.FileReadAttributes,
                                FileShare.ReadWrite | FileShare.Delete, FileMode.Open, FileFlagsAndAttributes.FileFlagOverlapped, out SafeFileHandle handle);

                if (!createOrOpenResult.Succeeded)
                {
                    throw ThrowLastWin32Error(path.ToString(), $"Failed to create or open file {path} to get its ID. Status: {createOrOpenResult.Status}");
                }

                return (ulong)handle.DangerousGetHandle().ToInt64();
            }
            else
            {
                using (SafeFileHandle sourceFileHandle = NativeMethods.CreateFile(
                    path.Path,
                    NativeMethods.FILE_READ_ATTRIBUTES,
                    FileShare.ReadWrite | FileShare.Delete,
                    IntPtr.Zero,
                    FileMode.Open,
                    0, /* Allow symbolic links to redirect us */
                    IntPtr.Zero))
                {
                    string errorMessage = string.Format(CultureInfo.InvariantCulture, "Could not get file id of {0}.", path.Path);

                    if (sourceFileHandle.IsInvalid)
                    {
                        throw new FileNotFoundException(errorMessage, path.FileName);
                    }

                    if (!NativeMethods.GetFileInformationByHandle(sourceFileHandle, out var handleInfo))
                    {
                        throw ThrowLastWin32Error(path.Path, errorMessage);
                    }

                    return (((ulong)handleInfo.FileIndexHigh) << 32) | handleInfo.FileIndexLow;
                }
            }
        }

        /// <inheritdoc />
        public int GetHardLinkCount(AbsolutePath path)
        {
            path.ThrowIfPathTooLong();

            if (BuildXL.Utilities.OperatingSystemHelper.IsUnixOS)
            {
                return (int)FileUtilities.GetHardLinkCount(path.ToString());
            }
            else
            {
                using (SafeFileHandle sourceFileHandle = NativeMethods.CreateFile(
                    path.Path,
                    NativeMethods.FILE_READ_ATTRIBUTES,
                    FileShare.ReadWrite | FileShare.Delete,
                    IntPtr.Zero,
                    FileMode.Open,
                    0,
                    IntPtr.Zero))
                {
                    string errorMessage = string.Format(CultureInfo.InvariantCulture, "Could not get link count of {0}.", path.Path);

                    if (sourceFileHandle.IsInvalid)
                    {
                        throw new FileNotFoundException(errorMessage, path.FileName);
                    }

                    if (!NativeMethods.GetFileInformationByHandle(sourceFileHandle, out var handleInfo))
                    {
                        throw ThrowLastWin32Error(path.Path, errorMessage);
                    }

                    return checked((int)handleInfo.NumberOfLinks);
                }
            }
        }

        /// <inheritdoc />
        public void DeleteDirectory(AbsolutePath path, DeleteOptions deleteOptions)
        {
            path.ThrowIfPathTooLong();

            try
            {
                Directory.Delete(path.Path, (deleteOptions & DeleteOptions.Recurse) != 0);
            }
            catch (UnauthorizedAccessException accessException)
            {
                if ((deleteOptions & DeleteOptions.ReadOnly) != 0 &&
                    (uint)accessException.HResult == Hresult.AccessDenied)
                {
                    bool foundReadonly = false;

                    foreach (FileInfo fileInfo in EnumerateFiles(path, EnumerateOptions.Recurse))
                    {
                        if ((GetFileAttributes(fileInfo.FullPath) & FileAttributes.ReadOnly) != 0)
                        {
                            SetFileAttributes(fileInfo.FullPath, FileAttributes.Normal);
                            foundReadonly = true;
                        }
                    }

                    foreach (AbsolutePath directoryPath in EnumerateDirectories(path, EnumerateOptions.Recurse))
                    {
                        if ((GetFileAttributes(directoryPath) & FileAttributes.ReadOnly) != 0)
                        {
                            SetFileAttributes(directoryPath, FileAttributes.Normal);
                            foundReadonly = true;
                        }
                    }

                    if (!foundReadonly)
                    {
                        throw;
                    }

                    Directory.Delete(path.Path, (deleteOptions & DeleteOptions.Recurse) != 0);
                }
                else
                {
                    throw;
                }
            }
        }

        /// <inheritdoc />
        public void DeleteFile(AbsolutePath path)
        {
            path.ThrowIfPathTooLong();

            try
            {
                FileUtilities.DeleteFile(path.Path);
            }
            catch (BuildXLException e)
            {
                // Preserving backward compatibility and throwing 'UnauthorizedAccessException' due to shared violation.
                // 0x20 is shared violation.
                if (e.InnerException is NativeWin32Exception win32 && win32.ErrorCode == 0x20)
                {
                    var extraMessage = FileUtilities.TryFindOpenHandlesToFile(path.ToString(), out var info, printCurrentFilePath: false)
                        ? info
                        : "Attempt to find processes with open handles to the file failed.";

                    throw new UnauthorizedAccessException($"{e.Message} {extraMessage}", e);
                }
            }
        }

        /// <inheritdoc />
        public void WriteAllBytes(AbsolutePath path, byte[] content)
        {
            path.ThrowIfPathTooLong();

            File.WriteAllBytes(path.Path, content);
        }

        /// <inheritdoc />
        public void MoveFile(AbsolutePath sourceFilePath, AbsolutePath destinationFilePath, bool replaceExisting)
        {
            sourceFilePath.ThrowIfPathTooLong();
            destinationFilePath.ThrowIfPathTooLong();

            if (OperatingSystemHelper.IsUnixOS)
            {
                if (replaceExisting && File.Exists(destinationFilePath.Path))
                {
                    File.Delete(destinationFilePath.Path);
                }

                File.Move(sourceFilePath.Path, destinationFilePath.Path);
            }
            else
            {
                if (!NativeMethods.MoveFileEx(
                    sourceFilePath.Path,
                    destinationFilePath.Path,
                    replaceExisting ? NativeMethods.MoveFileOption.MOVEFILE_REPLACE_EXISTING : 0))
                {
                    throw ThrowLastWin32Error(
                        destinationFilePath.Path,
                        string.Format(
                            CultureInfo.InvariantCulture,
                            "Unable to move file from '{0}' to '{1}'.",
                            sourceFilePath.Path,
                            destinationFilePath.Path));
                }
            }
        }

        /// <inheritdoc />
        public void MoveDirectory(AbsolutePath sourcePath, AbsolutePath destinationPath)
        {
            sourcePath.ThrowIfPathTooLong();
            destinationPath.ThrowIfPathTooLong();

            Directory.Move(sourcePath.Path, destinationPath.Path);
        }

        /// <inheritdoc />
        public async Task<Stream> OpenAsync(AbsolutePath path, FileAccess fileAccess, FileMode fileMode, FileShare share, FileOptions options, int bufferSize)
        {
            path.ThrowIfPathTooLong();

            if (FileSystemConstants.UnsupportedFileModes.Contains(fileMode))
            {
                throw new NotImplementedException($"The mode '{fileMode}' is not supported by the {nameof(PassThroughFileSystem)}.");
            }

            using (await ConcurrentAccess.WaitToken())
            {
                return TryOpenFile(path, fileAccess, fileMode, share, options, bufferSize);
            }
        }

        /// <inheritdoc />
        public Task<Stream> OpenReadOnlyAsync(AbsolutePath path, FileShare share)
        {
            return this.OpenAsync(path, FileAccess.Read, FileMode.Open, share);
        }

        private static bool GetSequentialScanOnOpenStreamThresholdEnvVariable(out long result)
        {
            var sequentialScanOnOpenThresholdOverride = Environment.GetEnvironmentVariable(SequentialScanOnOpenStreamThresholdEnvVariableName);
            if (!string.IsNullOrWhiteSpace(sequentialScanOnOpenThresholdOverride))
            {
                if (!long.TryParse(sequentialScanOnOpenThresholdOverride, out result))
                {
                    throw new ArgumentException(
                        $"Env var {SequentialScanOnOpenStreamThresholdEnvVariableName} with the value '{sequentialScanOnOpenThresholdOverride}' could not be parsed as a long size value.");
                }

                if (result < 0)
                {
                    throw new ArgumentException(
                        $"Env var {SequentialScanOnOpenStreamThresholdEnvVariableName} cannot be negative");
                }

                return true;
            }

            result = -1;
            return false;
        }

        private Task<bool> TryCopyOnWriteFileInsideSemaphoreAsync(
            AbsolutePath sourcePath,
            AbsolutePath destinationPath,
            bool replaceExisting)
        {
            return Task.Run(() =>
            {
                if (!FileUtilities.FileExistsNoFollow(sourcePath.Path))
                {
                    return false;
                }

                if (replaceExisting)
                {
                    var possiblyDeleteExistingDestination = FileUtilities.TryDeletePathIfExists(destinationPath.Path);

                    if (!possiblyDeleteExistingDestination.Succeeded)
                    {
                        return false;
                    }
                }

                CreateDirectory(destinationPath.Parent);

                var possiblyCreateCopyOnWrite = FileUtilities.TryCreateCopyOnWrite(sourcePath.Path, destinationPath.Path, followSymlink: false);

                return possiblyCreateCopyOnWrite.Succeeded;
            });
        }

        private async Task CopyFileInsideSemaphoreAsync(AbsolutePath sourcePath, AbsolutePath destinationPath, bool replaceExisting)
        {
            // It is very important to call OpenInternal and not to call OpenAsync method that will re-acquire the semaphore once again.
            // Violating this rule may cause a deadlock.
            using (var readStream = TryOpenFile(
                sourcePath, FileAccess.Read, FileMode.Open, FileShare.Read | FileShare.Delete, FileOptions.None, AbsFileSystemExtension.DefaultFileStreamBufferSize))
            {
                if (readStream == null)
                {
                    var message = string.Format(CultureInfo.InvariantCulture, "missing source file=[{0}]", sourcePath);
                    throw new FileNotFoundException(message, sourcePath.Path);
                }

                CreateDirectory(destinationPath.Parent);

                var mode = replaceExisting ? FileMode.OpenOrCreate : FileMode.CreateNew;

                using (var writeStream = TryOpenFile(
                    destinationPath, FileAccess.Write, mode, FileShare.Delete, FileOptions.None, AbsFileSystemExtension.DefaultFileStreamBufferSize))
                {
                    if (writeStream == null)
                    {
                        var message = string.Format(CultureInfo.InvariantCulture, "missing destination file=[{0}]", sourcePath);
                        throw new FileNotFoundException(message, sourcePath.Path);
                    }

                    await readStream.CopyToWithFullBufferAsync(writeStream, FileSystemConstants.FileIOBufferSize).ConfigureAwait(false);
                }
            }
        }

        private Stream TryOpenFile(AbsolutePath path, FileAccess accessMode, FileMode mode, FileShare share, FileOptions options, int bufferSize)
        {
            if (OperatingSystemHelper.IsUnixOS)
            {
                return TryOpenFileUnix(path, accessMode, mode, share, options, bufferSize);
            }
            else
            {
                return TryOpenFileWin(path, accessMode, mode, share, options, bufferSize);
            }
        }

        private Stream TryOpenFileUnix(AbsolutePath path, FileAccess accessMode, FileMode mode, FileShare share, FileOptions options, int bufferSize)
        {
            if (DirectoryExists(path))
            {
                throw new UnauthorizedAccessException($"Cannot open directory {path} as a file.");
            }

            if (mode == FileMode.Open && !FileExists(path))
            {
                return null;
            }

            return new FileStream(path.Path, mode, accessMode, share, bufferSize, options);
        }

        /// <summary>
        /// Tries opening a file with a given <paramref name="path"/>.
        /// </summary>
        /// <return>
        /// Method returns null if file or directory is not found.
        /// </return>
        /// <remarks>
        /// The method throws similar exception that <see cref="FileStream"/> constructor.
        /// </remarks>
        private Stream TryOpenFileWin(AbsolutePath path, FileAccess accessMode, FileMode mode, FileShare share, FileOptions options, int bufferSize)
        {
            options = GetOptions(path, options);

            var (result, exception) = tryOpenFile();

            if (exception != null && mode == FileMode.Create &&
                (exception.NativeErrorCode() == ERROR_ACCESS_DENIED || exception.NativeErrorCode() == ERROR_SHARING_VIOLATION))
            {
                // File creation failed with access denied or sharing violation.
                // Trying to change the attributes and delete the file.

                if (exception.NativeErrorCode() == ERROR_ACCESS_DENIED &&
                    (GetFileAttributes(path) & FileAttributes.ReadOnly) != 0)
                {
                    SetFileAttributes(path, FileAttributes.Normal);
                }

                DeleteFile(path);
                (result, exception) = tryOpenFile();
            }

            if (exception != null)
            {
                switch (exception.NativeErrorCode())
                {
                    case ERROR_FILE_NOT_FOUND:
                    case ERROR_PATH_NOT_FOUND:
                        return (Stream)null;
                    default:
                        throw ThrowLastWin32Error(
                            path.Path,
                            string.Format(
                                CultureInfo.InvariantCulture,
                                "Unable to open a file '{0}' as {1} with {2}.",
                                path.Path,
                                mode,
                                accessMode),
                            exception.NativeErrorCode());
                }
            }

            return result;

            (FileStream fileStream, Exception Exception) tryOpenFile()
            {
                try
                {
                    return (new TrackingFileStream(path.Path, mode, accessMode, share, bufferSize, options), null);
                }
                catch (Exception e)
                {
                    return (null, e);
                }
            }
        }

        private FileOptions GetOptions(AbsolutePath path, FileOptions options)
        {
            options |= FileOptions.Asynchronous;

            // Avoid churning filesystem cache with large existing files.
            if (FileExists(path) && GetFileSize(path) > _sequentialScanOnOpenThreshold)
            {
                options |= FileOptions.SequentialScan;
            }

            return options;
        }

        /// <inheritdoc />
        /// <remarks>
        ///     This was once an extension method, but had to be brought in as a member so that is could
        ///     use the ConcurrentAccess throttling mechanism to limit the number of calls into the
        ///     write stream dispose. That dispose call ends up in the framework calling its blocking
        ///     flush method. A large number of tasks in that call path end up causing the creation of
        ///     nearly a single dedicated thread per task.
        /// </remarks>
        public async Task CopyFileAsync(AbsolutePath sourcePath, AbsolutePath destinationPath, bool replaceExisting)
        {
            sourcePath.ThrowIfPathTooLong();
            destinationPath.ThrowIfPathTooLong();

            using (await ConcurrentAccess.WaitToken())
            {
                if (FileUtilities.IsCopyOnWriteSupportedByEnlistmentVolume)
                {
                    if (await TryCopyOnWriteFileInsideSemaphoreAsync(
                        sourcePath,
                        destinationPath,
                        replaceExisting))
                    {
                        return;
                    }
                }

                await CopyFileInsideSemaphoreAsync(sourcePath, destinationPath, replaceExisting);;
            }
        }

        /// <inheritdoc />
        public FileAttributes GetFileAttributes(AbsolutePath path)
        {
            path.ThrowIfPathTooLong();
            return File.GetAttributes(path.Path) & ~FileSystemConstants.UnsupportedFileAttributes;
        }

        /// <inheritdoc />
        public void SetFileAttributes(AbsolutePath path, FileAttributes attributes)
        {
            path.ThrowIfPathTooLong();
            if ((attributes & FileSystemConstants.UnsupportedFileAttributes) != 0)
            {
                throw new NotImplementedException();
            }

            FileUtilities.SetFileAttributes(path.Path, attributes);
        }

        /// <inheritdoc />
        public bool FileAttributesAreSubset(AbsolutePath path, FileAttributes attributes)
        {
            path.ThrowIfPathTooLong();
            return (attributes & File.GetAttributes(path.Path)) != 0;
        }

        /// <inheritdoc />
        public IEnumerable<AbsolutePath> EnumerateDirectories(AbsolutePath path, EnumerateOptions options)
        {
            path.ThrowIfPathTooLong();
            return Directory.EnumerateDirectories(
                path.Path,
                "*",
                (options & EnumerateOptions.Recurse) != 0 ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .Select(pathString => new AbsolutePath(pathString));
        }

        /// <inheritdoc />
        public void EnumerateFiles(AbsolutePath path, string pattern, bool recursive, Action<FileInfo> fileHandler)
        {
            path.ThrowIfPathTooLong();
            var result = FileUtilities.EnumerateFiles(
                path.Path,
                recursive,
                pattern ?? "*",
                (directory, fileName, attributes, length) =>
                {
                    fileHandler(
                        new FileInfo()
                        {
                            FullPath = new AbsolutePath(directory) / fileName,
                            Length = length
                        });
                });

            if (!result.Succeeded)
            {
                throw new IOException($"File enumeration failed for '{path}'.", new Win32Exception(result.NativeErrorCode));
            }
        }

        /// <inheritdoc />
        public long GetFileSize(AbsolutePath path)
        {
            path.ThrowIfPathTooLong();
            return new System.IO.FileInfo(path.Path).Length;
        }

        /// <inheritdoc />
        public DateTime GetLastAccessTimeUtc(AbsolutePath path)
        {
            path.ThrowIfPathTooLong();
            return new System.IO.FileInfo(path.Path).LastAccessTimeUtc;
        }

        /// <inheritdoc />
        public unsafe void SetLastAccessTimeUtc(AbsolutePath absPath, DateTime lastAccessTimeUtc)
        {
            absPath.ThrowIfPathTooLong();

            var path = absPath.Path;

            if (OperatingSystemHelper.IsUnixOS)
            {
                throw new PlatformNotSupportedException(".NETStandard APIs don't support creation time on MacOS.");
            }
            else
            {
                // System.IO.File.SetLastAccessTimeUtc over-requests the access it needs to set the timestamps on a file.
                // Specifically, it opens a handle to the file with GENERIC_WRITE which will fail on files marked read-only.
                var time = new NativeMethods.FILE_TIME(lastAccessTimeUtc);

                using (SafeFileHandle handle = NativeMethods.CreateFile(path, NativeMethods.FILE_WRITE_ATTRIBUTES, FileShare.ReadWrite | FileShare.Delete, IntPtr.Zero, FileMode.Open, 0, IntPtr.Zero))
                {
                    if (handle.IsInvalid)
                    {
                        throw ThrowLastWin32Error(
                            path,
                            string.Format(
                                CultureInfo.InvariantCulture,
                                "Unable to open a handle for SetFileTime at '{0}' to '{1}'.",
                                path,
                                lastAccessTimeUtc));
                    }

                    if (!NativeMethods.SetFileTime(handle, null, &time, null))
                    {
                        throw ThrowLastWin32Error(
                            path,
                            string.Format(
                                CultureInfo.InvariantCulture,
                                "Unable to SetFileTime at '{0}' to '{1}'.",
                                path,
                                lastAccessTimeUtc));
                    }
                }
            }
        }

        /// <inheritdoc />
        public IEnumerable<FileInfo> EnumerateFiles(AbsolutePath path, EnumerateOptions options)
        {
            path.ThrowIfPathTooLong();

            var dirInfo = new DirectoryInfo(path.Path);
            bool recursive = (options & EnumerateOptions.Recurse) != 0;

            foreach (System.IO.FileInfo fi in dirInfo.EnumerateFiles(
                "*",
                (options & EnumerateOptions.Recurse) != 0 ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly))
            {
                bool foundValidFile = false;
                FileInfo newFi = default(FileInfo);

                try
                { 
                    newFi = new FileInfo { FullPath = new AbsolutePath(fi.FullName), Length = fi.Length };
                    foundValidFile = true;
                }
                catch (FileNotFoundException)
                {
                    // Ignore FileNotFoundException here. EnumerateFiles returns a cached FileInfo and does not validate
                    // whether the underlying file exists. Calling Length on this FileInfo for the first time prompts it to
                    // refresh its properties/fields, leading to a FileNotFoundException if the file no longer exists.
                }

                if (foundValidFile)
                {
                    yield return newFi;
                }
            }
        }

        /// <inheritdoc />
        public CreateHardLinkResult CreateHardLink(AbsolutePath sourceFileName, AbsolutePath destinationFileName, bool replaceExisting)
        {
            // Intentionally do not check the file sizes because this is handled by the implementation.

            if (OperatingSystemHelper.IsUnixOS)
            {
                return CreateHardLinkUnix(sourceFileName, destinationFileName, replaceExisting);
            }

            return CreateHardLinkWin(sourceFileName, destinationFileName, replaceExisting);
        }

        private CreateHardLinkResult CreateHardLinkUnix(AbsolutePath sourceFileName, AbsolutePath destinationFileName, bool replaceExisting)
        {
            var createOrOpenResult = FileUtilities.TryCreateOrOpenFile(sourceFileName.ToString(), 0,
                            FileShare.ReadWrite | FileShare.Delete, FileMode.Open, FileFlagsAndAttributes.FileFlagOverlapped, out var sourceFileHandle);

            using (sourceFileHandle)
            {
                if (!createOrOpenResult.Succeeded)
                {
                    switch (createOrOpenResult.Status)
                    {
                        case OpenFileStatus.Success:
                            break;
                        case OpenFileStatus.FileNotFound:
                        case OpenFileStatus.PathNotFound:
                            return CreateHardLinkResult.FailedSourceDoesNotExist;
                        case OpenFileStatus.Timeout:
                        case OpenFileStatus.AccessDenied:
                        case OpenFileStatus.UnknownError:
                        default:
                            throw ThrowLastWin32Error(destinationFileName.Path, $"Failed to create or open file {sourceFileName.Path} to create hard link. Status: {createOrOpenResult.Status}");
                    }
                }
            }

            if (!replaceExisting && FileExists(destinationFileName))
            {
                return CreateHardLinkResult.FailedDestinationExists;
            }

            var createHardLinkResult = FileUtilities.TryCreateHardLink(destinationFileName.ToString(), sourceFileName.ToString());
            if (createHardLinkResult.HasFlag(CreateHardLinkStatus.FailedAccessDenied))
            {
                return CreateHardLinkResult.FailedAccessDenied;
            }
            else if (createHardLinkResult.HasFlag(CreateHardLinkStatus.FailedDueToPerFileLinkLimit))
            {
                return CreateHardLinkResult.FailedMaxHardLinkLimitReached;
            }
            else if (createHardLinkResult.HasFlag(CreateHardLinkStatus.FailedSinceDestinationIsOnDifferentVolume))
            {
                return CreateHardLinkResult.FailedSourceAndDestinationOnDifferentVolumes;
            }
            else if (createHardLinkResult.HasFlag(CreateHardLinkStatus.FailedSinceNotSupportedByFilesystem))
            {
                return CreateHardLinkResult.FailedNotSupported;
            }
            else if (createHardLinkResult.HasFlag(CreateHardLinkStatus.Failed))
            {
                return CreateHardLinkResult.Unknown;
            }
            else if (createHardLinkResult.HasFlag(CreateHardLinkStatus.Success))
            {
                return CreateHardLinkResult.Success;
            }
            else
            {
                throw ThrowLastWin32Error(sourceFileName.Path, $"Failed to create hard link for file {sourceFileName.Path} with unknown error: {createHardLinkResult.ToString()}");
            }
        }

        private CreateHardLinkResult CreateHardLinkWin(AbsolutePath sourceFileName, AbsolutePath destinationFileName, bool replaceExisting)
        {
            SafeFileHandle sourceFileHandle = NativeMethods.CreateFile(
                    sourceFileName.Path,
                    0,
                    /* Do not need to request any particular access to modify link info */ FileShare.ReadWrite | FileShare.Delete,
                    IntPtr.Zero,
                    FileMode.Open,
                    0 /* Allow symbolic links to redirect us */,
                    IntPtr.Zero);

            using (sourceFileHandle)
            {
                if (sourceFileHandle.IsInvalid)
                {
                    switch (Marshal.GetLastWin32Error())
                    {
                        case NativeMethods.ERROR_FILE_NOT_FOUND:
                        case NativeMethods.ERROR_PATH_NOT_FOUND:
                            return CreateHardLinkResult.FailedSourceDoesNotExist;
                        case NativeMethods.ERROR_ACCESS_DENIED:
                            return CreateHardLinkResult.FailedSourceAccessDenied;
                        default:
                            return CreateHardLinkResult.FailedSourceHandleInvalid;
                    }
                }

                if (destinationFileName.Length >= FileSystemConstants.MaxPath)
                {
                    return CreateHardLinkResult.FailedPathTooLong;
                }

                const string DosToNtPathPrefix = @"\??\";

                // NtSetInformationFile always expects a special prefix even for short paths.
                string path = DosToNtPathPrefix + destinationFileName.GetPathWithoutLongPathPrefix();

                var linkInfo = new NativeMethods.FileLinkInformation(path, replaceExisting);
                NativeMethods.NtStatus status = setLink(sourceFileHandle, linkInfo);

                if (status.StatusCodeUint == (uint)NativeMethods.NtStatusCode.StatusAccessDenied)
                {
                    // Access denied status can be returned by two reasons:
                    // 1. Something went wrong with the source path
                    // 2. Something went wrong with the destination path.
                    // The second case is potentially recoverable: so, we'll check the destination's file attribute
                    // and if the file has readonly attributes, then we'll remove them and will try to create hardlink one more time.
                    if (this.TryGetFileAttributes(destinationFileName, out var attributes) && (attributes & FileAttributes.ReadOnly) != 0)
                    {
                        SetFileAttributes(destinationFileName, FileAttributes.Normal);
                        status = setLink(sourceFileHandle, linkInfo);
                    }
                }

                if (status.Failed)
                {
                    switch (status.StatusCodeUint)
                    {
                        case (uint)NativeMethods.NtStatusCode.StatusTooManyLinks:
                            return CreateHardLinkResult.FailedMaxHardLinkLimitReached;
                        case (uint)NativeMethods.NtStatusCode.StatusObjectNameCollision:
                            return CreateHardLinkResult.FailedDestinationExists;
                        case (uint)NativeMethods.NtStatusCode.StatusNotSameDevice:
                            return CreateHardLinkResult.FailedSourceAndDestinationOnDifferentVolumes;
                        case (uint)NativeMethods.NtStatusCode.StatusAccessDenied:
                            return CreateHardLinkResult.FailedAccessDenied;
                        case (uint)NativeMethods.NtStatusCode.StatusNotSupported:
                            return CreateHardLinkResult.FailedNotSupported;
                        case (uint)NativeMethods.NtStatusCode.StatusObjectPathNotFound:
                            return CreateHardLinkResult.FailedDestinationDirectoryDoesNotExist;
                        default:
                            throw new NTStatusException(status.StatusCodeUint, status.StatusName, string.Format(
                                CultureInfo.InvariantCulture,
                                "Unable to create hard link at '{0}', pointing to existing file '{1}' with NTSTATUS:[0x{2:X}] = [{3}]",
                                destinationFileName,
                                sourceFileName,
                                status.StatusCodeUint,
                                status.StatusName));
                    }
                }

                return CreateHardLinkResult.Success;
            }

            NativeMethods.NtStatus setLink(SafeFileHandle handle, NativeMethods.FileLinkInformation linkInfo)
            {
                return NativeMethods.NtSetInformationFile(
                    handle,
                    out _,
                    linkInfo,
                    (uint)Marshal.SizeOf(linkInfo),
                    NativeMethods.FileInformationClass.FileLinkInformation);
            }
        }

        private void WithErrorHandling(AbsolutePath path, Action<AbsolutePath> action, Func<AbsolutePath, string> messageProvider)
        {
            try
            {
                action(path);
            }
            catch (FileNotFoundException ex)
            {
                throw new FileNotFoundException(messageProvider(path) + "|" + ex, ex);
            }
            catch (SystemException ex)
            {
                throw new IOException(messageProvider(path) + "|" + ex, ex);
            }
        }

        /// <inheritdoc />
        /// <remarks>
        ///     Interestingly, despite not requiring a handle as a parameter,
        ///     this is safe to call while holding a Stream to a file.
        /// </remarks>
        public void DenyFileWrites(AbsolutePath path)
        {
            path.ThrowIfPathTooLong();

            WithErrorHandling(
                path,
                absolutePath => SetFileWrites(absolutePath, AccessControlType.Deny),
                absolutePath => $"Failed to set a Deny Writes ACL on {absolutePath}");
        }

        /// <inheritdoc />
        /// <remarks>
        ///     Interestingly, despite not requiring a handle as a parameter,
        ///     this is safe to call while holding a Stream to a file.
        /// </remarks>
        public void AllowFileWrites(AbsolutePath path)
        {
            path.ThrowIfPathTooLong();

            WithErrorHandling(
                path,
                absolutePath => SetFileWrites(absolutePath, AccessControlType.Allow),
                absolutePath => $"Failed to remove a Deny Writes ACL on {absolutePath}");
        }

        /// <inheritdoc />
        /// <remarks>
        ///     Interestingly, despite not requiring a handle as a parameter,
        ///     this is safe to call while holding a Stream to a file.
        /// </remarks>
        public void DenyAttributeWrites(AbsolutePath path)
        {
            path.ThrowIfPathTooLong();

            WithErrorHandling(
                path,
                absolutePath => SetAttributeWrites(absolutePath, AccessControlType.Deny),
                absolutePath => $"Failed to set a Deny Write Attributes ACL on {absolutePath}");
        }

        /// <inheritdoc />
        /// <remarks>
        ///     Interestingly, despite not requiring a handle as a parameter,
        ///     this is safe to call while holding a Stream to a file.
        /// </remarks>
        public void AllowAttributeWrites(AbsolutePath path)
        {
            path.ThrowIfPathTooLong();

            WithErrorHandling(
                path,
                absolutePath => SetAttributeWrites(absolutePath, AccessControlType.Allow),
                absolutePath => $"Failed to remove a Deny Write Attributes ACL on {absolutePath}");
        }

        /// <inheritdoc />
        public AbsolutePath GetTempPath()
        {
            return new AbsolutePath(Path.GetTempPath());
        }

        /// <inheritdoc />
        public void FlushVolume(char driveLetter)
        {
            if (!BuildXL.Utilities.OperatingSystemHelper.IsUnixOS)
            {
                /*
                 * To flush all open files on a volume, call FlushFileBuffers with a handle to the volume. The caller must have administrative privileges. For more information, see Running with Special Privileges.
                 * When opening a volume with CreateFile, the lpFileName string should be the following form: \\.\x: or \\?\Volume{GUID}. Do not use a trailing backslash in the volume name, because that indicates the root directory of a drive.
                 * https://msdn.microsoft.com/en-us/library/windows/desktop/aa364439(v=vs.85).aspx
                 */
                string volumePath = string.Format(CultureInfo.InvariantCulture, "\\\\?\\{0}:", driveLetter);

                using (SafeFileHandle volumeHandle = NativeMethods.CreateFile(
                            volumePath,
                            NativeMethods.FILE_WRITE_DATA,
                            FileShare.Write,
                            IntPtr.Zero,
                            FileMode.Open,
                            0 /* Allow symbolic links to redirect us */,
                            IntPtr.Zero))
                {
                    if (volumeHandle.IsInvalid)
                    {
                        throw ThrowLastWin32Error(
                            path: null,
                            string.Format(
                                CultureInfo.InvariantCulture,
                                "Could not open volume handle to '{0}'.  Elevated administrator access is required.",
                                volumePath));
                    }

                    if (!NativeMethods.FlushFileBuffers(volumeHandle))
                    {
                        throw ThrowLastWin32Error(path: null, "Could not flush volume " + volumePath + ".");
                    }
                }
            }
        }

        /// <inheritdoc />
        public VolumeInfo GetVolumeInfo(AbsolutePath path)
        {
            path.ThrowIfPathTooLong();

            var di = new DriveInfo(new DriveInfo(path.Path).Name);
            return new VolumeInfo(di.TotalSize, di.AvailableFreeSpace);
        }

        private static void SetFileWrites(AbsolutePath path, AccessControlType accessControlType)
        {
            SetFileAccessControl(path, FileSystemRights.WriteData | FileSystemRights.AppendData, accessControlType);
        }

        private static void SetAttributeWrites(AbsolutePath path, AccessControlType accessControlType)
        {
            SetFileAccessControl(path, FileSystemRights.WriteAttributes | FileSystemRights.WriteExtendedAttributes, accessControlType);
        }

        private static void SetFileAccessControl(AbsolutePath path, FileSystemRights fileSystemRights, AccessControlType accessControlType)
        {
            FileUtilities.SetFileAccessControl(path.Path, fileSystemRights, accessControlType == AccessControlType.Allow);
        }

        private static Exception ThrowLastWin32Error(string path, string message, int? lastErrorArg = null)
        {
            var lastError = lastErrorArg ?? Marshal.GetLastWin32Error();
            if (OperatingSystemHelper.IsUnixOS)
            {
                throw new IOException(message);
            }
            else
            {
                var errorMessage = NativeMethods.GetErrorMessage(lastError);
                message = string.Format(CultureInfo.InvariantCulture, "{0} last error: [{1}] (error code {2})", message, errorMessage, lastError);

                switch (lastError)
                {
                    case ERROR_FILE_NOT_FOUND:
                        throw new FileNotFoundException(message);
                    case ERROR_PATH_NOT_FOUND:
                        throw new DirectoryNotFoundException(message);
                    case ERROR_ACCESS_DENIED:
                    case ERROR_SHARING_VIOLATION:

                        string extraMessage = string.Empty;

                        if (path != null)
                        {
                            extraMessage = " " + (FileUtilities.TryFindOpenHandlesToFile(path, out var info, printCurrentFilePath: false)
                                ? info
                                : "Attempt to find processes with open handles to the file failed.");
                        }

                        throw new UnauthorizedAccessException($"{message}.{extraMessage}");
                    default:
                        throw new IOException(message, ExceptionUtilities.HResultFromWin32(lastError));
                }
            }
        }
    }
}
