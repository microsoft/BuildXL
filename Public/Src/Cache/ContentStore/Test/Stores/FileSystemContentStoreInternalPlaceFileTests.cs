// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Interfaces.Utils;
using BuildXL.Cache.ContentStore.InterfacesTest.FileSystem;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Time;
using BuildXL.Cache.ContentStore.InterfacesTest.Utils;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using AbsolutePath = BuildXL.Cache.ContentStore.Interfaces.FileSystem.AbsolutePath;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using Xunit.Abstractions;

namespace ContentStoreTest.Stores
{
    public class FileSystemContentStoreInternalPlaceFileTests : FileSystemContentStoreInternalTestBase
    {
        private readonly MemoryClock _clock;

        public FileSystemContentStoreInternalPlaceFileTests(ITestOutputHelper outputHelper)
            : base(() => new MemoryFileSystem(new MemoryClock(), Drives), TestGlobal.Logger, outputHelper)
        {
            _clock = (MemoryClock)((MemoryFileSystem)FileSystem).Clock;
        }

        [Fact]
        public Task PlaceFileHardLinkRecoversFromExtraReplica()
        {
            var context = new Context(Logger);
            return PlaceFileRecoversAsync(
                context,
                async (store, tempDirectory) => await store.CorruptWithExtraReplicaAsync(context, _clock, tempDirectory),
                FileRealizationMode.HardLink);
        }

        [Fact]
        public Task PlaceFileHardLinkRecoversFromMissingReplica()
        {
            var context = new Context(Logger);
            return PlaceFileRecoversAsync(
                context,
                async (store, tempDirectory) => await store.CorruptWithMissingReplicaAsync(context, _clock, tempDirectory),
                FileRealizationMode.HardLink);
        }

        private Task PlaceFileRecoversAsync(
            Context context,
            Func<TestFileSystemContentStoreInternal, DisposableDirectory, Task<byte[]>> corruptFunc,
            FileRealizationMode fileRealizationMode)
        {
            return TestStore(context, _clock, async store =>
            {
                using (var tempDirectory = new DisposableDirectory(FileSystem))
                {
                    var bytes = await corruptFunc(store, tempDirectory);
                    var contentHash = bytes.CalculateHash(ContentHashType);

                    var placePath = tempDirectory.CreateRandomFileName();
                    PlaceFileResult result = null;
                    Func<Task> putFunc =
                        async () =>
                        {
                            result = await store.PlaceFileAsync(
                                context,
                                contentHash,
                                placePath,
                                FileAccessMode.ReadOnly,
                                FileReplacementMode.FailIfExists,
                                FileRealizationMode.HardLink,
                                null);
                        };
                    putFunc.Should().NotThrow();
                    Assert.NotNull(result);
                    result.Code.Should()
                        .Be(fileRealizationMode == FileRealizationMode.Copy
                            ? PlaceFileResult.ResultCode.PlacedWithCopy
                            : PlaceFileResult.ResultCode.PlacedWithHardLink);
                    FileSystem.GetHardLinkCount(placePath)
                        .Should()
                        .Be(fileRealizationMode == FileRealizationMode.Copy ? 1 : 2);
                }
            });
        }

        [Fact]
        public Task PlaceFileCopyRecoversFromContentDirectoryEntryForNonexistentBlob()
        {
            var context = new Context(Logger);
            return PlaceFileRecoversFromContentDirectoryEntryForNonexistentBlobAsync(context, FileRealizationMode.Copy);
        }

        [Fact]
        public Task PlaceFileHardLinkRecoversFromContentDirectoryEntryForNonexistentBlob()
        {
            var context = new Context(Logger);
            return PlaceFileRecoversFromContentDirectoryEntryForNonexistentBlobAsync(context, FileRealizationMode.HardLink);
        }

