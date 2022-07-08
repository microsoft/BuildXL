// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.Blobs;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
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

        private IDisposable CreateBlobContentStore(out AzureBlobStorageContentStore store)
        {
            var storage = AzuriteStorageProcess.CreateAndStartEmpty(
                _fixture,
                TestGlobal.Logger);

            var configuration = new AzureBlobStorageContentStoreConfiguration()
            {
                Credentials = new AzureBlobStorageCredentials(connectionString: storage.ConnectionString),
                FolderName = _runId.ToString(),
                BlobDownloadStrategyConfiguration = new BlobDownloadStrategyConfiguration(Strategy: BlobDownloadStrategy.HttpClientDownloadToMemoryMappedFile),
            };

            store = new AzureBlobStorageContentStore(configuration);

            return storage;
        }
    }
}
