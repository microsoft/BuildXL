// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Sessions;
using ContentStoreTest.Test;
using Xunit;

namespace ContentStoreTest.Sessions
{
    // ReSharper disable All
    public class StreamPathContentSessionTests : ContentSessionTests
    {
        private const string RootOfContentStoreForStream = "streams";
        private const string RootOfContentStoreForPath = "paths";

        public StreamPathContentSessionTests()
            : base(() => new PassThroughFileSystem(TestGlobal.Logger), TestGlobal.Logger)
        {
        }

        /// <inheritdoc />
        protected override IContentStore CreateStore(DisposableDirectory testDirectory, ContentStoreConfiguration configuration)
        {
            var rootPath = testDirectory.Path;
            var configurationModel = new ConfigurationModel(configuration);
            return new StreamPathContentStore(
                () => new FileSystemContentStore(FileSystem, SystemClock.Instance, rootPath / RootOfContentStoreForStream, configurationModel),
                () => new FileSystemContentStore(FileSystem, SystemClock.Instance, rootPath / RootOfContentStoreForPath, configurationModel));
        }

        [Fact]
        public Task PinExistingInStreamSession()
        {
            return RunTestAsync(ImplicitPin.None, null, async (context, session) =>
            {
                var putResult = await session.PutRandomAsync(context, ContentHashType, false, ContentByteCount, Token);
                ResultTestExtensions.ShouldBeSuccess((BoolResult) putResult);

                var result = await session.PinAsync(context, putResult.ContentHash, Token);
                result.ShouldBeSuccess();
            });
        }

        [Fact]
        public Task PinExistingInPathSession()
        {
            return RunTestAsync(ImplicitPin.None, null, async (context, session) =>
            {
                var putResult =
                    await
                        session.PutRandomFileAsync(context, FileSystem, ContentHashType, false, ContentByteCount, Token);
                ResultTestExtensions.ShouldBeSuccess((BoolResult) putResult);

                var result = await session.PinAsync(context, putResult.ContentHash, Token);
                result.ShouldBeSuccess();
            });
        }

        [Fact]
        public Task OpenStreamExistingFromStreamSession()
        {
            return RunTestAsync(ImplicitPin.None, null, async (context, session) =>
            {
                var putResult = await session.PutRandomAsync(
                    context, ContentHashType, false, ContentByteCount, Token);
                ResultTestExtensions.ShouldBeSuccess((BoolResult) putResult);

                var result = await session.OpenStreamAsync(context, putResult.ContentHash, Token);
                result.ShouldBeSuccess();
                Assert.NotNull(result.Stream);
                Assert.Equal(ContentByteCount, result.Stream.Length);
                result.Stream.Dispose();
            });
        }

        [Fact]
        public Task OpenStreamExistingFromPathSession()
        {
            return RunTestAsync(ImplicitPin.None, null, async (context, session) =>
            {
                var putResult = await session.PutRandomFileAsync(
                    context, FileSystem, ContentHashType, false, ContentByteCount, Token);
                ResultTestExtensions.ShouldBeSuccess((BoolResult) putResult);

                var result = await session.OpenStreamAsync(context, putResult.ContentHash, Token);
                result.ShouldBeSuccess();
                Assert.NotNull(result.Stream);
                Assert.Equal(ContentByteCount, result.Stream.Length);
                result.Stream.Dispose();
            });
        }

        [Fact]
        public async Task PlaceFileExistingFromStreamSession()
        {
            using (var placeDirectory = new DisposableDirectory(FileSystem))
            {
                var path = placeDirectory.Path / "file.dat";
                await RunTestAsync(ImplicitPin.None, null, async (context, session) =>
                {
                    var putResult = await session.PutRandomAsync(
                        context, ContentHashType, false, ContentByteCount, Token);
                    ResultTestExtensions.ShouldBeSuccess((BoolResult) putResult);

                    var result = await session.PlaceFileAsync(
                        context,
                        putResult.ContentHash,
                        path,
                        FileAccessMode.ReadOnly,
                        FileReplacementMode.ReplaceExisting,
                        FileRealizationMode.Any,
                        Token);
                    Assert.True(result.IsPlaced());
                });
            }
        }

