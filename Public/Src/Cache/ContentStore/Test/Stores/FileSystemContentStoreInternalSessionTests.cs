// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.ContentStore.InterfacesTest.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.Stores
{
    public sealed class FileSystemContentStoreInternalSessionTests : FileSystemContentStoreInternalTestBase
    {
        private static readonly ContentHash _emptyFileVsoHash = ComputeEmptyVsoHash();
        private readonly MemoryClock _clock;

        public FileSystemContentStoreInternalSessionTests()
            : base(() => new MemoryFileSystem(new MemoryClock()), TestGlobal.Logger)
        {
            _clock = (MemoryClock)((MemoryFileSystem)FileSystem).Clock;
        }

        [Fact]
        public async Task ConstructorSucceedsWithLargestPossibleCacheRootLength()
        {
            var context = new Context(Logger);
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                int extraLength = FileSystemConstants.LongPathsSupported ? 100 : 0;
                int maxAllowedCacheRootLength = FileSystemConstants.MaxShortPath - 1 -
                                                FileSystemContentStoreInternal.GetMaxContentPathLengthRelativeToCacheRoot() -
                                                testDirectory.Path.Length - 1 +
                                                extraLength;
                var cacheRoot = new AbsolutePath(testDirectory.Path + new string('a', maxAllowedCacheRootLength));

                using (var store = Create(cacheRoot, _clock))
                {
                    await store.StartupAsync(context).ShouldBeSuccess();
                    await store.ShutdownAsync(context).ShouldBeSuccess();
                }
            }
        }

        [Fact]
        public async Task ContentDirectoryCreatedWhenMissing()
        {
            var context = new Context(Logger);
            var data1 = ThreadSafeRandom.GetBytes(MaxSizeHard / 3);
            using (var dataStream1 = new MemoryStream(data1))
            {
                using (var testDirectory = new DisposableDirectory(FileSystem))
                {
                    var hash1 = new ContentHash(ContentHashType);
                    await TestStore(context, _clock, testDirectory, async store =>
                    {
                        store.DeleteContentDirectoryAfterDispose = true;

                        hash1 = (await store.PutStreamAsync(context, dataStream1, ContentHashType, null))
                            .ContentHash;
                    });

                    await TestStore(context, _clock, testDirectory, async store =>
                    {
                        var r = await store.GetStatsAsync(context);
                        r.CounterSet.GetIntegralWithNameLike("ReconstructCall").Should().Be(1);
                        Assert.True(await store.ContainsAsync(context, hash1, null));
                    });
                }
            }
        }

        [Fact]
        public Task ContainsRecoversFromContentDirectoryEntryForNonexistentBlob()
        {
            var context = new Context(Logger);
            return TestStore(context, _clock, async store =>
            {
                (await store.ContentDirectoryForTest.GetCountAsync()).Should().Be(0);

                // Add a content directory entry for which there is no blob on disk.
                var nonexistentHash = ContentHash.Random();
                await store.ContentDirectoryForTest.UpdateAsync(nonexistentHash, true, _clock, fileInfo =>
                    Task.FromResult(new ContentFileInfo(_clock, 1, 100)));
                (await store.ContentDirectoryForTest.GetCountAsync()).Should().Be(1);

                // Ensure that the cache does not report containing the content and that the entry has been removed
                (await store.ContainsAsync(context, nonexistentHash, null)).Should().BeFalse();
                (await store.ContentDirectoryForTest.GetCountAsync()).Should().Be(0);
            });
        }

        [Fact]
        public Task StartupWithCorruptedVersionFile()
        {
            return StartupWithCorruptedStoreFile(store => store.VersionFilePathForTest);
        }

        [Fact]
        public async Task UpgradeFromVersion0()
        {
            var context = new Context(Logger);
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                using (var store = Create(testDirectory.Path, _clock))
                {
                    await store.StartupAsync(context).ShouldBeSuccess();
                    await store.ShutdownAsync(context).ShouldBeSuccess();
                    store.SerializedDataVersionForTest.WriteValueFile(0);
                }

                var oldPath = GetPathForEmptyFile(testDirectory.Path, 0);
                var newPath = GetPathForEmptyFile(testDirectory.Path, TestFileSystemContentStoreInternal.CurrentVersionNumber);

                FileSystem.CreateDirectory(oldPath.Parent);
                FileSystem.WriteAllBytes(oldPath, new byte[0]);
                FileSystem.SetFileAttributes(oldPath, FileAttributes.ReadOnly);

                await TestStore(context, _clock, testDirectory, store =>
                {
                    Assert.False(FileSystem.GetFileAttributes(newPath).HasFlag(FileAttributes.ReadOnly));
                    Assert.Equal(
                        TestFileSystemContentStoreInternal.CurrentVersionNumber,
                        store.SerializedDataVersionForTest.ReadValueFile());
                    return Task.FromResult(true);
                });
            }
        }

        [Fact]
        public Task UpgradeFromVersion1()
        {
            return UpgradeFromVersion1Helper(false);
        }

        [Fact]
        public Task UpgradeFromVersion1ContentExists()
        {
            return UpgradeFromVersion1Helper(true);
        }

        [Fact]
        public Task UpgradeFromVersion2()
        {
            return UpgradeFromVersion2Helper(false);
        }

        [Fact]
        public Task UpgradeFromVersion2ContentAlreadyProtected()
        {
            return UpgradeFromVersion2Helper(true);
        }

        [Fact]
        public Task UpgradeLegacyVsoHashedContentWithMove()
        {
            return UpgradeLegacyVsoHashedContent(holdHandleToFile: false);
        }

        [Fact]
        public Task UpgradeLegacyVsoHashedContentWithCopy()
        {
            return UpgradeLegacyVsoHashedContent(holdHandleToFile: true);
        }

        private async Task UpgradeLegacyVsoHashedContent(bool holdHandleToFile)
        {
            var context = new Context(Logger);
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                // Create an empty store.
                using (var store = Create(testDirectory.Path, _clock))
                {
                    await store.StartupAsync(context).ShouldBeSuccess();
                    await store.ShutdownAsync(context).ShouldBeSuccess();
                }

                // Write a file into the cache corresponding to the generated content directory entry.
                string emptyFileVsoHashHex = _emptyFileVsoHash.ToHex();
                var oldRoot = testDirectory.Path /
                              "Shared" /
                              ((int)HashType.DeprecatedVso0).ToString();

                var oldPath = oldRoot /
                              emptyFileVsoHashHex.Substring(0, 3) /
                              (emptyFileVsoHashHex + ".blob");

                var newPath = testDirectory.Path /
                              "Shared" /
                              ContentHashType.Serialize() /
                              emptyFileVsoHashHex.Substring(0, 3) /
                              (emptyFileVsoHashHex + ".blob");

                FileSystem.CreateDirectory(oldPath.Parent);
                FileSystem.WriteAllBytes(oldPath, new byte[0]);

                // Load the store, checking that its content has been upgraded.
                // In this first pass, we might fail a file move.
                await TestStore(
                    context,
                    _clock,
                    testDirectory,
                    async store =>
                    {
                        Assert.Equal(holdHandleToFile, FileSystem.FileExists(oldPath));
                        Assert.True(FileSystem.FileExists(newPath));

                        var contentHash = new ContentHash(ContentHashType, HexUtilities.HexToBytes(emptyFileVsoHashHex));
                        var contains = await store.ContainsAsync(context, contentHash, null);
                        Assert.True(contains);
                    },
                    preStartupAction: store =>
                    {
                        if (holdHandleToFile)
                        {
                            store.ThrowOnUpgradeLegacyVsoHashedContentDirectoryRename = oldRoot;
                            store.ThrowOnUpgradeLegacyVsoHashedContentDirectoryDelete = oldRoot;
                            store.ThrowOnUpgradeLegacyVsoHashedContentFileRename = oldPath;
                        }
                    });

                Assert.Equal(holdHandleToFile, FileSystem.DirectoryExists(oldRoot));

                // Load the store, checking that its content has been upgraded.
                // In this second pass, we might do not fail any file move.
                await TestStore(context, _clock, testDirectory, async store =>
                {
                    // Make sure the cleanup completed.
                    Assert.False(FileSystem.FileExists(oldPath));
                    Assert.True(FileSystem.FileExists(newPath));

                    var contentHash = new ContentHash(ContentHashType, HexUtilities.HexToBytes(emptyFileVsoHashHex));
                    var contentFileInfo = await store.GetCacheFileInfo(contentHash);
                    Assert.NotNull(contentFileInfo);
                });

                Assert.False(FileSystem.DirectoryExists(oldRoot));
            }
        }

        private static ContentHash ComputeEmptyVsoHash()
        {
            using (var hasher = HashInfoLookup.Find(ContentHashType).CreateContentHasher())
            {
                return hasher.GetContentHash(new byte[] { });
            }
        }

        private static AbsolutePath GetPathForEmptyFile(AbsolutePath cacheRoot, int version)
        {
            var hashInfo = VsoHashInfo.Instance;
            switch (version)
            {
                case 0:
                case 1:
                    return cacheRoot / hashInfo.HashType.ToString() / HashOfEmptyFile.Substring(0, 3) / (HashOfEmptyFile + ".blob");
                case 2:
                case 3:
                case 4:
                    return cacheRoot / "Shared" / hashInfo.HashType.ToString() / HashOfEmptyFile.Substring(0, 3) /
                           (HashOfEmptyFile + ".blob");
                default:
                    throw new ArgumentException("Unknown version number: " + version);
            }
        }

        private async Task StartupWithCorruptedStoreFile(Func<TestFileSystemContentStoreInternal, AbsolutePath> fileToCorruptFunc)
        {
            var context = new Context(Logger);
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                AbsolutePath fileToCorrupt;
                using (var store = Create(testDirectory.Path, _clock))
                {
                    await store.StartupAsync(context).ShouldBeSuccess();
                    await store.ShutdownAsync(context).ShouldBeSuccess();
                    fileToCorrupt = fileToCorruptFunc(store);
                    FileSystem.WriteAllBytes(fileToCorrupt, new byte[] {});
                }

                using (var store = Create(testDirectory.Path, _clock))
                {
                    var result = await store.StartupAsync(context);
                    result.ShouldBeError();
                    result.ErrorMessage.Should().Contain(fileToCorrupt.Path);
                }
            }
        }

        private async Task UpgradeFromVersion1Helper(bool contentAlreadyExists)
        {
            var context = new Context(Logger);
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                using (var store = Create(testDirectory.Path, _clock))
                {
                    await store.StartupAsync(context).ShouldBeSuccess();
                    await store.ShutdownAsync(context).ShouldBeSuccess();
                    store.SerializedDataVersionForTest.WriteValueFile(1);
                }

                var oldPath = GetPathForEmptyFile(testDirectory.Path, 1);
                var newPath = GetPathForEmptyFile(testDirectory.Path, TestFileSystemContentStoreInternal.CurrentVersionNumber);

                if (contentAlreadyExists)
                {
                    FileSystem.CreateDirectory(newPath.Parent);
                    FileSystem.WriteAllBytes(newPath, new byte[0]);
                }

                FileSystem.CreateDirectory(oldPath.Parent);
                FileSystem.WriteAllBytes(oldPath, new byte[0]);

                await TestStore(context, _clock, testDirectory, store =>
                {
                    Assert.False(FileSystem.FileExists(oldPath));
                    Assert.True(FileSystem.FileExists(newPath));
                    Assert.Equal(
                        TestFileSystemContentStoreInternal.CurrentVersionNumber,
                        store.SerializedDataVersionForTest.ReadValueFile());
                    return Task.FromResult(true);
                });
            }
        }

        private async Task UpgradeFromVersion2Helper(bool contentIsAlreadyProtected)
        {
            var context = new Context(Logger);
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                using (var store = Create(testDirectory.Path, _clock))
                {
                    await store.StartupAsync(context).ShouldBeSuccess();
                    await store.ShutdownAsync(context).ShouldBeSuccess();
                    store.SerializedDataVersionForTest.WriteValueFile(2);
                }

                var contentPath = GetPathForEmptyFile(testDirectory.Path, 2);
                FileSystem.CreateDirectory(contentPath.Parent);
                FileSystem.WriteAllBytes(contentPath, new byte[0]);

                if (contentIsAlreadyProtected)
                {
                    FileSystem.SetFileAttributes(contentPath, FileAttributes.Normal);
                    FileSystem.DenyFileWrites(contentPath);
                    FileSystem.DenyAttributeWrites(contentPath);
                }
                else
                {
                    FileSystem.SetFileAttributes(contentPath, FileAttributes.ReadOnly);
                }

                await TestStore(context, _clock, testDirectory, store =>
                {
                    Assert.True(FileSystem.FileAttributesAreSubset(contentPath, FileAttributes.Normal | FileAttributes.Archive | FileAttributes.ReadOnly), FileSystem.GetFileAttributes(contentPath).ToString());
                    Action setAttributesFunc = () => FileSystem.SetFileAttributes(contentPath, FileAttributes.ReadOnly);
                    Action appendFunc = () => FileSystem.DeleteFile(contentPath);
                    setAttributesFunc.Should().NotThrow();
                    appendFunc.Should().Throw<IOException>();
                    return Task.FromResult(true);
                });
            }
        }
    }
}
