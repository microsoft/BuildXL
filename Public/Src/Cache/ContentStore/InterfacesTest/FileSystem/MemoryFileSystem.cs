// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.InterfacesTest.Utils;
using BuildXL.Utilities;
using FileInfo = BuildXL.Cache.ContentStore.Interfaces.FileSystem.FileInfo;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

namespace BuildXL.Cache.ContentStore.InterfacesTest.FileSystem
{
    /// <summary>
    ///     An IFileSystem implementation that is entirely in memory.
    /// </summary>
    public class MemoryFileSystem : IAbsFileSystem
    {
        public readonly ITestClock Clock;
        private readonly bool _useHardLinks;
        private readonly AbsolutePath _tempPath;

        private readonly Dictionary<char, FileObject> _drives = new Dictionary<char, FileObject>(
            CharCaseInsensitiveComparer.Instance);

        private readonly long _volumeSize = long.MaxValue;
        private bool _disposed;

        /// <summary>
        ///     Statistics structure for easy copy-by-value snapshots.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
        public struct Statistics
        {
            /// <summary>
            ///     Number of times a file is opened just for read
            /// </summary>
            public int FileOpensForRead;

            /// <summary>
            ///     Number of times a file is opened for write or read/write
            /// </summary>
            public int FileOpensForWrite;

            /// <summary>
            ///     Number of times a file's content is read from
            /// </summary>
            public int FileDataReadOperations;

            /// <summary>
            ///     Number of times a file's content is written to
            /// </summary>
            public int FileDataWriteOperations;

            /// <summary>
            ///     Number of times a file is moved
            /// </summary>
            public int FileMoves;

            /// <summary>
            ///     Number of times a file is moved
            /// </summary>
            public int FileDeletes;
        }

        private Statistics _currentStatistics;

        /// <summary>
        ///     Gets current statistic counts
        /// </summary>
        public Statistics CurrentStatistics => _currentStatistics;

        /// <summary>
        ///     Initializes a new instance of the <see cref="MemoryFileSystem" /> class.
        /// </summary>
        /// <param name="clock">Clock to use.</param>
        /// <param name="drives">Drives to support. If null, have the same drive letters as the current OS.</param>
        /// <param name="useHardLinks">Whether all drives support hardlinks.</param>
        /// <param name="tempPath">Optional override of default temporary directory.</param>
        public MemoryFileSystem(ITestClock clock, IEnumerable<char> drives = null, bool useHardLinks = true, AbsolutePath tempPath = null)
        {
            Contract.Requires(clock != null);

            Clock = clock;
            _useHardLinks = useHardLinks;

            if (OperatingSystemHelper.IsUnixOS)
            {
                _drives.Add(Path.VolumeSeparatorChar, new FileObject());
            }
            else
            {
                drives = drives ?? Directory.GetLogicalDrives().Select(driveString => driveString[0]);

                foreach (char driveLetter in drives)
                {
                    _drives.Add(char.ToUpperInvariant(driveLetter), new FileObject());
                }
            }

            if (tempPath != null)
            {
                if (!_drives.ContainsKey(char.ToUpperInvariant(tempPath.Path[0])))
                {
                    throw new ArgumentException("tempPath is not on an available drive");
                }

                _tempPath = tempPath;
            }
            else
            {
                if (OperatingSystemHelper.IsUnixOS)
                {
                    _tempPath = new AbsolutePath(Path.VolumeSeparatorChar + "temp");
                }
                else
                {
                    var firstDriveLetter = _drives.Keys.First();
                    var path = string.Format(CultureInfo.InvariantCulture, "{0}:\\temp", firstDriveLetter);
                    _tempPath = new AbsolutePath(path);
                }
            }
        }

        protected MemoryFileSystem(ITestClock clock, long volumeSize)
            : this(clock)
        {
            Contract.Requires(volumeSize > 0);
            _volumeSize = volumeSize;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Dispose(true);
            GC.SuppressFinalize(this);

            _disposed = true;
        }

        /// <summary>
        ///     Dispose pattern.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
        }

        /// <inheritdoc />
        public virtual IEnumerable<FileInfo> EnumerateFiles(AbsolutePath path, EnumerateOptions options)
        {
            var infos = new List<FileInfo>();
            lock (_drives)
            {
                FileObject root = FindFileObject(path);
                if (root == null)
                {
                    throw new DirectoryNotFoundException();
                }

                var recursive = (options & EnumerateOptions.Recurse) != 0;
                EnumerateObjectPaths(path, root, recursive, fileObject => !fileObject.IsDirectory, infos);
            }

            return infos;
        }

