// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Service;
using BuildXL.Cache.ContentStore.Sessions;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Utils;
using BuildXL.Cache.ContentStore.InterfacesTest;
using Xunit;
using Xunit.Abstractions;

namespace BuildXL.Cache.ContentStore.InterfacesTest.Sessions
{
    public abstract class ContentSessionTests : TestWithOutput
    {
        protected const string Name = "name";
        protected const int ContentByteCount = 100;
        protected const HashType ContentHashType = HashType.Vso0;
        protected const long DefaultMaxSize = 1 * 1024 * 1024;
        private static readonly Task CompletedTask = Task.FromResult(0);
        protected static readonly CancellationToken Token = CancellationToken.None;

        protected readonly IAbsFileSystem FileSystem;
        protected readonly ILogger Logger;

        private readonly bool _canHibernate;

        private bool _disposed;

        /// <summary>
        /// Gets a value indicating whether to run tests for bulk methods
        /// </summary>
        /// <remarks>
        /// TODO: Remove when all content stores implement bulk methods (bug 1365340)
        /// </remarks>
        protected virtual bool RunBulkMethodTests => true;

        protected ContentSessionTests(Func<IAbsFileSystem> createFileSystemFunc, ILogger logger, bool canHibernate = true, ITestOutputHelper output = null)
            : base(output)
        {
            FileSystem = createFileSystemFunc();
            Logger = logger;
            _canHibernate = canHibernate;
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            base.Dispose();

            if (_disposed)
            {
                return;
            }

            FileSystem?.Dispose();
            Logger?.Flush();

            _disposed = true;
        }

        protected virtual long MaxSize => DefaultMaxSize;

        protected ContentStoreConfiguration CreateStoreConfiguration()
        {
            return new ContentStoreConfiguration(new MaxSizeQuota($"{MaxSize}"));
        }

        protected abstract IContentStore CreateStore(DisposableDirectory testDirectory, ContentStoreConfiguration configuration);

        [Fact]
        public void EnumNoneValuesAreZero()
        {
            Assert.Equal((FileAccessMode)0, FileAccessMode.None);
            Assert.Equal((FileRealizationMode)0, FileRealizationMode.None);
            Assert.Equal((FileReplacementMode)0, FileReplacementMode.None);
        }

        [Fact]
        public Task Constructor()
        {
            return RunReadOnlyTestAsync(ImplicitPin.None, (context, session) => Task.FromResult(true));
        }

        [Fact]
        public Task PinNonExisting()
        {
            return RunReadOnlyTestAsync(ImplicitPin.None, async (context, session) =>
            {
                var result = await session.PinAsync(context, ContentHash.Random(), Token);
                Assert.Equal(PinResult.ResultCode.ContentNotFound, result.Code);
            });
        }

        [Fact]
        public Task PinExisting()
        {
            return RunTestAsync(ImplicitPin.None, null, async (context, session) =>
            {
                var putResult = await session.PutRandomAsync(context, ContentHashType, false, ContentByteCount, Token).ShouldBeSuccess();
                await session.PinAsync(context, putResult.ContentHash, Token).ShouldBeSuccess();
            });
        }

        [Fact]
        public Task BulkPinExisting()
        {
            if (!RunBulkMethodTests)
            {
                return CompletedTask;
            }

            return RunTestAsync(ImplicitPin.None, null, async (context, session) =>
            {
                var fileCount = 5;
                var contentHashes = await session.PutRandomAsync(context, ContentHashType, false, fileCount, ContentByteCount, true);
                var results = (await session.PinAsync(context, contentHashes, Token)).ToList();
                Assert.Equal(fileCount, results.Count);
                foreach (var result in results)
                {
                    var pinResult = await result;
                    pinResult.Item.ShouldBeSuccess();
                }
            });
        }

        [Fact]
        public Task BulkPinNonExisting()
        {
            if (!RunBulkMethodTests)
            {
                return CompletedTask;
            }

            return RunTestAsync(ImplicitPin.None, null, async (context, session) =>
            {
                var fileCount = 5;
                var randomHashes = Enumerable.Range(0, fileCount).Select(i => ContentHash.Random()).ToList();
                var results = (await session.PinAsync(context, randomHashes, Token)).ToList();
                Assert.Equal(fileCount, results.Count);
                foreach (var result in results)
                {
                    var pinResult = await result;
                    Assert.Equal(PinResult.ResultCode.ContentNotFound, pinResult.Item.Code);
                }
            });
        }