        private Task PlaceFileRecoversFromContentDirectoryEntryForNonexistentBlobAsync(
            Context context,
            FileRealizationMode fileRealizationMode)
        {
            using (var tempDirectory = new DisposableDirectory(FileSystem))
            {
                return TestStore(context, _clock, async store =>
                {
                    (await store.ContentDirectoryForTest.GetCountAsync()).Should().Be(0);

                    var bytes = await store.CorruptWithContentDirectoryEntryForNonexistentBlobAsync();
                    var nonexistentHash = bytes.CalculateHash(ContentHashType);

                    // Ensure that Place treats the missing blob as a miss despite the bad content directory entry.
                    var result = await store.PlaceFileAsync(
                        context,
                        nonexistentHash,
                        tempDirectory.CreateRandomFileName(),
                        FileAccessMode.ReadOnly,
                        FileReplacementMode.FailIfExists,
                        fileRealizationMode,
                        null);
                    result.Code.Should().Be(PlaceFileResult.ResultCode.NotPlacedContentNotFound);

                    // Ensure the cache has removed the bad content directory entry.
                    (await store.ContentDirectoryForTest.GetCountAsync()).Should().Be(0);
                });
            }
        }

        [Fact]
        public Task PlaceFileCopyRecoversFromCorruptedBlob()
        {
            var context = new Context(Logger);
            return TestStore(context, _clock, async store =>
            {
                using (var tempDirectory = new DisposableDirectory(FileSystem))
                {
                    var bytes = await store.CorruptWithCorruptedBlob(context, tempDirectory);
                    var contentHash = bytes.CalculateHash(ContentHashType);
                    var placePath = tempDirectory.CreateRandomFileName();
                    var result = await store.PlaceFileAsync(
                        context,
                        contentHash,
                        placePath,
                        FileAccessMode.ReadOnly,
                        FileReplacementMode.ReplaceExisting,
                        FileRealizationMode.Copy,
                        null);

                    // First place should error
                    result.Code.Should().Be(PlaceFileResult.ResultCode.Error);
                    using (Stream stream = await FileSystem.OpenAsync(
                        placePath, FileAccess.Read, FileMode.Open, FileShare.Read))
                    {
                        (await stream.CalculateHashAsync(ContentHashType)).Should().Be(
                            new byte[] {0}.CalculateHash(ContentHashType));
                    }

                    // Second place should just be a miss
                    result = await store.PlaceFileAsync(
                        context,
                        contentHash,
                        placePath,
                        FileAccessMode.ReadOnly,
                        FileReplacementMode.ReplaceExisting,
                        FileRealizationMode.Copy,
                        null);
                    result.Code.Should().Be(PlaceFileResult.ResultCode.NotPlacedContentNotFound);
                }
            });
        }

        [Fact]
        public async Task PlaceFileCopyReturnsErrorForDestinationIOExceptionsAsync()
        {
            var context = new Context(Logger);
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                await TestStore(context, _clock, testDirectory, async store =>
                {
                    byte[] bytes = ThreadSafeRandom.GetBytes(ValueSize);
                    ContentHash contentHash = bytes.CalculateHash(ContentHashType);
                    AbsolutePath pathToContent = testDirectory.CreateRandomFileName();
                    FileSystem.WriteAllBytes(pathToContent, bytes);
                    await store.PutFileAsync(context, pathToContent, FileRealizationMode.Copy, contentHash, null).ShouldBeSuccess();

                    AbsolutePath destinationPath = FileSystem.MakeLongPath(testDirectory.Path, MaxShortPath + 1);

                    var result = await store.PlaceFileAsync(
                        context,
                        contentHash,
                        destinationPath,
                        FileAccessMode.ReadOnly,
                        FileReplacementMode.ReplaceExisting,
                        FileRealizationMode.Copy,
                        null);

                    if (OperatingSystemHelper.IsWindowsOS && AbsolutePath.LongPathsSupported)
                    {
                        result.ShouldBeSuccess();
                    }
                    else
                    {
                        result.Code.Should().Be(PlaceFileResult.ResultCode.Error, result.ToString());
                        result.ErrorMessage.Should().Contain($"The fully qualified file name must be less than {FileSystemConstants.MaxPath} characters, and the directory name must be less than {FileSystemConstants.MaxPath - 12} characters.");
                        result.ErrorMessage.Should().Contain(destinationPath.Path);
                    }
                });
            }
        }

