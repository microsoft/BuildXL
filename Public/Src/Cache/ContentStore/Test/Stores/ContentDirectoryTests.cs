// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;

namespace ContentStoreTest.Stores
{
    public abstract class ContentDirectoryTests : TestBase
    {
        private const int DefaultInfoCount = 10;
        private const int DefaultFileSize = 100;
        private const int DefaultReplicaCount = 1;

        protected readonly MemoryClock MemoryClock;

        private readonly IContentHasher _hasher;

        protected ContentDirectoryTests(ILogger logger, MemoryClock clock, Lazy<IAbsFileSystem> fileSystem)
            : base(logger, fileSystem)
        {
            MemoryClock = clock;
            _hasher = HashInfoLookup.Find(HashType.Vso0).CreateContentHasher();
        }

        protected abstract IContentDirectory CreateContentDirectory(DisposableDirectory testDirectory);

        [Fact]
        public Task UserConstructorSucceeds()
        {
            var context = new Context(Logger);
            return TestContentDirectory(context);
        }

        [Fact]
        public async Task ContentDirectoryPersists()
        {
            var context = new Context(Logger);
            IReadOnlyList<KeyValuePair<ContentHash, ContentFileInfo>> hashInfoPairs = null;
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                await TestContentDirectory(context, testDirectory, async contentDirectory =>
                {
                    hashInfoPairs = await PopulateRandomInfo(contentDirectory);
                });

                Assert.NotNull(hashInfoPairs);

                await TestContentDirectory(context, testDirectory, async contentDirectory =>
                {
                    var contentHashes = await contentDirectory.EnumerateContentHashesAsync();
                    contentHashes.Count().Should().Be(hashInfoPairs.Count);

                    foreach (var hashInfoPair in hashInfoPairs)
                    {
                        await contentDirectory.UpdateAsync(hashInfoPair.Key, false, MemoryClock, fileInfo =>
                        {
                            fileInfo.Should().NotBeNull();
                            fileInfo.FileSize.Should().Be(hashInfoPair.Value.FileSize);
                            fileInfo.ReplicaCount.Should().Be(hashInfoPair.Value.ReplicaCount);
                            fileInfo.LastAccessedFileTimeUtc.Should().Be(hashInfoPair.Value.LastAccessedFileTimeUtc);
                            return null;
                        });
                    }
                });
            }
        }

        [Fact]
        public Task GetCountAndSize()
        {
            var context = new Context(Logger);
            return TestContentDirectory(context, async contentDirectory =>
            {
                (await contentDirectory.GetCountAsync()).Should().Be(0);
                (await contentDirectory.GetSizeAsync()).Should().Be(0);
                (await contentDirectory.GetTotalReplicaCountAsync()).Should().Be(0);

                await PopulateRandomInfo(contentDirectory);

                (await contentDirectory.GetCountAsync()).Should().Be(DefaultInfoCount);
                (await contentDirectory.GetSizeAsync()).Should().Be(DefaultFileSize * DefaultInfoCount);
            });
        }

        [Fact]
        public Task EnumerateContentHashes()
        {
            var context = new Context(Logger);
            return TestContentDirectory(context, async contentDirectory =>
            {
                var hashes = (await PopulateRandomInfo(contentDirectory)).Select(pair => pair.Key).ToArray();
                ContentHash[] result = (await contentDirectory.EnumerateContentHashesAsync()).ToArray();
                result.Should().BeEquivalentTo(hashes);
            });
        }

        [Fact]
        public Task Remove()
        {
            var context = new Context(Logger);
            return TestContentDirectory(context, async contentDirectory =>
            {
                Func<int, bool> alternator = i => i % 2 == 0;
                var hashInfoPairs = await PopulateRandomInfo(contentDirectory, alternator);

                await Enumerable.Range(0, DefaultInfoCount).ParallelForEachAsync(async i =>
                {
                    var removedInfo = await contentDirectory.RemoveAsync(hashInfoPairs[i].Key);
                    var removed = removedInfo != null;
                    removed.Should().Be(alternator(i));
                    if (removed)
                    {
                        Assert.True(removedInfo.Equals(hashInfoPairs[i].Value));
                    }

                    (await contentDirectory.RemoveAsync(hashInfoPairs[i].Key)).Should().BeNull();
                });
            });
        }

        [Fact]
        public Task PopulateWithUpdate()
        {
            var context = new Context(Logger);
            return TestContentDirectory(context, contentDirectory => PopulateRandomInfo(contentDirectory));
        }

        [Fact]
        public Task ModifyWithUpdate()
        {
            var context = new Context(Logger);
            return TestContentDirectory(context, async contentDirectory =>
            {
                var firstHash = (await PopulateRandomInfo(contentDirectory))[0].Key;
                var newInfo = new ContentFileInfo(MemoryClock, DefaultFileSize * 2, DefaultReplicaCount + 1);

                await contentDirectory.UpdateAsync(firstHash, false, MemoryClock, fileInfo =>
                {
                    Assert.NotNull(fileInfo);
                    return Task.FromResult(newInfo);
                });

                await contentDirectory.UpdateAsync(firstHash, false, MemoryClock, fileInfo =>
                {
                    fileInfo.ReplicaCount.Should().Be(newInfo.ReplicaCount);
                    return Task.FromResult((ContentFileInfo)null);
                });
            });
        }

        [Fact]
        public Task CannotModifyOutsideUpdate()
        {
            var context = new Context(Logger);
            return TestContentDirectory(context, async contentDirectory =>
            {
                var firstHash = (await PopulateRandomInfo(contentDirectory))[0].Key;

                ContentFileInfo outsideInfo = null;
                await contentDirectory.UpdateAsync(firstHash, false, MemoryClock, fileInfo =>
                {
                    Assert.NotNull(fileInfo);
                    outsideInfo = fileInfo;
                    return null;
                });

                outsideInfo.ReplicaCount++;
                await contentDirectory.UpdateAsync(firstHash, false, MemoryClock, fileInfo =>
                {
                    fileInfo.ReplicaCount.Should().NotBe(outsideInfo.ReplicaCount);
                    return null;
                });
            });
        }