        /// <inheritdoc />
        public IEnumerable<AbsolutePath> EnumerateDirectories(AbsolutePath path, EnumerateOptions options)
        {
            var infos = new List<FileInfo>();
            lock (_drives)
            {
                FileObject root = FindFileObject(path);
                if (root == null)
                {
                    throw new DirectoryNotFoundException();
                }

                var recursive = (options & EnumerateOptions.Recurse) != 0;
                EnumerateObjectPaths(path, root, recursive, fileObject => fileObject.IsDirectory, infos);
            }

            return infos.Select(info => info.FullPath);
        }

        /// <inheritdoc />
        public void EnumerateFiles(AbsolutePath path, string pattern, bool recursive, Action<FileInfo> fileHandler)
        {
            var infos = new List<FileInfo>();
            lock (_drives)
            {
                FileObject root = FindFileObject(path);
                if (root == null)
                {
                    throw new DirectoryNotFoundException();
                }

                EnumerateObjectPaths(path, root, recursive, fileObject => fileObject.IsDirectory, infos);
            }

            foreach (var info in infos)
            {
                fileHandler(info);
            }
        }

        /// <inheritdoc />
        public int GetHardLinkCount(AbsolutePath path)
        {
            lock (_drives)
            {
                FileObject file = FindFileObject(path);
                if (file == null)
                {
                    throw new FileNotFoundException();
                }

                return file.NumberOfLinks;
            }
        }

        /// <inheritdoc />
        public ulong GetFileId(AbsolutePath path)
        {
            lock (_drives)
            {
                FileObject file = FindFileObject(path);
                if (file == null)
                {
                    throw new FileNotFoundException();
                }

                return (ulong)((long)file.GetHashCode() + int.MaxValue);
            }
        }

        /// <inheritdoc />
        public long GetFileSize(AbsolutePath path)
        {
            lock (_drives)
            {
                FileObject file = FindFileObject(path);
                if (file == null)
                {
                    throw new FileNotFoundException();
                }

                if (file.IsDirectory)
                {
                    throw new IOException();
                }

                return file.Content.Length;
            }
        }

        /// <inheritdoc />
        public DateTime GetLastAccessTimeUtc(AbsolutePath path)
        {
            lock (_drives)
            {
                FileObject file = FindFileObject(path);
                if (file == null)
                {
                    throw new FileNotFoundException();
                }

                if (file.IsDirectory)
                {
                    throw new IOException();
                }

                return file.LastAccessTimeUtc;
            }
        }

        /// <inheritdoc />
        public void SetLastAccessTimeUtc(AbsolutePath path, DateTime lastAccessTimeUtc)
        {
            lock (_drives)
            {
                FileObject file = FindFileObject(path);
                if (file == null)
                {
                    throw new FileNotFoundException();
                }

                if (file.IsDirectory)
                {
                    throw new IOException();
                }

                file.LastAccessTimeUtc = lastAccessTimeUtc;
            }
        }

        /// <inheritdoc />
        /// <remarks>Ref count limit is not implemented for the In Memory file system.</remarks>
        public CreateHardLinkResult CreateHardLink(AbsolutePath sourceFileName, AbsolutePath destinationFileName, bool replaceExisting)
        {
            lock (_drives)
            {
                if (!_useHardLinks)
                {
                    return CreateHardLinkResult.FailedNotSupported;
                }

                FileObject fileObj = FindFileObject(sourceFileName);

                if (fileObj == null)
                {
                    return CreateHardLinkResult.FailedSourceDoesNotExist;
                }

                if (destinationFileName.Length >= FileSystemConstants.MaxPath)
                {
                    // Please note, we use FileSystemConstants.MaxPath that returns 
                    // ShortMaxPath or LongMaxPath depending on whether the system supports long paths or not.
                    return CreateHardLinkResult.FailedPathTooLong;
                }

                FileObject destination = FindFileObjectAndParent(destinationFileName, out var parentDestination);

                if (parentDestination == null)
                {
                    throw new IOException(
                        string.Format(CultureInfo.InvariantCulture, "Parent of destination {0} does not exist", destinationFileName.Path));
                }

                // ReSharper disable PossibleNullReferenceException
                char sourceDrive = sourceFileName.DriveLetter;
                char destinationDrive = destinationFileName.DriveLetter;

                // ReSharper restore PossibleNullReferenceException
                if (sourceDrive != destinationDrive)
                {
                    return CreateHardLinkResult.FailedSourceAndDestinationOnDifferentVolumes;
                }

                if (!fileObj.CanAddLink())
                {
                    return CreateHardLinkResult.FailedMaxHardLinkLimitReached;
                }

                if (destination != null)
                {
                    if (!replaceExisting)
                    {
                        return CreateHardLinkResult.FailedDestinationExists;
                    }

                    try
                    {
                        destination.DeleteLink(destinationFileName, false, true);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        return CreateHardLinkResult.FailedAccessDenied;
                    }
                }

                fileObj.AddLink(destinationFileName);
                parentDestination.Children[destinationFileName.FileName] = fileObj;
            }

            return CreateHardLinkResult.Success;
        }