        [Fact]
        public Task BulkPinSomeExisting()
        {
            if (!RunBulkMethodTests)
            {
                return CompletedTask;
            }

            return RunTestAsync(ImplicitPin.None, null, async (context, session) =>
            {
                var fileCount = 3;
                var addedHashes = await session.PutRandomAsync(context, ContentHashType, false, fileCount, ContentByteCount, true);
                var randomHashes = Enumerable.Range(0, fileCount).Select(i => ContentHash.Random());

                // First half are random missing hashes and remaining are actually present
                var hashesToQuery = randomHashes.Concat(addedHashes).ToList();

                var results = (await session.PinAsync(context, hashesToQuery, Token)).ToList();
                Assert.Equal(2 * fileCount, results.Count);
                foreach (var result in results)
                {
                    var pinResult = await result;
                    if (pinResult.Index < fileCount)
                    {
                        Assert.Equal(PinResult.ResultCode.ContentNotFound, pinResult.Item.Code);
                    }
                    else
                    {
                        pinResult.Item.ShouldBeSuccess();
                    }
                }
            });
        }

        [Fact]
        public Task OpenStreamNonExisting()
        {
            return RunReadOnlyTestAsync(ImplicitPin.None, async (context, session) =>
            {
                await session.OpenStreamAsync(context, ContentHash.Random(), Token).ShouldBeNotFound();
            });
        }

        [Fact]
        public Task OpenStreamExisting()
        {
            return RunTestAsync(ImplicitPin.None, null, async (context, session) =>
            {
                var putResult = await session.PutRandomAsync(
                    context, ContentHashType, false, ContentByteCount, Token).ShouldBeSuccess();
                var result = await session.OpenStreamAsync(context, putResult.ContentHash, Token).ShouldBeSuccess();
                Assert.NotNull(result.Stream);
                Assert.Equal(ContentByteCount, result.Stream.Length);
                result.Stream.Dispose();
            });
        }

        [Fact]
        public Task OpenStreamExistingEmpty()
        {
            return RunTestAsync(ImplicitPin.None, null, async (context, session) =>
            {
                using (var stream = new MemoryStream(new byte[0]))
                {
                    var r1 = await session.PutStreamAsync(context, ContentHashType, stream, Token).ShouldBeSuccess();
                    var r2 = await session.OpenStreamAsync(context, r1.ContentHash, Token).ShouldBeSuccess();
                    Assert.NotNull(r2.Stream);
                    r2.Stream.Dispose();
                }
            });
        }

        [Fact]
        public async Task OpenStreamImplicitlyPins()
        {
            using (var directory = new DisposableDirectory(FileSystem))
            {
                // Put some random content into the store in one session.
                var contentHash = ContentHash.Random(ContentHashType);
                await RunTestAsync(ImplicitPin.None, directory, async (context, session) =>
                {
                    var r = await session.PutRandomAsync(context, ContentHashType, false, ContentByteCount, Token).ShouldBeSuccess();
                    contentHash = r.ContentHash;
                });

                await RunTestAsync(ImplicitPin.PutAndGet, directory, async (context, session) =>
                {
                    // Open should pin this previously existing content, preventing eviction.
                    var r = await session.OpenStreamAsync(context, contentHash, Token).ShouldBeSuccess();
                    using (r.Stream)
                    {
                        // This put should fail as the previous open is holding content that cannot be evicted.
                        var pr1 = await session.PutRandomAsync(context, ContentHashType, false, MaxSize, Token).ShouldBeError();
                    }
                });
            }
        }

        [Fact]
        public async Task OpenStreamDoesNotPin()
        {
            using (var directory = new DisposableDirectory(FileSystem))
            {
                // Put some random content into the store in one session.
                var contentHash = ContentHash.Random(ContentHashType);
                await RunTestAsync(ImplicitPin.None, directory, async (context, session) =>
                {
                    var r = await session.PutRandomAsync(context, ContentHashType, false, ContentByteCount, Token).ShouldBeSuccess();
                    contentHash = r.ContentHash;
                });

                await RunTestAsync(ImplicitPin.None, directory, async (context, session) =>
                {
                    // Open should not pin this content, allowing eviction on the next put.
                    var r = await session.OpenStreamAsync(context, contentHash, Token).ShouldBeSuccess();
#pragma warning disable AsyncFixer02
                    r.Stream.Dispose();
#pragma warning restore AsyncFixer02

                    // This put should succeed after the first content is evicted.
                    await session.PutRandomAsync(context, ContentHashType, false, MaxSize, Token).ShouldBeSuccess();
                });
            }
        }