        [Fact]
        public Task GetLruOrderedCacheContent()
        {
            var context = new Context(Logger);
            return TestContentDirectory(context, async contentDirectory =>
            {
                var hashInfoPairs = await PopulateRandomInfo(contentDirectory);

                for (int i = 0; i < DefaultInfoCount; i++)
                {
                    await contentDirectory.UpdateAsync(hashInfoPairs[i].Key, true, MemoryClock, fileInfo => null);
                }

                var lruOrderedHashes = await contentDirectory.GetLruOrderedCacheContentAsync();

                // ReSharper disable once ReturnValueOfPureMethodIsNotUsed
                lruOrderedHashes.Zip(hashInfoPairs, (orderedHash, hashInfoPair) => orderedHash.Should().Be(hashInfoPair.Key));
            });
        }

        [Fact]
        public async Task ClearedIfDeleted()
        {
            var context = new Context(Logger);
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                AbsolutePath filePath = await TestContentDirectory(context, testDirectory, contentDirectory =>
                    PopulateRandomInfo(contentDirectory));
                FileSystem.DeleteFile(filePath);
                await TestContentDirectory(context, testDirectory, async contentDirectory =>
                    (await contentDirectory.GetCountAsync()).Should().Be(0));
            }
        }

        [Fact]
        public Task HandlesMultipleHashTypes()
        {
            var context = new Context(Logger);
            return TestContentDirectory(context, async contentDirectory =>
            {
                using (var sha1Hasher = HashInfoLookup.Find(HashType.SHA1).CreateContentHasher())
                {
                    using (var sha256Hasher = HashInfoLookup.Find(HashType.SHA256).CreateContentHasher())
                    {
                        byte[] content = ThreadSafeRandom.GetBytes(DefaultFileSize);
                        ContentHash sha1ContentHash = sha1Hasher.GetContentHash(content);
                        ContentHash sha256ContentHash = sha256Hasher.GetContentHash(content);

                        await contentDirectory.UpdateAsync(sha1ContentHash, true, MemoryClock, info =>
                        {
                            Assert.Null(info);
                            return Task.FromResult(new ContentFileInfo(MemoryClock, 1, content.Length));
                        });

                        await contentDirectory.UpdateAsync(sha256ContentHash, true, MemoryClock, info =>
                        {
                            Assert.Null(info);
                            return Task.FromResult(new ContentFileInfo(MemoryClock, 2, content.Length));
                        });

                        var hashes = (await contentDirectory.EnumerateContentHashesAsync()).ToList();
                        hashes.Contains(sha1ContentHash).Should().BeTrue();
                        hashes.Contains(sha256ContentHash).Should().BeTrue();
                    }
                }
            });
        }

        [Fact]
        public async Task PersistsFile()
        {
            var context = new Context(Logger);
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                AbsolutePath filePath = await PopulateAndGetFilePath(context, testDirectory);
                Assert.True(FileSystem.FileExists(filePath));
            }
        }

        protected async Task<IReadOnlyList<KeyValuePair<ContentHash, ContentFileInfo>>> PopulateRandomInfo(
            IContentDirectory contentDirectory, Func<int, bool> shouldPopulateIteration = null)
        {
            var list = new List<KeyValuePair<ContentHash, ContentFileInfo>>();
            for (var i = 0; i < DefaultInfoCount; i++)
            {
                byte[] content = ThreadSafeRandom.GetBytes(DefaultFileSize);
                using (var stream = new MemoryStream(content))
                {
                    ContentHash contentHash = await _hasher.GetContentHashAsync(stream);
                    var newInfo = new ContentFileInfo(MemoryClock, content.Length);

                    if (shouldPopulateIteration == null || shouldPopulateIteration(i))
                    {
                        await contentDirectory.UpdateAsync(contentHash, false, MemoryClock, fileInfo =>
                        {
                            Assert.Null(fileInfo);
                            return Task.FromResult(newInfo);
                        });
                    }

                    list.Add(new KeyValuePair<ContentHash, ContentFileInfo>(contentHash, newInfo));
                }
            }

            return list;
        }

        private Task<AbsolutePath> TestContentDirectory(Context context, Func<IContentDirectory, Task> contentDirectoryFunc)
        {
            return TestContentDirectory(context, null, contentDirectoryFunc);
        }

        protected async Task<AbsolutePath> TestContentDirectory(
            Context context,
            DisposableDirectory testDirectory = null,
            Func<IContentDirectory, Task> contentDirectoryFunc = null,
            bool shutdown = true)
        {
            using (var tempTestDirectory = new DisposableDirectory(FileSystem))
            {
                using (IContentDirectory contentDirectory = CreateContentDirectory(testDirectory ?? tempTestDirectory))
                {
                    await contentDirectory.StartupAsync(context).ShouldBeSuccess();
                    if (contentDirectoryFunc != null)
                    {
                        await contentDirectoryFunc.Invoke(contentDirectory);
                    }

                    if (shutdown)
                    {
                        await contentDirectory.ShutdownAsync(context).ShouldBeSuccess();
                    }

                    return contentDirectory.FilePath;
                }
            }
        }

        protected async Task<AbsolutePath> PopulateAndGetFilePath(Context context, DisposableDirectory testDirectory)
        {
            AbsolutePath filePath = await TestContentDirectory(
                context, testDirectory, contentDirectory => PopulateRandomInfo(contentDirectory));
            return filePath;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _hasher.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