        /// <inheritdoc />
        public bool DirectoryExists(AbsolutePath path)
        {
            lock (_drives)
            {
                FileObject fileObj = FindFileObject(path);

                return fileObj != null && fileObj.IsDirectory;
            }
        }

        /// <inheritdoc />
        public bool FileExists(AbsolutePath path)
        {
            lock (_drives)
            {
                FileObject fileObj = FindFileObject(path);

                return fileObj != null && !fileObj.IsDirectory;
            }
        }

        /// <inheritdoc />
        public byte[] ReadAllBytes(AbsolutePath path)
        {
            using (FileObject.FileMemoryStream fileStream = OpenStreamInternal(path, FileAccess.Read, FileMode.Open, FileShare.Read))
            {
                if (fileStream == null)
                {
                    throw new FileNotFoundException($"Could not find file '{path}'");
                }

                return fileStream.ToArray();
            }
        }

        /// <inheritdoc />
        public void CreateDirectory(AbsolutePath path)
        {
            lock (_drives)
            {
                FileObject parent = FindParentDirectory(path);

                if (parent == null)
                {
                    CreateDirectory(path.Parent);
                    parent = FindParentDirectory(path);
                }

                // Handle the case with root of the volume with a trailing slash. e.g. CreateDirectory(@"C:\");
                if (path.FileName.Length == 0)
                {
                    return;
                }

                if (parent.Children.TryGetValue(path.FileName, out var existingFileObject))
                {
                    if (!existingFileObject.IsDirectory)
                    {
                        throw new IOException("Cannot create a directory where a file already exists.");
                    }

                    // Directory already exists.
                    return;
                }

                parent.Children[path.FileName] = new FileObject();
            }
        }

        /// <inheritdoc />
        public void DeleteDirectory(AbsolutePath path, DeleteOptions deleteOptions)
        {
            lock (_drives)
            {
                FileObject parent = FindParentDirectory(path);

                if (parent == null)
                {
                    throw new DirectoryNotFoundException();
                }

                if (!parent.Children.TryGetValue(path.FileName, out var directory))
                {
                    throw new DirectoryNotFoundException();
                }

                DeleteDirectory(parent, path.FileName, directory, deleteOptions, path);
            }
        }

        /// <inheritdoc />
        public void DeleteFile(AbsolutePath path)
        {
            lock (_drives)
            {
                FileObject parent = FindParentDirectory(path);

                if (parent == null)
                {
                    return;
                }

                if (!parent.Children.TryGetValue(path.FileName, out var file))
                {
                    return;
                }

                file.DeleteLink(path, true);

                _currentStatistics.FileDeletes++;

                parent.Children.Remove(path.FileName);
            }
        }

        /// <inheritdoc />
        public void WriteAllBytes(AbsolutePath path, byte[] content)
        {
            if (FileExists(path.Parent))
            {
                throw new DirectoryNotFoundException();
            }

            using (FileObject.FileMemoryStream fileStream =
                OpenStreamInternal(path, FileAccess.Write, FileMode.OpenOrCreate, FileShare.Read))
            {
                if (fileStream == null)
                {
                    throw new DirectoryNotFoundException($"Could not find directory '{path}'");
                }

                fileStream.SetLength(content.LongLength);
                using (var sourceStream = new MemoryStream(content))
                {
                    sourceStream.CopyTo(fileStream);
                }
            }
        }

        /// <inheritdoc />
        public void MoveFile(AbsolutePath sourceFilePath, AbsolutePath destinationFilePath, bool replaceExisting)
        {
            lock (_drives)
            {
                FileObject sourceParent = FindParentDirectory(sourceFilePath);

                if (sourceParent == null)
                {
                    throw new DirectoryNotFoundException();
                }

                if (!sourceParent.Children.TryGetValue(sourceFilePath.FileName, out var sourceFile))
                {
                    throw new FileNotFoundException();
                }

                FileObject destinationParent = FindParentDirectory(destinationFilePath);

                if (destinationParent == null)
                {
                    throw new DirectoryNotFoundException();
                }

                if (destinationParent.Children.TryGetValue(destinationFilePath.FileName, out var destinationFile))
                {
                    if (!replaceExisting)
                    {
                        throw new IOException("Destination already exists");
                    }

                    destinationFile.DeleteLink(destinationFilePath, true);
                }

                _currentStatistics.FileMoves++;

                sourceParent.Children.Remove(sourceFilePath.FileName);
                destinationParent.Children[destinationFilePath.FileName] = sourceFile;
                sourceFile.MoveLink(sourceFilePath, destinationFilePath);
            }
        }

