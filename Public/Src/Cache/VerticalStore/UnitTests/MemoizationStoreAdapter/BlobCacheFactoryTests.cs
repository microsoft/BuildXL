// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using BuildXL.Cache.ContentStore.Interfaces.Auth;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest;
using BuildXL.Cache.MemoizationStoreAdapter;
using ContentStoreTest.Distributed.Redis;
using ContentStoreTest.Test;
using Xunit;
using Xunit.Abstractions;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.Test.ContentLocation;

/// <summary>
/// These tests exist because we utilize these credentials throughout, so it's important to validate that they work
/// properly.
/// </summary>
[Collection("Redis-based tests")]
[Trait("Category", "WindowsOSOnly")] // 'redis-server' executable no longer exists
public class BlobCacheFactoryTests(LocalRedisFixture fixture, ITestOutputHelper output) : TestBase(TestGlobal.Logger, output)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task TestBlobFromConnectionString(bool useSas)
    {
        return RunTest(async (storage, clock) =>
        {
            var connectionString = storage.ConnectionString;

            var client = new BlobServiceClient(connectionString);
            var container = client.GetBlobContainerClient("cachelogscontainer");
            await container.CreateIfNotExistsAsync();

            if (useSas)
            {
                connectionString = container.GenerateSasUri(
                    Azure.Storage.Sas.BlobContainerSasPermissions.All,
                    DateTimeOffset.UtcNow + TimeSpan.FromHours(10)).ToString();
            }

            var logpath = TestRootDirectoryPath / Path.GetTempFileName() + ".log";
            var config = new BlobCacheConfig()
            {
                LogToKusto = true,
                LogToKustoConnectionStringFileEnvironmentVariableName = $"CacheLogConnectionStringFile{Guid.NewGuid():N}",
                ConnectionStringFileDataProtectionEncrypted = false,
                CacheLogPath = logpath
            };

            var path = TestRootDirectoryPath / Path.GetTempFileName();

            File.WriteAllText(path.Path, connectionString);

            Environment.SetEnvironmentVariable(config.LogToKustoConnectionStringFileEnvironmentVariableName, path.Path);

            var result = await BlobCacheFactoryBase<BlobCacheConfig>.CreateLoggerAsync(config, TestGlobal.Logger);

            result.logger.Debug("Hello world");

            result.logger.Dispose();
        });
    }


    private async Task RunTest(Func<AzuriteStorageProcess, IClock, Task> runTest, IClock? clock = null)
    {
        clock ??= SystemClock.Instance;

        var tracingContext = new Context(TestGlobal.Logger);
        using var storage = AzuriteStorageProcess.CreateAndStartEmpty(fixture, TestGlobal.Logger);
        await runTest(storage, clock);
    }
}