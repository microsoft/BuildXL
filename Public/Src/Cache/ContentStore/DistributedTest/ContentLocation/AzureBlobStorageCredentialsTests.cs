// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using Azure;
using BuildXL.Cache.ContentStore.Interfaces.Auth;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest;
using ContentStoreTest.Distributed.Redis;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using OperationContext = BuildXL.Cache.ContentStore.Tracing.Internal.OperationContext;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Test.ContentLocation;

/// <summary>
/// These tests exist because we utilize these credentials throughout, so it's important to validate that they work
/// properly.
/// </summary>
[Collection("Redis-based tests")]
[Trait("Category", "WindowsOSOnly")] // 'redis-server' executable no longer exists
public class AzureBlobStorageCredentialsTests : TestWithOutput
{
    private readonly LocalRedisFixture _fixture;

    public AzureBlobStorageCredentialsTests(LocalRedisFixture fixture, ITestOutputHelper output)
        : base(output)
    {
        _fixture = fixture;
    }

    [Fact]
    public Task TestConnectionWithPlainTextV12Async()
    {
        return RunTest(async (context, storage, clock) =>
                       {
                           var connectionString = storage.ConnectionString;

                           var ptCreds = new SecretBasedAzureStorageCredentials(connectionString);
                           var ptClient = ptCreds.CreateBlobServiceClient(
                               // The current Azurite version we use supports up to this version
                               new Azure.Storage.Blobs.BlobClientOptions(Azure.Storage.Blobs.BlobClientOptions.ServiceVersion.V2021_02_12));

                           await ptClient
                               .GetBlobContainerClient("test-container")
                               .CreateIfNotExistsAsync(Azure.Storage.Blobs.Models.PublicAccessType.BlobContainer, null, null, context.Token);
                       });
    }

    [Fact(Skip = "The current Azurite version has issues with utilizing SAS tokens, and we can't upgrade because their build process is too complicated")]
    public Task TestConnectionWithSasTokenV12Async()
    {
        // Generates a SAS token with the V12 SDK, and uses it with the V12 SDK
        return RunTest(async (context, storage, clock) =>
                       {
                           var connectionString = @"Replace with a real connection string";

                           var ptCreds = new SecretBasedAzureStorageCredentials(connectionString);
                           var ptSvcClient = ptCreds.CreateBlobServiceClient(
                               // The current Azurite version we use supports up to this version
                               new Azure.Storage.Blobs.BlobClientOptions(Azure.Storage.Blobs.BlobClientOptions.ServiceVersion.V2021_02_12));

                           var accountName = ptSvcClient.AccountName;

                           var badSasUri = ptSvcClient.GenerateAccountSasUri(
                               Azure.Storage.Sas.AccountSasPermissions.Create,
                               expiresOn: clock.UtcNow + TimeSpan.FromDays(1),
                               Azure.Storage.Sas.AccountSasResourceTypes.All);

                           // WARNING: this uses the Query part of the URL only
                           var updatingSasToken = new UpdatingSasToken(new SasToken(badSasUri.Query.ToString(), accountName));
                           var sasCreds = new SecretBasedAzureStorageCredentials(updatingSasToken);
                           var sasSvcClient = sasCreds.CreateBlobServiceClient(
                               // The current Azurite version we use supports up to this version
                               new Azure.Storage.Blobs.BlobClientOptions(Azure.Storage.Blobs.BlobClientOptions.ServiceVersion.V2021_02_12));

                           bool threw = false;
                           try
                           {
                               await sasSvcClient.GetBlobContainerClient("test").GetBlobClient("test").ExistsAsync();
                           }
                           catch (RequestFailedException e)
                           {
                               threw = true;
                               e.Status.Should().Be(403);
                           }
                           threw.Should().BeTrue();

                           // Update the token, this would usually be done by the secret store.
                           var goodSasUri = ptSvcClient.GenerateAccountSasUri(
                               Azure.Storage.Sas.AccountSasPermissions.All,
                               expiresOn: clock.UtcNow + TimeSpan.FromDays(1),
                               Azure.Storage.Sas.AccountSasResourceTypes.All);
                           updatingSasToken.UpdateToken(new SasToken(goodSasUri.Query.ToString(), accountName));

                           // Attempt a get of an inexistent file. It should fail due to it not existing.
                           var exists = await sasSvcClient.GetBlobContainerClient("test").GetBlobClient("test").ExistsAsync();
                           exists.Value.Should().BeFalse();
                       });
    }

    private async Task RunTest(Func<OperationContext, AzuriteStorageProcess, IClock, Task> runTest, IClock? clock = null)
    {
        clock ??= SystemClock.Instance;

        var tracingContext = new Context(TestGlobal.Logger);
        var context = new OperationContext(tracingContext);

        using var storage = AzuriteStorageProcess.CreateAndStartEmpty(_fixture, TestGlobal.Logger);
        await runTest(context, storage, clock);
    }
}