        /// <inheritdoc />
        public void MoveDirectory(AbsolutePath sourcePath, AbsolutePath destinationPath)
        {
            lock (_drives)
            {
                FileObject sourceParent = FindParentDirectory(sourcePath);
                if (sourceParent == null)
                {
                    throw new DirectoryNotFoundException();
                }

                FileObject destinationParent = FindParentDirectory(destinationPath);
                if (destinationParent == null)
                {
                    throw new DirectoryNotFoundException();
                }

                if (destinationParent.Children.TryGetValue(destinationPath.FileName, out var destinationDirectory))
                {
                    throw new IOException("Destination already exists");
                }

                CreateDirectory(destinationPath);

                var fileInfos = EnumerateFiles(sourcePath, EnumerateOptions.Recurse);
                foreach (var fileInfo in fileInfos)
                {
                    var sourceFilePath = fileInfo.FullPath;
                    var destinationFilePath = sourceFilePath.SwapRoot(sourcePath, destinationPath);
                    CreateDirectory(destinationFilePath.Parent);
                    MoveFile(sourceFilePath, destinationFilePath, false);
                }

                DeleteDirectory(sourcePath, DeleteOptions.Recurse);
            }
        }

        /// <inheritdoc />
        public Task<Stream> OpenAsync(AbsolutePath path, FileAccess fileAccess, FileMode fileMode, FileShare share, FileOptions options, int bufferSize)
        {
            return Task.FromResult((Stream)OpenStreamInternal(path, fileAccess, fileMode, share));
        }

        /// <inheritdoc />
        public Task<Stream> OpenReadOnlyAsync(AbsolutePath path, FileShare share)
        {
            return this.OpenAsync(path, FileAccess.Read, FileMode.Open, share);
        }

        /// <inheritdoc />
        public void CopyFile(AbsolutePath sourcePath, AbsolutePath destinationPath, bool replaceExisting)
        {
            CopyFileAsync(sourcePath, destinationPath, replaceExisting).GetAwaiter().GetResult();
        }

        /// <inheritdoc />
        public async Task CopyFileAsync(AbsolutePath sourcePath, AbsolutePath destinationPath, bool replaceExisting)
        {
            using (var readStream = await this.OpenAsync(
                sourcePath, FileAccess.Read, FileMode.Open, FileShare.Read | FileShare.Delete))
            {
                if (readStream == null)
                {
                    throw new FileNotFoundException("missing source file", sourcePath.Path);
                }

                CreateDirectory(destinationPath.Parent);

                var mode = replaceExisting ? FileMode.OpenOrCreate : FileMode.CreateNew;
                using (var writeStream = await this.OpenAsync(
                    destinationPath, FileAccess.Write, mode, FileShare.Delete))
                {
                    await readStream.CopyToWithFullBufferAsync(
                        writeStream, FileSystemConstants.FileIOBufferSize);
                }
            }
        }

        /// <inheritdoc />
        public FileAttributes GetFileAttributes(AbsolutePath path)
        {
            lock (_drives)
            {
                FileObject fileObject = FindFileObject(path);

                if (fileObject == null)
                {
                    throw new FileNotFoundException();
                }

                return fileObject.Attributes;
            }
        }

        /// <inheritdoc />
        public void SetFileAttributes(AbsolutePath path, FileAttributes attributes)
        {
            lock (_drives)
            {
                FileObject fileObject = FindFileObject(path);

                if (fileObject == null)
                {
                    throw new FileNotFoundException();
                }

                if (fileObject.DenyAttributeWrites)
                {
                    throw new UnauthorizedAccessException("Cannot set attributes for file with deny-all write ACL");
                }

                fileObject.Attributes = attributes;
            }
        }

        public bool FileAttributesAreSubset(AbsolutePath path, FileAttributes attributes)
        {
            lock (_drives)
            {
                FileObject fileObject = FindFileObject(path);

                if (fileObject == null)
                {
                    throw new FileNotFoundException();
                }

                return attributes.HasFlag(fileObject.Attributes);
            }
        }

        /// <inheritdoc />
        public void DenyFileWrites(AbsolutePath path)
        {
            lock (_drives)
            {
                FileObject fileObject = FindFileObject(path);

                if (fileObject == null)
                {
                    throw new FileNotFoundException(
                        string.Format(CultureInfo.InvariantCulture, "Failed to set a Deny Writes ACL on {0}", path.Path),
                        path.Path);
                }

                fileObject.DenyAllWrites = true;
            }
        }

        /// <inheritdoc />
        public void AllowFileWrites(AbsolutePath path)
        {
            lock (_drives)
            {
                FileObject fileObject = FindFileObject(path);

                if (fileObject == null)
                {
                    throw new FileNotFoundException(
                        string.Format(CultureInfo.InvariantCulture, "Failed to set an Allow Writes ACL on {0}", path.Path), path.Path);
                }

                fileObject.DenyAllWrites = false;
            }
        }

