// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Auth;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Stores;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.InterfacesTest.Sessions;
using BuildXL.Cache.ContentStore.Sessions.Internal;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.UtilitiesCore;
using BuildXL.Cache.ContentStore.Utils;
using ContentStoreTest.Distributed.Redis;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Test;

[Collection("Redis-based tests")]
[Trait("Category", "WindowsOSOnly")] // 'redis-server' executable no longer exists
public class AzureBlobStorageContentSessionTests : ContentSessionTests
{
    private readonly string _runId = ThreadSafeRandom.LowercaseAlphanumeric(10);
    private readonly LocalRedisFixture _fixture;

    protected virtual bool UsePreauthenticatedUris => false;

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

    protected override async Task RunReadOnlyTestAsync(ImplicitPin implicitPin, Func<Context, IContentSession, Task> funcAsync)
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
        return RunTestAsync(
            ImplicitPin.None,
            null,
            async (context, session) =>
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
    public Task TouchEmptyBlob()
    {
        // By putting an empty file twice, we ensure we're triggering the Touch logic.
        return RunTestAsync(
            ImplicitPin.None,
            null,
            async (context, session) =>
            {
                var stream = new MemoryStream(0);
                await session.PutStreamAsync(context, HashType.Vso0, stream, CancellationToken.None).ShouldBeSuccess();
                await session.PutStreamAsync(context, HashType.Vso0, stream, CancellationToken.None).ShouldBeSuccess();
            });
    }

    [Fact]
    public Task RepeatedBulkPinShouldSucceedAsync()
    {
        return RunTestAsync(
            ImplicitPin.None,
            null,
            async (context, session) =>
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


    [Fact]
    public Task PutBigFileShouldSucceed()
    {
        return RunTestAsync(
            ImplicitPin.None,
            null,
            async (context, session) =>
            {
                var putResult = await session.PutRandomFileAsync(context, FileSystem, HashType.Vso0, provideHash: false, size: "100 MB".ToSize(), default);
                putResult.ShouldBeSuccess();

                using var placeDirectory = new DisposableDirectory(FileSystem);
                var placeResult = await session.PlaceFileAsync(context, putResult.ContentHash, placeDirectory.Path / "bigfile.dat", FileAccessMode.ReadOnly, FileReplacementMode.FailIfExists, FileRealizationMode.Any, Token);
                placeResult.ShouldBeSuccess();
            });
    }

    [Fact]
    public Task PutAndPlaceLotsOfRandomFilesShouldSucceed()
    {
        return RunTestAsync(
            ImplicitPin.None,
            null,
            async (context, session) =>
            {
                const int FileCount = 50;
                var contentHashes = await session.PutRandomAsync(context, ContentHashType, false, FileCount, ContentByteCount, true);

                using var placeDirectory = new DisposableDirectory(FileSystem);
                var hashes = contentHashes.Select(contentHash => new ContentHashWithPath(
                                                      contentHash,
                                                      placeDirectory.Path / contentHash.ToHex())).ToList();
                var results = await Task.WhenAll(await session.PlaceFileAsync(context, hashes, FileAccessMode.ReadOnly, FileReplacementMode.FailIfExists, FileRealizationMode.Any, Token));
                foreach (var result in results)
                {
                    result.Item.Code.Should().Be(PlaceFileResult.ResultCode.PlacedWithCopy);
                    result.Item.ShouldBeSuccess();
                }
            });
    }

    [Fact]
    public Task PutAndOpenStreamLotsOfRandomFilesShouldSucceed()
    {
        // This is just testing that pinning files repeatedly doesn't have any side effects
        return RunTestAsync(
            ImplicitPin.None,
            null,
            async (context, session) =>
            {
                const int FileCount = 50;
                var contentHashes = await session.PutRandomAsync(context, ContentHashType, false, FileCount, ContentByteCount, true);

                using var placeDirectory = new DisposableDirectory(FileSystem);
                var tasks = contentHashes.Select(contentHash => session.OpenStreamAsync(context, contentHash, Token)).ToList();
                foreach (var result in await Task.WhenAll(tasks))
                {
                    result.Code.Should().Be(OpenStreamResult.ResultCode.Success);
                    result.Stream.Should().NotBeNull();
                    result.ShouldBeSuccess();
                    result.Stream!.Dispose();
                }
            });
    }

    [Fact(Skip = "Used for manual testing of whether the bulk pin logic updates the last access time correctly in Storage")]
    public Task PinSpecificFile()
    {
        OverrideFolderName = "pinSpecificFile";
        return RunTestAsync(
            ImplicitPin.None,
            null,
            async (context, session) =>
            {
                var putResult = await session.PutContentAsync(context, $"hello").ThrowIfFailureAsync();
                var pinResult = await session.PinAsync(context, putResult.ContentHash, Token);
                pinResult.ShouldBeSuccess();

                var pinResults = (await session.PinAsync(context, new[] { putResult.ContentHash }, Token)).ToList();
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
            await RunTestAsync(
                ImplicitPin.None,
                null,
                async (context, session) =>
                {
                    var putResult = await session.PutRandomAsync(
                        context,
                        ContentHashType,
                        false,
                        "100MB".ToSize(),
                        Token).ShouldBeSuccess();
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
                        (await HashInfoLookup.GetContentHasher(ContentHashType).GetContentHashAsync(fs)).Should()
                            .BeEquivalentTo(putResult.ContentHash);
                    }
                });
        }
    }

    [Fact]
    public async Task DoesntDownloadMismatchingHashes()
    {
        // This test downloads a file in parallel, hence why we check
        using var placeDirectory = new DisposableDirectory(FileSystem);
        var path = placeDirectory.Path / "file.dat";
        await RunTestAsync(
            ImplicitPin.None,
            null,
            async (context, sess) =>
            {
                var session = (sess as ITrustedContentSession)!;

                // Generate some content
                var putResult = await session.PutContentAsync(context, "hello").ThrowIfFailureAsync();
                await session.PlaceFileAsync(
                    context,
                    putResult.ContentHash,
                    path,
                    FileAccessMode.ReadOnly,
                    FileReplacementMode.ReplaceExisting,
                    FileRealizationMode.Any,
                    CancellationToken.None).ThrowIfFailureAsync();

                // Perform a trusted put with invalid hash.
                var invalidHash = ContentHash.Random();
                await session.PutTrustedFileAsync(
                    context,
                    new ContentHashWithSize(invalidHash, 5),
                    path,
                    FileRealizationMode.Any,
                    CancellationToken.None,
                    UrgencyHint.Nominal).ThrowIfFailureAsync();

                // Placing the invalid hash should fail.
                var result = await session.PlaceFileAsync(
                    context,
                    invalidHash,
                    path,
                    FileAccessMode.ReadOnly,
                    FileReplacementMode.ReplaceExisting,
                    FileRealizationMode.Any,
                    Token);

                result.Code.Should().Be(PlaceFileResult.ResultCode.NotPlacedContentNotFound);
            });
    }

    internal string? OverrideFolderName { get; set; }

    private IDisposable CreateBlobContentStore(out AzureBlobStorageContentStore store)
    {
        var shards = Enumerable.Range(0, 10)
            .Select(shard => (BlobCacheStorageAccountName)new BlobCacheStorageShardingAccountName("0123456789", shard, "testing")).ToList();

        // Force it to use a non-sharding account
        shards.Add(new BlobCacheStorageNonShardingAccountName("devstoreaccount1"));

        var (process, secretsProvider) = CreateTestTopology(_fixture, shards, UsePreauthenticatedUris);

        var configuration = new AzureBlobStorageContentStoreConfiguration()
        {
            Topology = new ShardedBlobCacheTopology(
                                    new ShardedBlobCacheTopology.Configuration(
                                        new ShardingScheme(ShardingAlgorithm.JumpHash, shards),
                                        SecretsProvider: secretsProvider,
                                        Universe: OverrideFolderName ?? _runId,
                                        Namespace: "default",
                                        BlobRetryPolicy: new ShardedBlobCacheTopology.BlobRetryPolicy())),
        };

        store = new AzureBlobStorageContentStore(configuration);

        return process;
    }

    public static (AzuriteStorageProcess Process, IBlobCacheSecretsProvider secretsProvider) CreateTestTopology(LocalRedisFixture fixture, IReadOnlyList<BlobCacheStorageAccountName> accounts, bool usePreauthenticatedUris = false)
    {
        var process = AzuriteStorageProcess.CreateAndStart(
            fixture,
            TestGlobal.Logger,
            accounts: accounts.Select(account => account.AccountName).ToList());

        var credentials = accounts.Select(
            account =>
            {
                var connectionString = process.ConnectionString.Replace("devstoreaccount1", account.AccountName);

                IAzureStorageCredentials credentials;
                if (usePreauthenticatedUris)
                {
                    var client = new BlobServiceClient(connectionString);

                    var uri = client.GenerateAccountSasUri(
                        AccountSasPermissions.All,
                        DateTimeOffset.UtcNow.AddDays(1),
                        AccountSasResourceTypes.All);

                    credentials = new PreauthenticatedUriStorageCredentials(uri);
                }
                else
                {
                    credentials = new SecretBasedAzureStorageCredentials(connectionString);
                }

                Contract.Assert(credentials.GetAccountName() == account.AccountName);
                return (Account: account, Credentials: credentials);
            }).ToDictionary(kvp => kvp.Account, kvp => kvp.Credentials);

        var secretsProvider = new StaticBlobCacheSecretsProvider(credentials);
        return (process, secretsProvider);
    }
}

public class AzureBlobStorageContentSessionSasUriTests : AzureBlobStorageContentSessionTests
{
    protected override bool UsePreauthenticatedUris => true;

    public AzureBlobStorageContentSessionSasUriTests(LocalRedisFixture fixture, ITestOutputHelper output)
        : base(fixture, output)
    {
    }
}
