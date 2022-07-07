// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.InterfacesTest.Utils;
using FluentAssertions;
using Xunit;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;

namespace ContentStoreTest.Stores
{
    public class DiskFreePercentRuleTests : QuotaRuleTests
    {
        private const long VolumeSize = 100;
        private const long Hard = 10;
        private const long Soft = 20;

        protected override long SizeWithinTargetQuota => Soft + 10;

        protected override long SizeBeyondTargetQuota => Soft - 10;

        [Theory]
        [InlineData(9, 0, false)]
        [InlineData(9, 20, false)]
        [InlineData(10, 0, true)]
        [InlineData(10, 1, false)]
        [InlineData(10, 20, false)]
        [InlineData(11, 1, true)]
        [InlineData(11, 2, false)]
        [InlineData(11, 20, false)]
        public void IsInsideHardLimitResult(long volumeFreeSpace, long reserveSize, bool result)
        {
            var rule = CreateRule(volumeFreeSpace);
            rule.IsInsideHardLimit(reserveSize).Succeeded.Should().Be(result);
        }

        [Theory]
        [InlineData(19, 0, false)]
        [InlineData(19, 20, false)]
        [InlineData(20, 0, true)]
        [InlineData(20, 1, false)]
        [InlineData(20, 20, false)]
        [InlineData(21, 1, true)]
        [InlineData(21, 2, false)]
        [InlineData(21, 20, false)]
        public void IsInsideSoftLimitResult(long volumeFreeSpace, long reserveSize, bool result)
        {
            var rule = CreateRule(volumeFreeSpace);
            rule.IsInsideSoftLimit(reserveSize).Succeeded.Should().Be(result);
        }

        [Theory]
        [InlineData(20, 0, false)]
        [InlineData(20, 20, false)]
        [InlineData(21, 0, true)]
        [InlineData(21, 1, false)]
        [InlineData(21, 20, false)]
        [InlineData(22, 1, true)]
        [InlineData(22, 2, false)]
        [InlineData(22, 20, false)]
        public void IsInsideTargetLimitResult(long volumeFreeSpace, long reserveSize, bool result)
        {
            var rule = CreateRule(volumeFreeSpace);
            rule.IsInsideTargetLimit(reserveSize).Succeeded.Should().Be(result);
        }

        protected override IQuotaRule CreateRule(long currentSize, EvictResult evictResult = null)
        {
            var dummyPath = PathGeneratorUtilities.GetAbsolutePath("C", "dummy");
            var mock = new TestAbsFileSystem(currentSize);

            var quota = new DiskFreePercentQuota(Hard, Soft);
            return new DiskFreePercentRule(
                quota,
                mock,
                new AbsolutePath(dummyPath));
        }

        private class TestAbsFileSystem : IAbsFileSystem
        {
            private readonly long _currentSize;

            public TestAbsFileSystem(long currentSize)
            {
                _currentSize = currentSize;
            }

            public void AllowAttributeWrites(AbsolutePath path)
            {
                throw new NotImplementedException();
            }

            public void AllowFileWrites(AbsolutePath path)
            {
                throw new NotImplementedException();
            }

            public void CopyFile(AbsolutePath sourcePath, AbsolutePath destinationPath, bool replaceExisting)
            {
                throw new NotImplementedException();
            }

            public Task CopyFileAsync(AbsolutePath sourcePath, AbsolutePath destinationPath, bool replaceExisting)
            {
                throw new NotImplementedException();
            }

            public void CreateDirectory(AbsolutePath path)
            {
                throw new NotImplementedException();
            }

            public CreateHardLinkResult CreateHardLink(AbsolutePath sourceFileName, AbsolutePath destinationFileName, bool replaceExisting)
            {
                throw new NotImplementedException();
            }

            public void DeleteDirectory(AbsolutePath path, DeleteOptions deleteOptions)
            {
                throw new NotImplementedException();
            }

            public void DeleteFile(AbsolutePath path)
            {
                throw new NotImplementedException();
            }