        /// <inheritdoc />
        public void DenyAttributeWrites(AbsolutePath path)
        {
            lock (_drives)
            {
                FileObject fileObject = FindFileObject(path);

                if (fileObject == null)
                {
                    throw new FileNotFoundException(
                        string.Format(CultureInfo.InvariantCulture, "Failed to set a Deny Write Attributes ACL on {0}", path.Path),
                        path.Path);
                }

                fileObject.DenyAttributeWrites = true;
            }
        }

        /// <inheritdoc />
        public void AllowAttributeWrites(AbsolutePath path)
        {
            lock (_drives)
            {
                FileObject fileObject = FindFileObject(path);

                if (fileObject == null)
                {
                    throw new FileNotFoundException(
                        string.Format(CultureInfo.InvariantCulture, "Failed to set an Allow Write Attributes ACL on {0}", path.Path),
                        path.Path);
                }

                fileObject.DenyAttributeWrites = false;
            }
        }

        /// <inheritdoc />
        public AbsolutePath GetTempPath()
        {
            return _tempPath;
        }

        /// <inheritdoc />
        public void FlushVolume(char driveLetter)
        {
            // Nothing to do.
        }

        /// <inheritdoc />
        public virtual VolumeInfo GetVolumeInfo(AbsolutePath path)
        {
            var drive = _drives.First();
            var driveLetter = drive.Key;
            var rootPath = new AbsolutePath(PathGeneratorUtilities.GetAbsolutePath(driveLetter.ToString()));
            long size = EnumerateFiles(rootPath, EnumerateOptions.Recurse).Sum(fileInfo => fileInfo.Length);

            if (size > _volumeSize)
            {
                throw new NotSupportedException($"Unexpected total file size={size} > volume size={_volumeSize}");
            }

            var volumeFreeSpace = _volumeSize - size;
            return new VolumeInfo(_volumeSize, volumeFreeSpace);
        }

        private static IEnumerable<string> DirectoriesFromRoot(AbsolutePath path)
        {
            var parts = new List<string>();

            if (path == null)
            {
                return parts;
            }

            while (path != null && !path.IsRoot)
            {
                parts.Add(path.FileName);
                path = path.Parent;
            }

            parts.Reverse();

            return parts;
        }

        private void EnumerateObjectPaths(
            AbsolutePath rootPath, FileObject root, bool recursive, Func<FileObject, bool> filter, IList<FileInfo> infos)
        {
            foreach (KeyValuePair<string, FileObject> nameAndObject in root.Children)
            {
                if (filter(nameAndObject.Value))
                {
                    infos.Add(new FileInfo
                    {
                        FullPath = rootPath / nameAndObject.Key,
                        Length = nameAndObject.Value.Length
                    });
                }

                if (recursive && nameAndObject.Value.IsDirectory)
                {
                    EnumerateObjectPaths(rootPath / nameAndObject.Key, nameAndObject.Value, true, filter, infos);
                }
            }
        }

        private FileObject.FileMemoryStream OpenStreamInternal(AbsolutePath path, FileAccess accessMode, FileMode mode, FileShare share)
        {
            if (mode == FileMode.CreateNew && accessMode == FileAccess.Read)
            {
                throw new ArgumentException("FileMode.CreateNew and FileAccess.Read is an invalid combination.");
            }

            lock (_drives)
            {
                FileObject fileObj = FindFileObjectAndParent(path, out var parent);

                if (parent == null)
                {
                    return null;
                }

                switch (mode)
                {
                    case FileMode.Create:
                        if (fileObj != null)
                        {
                            fileObj.DeleteLink(path, false);
                            parent.Children.Remove(path.FileName);
                        }

                        fileObj = new FileObject(path, this, new byte[] {});
                        parent.Children[path.FileName] = fileObj;
                        break;

                    case FileMode.Open:
                        if (fileObj == null)
                        {
                            return null;
                        }

                        break;

                    case FileMode.OpenOrCreate:
                        if (fileObj == null)
                        {
                            fileObj = new FileObject(path, this, new byte[] {});
                            parent.Children[path.FileName] = fileObj;
                        }

                        break;

                    case FileMode.CreateNew:
                        if (fileObj != null)
                        {
                            unchecked
                            {
                                throw new IOException("File already exists", new IOException("File exists", (int)Hresult.FileExists));
                            }
                        }

                        fileObj = new FileObject(path, this, new byte[] {});
                        parent.Children[path.FileName] = fileObj;
                        break;

                    default:
                        throw new NotImplementedException($"Mode '{mode}' is not supported.");
                }

                var file = fileObj.Open(path, accessMode, share);
                if (accessMode.HasFlag(FileAccess.Write))
                {
                    _currentStatistics.FileOpensForWrite++;
                }
                else
                {
                    _currentStatistics.FileOpensForRead++;
                }

                return file;
            }
        }

