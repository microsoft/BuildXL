// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
    }
}