        [Fact]
        public async Task PlaceFileExistingFromPathSession()
        {
            using (var placeDirectory = new DisposableDirectory(FileSystem))
            {
                var path = placeDirectory.Path / "file.dat";
                await RunTestAsync(ImplicitPin.None, null, async (context, session) =>
                {
                    var putResult = await session.PutRandomFileAsync(
                        context, FileSystem, ContentHashType, false, ContentByteCount, Token);
                    ResultTestExtensions.ShouldBeSuccess((BoolResult) putResult);

                    var result = await session.PlaceFileAsync(
                        context,
                        putResult.ContentHash,
                        path,
                        FileAccessMode.ReadOnly,
                        FileReplacementMode.ReplaceExisting,
                        FileRealizationMode.Any,
                        Token);
                    Assert.True(result.IsPlaced());
                });
            }
        }

        [Fact]
        public Task PinMultipleContentsFromMultipleSessions()
        {
            return RunTestAsync(ImplicitPin.None, null, async (context, session) =>
            {
                var putStreamResult = await session.PutRandomAsync(
                    context, ContentHashType, false, ContentByteCount, Token);
                ResultTestExtensions.ShouldBeSuccess((BoolResult) putStreamResult);

                var putFileResult =
                    await
                        session.PutRandomFileAsync(context, FileSystem, ContentHashType, false, ContentByteCount, Token);
                ResultTestExtensions.ShouldBeSuccess((BoolResult) putFileResult);

                var results = await session.PinAsync(context, new[] {putStreamResult.ContentHash, ContentHash.Random(), putFileResult.ContentHash}, Token);

                foreach (var result in results)
                {
                    var r = await result;
                    switch (r.Index)
                    {
                        case 0:
                            r.Item.ShouldBeSuccess();
                            break;
                        case 1:
                            Assert.Equal(PinResult.ResultCode.ContentNotFound, r.Item.Code);
                            break;
                        case 2:
                            r.Item.ShouldBeSuccess();
                            break;
                        default:
                            Assert.False(true);
                            break;
                    }
                }
            });
        }

        [Fact]
        public async Task PlaceMultipleFilesFromMultipleSessions()
        {
            using (var placeDirectory = new DisposableDirectory(FileSystem))
            {
                var pathFromStream = placeDirectory.Path / "file-stream.dat";
                var pathFromPath = placeDirectory.Path / "file-path.dat";
                var pathFromNonexistent = placeDirectory.Path / "file-nonexistent.dat";

                await RunTestAsync(ImplicitPin.None, null, async (context, session) =>
                {
                    var putStreamResult = await session.PutRandomAsync(
                        context, ContentHashType, false, ContentByteCount, Token);
                    ResultTestExtensions.ShouldBeSuccess((BoolResult) putStreamResult);

                    var putFileResult = await session.PutRandomFileAsync(
                        context, FileSystem, ContentHashType, false, ContentByteCount, Token);
                    ResultTestExtensions.ShouldBeSuccess((BoolResult) putFileResult);

                    var hashesWithPaths = new[]
                    {
                        new ContentHashWithPath(putStreamResult.ContentHash, pathFromStream),
                        new ContentHashWithPath(ContentHash.Random(), pathFromNonexistent),
                        new ContentHashWithPath(putFileResult.ContentHash, pathFromPath),
                    };

                    var results = await session.PlaceFileAsync(
                        context,
                        hashesWithPaths,
                        FileAccessMode.ReadOnly,
                        FileReplacementMode.ReplaceExisting,
                        FileRealizationMode.Any,
                        Token);

                    foreach (var result in results)
                    {
                        var r = await result;
                        switch (r.Index)
                        {
                            case 0:
                                Assert.True(r.Item.Succeeded);
                                break;
                            case 1:
                                Assert.Equal(PlaceFileResult.ResultCode.NotPlacedContentNotFound, r.Item.Code);
                                break;
                            case 2:
                                Assert.True(r.Item.Succeeded);
                                break;
                            default:
                                Assert.False(true);
                                break;
                        }
                    }
                });
            }
        }
    }
}
