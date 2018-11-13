// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.AccessControl;
using System.Text;
using System.Text.RegularExpressions;
using BuildXL.Native.IO.Windows;
using BuildXL.Native.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;
using Microsoft.Win32.SafeHandles;
using static BuildXL.Interop.MacOS.IO;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Native.IO.Unix
{
    /// <summary>
    /// FileSystem related native implementations for Unix based systems
    /// </summary>
    public sealed class FileSystemUnix : IFileSystem
    {
        internal static int DefaultBufferSize = 4096;

        private static readonly Dictionary<FileFlagsAndAttributes, FileOptions> s_fileOptionsFlags = new Dictionary<FileFlagsAndAttributes, FileOptions>()
        {
            { FileFlagsAndAttributes.FileAttributeEncrypted, FileOptions.Encrypted },
            { FileFlagsAndAttributes.FileFlagDeleteOnClose, FileOptions.DeleteOnClose },
            { FileFlagsAndAttributes.FileFlagSequentialScan, FileOptions.SequentialScan },
            { FileFlagsAndAttributes.FileFlagRandomAccess, FileOptions.RandomAccess },
            { FileFlagsAndAttributes.FileFlagOverlapped, FileOptions.Asynchronous },
            { FileFlagsAndAttributes.FileFlagWriteThrough, FileOptions.WriteThrough }
        };


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

        private static OpenFileResult TryOpenDirectory(
            string directoryPath,
            FileDesiredAccess desiredAccess,
            FileShare shareMode,
            FileMode fileMode,
            FileFlagsAndAttributes flagsAndAttributes,
            out SafeFileHandle handle)
        {
            Contract.Requires(!string.IsNullOrEmpty(directoryPath));

            if (flagsAndAttributes.HasFlag(FileFlagsAndAttributes.FileFlagOpenReparsePoint) &&
                IsSymlink(directoryPath))
            {
                return TryOpenSymlink(directoryPath, desiredAccess, shareMode, fileMode, out handle);
            }

            try
            {
                FileStream fs = new FileStream(directoryPath, fileMode, FileDesiredAccessToFileAccess(desiredAccess), shareMode, DefaultBufferSize, FileFlagsAndAttributesToFileOptions(flagsAndAttributes | FileFlagsAndAttributes.FileFlagBackupSemantics));
                handle = fs.SafeFileHandle;
                return OpenFileResult.Create(NativeIOConstants.ErrorSuccess, fileMode, handleIsValid: true, openingById: false);
            }
            catch (Exception ex)
            {
                handle = null;
                int nativeErrorCode = (int)NativeErrorCodeForException(ex);
                Logger.Log.StorageTryOpenDirectoryFailure(Events.StaticContext, directoryPath, nativeErrorCode);
                return OpenFileResult.Create(nativeErrorCode, fileMode, handleIsValid: false, openingById: false);
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
        public void CreateDirectory(string directoryPath)
        {
            try
            {
                Directory.CreateDirectory(directoryPath);
            }
            catch (Exception ex)
            {
                throw new BuildXLException("Create directory failed", ex);
            }
        }

        /// <inheritdoc />
        public void RemoveDirectory(string path)
        {
            try
            {
                Directory.Delete(path, false);
            }
            catch (Exception ex)
            {
                throw new NativeWin32Exception((ex.InnerException ?? ex).HResult, (ex.InnerException ?? ex).ToString());
            }
        }

        /// <inheritdoc />
        public bool TryRemoveDirectory(string path, out int hr)
        {
            try
            {
                RemoveDirectory(path);
                hr = Marshal.GetLastWin32Error();
            }
            catch (NativeWin32Exception ex)
            {
                hr = ex.NativeErrorCode;
                return false;
            }

            return true;
        }

        /// <inheritdoc />
        public EnumerateDirectoryResult EnumerateDirectoryEntries(
            string directoryPath,
            bool recursive,
            Action<string, string, FileAttributes> handleEntry) => EnumerateDirectoryEntries(directoryPath, recursive, "*", handleEntry);

        /// <inheritdoc />
        public EnumerateDirectoryResult EnumerateDirectoryEntries(
            string directoryPath,
            bool recursive,
            string pattern,
            Action<string, string, FileAttributes> handleEntry)
        {
            try
            {
                var directoryEntries = Directory.GetFileSystemEntries(directoryPath);
                var enumerator = directoryEntries.GetEnumerator();
                pattern = FileSystemName.TranslateWin32Expression(pattern);

                while (enumerator.MoveNext())
                {
                    var entry = enumerator.Current as string;
                    var path = Path.GetDirectoryName(entry);
                    var filename = entry.Split(Path.DirectorySeparatorChar).Last();

                    FileAttributes attributes;
                    try
                    {
                        attributes = File.GetAttributes(entry);
                    }
                    catch (FileNotFoundException)
                    {
                        continue;
                    }

                    if (FileSystemName.MatchesWin32Expression(pattern, filename))
                    {
                        handleEntry(path, filename, attributes);
                    }

                    // important to not follow directory symlinks because infinite recussion can occur otherwise
                    if (recursive && FileUtilities.IsDirectoryNoFollow(attributes))
                    {
                        var recurs = EnumerateDirectoryEntries(
                                entry,
                                true,
                                pattern,
                                handleEntry);

                        if (!recurs.Succeeded)
                        {
                            return recurs;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return EnumerateDirectoryResult.CreateFromException(directoryPath, ex);
            }

            return new EnumerateDirectoryResult(
                directoryPath,
                EnumerateDirectoryStatus.Success,
                (int)NativeIOConstants.ErrorNoMoreFiles);
        }

        /// <inheritdoc />
        public EnumerateDirectoryResult EnumerateDirectoryEntries(string directoryPath, Action<string, FileAttributes> handleEntry) =>
            EnumerateDirectoryEntries(directoryPath, false, (currentDirectory, fileName, fileAttributes) => handleEntry(fileName, fileAttributes));

        /// <inheritdoc />
        public EnumerateDirectoryResult EnumerateDirectoryEntries(
            string directoryPath,
            bool enumerateDirectory,
            string pattern,
            uint directoriesToSkipRecursively,
            bool recursive,
            IDirectoryEntriesAccumulator accumulators)
        {
            try
            {
                var directoryEntries = Directory.GetFileSystemEntries(directoryPath);
                Array.Sort(directoryEntries, StringComparer.InvariantCulture);

                var enumerator = directoryEntries.GetEnumerator();
                var accumulator = accumulators.Current;
                pattern = FileSystemName.TranslateWin32Expression(pattern);

                while (enumerator.MoveNext())
                {
                    var entry = enumerator.Current as string;
                    var lastSegment = entry.Split(Path.DirectorySeparatorChar).Last();

                    FileAttributes attributes = File.GetAttributes(entry);
                    var isDirectory = (attributes & FileAttributes.Directory) == FileAttributes.Directory;

                    if (FileSystemName.MatchesWin32Expression(pattern, lastSegment))
                    {
                        if (!(enumerateDirectory ^ isDirectory) && directoriesToSkipRecursively == 0)
                        {
                            accumulator.AddFile(lastSegment);
                        }
                    }

                    accumulator.AddTrackFile(lastSegment, attributes);

                    if ((recursive || directoriesToSkipRecursively > 0) && isDirectory)
                    {
                        accumulators.AddNew(accumulator, lastSegment);

                        var recurs = EnumerateDirectoryEntries(
                            entry,
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
            }
            catch (Exception ex)
            {
                var result = EnumerateDirectoryResult.CreateFromException(directoryPath, ex);
                accumulators.Current.Succeeded = false;
                return result;
            }

            return new EnumerateDirectoryResult(
                directoryPath,
                EnumerateDirectoryStatus.Success,
                (int)NativeIOConstants.ErrorNoMoreFiles);
        }

        /// <inheritdoc />
        public string GetFinalPathNameByHandle(SafeFileHandle handle, bool volumeGuidPath = false) => throw new NotImplementedException();

        /// <inheritdoc />
        public OpenFileResult TryOpenFileById(
            SafeFileHandle existingHandleOnVolume,
            FileId fileId,
            FileDesiredAccess desiredAccess,
            FileShare shareMode,
            FileFlagsAndAttributes flagsAndAttributes,
            out SafeFileHandle handle) => throw new NotImplementedException();

        /// <inheritdoc />
        public bool CanGetFileIdAndVolumeIdByHandle()
            => false; // TODO: Task #1272136 (FileContentTable)

        /// <inheritdoc />
        public unsafe MiniUsnRecord? ReadFileUsnByHandle(SafeFileHandle fileHandle, bool forceJournalVersion2 = false)
            => null; // TODO: Task #1272136 (FileContentTable)

        /// <inheritdoc />
        public FileSystemType GetVolumeFileSystemByHandle(SafeFileHandle fileHandle) => throw new NotImplementedException();

        /// <inheritdoc />
        public unsafe Usn? TryWriteUsnCloseRecordByHandle(SafeFileHandle fileHandle)
            => null; // TODO: Task #1272136 (FileContentTable)

        /// <inheritdoc />
        public unsafe uint GetShortVolumeSerialNumberByHandle(SafeFileHandle fileHandle) => throw new NotImplementedException();

        /// <inheritdoc />
        public unsafe FileIdAndVolumeId? TryGetFileIdAndVolumeIdByHandle(SafeFileHandle fileHandle)
            => null; // TODO: Task #1272136 (FileContentTable)

        /// <inheritdoc />
        public List<Tuple<VolumeGuidPath, ulong>> ListVolumeGuidPathsAndSerials()
            => new List<Tuple<VolumeGuidPath, ulong>>(0); // TODO: Task #1272136 (FileContentTable)

        /// <inheritdoc />
        public ulong GetVolumeSerialNumberByHandle(SafeFileHandle fileHandle)
            => 0; // TODO: Task #1272136 (FileContentTable)

        /// <nodoc />
        private static FileOptions FileFlagsAndAttributesToFileOptions(FileFlagsAndAttributes flagsAndAttributes) =>
            s_fileOptionsFlags.Aggregate(FileOptions.None, (acc, kvp) => flagsAndAttributes.HasFlag(kvp.Key) ? acc | kvp.Value : acc);

        /// <nodoc />
        internal static FileAccess FileDesiredAccessToFileAccess(FileDesiredAccess desiredAccess)
        {
            FileAccess access = FileAccess.Read;

            if (desiredAccess.HasFlag(FileDesiredAccess.GenericWrite))
            {
                access = FileAccess.Write;
            }

            if (desiredAccess.HasFlag(FileDesiredAccess.GenericRead) && desiredAccess.HasFlag(FileDesiredAccess.GenericWrite))
            {
                access = FileAccess.ReadWrite;
            }

            return access;
        }

        /// <nodoc />
        internal static uint NativeErrorCodeForException(Exception ex)
        {
            switch (ex)
            {
                case ArgumentOutOfRangeException aourEx:
                case ArgumentNullException anEx:
                case ArgumentException ae:
                    return NativeIOConstants.ErrorInvalidParameter;
                case UnauthorizedAccessException uaEx:
                    return NativeIOConstants.ErrorAccessDenied;
                case DirectoryNotFoundException dnfEx:
                case FileNotFoundException fnfEx:
                    return NativeIOConstants.ErrorFileNotFound;
                case PathTooLongException patlEx:
                    return NativeIOConstants.ErrorPathNotFound;
                case IOException ioEx:
                case SecurityException se:
                case NotSupportedException nosEx:
                default:
                    return NativeIOConstants.ErrorNotSupported;
            }
        }

        private static OpenFlags CreateOpenFlags(FileDesiredAccess desiredAccess, FileShare fileShare, FileMode fileMode)
        {
            OpenFlags flags = ShouldCreateAndOpen(fileMode) ? OpenFlags.O_CREAT : 0;

            switch (fileMode)
            {
                case FileMode.Append:
                    flags |= OpenFlags.O_APPEND;
                    break;
                case FileMode.Truncate:
                    flags |= OpenFlags.O_TRUNC;
                    break;
                default:
                    break;
            }

            if (fileShare == FileShare.None)
            {
                flags |= OpenFlags.O_EXLOCK;
            }
            else
            {
                flags |= OpenFlags.O_SHLOCK;
            }

            if (desiredAccess.HasFlag(FileDesiredAccess.GenericRead) && !desiredAccess.HasFlag(FileDesiredAccess.GenericWrite))
            {
                flags |= OpenFlags.O_RDONLY;
            }

            if (desiredAccess.HasFlag(FileDesiredAccess.GenericWrite) && !desiredAccess.HasFlag(FileDesiredAccess.GenericRead))
            {
                flags |= OpenFlags.O_WRONLY;
            }

            if (desiredAccess.HasFlag(FileDesiredAccess.GenericRead | FileDesiredAccess.GenericWrite))
            {
                flags |= OpenFlags.O_RDWR;
            }

            return flags;
        }

        private static FilePermissions CreateFilePermissions(FileDesiredAccess desiredAccess)
        {
            FilePermissions permissions = 0;

            if (desiredAccess.HasFlag(FileDesiredAccess.GenericRead))
            {
                permissions |= (FilePermissions.S_IRUSR | FilePermissions.S_IRGRP | FilePermissions.S_IROTH);
            }

            if (desiredAccess.HasFlag(FileDesiredAccess.GenericWrite))
            {
                permissions |= (FilePermissions.S_IWUSR | FilePermissions.S_IWGRP | FilePermissions.S_IWOTH);
            }

            return permissions;
        }

        private static bool ShouldCreateAndOpen(FileMode fileMode)
        {
            return fileMode == FileMode.CreateNew || fileMode == FileMode.Create || fileMode == FileMode.OpenOrCreate;
        }

        private static OpenFileResult TryOpenSymlink(
            string path,
            FileDesiredAccess desiredAccess,
            FileShare shareMode,
            FileMode fileMode,
            out SafeFileHandle handle)
        {
            try
            {
                int fileHandle = 0;
                if (ShouldCreateAndOpen(fileMode))
                {
                    fileHandle = openAndCreate(path, (int)(CreateOpenFlags(desiredAccess, shareMode, fileMode) | OpenFlags.O_SYMLINK), CreateFilePermissions(desiredAccess));
                }
                else
                {
                    fileHandle = open(path, (int)(CreateOpenFlags(desiredAccess, shareMode, fileMode) | OpenFlags.O_SYMLINK));
                }

                handle = new SafeFileHandle(new IntPtr(fileHandle), ownsHandle: true);
                if (fileHandle <= 0)
                {
                    handle = null;
                    return CreateErrorResult(Marshal.GetLastWin32Error());
                }

                return OpenFileResult.Create(NativeIOConstants.ErrorSuccess, fileMode, handleIsValid: true, openingById: false);
            }
            catch (Exception e)
            {
                handle = null;
                return CreateErrorResult((int)NativeErrorCodeForException(e));
            }

            OpenFileResult CreateErrorResult(int errorCode)
            {
                Logger.Log.StorageTryOpenOrCreateFileFailure(Events.StaticContext, path, (int)fileMode, (int)errorCode);
                return OpenFileResult.Create((int)errorCode, fileMode, handleIsValid: false, openingById: false);
            }
        }

        private static bool IsSymlink(string path)
        {
            var maybeReparsePointType = GetReparsePointType(path);
            return maybeReparsePointType.Succeeded && maybeReparsePointType.Result == ReparsePointType.SymLink;
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
            if (flagsAndAttributes.HasFlag(FileFlagsAndAttributes.FileFlagOpenReparsePoint) &&
                IsSymlink(path))
            {
                return TryOpenSymlink(path, desiredAccess, shareMode, creationDisposition, out handle);
            }

            try
            {
                FileStream fs = new FileStream(path, creationDisposition, FileDesiredAccessToFileAccess(desiredAccess), shareMode, DefaultBufferSize, FileFlagsAndAttributesToFileOptions(flagsAndAttributes));
                handle = fs.SafeFileHandle;
                return OpenFileResult.Create(NativeIOConstants.ErrorSuccess, creationDisposition, handleIsValid: true, openingById: false);
            }
            catch (Exception ex)
            {
                handle = null;
                var errorCode = NativeErrorCodeForException(ex);
                Logger.Log.StorageTryOpenOrCreateFileFailure(Events.StaticContext, path, (int)creationDisposition, (int)errorCode);
                return OpenFileResult.Create((int)errorCode, creationDisposition, handleIsValid: false, openingById: false);
            }
        }

        /// <inheritdoc />
        public unsafe bool TrySetDeletionDisposition(SafeFileHandle handle) => throw new NotImplementedException();

        /// <inheritdoc />
        public ReOpenFileStatus TryReOpenFile(
            SafeFileHandle existing,
            FileDesiredAccess desiredAccess,
            FileShare shareMode,
            FileFlagsAndAttributes flagsAndAttributes,
            out SafeFileHandle reopenedHandle) => throw new NotImplementedException();

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
                    try
                    {
                        return new FileStream(path, fileMode, fileAccess, fileShare, bufferSize: DefaultBufferSize, options: options);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // This is a workaround to allow write access to a file that is marked as readonly. It is
                        // exercised when hashing the output files of pips that create readonly files. The hashing currently
                        // opens files as write
                        if (force)
                        {
                            File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
                            return new FileStream(path, fileMode, fileAccess, fileShare, bufferSize: DefaultBufferSize, options: options);
                        }

                        throw;
                    }
                },
                ex =>
                {
                    throw new BuildXLException(I($"Failed to open path '{path}'"), ex);
                });
        }

        /// <inheritdoc />
        public unsafe bool TryPosixDelete(string pathToDelete, out OpenFileResult openFileResult) => throw new NotImplementedException();

        /// <inheritdoc />
        public int MaxDirectoryPathLength() => 1024; // TODO: don't hardcode

        /// <inheritdoc />
        public CreateHardLinkStatus TryCreateHardLink(string link, string linkTarget)
        {
            // POSIX systems use the opposite ordering of inputs as Windows for linking files
            // Function stub from GNU docs: int link (const char *oldname, const char *newname)
            int result = BuildXL.Interop.MacOS.IO.link(linkTarget, link);
            if (result != 0)
            {
                var errno = Marshal.GetLastWin32Error();
                switch (errno)
                {
                    case (int)Errno.EACCES:
                        return CreateHardLinkStatus.FailedAccessDenied;
                    case (int)Errno.ELOOP:
                    case (int)Errno.EMLINK:
                        return CreateHardLinkStatus.FailedDueToPerFileLinkLimit;
                    case (int)Errno.EPERM:
                        return CreateHardLinkStatus.FailedAccessDenied;
                    default:
                        return CreateHardLinkStatus.Failed;
                }
            }

            return CreateHardLinkStatus.Success;
        }

        /// <inheritdoc />
        public CreateHardLinkStatus TryCreateHardLinkViaSetInformationFile(string link, string linkTarget, bool replaceExisting = true) => throw new NotImplementedException();

        /// <inheritdoc />
        public bool TryCreateSymbolicLink(string symLinkFileName, string target, bool isTargetFile)
        {
            try
            {
                var parentDirPath = Path.GetDirectoryName(symLinkFileName);
                if (!Directory.Exists(parentDirPath))
                {
                    CreateDirectory(parentDirPath);
                }

                int err = symlink(target, symLinkFileName);
                return err == 0;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
        }

        /// <inheritdoc />
        public void SetFileAttributes(string path, FileAttributes attributes)
        {
            int result = GetFilePermissionsForFilePath(path);
            if (result < 0)
            {
                throw new BuildXLException($"Failed to get permissions for file '{path}' - error: {Marshal.GetLastWin32Error()}");
            }

            FilePermissions filePermissions = checked((FilePermissions)result);
            FilePermissions writePermissions = (FilePermissions.S_IWUSR | FilePermissions.S_IWGRP | FilePermissions.S_IWOTH);

            if (attributes.HasFlag(FileAttributes.ReadOnly))
            {
                filePermissions &= ~writePermissions;
            }
            else
            {
                filePermissions |= writePermissions;
            }

            result = SetFilePermissionsForFilePath(path, filePermissions);
            if (result < 0)
            {
                throw new BuildXLException($"Failed to set permissions for file '{path}' - error: {Marshal.GetLastWin32Error()}");
            }
        }

        /// <inheritdoc />
        public bool TryReadSeekPenaltyProperty(SafeFileHandle driveHandle, out bool hasSeekPenalty, out int error) => throw new NotImplementedException();

        /// <inheritdoc />
        public FileAttributes GetFileAttributes(string path)
        {
            return TryGetFileAttributes(path);
        }

        private static FileAttributes TryGetFileAttributes(string path)
        {
            try
            {
                return File.GetAttributes(path);
            }
            catch (Exception ex)
            {
                throw new BuildXLException("Getting file attributes failed", ex);
            }
        }

        /// <inheritdoc />
        public unsafe FileAttributes GetFileAttributesByHandle(SafeFileHandle fileHandle) => throw new NotImplementedException();

        /// <inheritdoc />
        public unsafe bool IsPendingDelete(SafeFileHandle fileHandle) => throw new NotImplementedException();

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
        public unsafe bool TryRename(SafeFileHandle handle, string destination, bool replaceExisting) => throw new NotImplementedException();

        /// <inheritdoc />
        public bool IsReparsePointActionable(ReparsePointType reparsePointType)
        {
            Contract.Requires(reparsePointType != ReparsePointType.MountPoint, "Currently, ReparsePointType.MountPoint is not a valid reparse point type on macOS/Unix");
            return reparsePointType == ReparsePointType.SymLink;
        }

        /// <inheritdoc />
        public Possible<ReparsePointType> TryGetReparsePointType(string path)
        {
            return GetReparsePointType(path);
        }

        private static Possible<ReparsePointType> GetReparsePointType(string path)
        {
            try
            {
                FileAttributes attributes = TryGetFileAttributes(path);

                if ((attributes & FileAttributes.ReparsePoint) == 0)
                {
                    return ReparsePointType.None;
                }

                // The only reparse type supported by CoreFX is a symlink currently, we can revisit this once the implementation changes.
                // See: https://github.com/dotnet/corefx/blob/f25eb288a449010574a6e95fe298f3ad880ada1e/src/System.IO.FileSystem/src/System/IO/FileStatus.Unix.cs
                return ReparsePointType.SymLink;
            }
            catch
            {
                return ReparsePointType.None;
            }
        }

        /// <inheritdoc />
        public void GetChainOfReparsePoints(SafeFileHandle handle, string sourcePath, IList<string> chainOfReparsePoints)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(sourcePath));
            Contract.Requires(chainOfReparsePoints != null);

            do
            {
                chainOfReparsePoints.Add(sourcePath);

                var possibleReparsePointType = TryGetReparsePointType(sourcePath);
                if (!possibleReparsePointType.Succeeded || !possibleReparsePointType.Result.IsActionable())
                {
                    return;
                }

                var possibleTarget = TryGetReparsePointTarget(handle, sourcePath);
                if (!possibleTarget.Succeeded)
                {
                    return;
                }

                sourcePath = FileUtilities.ConvertReparsePointTargetPathToAbsolutePath(sourcePath, possibleTarget.Result);
            } while (true);

        }

        /// <inheritdoc />
        public Possible<string> TryGetReparsePointTarget(SafeFileHandle handle, string sourcePath)
        {
            try
            {
                var maxPathLength = BuildXL.Native.IO.NativeIOConstants.MaxPath + 1;
                var sb = new StringBuilder(maxPathLength);
                long numCharactersWritten = SafeReadLink(sourcePath, sb, maxPathLength);
                if (numCharactersWritten >= 0 && numCharactersWritten <= maxPathLength)
                {
                    return sb.ToString().TrimEnd('\0');
                }
                else
                {
                    return CreateFailure("Encountered error: " + Marshal.GetLastWin32Error());
                }
            }
            catch (Exception e)
            {
                return CreateFailure("Exception caught", e);
            }

            RecoverableExceptionFailure CreateFailure(string message, Exception e = null)
            {
                return new RecoverableExceptionFailure(
                    new BuildXLException(nameof(TryGetReparsePointTarget) + " failed: " + message, e));
            }
        }

        /// <inheritdoc />
        public unsafe ReadUsnJournalResult TryReadUsnJournal(
            SafeFileHandle volumeHandle,
            byte[] buffer,
            ulong journalId,
            Usn startUsn = default(Usn),
            bool forceJournalVersion2 = false,
            bool isJournalUnprivileged = false) => throw new NotImplementedException();

        /// <inheritdoc />
        public QueryUsnJournalResult TryQueryUsnJournal(SafeFileHandle volumeHandle) => throw new NotImplementedException();

        /// <inheritdoc />
        public unsafe NtStatus FlushPageCacheToFilesystem(SafeFileHandle handle) => throw new NotImplementedException();

        /// <inheritdoc />
        public void CreateJunction(string junctionPoint, string targetDir) => throw new NotImplementedException();

        /// <inheritdoc />
        public Possible<PathExistence, NativeFailure> TryProbePathExistence(string path, bool followSymlink)
        {
            var mode = GetFilePermissionsForFilePath(path, followSymlink: false);
            if (mode < 0)
            {
                return PathExistence.Nonexistent;
            }

            if (followSymlink)
            {
                return Directory.Exists(path)
                    ? PathExistence.ExistsAsDirectory
                    : PathExistence.ExistsAsFile;
            }
            else
            {
                FilePermissions permissions = checked((FilePermissions)mode);
                return
                    permissions.HasFlag(FilePermissions.S_IFDIR) ? PathExistence.ExistsAsDirectory :
                    permissions.HasFlag(FilePermissions.S_IFLNK) ? PathExistence.ExistsAsFile :
                    PathExistence.ExistsAsFile;
            }
        }

        /// <inheritdoc />
        public uint GetHardLinkCountByHandle(SafeFileHandle handle) => throw new NotImplementedException();

        /// <inheritdoc />
        public bool PathMatchPattern(string path, string pattern)
        {
            Regex regex = new Regex((pattern.StartsWith("*") ? "." : "") + pattern, RegexOptions.IgnoreCase);
            return regex.IsMatch(path);
        }

        /// <inheritdoc/>
        public bool IsWciReparsePoint(string path)
        {
            return false;
        }

        /// <inheritdoc/>
        public bool IsVolumeMapped(string volume) => false;
    }
}
