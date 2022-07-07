// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;

namespace BuildXL.Cache.ContentStore.InterfacesTest.FileSystem
{
    /// <summary>
    /// File system that allows us to redirect certain paths. This is useful when trying to mock several drives while only using one drive.
    /// In practice, we might only have a C:\ drive but we can trick our stores to use paths on a fake drive X:\, by making the file system redirect
    /// paths in the fake drive to some subfolder in C:\
    /// </summary>
    public class RedirectionFileSystem : IAbsFileSystem
    {
        private IAbsFileSystem Inner { get; }
        private AbsolutePath SourceRoot { get; }
        private AbsolutePath TargetRoot { get; }

        /// <nodoc />
        public RedirectionFileSystem(IAbsFileSystem inner, AbsolutePath sourceRoot, AbsolutePath targetRoot)
        {
            Inner = inner;
            SourceRoot = sourceRoot;
            TargetRoot = targetRoot;
        }

        /// <nodoc />
        public void AllowAttributeWrites(AbsolutePath path)
        {
            Inner.AllowAttributeWrites(Redirect(path));
        }

        /// <nodoc />
        public void AllowFileWrites(AbsolutePath path)
        {
            Inner.AllowFileWrites(Redirect(path));
        }

        /// <nodoc />
        public void CopyFile(AbsolutePath sourcePath, AbsolutePath destinationPath, bool replaceExisting)
        {
            Inner.CopyFile(Redirect(sourcePath), Redirect(destinationPath), replaceExisting);
        }

        /// <nodoc />
        public Task CopyFileAsync(AbsolutePath sourcePath, AbsolutePath destinationPath, bool replaceExisting)
        {
            return Inner.CopyFileAsync(Redirect(sourcePath), Redirect(destinationPath), replaceExisting);
        }

        /// <nodoc />
        public void CreateDirectory(AbsolutePath path)
        {
            Inner.CreateDirectory(Redirect(path));
        }

        /// <nodoc />
        public CreateHardLinkResult CreateHardLink(AbsolutePath sourceFileName, AbsolutePath destinationFileName, bool replaceExisting)
        {
            return Inner.CreateHardLink(Redirect(sourceFileName), Redirect(destinationFileName), replaceExisting);
        }

        /// <nodoc />
        public void DeleteDirectory(AbsolutePath path, DeleteOptions deleteOptions)
        {
            Inner.DeleteDirectory(Redirect(path), deleteOptions);
        }

        /// <nodoc />
        public void DeleteFile(AbsolutePath path)
        {
            Inner.DeleteFile(Redirect(path));
        }

        /// <nodoc />
        public void DenyAttributeWrites(AbsolutePath path)
        {
            Inner.DenyAttributeWrites(Redirect(path));
        }

        /// <nodoc />
        public void DenyFileWrites(AbsolutePath path, bool disableInheritance)
        {
            Inner.DenyFileWrites(Redirect(path), disableInheritance);
        }

        /// <nodoc />
        public bool DirectoryExists(AbsolutePath path)
        {
            return Inner.DirectoryExists(Redirect(path));
        }

        /// <nodoc />
        public void Dispose()
        {
            Inner.Dispose();
        }

        /// <nodoc />
        public IEnumerable<AbsolutePath> EnumerateDirectories(AbsolutePath path, EnumerateOptions options)
        {
            return Inner.EnumerateDirectories(Redirect(path), options);
        }

        /// <nodoc />
        public void EnumerateFiles(AbsolutePath path, string pattern, bool recursive, Action<Interfaces.FileSystem.FileInfo> fileHandler)
        {
            Inner.EnumerateFiles(Redirect(path), pattern, recursive, fileHandler);
        }

        /// <nodoc />
        public IEnumerable<Interfaces.FileSystem.FileInfo> EnumerateFiles(AbsolutePath path, EnumerateOptions options)
        {
            return Inner.EnumerateFiles(Redirect(path), options);
        }

        /// <nodoc />
        public bool FileAttributesAreSubset(AbsolutePath path, FileAttributes attributes)
        {
            return Inner.FileAttributesAreSubset(Redirect(path), attributes);
        }

