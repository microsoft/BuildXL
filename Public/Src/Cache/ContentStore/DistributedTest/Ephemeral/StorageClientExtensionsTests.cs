// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Sas;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.InterfacesTest;
using BuildXL.Cache.ContentStore.InterfacesTest.Results;
using BuildXL.Cache.ContentStore.Tracing;
using ContentStoreTest.Distributed.Redis;
using ContentStoreTest.Test;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using OperationContext = BuildXL.Cache.ContentStore.Tracing.Internal.OperationContext;

namespace BuildXL.Cache.ContentStore.Distributed.Test.Ephemeral
{
    [Collection("Redis-based tests")]
    public class StorageClientExtensionsTests : TestWithOutput
    {
        private readonly LocalRedisFixture _fixture;

        protected Tracer Tracer { get; } = new Tracer(nameof(StorageClientExtensionsTests));

        public StorageClientExtensionsTests(LocalRedisFixture fixture, ITestOutputHelper output)
            : base(output)
        {
            _fixture = fixture;
        }

        public enum CredentialType
        {
            ConnectionString,
            AccountScopedSasToken,
            ContainerScopedSasToken,
        }

        [Theory]
        [InlineData(CredentialType.ConnectionString)]
        [InlineData(CredentialType.AccountScopedSasToken)]
        [InlineData(CredentialType.ContainerScopedSasToken)]
        public Task CreateContainer(CredentialType type)
        {
            return RunTestAsync(async (context, client) =>
            {
                var containerName = "test";

                BlobContainerClient containerClient;
                switch (type)
                {
                    case CredentialType.ConnectionString:
                    {
                        containerClient = client.GetBlobContainerClient(containerName);
                        break;
                    }
                    case CredentialType.AccountScopedSasToken:
                    {
                        var sasBuilder = new AccountSasBuilder(AccountSasPermissions.All, expiresOn: DateTime.UtcNow.AddDays(1), AccountSasServices.Blobs, AccountSasResourceTypes.All);
                        var sasToken = client.GenerateAccountSasUri(sasBuilder);
                        var accountClient = new BlobServiceClient(sasToken);
                        containerClient = accountClient.GetBlobContainerClient(containerName);
                        break;
                    }
                    case CredentialType.ContainerScopedSasToken:
                    {
                        var sasBuilder = new BlobSasBuilder(BlobContainerSasPermissions.List, expiresOn: DateTime.UtcNow.AddDays(1));
                        var delegationClient = client.GetBlobContainerClient(containerName);
                        var sasToken = delegationClient.GenerateSasUri(sasBuilder);
                        containerClient = new BlobContainerClient(sasToken);
                        break;
                    }
                    default:
                        throw new NotImplementedException();
                }

                var exists = await StorageClientExtensions.CheckContainerExistsAsync(Tracer, context, containerClient, Timeout.InfiniteTimeSpan).ThrowIfFailureAsync();
                exists.Should().BeFalse();

                var create = await StorageClientExtensions.EnsureContainerExistsAsync(Tracer, context, containerClient, Timeout.InfiniteTimeSpan).ThrowIfFailureAsync();
                create.Should().BeTrue();

                exists = await StorageClientExtensions.CheckContainerExistsAsync(Tracer, context, containerClient, Timeout.InfiniteTimeSpan).ThrowIfFailureAsync();
                exists.Should().BeTrue();
            });
        }

        public async Task RunTestAsync(Func<OperationContext, BlobServiceClient, Task> action)
        {
            var tracingContext = new Context(TestGlobal.Logger);
            var context = new OperationContext(tracingContext);

            using var process = AzuriteStorageProcess.CreateAndStart(
                _fixture,
                TestGlobal.Logger);

            var client = new BlobServiceClient(process.ConnectionString);

            await action(context, client);
        }
    }
}
