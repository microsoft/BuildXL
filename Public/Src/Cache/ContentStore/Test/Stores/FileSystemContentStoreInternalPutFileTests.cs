// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.InterfacesTest.Utils;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;
using BuildXL.Utilities.ParallelAlgorithms;
using System.Threading;

namespace ContentStoreTest.Stores
{
    public class FileSystemContentStoreInternalPutFileTests : FileSystemContentStoreInternalTestBase
    {
        protected readonly MemoryClock Clock;

        public FileSystemContentStoreInternalPutFileTests(ITestOutputHelper output)
            : base(() => new MemoryFileSystem(new MemoryClock(), Drives), TestGlobal.Logger, output)
        {
            Clock = (MemoryClock)((MemoryFileSystem)FileSystem).Clock;
        }

        [Fact]
        public Task PutFileWithLongPath()
        {
            var context = new Context(Logger);
            
            if (!AbsolutePath.LongPathsSupported)
            {
                context.Debug($"The test '{nameof(PutFileWithLongPath)}' is skipped because long paths are not supported by the current version of .net framework.");
                return Task.FromResult(1);
            }

            return TestStore(context, Clock, async store =>
            {
                byte[] bytes = ThreadSafeRandom.GetBytes(ValueSize);
                ContentHash contentHash = bytes.CalculateHash(ContentHashType);

                // Verify content doesn't exist yet in store
                Assert.False(await store.ContainsAsync(context, contentHash, null));

                using (var tempDirectory = new DisposableDirectory(FileSystem))
                {
                    string longPathPart = new string('a', 300);
                    AbsolutePath pathToContent = tempDirectory.Path / $"tempContent{longPathPart}.txt";
                    FileSystem.WriteAllBytes(pathToContent, bytes);
                    ContentHash hashFromPut;
                    using (var pinContext = store.CreatePinContext())
                    {
                        // Put the content into the store w/ hard link
                        var r = await store.PutFileAsync(
                            context, pathToContent, FileRealizationMode.Any, ContentHashType, new PinRequest(pinContext));
                        hashFromPut = r.ContentHash;
                        Clock.Increment();
                        await store.EnsureContentIsPinned(context, Clock, hashFromPut);
                        Assert.True(pinContext.Contains(hashFromPut));
                    }

                    await store.EnsureContentIsNotPinned(context, Clock, hashFromPut);
                }
            });
        }