        [Fact]
        public Task PlaceFileNonExisting()
        {
            return RunReadOnlyTestAsync(ImplicitPin.None, async (context, session) =>
            {
                var path = new AbsolutePath(PathGeneratorUtilities.GetAbsolutePath("Z", "nonexist", "file.dat"));
                var result = await session.PlaceFileAsync(
                    context,
                    ContentHash.Random(),
                    path,
                    FileAccessMode.ReadOnly,
                    FileReplacementMode.ReplaceExisting,
                    FileRealizationMode.Any,
                    Token);
                Assert.Equal(PlaceFileResult.ResultCode.NotPlacedContentNotFound, result.Code);
            });
        }

        [Fact]
        public async Task PlaceFileExisting()
        {
            using (var placeDirectory = new DisposableDirectory(FileSystem))
            {
                var path = placeDirectory.Path / "file.dat";
                await RunTestAsync(ImplicitPin.None, null, async (context, session) =>
                {
                    var putResult = await session.PutRandomAsync(
                        context, ContentHashType, false, ContentByteCount, Token).ShouldBeSuccess();
                    var result = await session.PlaceFileAsync(
                        context,
                        putResult.ContentHash,
                        path,
                        FileAccessMode.ReadOnly,
                        FileReplacementMode.ReplaceExisting,
                        FileRealizationMode.Any,
                        Token).ShouldBeSuccess();
                    Assert.True(result.IsPlaced());
                });
            }
        }

        [Fact]
        public async Task PlaceFileExistingReplaces()
        {
            using (var placeDirectory = new DisposableDirectory(FileSystem))
            {
                var path = placeDirectory.Path / "file.dat";
                FileSystem.WriteAllBytes(path, new byte[0]);

                await RunTestAsync(ImplicitPin.None, null, async (context, session) =>
                {
                    var putResult = await session.PutRandomAsync(
                        context, ContentHashType, false, ContentByteCount, Token).ShouldBeSuccess();
                    var result = await session.PlaceFileAsync(
                        context,
                        putResult.ContentHash,
                        path,
                        FileAccessMode.ReadOnly,
                        FileReplacementMode.ReplaceExisting,
                        FileRealizationMode.Any,
                        Token).ShouldBeSuccess();
                    Assert.True(result.IsPlaced());
                });
            }
        }

        [Fact]
        public async Task PlaceFileFailsIfExists()
        {
            using (var placeDirectory = new DisposableDirectory(FileSystem))
            {
                var path = placeDirectory.Path / "file.dat";
                FileSystem.WriteAllBytes(path, new byte[0]);

                await RunTestAsync(ImplicitPin.None, null, async (context, session) =>
                {
                    var putResult = await session.PutRandomAsync(
                        context, ContentHashType, false, ContentByteCount, Token);
                    var result = await session.PlaceFileAsync(
                        context,
                        putResult.ContentHash,
                        path,
                        FileAccessMode.ReadOnly,
                        FileReplacementMode.FailIfExists,
                        FileRealizationMode.Any,
                        Token);
                    Assert.Equal(PlaceFileResult.ResultCode.NotPlacedAlreadyExists, result.Code);
                    Assert.False(result.Succeeded, $"Succeeded to place to path {path}");
                });
            }
        }

        [Fact]
        public async Task PlaceFileSkipsIfExists()
        {
            using (var placeDirectory = new DisposableDirectory(FileSystem))
            {
                var path = placeDirectory.Path / "file.dat";
                FileSystem.WriteAllBytes(path, new byte[0]);

                await RunTestAsync(ImplicitPin.None, null, async (context, session) =>
                {
                    var putResult = await session.PutRandomAsync(
                        context, ContentHashType, false, ContentByteCount, Token);
                    var result = await session.PlaceFileAsync(
                        context,
                        putResult.ContentHash,
                        path,
                        FileAccessMode.ReadOnly,
                        FileReplacementMode.SkipIfExists,
                        FileRealizationMode.Any,
                        Token);
                    Assert.False(result.Succeeded);
                    Assert.Equal(PlaceFileResult.ResultCode.NotPlacedAlreadyExists, result.Code);
                });
            }
        }