        [Fact]
        public Task PlaceFileHardLinkToLongFilePaths()
        {
            var context = new Context(Logger);
            return TestStore(context, _clock, async store =>
            {
                byte[] bytes = ThreadSafeRandom.GetBytes(ValueSize);

                using (var tempDirectory = new DisposableDirectory(FileSystem))
                {
                    var pathToContent = tempDirectory.CreateRandomFileName();
                    FileSystem.WriteAllBytes(pathToContent, bytes);

                    // Put the content into the store via copy
                    var r = await store.PutFileAsync(context, pathToContent, FileRealizationMode.Copy, ContentHashType, null);

                    foreach (var length in Enumerable.Range(MaxShortPath - 10, 20))
                    {
                        var shouldError = length >= MaxShortPath && !AbsolutePath.LongPathsSupported;
                        await PlaceFileHardLinkToLongFilePathAsync(store, context, tempDirectory, r.ContentHash, length, shouldError);
                    }
                }
            });
        }

        private async Task PlaceFileHardLinkToLongFilePathAsync(
            TestFileSystemContentStoreInternal store,
            Context context,
            DisposableDirectory tempDirectory,
            ContentHash hash,
            int length,
            bool shouldError)
        {
            AbsolutePath placePath = FileSystem.MakeLongPath(tempDirectory.Path, length);

            var result = await store.PlaceFileAsync(
                context, hash, placePath, FileAccessMode.ReadOnly, FileReplacementMode.FailIfExists, FileRealizationMode.HardLink, null);

            result.Code.Should().Be(shouldError ? PlaceFileResult.ResultCode.Error : PlaceFileResult.ResultCode.PlacedWithHardLink, result.ToString());
        }

        [Fact]
        [Trait("Category", "WindowsOSOnly")] // Mac machines only have one drive
        public Task PlaceFileHardLinkAcrossDrivesThrows()
        {
            return PlaceFileAcrossDrivesAsync(
                FileRealizationMode.HardLink,
                result =>
                {
                    return !result.Succeeded && result.ErrorMessage.Contains("FailedSourceAndDestinationOnDifferentVolumes");
                });
        }

        [Fact]
        public Task PlaceFileAnyAcrossDrivesFallsBackToCopy()
        {
            return PlaceFileAcrossDrivesAsync(FileRealizationMode.Any, result => result.Succeeded);
        }

        private async Task PlaceFileAcrossDrivesAsync(
            FileRealizationMode allowedFileRealizationMode, Func<PlaceFileResult, bool> checkResult)
        {
            // This only works when we have multiple drives.
            if (FileSystem is MemoryFileSystem)
            {
                return;
            }

            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                var context = new Context(Logger);

                using (var store = Create(testDirectory.Path, _clock))
                {
                    var startupResult = await store.StartupAsync(context);
                    startupResult.ShouldBeSuccess();
                    try
                    {
                        byte[] bytes = ThreadSafeRandom.GetBytes(ValueSize);

                        AbsolutePath pathToContent = testDirectory.CreateRandomFileName();
                        FileSystem.WriteAllBytes(pathToContent, bytes);

                        // Put the content into the store via copy
                        var putResult = await store.PutFileAsync(
                            context, pathToContent, FileRealizationMode.Copy, ContentHashType, null);
                        ResultTestExtensions.ShouldBeSuccess((BoolResult) putResult);

                        var pathToPlaceDifferentVolume = new AbsolutePath(PathGeneratorUtilities.GetAbsolutePath("D", "foo.txt"));

                        try
                        {
                            var result = await store.PlaceFileAsync(
                                context,
                                putResult.ContentHash,
                                pathToPlaceDifferentVolume,
                                FileAccessMode.ReadOnly,
                                FileReplacementMode.FailIfExists,
                                allowedFileRealizationMode,
                                null);
                            Assert.True(checkResult(result), result.ToString());
                        }
                        finally
                        {
                            FileSystem.DeleteFile(pathToPlaceDifferentVolume);
                        }
                    }
                    finally
                    {
                        await store.ShutdownAsync(context).ShouldBeSuccess();
                    }
                }
            }
        }
    }
}