        private FileObject FindParentDirectory(AbsolutePath path)
        {
            char driveLetter = path.DriveLetter;

            if (!_drives.TryGetValue(driveLetter, out var fileObj))
            {
                throw new DirectoryNotFoundException(
                    string.Format(CultureInfo.InvariantCulture, "Could not find drive '{0}' for path '{1}'.", driveLetter, path.Path));
            }

            foreach (string directory in DirectoriesFromRoot(path.Parent))
            {
                if (!fileObj.Children.TryGetValue(directory, out fileObj))
                {
                    return null;
                }

                if (!fileObj.IsDirectory)
                {
                    throw new DirectoryNotFoundException();
                }
            }

            return fileObj;
        }

        private FileObject FindFileObjectAndParent(AbsolutePath path, out FileObject parent)
        {
            if (path.IsRoot)
            {
                parent = null;
                return FindParentDirectory(path);
            }

            parent = FindParentDirectory(path);

            if (parent == null)
            {
                return null;
            }

            if (parent.Children.TryGetValue(path.FileName, out var fileObj))
            {
                return fileObj;
            }

            return null;
        }

        private FileObject FindFileObject(AbsolutePath path)
        {
            return FindFileObjectAndParent(path, out _);
        }

        private void DeleteDirectory(
            FileObject parentDirectory,
            string directoryName,
            FileObject directory,
            DeleteOptions deleteOptions,
            AbsolutePath directoryPath)
        {
            if (!directory.IsDirectory)
            {
                throw new IOException();
            }

            if ((deleteOptions & DeleteOptions.ReadOnly) != 0)
            {
                directory.Attributes &= ~FileAttributes.ReadOnly;
            }

            if ((deleteOptions & DeleteOptions.Recurse) != 0)
            {
                foreach (KeyValuePair<string, FileObject> file in
                    directory.Children.Where(kvp => !kvp.Value.IsDirectory).ToList())
                {
                    if ((deleteOptions & DeleteOptions.ReadOnly) != 0)
                    {
                        file.Value.Attributes &= ~FileAttributes.ReadOnly;
                    }

                    file.Value.DeleteLink(directoryPath / file.Key, true);

                    directory.Children.Remove(file.Key);
                }

                foreach (KeyValuePair<string, FileObject> childDirectory in directory.Children.ToList())
                {
                    DeleteDirectory(
                        directory, childDirectory.Key, childDirectory.Value, deleteOptions, directoryPath / childDirectory.Key);

                    directory.Children.Remove(childDirectory.Key);
                }
            }

            directory.CheckForDelete(null, true);

            parentDirectory.Children.Remove(directoryName);
        }

        private class CharCaseInsensitiveComparer : IEqualityComparer<char>
        {
            public static readonly CharCaseInsensitiveComparer Instance = new CharCaseInsensitiveComparer();

            private CharCaseInsensitiveComparer()
            {
            }

            public bool Equals(char char1, char char2)
            {
                return char.ToUpperInvariant(char1).Equals(char.ToUpperInvariant(char2));
            }

            public int GetHashCode(char obj)
            {
                return char.ToUpperInvariant(obj).GetHashCode();
            }
        }

        private class OpenLinkInfo
        {
            public readonly FileShare Share;
            public int Count;

            public OpenLinkInfo(FileShare share)
            {
                Share = share;
                Count = 0;
            }
        }

        private class FileObject
        {
            public readonly Dictionary<string, FileObject> Children;
            public byte[] Content;
            public bool DenyAllWrites;
            public bool DenyAttributeWrites;
            public DateTime LastAccessTimeUtc;

            private readonly HashSet<AbsolutePath> _linkedPaths = new HashSet<AbsolutePath>();

            [SuppressMessage(
                "Microsoft.Performance",
                "CA1823:AvoidUnusedPrivateFields",
                Justification = "Used in debug now and will be used in release soon")]
            private readonly
                ConcurrentDictionary<StackTrace, FileAccess> _openStacks = new ConcurrentDictionary<StackTrace, FileAccess>();

            private readonly MemoryFileSystem _fileSystem;
            private readonly Dictionary<AbsolutePath, OpenLinkInfo> _readersByPath = new Dictionary<AbsolutePath, OpenLinkInfo>();
            private readonly Dictionary<AbsolutePath, OpenLinkInfo> _writersByPath = new Dictionary<AbsolutePath, OpenLinkInfo>();
            private FileAttributes _attributes;
            private int _totalReaderCount;
            private int _totalWriterCount;