            public void DenyAttributeWrites(AbsolutePath path)
            {
                throw new NotImplementedException();
            }

            public void DenyFileWrites(AbsolutePath path, bool disableInheritance)
            {
                throw new NotImplementedException();
            }

            public bool DirectoryExists(AbsolutePath path)
            {
                throw new NotImplementedException();
            }

            public void Dispose()
            {
                throw new NotImplementedException();
            }

            public IEnumerable<AbsolutePath> EnumerateDirectories(AbsolutePath path, EnumerateOptions options)
            {
                throw new NotImplementedException();
            }

            public void EnumerateFiles(AbsolutePath path, string pattern, bool recursive, Action<BuildXL.Cache.ContentStore.Interfaces.FileSystem.FileInfo> fileHandler)
            {
                throw new NotImplementedException();
            }

            public IEnumerable<BuildXL.Cache.ContentStore.Interfaces.FileSystem.FileInfo> EnumerateFiles(AbsolutePath path, EnumerateOptions options)
            {
                throw new NotImplementedException();
            }

            public bool FileAttributesAreSubset(AbsolutePath path, FileAttributes attributes)
            {
                throw new NotImplementedException();
            }

            public bool FileExists(AbsolutePath path)
            {
                throw new NotImplementedException();
            }

            public void FlushVolume(char driveLetter)
            {
                throw new NotImplementedException();
            }

            public DateTime GetDirectoryCreationTimeUtc(AbsolutePath path)
            {
                throw new NotImplementedException();
            }

            public FileAttributes GetFileAttributes(AbsolutePath path)
            {
                throw new NotImplementedException();
            }

            public ulong GetFileId(AbsolutePath path)
            {
                throw new NotImplementedException();
            }

            public long GetFileSize(AbsolutePath path)
            {
                throw new NotImplementedException();
            }

            public int GetHardLinkCount(AbsolutePath path)
            {
                throw new NotImplementedException();
            }

            public DateTime GetLastAccessTimeUtc(AbsolutePath path)
            {
                throw new NotImplementedException();
            }

            public AbsolutePath GetTempPath()
            {
                throw new NotImplementedException();
            }

            public VolumeInfo GetVolumeInfo(AbsolutePath path)
            {
                return new VolumeInfo(VolumeSize, _currentSize);
            }

            public void MoveDirectory(AbsolutePath sourcePath, AbsolutePath destinationPath)
            {
                throw new NotImplementedException();
            }

            public void MoveFile(AbsolutePath sourceFilePath, AbsolutePath destinationFilePath, bool replaceExisting)
            {
                throw new NotImplementedException();
            }

            public Task<StreamWithLength?> OpenAsync(AbsolutePath path, FileAccess fileAccess, FileMode fileMode, FileShare share, FileOptions options, int bufferSize)
            {
                throw new NotImplementedException();
            }

            public StreamWithLength? TryOpen(AbsolutePath path, FileAccess fileAccess, FileMode fileMode, FileShare share, FileOptions options, int bufferSize)
            {
                throw new NotImplementedException();
            }

            public Task<StreamWithLength?> OpenReadOnlyAsync(AbsolutePath path, FileShare share)
            {
                throw new NotImplementedException();
            }

            public byte[] ReadAllBytes(AbsolutePath path)
            {
                throw new NotImplementedException();
            }

            public void SetFileAttributes(AbsolutePath path, FileAttributes attributes)
            {
                throw new NotImplementedException();
            }

            public void SetLastAccessTimeUtc(AbsolutePath path, DateTime lastAccessTimeUtc)
            {
                throw new NotImplementedException();
            }

            public void WriteAllBytes(AbsolutePath path, byte[] content)
            {
                throw new NotImplementedException();
            }

            public void DisableAuditRuleInheritance(AbsolutePath path)
            {
                throw new NotImplementedException();
            }

            public bool IsAclInheritanceDisabled(AbsolutePath path)
            {
                throw new NotImplementedException();
            }
        }
    }
}