        /// <nodoc />
        public bool FileExists(AbsolutePath path)
        {
            return Inner.FileExists(Redirect(path));
        }

        /// <nodoc />
        public void FlushVolume(char driveLetter)
        {
            Inner.FlushVolume(driveLetter);
        }

        /// <nodoc />
        public DateTime GetDirectoryCreationTimeUtc(AbsolutePath path)
        {
            return Inner.GetDirectoryCreationTimeUtc(Redirect(path));
        }

        /// <nodoc />
        public FileAttributes GetFileAttributes(AbsolutePath path)
        {
            return Inner.GetFileAttributes(Redirect(path));
        }

        /// <nodoc />
        public ulong GetFileId(AbsolutePath path)
        {
            return Inner.GetFileId(Redirect(path));
        }

        /// <nodoc />
        public long GetFileSize(AbsolutePath path)
        {
            return Inner.GetFileSize(Redirect(path));
        }

        /// <nodoc />
        public int GetHardLinkCount(AbsolutePath path)
        {
            return Inner.GetHardLinkCount(Redirect(path));
        }

        /// <nodoc />
        public DateTime GetLastAccessTimeUtc(AbsolutePath path)
        {
            return Inner.GetLastAccessTimeUtc(Redirect(path));
        }

        /// <nodoc />
        public AbsolutePath GetTempPath()
        {
            return Inner.GetTempPath();
        }

        /// <nodoc />
        public VolumeInfo GetVolumeInfo(AbsolutePath path)
        {
            return Inner.GetVolumeInfo(Redirect(path));
        }

        /// <nodoc />
        public void MoveDirectory(AbsolutePath sourcePath, AbsolutePath destinationPath)
        {
            Inner.MoveDirectory(Redirect(sourcePath), Redirect(destinationPath));
        }

        /// <nodoc />
        public void MoveFile(AbsolutePath sourceFilePath, AbsolutePath destinationFilePath, bool replaceExisting)
        {
            Inner.MoveFile(Redirect(sourceFilePath), Redirect(destinationFilePath), replaceExisting);
        }

        /// <nodoc />
        public Task<StreamWithLength?> OpenAsync(AbsolutePath path, FileAccess fileAccess, FileMode fileMode, FileShare share, FileOptions options, int bufferSize)
        {
            return Inner.OpenAsync(Redirect(path), fileAccess, fileMode, share, options, bufferSize);
        }

        /// <inheritdoc />
        public StreamWithLength? TryOpen(AbsolutePath path, FileAccess fileAccess, FileMode fileMode, FileShare share, FileOptions options, int bufferSize)
        {
            return Inner.TryOpen(Redirect(path), fileAccess, fileMode, share, options, bufferSize);
        }

        /// <nodoc />
        public Task<StreamWithLength?> OpenReadOnlyAsync(AbsolutePath path, FileShare share)
        {
            return Inner.OpenReadOnlyAsync(Redirect(path), share);
        }

        /// <nodoc />
        public byte[] ReadAllBytes(AbsolutePath path)
        {
            return Inner.ReadAllBytes(Redirect(path));
        }

        /// <nodoc />
        public void SetFileAttributes(AbsolutePath path, FileAttributes attributes)
        {
            Inner.SetFileAttributes(Redirect(path), attributes);
        }

        /// <nodoc />
        public void SetLastAccessTimeUtc(AbsolutePath path, DateTime lastAccessTimeUtc)
        {
            Inner.SetLastAccessTimeUtc(Redirect(path), lastAccessTimeUtc);
        }

        /// <nodoc />
        public void WriteAllBytes(AbsolutePath path, byte[] content)
        {
            Inner.WriteAllBytes(Redirect(path), content);
        }

        /// <nodoc />
        private AbsolutePath Redirect(AbsolutePath path)
        {
            return path.SwapRoot(SourceRoot, TargetRoot);
        }

        /// <nodoc />
        public void DisableAuditRuleInheritance(AbsolutePath path)
        {
            Inner.DisableAuditRuleInheritance(Redirect(path));
        }

        /// <nodoc />
        public bool IsAclInheritanceDisabled(AbsolutePath path)
        {
            return Inner.IsAclInheritanceDisabled(Redirect(path));
        }
    }
}