            public FileObject(
                AbsolutePath linkPath, MemoryFileSystem fileSystem, byte[] content, FileAttributes attributes = FileAttributes.Normal)
            {
                _fileSystem = fileSystem;
                Attributes = attributes;
                Content = content;
                LastAccessTimeUtc = _fileSystem.Clock.UtcNow;
                AddLink(linkPath);
            }

            public FileObject()
            {
                _attributes = FileAttributes.Directory;
                Children = new Dictionary<string, FileObject>(StringComparer.OrdinalIgnoreCase);
            }

            public long Length => Content?.Length ?? -1;

            public int NumberOfLinks => _linkedPaths.Count;

            public bool CanAddLink()
            {
                return _linkedPaths.Count < 1024;
            }

            public void AddLink(AbsolutePath linkPath)
            {
                _linkedPaths.Add(linkPath);
            }

            public FileAttributes Attributes
            {
                get { return _attributes; }

                set
                {
                    if ((value & FileSystemConstants.UnsupportedFileAttributes) != 0)
                    {
                        throw new NotImplementedException();
                    }

                    if (IsDirectory)
                    {
                        value &= FileAttributes.ReadOnly;
                        value |= FileAttributes.Directory;
                    }
                    else
                    {
                        value &= ~FileAttributes.Directory;
                    }

                    _attributes = value;
                }
            }

            public bool IsDirectory => Attributes.HasFlag(FileAttributes.Directory);

            public void CheckForDelete(AbsolutePath linkPath, bool checkForReadOnly, bool disallowOnAnyOpened = false)
            {
                lock (this)
                {
                    if (checkForReadOnly && Attributes.HasFlag(FileAttributes.ReadOnly))
                    {
                        throw new IOException("Cannot delete a read-only file or directory.");
                    }

                    if (IsDirectory)
                    {
                        if (Children.Any())
                        {
                            throw new IOException("Cannot delete a non-empty directory.");
                        }
                    }
                    else
                    {
                        bool disallowDelete = false;

                        foreach (var kvp in _readersByPath.Where(x => !x.Key.Equals(linkPath)))
                        {
                            if (!kvp.Value.Share.HasFlag(FileShare.Delete))
                            {
                                disallowDelete = true;
                            }
                        }

                        foreach (var kvp in _writersByPath.Where(x => !x.Key.Equals(linkPath)))
                        {
                            if (!kvp.Value.Share.HasFlag(FileShare.Delete))
                            {
                                disallowDelete = true;
                            }
                        }

                        if (_readersByPath.ContainsKey(linkPath))
                        {
                            if (!_readersByPath[linkPath].Share.HasFlag(FileShare.Delete) || disallowOnAnyOpened)
                            {
                                disallowDelete = true;
                            }
                        }

                        if (_writersByPath.ContainsKey(linkPath))
                        {
                            if (!_writersByPath[linkPath].Share.HasFlag(FileShare.Delete) || disallowOnAnyOpened)
                            {
                                disallowDelete = true;
                            }
                        }

                        if (disallowDelete)
                        {
                            throw new UnauthorizedAccessException(
                                "Cannot delete/overwrite a file when this file is in use. \n\n" +
                                "Links: \n" + string.Join("\n", _linkedPaths.Select(path => path.Path)) +
                                "\n\nOpen Stacks: \n\n" + string.Join("\n\n-----------------\n\n", _openStacks));
                        }
                    }
                }
            }

            public void MoveLink(AbsolutePath oldPath, AbsolutePath newPath)
            {
                lock (this)
                {
                    var removed = _linkedPaths.Remove(oldPath);
                    Contract.Assert(removed);
                    _linkedPaths.Add(newPath);
                }
            }

            public void DeleteLink(AbsolutePath linkPath, bool checkForReadOnly, bool disallowOnAnyOpened = false)
            {
                lock (this)
                {
                    CheckForDelete(linkPath, checkForReadOnly, disallowOnAnyOpened);
                    var linkRemoved = _linkedPaths.Remove(linkPath);
                    Contract.Assert(linkRemoved);
                }
            }