        [Fact]
        public async Task PlaceFileImplicitlyPins()
        {
            using (var directory = new DisposableDirectory(FileSystem))
            {
                // Put some random content into the store in one session.
                var contentHash = ContentHash.Random(ContentHashType);
                await RunTestAsync(ImplicitPin.None, directory, async (context, session) =>
                {
                    var r = await session.PutRandomAsync(context, ContentHashType, false, ContentByteCount, Token).ShouldBeSuccess();
                    contentHash = r.ContentHash;
                });

                await RunTestAsync(ImplicitPin.PutAndGet, directory, async (context, session) =>
                {
                    // PlaceFile should pin this previously existing content, preventing eviction.
                    var r = await session.PlaceFileAsync(
                        context,
                        contentHash,
                        directory.CreateRandomFileName(),
                        FileAccessMode.Write,
                        FileReplacementMode.FailIfExists,
                        FileRealizationMode.Any,
                        Token);
                    Assert.True(r.IsPlaced());

                    // This put should fail as the previous open is holding content that cannot be evicted.
                    await session.PutRandomAsync(context, ContentHashType, false, MaxSize, Token).ShouldBeError();
                });
            }
        }

        [Fact]
        public async Task PlaceFileDoesNotPin()
        {
            using (var directory = new DisposableDirectory(FileSystem))
            {
                // Put some random content into the store in one session.
                var contentHash = ContentHash.Random(ContentHashType);
                await RunTestAsync(ImplicitPin.None, directory, async (context, session) =>
                {
                    var r = await session.PutRandomAsync(context, ContentHashType, false, ContentByteCount, Token).ShouldBeSuccess();
                    contentHash = r.ContentHash;
                });

                await RunTestAsync(ImplicitPin.None, directory, async (context, session) =>
                {
                    // PlaceFile should not pin this content, allowing eviction on the next put.
                    var r = await session.PlaceFileAsync(
                        context,
                        contentHash,
                        directory.CreateRandomFileName(),
                        FileAccessMode.Write,
                        FileReplacementMode.FailIfExists,
                        FileRealizationMode.Any,
                        Token);
                    Assert.True(r.IsPlaced(), r.ToString());

                    // This put should succeed after the first content is evicted.
                    await session.PutRandomAsync(context, ContentHashType, false, MaxSize, Token).ShouldBeSuccess();
                });
            }
        }