        [Fact]
        public Task PutFilePins()
        {
            var context = new Context(Logger);
            return TestStore(context, Clock, async store =>
            {
                byte[] bytes = ThreadSafeRandom.GetBytes(ValueSize);
                ContentHash contentHash = bytes.CalculateHash(ContentHashType);

                // Verify content doesn't exist yet in store
                Assert.False(await store.ContainsAsync(context, contentHash, null));

                using (var tempDirectory = new DisposableDirectory(FileSystem))
                {
                    AbsolutePath pathToContent = tempDirectory.Path / "tempContent.txt";
                    FileSystem.WriteAllBytes(pathToContent, bytes);
                    ContentHash hashFromPut;
                    using (var pinContext = store.CreatePinContext())
                    {
                        // Put the content into the store w/ hard link
                        var r = await store.PutFileAsync(
                            context, pathToContent, FileRealizationMode.Any, ContentHashType, new PinRequest(pinContext));
                        hashFromPut = r.ContentHash;
                        Clock.Increment();
                        await store.EnsureContentIsPinned(context, Clock, hashFromPut);
                        Assert.True(pinContext.Contains(hashFromPut));
                    }

                    await store.EnsureContentIsNotPinned(context, Clock, hashFromPut);
                }
            });
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public Task PutSameContentManyTimesTest(bool useRedundantPutFileShortcut)
        {
            var context = new Context(Logger);
            ContentStoreSettings = new ContentStoreSettings()
            {
                UseRedundantPutFileShortcut = useRedundantPutFileShortcut
            };

            return TestStore(context, Clock, async store =>
            {
                byte[] bytes = ThreadSafeRandom.GetBytes(ValueSize);
                ContentHash contentHash = bytes.CalculateHash(ContentHashType);

                // Verify content doesn't exist yet in store
                Assert.False(await store.ContainsAsync(context, contentHash, null));

                using (var tempDirectory = new DisposableDirectory(FileSystem))
                {
                    
                    ContentHash hashFromPut;
                    using (var pinContext = store.CreatePinContext())
                    {
                        var concurrency = 24;
                        var iterations = 100;

                        var items = Enumerable.Range(0, concurrency).Select(i =>
                        {
                            AbsolutePath pathToContent = tempDirectory.Path / $"tempContent{i}.txt";
                            FileSystem.WriteAllBytes(pathToContent, bytes);
                            return (pathToContent, iterations);
                        }).ToArray();

                        await ParallelAlgorithms.WhenDoneAsync(24, CancellationToken.None, async (scheduleItem, item) =>
                        {
                            // Put the content into the store w/ hard link
                            var r = await store.PutFileAsync(
                                    context, item.pathToContent, FileRealizationMode.Any, ContentHashType, new PinRequest(pinContext));
                            hashFromPut = r.ContentHash;
                            Clock.Increment();
                            Assert.True(pinContext.Contains(hashFromPut));

                            if (item.iterations != 0)
                            {
                                scheduleItem((item.pathToContent, item.iterations - 1));
                            }
                        },
                        items);
                    }
                }
            });
        }

        [Fact]
        public Task PutFileTrustsHash()
        {
            var context = new Context(Logger);
            return TestStore(context, Clock, async store =>
            {
                byte[] bytes = ThreadSafeRandom.GetBytes(ValueSize);
                ContentHash contentHash = bytes.CalculateHash(ContentHashType);

                ContentHash fakeHash = ThreadSafeRandom.GetBytes(ValueSize).CalculateHash(ContentHashType);

                // Verify content doesn't exist yet in store
                Assert.False(await store.ContainsAsync(context, contentHash, null));

                using (var tempDirectory = new DisposableDirectory(FileSystem))
                {
                    AbsolutePath pathToContent = tempDirectory.Path / "tempContent.txt";
                    FileSystem.WriteAllBytes(pathToContent, bytes);
                    ContentHash hashFromPut;
                    using (var pinContext = store.CreatePinContext())
                    {
                        // Put the content into the store w/ hard link
                        var r = await store.PutTrustedFileAsync(
                            context, pathToContent, FileRealizationMode.Any, new ContentHashWithSize(fakeHash, ValueSize), new PinRequest(pinContext));
                        hashFromPut = r.ContentHash;
                        Clock.Increment();
                        await store.EnsureContentIsPinned(context, Clock, hashFromPut);
                        Assert.True(pinContext.Contains(hashFromPut));
                    }

                    await store.EnsureContentIsNotPinned(context, Clock, hashFromPut);
                    Assert.Equal(fakeHash, hashFromPut);
                    Assert.NotEqual(contentHash, hashFromPut);
                }
            });
        }

        [Fact]
        public Task PutFileCreatesHardLink()
        {
            var context = new Context(Logger);
            return TestStore(context, Clock, async store =>
            {
                byte[] bytes = ThreadSafeRandom.GetBytes(ValueSize);
                ContentHash contentHash = bytes.CalculateHash(ContentHashType);

                // Verify content doesn't exist yet in store
                Assert.False(await store.ContainsAsync(context, contentHash, null));

                using (var tempDirectory = new DisposableDirectory(FileSystem))
                {
                    AbsolutePath pathToContent = tempDirectory.Path / "tempContent.txt";
                    FileSystem.WriteAllBytes(pathToContent, bytes);

                    // Put the content into the store w/ hard link
                    var r = await store.PutFileAsync(context, pathToContent, FileRealizationMode.Any, ContentHashType, null);
                    ContentHash hashFromPut = r.ContentHash;

                    byte[] newBytes = ThreadSafeRandom.GetBytes(10);
                    Assert.False(newBytes.SequenceEqual(bytes));

                    // Update content
                    // This sneaky approach is only for testing, and is unexpected behavior
                    // in the system that could lead to cache inconsistencies.
                    FileSystem.AllowFileWrites(pathToContent);
                    FileSystem.AllowAttributeWrites(pathToContent);
                    FileSystem.SetFileAttributes(pathToContent, FileAttributes.Normal);

                    FileSystem.WriteAllBytes(pathToContent, newBytes);

                    // Verify new content is now reflected into the cache under the covers
                    var result = await store.OpenStreamAsync(context, hashFromPut, null);
                    byte[] bytesFromGet = await result.Stream.GetBytes();
                    Assert.True(bytesFromGet.SequenceEqual(newBytes));
                }
            });
        }

        [Fact]
        public Task PutFileCreatesReadOnlyHardLink()
        {
            var context = new Context(Logger);
            return TestStore(context, Clock, async store =>
            {
                byte[] bytes = ThreadSafeRandom.GetBytes(ValueSize);
                ContentHash contentHash = bytes.CalculateHash(ContentHashType);

                // Verify content doesn't exist yet in store
                Assert.False(await store.ContainsAsync(context, contentHash, null));

                using (var tempDirectory = new DisposableDirectory(FileSystem))
                {
                    AbsolutePath pathToContent = tempDirectory.Path / "tempContent.txt";
                    FileSystem.WriteAllBytes(pathToContent, bytes);

                    // Put the content into the store w/ hard link
                    await store.PutFileAsync(context, pathToContent, FileRealizationMode.HardLink, ContentHashType, null).ShouldBeSuccess();

                    byte[] newBytes = ThreadSafeRandom.GetBytes(10);
                    Assert.False(newBytes.SequenceEqual(bytes));

                    // Update content
                    Action writeAction = () => FileSystem.WriteAllBytes(pathToContent, newBytes);

                    writeAction.Should().Throw<UnauthorizedAccessException>();
                }
            });
        }

        [Fact]
        public Task PutFileHardLinkOverwritesSourceOnMismatchedExistingContentHash()
        {
            var context = new Context(Logger);
            return TestStore(context, Clock, async store =>
            {
                using (var tempDirectory = new DisposableDirectory(FileSystem))
                {
                    var bytes = ThreadSafeRandom.GetBytes(100);
                    var contentHash = bytes.CalculateHash(ContentHashType);
                    AbsolutePath sourcePath = tempDirectory.CreateRandomFileName();
                    FileSystem.WriteAllBytes(sourcePath, bytes);
                    await store.PutFileAsync(context, sourcePath, FileRealizationMode.HardLink, ContentHashType, null).ShouldBeSuccess();

                    var bytes2 = ThreadSafeRandom.GetBytes(200);
                    AbsolutePath sourcePath2 = tempDirectory.CreateRandomFileName();
                    FileSystem.WriteAllBytes(sourcePath2, bytes2);

                    var result = await store.PutFileAsync(context, sourcePath2, FileRealizationMode.HardLink, contentHash, null);
                    result.ContentHash.Should().Be(contentHash);
                    using (Stream stream = await FileSystem.OpenAsync(
                        sourcePath2, FileAccess.Read, FileMode.Open, FileShare.Read))
                    {
                        (await stream.CalculateHashAsync(ContentHashType)).Should().Be(contentHash);
                    }
                }
            });
        }

        [Fact]
        public Task PutFileWithExistingHashHardLinks()
        {
            var context = new Context(Logger);
            return TestStore(context, Clock, async store =>
            {
                using (var tempDirectory = new DisposableDirectory(FileSystem))
                {
                    var bytes = ThreadSafeRandom.GetBytes(100);
                    AbsolutePath sourcePath = tempDirectory.CreateRandomFileName();
                    FileSystem.WriteAllBytes(sourcePath, bytes);
                    await store.PutFileAsync(context, sourcePath, FileRealizationMode.HardLink, ContentHashType, null).ShouldBeSuccess();

                    AbsolutePath sourcePath2 = tempDirectory.CreateRandomFileName();
                    FileSystem.WriteAllBytes(sourcePath2, bytes);
                    await store.PutFileAsync(
                        context,
                        sourcePath2,
                        FileRealizationMode.HardLink,
                        bytes.CalculateHash(ContentHashType),
                        null).ShouldBeSuccess();
                    FileSystem.GetHardLinkCount(sourcePath2).Should().Be(3);
                }
            });
        }

        [Fact]
        public Task PutFileHardLinkRecoversFromContentDirectoryEntryForNonexistentBlob()
        {
            return PutFileRecoversAsync(FileRealizationMode.HardLink, async (store, tempDirectory) =>
                await store.CorruptWithContentDirectoryEntryForNonexistentBlobAsync());
        }

        [Fact]
        public Task PutFileCopyRecoversFromBlobForNonexistentContentDirectoryEntry()
        {
            var context = new Context(Logger);
            return PutFileRecoversAsync(FileRealizationMode.Copy, async (store, tempDirectory) =>
                await store.CorruptWithBlobForNonexistentContentDirectoryEntry(context, tempDirectory));
        }

        [Fact]
        public Task PutFileHardLinkRecoversFromBlobForNonexistentContentDirectoryEntry()
        {
            var context = new Context(Logger);
            return PutFileRecoversAsync(FileRealizationMode.HardLink, async (store, tempDirectory) =>
                await store.CorruptWithBlobForNonexistentContentDirectoryEntry(context, tempDirectory));
        }

        [Fact]
        public Task PutFileHardLinkRecoversFromExtraReplica()
        {
            var context = new Context(Logger);
            return PutFileRecoversAsync(FileRealizationMode.HardLink, async (store, tempDirectory) =>
                await store.CorruptWithExtraReplicaAsync(context, Clock, tempDirectory));
        }

        [Fact]
        public Task PutFileHardLinkRecoversFromMissingReplica()
        {
            var context = new Context(Logger);
            return PutFileRecoversAsync(FileRealizationMode.HardLink, async (store, tempDirectory) =>
                await store.CorruptWithMissingReplicaAsync(context, Clock, tempDirectory));
        }

        [Fact]
        public Task PutFileCopyRecoversFromContentDirectoryEntryForNonexistentBlob()
        {
            return PutFileRecoversAsync(FileRealizationMode.Copy, async (store, tempDirectory) =>
                await store.CorruptWithContentDirectoryEntryForNonexistentBlobAsync());
        }

        private Task PutFileRecoversAsync(
            FileRealizationMode fileRealizationMode,
            Func<TestFileSystemContentStoreInternal, DisposableDirectory, Task<byte[]>> corruptFunc)
        {
            var context = new Context(Logger);
            return TestStore(context, Clock, async store =>
            {
                using (var tempDirectory = new DisposableDirectory(FileSystem))
                {
                    var bytes = await corruptFunc(store, tempDirectory);

                    var contentPath2 = tempDirectory.CreateRandomFileName();
                    FileSystem.WriteAllBytes(contentPath2, bytes);
                    await store.PutFileAsync(
                        context, contentPath2, fileRealizationMode, ContentHashType, null).ShouldBeSuccess();
                    FileSystem.GetHardLinkCount(contentPath2)
                        .Should().Be(fileRealizationMode == FileRealizationMode.Copy ? 1 : 2);
                    await store.EnsureHasContent(context, bytes.CalculateHash(ContentHashType), tempDirectory);
                }
            });
        }

        [Fact]
        public Task PutFileExistingContentOverwritesSource()
        {
            var context = new Context(Logger);
            return TestStore(context, Clock, async store =>
            {
                byte[] bytes = ThreadSafeRandom.GetBytes(ValueSize);
                ContentHash contentHash = bytes.CalculateHash(ContentHashType);

                // Verify content doesn't exist yet in store
                Assert.False(await store.ContainsAsync(context, contentHash, null));

                using (var tempDirectory = new DisposableDirectory(FileSystem))
                {
                    AbsolutePath pathToContent = tempDirectory.Path / "tempContent.txt";
                    FileSystem.WriteAllBytes(pathToContent, bytes);

                    ulong originalFileId = FileSystem.GetFileId(pathToContent);

                    // Put the content into the store via copy
                    await store.PutFileAsync(context, pathToContent, FileRealizationMode.Copy, ContentHashType, null).ShouldBeSuccess();

                    Assert.Equal(originalFileId, FileSystem.GetFileId(pathToContent));

                    // Ensure we can open it
                    using (await FileSystem.OpenAsync(
                        pathToContent, FileAccess.Write, FileMode.Open, FileShare.None))
                    {
                    }

                    Assert.Equal(originalFileId, FileSystem.GetFileId(pathToContent));

                    // Now hard link it in. This should create the hard-link from the cache out to pathToContent.
                    await store.PutFileAsync(context, pathToContent, FileRealizationMode.HardLink, ContentHashType, null).ShouldBeSuccess();

                    Assert.NotEqual(originalFileId, FileSystem.GetFileId(pathToContent));

                    Func<Task> writeFunc = async () => await FileSystem.OpenAsync(
                        pathToContent, FileAccess.Write, FileMode.Open, FileShare.None);
                    writeFunc.Should().Throw<UnauthorizedAccessException>();
                }
            });
        }

        [Fact]
        [Trait("Category", "WindowsOSOnly")]
        public Task PutFileHardLinkAcrossDrivesThrows()
        {
            return PutFileAcrossDrivesAsync(FileRealizationMode.HardLink, false, false, result =>
                result.ErrorMessage.Contains("FailedSourceAndDestinationOnDifferentVolumes"));
        }

        [Fact]
        public Task PutFileAnyAcrossDrivesFallsBackToCopy()
        {
            return PutFileAcrossDrivesAsync(FileRealizationMode.Any, false, true, result => result.Succeeded);
        }

        [Fact]
        [Trait("Category", "WindowsOSOnly")]
        public Task PutFileHardLinkExistingContentThrows()
        {
            return PutFileAcrossDrivesAsync(FileRealizationMode.HardLink, true, true, result =>
                result.ErrorMessage.Contains("FailedSourceAndDestinationOnDifferentVolumes"));
        }

        [Fact]
        public Task PutFileAnyExistingContentFallsBackToCopy()
        {
            return PutFileAcrossDrivesAsync(FileRealizationMode.Any, true, true, result => result.Succeeded);
        }

        private async Task PutFileAcrossDrivesAsync(
            FileRealizationMode allowedFileRealizationMode,
            bool contentAlreadyCached,
            bool contentShouldBeCached,
            Func<PutResult, bool> checkResult)
        {
            // This only works when we have multiple drives.
            var memoryFileSystem = FileSystem as MemoryFileSystem;
            if (memoryFileSystem == null)
            {
                return;
            }

            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var context = new Context(Logger);

                try
                {
                    using (var store = Create(testDirectory.Path, Clock))
                    {
                        await store.StartupAsync(context).ShouldBeSuccess();

                        byte[] bytes = ThreadSafeRandom.GetBytes(ValueSize);
                        ContentHash contentHash = bytes.CalculateHash(ContentHashType);

                        // Verify content doesn't exist yet in store
                        Assert.False(await store.ContainsAsync(context, contentHash, null));

                        var pathToContentDifferentVolume = new AbsolutePath(PathGeneratorUtilities.GetAbsolutePath("D", "foo.txt"));

                        try
                        {
                            FileSystem.WriteAllBytes(pathToContentDifferentVolume, bytes);
                            if (contentAlreadyCached)
                            {
                                await store.PutFileAsync(
                                    context, pathToContentDifferentVolume, FileRealizationMode.Copy, ContentHashType, null).ShouldBeSuccess();
                            }

                            var result = await store.PutFileAsync(
                                context, pathToContentDifferentVolume, allowedFileRealizationMode, ContentHashType, null);
                            Assert.True(checkResult(result));

                            (await store.ContainsAsync(context, contentHash, null)).Should()
                                .Be(contentShouldBeCached);
                        }
                        finally
                        {
                            FileSystem.DeleteFile(pathToContentDifferentVolume);
                        }

                        await store.ShutdownAsync(context).ShouldBeSuccess();
                    }
                }
                finally
                {
                    FileSystem.DeleteDirectory(testDirectory.Path, DeleteOptions.All);
                }
            }
        }

        [Fact]
        public Task PutFileWithMissingFileReturnsError()
        {
            var context = new Context(Logger);
            return TestStore(context, Clock, async store =>
            {
                var contentHash = ContentHash.Random();
                using (var tempDirectory = new DisposableDirectory(FileSystem))
                {
                    var r = await store.PutFileAsync(
                        context, tempDirectory.CreateRandomFileName(), FileRealizationMode.Any, contentHash, null);
                    r.Succeeded.Should().BeFalse();
                    r.ErrorMessage.Should().Contain("Source file not found");
                }
            });
        }
    }
}