            public FileMemoryStream Open(AbsolutePath openPath, FileAccess accessMode, FileShare share)
            {
                lock (this)
                {
                    if (IsDirectory)
                    {
                        throw new UnauthorizedAccessException();
                    }

                    if (accessMode == FileAccess.Read)
                    {
                        if (_totalWriterCount > 0)
                        {
                            throw new UnauthorizedAccessException("File already opened for write.");
                        }

                        _totalReaderCount++;
                        if (!_readersByPath.ContainsKey(openPath))
                        {
                            _readersByPath[openPath] = new OpenLinkInfo(share);
                        }

                        _readersByPath[openPath].Count++;
                    }
                    else
                    {
                        if (Attributes.HasFlag(FileAttributes.ReadOnly))
                        {
                            throw new UnauthorizedAccessException("Cannot open a read-only file for write.");
                        }

                        if (DenyAllWrites)
                        {
                            throw new UnauthorizedAccessException("Cannot open file with deny-all write ACL");
                        }

                        if (_totalReaderCount > 0)
                        {
                            throw new UnauthorizedAccessException("File already opened for read.");
                        }

                        if (_totalWriterCount > 0)
                        {
                            throw new UnauthorizedAccessException("File already opened for write.");
                        }

                        _totalWriterCount++;
                        if (!_writersByPath.ContainsKey(openPath))
                        {
                            _writersByPath[openPath] = new OpenLinkInfo(share);
                        }

                        _writersByPath[openPath].Count++;
                    }

                    return new FileMemoryStream(openPath, this, accessMode);
                }
            }

            internal sealed class FileMemoryStream : MemoryStream
            {
                private readonly FileAccess _accessMode;
                private readonly FileObject _fileObject;
                private readonly StackTrace _openStack;
                private readonly AbsolutePath _openedPath;
                private bool _disposed;

                public FileMemoryStream(AbsolutePath openedPath, FileObject fileObject, FileAccess accessMode)
                {
                    _openedPath = openedPath;
                    _fileObject = fileObject;
                    _accessMode = FileAccess.Write;
                    _openStack = new StackTrace(true);
                    _fileObject._openStacks.TryAdd(_openStack, accessMode);

                    using (var contentBytes = new MemoryStream(fileObject.Content))
                    {
                        contentBytes.CopyTo(this);
                    }

                    _accessMode = accessMode;

                    Position = 0;
                }

                public override bool CanWrite => base.CanWrite && (_accessMode != FileAccess.Read);

                public override void Write(byte[] buffer, int offset, int count)
                {
                    if (_accessMode == FileAccess.Read)
                    {
                        throw new NotSupportedException("Cannot write to a file opened for read.");
                    }

                    _fileObject._fileSystem._currentStatistics.FileDataWriteOperations++;
                    base.Write(buffer, offset, count);
                }

                public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
                {
                    if (_accessMode == FileAccess.Read)
                    {
                        throw new NotSupportedException("Cannot write to a file opened for read.");
                    }

                    _fileObject._fileSystem._currentStatistics.FileDataWriteOperations++;
                    return base.WriteAsync(buffer, offset, count, cancellationToken);
                }

                public override void WriteByte(byte value)
                {
                    if (_accessMode == FileAccess.Read)
                    {
                        throw new NotSupportedException("Cannot write to a file opened for read.");
                    }

                    _fileObject._fileSystem._currentStatistics.FileDataWriteOperations++;
                    base.WriteByte(value);
                }

                public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
                {
                    if (_accessMode == FileAccess.Read)
                    {
                        throw new NotSupportedException("Cannot write to a file opened for read.");
                    }

                    _fileObject._fileSystem._currentStatistics.FileDataWriteOperations++;
                    return base.BeginWrite(buffer, offset, count, callback, state);
                }

                public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
                {
                    _fileObject._fileSystem._currentStatistics.FileDataReadOperations++;
                    return base.BeginRead(buffer, offset, count, callback, state);
                }

                public override int Read(byte[] buffer, int offset, int count)
                {
                    _fileObject._fileSystem._currentStatistics.FileDataReadOperations++;
                    return base.Read(buffer, offset, count);
                }

                public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
                {
                    _fileObject._fileSystem._currentStatistics.FileDataReadOperations++;
                    return base.ReadAsync(buffer, offset, count, cancellationToken);
                }

                public override int ReadByte()
                {
                    _fileObject._fileSystem._currentStatistics.FileDataReadOperations++;
                    return base.ReadByte();
                }

                protected override void Dispose(bool disposing)
                {
                    if (disposing && !_disposed)
                    {
                        lock (_fileObject)
                        {
                            if (_accessMode == FileAccess.Read)
                            {
                                _fileObject._totalReaderCount--;
                                _fileObject._readersByPath[_openedPath].Count--;
                                if (_fileObject._readersByPath[_openedPath].Count == 0)
                                {
                                    _fileObject._readersByPath.Remove(_openedPath);
                                }
                            }
                            else
                            {
                                _fileObject.Content = ToArray();
                                _fileObject._totalWriterCount--;
                                _fileObject._writersByPath[_openedPath].Count--;
                                if (_fileObject._writersByPath[_openedPath].Count == 0)
                                {
                                    _fileObject._writersByPath.Remove(_openedPath);
                                }
                            }

                            _fileObject._openStacks.TryRemove(_openStack, out var outAccess);
                        }

                        _disposed = true;
                    }

                    base.Dispose(disposing);
                }
            }
        }
    }
}