        [Fact]
        public Task PutFileNonExisting()
        {
            return RunTestAsync(ImplicitPin.None, null, async (context, session) =>
            {
                var path = new AbsolutePath(PathGeneratorUtilities.GetAbsolutePath("Z", "nonexist", "file.dat"));
                await session.PutFileAsync(context, ContentHash.Random(), path, FileRealizationMode.Any, Token).ShouldBeError();
            });
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public Task PutFileImplicitlyPins(bool provideHash)
        {
            return RunTestAsync(ImplicitPin.PutAndGet, null, async (context, session) =>
            {
                var pr1 = await session.PutRandomFileAsync(
                    context, FileSystem, ContentHashType, provideHash, ContentByteCount, Token).ShouldBeSuccess();

                var pr2 = await session.PutRandomFileAsync(
                    context, FileSystem, ContentHashType, provideHash, MaxSize, Token).ShouldBeError();

                var r = await session.OpenStreamAsync(context, pr1.ContentHash, Token).ShouldBeSuccess();
                r.Stream.Dispose();
            });
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public Task PutFileDoesNotPin(bool provideHash)
        {
            return RunTestAsync(ImplicitPin.None, null, async (context, session) =>
            {
                var pr1 = await session.PutRandomFileAsync(
                    context, FileSystem, ContentHashType, provideHash, ContentByteCount, Token).ShouldBeSuccess();

                var pr2 = await session.PutRandomFileAsync(
                    context, FileSystem, ContentHashType, provideHash, MaxSize, Token).ShouldBeSuccess();

                var r = await session.OpenStreamAsync(context, pr1.ContentHash, Token).ShouldBeNotFound();
            });
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public Task PutStreamImplicitlyPins(bool provideHash)
        {
            return RunTestAsync(ImplicitPin.PutAndGet, null, async (context, session) =>
            {
                var pr1 = await session.PutRandomAsync(context, ContentHashType, provideHash, ContentByteCount, Token).ShouldBeSuccess();

                var pr2 = await session.PutRandomAsync(context, ContentHashType, provideHash, MaxSize, Token).ShouldBeError();

                var r = await session.OpenStreamAsync(context, pr1.ContentHash, Token).ShouldBeSuccess();
                r.Stream.Dispose();
            });
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public Task PutStreamDoesNotPin(bool provideHash)
        {
            return RunTestAsync(ImplicitPin.None, null, async (context, session) =>
            {
                var pr1 = await session.PutRandomAsync(context, ContentHashType, provideHash, ContentByteCount, Token).ShouldBeSuccess();

                await session.PutRandomAsync(context, ContentHashType, provideHash, MaxSize, Token).ShouldBeSuccess();

                await session.OpenStreamAsync(context, pr1.ContentHash, Token).ShouldBeNotFound();
            });
        }

        [Fact]
        public Task SessionGivesPinnedContentForHibernation()
        {
            // Puts are not blocked, nor is content evicted, during sensitive sessions.
            if (!_canHibernate)
            {
                return CompletedTask;
            }

            return RunTestAsync(ImplicitPin.PutAndGet, null, async (context, session) =>
            {
                var contentHashes = new HashSet<ContentHash>();
                for (var i = 0; i < 10; i++)
                {
                    var r = await session.PutRandomAsync(context, ContentHashType, false, ContentByteCount, Token);
                    contentHashes.Add(r.ContentHash);
                }

                var hibernateSession = (IHibernateContentSession) session;

                var pinnedContentHashes = hibernateSession.EnumeratePinnedContentHashes().ToHashSet();
                Assert.Equal(contentHashes, pinnedContentHashes);
            });
        }

        [Fact]
        public Task SessionTakesHibernationPins()
        {
            if (!_canHibernate)
            {
                return CompletedTask;
            }

            return RunTestAsync(ImplicitPin.None, null, async (context, session) =>
            {
                var contentHashes = new HashSet<ContentHash>();
                for (var i = 0; i < 10; i++)
                {
                    var r = await session.PutRandomAsync(context, ContentHashType, false, ContentByteCount, Token);
                    contentHashes.Add(r.ContentHash);
                }

                var hibernateSession = (IHibernateContentSession) session;

                var pinnedContentHashes = hibernateSession.EnumeratePinnedContentHashes().ToHashSet();
                Assert.Equal(0, pinnedContentHashes.Count);

                await hibernateSession.PinBulkAsync(context, contentHashes);

                pinnedContentHashes = hibernateSession.EnumeratePinnedContentHashes().ToHashSet();
                Assert.Equal(contentHashes, pinnedContentHashes);
            });
        }

        protected async Task RunReadOnlyTestAsync(ImplicitPin implicitPin, Func<Context, IReadOnlyContentSession, Task> funcAsync)
        {
            var context = new Context(Logger);
            using (var directory = new DisposableDirectory(FileSystem))
            {
                var config = CreateStoreConfiguration();

                using (var store = CreateStore(directory, config))
                {
                    try
                    {
                        await store.StartupAsync(context).ShouldBeSuccess();

                        var createResult = store.CreateReadOnlySession(context, Name, implicitPin).ShouldBeSuccess();
                        using (var session = createResult.Session)
                        {
                            try
                            {
                                await session.StartupAsync(context).ShouldBeSuccess();
                                await funcAsync(context, session);
                            }
                            finally
                            {
                                await session.ShutdownAsync(context).ShouldBeSuccess();
                            }
                        }
                    }
                    finally
                    {
                        await store.ShutdownAsync(context).ShouldBeSuccess();
                    }
                }
            }
        }

        protected async Task RunTestAsync(ImplicitPin implicitPin, DisposableDirectory directory, Func<Context, IContentSession, Task> funcAsync)
        {
            var context = new Context(Logger);

            bool useNewDirectory = directory == null;
            if (useNewDirectory)
            {
                directory = new DisposableDirectory(FileSystem);
            }

            try
            {
                var config = CreateStoreConfiguration();

                using (var store = CreateStore(directory, config))
                {
                    try
                    {
                        await store.StartupAsync(context).ShouldBeSuccess();

                        var createResult = store.CreateSession(context, Name, implicitPin).ShouldBeSuccess();
                        using (var session = createResult.Session)
                        {
                            try
                            {
                                Assert.False(session.StartupStarted);
                                Assert.False(session.StartupCompleted);
                                Assert.False(session.ShutdownStarted);
                                Assert.False(session.ShutdownCompleted);

                                await session.StartupAsync(context).ShouldBeSuccess();

                                await funcAsync(context, session);
                            }
                            finally
                            {
                                await session.ShutdownAsync(context).ShouldBeSuccess();
                            }

                            Assert.True(session.StartupStarted);
                            Assert.True(session.StartupCompleted);
                            Assert.True(session.ShutdownStarted);
                            Assert.True(session.ShutdownCompleted);
                        }
                    }
                    finally
                    {
                        await store.ShutdownAsync(context).ShouldBeSuccess();
                    }
                }
            }
            finally
            {
                if (useNewDirectory)
                {
                    directory.Dispose();
                }
            }
        }
    }
}
