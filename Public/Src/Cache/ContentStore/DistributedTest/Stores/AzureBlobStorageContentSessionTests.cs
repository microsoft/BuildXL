// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Blobs;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Sessions;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Utils;
using ContentStoreTest.Distributed.Redis;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Test
{
    [Collection("Redis-based tests")]
    [Trait("Category", "WindowsOSOnly")] // 'redis-server' executable no longer exists
    public class AzureBlobStorageContentSessionTests : ContentSessionTests
    {
        private readonly Guid _runId = Guid.NewGuid();
        private readonly LocalRedisFixture _fixture;

        protected override bool RunEvictionBasedTests { get; } = false;

        protected override bool EnablePinContentSizeAssertions { get; } = false;

        public AzureBlobStorageContentSessionTests(LocalRedisFixture fixture, ITestOutputHelper output)
            : base(() => new PassThroughFileSystem(TestGlobal.Logger), TestGlobal.Logger, canHibernate: false, output)
        {
            _fixture = fixture;
        }

        protected override IContentStore CreateStore(
            DisposableDirectory testDirectory,
            ContentStoreConfiguration configuration)
        {
            // This is done on purpose as we need to properly dispose of the Azure Storage emulator
            throw new NotImplementedException();
        }

        protected override async Task RunReadOnlyTestAsync(ImplicitPin implicitPin, Func<Context, IReadOnlyContentSession, Task> funcAsync)
        {
            using var directory = new DisposableDirectory(FileSystem);

            // ReadOnly/ReadWrite implementation is equivalent for the BlobContentStore
            await RunTestAsync(implicitPin, directory, (context, session) => funcAsync(context, session));
        }

        protected override async Task RunTestAsync(
            ImplicitPin implicitPin,
            DisposableDirectory? directory,
            Func<Context, IContentSession, Task> funcAsync)
        {
            var context = new Context(Logger);

            bool useNewDirectory = directory == null;
            if (useNewDirectory)
            {
                directory = new DisposableDirectory(FileSystem);
            }

            try
            {
                using (var storage = CreateBlobContentStore(out var store))
                {
                    try
                    {
                        await store.StartupAsync(context).ShouldBeSuccess();

                        var createResult = store.CreateSession(context, Name, implicitPin).ShouldBeSuccess();
                        using (var session = createResult.Session)
                        {
                            try
                            {
                                Assert.False(session!.StartupStarted);
                                Assert.False(session!.StartupCompleted);
                                Assert.False(session!.ShutdownStarted);
                                Assert.False(session!.ShutdownCompleted);

                                await session!.StartupAsync(context).ShouldBeSuccess();

                                await funcAsync(context, session);
                            }
                            finally
                            {
                                await session!.ShutdownAsync(context).ShouldBeSuccess();
                            }

                            Assert.True(session!.StartupStarted);
                            Assert.True(session!.StartupCompleted);
                            Assert.True(session!.ShutdownStarted);
                            Assert.True(session!.ShutdownCompleted);
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
                    directory!.Dispose();
                }
            }
        }

        [Fact]
        public Task BulkPinManyFiles()
        {
            // This is testing that the store is not sending too many bulk subrequests within a single API call. Mainly
            // important when using the bulk pin strategies
            return RunTestAsync(ImplicitPin.None, null, async (context, session) =>
            {
                var fileCount = 1337;
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
        public Task RepeatedBulkPinShouldSucceedAsync()
        {
            // This is just testing that pinning files repeatedly doesn't have any side effects
            return RunTestAsync(ImplicitPin.None, null, async (context, session) =>
            {
                var fileCount = 5;
                var contentHashes = await session.PutRandomAsync(context, ContentHashType, false, fileCount, ContentByteCount, true);

                {
                    var results = (await session.PinAsync(context, contentHashes, Token)).ToList();
                    Assert.Equal(fileCount, results.Count);
                    foreach (var result in results)
                    {
                        var pinResult = await result;
                        pinResult.Item.ShouldBeSuccess();
                    }
                }

                {
                    var result = await session.PinAsync(context, contentHashes[0], Token);
                    result.ShouldBeSuccess();
                }
            });
        }

        [Fact(Skip = "Used for manual testing of whether the bulk pin logic updates the last access time correctly in Storage")]
        public Task PinSpecificFile()
        {
            OverrideFolderName = "pinSpecificFile";
            return RunTestAsync(ImplicitPin.None, null, async (context, session) =>
            {
                var putResult = await session.PutContentAsync(context, $"hello").ThrowIfFailureAsync();
                var pinResult = await session.PinAsync(context, putResult.ContentHash, Token);
                pinResult.ShouldBeSuccess();

                var pinResults = (await session.PinAsync(context, new [] { putResult.ContentHash }, Token)).ToList();
                pinResult = (await pinResults[0]).Item;
                pinResult.ShouldBeSuccess();
            });
        }

        [Fact]
        public async Task PlaceLargeFileAsync()
        {
            // This test downloads a file in parallel, hence why we check
            using (var placeDirectory = new DisposableDirectory(FileSystem))
            {
                var path = placeDirectory.Path / "file.dat";
                await RunTestAsync(ImplicitPin.None, null, async (context, session) =>
                {
                    var putResult = await session.PutRandomAsync(
                        context, ContentHashType, false, "100MB".ToSize(), Token).ShouldBeSuccess();
                    var result = await session.PlaceFileAsync(
                        context,
                        putResult.ContentHash,
                        path,
                        FileAccessMode.ReadOnly,
                        FileReplacementMode.ReplaceExisting,
                        FileRealizationMode.Any,
                        Token).ShouldBeSuccess();

                    Assert.True(result.IsPlaced());

                    using (var fs = FileSystem.OpenForHashing(path))
                    {
                        (await HashInfoLookup.GetContentHasher(ContentHashType).GetContentHashAsync(fs)).Should().BeEquivalentTo(putResult.ContentHash);
                    }
                });
            }
        }

        internal string? OverrideFolderName { get; set; }

        private IDisposable CreateBlobContentStore(out AzureBlobStorageContentStore store)
        {
            var storage = AzuriteStorageProcess.CreateAndStartEmpty(
                _fixture,
                TestGlobal.Logger);

            var configuration = new AzureBlobStorageContentStoreConfiguration()
            {
                Credentials = new AzureBlobStorageCredentials(storage.ConnectionString),
                FolderName = OverrideFolderName ?? _runId.ToString(),
                // NOTE: bulk pin strategies don't work with the storage emulator, so if you want to test these, you
                // need to hard code a connection string to an actual storage account.
                BulkPinStrategy = AzureBlobStorageContentSession.BulkPinStrategy.Individual,
            };

            store = new AzureBlobStorageContentStore(configuration);

            return storage;
        }
    }
}
