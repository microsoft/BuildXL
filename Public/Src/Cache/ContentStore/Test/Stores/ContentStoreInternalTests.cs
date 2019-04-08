// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using BuildXL.Cache.ContentStore.UtilitiesCore;

namespace ContentStoreTest.Stores
{
    public abstract class ContentStoreInternalTests<TStore> : TestBase
        where TStore : class, IContentStoreInternal
    {
        private const HashType ContentHashType = HashType.Vso0;
        protected const int ValueSize = 100;
        private readonly DisposableDirectory _tempDirectory;

        protected ContentStoreInternalTests(Func<IAbsFileSystem> createFileSystemFunc, ILogger logger, ITestOutputHelper output = null)
            : base(createFileSystemFunc, logger, output)
        {
            _tempDirectory = new DisposableDirectory(FileSystem);
        }

        protected abstract void CorruptContent(TStore store, ContentHash contentHash);

        protected abstract TStore CreateStore(DisposableDirectory testDirectory);

        async Task TestStore(Context context, Func<TStore, Task> func, IContentChangeAnnouncer announcer = null)
        {
            using (var testDirectory = new DisposableDirectory(FileSystem))
            {
                using (var store = CreateStore(testDirectory))
                {
                    if (announcer != null)
                    {
                        store.Announcer = announcer;
                    }

                    store.Should().NotBeNull();
                    try
                    {
                        await store.StartupAsync(context).ShouldBeSuccess();
                        await func(store);
                    }
                    finally
                    {
                        await store.ShutdownAsync(context).ShouldBeSuccess();
                    }
                }
            }
        }

        private Task TestStoreWithRandomContent(Context context, Func<TStore, ContentHash, byte[], Task> func)
        {
            return TestStore(context, async store =>
            {
                byte[] bytes = ThreadSafeRandom.GetBytes(ValueSize);
                ContentHash contentHash;
                using (var memoryStream = new MemoryStream(bytes))
                {
                    var result = await store.PutStreamAsync(context, memoryStream, ContentHashType);
                    contentHash = result.ContentHash;
                    Assert.Equal(bytes.Length, result.ContentSize);
                }

                await func(store, contentHash, bytes);
            });
        }

        [Fact]
        public Task ContentMiss()
        {
            var context = new Context(Logger);
            return TestStore(context, async store =>
            {
                var result = await store.OpenStreamAsync(context, ContentHash.Random());
                Assert.Null(result.Stream);
            });
        }

        [Fact]
        public Task PutStreamWithNewContent()
        {
            var context = new Context(Logger);
            return TestStore(context, async store =>
            {
                byte[] bytes = ThreadSafeRandom.GetBytes(ValueSize);
                ContentHash contentHash = bytes.CalculateHash(ContentHashType);

                // Verify content doesn't exist yet in store
                var result = await store.OpenStreamAsync(context, contentHash);
                Assert.Null(result.Stream);

                await TestSuccessfulPutStreamAndGet(store, bytes);
            });
        }

        [Fact]
        public Task GetContentSizeExisting()
        {
            var context = new Context(Logger);
            return TestStore(context, async store =>
            {
                byte[] bytes = ThreadSafeRandom.GetBytes(ValueSize);
                ContentHash contentHash = bytes.CalculateHash(ContentHashType);

                using (var stream = new MemoryStream(bytes))
                {
                    await store.PutStreamAsync(context, stream, contentHash).ShouldBeSuccess();
                }

                var r = await store.GetContentSizeAndCheckPinnedAsync(context, contentHash);
                r.Size.Should().Be(bytes.Length);
            });
        }

        [Fact]
        public Task GetContentSizePins()
        {
            var context = new Context(Logger);
            return TestStore(context, async store =>
            {
                byte[] bytes = ThreadSafeRandom.GetBytes(ValueSize);
                ContentHash contentHash = bytes.CalculateHash(ContentHashType);

                using (var pinContext = store.CreatePinContext())
                {
                    using (var stream = new MemoryStream(bytes))
                    {
                        await store.PutStreamAsync(context, stream, contentHash, new PinRequest(pinContext)).ShouldBeSuccess();
                    }

                    var r = await store.GetContentSizeAndCheckPinnedAsync(context, contentHash);
                    r.WasPinned.Should().BeTrue();

                    await pinContext.DisposeAsync();
                }
            });
        }

        [Fact]
        public async Task PutAnnouncesAdd()
        {
            var context = new Context(Logger);
            var mockAnnouncer = new TestContentChangeAnnouncer();

            await TestStore(
                context,
                async store =>
                {
                    store.Announcer.Should().NotBeNull();
                    using (var stream = new MemoryStream(ThreadSafeRandom.GetBytes(ValueSize)))
                    {
                        await store.PutStreamAsync(context, stream, ContentHashType).ShouldBeSuccess();
                    }
                },
                announcer: mockAnnouncer
                );

            mockAnnouncer.contentAddedCalled.Should().BeTrue();
        }

        [Fact]
        public Task PutStreamOverwritesExistingContent()
        {
            var context = new Context(Logger);
            return TestStoreWithRandomContent(
                context, async (store, contentHash, bytes) =>
                {
                    await TestSuccessfulPutStreamAndGet(store, bytes);
                });
        }

        private static async Task RunDifferentRealizationModes(Func<FileRealizationMode, Task> testAction)
        {
            await testAction(FileRealizationMode.Any);
            await testAction(FileRealizationMode.Copy);
            await testAction(FileRealizationMode.HardLink);
        }

        [Fact]
        public Task PutFileWithNewContent()
        {
            var context = new Context(Logger);
            return RunDifferentRealizationModes(async ingressMode =>
            {
                await TestStore(context, async store =>
                {
                    byte[] bytes = ThreadSafeRandom.GetBytes(ValueSize);
                    ContentHash contentHash = bytes.CalculateHash(ContentHashType);

                    // Verify content doesn't exist yet in store
                    var result = await store.OpenStreamAsync(context, contentHash);
                    Assert.Null(result.Stream);
                    await TestSuccessfulPutAbsolutePathAndGet(store, bytes, ingressMode);
                });
            });
        }

        [Fact]
        public Task PutFileOverwritesExistingContent()
        {
            var context = new Context(Logger);
            return RunDifferentRealizationModes(async ingressMode =>
            {
                await TestStoreWithRandomContent(
                    context,
                    async (store, contentHash, bytes) =>
                    {
                        await TestSuccessfulPutAbsolutePathAndGet(store, bytes, ingressMode);
                    });
            });
        }

        [Fact]
        public Task PlaceMiss()
        {
            var context = new Context(Logger);
            return TestStore(context, async store =>
            {
                ContentHash contentHash = ContentHash.Random();
                AbsolutePath tempPath = _tempDirectory.CreateRandomFileName();
                var result = await store.PlaceFileAsync(
                    context, contentHash, tempPath, FileAccessMode.ReadOnly, FileReplacementMode.FailIfExists, FileRealizationMode.Any);
                result.Code.Should().Be(PlaceFileResult.ResultCode.NotPlacedContentNotFound);
            });
        }

        [Fact]
        public Task PlaceHit()
        {
            var context = new Context(Logger);
            return TestStoreWithRandomContent(context, async (store, contentHash, bytes) =>
            {
                AbsolutePath tempPath = _tempDirectory.CreateRandomFileName();
                await store.PlaceFileAsync(
                    context, contentHash, tempPath, FileAccessMode.ReadOnly, FileReplacementMode.FailIfExists, FileRealizationMode.Any).ShouldBeSuccess();
                Assert.True(FileSystem.ReadAllBytes(tempPath).SequenceEqual(bytes));
            });
        }

        [Fact]
        public Task PlaceHitSkipIfExistsSkipsExistingFile()
        {
            var context = new Context(Logger);
            return TestStoreWithRandomContent(context, async (store, contentHash, bytes) =>
            {
                AbsolutePath tempPath = _tempDirectory.CreateRandomFileName();
                FileSystem.WriteAllBytes(tempPath, new byte[0]);
                var result = await store.PlaceFileAsync(
                    context, contentHash, tempPath, FileAccessMode.ReadOnly, FileReplacementMode.SkipIfExists, FileRealizationMode.Any);
                result.Code.Should().Be(PlaceFileResult.ResultCode.NotPlacedAlreadyExists);
                Assert.False(FileSystem.ReadAllBytes(tempPath).SequenceEqual(bytes));
            });
        }

        [Fact]
        public Task PlaceHitSkipIfExists()
        {
            var context = new Context(Logger);
            return TestStoreWithRandomContent(context, async (store, contentHash, bytes) =>
            {
                AbsolutePath tempPath = _tempDirectory.CreateRandomFileName();
                var result = await store.PlaceFileAsync(
                    context, contentHash, tempPath, FileAccessMode.ReadOnly, FileReplacementMode.SkipIfExists, FileRealizationMode.Any);
                result.Code.Should().Be(PlaceFileResult.ResultCode.PlacedWithHardLink);
                Assert.True(FileSystem.ReadAllBytes(tempPath).SequenceEqual(bytes));
            });
        }

        [Fact]
        public Task PlaceReadOnly()
        {
            var context = new Context(Logger);
            return TestStoreWithRandomContent(context, async (store, contentHash, bytes) =>
            {
                AbsolutePath tempPath = _tempDirectory.CreateRandomFileName();
                await store.PlaceFileAsync(
                    context, contentHash, tempPath, FileAccessMode.ReadOnly, FileReplacementMode.FailIfExists, FileRealizationMode.Copy).ShouldBeSuccess();

                Assert.False(FileSystem.GetFileAttributes(tempPath).HasFlag(FileAttributes.ReadOnly));

                Action a = () => FileSystem.WriteAllBytes(tempPath, new byte[] {1});
                a.Should().Throw<UnauthorizedAccessException>();

                Assert.True(FileSystem.ReadAllBytes(tempPath).SequenceEqual(bytes));
            });
        }

        [Fact]
        public Task PlaceWritable()
        {
            var context = new Context(Logger);
            return TestStoreWithRandomContent(context, async (store, contentHash, bytes) =>
            {
                AbsolutePath tempPath = _tempDirectory.CreateRandomFileName();
                await store.PlaceFileAsync(
                    context, contentHash, tempPath, FileAccessMode.Write, FileReplacementMode.FailIfExists, FileRealizationMode.Any).ShouldBeSuccess();

                Assert.False(FileSystem.GetFileAttributes(tempPath).HasFlag(FileAttributes.ReadOnly));
                Assert.False(FileSystem.GetFileAttributes(tempPath).HasFlag(FileAttributes.ReparsePoint));

                Assert.True(FileSystem.ReadAllBytes(tempPath).SequenceEqual(bytes));

                FileSystem.WriteAllBytes(tempPath, new byte[] {1});
            });
        }

        [Fact]
        public Task PlaceCopyWritable()
        {
            var context = new Context(Logger);
            return TestStoreWithRandomContent(context, async (store, contentHash, bytes) =>
            {
                AbsolutePath tempPath = _tempDirectory.CreateRandomFileName();
                await store.PlaceFileAsync(
                    context, contentHash, tempPath, FileAccessMode.Write, FileReplacementMode.FailIfExists, FileRealizationMode.Copy).ShouldBeSuccess();
                FileSystem.WriteAllBytes(tempPath, new byte[] {1});
            });
        }

        [Fact]
        public Task PlaceCopyIfFileExists()
        {
            var context = new Context(Logger);
            return TestStoreWithRandomContent(context, async (store, contentHash, bytes) =>
            {
                AbsolutePath tempPath = _tempDirectory.CreateRandomFileName();

                byte[] canaryBytes = {1, 2, 3, 4};
                FileSystem.WriteAllBytes(tempPath, canaryBytes);

                var result = await store.PlaceFileAsync(
                    context, contentHash, tempPath, FileAccessMode.ReadOnly, FileReplacementMode.FailIfExists, FileRealizationMode.Copy);
                result.Code.Should().Be(PlaceFileResult.ResultCode.NotPlacedAlreadyExists);
                Assert.True(canaryBytes.SequenceEqual(FileSystem.ReadAllBytes(tempPath)));
            });
        }

        [Fact]
        public Task PlaceCopyNoVerifyCopiesFileWithoutHashing()
        {
            var context = new Context(Logger);
            return TestStoreWithRandomContent(context, async (store, contentHash, bytes) =>
            {
                AbsolutePath tempPath = _tempDirectory.CreateRandomFileName();

                var result = await store.PlaceFileAsync(
                    context, contentHash, tempPath, FileAccessMode.ReadOnly, FileReplacementMode.FailIfExists, FileRealizationMode.CopyNoVerify);
                result.Code.Should().Be(PlaceFileResult.ResultCode.PlacedWithCopy);
                Assert.True(FileSystem.ReadAllBytes(tempPath).SequenceEqual(bytes));
            });
        }

        [Fact]
        public Task PlaceCopyNoVerifyFailsIfFileExistsWithoutHashing()
        {
            var context = new Context(Logger);
            return TestStoreWithRandomContent(context, async (store, contentHash, bytes) =>
            {
                AbsolutePath tempPath = _tempDirectory.CreateRandomFileName();

                byte[] canaryBytes = {1, 2, 3, 4};
                FileSystem.WriteAllBytes(tempPath, canaryBytes);
                var result = await store.PlaceFileAsync(
                    context, contentHash, tempPath, FileAccessMode.ReadOnly, FileReplacementMode.FailIfExists, FileRealizationMode.CopyNoVerify);
                result.Code.Should().Be(PlaceFileResult.ResultCode.NotPlacedAlreadyExists);
                Assert.True(FileSystem.ReadAllBytes(tempPath).SequenceEqual(canaryBytes));
            });
        }

        [Fact]
        public Task PlaceCopyNoVerifyOverwritesDestinationFileWithoutHashing()
        {
            var context = new Context(Logger);
            return TestStoreWithRandomContent(context, async (store, contentHash, bytes) =>
            {
                AbsolutePath tempPath = _tempDirectory.CreateRandomFileName();

                byte[] canaryBytes = {1, 2, 3, 4};
                FileSystem.WriteAllBytes(tempPath, canaryBytes);
                var result = await store.PlaceFileAsync(
                    context, contentHash, tempPath, FileAccessMode.ReadOnly, FileReplacementMode.ReplaceExisting, FileRealizationMode.CopyNoVerify);
                result.Code.Should().Be(PlaceFileResult.ResultCode.PlacedWithCopy);
                Assert.True(FileSystem.ReadAllBytes(tempPath).SequenceEqual(bytes));
            });
        }

        [Fact]
        public Task PlaceHardLinkIfFileExists()
        {
            var context = new Context(Logger);
            return TestStoreWithRandomContent(context, async (store, contentHash, bytes) =>
            {
                AbsolutePath tempPath = _tempDirectory.CreateRandomFileName();

                byte[] canaryBytes = {1, 2, 3, 4};
                FileSystem.WriteAllBytes(tempPath, canaryBytes);

                var result = await store.PlaceFileAsync(
                    context, contentHash, tempPath, FileAccessMode.ReadOnly, FileReplacementMode.FailIfExists, FileRealizationMode.HardLink);
                result.Code.Should().Be(PlaceFileResult.ResultCode.NotPlacedAlreadyExists);
                Assert.True(canaryBytes.SequenceEqual(FileSystem.ReadAllBytes(tempPath)));
            });
        }

        [Fact]
        public Task PlaceCopyOverwrite()
        {
            var context = new Context(Logger);
            return TestStoreWithRandomContent(context, async (store, contentHash, bytes) =>
            {
                AbsolutePath tempPath = _tempDirectory.CreateRandomFileName();

                FileSystem.WriteAllBytes(tempPath, new byte[] {1, 2, 3, 4});

                await store.PlaceFileAsync(
                    context, contentHash, tempPath, FileAccessMode.ReadOnly, FileReplacementMode.ReplaceExisting, FileRealizationMode.Copy).ShouldBeSuccess();

                Assert.True(bytes.SequenceEqual(FileSystem.ReadAllBytes(tempPath)));
            });
        }

        [Fact]
        public Task PlaceCopyOverwriteReadOnly()
        {
            var context = new Context(Logger);
            return TestStoreWithRandomContent(context, async (store, contentHash, bytes) =>
            {
                AbsolutePath tempPath = _tempDirectory.CreateRandomFileName();

                FileSystem.WriteAllBytes(tempPath, new byte[] {1, 2, 3, 4});
                FileSystem.SetFileAttributes(tempPath, FileAttributes.ReadOnly);

                await store.PlaceFileAsync(
                    context, contentHash, tempPath, FileAccessMode.ReadOnly, FileReplacementMode.ReplaceExisting, FileRealizationMode.Copy).ShouldBeSuccess();

                Assert.True(bytes.SequenceEqual(FileSystem.ReadAllBytes(tempPath)));
            });
        }

        [Fact]
        public Task PlaceCopyOverwriteDenyWrite()
        {
            var context = new Context(Logger);
            return TestStoreWithRandomContent(context, async (store, contentHash, bytes) =>
            {
                AbsolutePath tempPath = _tempDirectory.CreateRandomFileName();

                FileSystem.WriteAllBytes(tempPath, new byte[] {1, 2, 3, 4});
                FileSystem.DenyFileWrites(tempPath);

                await store.PlaceFileAsync(
                    context, contentHash, tempPath, FileAccessMode.ReadOnly, FileReplacementMode.ReplaceExisting, FileRealizationMode.Copy).ShouldBeSuccess();

                Assert.True(bytes.SequenceEqual(FileSystem.ReadAllBytes(tempPath)));
            });
        }

        [Fact]
        public Task PlaceHardLinkOverwrite()
        {
            var context = new Context(Logger);
            return TestStoreWithRandomContent(context, async (store, contentHash, bytes) =>
            {
                AbsolutePath tempPath = _tempDirectory.CreateRandomFileName();

                FileSystem.WriteAllBytes(tempPath, new byte[] {1, 2, 3, 4});

                await store.PlaceFileAsync(
                    context, contentHash, tempPath, FileAccessMode.ReadOnly, FileReplacementMode.ReplaceExisting, FileRealizationMode.HardLink).ShouldBeSuccess();

                Assert.True(bytes.SequenceEqual(FileSystem.ReadAllBytes(tempPath)));
            });
        }

        private IEnumerable<Tuple<FileRealizationMode, FileAccessMode>> GetModes()
        {
            var modes = new[] {FileRealizationMode.Copy, FileRealizationMode.HardLink};
            var accesses = new[] {FileAccessMode.ReadOnly, FileAccessMode.Write};

            foreach (FileRealizationMode mode in modes)
            {
                foreach (FileAccessMode access in accesses)
                {
                    if (mode == FileRealizationMode.HardLink && access == FileAccessMode.Write)
                    {
                        continue;
                    }

                    yield return new Tuple<FileRealizationMode, FileAccessMode>(mode, access);
                }
            }
        }

        [Fact]
        public async Task OverwriteTests()
        {
            foreach (Tuple<FileRealizationMode, FileAccessMode> firstMode in GetModes())
            {
                foreach (Tuple<FileRealizationMode, FileAccessMode> secondMode in GetModes())
                {
                    try
                    {
                        await OverwriteTestsHelper(firstMode.Item2, firstMode.Item1, secondMode.Item2, secondMode.Item1);
                    }
                    catch (Exception original)
                    {
                        throw new Exception(
                            string.Format(
                                CultureInfo.InvariantCulture,
                                "Exception with {0}:{1} then {2}:{3}: {4}",
                                firstMode.Item2,
                                firstMode.Item1,
                                secondMode.Item2,
                                secondMode.Item1,
                                original),
                            original);
                    }
                }
            }
        }

        private async Task OverwriteTestWrite(
            TStore store,
            Context context,
            AbsolutePath tempPath,
            ContentHash contentHash,
            byte[] bytes,
            FileAccessMode access,
            FileRealizationMode mode)
        {
            AbsolutePath cacheContentCheckPath1 = _tempDirectory.CreateRandomFileName();

            // Ensure cache still has good content
            await store.PlaceFileAsync(
                context,
                contentHash,
                cacheContentCheckPath1,
                FileAccessMode.ReadOnly,
                FileReplacementMode.ReplaceExisting,
                FileRealizationMode.HardLink).ShouldBeSuccess();
            Assert.True(bytes.SequenceEqual(FileSystem.ReadAllBytes(cacheContentCheckPath1)));

            // Run the test
            await store.PlaceFileAsync(context, contentHash, tempPath, access, FileReplacementMode.ReplaceExisting, mode).ShouldBeSuccess();

            Action write = () => FileSystem.WriteAllBytes(tempPath, new byte[] {0});

            // Tamper with the placed file if writeable
            if (access == FileAccessMode.Write)
            {
                write();
            }
            else
            {
                write.Should().Throw<UnauthorizedAccessException>();
            }

            // Ensure cache still has good content
            AbsolutePath cacheContentCheckPath2 = _tempDirectory.CreateRandomFileName();
            await store.PlaceFileAsync(
                context,
                contentHash,
                cacheContentCheckPath2,
                FileAccessMode.ReadOnly,
                FileReplacementMode.ReplaceExisting,
                FileRealizationMode.HardLink).ShouldBeSuccess();
            Assert.True(bytes.SequenceEqual(FileSystem.ReadAllBytes(cacheContentCheckPath2)));
        }

        private Task OverwriteTestsHelper(
            FileAccessMode firstAccess, FileRealizationMode firstMode, FileAccessMode secondAccess, FileRealizationMode secondMode)
        {
            var context = new Context(Logger);
            return TestStoreWithRandomContent(context, async (store, contentHash, bytes) =>
            {
                AbsolutePath tempPath = _tempDirectory.CreateRandomFileName();

                await OverwriteTestWrite(store, context, tempPath, contentHash, bytes, firstAccess, firstMode);

                await OverwriteTestWrite(store, context, tempPath, contentHash, bytes, secondAccess, secondMode);
            });
        }

        [Fact]
        public Task PlaceHardLinkOverwriteReadOnly()
        {
            var context = new Context(Logger);
            return TestStoreWithRandomContent(context, async (store, contentHash, bytes) =>
            {
                AbsolutePath tempPath = _tempDirectory.CreateRandomFileName();

                FileSystem.WriteAllBytes(tempPath, new byte[] {1, 2, 3, 4});
                FileSystem.SetFileAttributes(tempPath, FileAttributes.ReadOnly);

                await store.PlaceFileAsync(
                    context, contentHash, tempPath, FileAccessMode.ReadOnly, FileReplacementMode.ReplaceExisting, FileRealizationMode.HardLink).ShouldBeSuccess();

                Assert.True(bytes.SequenceEqual(FileSystem.ReadAllBytes(tempPath)));
            });
        }

        [Fact]
        public Task PlaceHardLinkOverwriteDenyWrite()
        {
            var context = new Context(Logger);
            return TestStoreWithRandomContent(context, async (store, contentHash, bytes) =>
            {
                AbsolutePath tempPath = _tempDirectory.CreateRandomFileName();

                FileSystem.WriteAllBytes(tempPath, new byte[] {1, 2, 3, 4});
                FileSystem.DenyFileWrites(tempPath);

                await store.PlaceFileAsync(
                    context, contentHash, tempPath, FileAccessMode.ReadOnly, FileReplacementMode.ReplaceExisting, FileRealizationMode.HardLink).ShouldBeSuccess();

                Assert.True(bytes.SequenceEqual(FileSystem.ReadAllBytes(tempPath)));
            });
        }

        [Fact]
        public Task IncorrectContentHash()
        {
            var context = new Context(Logger);
            return TestStoreWithRandomContent(context, async (store, contentHash, bytes) =>
            {
                AbsolutePath tempPath = _tempDirectory.CreateRandomFileName();

                CorruptContent(store, contentHash);

                var result = await store.PlaceFileAsync(
                    context, contentHash, tempPath, FileAccessMode.ReadOnly, FileReplacementMode.ReplaceExisting, FileRealizationMode.Copy);

                result.Code.Should().Be(PlaceFileResult.ResultCode.Error);
            });
        }

        private async Task RunTestWithMaximumHardLinks(Func<TStore, ContentHash, byte[], Task> testAction)
        {
            var context = new Context(Logger);
            await TestStoreWithRandomContent(context, async (store, contentHash, bytes) =>
            {
                for (var i = 0; i < 1022; i++)
                {
                    AbsolutePath tempPath = _tempDirectory.CreateRandomFileName();
                    var result = await store.PlaceFileAsync(
                        context, contentHash, tempPath, FileAccessMode.ReadOnly, FileReplacementMode.FailIfExists, FileRealizationMode.HardLink);
                    result.Code.Should().Be(PlaceFileResult.ResultCode.PlacedWithHardLink);
                }

                await testAction(store, contentHash, bytes);
            });

            await TestStoreWithRandomContent(context, async (store, contentHash, bytes) =>
            {
                for (var i = 0; i < 2048; i++)
                {
                    AbsolutePath tempPath = _tempDirectory.CreateRandomFileName();
                    var result = await store.PlaceFileAsync(
                        context, contentHash, tempPath, FileAccessMode.ReadOnly, FileReplacementMode.FailIfExists, FileRealizationMode.HardLink);
                    result.Code.Should().Be(PlaceFileResult.ResultCode.PlacedWithHardLink);
                }

                await testAction(store, contentHash, bytes);
            });
        }

        [Fact]
        public Task PutViaCopyWithHardLinksAlreadyAtMaximum()
        {
            return RunTestWithMaximumHardLinks(async (store, contentHash, bytes) =>
            {
                AbsolutePath newContentPath = _tempDirectory.CreateRandomFileName();
                FileSystem.WriteAllBytes(newContentPath, bytes);

                var context = new Context(Logger);
                await store.PutFileAsync(context, newContentPath, FileRealizationMode.Copy, ContentHashType).ShouldBeSuccess();
            });
        }

        [Fact]
        public Task PutViaHardLinkWithHardLinksAlreadyAtMaximum()
        {
            return RunTestWithMaximumHardLinks(async (store, contentHash, bytes) =>
            {
                AbsolutePath newContentPath = _tempDirectory.CreateRandomFileName();
                FileSystem.WriteAllBytes(newContentPath, bytes);

                var context = new Context(Logger);
                await store.PutFileAsync(context, newContentPath, FileRealizationMode.HardLink, ContentHashType).ShouldBeSuccess();
            });
        }

        [Fact]
        public Task PutViaAnyWithHardLinksAlreadyAtMaximum()
        {
            return RunTestWithMaximumHardLinks(async (store, contentHash, bytes) =>
            {
                AbsolutePath newContentPath = _tempDirectory.CreateRandomFileName();
                FileSystem.WriteAllBytes(newContentPath, bytes);

                var context = new Context(Logger);
                await store.PutFileAsync(context, newContentPath, FileRealizationMode.Any, ContentHashType).ShouldBeSuccess();
            });
        }

        [Fact]
        public Task PlaceViaCopyWithHardLinksAlreadyAtMaximum()
        {
            var context = new Context(Logger);
            return RunTestWithMaximumHardLinks(async (store, contentHash, bytes) =>
            {
                AbsolutePath newContentPath = _tempDirectory.CreateRandomFileName();

                var result = await store.PlaceFileAsync(
                    context, contentHash, newContentPath, FileAccessMode.ReadOnly, FileReplacementMode.FailIfExists, FileRealizationMode.Copy);
                result.Code.Should().Be(PlaceFileResult.ResultCode.PlacedWithCopy);
            });
        }

        [Fact]
        public Task PlaceViaHardLinkWithHardLinksAlreadyAtMaximum()
        {
            var context = new Context(Logger);
            return RunTestWithMaximumHardLinks(async (store, contentHash, bytes) =>
            {
                AbsolutePath newContentPath = _tempDirectory.CreateRandomFileName();

                var result = await store.PlaceFileAsync(
                    context, contentHash, newContentPath, FileAccessMode.ReadOnly, FileReplacementMode.FailIfExists, FileRealizationMode.HardLink);
                result.Code.Should().Be(PlaceFileResult.ResultCode.PlacedWithHardLink);
            });
        }

        [Fact]
        public Task PlaceViaAnyWithHardLinksAlreadyAtMaximum()
        {
            var context = new Context(Logger);
            return RunTestWithMaximumHardLinks(async (store, contentHash, bytes) =>
            {
                AbsolutePath newContentPath = _tempDirectory.CreateRandomFileName();

                var result = await store.PlaceFileAsync(
                    context, contentHash, newContentPath, FileAccessMode.ReadOnly, FileReplacementMode.FailIfExists, FileRealizationMode.Any);
                result.Code.Should().Be(PlaceFileResult.ResultCode.PlacedWithHardLink);
            });
        }

        private async Task TestSuccessfulPutStreamAndGet(IContentStoreInternal store, byte[] content)
        {
            ContentHash actualContentHash = content.CalculateHash(ContentHashType);

            using (var contentStream = new MemoryStream(content))
            {
                var context = new Context(Logger);
                var result = await store.PutStreamAsync(context, contentStream, actualContentHash);
                ContentHash hashFromPut = result.ContentHash;
                long sizeFromPut = result.ContentSize;

                Assert.True(actualContentHash.Equals(hashFromPut));
                Assert.Equal(content.Length, sizeFromPut);

                // Get the content from the store
                using (var pinContext = store.CreatePinContext())
                {
                    var r2 = await store.PinAsync(context, hashFromPut, pinContext);
                    r2.ShouldBeSuccess();
                    var r3 = await store.OpenStreamAsync(context, hashFromPut);
                    r3.ShouldBeSuccess();
                    byte[] bytes = await r3.Stream.GetBytes();
                    Assert.True(content.SequenceEqual(bytes));
                }
            }
        }

        private async Task TestSuccessfulPutAbsolutePathAndGet(IContentStoreInternal store, byte[] content, FileRealizationMode ingressModes)
        {
            using (var tempDirectory = new DisposableDirectory(FileSystem))
            {
                AbsolutePath pathToFile = CreateRandomFile(tempDirectory.Path, content);

                using (var contentStream = new MemoryStream(content))
                {
                    ContentHash actualContentHash = await contentStream.CalculateHashAsync(ContentHashType);

                    var context = new Context(Logger);
                    var result = await store.PutFileAsync(context, pathToFile, ingressModes, ContentHashType);
                    ContentHash hashFromPut = result.ContentHash;
                    long sizeFromPut = result.ContentSize;

                    Assert.True(actualContentHash.Equals(hashFromPut));
                    Assert.Equal(content.Length, sizeFromPut);

                    // Get the content from the store
                    using (var pinContext = store.CreatePinContext())
                    {
                        var r2 = await store.PinAsync(context, hashFromPut, pinContext);
                        r2.ShouldBeSuccess();
                        var r3 = await store.OpenStreamAsync(context, hashFromPut);
                        var bytes = await r3.Stream.GetBytes();
                        Assert.True(content.SequenceEqual(bytes));
                    }
                }
            }
        }

        [Fact]
        public Task EnumerateContentInfo()
        {
            var context = new Context(Logger);
            return TestStore(context, async store =>
            {
                const int fileCount = 10;
                int byteCount = await CreateRandomStore(store, context, fileCount);

                var contentInfoList = await store.EnumerateContentInfoAsync();
                contentInfoList.Count.Should().Be(fileCount);
                contentInfoList.Select(x => x.Size).Sum().Should().Be(byteCount);
            });
        }

        [Fact]
        public Task EnumerateContentInfoEmptyStore()
        {
            var context = new Context(Logger);
            return TestStore(context, async store =>
            {
                var contentInfoList = await store.EnumerateContentInfoAsync();
                contentInfoList.Count.Should().Be(0);
            });
        }

        private static async Task<int> CreateRandomStore(TStore store, Context context, int fileCount)
        {
            var byteCount = 0;

            foreach (var i in Enumerable.Range(0, fileCount))
            {
                var bytes = Encoding.UTF8.GetBytes(i.ToString(CultureInfo.InvariantCulture));
                byteCount += bytes.Length;

                using (var memoryStream = new MemoryStream(bytes))
                {
                    var putResult = await store.PutStreamAsync(context, memoryStream, ContentHashType);
                    putResult.Succeeded.Should().BeTrue();
                }
            }

            return byteCount;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _tempDirectory.Dispose();
            }

            base.Dispose(disposing);
        }

        private AbsolutePath CreateRandomFile(AbsolutePath directory, byte[] bytes)
        {
            AbsolutePath absolutePathToFile = directory / GetRandomFileName();
            FileSystem.WriteAllBytes(absolutePathToFile, bytes);

            return absolutePathToFile;
        }

        private class TestContentChangeAnnouncer : IContentChangeAnnouncer
        {
            public bool contentAddedCalled { get; private set; } = false;

            public Task ContentAdded(ContentHashWithSize item)
            {
                contentAddedCalled = true;
                return Task.FromResult(0);
            }

            public Task ContentEvicted(ContentHashWithSize item)
            {
                return Task.FromResult(0);
            }
        }
    }
}
